using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Protocols.Sabr;

namespace YoutubeDl.Engine.Providers.YouTube;

/// <summary>Independent reader for the player response returned by YouTube pages.</summary>
public static class YouTubePlayerParser
{
    public static PageMetadata ParsePage(string html, string expectedVideoId, string? playlistId = null,
        IYouTubePlayerSession? player = null, CancellationToken ct = default)
    {
        var page = ParseResponse(YouTubePageJson.ReadPlayerResponse(html), expectedVideoId, YouTubePageJson.ReadPlayerScriptUrl(html), playlistId, player, ct);
        var initial = html.Contains("chapterRenderer", StringComparison.Ordinal) || html.Contains("macroMarkersListItemRenderer", StringComparison.Ordinal)
            ? YouTubePageJson.TryReadInitialData(html) : null;
        return page with { Chapters = YouTubeChapterParser.Parse(initial, page.DurationSeconds, page.Description) };
    }

    public static PageMetadata ParseResponse(string json, string expectedVideoId, Uri? playerScriptUrl = null, string? playlistId = null,
        IYouTubePlayerSession? player = null, CancellationToken ct = default, SabrPlaybackSession? sabrSession = null)
    {
        if (Encoding.UTF8.GetByteCount(json) > 2 * 1024 * 1024)
            throw new InvalidDataException("The YouTube player response exceeds the extraction limit.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var playability = Object(root, "playabilityStatus");
        var status = Text(playability, "status");
        if (status != "OK")
            throw new InvalidOperationException("YouTube: " + (Text(playability, "reason") ??
                (status is null ? "The player response did not include a playback status." : "Playback is unavailable (" + status + ").")));
        var details = Object(root, "videoDetails");
        var microformat = Object(Object(root, "microformat"), "playerMicroformatRenderer");
        var videoId = Text(details, "videoId");
        if (videoId != expectedVideoId) throw new InvalidDataException("The YouTube response belongs to a different video.");
        if (sabrSession is not null && sabrSession.VideoId != videoId) throw new InvalidDataException("The SABR playback context belongs to a different video.");
        var streaming = Object(root, "streamingData");
        var hasSabr = !string.IsNullOrWhiteSpace(Text(streaming, "serverAbrStreamingUrl"));
        var media = new List<MediaCandidate>();
        var reported = new List<NativeFormat>();
        ReadFormats(streaming, "formats", combined: true, hasSabr, media, reported, player, ct, sabrSession);
        ReadFormats(streaming, "adaptiveFormats", combined: false, hasSabr, media, reported, player, ct, sabrSession);
        AddManifest(streaming, "hlsManifestUrl", MediaKind.Hls, media, reported);
        AddManifest(streaming, "dashManifestUrl", MediaKind.Dash, media, reported);
        var warnings = reported.Where(format => format.Availability != FormatAvailability.Ready)
            .Select(format => format.UnavailableReason!).Distinct(StringComparer.Ordinal).ToArray();
        if (reported.Count == 0) warnings = ["YouTube did not expose media formats in this player response."];
        return new PageMetadata
        {
            Id = videoId,
            Extractor = "youtube",
            Title = Text(details, "title") ?? videoId,
            Uploader = Text(details, "author"),
            Description = Text(details, "shortDescription"),
            DurationSeconds = Number(details, "lengthSeconds"),
            UploadDate = MetadataFacts.Date(Text(microformat, "uploadDate") ?? Text(microformat, "publishDate")),
            ViewCount = Long(details, "viewCount"),
            AgeLimit = microformat.ValueKind == JsonValueKind.Object && microformat.TryGetProperty("isFamilySafe", out var familySafe) && familySafe.ValueKind is JsonValueKind.True or JsonValueKind.False ? familySafe.GetBoolean() ? 0 : 18 : null,
            MetadataFields = new Dictionary<string, object?> { ["channel_id"] = Text(details, "channelId") },
            IsLive = Boolean(details, "isLive") || Boolean(Object(Object(Object(root, "microformat"), "playerMicroformatRenderer"), "liveBroadcastDetails"), "isLiveNow"),
            ThumbnailUrl = ReadThumbnail(details),
            Thumbnails = ReadThumbnails(details),
            PlayerScriptUrl = playerScriptUrl,
            PlaylistId = playlistId,
            Media = media,
            ReportedFormats = reported,
            Subtitles = ReadSubtitles(root),
            Chapters = YouTubeChapterParser.Parse(null, Number(details, "lengthSeconds"), Text(details, "shortDescription")),
            Warnings = warnings
        };
    }

    private static void ReadFormats(JsonElement streaming, string key, bool combined, bool hasSabr,
        List<MediaCandidate> media, List<NativeFormat> reported, IYouTubePlayerSession? player, CancellationToken ct, SabrPlaybackSession? sabrSession)
    {
        var formats = Array(streaming, key);
        if (formats.Length > 1000) throw new InvalidDataException("YouTube returned too many formats.");
        foreach (var format in formats)
        {
            ct.ThrowIfCancellationRequested();
            var itag = Text(format, "itag") ?? Number(format, "itag")?.ToString(CultureInfo.InvariantCulture);
            var mimeText = Text(format, "mimeType");
            if (itag is null || !MediaTypeHeaderValue.TryParse(mimeText, out var mime) || mime.MediaType is null)
                throw new InvalidDataException("YouTube returned an invalid format identifier or MIME type.");
            var mimeType = mime.MediaType;
            var audioOnly = mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);
            if (!audioOnly && !mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) continue;
            var hasAudio = audioOnly || combined;
            var codecs = mime.Parameters.FirstOrDefault(parameter => parameter.Name.Equals("codecs", StringComparison.OrdinalIgnoreCase))?
                .Value?.Trim('"').Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
            var audioTrack = Object(format, "audioTrack");
            var audioId = Text(audioTrack, "id");
            var extraTags = Text(format, "xtags") ?? "";
            var drc = Boolean(format, "isDrc") || YouTubeAudioTags.Enabled(extraTags, "drc");
            var voiceBoost = Boolean(format, "isVb") || YouTubeAudioTags.Enabled(extraTags, "vb");
            var id = audioId is null ? itag : itag + ":" + audioId;
            if (drc) id += "-drc";
            if (voiceBoost) id += "-vb";
            SabrStreamDescriptor? sabr = null;
            YouTubeStreamUrl stream;
            if (Array(format, "drmFamilies").Length > 0 || Text(format, "drmTrackType") is not null)
                stream = new(null, FormatAvailability.DrmProtected, "This YouTube format is protected and is not available for native downloading.");
            else
            {
                try { stream = YouTubeStreamUrl.Read(format, hasSabr, player, ct); }
                catch (Exception ex) when (player is not null && ex is InvalidDataException or NotSupportedException or ObjectDisposedException)
                {
                    stream = new(null, FormatAvailability.PlayerChallengeRequired, "The YouTube player transformation could not resolve this format.");
                }
            }
            if (stream.Availability == FormatAvailability.AdaptiveTransportRequired && sabrSession is not null &&
                (!mimeType.EndsWith("/webm", StringComparison.OrdinalIgnoreCase) || sabrSession.SupportsWebM) &&
                uint.TryParse(itag, NumberStyles.None, CultureInfo.InvariantCulture, out var formatNumber) && formatNumber > 0 && Long(format, "lastModified") is > 0 and var modified)
            {
                sabr = new(sabrSession, new(formatNumber, (ulong)modified, extraTags))
                { AudioTrack = audioOnly ? new(drc && !voiceBoost, audioId, voiceBoost, Integer(format, "audioChannels")) : null };
                stream = new(sabrSession.Endpoint, FormatAvailability.Ready, null);
            }
            else if (stream.Availability == FormatAvailability.AdaptiveTransportRequired && sabrSession is { SupportsWebM: false } && mimeType.EndsWith("/webm", StringComparison.OrdinalIgnoreCase))
                stream = new(null, FormatAvailability.AdaptiveTransportRequired, "This YouTube playback session cannot deliver WebM. Choose an available MP4 quality.");
            var extension = mimeType switch
            {
                "audio/mp4" => "m4a", "video/mp4" => "mp4", "video/webm" or "audio/webm" => "webm", _ => null
            };
            var native = new NativeFormat
            {
                Id = id, Url = stream.Url, MimeType = mimeType, Extension = extension,
                Height = Integer(format, "height"), Width = Integer(format, "width"), Bandwidth = Number(format, "bitrate"),
                FramesPerSecond = Number(format, "fps"), FileSize = Long(format, "contentLength"),
                Note = Text(format, "qualityLabel") ?? Text(format, "audioQuality"), AudioOnly = audioOnly,
                HasAudio = hasAudio, HasVideo = !audioOnly,
                AudioCodec = audioOnly ? codecs.FirstOrDefault() : combined ? codecs.Skip(1).FirstOrDefault() : null,
                VideoCodec = audioOnly ? null : codecs.FirstOrDefault(),
                AudioLanguage = audioId?.Split('.')[0], IsDefaultAudio = audioId is null || Boolean(audioTrack, "audioIsDefault"),
                Protocol = sabr is null ? "https" : "sabr", Availability = stream.Availability, UnavailableReason = stream.Reason
            };
            reported.Add(native);
            if (stream.Url is not null)
                media.Add(new MediaCandidate
                {
                    Id = id, Url = stream.Url, Extension = extension, Kind = sabr is null ? MediaKind.Progressive : MediaKind.Sabr, Source = "youtube", Sabr = sabr,
                    MimeType = mimeType, Width = native.Width, Height = native.Height, Bandwidth = native.Bandwidth,
                    HasAudio = hasAudio, HasVideo = !audioOnly, AudioLanguage = native.AudioLanguage, IsDefaultAudio = native.IsDefaultAudio,
                    Label = native.Note, AudioCodec = native.AudioCodec, VideoCodec = native.VideoCodec,
                    FramesPerSecond = native.FramesPerSecond, FileSize = native.FileSize
                });
        }
    }

