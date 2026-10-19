using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Wikimedia;

public sealed class WikimediaProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("wikimedia.org", "Wikimedia public file audio and video", 200);
    public bool CanHandle(Uri url)
    {
        if (!WikimediaAddress.IsHost(url)) return false;
        static bool FileTitle(string value) => new[] { "File:", "Image:", "Special:FilePath/", "Special:Redirect/file/" }.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (url.AbsolutePath.StartsWith("/wiki/", StringComparison.Ordinal)) return FileTitle(Uri.UnescapeDataString(url.AbsolutePath[6..]));
        return url.AbsolutePath == "/w/index.php" && url.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
            .Any(pair => pair.Length == 2 && Uri.UnescapeDataString(pair[0]) == "title" && FileTitle(Uri.UnescapeDataString(pair[1].Replace('+', ' '))));
    }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = WikimediaAddress.Parse(request.Url); var originalOnly = address.OriginalOnly; var visited = new HashSet<string>(StringComparer.Ordinal);
        using var session = http.WithHeaders(new Dictionary<string, string> { ["User-Agent"] = string.IsNullOrWhiteSpace(request.UserAgent) ? "YoutubeDlNative/" + NativeEngine.Version + " (public media client)" : request.UserAgent });
        for (var depth = 0; depth < 3; depth++)
        {
            if (!visited.Add(address.Host + "/" + address.Title)) throw new InvalidDataException("Wikimedia repeated a shared-repository reference.");
            var response = await session.GetDocumentAsync(address.Api, 8 * 1024 * 1024, ct).ConfigureAwait(false);
            if (response.Url != address.Api) throw new InvalidDataException("Wikimedia redirected the metadata request.");
            using var json = JsonDocument.Parse(response.Text); var root = json.RootElement; var query = Object(root, "query");
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Wikimedia returned an invalid metadata document.");
            if (root.TryGetProperty("error", out _) || root.TryGetProperty("continue", out _)) throw new NotSupportedException("Wikimedia did not return one complete accessible file response.");
            var title = ResolveTitle(query, address.Title); var pages = Array(query, "pages");
            if (pages.Length != 1 || Integer(pages[0], "ns") != 6 || Text(pages[0], "title") != title) throw new InvalidDataException("Wikimedia returned another file page.");
            var page = pages[0]; var entries = Array(page, "videoinfo");
            if (entries.Length != 1 || Hidden(entries[0], "filehidden") || True(page, "invalid")) throw new NotSupportedException("This Wikimedia file is unavailable.");
            var data = entries[0]; var canonical = Text(data, "canonicaltitle") ?? title; WikimediaAddress.ValidateTitle(canonical);
            if (WikimediaAddress.FileName(canonical) != WikimediaAddress.FileName(title)) throw new InvalidDataException("Wikimedia returned another canonical file name.");
            var description = DescriptionAddress(Text(data, "descriptionurl"), canonical);
            if (Text(page, "imagerepository") == "shared")
            {
                if (description.Host == address.Host) throw new InvalidDataException("The shared file did not identify its repository.");
                address = description; continue;
            }
            if (Text(page, "imagerepository") != "local" || True(page, "missing") || Long(page, "pageid") is not > 0 || description.Host != address.Host)
                throw new NotSupportedException("The Wikimedia file does not expose one supported local repository identity.");
            ct.ThrowIfCancellationRequested();
            return Read(data, canonical, Long(page, "pageid")!.Value, description, originalOnly, request.VideoOnly);
        }
        throw new NotSupportedException("The shared Wikimedia repository chain exceeds its limit.");
    }
    private static PageMetadata Read(JsonElement data, string title, long pageId, WikimediaAddress address, bool originalOnly, bool videoOnly)
    {
        var mediaType = Text(data, "mediatype");
        if (mediaType is not ("VIDEO" or "AUDIO")) throw new NotSupportedException("This Wikimedia file is not audio or video.");
        var filename = WikimediaAddress.FileName(title); var original = Upload(Text(data, "url"));
        var originalParts = original.AbsolutePath.Split('/');
        var storage = address.Storage;
        if (originalParts.Length != 6 || originalParts[1] != storage.Family || originalParts[2] != storage.Project || !Regex.IsMatch(originalParts[3], @"\A[0-9a-f]\z") || !Regex.IsMatch(originalParts[4], @"\A[0-9a-f]{2}\z") || Uri.UnescapeDataString(originalParts[5]) != filename)
            throw new InvalidDataException("Wikimedia's original resource does not match the selected file.");
        var duration = Number(data, "duration"); if (duration is not > 0) throw new InvalidDataException("Wikimedia did not report a complete positive media duration.");
        var derivatives = Array(data, "derivatives"); if (derivatives.Length > 256) throw new InvalidDataException("The Wikimedia format inventory exceeds its limit.");
        var originalRows = derivatives.Where(item => Text(item, "transcodekey") is not { Length: > 0 } && Text(item, "src") is { } raw && Upload(raw).AbsolutePath == original.AbsolutePath).ToArray();
        if (originalRows.Length > 1) throw new InvalidDataException("Wikimedia returned repeated original descriptors.");
        var originalDescriptor = originalRows.FirstOrDefault();
        var originalMime = Text(originalDescriptor, "type") ?? Text(data, "mime") ?? "";
        var originalFacts = Facts(originalMime, mediaType == "VIDEO");
        if (originalFacts.Video != (mediaType == "VIDEO")) throw new InvalidDataException("Wikimedia returned contradictory original media types.");
        var metadata = Array(data, "metadata");
        var originalAudio = mediaType == "AUDIO" ? true : originalFacts.Audio ?? (MetaArray(metadata, "audio").Length > 0 ? true : null);
        var formats = new List<MediaCandidate>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        Add(originalDescriptor, "original", original, originalMime, true);
        if (!originalOnly)
            foreach (var derivative in derivatives)
            {
                var key = Text(derivative, "transcodekey"); if (string.IsNullOrEmpty(key)) continue;
                if (!Regex.IsMatch(key, @"\A[A-Za-z0-9_.-]{1,100}\z", RegexOptions.CultureInvariant)) throw new InvalidDataException("Wikimedia returned an invalid transcode key.");
                var url = Upload(Text(derivative, "src"));
                var expected = "/" + originalParts[1] + "/" + originalParts[2] + "/transcoded/" + originalParts[3] + "/" + originalParts[4] + "/" + filename + "/" + filename + "." + key;
                if (Uri.UnescapeDataString(url.AbsolutePath) != expected) throw new InvalidDataException("Wikimedia returned a rendition for another file.");
                Add(derivative, key, url, Text(derivative, "type") ?? "", false);
            }
        var captions = new List<SubtitleTrack>(); var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Array(data, "timedtext"))
        {
            if (captions.Count >= 256) throw new InvalidDataException("The Wikimedia caption inventory exceeds its limit.");
            if (Text(item, "kind") is not ("subtitles" or "captions")) continue;
            var language = Text(item, "srclang"); var mime = Text(item, "type");
            if (language is null || !Regex.IsMatch(language, @"\A[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*\z") || !languages.Add(language) || mime is not ("text/vtt" or "application/x-subrip")) throw new InvalidDataException("Wikimedia returned an unsupported caption descriptor.");
            if (!Uri.TryCreate(Text(item, "src"), UriKind.Absolute, out var url) || url.Scheme != "https" || url.Host != address.Host || url.AbsolutePath != "/w/api.php" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0)
                throw new InvalidDataException("Wikimedia returned a caption from another origin.");
            var query = WikimediaAddress.Query(url); var extension = mime == "text/vtt" ? "vtt" : "srt";
            if (query.GetValueOrDefault("action") != "timedtext" || query.GetValueOrDefault("trackformat") != extension || !SameLanguage(query.GetValueOrDefault("lang"), language) ||
                query.GetValueOrDefault("title") is not { } captionTitle || !SameNamespace(captionTitle, title) || WikimediaAddress.FileName(captionTitle) != filename || query.Keys.Any(key => key is not ("action" or "title" or "lang" or "trackformat" or "origin")))
                throw new InvalidDataException("Wikimedia's caption address does not match the selected file and language.");
            captions.Add(new() { Url = url, Language = language, Extension = extension });
        }
        var extra = Object(data, "extmetadata"); var display = Plain(Text(Object(extra, "ObjectName"), "value"));
        return new() { Id = pageId.ToString(CultureInfo.InvariantCulture), Extractor = "wikimedia.org", ArchiveIdentity = new("wikimedia:page", address.Host + "/" + pageId.ToString(CultureInfo.InvariantCulture)),
            Title = string.IsNullOrWhiteSpace(display) ? title[(title.IndexOf(':') + 1)..] : display, Description = Plain(Text(Object(extra, "ImageDescription"), "value")),
            Uploader = Plain(Text(Object(extra, "Artist"), "value")) ?? (Hidden(data, "userhidden") ? null : Text(data, "user")), DurationSeconds = duration,
            Media = formats, Subtitles = captions, Thumbnails = Thumbnails(data, originalParts, filename, address.Host, mediaType == "AUDIO"), SourcePageUrl = address.Page };

        void Add(JsonElement item, string id, Uri url, string mime, bool source)
        {
            if (!ids.Add(id)) throw new InvalidDataException("Wikimedia repeated a format identity.");
            var facts = Facts(mime, mediaType == "VIDEO"); var extension = Path.GetExtension(url.AbsolutePath).TrimStart('.').ToLowerInvariant();
            var kind = PageMediaExtractor.Classify(url, facts.Mime);
            if (kind == MediaKind.Unknown || extension is not ("webm" or "mp4" or "mov" or "ogv" or "ogg" or "mp3" or "wav" or "flac" or "m3u8")) throw new NotSupportedException("Wikimedia returned an unsupported media container.");
            var audio = source ? originalAudio : facts.Audio;
            formats.Add(new() { Id = id, Url = url, Kind = kind, Extension = extension, MimeType = facts.Mime, HasVideo = facts.Video, HasAudio = audio,
                IsCompleteMedia = source || !facts.Video || audio != false || originalAudio == false, IsFallback = !source && facts.Video && audio == false && originalAudio != false && !videoOnly,
                Width = Integer(item, "width") ?? (source ? Integer(data, "width") : null), Height = Integer(item, "height") ?? (source ? Integer(data, "height") : null),
                Bandwidth = Number(item, "bandwidth") ?? (source ? MetaNumber(metadata, "bitrate") : null), VideoCodec = facts.VideoCodec, AudioCodec = facts.AudioCodec,
                FramesPerSecond = source ? MetaNumber(MetaArray(metadata, "video"), "frame_rate") : null, FileSize = source ? Long(data, "size") : null, Source = "wikimedia.org",
                VerificationDemuxer = kind == MediaKind.Hls ? "mpegts" : extension switch { "webm" => "matroska", "mp4" or "mov" => "mov", "ogv" or "ogg" => "ogg", "mp3" => "mp3", "wav" => "wav", "flac" => "flac", _ => null },
                VerificationDurationSeconds = duration, VerificationFileSize = source ? Long(data, "size") : null });
        }
    }
    private static (string Mime, bool Video, bool? Audio, string? VideoCodec, string? AudioCodec) Facts(string raw, bool fileVideo)
    {
        if (!MediaTypeHeaderValue.TryParse(raw, out var type) || type.MediaType is null) throw new InvalidDataException("Wikimedia returned an invalid media type.");
        var mime = type.MediaType; var video = mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase) || mime is "application/ogg" or "application/vnd.apple.mpegurl" or "application/x-mpegurl" && fileVideo;
        if (!mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase) && !mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) && mime is not ("application/ogg" or "application/vnd.apple.mpegurl" or "application/x-mpegurl"))
            throw new NotSupportedException("Wikimedia returned a non-media rendition type.");
        var codecs = type.Parameters.FirstOrDefault(value => value.Name.Equals("codecs", StringComparison.OrdinalIgnoreCase))?.Value?.Trim('"').Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
        var audioCodec = codecs.FirstOrDefault(codec => codec is "opus" or "vorbis" or "mp3" or "flac" || codec.StartsWith("mp4a", StringComparison.OrdinalIgnoreCase)) ?? (mime == "audio/mpeg" ? "mp3" : mime == "audio/flac" ? "flac" : null);
        var videoCodec = codecs.FirstOrDefault(codec => codec is "theora" or "vp8" or "vp9" or "mp4v" or "av1" || codec.StartsWith("avc", StringComparison.OrdinalIgnoreCase) || codec.StartsWith("av01", StringComparison.OrdinalIgnoreCase));
        bool? audio = mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) || !fileVideo ? true : codecs.Length == 0 ? null : audioCodec is not null;
        return (mime, video, audio, videoCodec, audioCodec);
    }
    private static string ResolveTitle(JsonElement query, string expected)
    {
        foreach (var name in new[] { "normalized", "redirects" })
        {
            var mappings = Array(query, name); if (mappings.Length > 16) throw new InvalidDataException("The Wikimedia title redirect chain exceeds its limit.");
            foreach (var entry in mappings)
            {
                if (Text(entry, "from") != expected || Text(entry, "to") is not { } next) throw new InvalidDataException("Wikimedia returned an unrelated title normalization or redirect.");
                WikimediaAddress.ValidateTitle(next); expected = next;
            }
        }
        return expected;
    }
    private static WikimediaAddress DescriptionAddress(string? raw, string title)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || !WikimediaAddress.IsHost(url) || url.Scheme != "https" || !url.AbsolutePath.StartsWith("/wiki/", StringComparison.Ordinal)) throw new InvalidDataException("Wikimedia returned an invalid file description address.");
        var declared = Uri.UnescapeDataString(url.AbsolutePath[6..]).Replace('_', ' ');
        if (!SameNamespace(declared, title) || WikimediaAddress.FileName(declared) != WikimediaAddress.FileName(title)) throw new InvalidDataException("The file description address belongs to another file.");
        return new(url.Host, "File:" + declared[(declared.IndexOf(':') + 1)..]);
    }
    private static Uri Upload(string? raw, bool thumbnail = false) => Uri.TryCreate(raw, UriKind.Absolute, out var url) && url.Scheme == "https" && (url.Host == "upload.wikimedia.org" || thumbnail && url.Host == "thumb.wikimedia.org") && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0
        ? url : throw new InvalidDataException("Wikimedia returned an unverified media origin.");
    private static IReadOnlyList<ThumbnailCandidate> Thumbnails(JsonElement data, string[] original, string filename, string host, bool audio)
    {
        var result = new List<ThumbnailCandidate>();
        // The API reports display dimensions while the CDN can serve a larger bucket.
        if (Text(data, "thumburl") is { } raw) Add(raw, "preview");
        var responsive = Object(data, "responsiveUrls");
        if (responsive.ValueKind == JsonValueKind.Object)
            foreach (var pair in responsive.EnumerateObject())
            {
                if (!double.TryParse(pair.Name, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var factor) || factor is <= 0 or > 4 || pair.Value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Wikimedia returned an invalid responsive thumbnail.");
                // Responsive factors refer to the requested display size, not necessarily
                // the CDN bucket returned for the primary image. Do not invent pixel sizes.
                Add(pair.Value.GetString()!, "preview-" + pair.Name);
            }
        return result;
        void Add(string raw, string id)
        {
            if (result.Count >= 16) throw new InvalidDataException("Wikimedia returned too many thumbnail variants.");
            if (audio && Uri.TryCreate(raw, UriKind.Absolute, out var icon) && icon.Scheme == "https" && icon.Host == host && icon.IsDefaultPort && icon.UserInfo.Length == 0 && icon.Fragment.Length == 0 &&
                Regex.IsMatch(icon.AbsolutePath, @"\A/w/resources/assets/file-type-icons/fileicon-(?:ogg|mp3|wav|flac|audio)\.png\z", RegexOptions.CultureInvariant))
            { result.Add(new() { Url = icon, Id = id }); return; }
            var url = Upload(raw, thumbnail: true); var path = Uri.UnescapeDataString(url.AbsolutePath);
            if (!path.StartsWith("/" + original[1] + "/" + original[2] + "/thumb/" + original[3] + "/" + original[4] + "/" + filename + "/", StringComparison.Ordinal)) throw new InvalidDataException("Wikimedia returned another file's thumbnail.");
            result.Add(new() { Url = url, Id = id });
        }
    }
    private static bool Hidden(JsonElement value, string key) => value.TryGetProperty(key, out var field) && field.ValueKind != JsonValueKind.False;
    private static bool SameLanguage(string? apiCode, string label) => string.Equals(apiCode, label, StringComparison.OrdinalIgnoreCase) || (apiCode?.ToLowerInvariant(), label.ToLowerInvariant()) is
        ("zh-cn", "zh-hans-cn") or ("zh-tw", "zh-hant-tw");
    private static bool SameNamespace(string value, string canonical) => value.StartsWith("File:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("Image:", StringComparison.OrdinalIgnoreCase) ||
        value.IndexOf(':') > 0 && canonical.IndexOf(':') > 0 && value[..value.IndexOf(':')] == canonical[..canonical.IndexOf(':')];
    private static JsonElement Meta(JsonElement[] items, string key) => items.FirstOrDefault(item => Text(item, "name") == key) is var item && item.ValueKind == JsonValueKind.Object && item.TryGetProperty("value", out var value) ? value : default;
    private static JsonElement[] MetaArray(JsonElement[] items, string key) => Meta(items, key) is var value && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
    private static double? MetaNumber(JsonElement[] items, string key) { var item = items.FirstOrDefault(item => Text(item, "name") == key); return Number(item, "value"); }
    private static string? Plain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Length > 65536) throw new InvalidDataException("The Wikimedia display metadata exceeds its limit.");
        value = Regex.Replace(value, @"<(?:script|style)\b[^>]*>.*?</(?:script|style)\s*>|<!--.*?-->", "", RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        return WebUtility.HtmlDecode(Regex.Replace(value, "<[^>]*>", " ", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))).Trim();
    }
}
