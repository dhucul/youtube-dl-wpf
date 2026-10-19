namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    private sealed record TimecodePlan(int DataIndex, string Value, string TimeBase, string Configuration);
    private static bool IsTimecodeContainer(string path) => Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".mov" or ".m4v";

    private static async Task<TimecodePlan?> PrepareTimecodeAsync(string ffmpeg, string input, string output, CancellationToken ct)
    {
        if (!IsTimecodeContainer(input) || !IsTimecodeContainer(output)) return null;
        var streams = await ProbeStreamsAsync(ffmpeg, input, ct, "preserve MP4 timecode tracks").ConfigureAwait(false);
        var data = streams.Where(stream => stream.Kind == "data" && stream.CodecTag == "tmcd").ToArray();
        if (data.Length == 0) return null;
        var video = streams.Where(stream => stream.Kind == "video" && !stream.Attached && stream.Timecode is not null).ToArray();
        if (data.Length != 1 || video.Length != 1 || data[0].FrameCount != "1" || string.IsNullOrEmpty(data[0].Timecode) ||
            data[0].Timecode != video[0].Timecode || string.IsNullOrEmpty(data[0].ExtraDataHash))
            throw new NotSupportedException("This MP4 timecode layout cannot yet be preserved during native processing.");
        return new(data[0].Index, data[0].Timecode!, data[0].TimeBase, data[0].ExtraDataHash);
    }
    private static void MapTimecode(List<string> arguments, TimecodePlan? timecode)
    {
        if (timecode is null) return;
        // ffmpeg cannot copy a codec-none tmcd stream. It can regenerate the
        // same timecode from the associated copied video's existing metadata.
        // Exclude only this verified stream; retain all other source streams.
        arguments.AddRange(["-map", "-0:" + timecode.DataIndex, "-write_tmcd", "1"]);
    }
    private static async Task VerifyTimecodeAsync(string ffmpeg, string output, TimecodePlan? expected, CancellationToken ct)
    {
        if (expected is null) return;
        var streams = await ProbeStreamsAsync(ffmpeg, output, ct, "verify preserved MP4 timecode").ConfigureAwait(false);
        var data = streams.Where(stream => stream.Kind == "data" && stream.CodecTag == "tmcd").ToArray();
        if (data.Length != 1 || data[0].FrameCount != "1" || data[0].Timecode != expected.Value || data[0].TimeBase != expected.TimeBase ||
            data[0].ExtraDataHash != expected.Configuration || !streams.Any(stream => stream.Kind == "video" && !stream.Attached && stream.Timecode == expected.Value))
            throw new InvalidDataException("The processed file did not retain its original timecode configuration and value.");
    }
}