    private static void AddManifest(JsonElement streaming, string key, MediaKind kind,
        List<MediaCandidate> media, List<NativeFormat> reported)
    {
        var raw = Text(streaming, key);
        if (raw is null) return;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.Scheme != "https" ||
            !(YouTubeAddress.IsYouTubeHost(url) || url.Host.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("YouTube returned an invalid manifest address.");
        var id = kind == MediaKind.Hls ? "hls" : "dash";
        media.Add(new MediaCandidate { Id = id, Url = url, Kind = kind, Source = "youtube", Extension = kind == MediaKind.Hls ? "m3u8" : "mpd" });
        reported.Add(new NativeFormat { Id = id, Url = url, Protocol = id == "hls" ? "m3u8" : "dash", Note = "Adaptive manifest" });
    }

    private static IReadOnlyList<SubtitleTrack> ReadSubtitles(JsonElement root)
    {
        var tracks = Array(Object(Object(root, "captions"), "playerCaptionsTracklistRenderer"), "captionTracks");
        if (tracks.Length > 200) throw new InvalidDataException("YouTube returned too many caption tracks.");
        var result = new List<SubtitleTrack>();
        foreach (var track in tracks)
        {
            var raw = Text(track, "baseUrl");
            var language = Text(track, "languageCode");
            if (raw is null || language is null) continue;
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.Scheme != "https" || !YouTubeAddress.IsYouTubeHost(url))
                throw new InvalidDataException("YouTube returned an invalid caption address.");
            result.Add(new SubtitleTrack { Url = YouTubeQuery.Set(url, "fmt", "vtt"), Language = language,
                Extension = "vtt", Automatic = Text(track, "kind") == "asr" });
        }
        return result;
    }

