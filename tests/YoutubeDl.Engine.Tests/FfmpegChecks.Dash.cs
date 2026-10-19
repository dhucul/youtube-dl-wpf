using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;

internal static partial class FfmpegChecks
{
    private static async Task DashMedia(string ffmpeg, bool keep = false, int concurrentFragments = 1, string mergeFormat = "")
    {
        using var files = new Files();
        var manifest = Path.Combine(files.Directory, "source.mpd");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=red:s=128x96:r=10:d=0.6", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100:duration=0.6",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "2", "-c:a", "aac", "-f", "dash", "-seg_duration", "0.2", "-use_template", "1", "-use_timeline", "1", manifest]);
        var xml = await File.ReadAllTextAsync(manifest);
        var assets = System.IO.Directory.EnumerateFiles(files.Directory).ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes);
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request =>
        {
            var name = Path.GetFileName(request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = name == "source.mpd" ? new StringContent(xml) : new ByteArrayContent(assets[name]) };
        })));
        var result = await engine.DownloadAsync(new NativeDownloadRequest
        {
            Url = new Uri("https://media.example/source.mpd"), OutputDirectory = files.Directory, FileName = "result", FfmpegPath = ffmpeg, Retries = 0, KeepOriginal = keep, ConcurrentFragments = concurrentFragments, MergeOutputFormat = mergeFormat
        }, null, null, default);
        if (!result.Succeeded || result.OutputPath is null || Path.GetExtension(result.OutputPath) != (mergeFormat.Length > 0 ? ".mkv" : ".mp4")) throw new InvalidOperationException("The native DASH output is missing or mislabeled.");
        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        var probe = await Run(ffprobe, ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", result.OutputPath]);
        using var document = JsonDocument.Parse(probe);
        var types = document.RootElement.GetProperty("streams").EnumerateArray().Select(stream => stream.GetProperty("codec_type").GetString()).ToArray();
        if (!types.Contains("video") || !types.Contains("audio")) throw new InvalidOperationException("A DASH track was omitted.");
        if (document.RootElement.GetProperty("streams").EnumerateArray().Single(stream => stream.GetProperty("codec_type").GetString() == "video").GetProperty("nb_read_frames").GetString() != "6")
            throw new InvalidOperationException("The native DASH transfer changed the complete video frame count.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", result.OutputPath, "-f", "null", "-"]);
        if (keep) await VerifyKeptComponents(ffmpeg, result, files.Directory);
        if (mergeFormat.Length > 0)
        {
            await File.WriteAllTextAsync(result.OutputPath, "previous processed output"); var callbacks = 0;
            var repeat = await engine.DownloadAsync(new NativeDownloadRequest { Url = new("https://media.example/source.mpd"), OutputDirectory = files.Directory,
                FileName = "result", Retries = 0, FfmpegPath = ffmpeg, MergeOutputFormat = mergeFormat, NoPostOverwrites = true }, null, null, default, _ => callbacks++);
            if (!repeat.Succeeded || callbacks != 0 || await File.ReadAllTextAsync(result.OutputPath) != "previous processed output") throw new InvalidOperationException("DASH replaced a protected merged file.");
        }
    }
}
