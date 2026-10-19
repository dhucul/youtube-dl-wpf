namespace YoutubeDl.Engine.Media;

internal sealed record VideoRecodeProfile(string Extension, string VideoEncoder, string VideoCodec, string AudioEncoder, string AudioCodec)
{
    internal static VideoRecodeProfile? Parse(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        "" => null,
        "mp4" => new("mp4", "libx264", "h264", "aac", "aac"),
        "mkv" => new("mkv", "libx264", "h264", "aac", "aac"),
        "webm" => new("webm", "libvpx-vp9", "vp9", "libopus", "opus"),
        "avi" => new("avi", "mpeg4", "mpeg4", "pcm_s16le", "pcm_s16le"),
        "flv" => new("flv", "libx264", "h264", "aac", "aac"),
        "ogg" => new("ogg", "libtheora", "theora", "libvorbis", "vorbis"),
        _ => throw new NotSupportedException("Native video recoding supports mp4, mkv, webm, avi, flv and ogg.")
    };
    internal static string Demuxer(string extension) => extension.TrimStart('.').ToLowerInvariant() switch
    {
        "mp4" or "mov" or "m4v" or "m4a" or "3gp" => "mov",
        "mkv" or "webm" or "mka" => "matroska", "ts" or "m2ts" => "mpegts", "avi" => "avi", "flv" => "flv",
        "ogg" or "ogv" => "ogg", "wmv" or "asf" => "asf", "mpg" or "mpeg" => "mpeg",
        _ => throw new NotSupportedException("This source container requires additional native video-recoding support.")
    };
}
