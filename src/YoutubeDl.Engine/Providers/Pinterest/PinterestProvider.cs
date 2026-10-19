using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Pinterest;

public sealed partial class PinterestProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("pinterest", "Pinterest public hosted videos and offered player embeds", 200);
    public bool CanHandle(Uri url) => PinterestAddress.Matches(url);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        using var anonymous = http.CreateAnonymousSession();
        using var session = anonymous.WithHeaders(new Dictionary<string, string> { ["Referer"] = "https://www.pinterest.com/",
            ["User-Agent"] = string.IsNullOrWhiteSpace(request.UserAgent) ? MediaHttp.DefaultUserAgent : request.UserAgent });
        if (PinterestBoardAddress.TryParse(request.Url, out var boardAddress)) return await ReadBoardAsync(request, boardAddress!, session, ct).ConfigureAwait(false);
        var address = PinterestAddress.Parse(request.Url);
        var document = await session.GetDocumentAsync(address.Page, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        if (address.Id is null && PinterestBoardAddress.TryParse(document.Url, out boardAddress)) return await ReadBoardAsync(request, boardAddress!, session, ct, document).ConfigureAwait(false);
        var returned = PinterestAddress.Parse(document.Url);
        if (returned.Id is null || address.Id is not null && returned.Id != address.Id) throw new InvalidDataException("Pinterest redirected to another pin or an unresolved short link.");
        var id = returned.Id; using var state = PinterestRelayReader.Read(document.Text, id); var pin = state.RootElement;
        var hasHidden = pin.TryGetProperty("isHidden", out var hidden);
        // Some public story responses omit this optional pin field. Their
        // completed story identity/deletion state and offered resources are
        // checked independently below; an explicit hidden state still fails.
        if (hasHidden && hidden.ValueKind != JsonValueKind.False || !hasHidden && Array(pin, "__storyProjections").Length == 0)
            throw new NotSupportedException("Pinterest did not confirm an available public pin.");
        if (Object(pin, "carouselData").ValueKind == JsonValueKind.Object)
            throw new NotSupportedException("Pinterest carousels need additional native inventory support.");
        var description = Text(pin, "closeupUnifiedDescription") ?? Text(pin, "description");
        var title = new[] { Text(pin, "closeupUnifiedTitle"), Text(pin, "title"), Text(pin, "gridTitle") }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ??
            (string.IsNullOrWhiteSpace(description) ? "Pinterest pin " + id : description[..Math.Min(description.Length, 160)]);
        var creator = Object(pin, "nativeCreator"); var uploader = Text(creator, "fullName") ?? Text(Object(pin, "pinner"), "fullName");
        if (Array(pin, "__storyProjections").Length > 0) return ReadStory(request, pin, id, title, description, uploader, document.Url);
        var videos = Object(pin, "videos"); var formats = Object(videos, "videoList");
        if (formats.ValueKind != JsonValueKind.Object)
        {
            var embed = Text(Object(pin, "embed"), "src");
            var delegated = Uri.TryCreate(embed, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" && url.IsDefaultPort && url.UserInfo.Length == 0 ? new[] { url } : [];
            return new() { Id = id, Extractor = Info.Id, Title = title, Description = description, Uploader = uploader, SourcePageUrl = document.Url,
                EmbeddedPages = delegated, Warnings = delegated.Length == 0 ? ["This pin does not expose supported hosted video or a native player embed. Image and link-only layouts need additional support."] : [] };
        }
        return Hosted(id, videos, title, description, uploader, document.Url, pin);
    }
    private static PageMetadata Hosted(string id, JsonElement videos, string title, string? description, string? uploader, Uri page, JsonElement pin = default, bool requireVideoId = true)
    {
        var formats = Object(videos, "videoList");
        var duration = Number(videos, "duration") / 1000;
        if (duration is not > 0 || duration > 7 * 24 * 3600 || requireVideoId && !NumericId(Text(videos, "entityId")))
            throw new InvalidDataException("Pinterest omitted a supported complete video identity/duration.");
        if (formats.EnumerateObject().Count() > 64) throw new InvalidDataException("Pinterest returned too many video formats.");
        var media = new List<MediaCandidate>(); var thumbnails = new List<ThumbnailCandidate>(); string? asset = null;
        foreach (var format in formats.EnumerateObject())
        {
            if (format.Name.StartsWith("__", StringComparison.Ordinal) || format.Value.ValueKind == JsonValueKind.Null) continue;
            if (format.Value.ValueKind != JsonValueKind.Object || !Regex.IsMatch(format.Name, @"\A[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                throw new InvalidDataException("Pinterest returned an unsupported format declaration.");
            var (url, identity, hls) = Resource(Text(format.Value, "url"));
            if (asset is not null && identity != asset) throw new InvalidDataException("Pinterest video formats point to different assets.");
            asset = identity;
            if (Number(format.Value, "duration") is { } milliseconds && Math.Abs(milliseconds / 1000 - duration.Value) > 0.001)
                throw new InvalidDataException("Pinterest returned conflicting complete video durations.");
            var width = Dimension(format.Value, "width"); var height = Dimension(format.Value, "height");
            media.Add(new() { Id = format.Name, Url = url, Kind = hls ? MediaKind.Hls : MediaKind.Progressive, Extension = hls ? "ts" : "mp4",
                MimeType = hls ? "application/x-mpegURL" : "video/mp4", Width = hls ? null : width, Height = hls ? null : height,
                HasVideo = true, HasAudio = null, IsCompleteMedia = true, Source = "pinterest", Label = format.Name,
                VerificationDemuxer = hls ? "mpegts" : "mov", VerificationDurationSeconds = duration });
            if (VideoImage(Text(format.Value, "thumbnail"), asset) is { } poster)
                thumbnails.Add(new() { Id = format.Name, Url = poster, Width = width, Height = height });
        }
        if (media.Count == 0 || asset is null) throw new NotSupportedException("Pinterest supplied no supported complete video formats.");
        var captions = Captions(Object(videos, "videoListForCaptions"), asset);
        var image = Object(pin, "images_orig");
        if (PostImage(Text(image, "url")) is { } cover) thumbnails.Add(new() { Id = "original-cover", Url = cover, Width = Integer(image, "width"), Height = Integer(image, "height") });
        var preferred = thumbnails.FirstOrDefault();
        return new() { Id = id, Extractor = "pinterest", ArchiveIdentity = new("pinterest", id), Title = title, Description = description, Uploader = uploader,
            DurationSeconds = duration, Media = media, Subtitles = captions, ThumbnailUrl = preferred?.Url.AbsoluteUri, Thumbnails = thumbnails,
            AnonymousPlayback = true, SourcePageUrl = page };
    }
    private static IReadOnlyList<SubtitleTrack> Captions(JsonElement inventory, string asset)
    {
        var captions = new List<SubtitleTrack>(); var languages = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        if (inventory.ValueKind != JsonValueKind.Object) return captions;
        if (inventory.EnumerateObject().Count() > 64) throw new InvalidDataException("Pinterest returned too many caption format records.");
        foreach (var format in inventory.EnumerateObject())
        {
            if (format.Name.StartsWith("__", StringComparison.Ordinal)) continue;
            var urls = Object(format.Value, "captionsUrls"); if (urls.ValueKind != JsonValueKind.Object) continue;
            if (urls.EnumerateObject().Count() > 32) throw new InvalidDataException("Pinterest returned too many caption languages.");
            foreach (var language in urls.EnumerateObject())
            {
                if (language.Name.StartsWith("__", StringComparison.Ordinal) || language.Value.ValueKind == JsonValueKind.Null) continue;
                if (language.Value.ValueKind != JsonValueKind.String || !Regex.IsMatch(language.Name, @"\A[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                    throw new InvalidDataException("Pinterest returned an unsupported caption language/resource.");
                var url = Origin(language.Value.GetString(), "v1.pinimg.com", "v.pinimg.com");
                var expected = "/videos/captions/" + language.Name + "/" + asset[..2] + "/" + asset.Substring(2, 2) + "/" + asset.Substring(4, 2) + "/" + asset + ".vtt";
                if (url.AbsolutePath != expected) throw new InvalidDataException("Pinterest captions belong to another language/video asset.");
                if (languages.TryGetValue(language.Name, out var old) && old != url) throw new InvalidDataException("Pinterest returned conflicting caption resources.");
                languages[language.Name] = url;
            }
        }
        if (languages.Count > 32) throw new InvalidDataException("Pinterest returned too many caption languages.");
        foreach (var (language, url) in languages) captions.Add(new() { Language = language, Url = url, Automatic = true });
        return captions;
    }
    private static (Uri Url, string Asset, bool Hls) Resource(string? value)
    {
        var url = Origin(value, "v1.pinimg.com", "v.pinimg.com");
        var match = Regex.Match(url.AbsolutePath, @"\A/videos/(?<family>iht/hls|iht/expMp4|hls|720p|mc/720p|mc/hls|mc/expMp4|originals)/(?<a>[a-f0-9]{2})/(?<b>[a-f0-9]{2})/(?<c>[a-f0-9]{2})/(?<asset>[a-f0-9]{32})(?:_[A-Za-z0-9]{1,32})?\.(?<ext>mp4|m3u8)\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) throw new NotSupportedException("This Pinterest video resource family needs additional native support.");
        var asset = match.Groups["asset"].Value; var hls = match.Groups["family"].Value.EndsWith("hls", StringComparison.Ordinal);
        if (match.Groups["a"].Value != asset[..2] || match.Groups["b"].Value != asset.Substring(2, 2) || match.Groups["c"].Value != asset.Substring(4, 2) ||
            match.Groups["ext"].Value != (hls ? "m3u8" : "mp4")) throw new InvalidDataException("Pinterest returned inconsistent video resource identity/type.");
        return (url, asset, hls);
    }
    private static Uri Origin(string? value, params string[] hosts) => Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == "https" && hosts.Contains(url.Host, StringComparer.Ordinal) &&
        url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 ? url : throw new InvalidDataException("Pinterest returned an unsupported media origin.");
    private static Uri? VideoImage(string? value, string asset)
    {
        if (value is null) return null;
        var url = Origin(value, "i.pinimg.com");
        var expected = "/videos/thumbnails/originals/" + asset[..2] + "/" + asset.Substring(2, 2) + "/" + asset.Substring(4, 2) + "/" + asset;
        if (!Regex.IsMatch(url.AbsolutePath, @"\A" + Regex.Escape(expected) + @"(?:\.[0-9]+)?\.(?:jpg|png|webp)\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new InvalidDataException("Pinterest supplied another video's poster.");
        return url;
    }
    private static Uri? PostImage(string? value)
    {
        if (value is null) return null;
        var url = Origin(value, "i.pinimg.com");
        return Regex.IsMatch(url.AbsolutePath, @"\A/(?:originals|[0-9]+x)/[a-f0-9]{2}/[a-f0-9]{2}/[a-f0-9]{2}/[a-f0-9]{32}\.(?:jpg|png|webp)\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) ? url : null;
    }
    private static int Dimension(JsonElement value, string field) => Integer(value, field) is > 0 and <= 65535 and var dimension ? dimension :
        throw new InvalidDataException("Pinterest omitted a supported video format dimension.");
    private static bool NumericId(string? value) => value is not null && Regex.IsMatch(value, @"\A[1-9][0-9]{0,29}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}
