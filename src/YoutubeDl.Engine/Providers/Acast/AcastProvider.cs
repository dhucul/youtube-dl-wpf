using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Acast;

public sealed class AcastProvider : IMediaProvider
{
    private const string Api = "https://phoenix.prod.ateam.acast.cloud/api/v1/shows/";
    public MediaProviderInfo Info { get; } = new("acast", "Acast public podcast episodes embeds and complete shows", 200);
    public bool CanHandle(Uri uri) => AcastAddress.Matches(uri);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => AcastAddress.Parse(request.Url).Episode is { } id && AcastAddress.Mongo(id) ? new("acast", id) : null;
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = AcastAddress.Parse(request.Url); using var anonymous = http.CreateAnonymousSession();
        if (address.Episode is { } episode)
        {
            var endpoint = new Uri(Api + Uri.EscapeDataString(address.Show) + "/episodes/" + Uri.EscapeDataString(episode) + "?showInfo=true");
            using var document = await JsonAsync(endpoint, anonymous, ct).ConfigureAwait(false); var root = document.RootElement;
            var show = Object(root, "show"); var identity = Show(show, address.Show); var id = EpisodeId(root, identity.Id);
            if (AcastAddress.Mongo(episode) ? id != episode : Text(root, "episodeUrl") != episode) throw new InvalidDataException("Acast returned another requested episode identity.");
            return Episode(root, show, identity.Alias, id);
        }
        var entries = new List<NativePlaylistEntry>(); var ids = new HashSet<string>(StringComparer.Ordinal); string? showId = null; string? alias = null; int? count = null; string? revision = null; PageMetadata? metadata = null;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var endpoint = new Uri(Api + Uri.EscapeDataString(showId ?? address.Show) + "?limit=100&from=" + entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var response = await JsonAsync(endpoint, anonymous, ct).ConfigureAwait(false); var root = response.RootElement; var identity = Show(root, showId ?? address.Show);
            if (showId is not null && (identity.Id != showId || identity.Alias != alias)) throw new InvalidDataException("Acast changed show identity during pagination.");
            showId = identity.Id; alias = identity.Alias;
            var total = Integer(root, "count") ?? throw new InvalidDataException("Acast omitted its declared show inventory count.");
            if (total > request.MaximumPlaylistEntries || total > 10000) throw new NotSupportedException("The Acast show exceeds the configured complete-inventory limit.");
            if (count is not null && count != total) throw new InvalidDataException("Acast changed its episode count during pagination."); count = total;
            var checksum = Text(root, "checkSum"); if (revision is not null && checksum is not null && revision != checksum) throw new InvalidDataException("Acast changed its show revision during pagination."); revision ??= checksum;
            if (!root.TryGetProperty("episodes", out var supplied) || supplied.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Acast omitted a show episode page.");
            var page = supplied.EnumerateArray().ToArray(); if (page.Length > 100 || entries.Count + page.Length > total) throw new InvalidDataException("Acast returned an oversized or conflicting show page.");
            foreach (var item in page)
            {
                var id = EpisodeId(item, showId); if (!ids.Add(id)) throw new InvalidDataException("Acast repeated an episode identity during pagination.");
                var available = !Premium(item); var slug = Text(item, "episodeUrl");
                if (slug is not null && !AcastAddress.Identifier(slug)) throw new InvalidDataException("Acast returned an invalid episode alias.");
                entries.Add(new(entries.Count + 1, id, Text(item, "title") ?? id, available ? AcastAddress.Canonical(alias, id) : null, Number(item, "duration"), Poster(Text(item, "image")), available,
                    ExpectedArchiveIdentity: new("acast", id)));
            }
            metadata ??= new() { Id = showId, PlaylistId = showId, Extractor = "acast:channel", IsPlaylist = true, Title = Text(root, "title") ?? showId,
                Description = Text(root, "description") ?? Text(root, "summary"), Uploader = Text(root, "author"), ThumbnailUrl = Poster(Text(root, "image")), SourcePageUrl = AcastAddress.Canonical(alias, null) };
            if (entries.Count == total) return metadata with { Entries = entries };
            if (page.Length == 0) throw new InvalidDataException("Acast ended pagination before its declared complete episode count.");
        }
    }
    private static (string Id, string Alias) Show(JsonElement show, string expected)
    {
        var id = Text(show, "showId"); var alias = Text(show, "showUrl");
        if (id is null || !AcastAddress.Mongo(id) || Text(show, "id") != id || alias is null || !AcastAddress.Identifier(alias) || (AcastAddress.Mongo(expected) ? id != expected : alias != expected))
            throw new InvalidDataException("Acast returned another show identity or an unsupported alias.");
        if (Flag(show, "private") || Flag(show, "premium") || True(show, "blacklisted")) throw new NotSupportedException("This Acast show needs additional private/subscriber access support.");
        var publicLink = PublicUrl(Text(show, "link"));
        if (AcastAddress.Matches(publicLink)) { var link = AcastAddress.Parse(publicLink); if (link.Show != alias || link.Episode is not null) throw new InvalidDataException("Acast's public show link disagrees with its identity."); }
        return (id, alias);
    }
    private static string EpisodeId(JsonElement episode, string showId)
    {
        var id = Text(episode, "acastId");
        var original = Text(episode, "id");
        if (id is null || !AcastAddress.Mongo(id) || original is null || original.Length is < 1 or > 2048 || AcastAddress.Mongo(original) && original != id || Text(episode, "showId") != showId)
            throw new InvalidDataException("Acast returned a mismatched episode/show identity.");
        return id;
    }
    private static PageMetadata Episode(JsonElement root, JsonElement show, string alias, string id)
    {
        if (Premium(root)) throw new NotSupportedException("This Acast episode requires additional subscriber/private-feed support.");
        var slug = Text(root, "episodeUrl") ?? id; if (!AcastAddress.Identifier(slug)) throw new InvalidDataException("Acast omitted its episode alias.");
        var publicLink = PublicUrl(Text(root, "link"));
        if (AcastAddress.Matches(publicLink)) { var link = AcastAddress.Parse(publicLink); if (link.Show != alias || link.Episode != slug) throw new InvalidDataException("Acast's episode link disagrees with the supplied show/episode identity."); }
        var duration = Number(root, "duration"); if (duration is not > 0 || duration > 604800) throw new InvalidDataException("Acast omitted a valid episode duration.");
        var url = PublicUrl(Text(root, "url")); var showId = Text(root, "showId")!;
        if (url.Host != "sphinx.acast.com" || url.AbsolutePath != "/p/open/s/" + showId + "/e/" + id + "/media.mp3" || Text(root, "contentType") != "audio/mpeg")
            throw new NotSupportedException("This Acast enclosure/delivery layout needs additional native support.");
        return new() { Id = id, Extractor = "acast", ArchiveIdentity = new("acast", id), Title = Text(root, "title") ?? id, Description = Text(root, "description") ?? Text(root, "summary"),
            DurationSeconds = duration, Uploader = Text(show, "author") ?? Text(show, "title"), ThumbnailUrl = Poster(Text(root, "image")), SourcePageUrl = AcastAddress.Canonical(alias, slug), AnonymousPlayback = true,
            Media = [new() { Id = "audio", Url = url, Kind = MediaKind.Progressive, Extension = "mp3", MimeType = "audio/mpeg", HasVideo = false, HasAudio = true, IsCompleteMedia = true, Source = "acast",
                FileSize = Long(root, "contentLength"), VerificationDemuxer = "mp3", VerificationDurationSeconds = duration }] };
    }
    private static bool Premium(JsonElement element) => Flag(element, "premium");
    private static bool Flag(JsonElement element, string field) => element.TryGetProperty(field, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.ValueKind == JsonValueKind.True :
        throw new NotSupportedException("Acast omitted a supported explicit public access state.");
    private static Uri PublicUrl(string? raw) => Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 ? uri :
        throw new InvalidDataException("Acast returned an unsupported public URL.");
    private static string? Poster(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null; var uri = PublicUrl(raw);
        if (uri.Host is not ("assets.pippa.io" or "assets.acast.com" or "thumborcdn.acast.com" or "cdn-image.acast.com")) throw new NotSupportedException("Acast returned an unsupported artwork origin."); return uri.AbsoluteUri;
    }
    private static async Task<JsonDocument> JsonAsync(Uri endpoint, MediaHttp http, CancellationToken ct)
    {
        var response = await http.GetDocumentAsync(endpoint, 8 * 1024 * 1024, ct).ConfigureAwait(false); if (response.Url != endpoint) throw new InvalidDataException("Acast redirected its public metadata identity.");
        JsonDocument document; try { document = JsonDocument.Parse(response.Text, new() { MaxDepth = 64 }); } catch (JsonException) { throw new InvalidDataException("Acast returned malformed public metadata."); }
        try
        {
            var stack = new Stack<JsonElement>(); stack.Push(document.RootElement); var count = 0;
            while (stack.TryPop(out var item))
            {
                if (++count > 200000) throw new InvalidDataException("Acast returned excessive metadata fields.");
                if (item.ValueKind == JsonValueKind.Object) { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var property in item.EnumerateObject()) { if (!names.Add(property.Name)) throw new InvalidDataException("Acast returned repeated metadata fields."); stack.Push(property.Value); } }
                else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) stack.Push(child);
            }
            return document;
        }
        catch { document.Dispose(); throw; }
    }
}
