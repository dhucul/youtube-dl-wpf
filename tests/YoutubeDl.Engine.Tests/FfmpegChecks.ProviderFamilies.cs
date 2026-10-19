using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;

internal static partial class FfmpegChecks
{
    internal static readonly string[] ProviderFamiliesModes = ["tvp-hls", "tvp-dash", "tvp-live", "tvp-collection", "tvp-renewal", "arte-hls", "arte-embed", "arte-collection", "arte-renewal", "orf-direct", "orf-conversion", "orf-collection", "orf-renewal", "tvp-denied", "arte-stale", "orf-stale", "orf-interrupted"];
    internal static IEnumerable<(string Name, Func<Task> Test)> ProviderFamiliesMediaChecks(string ffmpeg) => ProviderFamiliesModes.Select(mode => ("Provider families media " + mode, (Func<Task>)(() => ProviderFamiliesMedia(ffmpeg, mode))));
    internal static async Task ProviderFamiliesMedia(string ffmpeg, string mode, string? retained = null)
    {
        using var files = new Files(retained, retained is null); var family = mode[..mode.IndexOf('-')]; var operation = mode[(mode.IndexOf('-') + 1)..]; var fixture = new ProviderFamiliesChecks.Fixture(family);
        var source = Path.Combine(files.Directory, family == "orf" ? "source.mp3" : "source.mp4");
        if (family == "orf") await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=420:sample_rate=48000:duration=3", "-c:a", "libmp3lame", "-b:a", "128k", source]);
        else
        {
            await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=128x72:rate=12:duration=3", "-f", "lavfi", "-i", "sine=frequency=420:sample_rate=48000:duration=3", "-c:v", "libx264", "-g", "12", "-keyint_min", "12", "-sc_threshold", "0", "-c:a", "aac", "-t", "3", source]);
            await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-map", "0", "-c", "copy", "-f", "hls", "-hls_time", "1", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(files.Directory, "segment%03d.ts"), Path.Combine(files.Directory, "master.m3u8")]);
            await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-map", "0", "-c", "copy", "-f", "dash", "-seg_duration", "1", "-use_timeline", "1", "-use_template", "1", Path.Combine(files.Directory, "master.mpd")]);
            fixture.Files = Directory.GetFiles(files.Directory).Where(path => Path.GetFileName(path) != "source.mp4").ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes);
        }
        fixture.Data = await File.ReadAllBytesAsync(source); fixture.Live = operation == "live"; fixture.Mode = operation == "renewal" ? "renewal" : operation == "denied" ? "denied" : "metadata";
        var collection = operation is "collection" or "stale"; var request = ProviderFamiliesChecks.Request(fixture, Path.Combine(files.Directory, "output"), collection) with { FfmpegPath = ffmpeg, KeepOriginal = true, DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt"), AudioOnly = operation == "conversion", AudioFormat = "flac", PlaylistItems = collection ? "2" : "", FormatSelector = operation == "dash" ? "dash-0" : family == "tvp" ? "hls-0" : "", LiveDurationSeconds = operation == "live" ? 4 : null };
        if (operation == "embed") request = request with { Url = new Uri("https://www.arte.tv/player/v7/index.php?json_url=" + Uri.EscapeDataString("https://api.arte.tv/api/player/v2/config/fr/123456-001-A")) };
        if (collection)
        {
            var inspected = await fixture.Engine.InspectAsync(request with { ExpandPlaylist = true }, default); request = request with { PlaylistSnapshot = inspected.PlaylistSnapshot };
            if (operation == "stale")
            {
                fixture.Mode = "moved"; var callbacks = new List<string>(); try { var changed = await fixture.Engine.DownloadAsync(request, null, null, default, callbacks.Add); ProviderFamiliesChecks.Assert(!changed.Succeeded, "An inspected collection selected replacement membership."); } catch (InvalidDataException) { }
                ProviderFamiliesChecks.Assert(fixture.Transfers == 0 && callbacks.Count == 0 && !File.Exists(request.DownloadArchiveFile), "Changed public membership transferred or published media."); return;
            }
        }
        if (operation == "denied") { var denied = await fixture.Engine.DownloadAsync(request, null, null, default); ProviderFamiliesChecks.Assert(!denied.Succeeded && fixture.Transfers == 0 && !File.Exists(request.DownloadArchiveFile), "A denied public programme published media."); return; }
        if (operation == "interrupted")
        {
            PublicationFaults.Observer = phase => phase == "processing" ? throw new IOException("Owned provider processing interruption") : Task.CompletedTask;
            try { await ProviderFamiliesChecks.Expect<IOException>(() => fixture.Engine.DownloadAsync(request, null, null, default)); } finally { PublicationFaults.Observer = null; }
            ProviderFamiliesChecks.Assert(!File.Exists(request.DownloadArchiveFile), "Interrupted public processing recorded an archive entry.");
        }
        var completed = new List<string>(); var result = await fixture.Engine.DownloadAsync(request, null, null, default, completed.Add);
        ProviderFamiliesChecks.Assert(result.Succeeded && result.OutputPath is not null && completed.Count > 0, "A native provider media workflow failed to publish.");
        await Run(ffmpeg, ["-v", "error", "-xerror", "-i", result.OutputPath!, "-f", "null", "-"]);
        var transferred = fixture.Transfers; var repeat = await fixture.Engine.DownloadAsync(request, null, null, default); ProviderFamiliesChecks.Assert(repeat.Succeeded && repeat.Skipped && transferred == fixture.Transfers, "An archived public programme transferred media again.");
        if (retained is not null) await File.WriteAllTextAsync(Path.Combine(files.Directory, "verification.json"), JsonSerializer.Serialize(new { mode, Source = source, result.OutputPath, result.OutputPaths, fixture.Api, fixture.Transfers, fixture.Generation, Callbacks = completed.Count }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
