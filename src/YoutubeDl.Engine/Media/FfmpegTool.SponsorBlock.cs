using System.Globalization;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    internal static async Task<SponsorBlockPlan> BindSponsorSourceAsync(string ffmpeg, string input, SponsorBlockPlan plan, CancellationToken ct)
    {
        var streams = await ProbeStreamsAsync(ffmpeg, input, ct, "verify SponsorBlock source", inputDemuxer: "mov", countFrames: true).ConfigureAwait(false);
        if (streams.Count is 0 or > 2 || streams.Any(s => s.Kind is not ("audio" or "video") || s.Attached) || streams.Count(s => s.Kind == "video") > 1 || streams.Count(s => s.Kind == "audio") > 1)
            throw new NotSupportedException("SponsorBlock removal currently requires one clear MP4 video/audio program without extra embedded tracks.");
        foreach (var s in streams)
        {
            if (s.Kind == "video" && (s.Codec != "h264" || s.PixelFormat != "yuv420p" || s.Rotation != 0 || !s.SimpleDisplay || s.AdvancedHdr || s.StaticHdr || s.Dolby is not null || s.ColorTransfer is "smpte2084" or "arib-std-b67") ||
                s.Kind == "audio" && (s.Codec is not ("aac" or "alac" or "flac") || s.Channels is < 1 or > 8 || s.SampleRate is < 8000 or > 192000) || !long.TryParse(s.FrameCount, out var count) || count <= 0 || s.DurationSeconds is not > 0 || s.StartTime is null)
                throw new NotSupportedException("This SponsorBlock decoder, color, clock or audio layout needs additional native support.");
        }
        if (streams.Max(s => s.StartTime!.Value) - streams.Min(s => s.StartTime!.Value) > 0.001m) throw new NotSupportedException("SponsorBlock removal requires aligned source audio/video clocks.");
        var duration = streams.Max(s => s.DurationSeconds!.Value);
        if (Math.Abs(duration - plan.Duration) > 1.1m) throw new InvalidDataException("The complete SponsorBlock source differs from the selected video duration.");
        return plan with { Duration = duration };
    }
    internal static async Task RemoveSponsorSegmentsAsync(string ffmpeg, string input, string output, SponsorBlockPlan plan, NativeDownloadRequest request, CancellationToken ct)
    {
        var source = await ProbeStreamsAsync(ffmpeg, input, ct, "inspect SponsorBlock streams", inputDemuxer: "mov").ConfigureAwait(false);
        var video = source.Any(s => s.Kind == "video"); var audio = source.Any(s => s.Kind == "audio"); var spans = plan.Kept(request); var filters = new List<string>();
        string Time(decimal value) => value.ToString(CultureInfo.InvariantCulture);
        if (video)
        {
            var select = string.Join('+', spans.Select(span => "gte(t," + Time(span.Start) + ")*lt(t," + Time(span.End) + ")"));
            var removed = new List<SponsorSpan>(); decimal position = 0;
            foreach (var span in spans) { if (span.Start > position) removed.Add(new(position, span.Start)); position = span.End; }
            if (position < plan.Duration) removed.Add(new(position, plan.Duration));
            var shift = string.Join('+', removed.Select(span => Time(span.Duration) + "*gte(T," + Time(span.End) + ")"));
            filters.Add("[0:v:0]setpts=PTS-STARTPTS,select='" + select + "',setpts='PTS-(" + (shift.Length == 0 ? "0" : shift) + ")/TB'[vout]");
        }
        if (audio) filters.Add("[0:a:0]asetpts=PTS-STARTPTS,asplit=" + spans.Count + string.Concat(Enumerable.Range(0, spans.Count).Select(i => "[as" + i + "]")));
        for (var i = 0; i < spans.Count; i++)
        {
            var span = spans[i];

            if (audio) filters.Add("[as" + i + "]atrim=start=" + Time(span.Start) + ":end=" + Time(span.End) + ",asetpts=PTS-STARTPTS,apad,atrim=duration=" + Time(span.Duration) + "[a" + i + "]");
        }
        // Audio cuts use sample boundaries. Video keeps its exact source frame
        // positions projected through the same edit map, including VFR gaps.
        if (audio) filters.Add(string.Concat(Enumerable.Range(0, spans.Count).Select(i => "[a" + i + "]")) + "concat=n=" + spans.Count + ":v=0:a=1[aout]");
        var filterFile = output + ".filters";
        await File.WriteAllTextAsync(filterFile, string.Join(';', filters), ct).ConfigureAwait(false);
        var args = new List<string> { "-y", "-v", "error", "-xerror", "-protocol_whitelist", "file,pipe", "-f", "mov", "-enable_drefs", "0", "-use_absolute_path", "0", "-i", input, "-/filter_complex", filterFile, "-map_metadata", "0", "-map_chapters", "-1" };
        if (video) args.AddRange(["-map", "[vout]", "-c:v", "libx264", "-preset", "fast", "-qp", "0", "-pix_fmt", "yuv420p", "-fps_mode:v", "passthrough", "-enc_time_base:v", "demux"]);
        if (audio) args.AddRange(["-map", "[aout]", "-c:a", "alac"]);
        args.AddRange(["-t", Time(spans.Sum(s => s.Duration)), output]);
        try { await RunAsync(ffmpeg, args, ct, rejectDiagnostics: true).ConfigureAwait(false); } finally { File.Delete(filterFile); }
        await VerifyDownloadedMediaAsync(ffmpeg, output, new MediaCandidate { Url = new(Path.GetFullPath(output)), Kind = MediaKind.Progressive, HasAudio = audio, HasVideo = video, VerificationDemuxer = "mov", VerificationDurationSeconds = (double)spans.Sum(s => s.Duration) }, ct).ConfigureAwait(false);
        var result = await ProbeStreamsAsync(ffmpeg, output, ct, "verify SponsorBlock output", inputDemuxer: "mov", countFrames: true).ConfigureAwait(false);
        if (result.Count != source.Count || result.Any(s => !long.TryParse(s.FrameCount, out var count) || count <= 0) || result.Any(s => s.Kind == "audio" && (s.SampleRate != source.Single(a => a.Kind == "audio").SampleRate || s.Channels != source.Single(a => a.Kind == "audio").Channels))) throw new InvalidDataException("SponsorBlock output lost a selected stream or its audio layout.");
    }
}
