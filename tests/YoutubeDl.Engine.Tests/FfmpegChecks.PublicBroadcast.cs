using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;

internal static partial class FfmpegChecks
{
    internal static readonly string[] PublicBroadcastModes = ["ard-direct", "ard-captions", "ard-renewal", "ard-segment-renewal", "ard-no-overwrite", "ard-interrupted", "ard-denied", "ard-changed-renewal", "zdf-direct", "zdf-flac", "zdf-renewal", "zdf-segment-renewal", "zdf-no-overwrite", "zdf-interrupted", "zdf-denied", "zdf-changed-renewal", "nrk-direct", "nrk-captions", "nrk-renewal", "nrk-segment-renewal", "nrk-no-overwrite", "nrk-interrupted", "nrk-denied", "nrk-changed-renewal"];
    private static readonly AsyncLocal<double?> PublicSubtitleFixtureDelay = new();
    internal static IEnumerable<(string Name, Func<Task> Test)> PublicBroadcastMediaChecks(string ffmpeg) => PublicBroadcastModes.Select(mode => ("Public broadcast media " + mode, (Func<Task>)(() => PublicBroadcastMedia(ffmpeg, mode))))
        .Concat(new[] { "hls-resolved", "dash-resolved", "hls-live", "dash-live" }.Select(mode => ("Public broadcast caption policy " + mode, (Func<Task>)(() => PublicCaptionDelayMedia(ffmpeg, mode)))));
    private static async Task PublicCaptionDelayMedia(string ffmpeg, string mode)
    {
        var previous = PublicSubtitleFixtureDelay.Value; PublicSubtitleFixtureDelay.Value = .1;
        try
        {
            if (mode == "hls-resolved") await HlsCaptionMedia(ffmpeg, "ranged");
            else if (mode == "dash-resolved") await DashCaptionMedia(ffmpeg);
            else await LiveCaptionMedia(ffmpeg, mode == "hls-live" ? "hls" : "dash-wvtt");
        }
        finally { PublicSubtitleFixtureDelay.Value = previous; }
    }
    internal static async Task PublicBroadcastMedia(string ffmpeg, string mode, string? retained = null)
    {
        using var files = new Files(retained, retained is null); var family = mode[..3]; var operation = mode[4..]; var source = Path.Combine(files.Directory, "source.mp4"); await MixedSource(ffmpeg, source); var manifest = Path.Combine(files.Directory, "master.m3u8");
        await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-c", "copy", "-hls_time", "1", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(files.Directory, "part%03d.ts"), manifest]);
        var fixture = new PublicBroadcastChecks.Fixture(family) { Mode = operation is "renewal" or "segment-renewal" or "denied" or "changed-renewal" ? operation : "metadata", Master = await File.ReadAllBytesAsync(manifest), Files = Directory.GetFiles(files.Directory).Where(path => Path.GetFileName(path) != "source.mp4").ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes) };
        var request = fixture.Request(Path.Combine(files.Directory, "output")) with { FfmpegPath = ffmpeg, KeepOriginal = true, AudioOnly = operation == "flac", AudioFormat = "flac", WriteSubs = operation == "captions", EmbedSubs = operation == "captions", SubLanguages = "all", SubtitleSleepSeconds = operation == "captions" ? .08 : null, DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") }; var callbacks = new List<string>(); var logs = new List<string>();
        if (operation == "denied") { var denied = await fixture.Engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add); PublicBroadcastChecks.Assert(!denied.Succeeded && fixture.Transfers == 0 && callbacks.Count == 0 && !File.Exists(request.DownloadArchiveFile), "An ungranted public programme published media."); return; }
        if (operation == "changed-renewal") { await ProviderFamiliesChecks.Expect<InvalidDataException>(() => fixture.Engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add)); PublicBroadcastChecks.Assert(fixture.Transfers == 0 && callbacks.Count == 0 && !File.Exists(request.DownloadArchiveFile), "A changed public edition published replacement media."); return; }
        if (operation == "interrupted")
        {
            PublicationFaults.Observer = phase => phase == "processing" ? throw new IOException("Owned public broadcast processing interruption") : Task.CompletedTask;
            try { await ProviderFamiliesChecks.Expect<IOException>(() => fixture.Engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add)); } finally { PublicationFaults.Observer = null; }
            PublicBroadcastChecks.Assert(callbacks.Count == 0 && !File.Exists(request.DownloadArchiveFile), "Interrupted public playback recorded completion.");
        }
        var result = await fixture.Engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add); PublicBroadcastChecks.Assert(result.Succeeded && result.OutputPath is not null && callbacks.Count > 0, "Public broadcast media failed: " + result.Error + " " + string.Join(" | ", logs.TakeLast(8)));
        await Run(ffmpeg, ["-v", "error", "-xerror", "-i", result.OutputPath!, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"]);
        if (operation == "captions") PublicBroadcastChecks.Assert(result.SidecarPaths.Count(path => path.EndsWith(".vtt", StringComparison.Ordinal)) == 1, "A delayed public caption was not saved.");
        var count = fixture.Transfers; var notifications = callbacks.Count;
        if (operation == "no-overwrite") { var skip = await fixture.Engine.DownloadAsync(request with { NoOverwrites = true, DownloadArchiveFile = "" }, null, logs.Add, default); PublicBroadcastChecks.Assert(skip.Succeeded && skip.Skipped && fixture.Transfers == count, "A completed public source was retransferred by no-overwrite."); }
        var repeat = await fixture.Engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add); PublicBroadcastChecks.Assert(repeat.Succeeded && repeat.Skipped && fixture.Transfers == count && callbacks.Count == notifications, "Archived public playback transferred media or repeated callbacks.");
        if (retained is not null) await File.WriteAllTextAsync(Path.Combine(files.Directory, "verification.json"), JsonSerializer.Serialize(new { mode, Source = source, result.OutputPath, result.OutputPaths, result.SidecarPaths, Transfers = count, Generations = fixture.Generation, Callbacks = callbacks.Count }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
