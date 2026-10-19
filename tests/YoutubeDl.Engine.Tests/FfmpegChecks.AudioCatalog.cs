using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;

internal static partial class FfmpegChecks
{
    internal static readonly string[] AudioCatalogModes = ["mp3", "m4a", "flac", "renewal", "no-overwrite", "interrupted", "denied", "changed-renewal", "paced"];
    internal static IEnumerable<(string Name, Func<Task> Test)> AudioCatalogMediaChecks(string ffmpeg) => AudioCatalogModes.Select(mode => ("Audio catalog media " + mode, (Func<Task>)(() => AudioCatalogMedia(ffmpeg, mode))));
    internal static async Task AudioCatalogMedia(string ffmpeg, string mode, string? retained = null)
    {
        using var files = new Files(retained, retained is null); var extension = mode == "m4a" ? "m4a" : "mp3"; var source = Path.Combine(files.Directory, "source." + extension);
        await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=741:sample_rate=48000:duration=3", "-c:a", extension == "m4a" ? "aac" : "libmp3lame", "-b:a", "128k", source]);
        var fixture = new AudioCatalogChecks.Fixture { Mode = mode is "renewal" or "denied" or "changed-renewal" ? mode : "metadata", Extension = extension, Bytes = await File.ReadAllBytesAsync(source) };
        var request = fixture.Request(Path.Combine(files.Directory, "output")) with { FfmpegPath = ffmpeg, KeepOriginal = true, AudioOnly = mode == "flac", AudioFormat = "flac", RequestSleepSeconds = mode == "paced" ? .02 : null, DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") }; var callbacks = new List<string>(); var logs = new List<string>();
        if (mode == "denied") { var denied = await fixture.Engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add); AudioCatalogChecks.Assert(!denied.Succeeded && fixture.Transfers == 0 && callbacks.Count == 0 && !File.Exists(request.DownloadArchiveFile), "Denied ARD audio published output."); return; }
        if (mode == "changed-renewal") { await ProviderFamiliesChecks.Expect<InvalidDataException>(() => fixture.Engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add)); AudioCatalogChecks.Assert(fixture.Transfers == 0 && callbacks.Count == 0 && !File.Exists(request.DownloadArchiveFile), "Changed audio renewal published another source."); return; }
        if (mode == "interrupted")
        {
            PublicationFaults.Observer = phase => phase == "processing" ? throw new IOException("Owned audio processing interruption") : Task.CompletedTask;
            try { await ProviderFamiliesChecks.Expect<IOException>(() => fixture.Engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add)); } finally { PublicationFaults.Observer = null; }
            AudioCatalogChecks.Assert(callbacks.Count == 0 && !File.Exists(request.DownloadArchiveFile), "Interrupted audio recorded completion.");
        }
        var result = await fixture.Engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add); AudioCatalogChecks.Assert(result.Succeeded && result.OutputPath is not null && callbacks.Count > 0, "ARD audio media failed: " + result.Error + " " + string.Join(" | ", logs.TakeLast(8)));
        await Run(ffmpeg, ["-v", "error", "-xerror", "-i", result.OutputPath!, "-map", "0:a:0", "-f", "null", "-"]);
        if (mode != "flac") AudioCatalogChecks.Assert((await File.ReadAllBytesAsync(result.OutputPath!)).AsEnumerable().SequenceEqual(fixture.Bytes), "Complete progressive audio bytes changed.");
        var transfers = fixture.Transfers; var completed = callbacks.Count;
        if (mode == "no-overwrite") { var skip = await fixture.Engine.DownloadAsync(request with { NoOverwrites = true, DownloadArchiveFile = "" }, null, logs.Add, default); AudioCatalogChecks.Assert(skip.Succeeded && skip.Skipped && fixture.Transfers == transfers, "No-overwrite retransferred completed audio."); }
        var archived = await fixture.Engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add); AudioCatalogChecks.Assert(archived.Succeeded && archived.Skipped && fixture.Transfers == transfers && callbacks.Count == completed, "Archived audio retransferred or repeated callbacks.");
        if (retained is not null) await File.WriteAllTextAsync(Path.Combine(files.Directory, "verification.json"), JsonSerializer.Serialize(new { mode, Source = source, result.OutputPath, result.OutputPaths, result.SidecarPaths, Transfers = transfers, Generations = fixture.Generation, Callbacks = callbacks.Count }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
