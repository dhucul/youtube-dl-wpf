using System.Net;
using System.Text.Json;

internal static partial class FfmpegChecks
{
    private static async Task RedditMedia(string ffmpeg, bool silent)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4");
        var args = new List<string> { "-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=128x96:r=10:d=1" };
        if (!silent) args.AddRange(["-f", "lavfi", "-i", "sine=frequency=600:sample_rate=48000:duration=1", "-c:a", "aac"]);
        else args.Add("-an");
        args.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p", source]);
        await Run(ffmpeg, args);
        if (!silent)
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", source, "-c", "copy", "-hls_time", "1", "-hls_list_size", "0", "-hls_segment_filename",
                Path.Combine(files.Directory, "segment-%d.ts"), Path.Combine(files.Directory, "HLSPlaylist.m3u8")]);
        var calls = new List<Uri>(); var post = RedditChecks.Post(silent, !silent);
        var result = await RedditChecks.Engine(post, calls, request =>
        {
            var name = Path.GetFileName(request.RequestUri!.AbsolutePath);
            var path = silent ? source : Path.Combine(files.Directory, name);
            if (!silent && name.EndsWith(".mp4")) throw new InvalidOperationException("Reddit selected its silent fallback instead of the complete HLS source.");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(path)) };
        }).DownloadAsync(RedditChecks.Request(files.Directory) with { Container = "mp4", FfmpegPath = ffmpeg }, null, null, default);
        if (!result.Succeeded || result.OutputPath is null) throw new InvalidOperationException("Native Reddit media transfer failed.");
        var probe = await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,codec_name,nb_read_frames", "-of", "json", result.OutputPath]);
        using var document = JsonDocument.Parse(probe);
        var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        if (streams.Single(stream => stream.GetProperty("codec_type").GetString() == "video").GetProperty("nb_read_frames").GetString() != "10" ||
            streams.Count(stream => stream.GetProperty("codec_type").GetString() == "audio") != (silent ? 0 : 1))
            throw new InvalidOperationException("Reddit transfer lost video frames or changed the intended audio presence.");
        if (silent && !File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(result.OutputPath))) throw new InvalidOperationException("A complete silent Reddit source was unnecessarily rewritten.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath, "-f", "null", "-"]);
    }
}
