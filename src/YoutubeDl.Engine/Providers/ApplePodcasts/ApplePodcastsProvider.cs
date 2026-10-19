using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Feeds;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.ApplePodcasts;

public sealed class ApplePodcastsProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("apple:podcasts", "Apple Podcasts public shows and episodes", 200);
    public bool CanHandle(Uri url) => ApplePodcastAddress.IsHost(url) && (url.Host != "itunes.apple.com" || url.AbsolutePath.Contains("/podcast/", StringComparison.Ordinal));
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)
    {
        var address = ApplePodcastAddress.Parse(request.Url);
        return address.EpisodeId is { } episode ? new("applepodcasts", episode) : null;
    }

    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = ApplePodcastAddress.Parse(request.Url);
        if (address.EpisodeId is not null)
        {
            var document = await http.GetDocumentAsync(address.Canonical, 8 * 1024 * 1024, ct).ConfigureAwait(false);
            var final = ApplePodcastAddress.Parse(document.Url);
            if (final.ShowId != address.ShowId || final.EpisodeId != address.EpisodeId || final.Country != address.Country)
                throw new InvalidDataException("Apple redirected the request to a different podcast episode or storefront.");
            return await ParseEpisodeAsync(document.Text, address, document.Url, http, ct).ConfigureAwait(false);
        }

        var lookup = new Uri("https://itunes.apple.com/lookup?id=" + address.ShowId + "&entity=podcast&country=" + address.Country);
        var response = await http.GetDocumentAsync(lookup, 2 * 1024 * 1024, ct).ConfigureAwait(false);
        if (response.Url.Scheme != "https" || response.Url.Host != "itunes.apple.com" || !response.Url.IsDefaultPort || response.Url.UserInfo.Length != 0)
            throw new InvalidDataException("Apple redirected its catalogue lookup outside the supported API.");
        using var json = JsonDocument.Parse(response.Text);
        var results = Array(json.RootElement, "results");
        if (Integer(json.RootElement, "resultCount") != results.Length || results.Length > 10) throw new InvalidDataException("Apple returned an invalid catalogue result count.");
        if (results.Length == 0) throw new InvalidOperationException("This podcast is not available in the requested Apple storefront.");
        if (results.Length != 1 || Text(results[0], "kind") != "podcast" || Id(results[0], "collectionId") != address.ShowId || Id(results[0], "trackId") != address.ShowId)
            throw new InvalidDataException("Apple returned another podcast identity.");
        var show = results[0];
        var feedUrl = HttpUrl(Text(show, "feedUrl"), "publisher feed");
        var feedDocument = await http.GetDocumentAsync(feedUrl, 5 * 1024 * 1024, ct).ConfigureAwait(false);
        var feed = await MediaFeedReader.ReadAsync(feedDocument, request, http, ct).ConfigureAwait(false);
        return feed with { Id = address.ShowId, PlaylistId = address.ShowId, Extractor = Info.Id, SourcePageUrl = address.Canonical,
            Uploader = feed.Uploader ?? Text(show, "artistName"), ThumbnailUrl = feed.ThumbnailUrl ?? ArtworkUrl(Text(show, "artworkUrl600")) };
    }

    private static async Task<PageMetadata> ParseEpisodeAsync(string html, ApplePodcastAddress address, Uri source, MediaHttp http, CancellationToken ct)
    {
        var scripts = Regex.Matches(html, @"<script\b(?=[^>]{0,8192}\bid\s*=\s*['""]serialized-server-data['""])[^>]*>(?<json>[\s\S]*?)</script\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (scripts.Count != 1) throw new NotSupportedException("The Apple episode page did not expose a supported playback document.");
        var raw = scripts[0].Groups["json"].Value;
        if (Encoding.UTF8.GetByteCount(raw) > 6 * 1024 * 1024) throw new InvalidDataException("The Apple playback document exceeds its size limit.");
        using var document = JsonDocument.Parse(raw);
        var records = Array(document.RootElement, "data");
        if (records.Length > 20) throw new InvalidDataException("Apple returned too many page records.");
        var matches = records.Where(record => Text(Object(record, "intent"), "$kind") == "EpisodePageIntent" &&
            Id(Object(record, "intent"), "adamId") == address.EpisodeId && Id(Object(record, "intent"), "showAdamId") == address.ShowId &&
            (Text(Object(record, "intent"), "storefront") is not { } storefront || storefront.Equals(address.Country, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("The Apple playback document does not identify exactly the requested episode.");
        var page = Object(matches[0], "data");
        if (Id(page, "pageAdamId") != address.EpisodeId || Text(page, "pageContext") != "episodePage" || True(page, "isIncomplete"))
            throw new InvalidDataException("Apple returned incomplete or mismatched episode data.");
        var buttons = Array(page, "headerButtonItems");
        if (buttons.Length > 100) throw new InvalidDataException("The Apple episode header exceeds its size limit.");
        var models = buttons.Where(button => Text(button, "modelType") == "EpisodeLockup").Select(button => Object(button, "model"))
            .Where(model => Id(model, "adamId") == address.EpisodeId && Id(model, "showAdamId") == address.ShowId).ToArray();
        if (models.Length == 0) throw new NotSupportedException("This Apple episode layout has no supported playback model.");
        var model = models[0];
        if (models.Skip(1).Any(other => Text(other, "entitlementState") != Text(model, "entitlementState") ||
            !Array(other, "mediaEnclosures").Select(EnclosureIdentity).SequenceEqual(Array(model, "mediaEnclosures").Select(EnclosureIdentity))))
            throw new InvalidDataException("Apple returned conflicting playback models for the episode.");
        var metadata = new PageMetadata { Id = address.EpisodeId, PlaylistId = address.ShowId, Extractor = "apple:podcasts", Title = Text(model, "title") ?? address.EpisodeId!,
            Description = PlainText(Text(model, "summary")), Uploader = Text(model, "channelName"), DurationSeconds = Number(model, "duration"), SourcePageUrl = source,
            ThumbnailUrl = Artwork(Object(model, "episodeArtwork")) ?? Artwork(Object(model, "icon")), ArchiveIdentity = new("applepodcasts", address.EpisodeId!) };
        if (Text(model, "entitlementState") != "free") return metadata with { Warnings = ["This Apple episode requires playback access that the native provider has not established."] };
        var enclosures = Array(model, "mediaEnclosures");
        if (enclosures.Length > 20) throw new InvalidDataException("Apple returned too many episode media enclosures.");
        var media = new List<MediaCandidate>(); var formats = new List<NativeFormat>();
        foreach (var enclosure in enclosures)
        {
            ct.ThrowIfCancellationRequested();
            var id = "episode-" + (formats.Count + 1);
            var price = Text(enclosure, "priceType");
            var unavailable = True(enclosure, "isPreview") || price is "PRVW" or "PREVIEW" ? "Apple exposed a preview rather than complete episode media." :
                True(enclosure, "isProtected") || Object(enclosure, "drm").ValueKind == JsonValueKind.Object ? "This Apple episode media is protected." : null;
            var url = unavailable is null ? HttpUrl(Text(enclosure, "streamUrl"), "episode media") : null;
            var kind = url is null ? MediaKind.Unknown : PageMediaExtractor.Classify(url);
            var extension = url is null ? null : PageMediaExtractor.ExtensionFrom(url);
            string? contentType = null;
            if (unavailable is null && kind == MediaKind.Unknown)
            {
                try
                {
                    var headers = await http.GetDocumentAsync(url!, 4096, ct, mediaHeadersOnly: true).ConfigureAwait(false);
                    url = HttpUrl(headers.Url.AbsoluteUri, "redirected episode media"); contentType = headers.ContentType;
                    kind = PageMediaExtractor.Classify(url, contentType); extension = PageMediaExtractor.ExtensionFrom(url, contentType);
                }
                catch (Exception ex) when (ex is HttpRequestException or TimeoutException or InvalidDataException)
                { unavailable = "The Apple episode media endpoint did not return supported media headers."; }
            }
            if (unavailable is null && (kind == MediaKind.Unknown || extension is null)) unavailable = "This Apple media address does not expose a supported media format.";
            var kinds = Array(enclosure, "mediaKinds").Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).ToArray();
            var video = kinds.Contains("video") || Text(model, "mediaType") == "video";
            var audio = kinds.Contains("audio") || Text(model, "mediaType") == "audio";
            if (!video && !audio) unavailable ??= "Apple did not identify the episode media type.";
            var codec = !video ? extension switch { "mp3" => "mp3", "aac" => "aac", "opus" => "opus", "flac" => "flac", _ => null } : null;
            var mime = contentType ?? (extension switch { "mp3" => "audio/mpeg", "m4a" => "audio/mp4", "mp4" or "m4v" => "video/mp4", "webm" => "video/webm", _ => null });
            formats.Add(new() { Id = id, Url = unavailable is null ? url : null, Protocol = kind == MediaKind.Hls ? "m3u8" : kind == MediaKind.Dash ? "dash" : "https",
                Extension = extension, MimeType = mime, HasVideo = video, HasAudio = audio ? true : null, AudioOnly = audio && !video, AudioCodec = codec, Note = price,
                Availability = unavailable is null ? FormatAvailability.Ready : FormatAvailability.MissingUrl, UnavailableReason = unavailable });
            if (unavailable is null) media.Add(new() { Id = id, Url = url!, Kind = kind, Extension = extension, MimeType = mime, HasVideo = video, HasAudio = audio ? true : null,
                AudioCodec = codec, Source = "apple:podcasts" });
        }
        return metadata with { Media = media, ReportedFormats = formats, Warnings = enclosures.Length == 0 ? ["Apple did not expose media for this episode."] :
            formats.Where(format => format.UnavailableReason is not null).Select(format => format.UnavailableReason!).Distinct().ToArray() };
    }

    private static string? Id(JsonElement element, string property) => Text(element, property) ?? Long(element, property)?.ToString(CultureInfo.InvariantCulture);
    private static string EnclosureIdentity(JsonElement item) => JsonSerializer.Serialize(new { Url = Text(item, "streamUrl"), Price = Text(item, "priceType"),
        Preview = True(item, "isPreview"), Protected = True(item, "isProtected") || Object(item, "drm").ValueKind == JsonValueKind.Object,
        Duration = Number(item, "duration"), Kinds = Array(item, "mediaKinds").Select(kind => kind.GetRawText()).Order().ToArray() });
    private static Uri HttpUrl(string? value, string purpose)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https") || url.UserInfo.Length != 0)
            throw new InvalidDataException("Apple returned an invalid " + purpose + " URL.");
        return url;
    }
    private static string? Artwork(JsonElement image)
    {
        var template = Text(image, "template");
        if (template is null) return null;
        var width = Math.Clamp(Integer(image, "width") ?? 1200, 1, 1200); var height = Math.Clamp(Integer(image, "height") ?? 1200, 1, 1200);
        var value = template.Replace("{w}", width.ToString(CultureInfo.InvariantCulture)).Replace("{h}", height.ToString(CultureInfo.InvariantCulture)).Replace("{f}", "jpg");
        return value.Contains('{') || value.Contains('}') ? null : ArtworkUrl(value);
    }
    private static string? ArtworkUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 &&
        url.Host.EndsWith(".mzstatic.com", StringComparison.OrdinalIgnoreCase) ? url.AbsoluteUri : null;
    private static string? PlainText(string? value)
    {
        if (value is null) return null;
        if (value.Length > 128 * 1024) throw new InvalidDataException("The Apple episode description exceeds its size limit.");
        value = Regex.Replace(value, @"</?(?:p|div|br)\b[^>]*>", "\n", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        return WebUtility.HtmlDecode(Regex.Replace(value, @"<[^>]*>", "", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))).Trim();
    }
}
