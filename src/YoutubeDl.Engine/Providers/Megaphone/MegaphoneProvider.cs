using System.Globalization;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Megaphone;

public sealed class MegaphoneProvider : IMediaProvider
{
    private const string Api = "https://player.megaphone.fm/playlist/";
    public MediaProviderInfo Info { get; } = new("megaphone", "Megaphone public episode podcast and channel players", 200);
    public bool CanHandle(Uri uri) => MegaphoneAddress.Matches(uri);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => MegaphoneAddress.Parse(request.Url).EpisodeId is { } id ? new("megaphone", id) : null;
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = MegaphoneAddress.Parse(request.Url); using var anonymous = http.CreateAnonymousSession();
        if (address.Podcast is null)
        {
            using var document = await JsonAsync(new(Api + "episode/" + address.EpisodeId), anonymous, ct).ConfigureAwait(false); var root = document.RootElement; var items = Items(root);
            _ = Feed(root); if (Integer(root, "totalCount") != 1 || Integer(root, "page") != 1 || Integer(root, "limit") != 1 || items.Length != 1 || Id(items[0]) != address.EpisodeId) throw new InvalidDataException("Megaphone returned another or incomplete requested episode.");
            return Episode(items[0], root);
        }
        var entries = new List<NativePlaylistEntry>(); var seen = new HashSet<string>(StringComparer.Ordinal); int? count = null; int? limit = null; string? owner = null; PageMetadata? metadata = null; PageMetadata? selected = null; var pageNumber = 1;
        while (true)
        {
            ct.ThrowIfCancellationRequested(); var endpoint = new Uri(Api + Uri.EscapeDataString(address.Podcast) + "?page=" + pageNumber.ToString(CultureInfo.InvariantCulture) + (address.LatestEpisodes is { } latest ? "&episodes=" + latest.ToString(CultureInfo.InvariantCulture) : ""));
            using var document = await JsonAsync(endpoint, anonymous, ct).ConfigureAwait(false); var root = document.RootElement; var feed = Feed(root); var key = feed.AbsoluteUri;
            if (!MegaphoneAddress.Uid(address.Podcast) && feed.AbsolutePath.Trim('/') != address.Podcast) throw new InvalidDataException("Megaphone returned another named podcast identity.");
            if (owner is not null && owner != key) throw new InvalidDataException("Megaphone changed podcast ownership during pagination."); owner = key;
            var total = Integer(root, "totalCount") ?? throw new InvalidDataException("Megaphone omitted its declared podcast count."); var pageLimit = Integer(root, "limit");
            if (pageLimit is not > 0 or > 1000 || Integer(root, "page") != pageNumber || count is not null && count != total || limit is not null && limit != pageLimit || address.LatestEpisodes is { } maximum && total > maximum) throw new InvalidDataException("Megaphone changed its public count page or latest-episode scope."); count = total; limit = pageLimit;
            if (total > Math.Min(10000, request.MaximumPlaylistEntries)) throw new NotSupportedException("The complete Megaphone inventory exceeds the configured limit.");
            var items = Items(root); if (items.Length > pageLimit || entries.Count + items.Length > total) throw new InvalidDataException("Megaphone returned an oversized or conflicting podcast page.");
            foreach (var item in items)
            {
                var id = Id(item); if (!seen.Add(id)) throw new InvalidDataException("Megaphone repeated an episode during pagination.");
                var available = Text(item, "audioUrl") is { Length: > 0 }; var info = available ? Episode(item, root) : null;
                entries.Add(new(entries.Count + 1, id, Text(item, "title") ?? id, available ? MegaphoneAddress.Episode(id) : null, Number(item, "duration"), info?.ThumbnailUrl, available, ExpectedArchiveIdentity: new("megaphone", id)));
                if (id == address.EpisodeId) selected = info;
            }
            metadata ??= new() { Id = feed.AbsolutePath.Trim('/'), PlaylistId = feed.AbsolutePath.Trim('/'), Extractor = "megaphone.fm:podcast", IsPlaylist = true, Title = Text(root, "podcastTitle") ?? address.Podcast, Uploader = Text(root, "podcastTitle"), ThumbnailUrl = entries.FirstOrDefault()?.ThumbnailUrl, SourcePageUrl = request.Url };
            if (entries.Count == total)
            {
                if (address.EpisodeId is not null) return selected ?? throw new NotSupportedException("The requested Megaphone channel episode is unavailable or outside its public inventory.");
                return metadata with { Entries = entries };
            }
            if (items.Length != pageLimit) throw new InvalidDataException("Megaphone ended its public inventory before the declared episode count.");
            pageNumber++;
        }
    }
    private static PageMetadata Episode(JsonElement item, JsonElement podcast)
    {
        var id = Id(item); var title = Text(item, "title") ?? id; var raw = Text(item, "audioUrl"); var poster = Poster(Text(item, "imageUrl"));
        var result = new PageMetadata { Id = id, Extractor = "megaphone.fm", ArchiveIdentity = new("megaphone", id), Title = title, Description = Text(item, "summary"), Uploader = Text(podcast, "podcastTitle"), ThumbnailUrl = poster,
            DurationSeconds = Number(item, "duration"), SourcePageUrl = MegaphoneAddress.Episode(id), AnonymousPlayback = true };
        if (string.IsNullOrWhiteSpace(raw)) return result with { Warnings = ["This Megaphone episode has no available public audio."] };
        var duration = Number(item, "duration"); if (duration is not > 0 || duration > 604800) throw new InvalidDataException("Megaphone omitted a supported full program duration.");
        var source = Media(raw, id);
        if (Text(item, "episodeUrlHRef") is { Length: > 0 } direct) _ = Media(direct, id);
        if (Text(item, "dataClipboardText") is { Length: > 0 } link && (MegaphoneAddress.Parse(PublicUrl(link)).EpisodeId != id)) throw new InvalidDataException("Megaphone's share link conflicts with its episode identity.");
        return result with { Media = [new() { Id = "primary", Url = source, Kind = MediaKind.Progressive, Source = "megaphone", Extension = "mp3", MimeType = "audio/mpeg", HasAudio = true, HasVideo = false, AudioCodec = "mp3", IsCompleteMedia = true,
            VerificationDemuxer = "mp3", VerificationMinimumDurationSeconds = duration }] };
    }
    private static JsonElement[] Items(JsonElement root) { if (!root.TryGetProperty("episodes", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 1000) throw new InvalidDataException("Megaphone omitted or oversized its episode page."); return items.EnumerateArray().ToArray(); }
    private static Uri Feed(JsonElement root) { var uri = PublicUrl(Text(root, "podcastFeedUrl")); if (uri.Host != "feeds.megaphone.fm" || uri.Query.Length != 0 || !MegaphoneAddress.Name(uri.AbsolutePath.Trim('/'))) throw new InvalidDataException("Megaphone returned an unsupported public podcast feed identity."); return new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri; }
    private static string Id(JsonElement item) { var id = Text(item, "uid"); return id is not null && MegaphoneAddress.Uid(id) ? id.ToUpperInvariant() : throw new InvalidDataException("Megaphone omitted a valid stable episode UID."); }
    private static Uri PublicUrl(string? raw) => Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 ? uri : throw new InvalidDataException("Megaphone returned an invalid public URL.");
    private static Uri Media(string raw, string id)
    {
        var source = PublicUrl(raw); var current = source;
        for (var depth = 0; depth < 5; depth++)
        {
            if (current.Host == "traffic.megaphone.fm") { if (current.AbsolutePath != "/" + id + ".mp3") throw new InvalidDataException("Megaphone's supplied media filename conflicts with its episode UID."); return source; }
            var prefix = current.Host == "pscrb.fm" ? "/rss/p/" : current.Host == "dts.podtrac.com" ? "/redirect.mp3/" : null;
            if (current.Host == "chrt.fm") { var parts = current.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries); if (parts.Length < 4 || parts[0] != "track" || !MegaphoneAddress.Name(parts[1])) break; prefix = "/track/" + parts[1] + "/"; }
            if (prefix is null || !current.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) break;
            current = PublicUrl("https://" + current.AbsolutePath[prefix.Length..]);
        }
        throw new NotSupportedException("This Megaphone media origin or tracking prefix needs additional support.");
    }
    private static string? Poster(string? raw) { if (string.IsNullOrWhiteSpace(raw)) return null; var uri = PublicUrl(raw); if (uri.Host is not ("megaphone.imgix.net" or "assets.megaphone.fm")) throw new NotSupportedException("Megaphone returned an unsupported artwork origin."); return uri.AbsoluteUri; }
    private static async Task<JsonDocument> JsonAsync(Uri endpoint, MediaHttp http, CancellationToken ct)
    {
        var response = await http.GetDocumentAsync(endpoint, 8 * 1024 * 1024, ct).ConfigureAwait(false); if (response.Url != endpoint) throw new InvalidDataException("Megaphone redirected a public metadata identity.");
        JsonDocument document; try { document = JsonDocument.Parse(response.Text, new() { MaxDepth = 64 }); } catch (JsonException) { throw new InvalidDataException("Megaphone returned malformed public metadata."); }
        try
        {
            var stack = new Stack<JsonElement>(); stack.Push(document.RootElement); var count = 0;
            while (stack.TryPop(out var item))
            {
                if (++count > 200000) throw new InvalidDataException("Megaphone returned excessive metadata.");
                if (item.ValueKind == JsonValueKind.Object) { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var field in item.EnumerateObject()) { if (!names.Add(field.Name)) throw new InvalidDataException("Megaphone returned repeated metadata fields."); stack.Push(field.Value); } }
                else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) stack.Push(child);
            }
            return document;
        }
        catch { document.Dispose(); throw; }
    }
}