    private static string? ReadThumbnail(JsonElement details) => Array(Object(details, "thumbnail"), "thumbnails")
        .Where(thumbnail => Uri.TryCreate(Text(thumbnail, "url"), UriKind.Absolute, out var url) && url.Scheme == "https")
        .OrderByDescending(thumbnail => (Number(thumbnail, "width") ?? 0) * (Number(thumbnail, "height") ?? 0))
        .Select(thumbnail => Text(thumbnail, "url")).FirstOrDefault();
    private static IReadOnlyList<ThumbnailCandidate> ReadThumbnails(JsonElement details)
    {
        var images = Array(Object(details, "thumbnail"), "thumbnails");
        if (images.Length > ThumbnailCatalog.MaximumCount) throw new InvalidDataException("YouTube returned too many thumbnails.");
        return images.Select((image, index) => new { Image = image, Index = index, Url = Text(image, "url") })
            .Where(image => Uri.TryCreate(image.Url, UriKind.Absolute, out var url) && url.Scheme == "https")
            .Select(image => new ThumbnailCandidate { Url = new(image.Url!), Id = image.Index.ToString(CultureInfo.InvariantCulture),
                Width = ProviderJson.Integer(image.Image, "width"), Height = ProviderJson.Integer(image.Image, "height") }).ToArray();
    }

    private static JsonElement Object(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
    private static JsonElement[] Array(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
    private static string? Text(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool Boolean(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
    private static double? Number(JsonElement element, string key)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(key, out var value)) return null;
        double result;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out result) && double.IsFinite(result) && result >= 0) return result;
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
            double.IsFinite(result) && result >= 0) return result;
        return null;
    }
    private static int? Integer(JsonElement element, string key) => Number(element, key) is { } number && number <= int.MaxValue && number == Math.Truncate(number) ? (int)number : null;
    private static long? Long(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) &&
        ((value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0) ||
         (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out number) && number >= 0)) ? number : null;
}
