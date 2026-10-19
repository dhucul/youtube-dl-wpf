using System.Globalization;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    internal static async Task MergeLoopedVideoAsync(string ffmpeg, string video, string audio, string output, Uri source, CancellationToken ct)
    {
        var videoStreams = await ProbeStreamsAsync(ffmpeg, video, ct, "measure a provider-declared video loop", localMediaOnly: true).ConfigureAwait(false);
        var audioStreams = await ProbeStreamsAsync(ffmpeg, audio, ct, "measure the complete loop audio", localMediaOnly: true).ConfigureAwait(false);
        var cycle = videoStreams.FirstOrDefault(stream => stream.Kind == "video" && !stream.Attached)?.DurationSeconds;
        var length = audioStreams.FirstOrDefault(stream => stream.Kind == "audio")?.DurationSeconds;
        if (cycle is not > 0 || length is not > 0 || cycle > 600 || length > 3600 || decimal.Ceiling(length.Value / cycle.Value) > 1000 || decimal.Ceiling(cycle.Value / length.Value) > 1000)
            throw new NotSupportedException("This loop/audio layout lacks a bounded complete timeline supported by the native renderer.");
        var target = Math.Max(cycle.Value, length.Value);
        var args = new List<string> { "-y", "-v", "error", "-nostats", "-protocol_whitelist", "file,pipe", "-stream_loop", "-1" };
        if (Path.GetExtension(video).Equals(".mp4", StringComparison.OrdinalIgnoreCase)) args.AddRange(["-f", "mov", "-enable_drefs", "0", "-use_absolute_path", "0"]);
        args.AddRange(["-i", video, "-protocol_whitelist", "file,pipe"]);
        if (length < cycle) args.AddRange(["-stream_loop", "-1"]);
        args.AddRange(["-i", audio, "-map", "0:v:0", "-map", "1:a:0", "-c", "copy", "-t", target.ToString("0.################", CultureInfo.InvariantCulture), "-shortest", output]);
        await RunAsync(ffmpeg, args, ct, rejectDiagnostics: true).ConfigureAwait(false);
        var demuxer = Path.GetExtension(output).ToLowerInvariant() switch { ".mkv" or ".webm" => "matroska", ".mp4" or ".mov" => "mov", _ => throw new NotSupportedException("This rendered loop container needs additional verification support.") };
        await VerifyDownloadedMediaAsync(ffmpeg, output, new MediaCandidate { Url = source, Kind = MediaKind.Progressive, HasVideo = true, HasAudio = true,
            VerificationDemuxer = demuxer, VerificationDurationSeconds = (double)target }, ct).ConfigureAwait(false);
    }
}
