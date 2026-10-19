using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Libsyn;

public sealed class LibsynProvider : IMediaProvider
{
    private const string Api = "https://html5-player.libsyn.com/api/";
    public MediaProviderInfo Info { get; } = new("libsyn", "Libsyn public episodes hosted pages and complete player destinations", 200);
    public bool CanHandle(Uri uri) => LibsynAddress.Matches(uri);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) { var address = LibsynAddress.Parse(request.Url); return !address.Destination && address.Id is { } id ? new("libsyn", id) : null; }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = LibsynAddress.Parse(request.Url); using var anonymous = http.CreateAnonymousSession(); PageMetadata? web = null;
        if (address.Page is { } page)
        {
            var document = await anonymous.GetDocumentAsync(page, 8 * 1024 * 1024, ct).ConfigureAwait(false);
            if (document.Url.Host != page.Host || document.Url.AbsolutePath.TrimEnd('/') != page.AbsolutePath.TrimEnd('/')) throw new InvalidDataException("Libsyn redirected to another hosted episode page.");
            var clean = Regex.Replace(document.Text, @"<!--.*?-->|<(?:script|template|textarea)\b[^>]*>.*?</(?:script|template|textarea)\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromMilliseconds(500));
            var players = new List<LibsynAddress>();
            foreach (Match tag in Regex.Matches(clean, @"<(?:iframe|a)\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500)))
            {
                var attributes = Providers.AudioBoom.AudioBoomHtml.Attributes(tag.Groups["attrs"].Value);
                var raw = attributes.GetValueOrDefault("src") ?? attributes.GetValueOrDefault("href"); if (raw is null || !Uri.TryCreate(page, raw, out var uri) || uri.Host is not ("play.libsyn.com" or "html5-player.libsyn.com")) continue;
                var player = LibsynAddress.Parse(uri); if (!player.Destination) players.Add(player);
            }
            var ids = players.Select(player => player.Id).Distinct(StringComparer.Ordinal).ToArray(); if (ids.Length != 1) throw new NotSupportedException("Libsyn did not expose one identified public episode player on this page.");
            address = players.First() with { ShowId = address.ShowId }; web = PageMetadataReader.Read(clean, page);
        }
        if (!address.Destination)
        {
            using var document = await JsonAsync(new(Api + "episode/id/" + address.Id), anonymous, ct).ConfigureAwait(false);
            return Episode(document.RootElement, address.Id!, address.ShowId, address.TrackingDestination, web);
        }
        var entries = new List<NativePlaylistEntry>(); var idsSeen = new HashSet<string>(StringComparer.Ordinal); string? showId = null; PageMetadata? metadata = null; string[]? firstIds = null; var pageNumber = 1;
        var baseUrl = Api + "destination/id/" + address.Id + "/size/100/sort_by_direction/" + (address.OldestFirst ? "asc" : "desc");
        while (true)
        {
            ct.ThrowIfCancellationRequested(); using var document = await JsonAsync(new(baseUrl + "?page=" + pageNumber.ToString(CultureInfo.InvariantCulture)), anonymous, ct).ConfigureAwait(false); var root = document.RootElement;
            var show = Destination(root, address.Id!); var currentShow = Id(show, "show_id"); if (showId is not null && currentShow != showId) throw new InvalidDataException("Libsyn changed destination ownership during pagination."); showId = currentShow;
            var items = Items(root); var currentIds = items.Select(item => Id(item, "item_id")).ToArray(); firstIds ??= currentIds;
            foreach (var item in items)
            {
                var identity = Item(item, null, showId); var id = identity.Id; if (!idsSeen.Add(id)) throw new InvalidDataException("Libsyn repeated an episode during destination pagination.");
                if (entries.Count >= Math.Min(10000, request.MaximumPlaylistEntries)) throw new NotSupportedException("The complete Libsyn destination exceeds the configured inventory limit.");
                var body = Object(item, "_item"); var content = Object(body, "_primary_content"); var available = Text(body, "premium_state") == "auto" && Text(content, "_url") is { Length: > 0 } && Text(Object(content, "_format"), "file_class") is "audio" or "video";
                entries.Add(new(entries.Count + 1, id, Text(body, "item_title") ?? id, available ? LibsynAddress.Episode(id, address.Id) : null, Duration(content), Poster(Text(body, "_image_url")), available, ExpectedArchiveIdentity: new("libsyn", id)));
            }
            metadata ??= new() { Id = address.Id, PlaylistId = address.Id, Extractor = "libsyn:destination", IsPlaylist = true, Title = Text(show, "show_title") ?? address.Id!, Uploader = Text(show, "show_title"), ThumbnailUrl = Poster(Text(show, "_image_url")), SourcePageUrl = request.Url };
            if (items.Length < 100)
            {
                using var anchor = await JsonAsync(new(baseUrl + "?page=1"), anonymous, ct).ConfigureAwait(false); var returned = Destination(anchor.RootElement, address.Id!);
                if (Id(returned, "show_id") != showId || !Items(anchor.RootElement).Select(item => Id(item, "item_id")).SequenceEqual(firstIds, StringComparer.Ordinal)) throw new InvalidDataException("Libsyn changed its destination's initial inventory during pagination.");
                return metadata with { Entries = entries };
            }
            if (++pageNumber > 10001) throw new InvalidDataException("Libsyn did not terminate its public destination inventory.");
        }
    }
    private static JsonElement Destination(JsonElement root, string expected)
    {
        var destination = Object(root, "_destination"); if (Id(root, "destination_id") != expected || Id(destination, "destination_id") != expected) throw new InvalidDataException("Libsyn returned another destination identity.");
        var show = Object(destination, "_show"); ValidateShow(show); return show;
    }
    private static JsonElement[] Items(JsonElement root)
    { if (!root.TryGetProperty("_items", out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 100) throw new InvalidDataException("Libsyn omitted or oversized its destination episode page."); return array.EnumerateArray().ToArray(); }
    private static (string Id, string Show) Item(JsonElement root, string? expected, string? showId)
    {
        var body = Object(root, "_item"); var id = Id(root, "item_id"); var show = Object(body, "_show"); var owner = Id(body, "show_id");
        if (Id(body, "item_id") != id || expected is not null && id != expected || Id(show, "show_id") != owner || showId is not null && owner != showId) throw new InvalidDataException("Libsyn returned mismatched episode or show identities.");
        ValidateShow(show, false); return (id, owner);
    }
    private static void ValidateShow(JsonElement show, bool requireFeed = true)
    {
        var id = Id(show, "show_id"); var raw = Text(show, "feed_url");
        if (string.IsNullOrEmpty(raw) && !requireFeed) return;
        var feed = PublicUrl(raw);
        if (feed.Host != "feeds.libsyn.com" || feed.AbsolutePath.TrimEnd('/') != "/" + id + "/rss") throw new InvalidDataException("Libsyn's feed URL disagrees with its show identity.");
    }
    private static PageMetadata Episode(JsonElement root, string expected, string? showId, string? tracking, PageMetadata? web)
    {
        var identity = Item(root, expected, showId); var body = Object(root, "_item"); var show = Object(body, "_show"); var content = Object(body, "_primary_content");
        if (Text(body, "premium_state") != "auto") throw new NotSupportedException("This Libsyn episode needs additional premium/access-state support.");
        var metadata = new PageMetadata { Id = expected, Extractor = "libsyn", ArchiveIdentity = new("libsyn", expected), Title = Text(body, "item_title") ?? expected, Description = Text(body, "item_body"), Uploader = Text(show, "show_title"),
            ThumbnailUrl = Poster(Text(body, "_image_url") ?? web?.ThumbnailUrl), DurationSeconds = Duration(content), SourcePageUrl = web?.SourcePageUrl ?? LibsynAddress.Episode(expected), AnonymousPlayback = true };
        if (Text(content, "_url") is not { Length: > 0 } supplied) return metadata with { Warnings = ["This Libsyn episode has no available public media."] };
        _ = Id(content, "content_id"); var source = Media(supplied); var duration = Duration(content); if (duration is not > 0 || duration > 604800) throw new InvalidDataException("Libsyn omitted a supported complete program duration.");
        var audio = Text(Object(content, "_format"), "file_class") == "audio"; var video = Text(Object(content, "_format"), "file_class") == "video"; var extension = Path.GetExtension(source.AbsolutePath).TrimStart('.').ToLowerInvariant();
        if (!(audio && extension is "mp3" or "m4a" || video && extension is "mp4" or "m4v" or "mov")) throw new NotSupportedException("This Libsyn media class or container needs additional native support.");
        if (tracking is not null) source = new(source.AbsoluteUri + (source.Query.Length == 0 ? "?" : "&") + "dest-id=" + tracking);
        return metadata with { Media = [new() { Id = "primary", Url = source, Kind = MediaKind.Progressive, Source = "libsyn", Extension = extension, MimeType = audio ? extension == "mp3" ? "audio/mpeg" : "audio/mp4" : "video/mp4", HasAudio = audio ? true : null, HasVideo = video, AudioCodec = audio && extension == "mp3" ? "mp3" : null, IsCompleteMedia = true,
            VerificationDemuxer = extension == "mp3" ? "mp3" : "mov", VerificationDurationSeconds = video ? duration : null, VerificationMinimumDurationSeconds = audio ? duration : null }] };
    }
    private static double? Duration(JsonElement content) => Number(content, "duration") ?? ProviderJson.Duration(Text(content, "duration"));
    private static string Id(JsonElement root, string key) { var id = Long(root, key)?.ToString(CultureInfo.InvariantCulture); return id is not null && LibsynAddress.Numeric(id) ? id : throw new InvalidDataException("Libsyn omitted a valid stable numeric identity."); }
    private static Uri PublicUrl(string? raw) { if (raw?.StartsWith("//", StringComparison.Ordinal) == true) raw = "https:" + raw; return Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 ? uri : throw new InvalidDataException("Libsyn returned an invalid public URL."); }
    private static Uri Media(string raw)
    {
        var source = PublicUrl(raw); var resource = source;
        for (var depth = 0; depth < 5; depth++)
        {
            if (resource.Host is "traffic.libsyn.com" or "media.libsyn.com") return source;
            var prefix = resource.Host == "pscrb.fm" ? "/rss/p/" : resource.Host == "dts.podtrac.com" ? "/redirect.mp3/" : null;
            if (prefix is null || !resource.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) break;
            resource = PublicUrl("https://" + resource.AbsolutePath[prefix.Length..]);
        }
        throw new NotSupportedException("This Libsyn media origin or tracking prefix needs additional native support.");
    }
    private static string? Poster(string? raw) { if (raw is null) return null; var uri = PublicUrl(raw); if (uri.Host != "assets.libsyn.com") throw new NotSupportedException("Libsyn returned an unsupported artwork origin."); return uri.AbsoluteUri; }
    private static async Task<JsonDocument> JsonAsync(Uri endpoint, MediaHttp http, CancellationToken ct)
    {
        var response = await http.GetDocumentAsync(endpoint, 8 * 1024 * 1024, ct).ConfigureAwait(false); if (response.Url != endpoint) throw new InvalidDataException("Libsyn redirected its public player metadata.");
        JsonDocument document; try { document = JsonDocument.Parse(response.Text, new() { MaxDepth = 64 }); } catch (JsonException) { throw new InvalidDataException("Libsyn returned malformed player metadata."); }
        try
        {
            var stack = new Stack<JsonElement>(); stack.Push(document.RootElement); var count = 0;
            while (stack.TryPop(out var item))
            {
                if (++count > 200000) throw new InvalidDataException("Libsyn returned excessive player metadata.");
                if (item.ValueKind == JsonValueKind.Object) { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var field in item.EnumerateObject()) { if (!names.Add(field.Name)) throw new InvalidDataException("Libsyn returned repeated metadata fields."); stack.Push(field.Value); } }
                else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) stack.Push(child);
            }
            return document;
        }
        catch { document.Dispose(); throw; }
    }
}
