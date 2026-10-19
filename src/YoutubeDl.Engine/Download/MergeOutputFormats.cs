namespace YoutubeDl.Engine.Download;

/// <summary>Container preferences never imply re-encoding or remuxing an unpaired file.</summary>
internal static class MergeOutputFormats
{
    private static readonly HashSet<string> Supported = ["mp4", "mov", "mkv", "webm", "avi", "flv", "ogg"];

    internal static string[] Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var choices = value.Split('/');
        if (choices.Length > 8 || choices.Any(choice => !Supported.Contains(choice)))
            throw new ArgumentException("Choose merge containers from mp4, mov, mkv, webm, avi, flv or ogg, separated by '/'.");
        return choices.Distinct(StringComparer.Ordinal).ToArray();
    }

    internal static string Select(string preference, string automatic, string? videoCodec, string? audioCodec)
    {
        var choices = Parse(preference);
        if (choices.Length == 0) return automatic;
        foreach (var choice in choices)
            if (Allows(choice, Codec(videoCodec), Codec(audioCodec))) return choice;
        throw new NotSupportedException("None of the requested merge containers can preserve the selected video and audio codecs. Include mkv as a fallback or choose video conversion.");
    }

    internal static bool CanPredict(string preference, string? videoCodec, string? audioCodec) =>
        Parse(preference).Length <= 1 || Codec(videoCodec) is not null && Codec(audioCodec) is not null;

    // Unknown facts are checked by ffmpeg during the staged merge. Known incompatible
    // codecs must not silently trigger conversion or a different, unrequested container.
    private static bool Allows(string container, string? video, string? audio) => container switch
    {
        "mkv" => true,
        "webm" => (video is null or "vp8" or "vp9" or "av1") && (audio is null or "opus" or "vorbis"),
        "ogg" => (video is null or "theora" or "vp8") && (audio is null or "vorbis" or "opus" or "flac"),
        "flv" => (video is null or "h264" or "flv1") && (audio is null or "aac" or "mp3"),
        "avi" => (video is null or "h264" or "mpeg4" or "mpeg2video" or "mjpeg") && (audio is null or "mp3" or "ac3" || audio.StartsWith("pcm_", StringComparison.Ordinal)),
        "mp4" => (video is null or "h264" or "hevc" or "av1" or "vp9" or "mpeg4" or "mpeg2video") &&
            (audio is null or "aac" or "mp3" or "opus" or "flac" or "alac" or "ac3" or "eac3"),
        "mov" => (video is null or "h264" or "hevc" or "av1" or "vp9" or "mpeg4" or "mpeg2video" or "prores" or "mjpeg") &&
            (audio is null or "aac" or "mp3" or "alac" or "ac3" or "eac3" || audio.StartsWith("pcm_", StringComparison.Ordinal)),
        _ => false
    };

    private static string? Codec(string? codec)
    {
        if (string.IsNullOrWhiteSpace(codec) || codec == "unknown") return null;
        codec = codec.ToLowerInvariant();
        if (codec.StartsWith("avc", StringComparison.Ordinal)) return "h264";
        if (codec.StartsWith("dvh1", StringComparison.Ordinal) || codec.StartsWith("dvhe", StringComparison.Ordinal) || codec.StartsWith("hvc", StringComparison.Ordinal) || codec.StartsWith("hev", StringComparison.Ordinal) || codec == "h265") return "hevc";
        if (codec.StartsWith("av01", StringComparison.Ordinal)) return "av1";
        if (codec.StartsWith("vp09", StringComparison.Ordinal) || codec.StartsWith("vp9", StringComparison.Ordinal)) return "vp9";
        if (codec.StartsWith("vp08", StringComparison.Ordinal)) return "vp8";
        if (codec.StartsWith("mp4a", StringComparison.Ordinal)) return "aac";
        return codec.Split('.')[0];
    }
}

