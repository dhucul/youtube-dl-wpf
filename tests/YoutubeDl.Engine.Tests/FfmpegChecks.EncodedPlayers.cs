using System.Text.Json;
using YoutubeDl.Engine;

internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> EncodedPlayerMediaChecks(string ffmpeg) => new[] { "progressive", "hls", "silent", "preference", "short-source" }
        .Select(mode => ("Encoded player media " + mode, (Func<Task>)(() => EncodedPlayerMedia(ffmpeg, mode))));
    internal static async Task EncodedPlayerMedia(string ffmpeg, string mode, string? retained = null)
    {
        using var files = new Files(retained, retained is null); var source = Path.Combine(files.Directory, "source.mp4"); await MixedSource(ffmpeg, source);
        if (mode == "silent") { var silent = Path.Combine(files.Directory, "silent.mp4"); await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-map", "0:v:0", "-c", "copy", silent]); source = silent; }
        var fixture = new EncodedPlayerChecks.Fixture { Mode = mode is "hls" or "preference" ? mode : "metadata", Duration = mode == "short-source" ? 22 : 3, Media = await File.ReadAllBytesAsync(source) };
        if (mode is "hls" or "preference")
        {
            await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-c", "copy", "-hls_time", "1", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(files.Directory, "part%03d.ts"), Path.Combine(files.Directory, "master.m3u8")]);
            fixture.Segments = Directory.GetFiles(files.Directory).Where(path => Path.GetExtension(path) is ".ts" or ".m3u8").ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes);
        }
        var request = fixture.Request(Path.Combine(files.Directory, "output")) with { FfmpegPath = ffmpeg, KeepOriginal = true, DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") }; var callbacks = new List<string>();
        if (mode == "short-source")
        {
            await ProviderFamiliesChecks.Expect<InvalidDataException>(() => fixture.Engine.DownloadAsync(request, null, null, default, callbacks.Add));
            if (callbacks.Count != 0 || File.Exists(request.DownloadArchiveFile) && File.ReadAllText(request.DownloadArchiveFile).Contains("generic", StringComparison.Ordinal)) throw new Exception("An incomplete decoded source was published or archived."); return;
        }
        var result = await fixture.Engine.DownloadAsync(request, null, null, default, callbacks.Add); if (!result.Succeeded || result.OutputPath is null) throw new Exception("Encoded source failed: " + result.Error);
        await Run(ffmpeg, ["-v", "error", "-xerror", "-err_detect", "explode", "-i", result.OutputPath, "-map", "0:v", "-map", "0:a?", "-f", "null", "-"]);
        if (mode is "progressive" or "silent" && !(await File.ReadAllBytesAsync(result.OutputPath)).SequenceEqual(fixture.Media)) throw new Exception("The complete encoded-address source bytes changed.");
        var transfers = fixture.MediaTransfers; var completed = callbacks.Count;
        var skip = await fixture.Engine.DownloadAsync(request with { NoOverwrites = true, DownloadArchiveFile = "" }, null, null, default, callbacks.Add);
        var archive = await fixture.Engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!skip.Succeeded || !skip.Skipped || !archive.Succeeded || !archive.Skipped || fixture.MediaTransfers != transfers || callbacks.Count != completed) throw new Exception("A repeated encoded-source job transferred or republished media.");
        if (retained is not null) await File.WriteAllTextAsync(Path.Combine(files.Directory, "verification.json"), JsonSerializer.Serialize(new { mode, Source = source, result.OutputPath, result.OutputPaths, Transfers = transfers }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
