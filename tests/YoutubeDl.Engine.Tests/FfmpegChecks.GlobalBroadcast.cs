using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;

internal static partial class FfmpegChecks
{
    internal static readonly string[] GlobalBroadcastModes = ["bbc-hls", "bbc-dash", "bbc-flac", "bbc-renewal", "bbc-segment-renewal", "bbc-no-overwrite", "bbc-interrupted", "bbc-denied", "bbc-changed-renewal", "svt-hls", "svt-dash", "svt-captions", "svt-renewal", "svt-segment-renewal", "svt-no-overwrite", "svt-interrupted", "svt-denied", "svt-changed-renewal"];
    internal static IEnumerable<(string Name, Func<Task> Test)> GlobalBroadcastMediaChecks(string ffmpeg) => GlobalBroadcastModes.Select(mode => ("Global broadcast media " + mode, (Func<Task>)(() => GlobalBroadcastMedia(ffmpeg, mode))));
    internal static async Task GlobalBroadcastMedia(string ffmpeg, string mode, string? retained = null)
    {
        using var files = new Files(retained, retained is null); var family = mode[..3]; var operation = mode[4..]; var source = Path.Combine(files.Directory, "source.mp4"); await MixedSource(ffmpeg, source); var dash = operation == "dash"; var manifest = Path.Combine(files.Directory, dash ? "master.mpd" : "master.m3u8");
        if (dash) await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-map", "0:v:0", "-map", "0:a:0", "-c", "copy", "-f", "dash", "-seg_duration", "1", "-use_template", "0", "-use_timeline", "0", manifest]);
        else await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-c", "copy", "-hls_time", "1", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(files.Directory, "part%03d.ts"), manifest]);
        var fixture = new GlobalBroadcastChecks.Fixture(family) { Mode = operation is "renewal" or "segment-renewal" or "denied" or "changed-renewal" ? operation : "metadata", Transport = dash ? "dash" : "hls", Master = await File.ReadAllBytesAsync(manifest), Files = Directory.GetFiles(files.Directory).Where(path => Path.GetFileName(path) != "source.mp4").ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes) };
        var request = fixture.Request(Path.Combine(files.Directory, "output")) with { FfmpegPath = ffmpeg, KeepOriginal = true, AudioOnly = operation == "flac", AudioFormat = "flac", WriteSubs = operation == "captions", SubLanguages = operation == "captions" ? "all" : "", EmbedSubs = operation == "captions", DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") }; var callbacks = new List<string>(); var logs = new List<string>();
        if (operation == "denied") { var result = await fixture.Engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add); GlobalBroadcastChecks.Assert(!result.Succeeded && fixture.Transfers == 0 && callbacks.Count == 0 && !File.Exists(request.DownloadArchiveFile), "Broadcast denial published a substitute."); return; }
        if (operation == "changed-renewal") { await ProviderFamiliesChecks.Expect<InvalidDataException>(() => fixture.Engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add)); GlobalBroadcastChecks.Assert(fixture.Transfers == 0 && callbacks.Count == 0 && !File.Exists(request.DownloadArchiveFile), "A replaced broadcast version published success."); return; }
        if (operation == "interrupted")
        {
            PublicationFaults.Observer = phase => phase == "processing" ? throw new IOException("Owned broadcast processing interruption") : Task.CompletedTask;
            try { await ProviderFamiliesChecks.Expect<IOException>(() => fixture.Engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add)); } finally { PublicationFaults.Observer = null; }
            GlobalBroadcastChecks.Assert(callbacks.Count == 0 && !File.Exists(request.DownloadArchiveFile), "Interrupted broadcast recorded completion.");
        }
        var completed = await fixture.Engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add); GlobalBroadcastChecks.Assert(completed.Succeeded && completed.OutputPath is not null && callbacks.Count > 0, "Broadcast media failed: " + completed.Error + " " + string.Join(" | ", logs.TakeLast(8)));
        await Run(ffmpeg, ["-v", "error", "-xerror", "-i", completed.OutputPath!, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"]);
        if (operation == "captions") { GlobalBroadcastChecks.Assert(completed.SidecarPaths.Count(path => path.EndsWith(".vtt", StringComparison.Ordinal)) == 2, "SVT standard/accessibility caption files collided."); }
        var count = fixture.Transfers; var notifications = callbacks.Count;
        if (operation == "no-overwrite") { var skip = await fixture.Engine.DownloadAsync(request with { NoOverwrites = true, DownloadArchiveFile = "" }, null, logs.Add, default); GlobalBroadcastChecks.Assert(skip.Succeeded && skip.Skipped && fixture.Transfers == count, "An existing broadcast output retransferred media."); }
        var repeat = await fixture.Engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add); GlobalBroadcastChecks.Assert(repeat.Succeeded && repeat.Skipped && fixture.Transfers == count && callbacks.Count == notifications, "Broadcast archive repeat retransferred media or notified again.");
        if (retained is not null) await File.WriteAllTextAsync(Path.Combine(files.Directory, "verification.json"), JsonSerializer.Serialize(new { mode, Source = source, completed.OutputPath, completed.OutputPaths, completed.SidecarPaths, Transfers = count, Generations = fixture.Generation, Callbacks = callbacks.Count }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

