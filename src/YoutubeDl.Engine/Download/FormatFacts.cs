using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Download;

internal static class FormatFacts
{
    public static NativeFormat? Reported(PageMetadata page, MediaCandidate candidate) => page.ReportedFormats.FirstOrDefault(format => format.Id == candidate.Id);
    public static bool Video(MediaCandidate candidate) => candidate.HasVideo ?? !candidate.IsAudioOnly;
    public static bool Audio(MediaCandidate candidate) => candidate.HasAudio ?? candidate.IsAudioOnly || candidate.HasAudio is null && candidate.HasVideo != false;
    public static string? VideoCodec(PageMetadata page, MediaCandidate candidate) => !Video(candidate) ? "none" : candidate.VideoCodec ?? Reported(page, candidate)?.VideoCodec;
    public static string? AudioCodec(PageMetadata page, MediaCandidate candidate) => !Audio(candidate) ? "none" : candidate.AudioCodec ?? Reported(page, candidate)?.AudioCodec;
    public static double? Number(PageMetadata page, MediaCandidate candidate, string field)
    {
        var reported = Reported(page, candidate);
        return field switch
        {
            "height" => candidate.Height ?? reported?.Height,
            "width" => candidate.Width ?? reported?.Width,
            "fps" => candidate.FramesPerSecond ?? reported?.FramesPerSecond,
            "filesize" => candidate.FileSize ?? reported?.FileSize,
            "tbr" => (candidate.Bandwidth ?? reported?.Bandwidth) / 1000,
            "aspect_ratio" => candidate.Height is > 0 && candidate.Width is { } width ? (double)width / candidate.Height.Value : null,
            _ => throw new NotSupportedException("Native format filtering is pending for numeric field: " + field)
        };
    }
    public static string? Text(PageMetadata page, MediaCandidate candidate, string field) => field switch
    {
        "format_id" => candidate.Id,
        "ext" or "container" => candidate.Extension,
        "protocol" => candidate.Kind switch { MediaKind.Hls => "m3u8", MediaKind.Dash => "http_dash_segments", MediaKind.Sabr => "sabr", MediaKind.HttpSequence => "http_sequence", _ => candidate.Url.Scheme },
        "vcodec" => VideoCodec(page, candidate), "acodec" => AudioCodec(page, candidate),
        "language" => candidate.AudioLanguage ?? Reported(page, candidate)?.AudioLanguage,
        "url" => candidate.Kind == MediaKind.HttpSequence ? null : candidate.Url.AbsoluteUri,
        _ => throw new NotSupportedException("Native format filtering is pending for text field: " + field)
    };
    public static bool CodecMatches(string? actual, string? desired) => (desired ?? "any").ToLowerInvariant() switch
    {
        "" or "any" => true,
        "h264" => actual?.StartsWith("avc", StringComparison.OrdinalIgnoreCase) == true || actual?.Equals("h264", StringComparison.OrdinalIgnoreCase) == true,
        "h265" => actual?.StartsWith("dvh1", StringComparison.OrdinalIgnoreCase) == true || actual?.StartsWith("dvhe", StringComparison.OrdinalIgnoreCase) == true || actual?.StartsWith("hev", StringComparison.OrdinalIgnoreCase) == true || actual?.StartsWith("hvc", StringComparison.OrdinalIgnoreCase) == true || actual?.Equals("hevc", StringComparison.OrdinalIgnoreCase) == true,
        "vp9" => actual is not null && (actual.Equals("vp9", StringComparison.OrdinalIgnoreCase) || actual.StartsWith("vp9.", StringComparison.OrdinalIgnoreCase) || actual.Equals("vp09", StringComparison.OrdinalIgnoreCase) || actual.StartsWith("vp09.", StringComparison.OrdinalIgnoreCase)),
        "av1" => actual?.StartsWith("av01", StringComparison.OrdinalIgnoreCase) == true || actual?.Equals("av1", StringComparison.OrdinalIgnoreCase) == true,
        _ => throw new ArgumentException("Choose any, h264, h265, vp9, or av1 as the native video codec preference.")
    };
    public static bool MatchesConstraints(PageMetadata page, MediaCandidate candidate, NativeDownloadRequest request)
    {
        if (request.AudioOnly || candidate.IsAudioOnly) return true;
        var manifest = candidate.Kind is MediaKind.Hls or MediaKind.Dash;
        var height = Number(page, candidate, "height");
        var fps = Number(page, candidate, "fps");
        var codec = VideoCodec(page, candidate);
        return (request.MaximumHeight is not > 0 || height <= request.MaximumHeight || manifest && height is null) &&
            (request.MaximumFrameRate <= 0 || fps <= request.MaximumFrameRate || manifest && fps is null) &&
            (CodecMatches(codec, request.VideoCodec) || manifest && codec is null);
    }
}

