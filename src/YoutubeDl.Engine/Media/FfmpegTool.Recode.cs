using System.Globalization;

namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    internal static async Task RecodeVideoAsync(string ffmpeg, string input, string output, VideoRecodeProfile profile, CancellationToken ct)
    {
        var demuxer = VideoRecodeProfile.Demuxer(Path.GetExtension(input));
        var source = await ProbeStreamsAsync(ffmpeg, input, ct, "verify the complete source video", demuxer, countFrames: true).ConfigureAwait(false);
        if (!source.Any(stream => stream.Kind == "video" && !stream.Attached)) throw new InvalidDataException("Video conversion requires a video stream.");
        if (source.Any(stream => stream.Kind == "video" && !stream.Attached && Math.Abs(stream.Rotation / 90 - Math.Round(stream.Rotation / 90)) > 0.00001))
            throw new NotSupportedException("Video conversion of a non-orthogonal display rotation requires further support.");
        if (source.Any(stream => stream.Kind == "audio" && (!long.TryParse(stream.FrameCount, out var count) || count <= 0)))
            throw new InvalidDataException("Video conversion cannot preserve an empty or undecodable audio track.");
        if (source.Any(stream => stream.Kind is not ("video" or "audio" or "subtitle" or "attachment")))
            throw new NotSupportedException("Video conversion of additional data/timecode streams requires further native support.");
        if (profile.Extension is "avi" or "flv" or "ogg" && source.Any(stream => stream.Kind is "subtitle" or "attachment" || stream.Attached))
            throw new NotSupportedException("The selected conversion container cannot preserve these subtitle or attached-image streams.");
        if (source.Any(stream => stream.Attached) && profile.Extension != "mp4") throw new NotSupportedException("Cross-container cover conversion requires additional native support.");
        if (source.Any(stream => stream.Kind == "attachment") && profile.Extension != "mkv") throw new NotSupportedException("The selected conversion container cannot preserve attachments.");
        if (profile.Extension == "flv" && (source.Count(stream => stream.Kind == "video") != 1 || source.Count(stream => stream.Kind == "audio") > 1))
            throw new NotSupportedException("FLV conversion requires one video and at most one audio stream.");
        var args = new List<string> { "-y", "-v", "error", "-xerror", "-protocol_whitelist", "file,pipe", "-f", demuxer };
        if (demuxer == "mov") args.AddRange(["-enable_drefs", "0", "-use_absolute_path", "0"]);
        args.AddRange(["-i", input, "-map", "0", "-map_metadata", "0", "-map_chapters", "0", "-c", "copy", "-c:a", profile.AudioEncoder]);
        if (profile.AudioEncoder is "aac" or "libopus") args.AddRange(["-b:a", "192k"]);
        if (profile.AudioEncoder == "libvorbis") args.AddRange(["-q:a", "5"]);
        var videoIndex = 0; var subtitleIndex = 0;
        foreach (var stream in source)
        {
            if (stream.Kind == "video")
            {
                var index = (videoIndex++).ToString(CultureInfo.InvariantCulture);
                if (stream.Attached) continue;
                var suffix = ":v:" + index;
                args.AddRange(["-c" + suffix, profile.VideoEncoder, "-fps_mode" + suffix, "passthrough"]);
                args.AddRange(["-pix_fmt" + suffix, profile.VideoEncoder is "libx264" or "libvpx-vp9" && (stream.Width % 2 != 0 || stream.Height % 2 != 0) ? "yuv444p" : "yuv420p"]);
                if (profile.VideoEncoder is "libx264" or "libvpx-vp9") args.AddRange(["-enc_time_base" + suffix, "demux"]);
                if (profile.VideoEncoder == "libx264") args.AddRange(["-crf" + suffix, "18", "-preset" + suffix, "medium"]);
                else if (profile.VideoEncoder == "libvpx-vp9") args.AddRange(["-crf" + suffix, "30", "-b" + suffix, "0", "-deadline" + suffix, "good", "-cpu-used" + suffix, "2"]);
                else args.AddRange(["-q" + suffix, profile.VideoEncoder == "libtheora" ? "7" : "2"]);
            }
            if (stream.Kind == "subtitle")
            {
                var index = (subtitleIndex++).ToString(CultureInfo.InvariantCulture);
                var text = stream.Codec is "subrip" or "ass" or "ssa" or "webvtt" or "mov_text" or "text";
                var encoder = profile.Extension switch { "mp4" when text => "mov_text", "webm" when text => "webvtt", "mkv" when stream.Codec == "mov_text" => "srt", "mkv" => "copy", _ => throw new NotSupportedException("This subtitle type cannot be preserved in the selected video container.") };
                args.AddRange(["-c:s:" + index, encoder]);
            }
        }
        if (profile.Extension == "mp4") args.AddRange(["-movflags", "+faststart"]);
        args.Add(output);
        await RunConfiguredAsync(ffmpeg, args, ct, ["VideoConvertor"], rejectDiagnostics: true).ConfigureAwait(false);
        var result = await ProbeStreamsAsync(ffmpeg, output, ct, "verify the complete converted video", VideoRecodeProfile.Demuxer(profile.Extension), countFrames: true).ConfigureAwait(false);
        if (result.Count != source.Count) throw new InvalidDataException("Video conversion changed the number of source streams.");
        foreach (var kind in new[] { "video", "audio", "subtitle", "attachment" })
        {
            var before = source.Where(stream => stream.Kind == kind).ToArray(); var after = result.Where(stream => stream.Kind == kind).ToArray();
            if (before.Length != after.Length) throw new InvalidDataException("Video conversion lost a source stream.");
            for (var index = 0; index < before.Length; index++)
            {
                var original = before[index]; var converted = after[index];
                var swap = !original.Attached && Math.Abs((int)Math.Round(original.Rotation / 90)) % 2 == 1;
                if (kind == "video" && (converted.Attached != original.Attached || converted.Width != (swap ? original.Height : original.Width) || converted.Height != (swap ? original.Width : original.Height) ||
                    !original.Attached && Math.Abs(converted.Rotation % 360) > 0.00001 ||
                    converted.Codec != (original.Attached ? original.Codec : profile.VideoCodec) || !long.TryParse(original.FrameCount, out var frames) || frames <= 0 || original.FrameCount != converted.FrameCount))
                    throw new InvalidDataException("Video conversion did not preserve every video frame and its dimensions.");
                if (kind == "audio" && (converted.Codec != profile.AudioCodec || converted.Channels != original.Channels || !long.TryParse(converted.FrameCount, out var audioFrames) || audioFrames <= 0))
                    throw new InvalidDataException("Video conversion changed the requested audio tracks or channel count.");
                // Subtitle container duration can include empty trailing intervals;
                // it is not the duration of the visible caption timeline.
                if (kind is "video" or "audio" && original.DurationSeconds is { } duration && converted.DurationSeconds is { } actual && Math.Abs(duration - actual) > 0.15m)
                    throw new InvalidDataException("Video conversion changed the complete stream duration.");
            }
        }
    }
}
