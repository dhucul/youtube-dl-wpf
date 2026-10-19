using System.Globalization;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Simplecast;

public sealed class SimplecastProvider : IMediaProvider
{
    private const string Api = "https://api.simplecast.com/";
    public MediaProviderInfo Info { get; } = new("simplecast", "Simplecast public episodes players and complete published podcasts", 200);
    public bool CanHandle(Uri uri) => SimplecastAddress.Matches(uri);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) { var address = SimplecastAddress.Parse(request.Url); return !address.Podcast && address.Id is { } id ? new("simplecast", id) : null; }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = SimplecastAddress.Parse(request.Url); using var anonymous = http.CreateAnonymousSession(); var podcastId = address.Podcast ? address.Id : null;
        if (address.Site is { } site)
        {
            using var document = await JsonAsync(new(Api + "sites/search"), anonymous, ct, new { url = site.AbsoluteUri }).ConfigureAwait(false); var root = document.RootElement;
            if (Text(root, "url")?.TrimEnd('/') != site.AbsoluteUri.TrimEnd('/') || !Boolean(root, "site_enabled") || Text(Object(root, "podcast"), "status") != "published") throw new InvalidDataException("Simplecast returned another unavailable website identity.");
            podcastId = Id(Object(root, "podcast"));
        }
        if (!address.Podcast)
        {
            var endpoint = address.Site is null ? SimplecastAddress.Episode(address.Id!) : new Uri(Api + "episodes/search");
            using var document = await JsonAsync(endpoint, anonymous, ct, address.Site is null ? null : new { url = new Uri(address.Site, "episodes/" + address.Slug).AbsoluteUri }).ConfigureAwait(false); var root = document.RootElement; var id = Id(root); var owner = Id(Object(root, "podcast"));
            if (address.Id is { } expected && id != expected || podcastId is not null && owner != podcastId || address.Slug is { } slug && Text(root, "slug") != slug) throw new InvalidDataException("Simplecast returned another requested episode or website/podcast owner.");
            using var podcast = await JsonAsync(new(Api + "podcasts/" + owner), anonymous, ct).ConfigureAwait(false); ValidatePodcast(podcast.RootElement, owner);
            return Episode(root, podcast.RootElement, id, owner);
        }
        using var showDocument = await JsonAsync(new(Api + "podcasts/" + podcastId), anonymous, ct).ConfigureAwait(false); var show = showDocument.RootElement; ValidatePodcast(show, podcastId!);
        var entries = new List<NativePlaylistEntry>(); var ids = new HashSet<string>(StringComparer.Ordinal); int? total = null; int? pageTotal = null; var pageNumber = 1;
        var endpointPage = new Uri(Api + "podcasts/" + podcastId + "/episodes?limit=100&offset=0&private=false&sort=latest&status=published"); var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            ct.ThrowIfCancellationRequested(); if (!seen.Add(endpointPage.AbsoluteUri)) throw new InvalidDataException("Simplecast repeated a podcast continuation.");
            using var page = await JsonAsync(endpointPage, anonymous, ct).ConfigureAwait(false); var root = page.RootElement; var count = Integer(root, "count") ?? throw new InvalidDataException("Simplecast omitted its published episode count.");
            var self = PublicUrl(Text(root, "href")); if (self.Host != "api.simplecast.com" || self.AbsolutePath != endpointPage.AbsolutePath) throw new InvalidDataException("Simplecast returned another public inventory owner or endpoint.");
            if (count > Math.Min(10000, request.MaximumPlaylistEntries)) throw new NotSupportedException("The complete Simplecast podcast exceeds the configured inventory limit.");
            if (total is not null && count != total) throw new InvalidDataException("Simplecast changed its published episode count during pagination."); total = count;
            var pages = Object(root, "pages"); var number = Integer(pages, "current"); var declaredPages = Integer(pages, "total");
            if (number != pageNumber || Integer(pages, "limit") != 100 || declaredPages is null || declaredPages != Math.Max(1, (count + 99) / 100) || pageTotal is not null && pageTotal != declaredPages) throw new InvalidDataException("Simplecast returned conflicting public page counts."); pageTotal = declaredPages;
            if (!root.TryGetProperty("collection", out var collection) || collection.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Simplecast omitted a published episode page.");
            var items = collection.EnumerateArray().ToArray(); if (items.Length > 100 || entries.Count + items.Length > count) throw new InvalidDataException("Simplecast returned an oversized public episode inventory.");
            foreach (var item in items)
            {
                var id = Id(item); if (!ids.Add(id) || Text(item, "href") != SimplecastAddress.Episode(id).AbsoluteUri) throw new InvalidDataException("Simplecast repeated or changed an episode identity.");
                var available = Text(item, "status") == "published" && !Boolean(item, "is_hidden") && Text(item, "audio_status") == "transcoded";
                entries.Add(new(entries.Count + 1, id, Text(item, "title") ?? id, available ? SimplecastAddress.Episode(id) : null, Number(item, "duration"), Poster(Text(item, "image_url")), available, ExpectedArchiveIdentity: new("simplecast", id)));
            }
            if (!pages.TryGetProperty("next", out var next)) throw new InvalidDataException("Simplecast omitted its terminal public pagination state.");
            if (next.ValueKind == JsonValueKind.Null) { if (entries.Count != count || pageNumber != pageTotal) throw new InvalidDataException("Simplecast ended before its declared complete inventory."); break; }
            if (next.ValueKind != JsonValueKind.Object || items.Length != 100 || entries.Count >= count) throw new InvalidDataException("Simplecast returned an incomplete public episode page.");
            endpointPage = Continuation(Text(next, "href"), podcastId!, entries.Count); pageNumber++;
        }
        return new() { Id = podcastId, PlaylistId = podcastId, Extractor = "simplecast:podcast", IsPlaylist = true, Title = Text(show, "title") ?? podcastId!, Description = Text(show, "description"),
            Uploader = Text(show, "title"), ThumbnailUrl = Poster(Text(show, "image_url")), Entries = entries, SourcePageUrl = address.Site ?? new Uri(Api + "podcasts/" + podcastId) };
    }
    private static void ValidatePodcast(JsonElement root, string expected)
    {
        if (Id(root) != expected || Text(root, "href") != Api + "podcasts/" + expected) throw new InvalidDataException("Simplecast returned another podcast identity.");
        if (Text(root, "status") != "published" || Boolean(root, "is_private")) throw new NotSupportedException("This Simplecast podcast requires additional private/unpublished access support.");
    }
    private static PageMetadata Episode(JsonElement root, JsonElement podcast, string id, string owner)
    {
        if (Text(root, "href") != SimplecastAddress.Episode(id).AbsoluteUri || Text(Object(root, "podcast"), "href") != Api + "podcasts/" + owner) throw new InvalidDataException("Simplecast's episode and podcast links conflict with their identities.");
        if (Text(root, "status") != "published" || !Boolean(root, "is_published") || Boolean(root, "is_hidden") || Text(root, "audio_status") != "transcoded") throw new NotSupportedException("Simplecast did not expose a public published complete audio episode.");
        var duration = Number(root, "duration"); if (duration is not > 0 || duration > 604800) throw new InvalidDataException("Simplecast omitted a supported full episode duration.");
        if (Text(root, "audio_content_type") != "audio/mpeg") throw new NotSupportedException("This Simplecast media type needs additional native support.");
        var source = PublicUrl(Text(root, "audio_file_url")); if (Text(Object(root, "audio_file"), "url") != source.AbsoluteUri) throw new InvalidDataException("Simplecast returned conflicting supplied audio URLs.");
        var prefix = source.Host == "cdn.simplecast.com" ? "/audio/" : "/";
        if (source.Host is not ("cdn.simplecast.com" or "audio.simplecast.com" or "sxm.simplecastaudio.com") || !source.AbsolutePath.StartsWith(prefix + owner + "/episodes/" + id + "/audio/", StringComparison.Ordinal) || !source.AbsolutePath.EndsWith(".mp3", StringComparison.Ordinal)) throw new NotSupportedException("This Simplecast audio origin or owner/episode layout needs additional support.");
        var dynamicAudio = source.Host == "sxm.simplecastaudio.com";
        return new() { Id = id, Extractor = "simplecast", ArchiveIdentity = new("simplecast", id), Title = Text(root, "title") ?? id, Description = Text(root, "description") ?? Text(root, "long_description"),
            Uploader = Text(podcast, "title"), DurationSeconds = duration, ThumbnailUrl = Poster(Text(root, "image_url") ?? Text(podcast, "image_url")), SourcePageUrl = SimplecastAddress.Episode(id), AnonymousPlayback = true,
            Warnings = dynamicAudio ? ["This service may insert ads, so downloaded audio can be longer than the episode's listed duration."] : [],
            Media = [new() { Id = "source", Url = source, Kind = MediaKind.Progressive, Source = "simplecast", Extension = "mp3", MimeType = "audio/mpeg", HasAudio = true, HasVideo = false, AudioCodec = "mp3", IsCompleteMedia = true,
                FileSize = Long(root, "audio_file_size"), VerificationDurationSeconds = dynamicAudio ? null : duration, VerificationMinimumDurationSeconds = dynamicAudio ? duration : null, VerificationDemuxer = "mp3" }] };
    }
    private static bool Boolean(JsonElement root, string name) => root.TryGetProperty(name, out var field) && field.ValueKind is JsonValueKind.True or JsonValueKind.False ? field.ValueKind == JsonValueKind.True : throw new InvalidDataException("Simplecast omitted a supported explicit access state.");
    private static string Id(JsonElement root) { var id = Text(root, "id"); return id is not null && SimplecastAddress.Uuid(id) ? id.ToLowerInvariant() : throw new InvalidDataException("Simplecast omitted a valid stable UUID."); }
    private static Uri PublicUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 ? uri : throw new InvalidDataException("Simplecast returned an invalid public URL.");
    private static string? Poster(string? raw) { if (raw is null) return null; var uri = PublicUrl(raw); if (uri.Host != "image.simplecastcdn.com" || !uri.AbsolutePath.StartsWith("/images/", StringComparison.Ordinal)) throw new NotSupportedException("Simplecast returned an unsupported artwork origin."); return uri.AbsoluteUri; }
    private static Uri Continuation(string? raw, string podcast, int offset)
    {
        var uri = PublicUrl(raw); if (uri.Host != "api.simplecast.com" || uri.AbsolutePath != "/podcasts/" + podcast + "/episodes") throw new InvalidDataException("Simplecast continued another podcast or endpoint.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)) { var pair = part.Split('=', 2); if (pair.Length != 2 || !values.TryAdd(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1]))) throw new InvalidDataException("Simplecast repeated or omitted continuation parameters."); }
        if (values.Count != 5 || values.GetValueOrDefault("limit") != "100" || values.GetValueOrDefault("offset") != offset.ToString(CultureInfo.InvariantCulture) || values.GetValueOrDefault("private") != "false" || values.GetValueOrDefault("sort") != "latest" || values.GetValueOrDefault("status") != "published") throw new InvalidDataException("Simplecast changed public podcast continuation scope.");
        return uri;
    }
    private static async Task<JsonDocument> JsonAsync(Uri endpoint, MediaHttp http, CancellationToken ct, object? body = null)
    {
        var response = body is null ? await http.GetDocumentAsync(endpoint, 8 * 1024 * 1024, ct).ConfigureAwait(false) : await http.PostJsonDocumentAsync(endpoint, body, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        if (response.Url != endpoint) throw new InvalidDataException("Simplecast redirected a public metadata identity.");
        JsonDocument document; try { document = JsonDocument.Parse(response.Text, new() { MaxDepth = 64 }); } catch (JsonException) { throw new InvalidDataException("Simplecast returned malformed public metadata."); }
        try
        {
            var stack = new Stack<JsonElement>(); stack.Push(document.RootElement); var count = 0;
            while (stack.TryPop(out var item))
            {
                if (++count > 200000) throw new InvalidDataException("Simplecast returned excessive metadata fields.");
                if (item.ValueKind == JsonValueKind.Object) { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var property in item.EnumerateObject()) { if (!names.Add(property.Name)) throw new InvalidDataException("Simplecast returned repeated metadata fields."); stack.Push(property.Value); } }
                else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) stack.Push(child);
            }
            return document;
        }
        catch { document.Dispose(); throw; }
    }
}
