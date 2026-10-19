using System.Text.Json;
using YoutubeDl.Engine;

internal static partial class FfmpegChecks
{
    internal static readonly string[] PracticalPlayerModes = ["jw", "videojs", "flashvars", "html5", "xplayer", "hls", "audio"];
    internal static IEnumerable<(string Name, Func<Task> Test)> PracticalPlayerMediaChecks(string ffmpeg) => PracticalPlayerModes.Select(mode => ("Practical common player media " + mode, (Func<Task>)(() => PracticalPlayerMedia(ffmpeg, mode))));
    internal static async Task PracticalPlayerMedia(string ffmpeg, string mode, string? retained = null)
    {
        using var files = new Files(retained, retained is null); var source = Path.Combine(files.Directory, "source.mp4"); await MixedSource(ffmpeg, source); var fixture = new PracticalPlayerChecks.Fixture { Mode = mode is "audio" or "hls" ? "jw" : mode, Media = await File.ReadAllBytesAsync(source), Hls = mode == "hls" };
        if (fixture.Hls)
        {
            await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-c", "copy", "-hls_time", "1", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(files.Directory, "part%03d.ts"), Path.Combine(files.Directory, "master.m3u8")]);
            fixture.Files = Directory.GetFiles(files.Directory).Where(path => Path.GetExtension(path) is ".ts" or ".m3u8").ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes);
        }
        var request = fixture.Request(Path.Combine(files.Directory, "output")) with { FfmpegPath = ffmpeg, KeepOriginal = true, AudioOnly = mode == "audio", AudioFormat = "flac", DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") }; var callbacks = new List<string>();
        var result = await fixture.Engine.DownloadAsync(request, null, null, default, callbacks.Add); PracticalPlayerChecks.Assert(result.Succeeded && result.OutputPath is not null, "Common player media failed: " + result.Error); await Run(ffmpeg, ["-v", "error", "-xerror", "-i", result.OutputPath!, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"]);
        if (mode is not ("audio" or "hls")) PracticalPlayerChecks.Assert((await File.ReadAllBytesAsync(result.OutputPath!)).AsEnumerable().SequenceEqual(fixture.Media), "Complete common-player source bytes changed.");
        var transfers = fixture.Transfers; var completed = callbacks.Count; var skip = await fixture.Engine.DownloadAsync(request with { NoOverwrites = true, DownloadArchiveFile = "" }, null, null, default); PracticalPlayerChecks.Assert(skip.Succeeded && skip.Skipped && fixture.Transfers == transfers, "Common-player no-overwrite retransferred media.");
        var archived = await fixture.Engine.DownloadAsync(request, null, null, default, callbacks.Add); PracticalPlayerChecks.Assert(archived.Succeeded && archived.Skipped && fixture.Transfers == transfers && callbacks.Count == completed, "Common-player archive repeated transfer or completion.");
        if (retained is not null) await File.WriteAllTextAsync(Path.Combine(files.Directory, "verification.json"), JsonSerializer.Serialize(new { mode, Source = source, result.OutputPath, result.OutputPaths, Transfers = transfers }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
