using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Providers.Vimeo;

public sealed partial class VimeoProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("vimeo", "Vimeo exposed player streams", 200);
    public NativeAuthenticationCapabilities AuthenticationCapabilities=>NativeAuthenticationCapabilities.VideoPassword|NativeAuthenticationCapabilities.AccessToken|NativeAuthenticationCapabilities.OAuth;
    public bool CanHandle(Uri url) => VimeoAddress.IsVimeoHost(url);
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => VimeoCollectionAddress.TryParse(request.Url,out _)?null:new("vimeo", VimeoAddress.ParseFileLink(request.Url)?.VideoId ?? VimeoAddress.Parse(request.Url).VideoId);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        if(VimeoCollectionAddress.TryParse(request.Url,out var collection))return await ReadCollectionAsync(collection!,request,http,ct).ConfigureAwait(false);
        if (VimeoAddress.ParseFileLink(request.Url) is { } file) return await ReadFileLinkAsync(file, request, http, ct).ConfigureAwait(false);
        var address = VimeoAddress.Parse(request.Url);
        var session=http.SessionState("vimeo:"+address.VideoId+":"+address.UnlistedHash,()=>new VimeoSession(http,request,address));
        return await session.ReadAsync(ct).ConfigureAwait(false);
    }
    private static async Task<PageMetadata> ReadFileLinkAsync(VimeoAddress.FileLink file, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, request.Url);
        message.Headers.AcceptEncoding.ParseAdd("identity");
        using var response = await http.SendAsync(message, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var url = MediaUrl((response.RequestMessage?.RequestUri ?? request.Url).AbsoluteUri)!;
        var mime = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
        var kind = PageMediaExtractor.Classify(url, mime);
        var declaredKind = PageMediaExtractor.Classify(new Uri("https://media.invalid/no-extension"), mime);
        if (kind != file.Kind || mime is not null and not "application/octet-stream" && declaredKind != file.Kind ||
            mime is null && PageMediaExtractor.Classify(url) == MediaKind.Unknown)
            throw new NotSupportedException("The Vimeo file link did not return the requested media type. It may have expired or require additional access.");
        var extension = file.Kind == MediaKind.Hls ? "m3u8" : file.Kind == MediaKind.Dash ? "mpd" :
            PageMediaExtractor.ExtensionFrom(new Uri("https://media.invalid/no-extension"), mime) ?? PageMediaExtractor.ExtensionFrom(url, mime);
        if (extension is null) throw new NotSupportedException("The Vimeo file link does not expose a supported media container.");
        var audio = mime?.StartsWith("audio/", StringComparison.Ordinal) == true;
        return new() { Id = file.VideoId, Extractor = "vimeo", Title = file.VideoId, SourcePageUrl = request.Url,
            Media = [new() { Id = "direct", Url = url, Kind = file.Kind, Extension = extension, MimeType = mime, Source = "vimeo:file",
                HasVideo = audio ? false : null, HasAudio = audio ? true : null, FileSize = file.Kind == MediaKind.Progressive ? response.Content.Headers.ContentLength : null }] };
    }

    public static PageMetadata ParseConfiguration(string json, string expectedId, string? preferredVideoCodec = null)
    {
        if (Encoding.UTF8.GetByteCount(json) > 2 * 1024 * 1024)
            throw new InvalidDataException("The Vimeo player configuration exceeds the extraction limit.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The Vimeo configuration is not an object.");
        if (root.TryGetProperty("view", out _) && Integer(root, "view") is not (1 or 3) || HasProtection(root, "error") || HasProtection(Object(root, "request"), "error"))
            throw new NotSupportedException("Vimeo returned an unavailable player view. This item needs another access or playback workflow.");
        var video = Object(root, "video");
        var id = Text(video, "id") ?? ProviderJson.Long(video, "id")?.ToString(CultureInfo.InvariantCulture);
        if (id != expectedId) throw new InvalidDataException("The Vimeo player response belongs to a different video.");
        var request = Object(root, "request");
        var files = Object(request, "files");
        var media = new List<MediaCandidate>();
        var reported = new List<NativeFormat>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var formats = Array(files, "progressive");
        if (formats.Length > 1000 || Array(request, "text_tracks").Length > 256)
            throw new InvalidDataException("The Vimeo configuration contains too many formats or caption tracks.");
        foreach (var format in formats)
        {
            var url = MediaUrl(Text(format, "url"));
            if (url is null) continue;
            var mimeText = Text(format, "mime") ?? "video/mp4";
            if (!MediaTypeHeaderValue.TryParse(mimeText, out var mime) || mime.MediaType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) != true)
                throw new InvalidDataException("Vimeo returned an unsupported progressive MIME type.");
            var codecs = mime.Parameters.FirstOrDefault(parameter => parameter.Name.Equals("codecs", StringComparison.OrdinalIgnoreCase))?.Value?.Trim('"').Split(',', StringSplitOptions.TrimEntries) ?? [];
            var candidate = new MediaCandidate
            {
                Id = UniqueId(Text(format, "quality") ?? "progressive", ids), Url = url, Source = "vimeo", Kind = MediaKind.Progressive,
                MimeType = mime.MediaType, Extension = PageMediaExtractor.ExtensionFrom(url, mime.MediaType),
                Height = PositiveInteger(format, "height"), Width = PositiveInteger(format, "width"), HasVideo = true, HasAudio = true,
                FramesPerSecond = Number(format, "fps") is > 0 and var fps ? fps : null,
                FileSize = ProviderJson.Long(format, "size") is > 0 and var size ? size : null,
                VideoCodec = codecs.FirstOrDefault(IsVideoCodec) ?? Text(format, "codec"), AudioCodec = codecs.FirstOrDefault(codec => !IsVideoCodec(codec)), Label = Text(format, "quality")
            };
            Add(candidate, Protected(format, url), media, reported);
        }
        var preferAvc = preferredVideoCodec?.Equals("h264", StringComparison.OrdinalIgnoreCase) == true;
        AddManifest(files, "hls", MediaKind.Hls, media, reported, ids, preferAvc);
        AddManifest(files, "dash", MediaKind.Dash, media, reported, ids, preferAvc);
        AddManifest(files, "hls_cenc", MediaKind.Hls, media, reported, ids, preferAvc);
        var subtitles = Array(request, "text_tracks").Select(track => new
        {
            Url = MediaUrl(Text(track, "url")), Language = Text(track, "lang") ?? ""
        }).Where(track => track.Url is not null).Select(track => new SubtitleTrack { Url = track.Url!, Language = track.Language, Extension = "vtt" }).ToArray();
        var warnings = reported.Where(format => format.Availability != FormatAvailability.Ready).Select(format => format.UnavailableReason!).Distinct(StringComparer.Ordinal).ToList();
        var thumbnails = new List<ThumbnailCandidate>();
        if (Object(video, "thumbs") is { ValueKind: JsonValueKind.Object } images)
            foreach (var image in images.EnumerateObject())
                if (image.Value.ValueKind == JsonValueKind.String && MediaUrl(image.Value.GetString()) is { } imageUrl)
                    thumbnails.Add(new() { Id = image.Name, Url = imageUrl, Width = int.TryParse(image.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var width) && width > 0 ? width : null });
        var allProtected = media.Count == 0 && reported.Any(format => format.Availability == FormatAvailability.DrmProtected);
        if (reported.Count == 0) warnings.Add("The Vimeo player did not expose downloadable streams. The video may require additional access or player support.");
        return new PageMetadata
        {
            Id = id, Extractor = "vimeo", Title = Text(video, "title") ?? id, DurationSeconds = Number(video, "duration"),
            Uploader = Text(Object(video, "owner"), "name"), ThumbnailUrl = MediaUrl(Text(video, "thumbnail_url"))?.AbsoluteUri, Thumbnails = thumbnails, Media = media, ReportedFormats = reported, Subtitles = subtitles,
            SourcePageUrl = new Uri("https://vimeo.com/" + id), Warnings = warnings, DrmRefused = allProtected,
            RefusalReason = allProtected ? "Vimeo supplied only protected media, which the native engine cannot download." : null
        };
    }
    private static void AddManifest(JsonElement files, string key, MediaKind kind, List<MediaCandidate> media, List<NativeFormat> reported, HashSet<string> ids, bool preferAvc)
    {
        var manifest = Object(files, key);
        var cdns = Object(manifest, "cdns");
        var selected = Text(manifest, "default_cdn");
        if (selected is null || cdns.ValueKind != JsonValueKind.Object || !cdns.TryGetProperty(selected, out var cdn)) return;
        if (cdn.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Vimeo returned an invalid selected CDN descriptor.");
        var avc = MediaUrl(Text(cdn, "avc_url"));
        var seen = new HashSet<Uri>();
        foreach (var field in new[] { "url", "avc_url", "fallback_url" })
        {
            var url = MediaUrl(Text(cdn, field));
            if (url is null || !seen.Add(url)) continue;
            var suffix = field == "url" ? "" : field == "avc_url" ? "-avc" : "-fallback";
            var candidate = new MediaCandidate { Id = UniqueId(key.Replace('_', '-') + suffix, ids), Url = url, Kind = kind, Source = "vimeo",
                Extension = kind == MediaKind.Hls ? "m3u8" : "mpd", HasVideo = true, HasAudio = true, VideoCodec = url == avc ? "avc1" : null,
                IsFallback = field == "fallback_url" || preferAvc && avc is not null && url != avc };
            Add(candidate, key == "hls_cenc" || Protected(manifest, url) || Protected(cdn, url), media, reported);
        }
    }
    private static void Add(MediaCandidate candidate, bool protection, List<MediaCandidate> media, List<NativeFormat> reported)
    {
        if (!protection) media.Add(candidate);
        reported.Add(new NativeFormat { Id = candidate.Id!, Url = protection ? null : candidate.Url, MimeType = candidate.MimeType, Width = candidate.Width, Height = candidate.Height,
            Extension = candidate.Extension, FileSize = candidate.FileSize, FramesPerSecond = candidate.FramesPerSecond, HasVideo = true, HasAudio = true,
            VideoCodec = candidate.VideoCodec, AudioCodec = candidate.AudioCodec, Note = candidate.Label ?? "vimeo", Protocol = candidate.Kind == MediaKind.Hls ? "m3u8" : candidate.Kind == MediaKind.Dash ? "http_dash_segments" : "https",
            Availability = protection ? FormatAvailability.DrmProtected : FormatAvailability.Ready, UnavailableReason = protection ? "Vimeo supplied protected media that is unavailable for native downloading." : null });
    }
    private static string UniqueId(string value, HashSet<string> ids)
    {
        if (value.Length is 0 or > 128 || value.Any(char.IsControl)) throw new InvalidDataException("Vimeo returned an invalid format identity.");
        var id = value; for (var count = 2; !ids.Add(id); count++) id = value + "-" + count.ToString(CultureInfo.InvariantCulture); return id;
    }
    private static bool IsVideoCodec(string codec) => new[] { "avc", "h264", "hvc", "hev", "hevc", "vp9", "vp09", "av01", "av1" }.Any(prefix => codec.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    private static bool Protected(JsonElement value, Uri url) => HasProtection(value, "drm") || HasProtection(value, "key_systems") ||
        url.AbsolutePath.Contains("/drm/cenc,", StringComparison.OrdinalIgnoreCase) || url.AbsolutePath.Contains("/drm/cbcs,", StringComparison.OrdinalIgnoreCase);
    private static bool HasProtection(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) && field.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.False => false, JsonValueKind.Object => field.EnumerateObject().Any(), JsonValueKind.Array => field.GetArrayLength() > 0,
        JsonValueKind.String => !string.IsNullOrWhiteSpace(field.GetString()), _ => true
    };
    private static Uri? MediaUrl(string? value)
    {
        if (value is null) return null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme != "https" || !string.IsNullOrEmpty(url.UserInfo) || !url.IsDefaultPort || url.Fragment.Length > 0)
            throw new InvalidDataException("Vimeo returned an invalid media address.");
        return url;
    }
    private static JsonElement Object(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
    private static JsonElement[] Array(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
    private static string? Text(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static double? Number(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number >= 0 ? number : null;
    private static int? Integer(JsonElement element, string key) => Number(element, key) is { } value && value <= int.MaxValue && Math.Truncate(value) == value ? (int)value : null;
    private static int? PositiveInteger(JsonElement element, string key) => Integer(element, key) is > 0 and var value ? value : null;
}
