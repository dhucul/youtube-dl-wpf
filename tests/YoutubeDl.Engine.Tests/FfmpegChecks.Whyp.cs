using System.Text.Json;
using YoutubeDl.Engine;

internal static partial class FfmpegChecks
{
    internal static readonly string[] WhypModes = ["mp3", "flac", "renew", "changed-version", "short", "cancel"];
    internal static IEnumerable<(string Name, Func<Task> Test)> WhypMediaChecks(string ffmpeg) => WhypModes.Select(mode => ("Whyp media " + mode, (Func<Task>)(() => WhypMedia(ffmpeg, mode))));
    private static async Task WhypMedia(string ffmpeg, string mode, string? retained = null)
    {
        using var files = new Files(retained, retained is null); var extension = mode == "flac" ? "flac" : "mp3"; var source = Path.Combine(files.Directory, "source." + extension);
        await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=sample_rate=48000:duration=" + (mode == "short" ? "0.3" : "3"), "-c:a", extension == "flac" ? "flac" : "libmp3lame", source]);
        var fixture = new WhypChecks.Fixture { Data = await File.ReadAllBytesAsync(source), Extension = extension, Renew = mode is "renew" or "changed-version", ChangeVersion = mode == "changed-version" };
        var engine = fixture.Engine; var callbacks = new List<string>(); var output = Path.Combine(files.Directory, "result." + extension); await File.WriteAllTextAsync(output, "previous");
        var request = WhypChecks.Request with { OutputDirectory = files.Directory, FileName = "result", FfmpegPath = ffmpeg, AddMetadata = true, KeepOriginal = true, DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt"), PostprocessorArguments = ["Metadata:-metadata comment=whyp-proof"] };
        using var cancellation = new CancellationTokenSource(); if (mode == "cancel") cancellation.Cancel();
        NativeDownloadResult? result = null;
        try { result = await engine.DownloadAsync(request, null, null, cancellation.Token, callbacks.Add); } catch (Exception ex) when (mode is "changed-version" or "short" or "cancel" && ex is InvalidDataException or InvalidOperationException or OperationCanceledException) { }
        if (mode is "changed-version" or "short" or "cancel")
        {
            ProcessingOptionsChecks.Assert(result?.Succeeded != true && callbacks.Count == 0 && await File.ReadAllTextAsync(output) == "previous" && !File.Exists(request.DownloadArchiveFile), "Failed Whyp media changed prior output/archive/callbacks."); return;
        }
        ProcessingOptionsChecks.Assert(result!.Succeeded && result.OutputPaths.Count == 2 && callbacks.Count == 2, "Whyp did not publish processed audio and its original.");
        foreach (var path in result.OutputPaths)
        {
            await Run(ffmpeg, ["-v", "error", "-xerror", "-i", path, "-f", "null", "-"]);
            ProcessingOptionsChecks.Assert((await AdaptivePacketHashes(ffmpeg, source, "a:0")).AsEnumerable().SequenceEqual(await AdaptivePacketHashes(ffmpeg, path, "a:0")), "Whyp coded audio changed during native publication.");
        }
        ProcessingOptionsChecks.Assert(mode != "renew" || fixture.Rejects == 1 && fixture.Metadata == 2, "Whyp did not renew exactly one rejected signed resource.");
        var before = (fixture.Media, callbacks.Count); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        ProcessingOptionsChecks.Assert(repeat.Succeeded && repeat.Skipped && before == (fixture.Media, callbacks.Count), "Archived Whyp audio transferred again or emitted callbacks.");
        if (retained is not null) await File.WriteAllTextAsync(Path.Combine(files.Directory, "verification.json"), JsonSerializer.Serialize(new { Version = NativeEngine.Version, Mode = mode, Source = source, Files = result.OutputPaths, fixture.Metadata, fixture.Media, fixture.Rejects, RepeatMediaRequests = 0, RepeatCallbacks = 0 }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
