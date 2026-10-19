using System.Security.Cryptography;
using YoutubeDl.Engine;

internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> SimpleDownloadMediaChecks(string ffmpeg) => new[] { "xvideos-video", "xvideos-audio", "xnxx-video", "xnxx-audio", "silent-hls" }
        .Select(mode => ("Simple downloader media " + mode, (Func<Task>)(() => SimpleDownloadMedia(ffmpeg, mode))));
    private static async Task SimpleDownloadMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4"); await MixedSource(ffmpeg, source);
        if (mode.StartsWith("silent-", StringComparison.Ordinal))
        {
            var silent = Path.Combine(files.Directory, "silent.mp4");
            await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-map", "0:v:0", "-c", "copy", silent]);
            var fixture = new EncodedPlayerChecks.Fixture { Mode = "hls", Duration = 3 };
            await Run(ffmpeg, ["-v", "error", "-y", "-i", silent, "-c", "copy", "-hls_time", "1", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(files.Directory, "part%03d.ts"), Path.Combine(files.Directory, "master.m3u8")]);
            fixture.Segments = Directory.GetFiles(files.Directory).Where(path => Path.GetExtension(path) is ".ts" or ".m3u8").ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes);
            var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
            try { await fixture.Engine.DownloadAsync(fixture.Request(Path.Combine(files.Directory, "output")) with { FfmpegPath = ffmpeg, AudioOnly = true, AudioFormat = "mp3", DownloadArchiveFile = archive }, null, null, default, callbacks.Add); throw new Exception("A silent HLS source published audio."); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("no audio track", StringComparison.Ordinal)) { }
            if (callbacks.Count != 0 || File.Exists(archive) && !string.IsNullOrWhiteSpace(await File.ReadAllTextAsync(archive))) throw new Exception("A silent source was archived or published.");
            return;
        }
        var f = new SimpleDownloadChecks.Fixture { Site = mode.StartsWith("xnxx", StringComparison.Ordinal) ? "xnxx" : "xvideos", Bytes = await File.ReadAllBytesAsync(source) };
        var request = f.Request(Path.Combine(files.Directory, "output")) with { FfmpegPath = ffmpeg, AudioOnly = mode.EndsWith("audio", StringComparison.Ordinal), AudioFormat = "mp3", AudioQuality = "192K", AddMetadata = true, EmbedChapters = true, KeepOriginal = true, DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") };
        var completed = new List<string>(); var result = await f.Engine.DownloadAsync(request, null, null, default, completed.Add);
        if (!result.Succeeded || result.OutputPath is null) throw new Exception("Canonical video download failed.");
        await Run(ffmpeg, ["-v", "error", "-xerror", "-err_detect", "explode", "-i", result.OutputPath, "-f", "null", "-"]);
        var hashes = result.OutputPaths.ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        var pages = f.Pages; var transfers = f.Transfers; var completedCount = completed.Count;
        var repeat = await f.Engine.DownloadAsync(request, null, null, default, completed.Add);
        if (!repeat.Succeeded || !repeat.Skipped || f.Pages != pages || f.Transfers != transfers || completed.Count != completedCount) throw new Exception("Rotating signed links reopened an archived video.");
        var skip = await f.Engine.DownloadAsync(request with { NoOverwrites = true, DownloadArchiveFile = "" }, null, null, default, completed.Add);
        if (!skip.Succeeded || !skip.Skipped || f.Transfers != transfers || completed.Count != completedCount || hashes.Any(pair => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key))) != pair.Value)) throw new Exception("No-overwrite changed the canonical output after signature rotation.");
        if ((await File.ReadAllLinesAsync(request.DownloadArchiveFile!)).Count(line => !string.IsNullOrWhiteSpace(line)) != 1) throw new Exception("One video accumulated multiple archive identities.");
    }
}
