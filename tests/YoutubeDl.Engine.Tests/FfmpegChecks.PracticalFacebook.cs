using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;

internal static partial class FfmpegChecks
{
    internal static readonly string[] PracticalFacebookModes = ["video", "audio", "sd", "renewal", "no-overwrite", "interrupted", "mixed-audio-fallback"];
    internal static IEnumerable<(string Name, Func<Task> Test)> PracticalFacebookMediaChecks(string ffmpeg) => PracticalFacebookModes.Select(mode => ("Practical Facebook media " + mode, (Func<Task>)(() => PracticalFacebookMedia(ffmpeg, mode))))
        .Concat(new[] { "video", "audio", "renewal" }.Select(mode => ("Practical Facebook media DASH " + mode, (Func<Task>)(() => PracticalFacebookDashMedia(ffmpeg, mode)))))
        .Concat(new[]{"progressive","progressive-renewal","progressive-audio","dash","dash-renewal","dash-audio"}.Select(mode=>("Practical Facebook media silent "+mode,(Func<Task>)(()=>PracticalFacebookSilentMedia(ffmpeg,mode)))))
        .Concat(new[]{"resume","validator","no-validator","cancel"}.Select(mode=>("Practical Facebook media lifecycle "+mode,(Func<Task>)(()=>PracticalFacebookLifecycle(ffmpeg,mode)))));
    private static async Task PracticalFacebookDashMedia(string ffmpeg, string mode)
    {
        static string[] PayloadHashes(string output) => output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(row => !row.StartsWith('#')).Select(row => row.Split(',')[5].Trim()).ToArray();
        using var files = new Files();
        var source = Path.Combine(files.Directory, "source.mp4"); var video = Path.Combine(files.Directory, "video.mp4"); var audio = Path.Combine(files.Directory, "audio.m4a");
        await MixedSource(ffmpeg, source);
        await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-map", "0:v:0", "-c", "copy", video]);
        await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-map", "0:a:0", "-c", "copy", audio]);
        var fixture = new PracticalFacebookChecks.Fixture { Mode = mode == "renewal" ? "dash-renewal" : "dash-only", VideoBytes = await File.ReadAllBytesAsync(video), AudioBytes = await File.ReadAllBytesAsync(audio) };
        var request = fixture.Request(Path.Combine(files.Directory, "output")) with { FfmpegPath = ffmpeg, AudioOnly = mode == "audio", KeepOriginal = true, DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") };
        var result = await fixture.Engine.DownloadAsync(request, null, null, default);
        PracticalFacebookChecks.Assert(result.Succeeded && result.OutputPath is not null, "Facebook DASH download failed: " + result.Error);
        await Run(ffmpeg, ["-v", "error", "-xerror", "-i", result.OutputPath!, "-map", "0:v?", "-map", "0:a", "-f", "null", "-"]);
        foreach (var role in mode == "audio" ? new[] { "a:0" } : new[] { "v:0", "a:0" })
        {
            var expected = PayloadHashes(await Run(ffmpeg, ["-v", "error", "-i", role == "v:0" ? video : audio, "-map", role, "-c", "copy", "-f", "framehash", "-hash", "sha256", "-"]));
            var actual = PayloadHashes(await Run(ffmpeg, ["-v", "error", "-i", result.OutputPath!, "-map", role, "-c", "copy", "-f", "framehash", "-hash", "sha256", "-"]));
            PracticalFacebookChecks.Assert(expected.Length > 0 && expected.SequenceEqual(actual), "Facebook DASH changed or lost ordered source packets for " + role);
        }
        PracticalFacebookChecks.Assert(fixture.Transfers == (mode == "audio" ? 1 : 2) && fixture.Generation == (mode == "renewal" ? 2 : 1), "Facebook DASH fetched the wrong tracks or failed renewal.");
        var transfers = fixture.Transfers;
        var archived = await fixture.Engine.DownloadAsync(request, null, null, default);
        PracticalFacebookChecks.Assert(archived.Succeeded && archived.Skipped && fixture.Transfers == transfers, "Facebook DASH archive repeated media transfer.");
    }
    internal static async Task PracticalFacebookMedia(string ffmpeg, string mode, string? retained = null)
    {
        using var files = new Files(retained, retained is null); var source = Path.Combine(files.Directory, "source.mp4"); await MixedSource(ffmpeg, source); var fixture = new PracticalFacebookChecks.Fixture { Mode = mode == "renewal" ? mode : mode == "mixed-audio-fallback" ? "silent-dash-mixed-audio" : "metadata", Bytes = await File.ReadAllBytesAsync(source) };
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
