using YoutubeDl.Engine.Download;

namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    internal static async Task<string> SelectMergeExtensionAsync(string ffmpeg, string video, string audio, string preference, string automatic, CancellationToken ct)
    {
        var videoStreams = await ProbeStreamsAsync(ffmpeg, video, ct, "choose the merge container", localMediaOnly: true).ConfigureAwait(false);
        var audioStreams = await ProbeStreamsAsync(ffmpeg, audio, ct, "choose the merge container", localMediaOnly: true).ConfigureAwait(false);
        var videoStream = videoStreams.FirstOrDefault(stream => stream.Kind == "video" && !stream.Attached)
            ?? throw new InvalidDataException("The selected merge input has no video track.");
        var audioStream = audioStreams.FirstOrDefault(stream => stream.Kind == "audio")
            ?? throw new InvalidDataException("The selected merge input has no audio track.");
        return MergeOutputFormats.Select(preference, automatic, videoStream.Codec, audioStream.Codec);
    }
}
