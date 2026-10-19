using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Podomatic;

public sealed partial class PodomaticProvider : IMediaProvider
{
    private const string Api = "https://www.podomatic.com/v2/";
    public MediaProviderInfo Info { get; } = new("podomatic", "Podomatic current public episodes embeds and complete podcasts", 200);
    public bool CanHandle(Uri uri) => PodomaticAddress.Matches(uri);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => PodomaticAddress.Parse(request.Url).EpisodeId is { } id ? new("podomatic", id) : null;
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = PodomaticAddress.Parse(request.Url); using var anonymous = http.CreateAnonymousSession();
        if (address.Feed) return await ReadFeedAsync(address, request, anonymous, ct).ConfigureAwait(false);
        if (address.EpisodeId is not null || address.Stamp is not null)
        {
            var endpoint = address.EpisodeId is { } id ? new Uri(Api + "episodes/" + id) : new Uri(Api + "podcasts/~" + Uri.EscapeDataString(address.Name!) + "/episodes/" + Uri.EscapeDataString(address.Stamp!));
            using var document = await JsonAsync(endpoint, anonymous, ct).ConfigureAwait(false); var item = Object(document.RootElement, "episode"); var identity = Identity(item);
            if (address.EpisodeId is { } expected && identity.Id != expected || address.Name is { } name && identity.Name != name || address.Stamp is { } stamp && identity.Stamp != stamp) throw new InvalidDataException("Podomatic returned another requested episode or publication identity.");
            using var podcast = await JsonAsync(new(Api + "podcasts/" + identity.Collection), anonymous, ct).ConfigureAwait(false); ValidatePodcast(Object(podcast.RootElement, "podcast"), identity.Collection, identity.Name);
            return Episode(item, identity);
        }
        var showEndpoint = new Uri(Api + "podcasts/" + (address.PodcastId ?? "~" + Uri.EscapeDataString(address.Name!)));
        using var showDocument = await JsonAsync(showEndpoint, anonymous, ct).ConfigureAwait(false); var show = Object(showDocument.RootElement, "podcast"); var collection = Id(show, "collection_guid"); var owner = Text(show, "subdomain");
        if (owner is null || !PodomaticAddress.ValidName(owner) || address.PodcastId is { } expectedCollection && collection != expectedCollection || address.Name is { } expectedOwner && owner != expectedOwner) throw new InvalidDataException("Podomatic returned another requested podcast.");
        ValidatePodcast(show, collection, owner);
        var entries = new List<NativePlaylistEntry>(); var ids = new HashSet<string>(StringComparer.Ordinal); var pageNumber = 1; int? pageCount = null; int? declared = null;
        while (true)
        {
            ct.ThrowIfCancellationRequested(); using var document = await JsonAsync(new(Api + "podcasts/" + collection + "/episodes?per_page=100&page=" + pageNumber.ToString(CultureInfo.InvariantCulture)), anonymous, ct).ConfigureAwait(false); var root = document.RootElement; var pagination = Object(root, "pagination");
            var count = Integer(pagination, "item_count") ?? throw new InvalidDataException("Podomatic omitted its declared public listing count.");
            if (count > Math.Min(10000, request.MaximumPlaylistEntries)) throw new NotSupportedException("The complete Podomatic public listing exceeds the configured inventory limit.");
            var pages = Integer(pagination, "page_count"); if (declared is not null && count != declared || Integer(pagination, "page") != pageNumber || pages is null || count > 0 && pages == 0 || pageCount is not null && pageCount != pages) throw new InvalidDataException("Podomatic changed its public episode or page counts."); declared = count; pageCount = pages;
            if (!root.TryGetProperty("episodes", out var supplied) || supplied.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Podomatic omitted an episode page.");
            var items = supplied.EnumerateArray().ToArray(); if (items.Length > 100 || entries.Count + items.Length > declared) throw new InvalidDataException("Podomatic returned an oversized public episode page.");
            foreach (var item in items)
            {
                var identity = Identity(item); if (identity.Collection != collection || identity.Name != owner || !ids.Add(identity.Id)) throw new InvalidDataException("Podomatic changed ownership or repeated an episode during pagination.");
                var available = Text(item, "media_type") is "audio" or "video" && Text(item, "media_url") is { Length: > 0 };
                entries.Add(new(entries.Count + 1, identity.Id, Text(item, "title") ?? identity.Id, available ? PodomaticAddress.Episode(identity.Id) : null, Number(item, "duration"), Poster(Text(item, "large_image_url") ?? Text(item, "image_url")), available, ExpectedArchiveIdentity: new("podomatic", identity.Id)));
            }
            if (pageNumber == pageCount || declared == 0) { if (entries.Count != declared) throw new InvalidDataException("Podomatic ended before its declared complete episode count."); break; }
            if (items.Length == 0 || entries.Count >= declared || ++pageNumber > 10000) throw new InvalidDataException("Podomatic returned incomplete or unbounded pagination.");
        }
        return new() { Id = collection, PlaylistId = collection, Extractor = "podomatic:podcast", IsPlaylist = true, Title = Text(show, "title") ?? owner, Description = Text(show, "description"), Uploader = Text(Object(show, "profile"), "profile_name"), ThumbnailUrl = Poster(Text(show, "large_image_url") ?? Text(show, "image_url")),
            SourcePageUrl = new("https://www.podomatic.com/podcasts/" + owner), Entries = entries };
    }
    private sealed record EpisodeIdentity(string Id, string Collection, string Name, string Stamp, Uri Permalink);
    private static EpisodeIdentity Identity(JsonElement item)
    {
        var id = Id(item, "episode_guid"); var collection = Id(item, "collection_guid"); var name = Text(item, "podcast_subdomain"); var permalink = PublicUrl(Text(item, "permalink_url")); var address = PodomaticAddress.Parse(permalink);
        if (name is null || !PodomaticAddress.ValidName(name) || address.Name != name || address.Stamp is null || address.EpisodeId is not null) throw new InvalidDataException("Podomatic's episode permalink conflicts with its publisher identity.");
        return new(id, collection, name, address.Stamp, permalink);
    }
    private static void ValidatePodcast(JsonElement show, string id, string name)
    {
        if (Id(show, "collection_guid") != id || Text(show, "subdomain") != name) throw new InvalidDataException("Podomatic returned conflicting podcast identities.");
        var feed = PublicUrl(Text(show, "feed_url")); var page = PodomaticAddress.Parse(PublicUrl(Text(show, "podcast_url")));
        if (feed.Host != name + ".podomatic.com" || feed.AbsolutePath != "/rss2.xml" || page.Name != name || page.Stamp is not null) throw new InvalidDataException("Podomatic's public feed or page belongs to another podcast.");
    }
    private static PageMetadata Episode(JsonElement item, EpisodeIdentity identity)
    {
        var metadata = new PageMetadata { Id = identity.Id, Extractor = "podomatic", ArchiveIdentity = new("podomatic", identity.Id), Title = Text(item, "title") ?? identity.Id, Description = Text(item, "description"), Uploader = Text(Object(item, "profile"), "profile_name") ?? Text(item, "podcast_title"),
            DurationSeconds = Number(item, "duration"), ThumbnailUrl = Poster(Text(item, "large_image_url") ?? Text(item, "image_url")), SourcePageUrl = identity.Permalink, AnonymousPlayback = true };
        var raw = Text(item, "media_url"); var kind = Text(item, "media_type"); if (string.IsNullOrWhiteSpace(raw) || kind == "none") return metadata with { Warnings = ["This Podomatic episode has no available public media."] };
        var source = PublicUrl(raw); var extension = Path.GetExtension(source.AbsolutePath).TrimStart('.').ToLowerInvariant(); var audio = kind == "audio"; var video = kind == "video";
        if (!(audio && extension is "mp3" or "m4a" || video && extension is "mp4" or "m4v" or "mov")) throw new NotSupportedException("This Podomatic media type or container needs additional native support.");
        var path = Regex.Match(source.AbsolutePath, @"\A/vod/[A-Za-z0-9_-]+/[a-f0-9]{2}/[a-f0-9]{2}/[a-f0-9]{2}/(?<owner>[A-Za-z0-9_-]+)/media/published/(?<file>[1-9][0-9]*)\.[A-Za-z0-9]+\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (source.Host != "streams.podomatic.com" || !path.Success || path.Groups["owner"].Value != identity.Name) throw new NotSupportedException("This Podomatic media origin or publisher path needs additional support.");
        var duration = Number(item, "duration"); if (duration is not > 0 || duration > 604800) throw new InvalidDataException("Podomatic omitted a supported complete program duration.");
        if (!item.TryGetProperty("has_dynamic_ads", out var ads) || ads.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException("Podomatic omitted its supported delivery-duration state.");
        var dynamic = ads.ValueKind == JsonValueKind.True;
        return metadata with { Media = [new() { Id = "primary", Url = source, Kind = MediaKind.Progressive, Source = "podomatic", Extension = extension, MimeType = audio ? extension == "mp3" ? "audio/mpeg" : "audio/mp4" : "video/mp4", HasAudio = audio ? true : null, HasVideo = video, AudioCodec = audio && extension == "mp3" ? "mp3" : null, IsCompleteMedia = true,
            VerificationDemuxer = extension == "mp3" ? "mp3" : "mov", VerificationDurationSeconds = dynamic ? null : duration, VerificationMinimumDurationSeconds = dynamic ? duration : null }] };
    }
    private static string Id(JsonElement element, string name) { var value = Long(element, name)?.ToString(CultureInfo.InvariantCulture); return value is not null && PodomaticAddress.Numeric(value) ? value : throw new InvalidDataException("Podomatic omitted a valid stable numeric identity."); }
    private static Uri PublicUrl(string? raw) => Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 ? uri : throw new InvalidDataException("Podomatic returned an invalid public URL.");
    private static string? Poster(string? raw) { if (string.IsNullOrWhiteSpace(raw)) return null; var uri = PublicUrl(raw); if (uri.Host != "assets.podomatic.net") throw new NotSupportedException("Podomatic returned an unsupported artwork origin."); return uri.AbsoluteUri; }
    private static async Task<JsonDocument> JsonAsync(Uri endpoint, MediaHttp http, CancellationToken ct)
    {
        var response = await http.GetDocumentAsync(endpoint, 8 * 1024 * 1024, ct).ConfigureAwait(false); if (response.Url != endpoint) throw new InvalidDataException("Podomatic redirected its public metadata identity.");
        JsonDocument document; try { document = JsonDocument.Parse(response.Text, new() { MaxDepth = 64 }); } catch (JsonException) { throw new InvalidDataException("Podomatic returned malformed public metadata."); }
        try
        {
            var stack = new Stack<JsonElement>(); stack.Push(document.RootElement); var count = 0;
            while (stack.TryPop(out var item))
            {
                if (++count > 200000) throw new InvalidDataException("Podomatic returned excessive public metadata.");
                if (item.ValueKind == JsonValueKind.Object) { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var field in item.EnumerateObject()) { if (!names.Add(field.Name)) throw new InvalidDataException("Podomatic returned repeated metadata fields."); stack.Push(field.Value); } }
                else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) stack.Push(child);
            }
            if (Text(document.RootElement, "status") != "success") throw new NotSupportedException("Podomatic did not provide an available public metadata response.");
            return document;
        }
        catch { document.Dispose(); throw; }
    }
}
