using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;

internal static partial class FfmpegChecks
{
    internal static readonly string[] PracticalFacebookModes = ["video", "audio", "sd", "renewal", "no-overwrite", "interrupted"];
    internal static IEnumerable<(string Name, Func<Task> Test)> PracticalFacebookMediaChecks(string ffmpeg) => PracticalFacebookModes.Select(mode => ("Practical Facebook media " + mode, (Func<Task>)(() => PracticalFacebookMedia(ffmpeg, mode))));
    internal static async Task PracticalFacebookMedia(string ffmpeg, string mode, string? retained = null)
    {
        using var files = new Files(retained, retained is null); var source = Path.Combine(files.Directory, "source.mp4"); await MixedSource(ffmpeg, source); var fixture = new PracticalFacebookChecks.Fixture { Mode = mode == "renewal" ? mode : "metadata", Bytes = await File.ReadAllBytesAsync(source) };
        var request = fixture.Request(Path.Combine(files.Directory, "output")) with { FfmpegPath = ffmpeg, KeepOriginal = true, AudioOnly = mode == "audio", AudioFormat = "flac", FormatId = mode == "sd" ? "sd" : null, DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") }; var callbacks = new List<string>();
        if (mode == "interrupted") { PublicationFaults.Observer = phase => phase == "processing" ? throw new IOException("Owned Facebook interruption") : Task.CompletedTask; try { await ProviderFamiliesChecks.Expect<IOException>(() => fixture.Engine.DownloadAsync(request, null, null, default, callbacks.Add)); } finally { PublicationFaults.Observer = null; } PracticalFacebookChecks.Assert(callbacks.Count == 0 && !File.Exists(request.DownloadArchiveFile), "Interrupted Facebook media published completion."); }
        var result = await fixture.Engine.DownloadAsync(request, null, null, default, callbacks.Add); PracticalFacebookChecks.Assert(result.Succeeded && result.OutputPath is not null, "Facebook full media failed: " + result.Error); await Run(ffmpeg, ["-v", "error", "-xerror", "-i", result.OutputPath!, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"]);
        if (mode != "audio") PracticalFacebookChecks.Assert((await File.ReadAllBytesAsync(result.OutputPath!)).AsEnumerable().SequenceEqual(fixture.Bytes), "Complete Facebook source bytes changed.");
        var transfers = fixture.Transfers; var completed = callbacks.Count;
        if (mode == "no-overwrite") { var skip = await fixture.Engine.DownloadAsync(request with { NoOverwrites = true, DownloadArchiveFile = "" }, null, null, default); PracticalFacebookChecks.Assert(skip.Succeeded && skip.Skipped && fixture.Transfers == transfers, "Facebook no-overwrite retransferred media."); }
        var archived = await fixture.Engine.DownloadAsync(request, null, null, default, callbacks.Add); PracticalFacebookChecks.Assert(archived.Succeeded && archived.Skipped && fixture.Transfers == transfers && callbacks.Count == completed, "Facebook archive repeated transfer or completion.");
        if (retained is not null) await File.WriteAllTextAsync(Path.Combine(files.Directory, "verification.json"), JsonSerializer.Serialize(new { mode, Source = source, result.OutputPath, result.OutputPaths, Transfers = transfers, Generations = fixture.Generation }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
