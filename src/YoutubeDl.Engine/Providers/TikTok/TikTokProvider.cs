using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.TikTok;

public sealed class TikTokProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("tiktok", "TikTok exposed public video playback", 200);
    public bool CanHandle(Uri url)
    {
        if (!TikTokAddress.IsHost(url)) return false;
        try { _ = TikTokAddress.Parse(url); return true; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return false; }
    }
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => TikTokAddress.Parse(request.Url).Id is { } id ? new("tiktok", id) : null;

    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var agent = string.IsNullOrWhiteSpace(request.UserAgent) ? "YoutubeDlNative/" + NativeEngine.Version : request.UserAgent;
        using var nativeClient = http.WithHeaders(new Dictionary<string, string> { ["User-Agent"] = agent });
        var address = TikTokAddress.Parse(request.Url);
        var document = await nativeClient.GetDocumentAsync(address.Url, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        var final = TikTokAddress.Parse(document.Url);
        if (final.Id is null) throw new NotSupportedException("TikTok did not resolve the share link to an accessible video page.");
        if (address.Id is not null && address.Id != final.Id) throw new InvalidDataException("TikTok redirected to a different video.");
        var page = ParsePage(document.Text, final.Id, document.Url);
        return page with { PlaybackHeaders = new Dictionary<string, string>(page.PlaybackHeaders) { ["User-Agent"] = agent } };
    }
    public static PageMetadata ParsePage(string html, string expectedId, Uri source)
    {
        if (!TikTokAddress.IsId(expectedId) || Encoding.UTF8.GetByteCount(html) > 8 * 1024 * 1024) throw new InvalidDataException("The TikTok page identity or size is invalid.");
        var universal = Script(html, "__UNIVERSAL_DATA_FOR_REHYDRATION__");
        if (universal is null && html.Contains("_wafchallengeid", StringComparison.Ordinal)) throw new NotSupportedException("TikTok requires additional verification that this download engine has not completed.");
        JsonElement item;
        if (universal is not null)
        {
            using var document = JsonDocument.Parse(universal);
            var detail = Object(Object(document.RootElement, "__DEFAULT_SCOPE__"), "webapp.video-detail");
            if (Integer(detail, "statusCode") is not { } status) throw new NotSupportedException("TikTok did not expose a supported video detail response.");
            if (status != 0) throw new InvalidOperationException("TikTok did not make this video available to the current session.");
            var value = Object(Object(detail, "itemInfo"), "itemStruct");
            if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("TikTok did not return the requested video item.");
            item = value.Clone();
        }
        else
        {
            var legacy = Script(html, "SIGI_STATE") ?? throw new NotSupportedException("TikTok did not expose video data. Additional verification or page support may be required.");
            using var document = JsonDocument.Parse(legacy);
            var value = Object(Object(document.RootElement, "ItemModule"), expectedId);
            if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("TikTok did not return the requested video item.");
            item = value.Clone();
        }
        if ((Text(item, "id") ?? Long(item, "id")?.ToString(CultureInfo.InvariantCulture)) != expectedId) throw new InvalidDataException("The TikTok page data belongs to another video.");
        var video = Object(item, "video"); var author = Object(item, "author");
        var description = Text(item, "desc");
        var page = new PageMetadata { Id = expectedId, Extractor = "tiktok", Title = string.IsNullOrWhiteSpace(description) ? "TikTok " + expectedId : description,
            Description = description, Uploader = Text(author, "nickname") ?? Text(author, "uniqueId"), DurationSeconds = Number(video, "duration"),
            ThumbnailUrl = OptionalResource(Text(video, "originCover") ?? Text(video, "cover")), SourcePageUrl = source,
            Thumbnails = new[] { "originCover", "cover", "dynamicCover" }.Select(key => new { Key = key, Url = OptionalResource(Text(video, key)) }).Where(image => image.Url is not null)
                .Select(image => new ThumbnailCandidate { Id = image.Key, Url = new(image.Url!) }).ToArray(),
            PlaybackHeaders = new Dictionary<string, string> { ["Referer"] = "https://www.tiktok.com/" } };
        if (new[] { "privateItem", "secret", "forFriend", "isProhibited", "takeDown", "isReviewing" }.Any(name => True(item, name) || Number(item, name) is > 0))
            return page with { Warnings = ["This TikTok video requires access that the native provider has not established."] };
        if (video.ValueKind != JsonValueKind.Object || Object(item, "imagePost") is { ValueKind: JsonValueKind.Object } images && images.EnumerateObject().Any())
            return page with { Warnings = ["This TikTok post does not expose a supported video. Photo posts are not downloaded as their background music."] };
        if (Protected(video)) return page with { Warnings = ["This TikTok video declares protected media that is not supported."] };
        var media = new List<MediaCandidate>(); var formats = new List<NativeFormat>(); var urls = new HashSet<string>(StringComparer.Ordinal); var ids = new HashSet<string>(StringComparer.Ordinal);
        var unavailableUrls = new HashSet<string>(StringComparer.Ordinal);
        var bitrates = Array(video, "bitrateInfo");
        if (bitrates.Length > 100) throw new InvalidDataException("TikTok returned too many video formats.");
        foreach (var bitrate in bitrates.Where(bitrate => Protected(bitrate) || Text(bitrate, "Format") is { } format && !format.Equals("mp4", StringComparison.OrdinalIgnoreCase)))
            foreach (var value in Array(Object(bitrate, "PlayAddr"), "UrlList"))
                if (value.ValueKind == JsonValueKind.String && Uri.TryCreate(value.GetString(), UriKind.Absolute, out var blocked)) unavailableUrls.Add(blocked.AbsoluteUri);
        foreach (var bitrate in bitrates)
        {
            if (Protected(bitrate) || Text(bitrate, "Format") is { } format && !format.Equals("mp4", StringComparison.OrdinalIgnoreCase)) continue;
            var playback = Object(bitrate, "PlayAddr");
            var quality = Integer(bitrate, "QualityType")?.ToString(CultureInfo.InvariantCulture) ?? (formats.Count + 1).ToString(CultureInfo.InvariantCulture);
            Add("q" + quality + "-" + (Text(bitrate, "CodecType") ?? "video"), FirstUrl(playback), playback, Text(bitrate, "CodecType"),
                Number(bitrate, "Bitrate"), Number(bitrate, "BitrateFPS"), Text(bitrate, "GearName"));
        }
        var structured = Object(video, "PlayAddrStruct");
        Add("play", FirstUrl(structured) ?? Text(video, "playAddr"), structured, Text(video, "codecType"), Number(video, "bitrate"), Number(video, "fps"), Text(video, "definition"));
        Add("download", Text(video, "downloadAddr") ?? FirstUrl(Object(video, "downloadAddr")), video, Text(video, "codecType"), Number(video, "bitrate"), Number(video, "fps"), "Offered download");
        return page with { Media = media, ReportedFormats = formats, Warnings = media.Count == 0 ? ["TikTok did not expose a supported playable video address."] : [] };

        void Add(string id, string? raw, JsonElement facts, string? rawCodec, double? bitrate, double? fps, string? note)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            var url = Resource(raw);
            if (unavailableUrls.Contains(url.AbsoluteUri) || !urls.Add(url.AbsoluteUri)) return;
            if (!Regex.IsMatch(id, @"\A[A-Za-z0-9_-]{1,100}\z")) throw new InvalidDataException("TikTok returned an invalid video format identifier.");
            var baseId = id; for (var suffix = 2; !ids.Add(id); suffix++) id = baseId + "-" + suffix;
            var codec = rawCodec?.ToLowerInvariant() switch { "h264" or "avc1" => "avc1", "h265" or "h265_hvc1" or "hevc" => "hvc1", "av1" => "av01", _ => null };
            var width = PositiveDimension(Integer(facts, "Width") ?? Integer(facts, "width") ?? Integer(video, "width"));
            var height = PositiveDimension(Integer(facts, "Height") ?? Integer(facts, "height") ?? Integer(video, "height"));
            var size = Long(facts, "DataSize") ?? Long(facts, "size");
            var frameRate = fps is > 0 and <= 1000 ? fps : null;
            var bandwidth = bitrate is > 0 ? bitrate : null;
            media.Add(new() { Id = id, Url = url, Kind = MediaKind.Progressive, Extension = "mp4", MimeType = "video/mp4", HasVideo = true, Width = width, Height = height,
                VideoCodec = codec, FramesPerSecond = frameRate, Bandwidth = bandwidth, FileSize = size is > 0 ? size : null, Label = note, Source = "tiktok" });
            formats.Add(new() { Id = id, Url = url, Extension = "mp4", MimeType = "video/mp4", HasVideo = true, Width = width, Height = height,
                VideoCodec = codec, FramesPerSecond = frameRate, Bandwidth = bandwidth, FileSize = size is > 0 ? size : null, Note = note });
        }
    }
    private static int? PositiveDimension(int? value) => value is > 0 and <= 16384 ? value : null;
    private static bool Protected(JsonElement value) => True(value, "isDrm") || True(value, "isEncrypted") || Number(value, "drmType") is > 0 || Number(value, "DRMType") is > 0 ||
        Object(value, "drm") is { ValueKind: JsonValueKind.Object } drm && drm.EnumerateObject().Any();
    private static string? FirstUrl(JsonElement value)
    {
        var urls = Array(value, "UrlList");
        if (urls.Length > 20) throw new InvalidDataException("TikTok returned too many delivery alternatives.");
        return urls.Where(url => url.ValueKind == JsonValueKind.String).Select(url => url.GetString()).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
    }
    private static string? Script(string html, string id)
    {
        var matches = Regex.Matches(html, @"<script\b(?=[^>]{0,8192}\bid\s*=\s*['""]" + Regex.Escape(id) + @"['""])[^>]*>(?<json>[\s\S]*?)</script\s*>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (matches.Count > 1) throw new InvalidDataException("TikTok returned ambiguous video documents.");
        if (matches.Count == 0) return null;
        var text = matches[0].Groups["json"].Value;
        if (Encoding.UTF8.GetByteCount(text) > 6 * 1024 * 1024) throw new InvalidDataException("The TikTok video document exceeds its size limit.");
        return text;
    }
    private static string? OptionalResource(string? value) => string.IsNullOrWhiteSpace(value) ? null : Resource(value).AbsoluteUri;
    private static Uri Resource(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0 ||
            !new[] { "tiktok.com", "tiktokcdn.com", "tiktokcdn-us.com", "tiktokcdn-eu.com", "tiktokv.com", "byteoversea.com", "ibytedtos.com", "ibyteimg.com", "muscdn.com" }
                .Any(host => url.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || url.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("TikTok returned an invalid media or image address.");
        return url;
    }
}
