namespace YoutubeDl.Engine.Media;

/// <summary>Reads clear fragmented stpp text samples through the shared MP4 caption parser.</summary>
internal static class Mp4TtmlReader
{
    internal static bool IsSupportedCodec(string? codec) => codec is "stpp" or "stpp.ttml" or "stpp.ttml.im1t" or "stpp.ttml.im2t";
    internal static string Read(byte[] bytes, double? durationSeconds = null, double offsetSeconds = 0, CancellationToken ct = default) =>
        ReadSegments(bytes, [new(bytes)], durationSeconds, offsetSeconds, ct);
    internal static string ReadSegments(byte[] initialization, IReadOnlyList<Mp4WebVttSegment> segments, double? durationSeconds = null, double offsetSeconds = 0, CancellationToken ct = default) =>
        Mp4WebVttReader.ReadTtmlSegments(initialization, segments, durationSeconds, offsetSeconds, ct);
}
