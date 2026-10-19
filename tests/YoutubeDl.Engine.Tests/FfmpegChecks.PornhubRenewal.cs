using System.Text;
using YoutubeDl.Engine;

internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> PornhubRenewalMediaChecks(string ffmpeg) => new[] { "video", "audio", "midway" }
        .Select(mode => ("Pornhub renewal media " + mode, (Func<Task>)(() => PornhubRenewalMedia(ffmpeg, mode))));
    private static async Task PornhubRenewalMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4"); await MixedSource(ffmpeg, source);
        await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-c:v", "libx264", "-preset", "ultrafast", "-g", "12", "-sc_threshold", "0", "-c:a", "copy", "-hls_time", "1", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(files.Directory, "part%d.ts"), Path.Combine(files.Directory, "child.m3u8")]);
        var content = Directory.GetFiles(files.Directory).Where(path => Path.GetExtension(path) is ".ts" or ".m3u8").ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes);
        var child = Encoding.UTF8.GetString(content["child.m3u8"]!);
        // Each fresh signed manifest explicitly declares the generation of every segment.
        var fixture = new PornhubRenewalChecks.Fixture { Mode = "media-" + mode, Media = content! };
        content["master.m3u8"] = Encoding.UTF8.GetBytes("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=500000,RESOLUTION=426x240\nchild.m3u8?generation=1\n");
        content["child.m3u8"] = Encoding.UTF8.GetBytes(child);
        var request = fixture.Request(Path.Combine(files.Directory, "output")) with { FfmpegPath = ffmpeg, KeepOriginal = true,
            AudioOnly = mode == "audio",
            AudioFormat = "mp3", AudioQuality = "192K", DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") };
        var callbacks = new List<string>(); var result = await fixture.Engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!result.Succeeded || result.OutputPath is null || fixture.Pages != 2) throw new Exception("Automatic renewal did not complete: " + result.Error + "; pages=" + fixture.Pages);
        await Run(ffmpeg, ["-v", "error", "-xerror", "-err_detect", "explode", "-i", result.OutputPath, "-f", "null", "-"]);
        if (fixture.SuccessfulParts.GetValueOrDefault("part0.ts") != 1 || fixture.SuccessfulParts.GetValueOrDefault("part1.ts") != 1) throw new Exception("Completed prefix was retransferred or a segment was lost.");
        var transfers = fixture.SuccessfulParts.Where(part => part.Key.EndsWith(".ts", StringComparison.Ordinal)).Sum(part => part.Value); var completed = callbacks.Count;
        var skipped = await fixture.Engine.DownloadAsync(request with { NoOverwrites = true, DownloadArchiveFile = "" }, null, null, default, callbacks.Add);
        var archived = await fixture.Engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!skipped.Skipped || !archived.Skipped || !skipped.Succeeded || !archived.Succeeded || fixture.SuccessfulParts.Where(part => part.Key.EndsWith(".ts", StringComparison.Ordinal)).Sum(part => part.Value) != transfers || callbacks.Count != completed) throw new Exception("Repeated job transferred media after renewal.");
    }
}
