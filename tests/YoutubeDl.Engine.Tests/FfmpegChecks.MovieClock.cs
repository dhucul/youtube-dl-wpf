using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Media;

internal static partial class FfmpegChecks
{
    private static async Task MovieClockPersistence(string ffmpeg, bool gaps = false)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4"); var captions = Path.Combine(files.Directory, "captions.vtt");
        await File.WriteAllTextAsync(captions, "WEBVTT\n\n00:00:00.100 --> 00:00:11.000\nClock fixture caption.\n");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x96:r=24:d=12", "-f", "lavfi", "-i", "sine=frequency=500:sample_rate=44100:duration=12",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-video_track_timescale", "90000", source]);
        var authored = Path.Combine(files.Directory, "authored.mp4"); var metadata = Path.Combine(files.Directory, "metadata.mp4"); var remuxed = Path.Combine(files.Directory, "remuxed.mov");
        var chapters = gaps ? new[] { new ChapterMarker { Title = "First", StartSeconds = 0.57, EndSeconds = 5 }, new ChapterMarker { Title = "Second", StartSeconds = 7, EndSeconds = 12 } } :
            new[] { new ChapterMarker { Title = "First", StartSeconds = 0, EndSeconds = 10 }, new ChapterMarker { Title = "Second", StartSeconds = 10, EndSeconds = 12 } };
        await FfmpegTool.EmbedAsync(ffmpeg, source, authored, [new(captions, "en-US")], null, null, default, chapters);
        await FfmpegTool.EmbedAsync(ffmpeg, authored, metadata, [], null, new Dictionary<string, string> { ["title"] = "Further processing" }, default);
        await FfmpegTool.RemuxAsync(ffmpeg, metadata, remuxed, default);
        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        async Task<string[]> Packets(string path, string role)
        {
            using var json = JsonDocument.Parse(await Run(probe, ["-v", "error", "-select_streams", role, "-show_packets", "-show_data_hash", "sha256", "-show_entries", "packet=data_hash", "-of", "json", path], rejectDiagnostics: true));
            return json.RootElement.GetProperty("packets").EnumerateArray().Select(packet => packet.GetProperty("data_hash").GetString()!).ToArray();
        }
        var originalVideo = await Packets(source, "v:0"); var originalAudio = await Packets(source, "a:0");
        foreach (var path in new[] { authored, metadata, remuxed })
        {
            using var json = JsonDocument.Parse(await Run(probe, ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames:chapter=start_time,end_time:chapter_tags=title", "-of", "json", path], rejectDiagnostics: true));
            var markers = json.RootElement.GetProperty("chapters").EnumerateArray().ToArray(); var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            if (markers.Length != (gaps ? 4 : 2) || !gaps && (markers[0].GetProperty("end_time").GetString() != "10.000000" || markers[1].GetProperty("end_time").GetString() != "12.000000") ||
                streams.Single(stream => stream.GetProperty("codec_type").GetString() == "video").GetProperty("nb_read_frames").GetString() != "288")
                throw new InvalidOperationException("Later processing damaged chapter timing or complete video frames.");
            if (gaps && (markers[1].GetProperty("start_time").GetString() != "0.570000" || markers[1].GetProperty("end_time").GetString() != "5.000000" ||
                markers[3].GetProperty("start_time").GetString() != "7.000000" || markers[3].GetProperty("end_time").GetString() != "12.000000"))
                throw new InvalidOperationException("An unlabelled gap moved a named chapter.");
            var video = await Packets(path, "v:0"); var audio = await Packets(path, "a:0");
            if (!originalVideo.SequenceEqual(video) || !originalAudio.SequenceEqual(audio)) throw new InvalidOperationException("Movie clock selection changed encoded media packets.");
            var subtitle = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", path, "-map", "0:s:0", "-f", "srt", "-"], rejectDiagnostics: true);
            if (!subtitle.Contains("Clock fixture caption.") || !subtitle.Contains("00:00:00,100 --> 00:00:11,000")) throw new InvalidOperationException("Later processing changed caption text or timing.");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v:0", "-map", "0:a:0", "-fps_mode:v", "passthrough", "-enc_time_base:v", "demux", "-f", "null", "-"], rejectDiagnostics: true);
        }
    }
}
