using System.Text.Json;
using YoutubeDl.Engine.Media;

internal static partial class FfmpegChecks
{
    private static async Task OdyseeTimecode(string ffmpeg, bool dropFrame)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4");
        var timecode = dropFrame ? "01:00:00;00" : "01:00:00:00";
        var rate = dropFrame ? "30000/1001" : "25";
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=128x96:r=" + rate + ":d=1", "-f", "lavfi", "-i", "sine=frequency=600:sample_rate=48000:duration=1",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-timecode", timecode, source]);
        string output;
        if (dropFrame)
        {
            output = Path.Combine(files.Directory, "remuxed.mov");
            await FfmpegTool.RemuxAsync(ffmpeg, source, output, default);
        }
        else
        {
            var bytes = await File.ReadAllBytesAsync(source); var claim = OdyseeChecks.Stream(); claim["value"]!["source"]!["size"] = bytes.Length;
            claim["value"]!["video"]!["width"] = 128; claim["value"]!["video"]!["height"] = 96;
            var result = await OdyseeChecks.Engine(claim, [], media: _ => new(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) })
                .DownloadAsync(OdyseeChecks.Request(files.Directory) with { AddMetadata = true, KeepOriginal = true, FfmpegPath = ffmpeg }, null, null, default);
            if (!result.Succeeded || result.OutputPaths.Count != 2 || !File.ReadAllBytes(result.OutputPaths.Single(path => path.EndsWith(".original.mp4"))).SequenceEqual(bytes))
                throw new InvalidOperationException("Native Odysee processing lost its complete original.");
            output = result.OutputPath!;
        }
        var original = await TimecodeFacts(source); var processed = await TimecodeFacts(output);
        if (original != processed || !processed.Contains(timecode)) throw new InvalidOperationException("Processing changed the timecode configuration, packet payload, timing or value.");
        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        async Task<string[]> Frames(string path)
        {
            using var json = JsonDocument.Parse(await Run(ffprobe, ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path]));
            return json.RootElement.GetProperty("streams").EnumerateArray().Where(item => item.GetProperty("codec_type").GetString() is "video" or "audio")
                .Select(item => item.GetProperty("codec_type").GetString() + ":" + item.GetProperty("nb_read_frames").GetString()).ToArray();
        }
        var sourceFrames = await Frames(source); var outputFrames = await Frames(output);
        if (!sourceFrames.SequenceEqual(outputFrames)) throw new InvalidOperationException("Timecode preservation changed video or audio frame counts.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", output, "-map", "0:v:0", "-map", "0:a:0", "-f", "null", "-"]);

        async Task<string> TimecodeFacts(string path)
        {
            var json = await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-select_streams", "d", "-show_packets", "-show_data_hash", "sha256",
                "-show_entries", "packet=pts_time,duration_time,size,data_hash:stream=codec_tag_string,time_base,extradata_hash,nb_frames:stream_tags=timecode", "-of", "json", path]);
            using var document = JsonDocument.Parse(json);
            var packets = document.RootElement.GetProperty("packets"); var streams = document.RootElement.GetProperty("streams");
            if (streams.GetArrayLength() != 1 || packets.GetArrayLength() != 1) throw new InvalidOperationException("Timecode tracks or packets were duplicated or lost.");
            return packets.GetRawText() + streams.GetRawText();
        }
    }
}
