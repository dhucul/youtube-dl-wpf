using System.Text.Json;
using YoutubeDl.Engine.Media;

internal static partial class FfmpegChecks
{
    private static async Task LegacyHintMedia(string ffmpeg, bool metadata)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "hinted.mp4"); var output = Path.Combine(files.Directory, "processed.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x96:r=24:d=2", "-f", "lavfi", "-i", "sine=frequency=650:sample_rate=48000:duration=2", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-movflags", "+rtphint", source]);
        var original = await File.ReadAllBytesAsync(source);
        if (metadata) await FfmpegTool.EmbedAsync(ffmpeg, source, output, [], null, new Dictionary<string, string> { ["title"] = "Preserved hinted media" }, default);
        else await FfmpegTool.RemuxAsync(ffmpeg, source, output, default);
        var retained = await File.ReadAllBytesAsync(source); if (!retained.SequenceEqual(original)) throw new InvalidOperationException("Processing changed the original hinted movie.");
        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        using var input = JsonDocument.Parse(await Run(probe, ["-v", "error", "-show_entries", "stream=codec_type,codec_tag_string", "-of", "json", source], rejectDiagnostics: true));
        if (!input.RootElement.GetProperty("streams").EnumerateArray().Any(stream => stream.TryGetProperty("codec_tag_string", out var tag) && tag.GetString() == "rtp ")) throw new InvalidOperationException("Generated source had no real hint tracks.");
        using var result = JsonDocument.Parse(await Run(probe, ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", output], rejectDiagnostics: true));
        var streams = result.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        if (streams.Length != 2 || streams.Single(stream => stream.GetProperty("codec_type").GetString() == "video").GetProperty("nb_read_frames").GetString() != "48") throw new InvalidOperationException("Hint filtering lost playback tracks or video frames.");
        foreach (var type in new[] { "v:0", "a:0" })
        {
            var before = await Run(probe, ["-v", "error", "-select_streams", type, "-show_packets", "-show_data_hash", "sha256", "-show_entries", "packet=data_hash", "-of", "json", source], rejectDiagnostics: true);
            var after = await Run(probe, ["-v", "error", "-select_streams", type, "-show_packets", "-show_data_hash", "sha256", "-show_entries", "packet=data_hash", "-of", "json", output], rejectDiagnostics: true);
            if (before != after) throw new InvalidOperationException("Hint processing changed encoded playback payloads or packet order.");
        }
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", output, "-map", "0:v", "-map", "0:a", "-f", "null", "-"], rejectDiagnostics: true);
    }
}
