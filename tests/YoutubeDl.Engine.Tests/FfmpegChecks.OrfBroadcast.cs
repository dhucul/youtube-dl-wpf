using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;

internal static partial class FfmpegChecks
{
    internal static readonly string[] OrfBroadcastModes = ["on-hls", "on-dash", "on-renewal", "on-caption", "on-collection", "on-stale", "on-denied", "on-no-overwrite", "radio-direct", "radio-sequence", "radio-sequence-layout", "radio-sequence-corrupt", "radio-sequence-clock", "radio-sequence-no-overwrite", "radio-sequence-interrupted", "radio-item", "radio-live", "radio-packed-live", "radio-conversion", "radio-renewal", "radio-collection", "radio-interrupted"];
    internal static IEnumerable<(string Name, Func<Task> Test)> OrfBroadcastMediaChecks(string ffmpeg) => OrfBroadcastModes.Select(mode => ("ORF broadcast media " + mode, (Func<Task>)(() => OrfBroadcastMedia(ffmpeg, mode))));
    internal static async Task OrfBroadcastMedia(string ffmpeg, string mode, string? retained = null)
    {
        using var files = new Files(retained, retained is null); var family = mode.Split('-')[0]; var operation = mode[(mode.IndexOf('-') + 1)..]; var sequence = operation.StartsWith("sequence", StringComparison.Ordinal); var f = new OrfBroadcastChecks.Fixture(family) { Mode = sequence ? "sequence" : operation is "item" or "renewal" or "caption" or "denied" ? operation : "metadata", Live = operation is "live" or "packed-live" };
        var source = Path.Combine(files.Directory, family == "on" ? "source.mp4" : "source.mp3");
        if (family == "on")
        {
            await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=128x72:rate=12:duration=3", "-f", "lavfi", "-i", "sine=frequency=410:sample_rate=48000:duration=3", "-c:v", "libx264", "-g", "12", "-keyint_min", "12", "-sc_threshold", "0", "-c:a", "aac", "-t", "3", source]);
            await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-map", "0", "-c", "copy", "-f", "hls", "-hls_time", "1", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(files.Directory, "segment%03d.ts"), Path.Combine(files.Directory, "master.m3u8")]);
            await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-map", "0", "-c", "copy", "-f", "dash", "-seg_duration", "1", Path.Combine(files.Directory, "master.mpd")]);
            await File.WriteAllTextAsync(Path.Combine(files.Directory, "owned.vtt"), "WEBVTT\n\n00:00:00.100 --> 00:00:01.000\nOwned ORF caption\n");
        }
        else
        {
            await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=410:sample_rate=48000:duration=3", "-c:a", "libmp3lame", "-b:a", "128k", source]);
            if (sequence)
            {
                File.Copy(source, Path.Combine(files.Directory, "source-part0.mp3"));
                await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=820:sample_rate=48000:duration=" + (operation == "sequence-clock" ? "3.2" : "3"), "-ac", operation == "sequence-layout" ? "2" : "1", "-c:a", "libmp3lame", "-b:a", "128k", Path.Combine(files.Directory, "source-part1.mp3")]);
                if (operation == "sequence-corrupt") await File.WriteAllTextAsync(Path.Combine(files.Directory, "source-part1.mp3"), "owned invalid audio");
            }
            if (operation == "packed-live") await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-c:a", "aac", "-f", "segment", "-segment_time", "1", "-segment_list", Path.Combine(files.Directory, "master.m3u8"), "-segment_format", "adts", Path.Combine(files.Directory, "segment%03d.aac")]);
            else if (f.Live) await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-c:a", "aac", "-f", "hls", "-hls_time", "1", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(files.Directory, "segment%03d.ts"), Path.Combine(files.Directory, "master.m3u8")]);
            if (f.Live)
            {
                var playlist = Path.Combine(files.Directory, "master.m3u8"); var text = await File.ReadAllTextAsync(playlist); var at = text.IndexOf("#EXTINF:", StringComparison.Ordinal); await File.WriteAllTextAsync(playlist, text.Insert(at, "#EXT-X-PROGRAM-DATE-TIME:2020-01-01T00:00:00Z\n"));
            }
        }
        f.Data = await File.ReadAllBytesAsync(source); f.Files = Directory.GetFiles(files.Directory).Where(path => path != source).ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes);
        var collection = operation is "collection" or "stale"; var request = OrfBroadcastChecks.Request(f, Path.Combine(files.Directory, "output"), collection) with { FfmpegPath = ffmpeg, KeepOriginal = true, DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt"), PlaylistItems = collection ? "1" : "", FormatSelector = family == "on" ? operation == "dash" ? "dash-Q4A" : "hls-Q4A" : "", LiveDurationSeconds = f.Live ? 4 : null, AudioOnly = operation == "conversion", AudioFormat = "flac", WriteSubs = operation == "caption", EmbedSubs = operation == "caption", SubLanguages = "de", SubFormat = "vtt" };
        if (collection)
        {
            var info = await f.Engine.InspectAsync(request with { ExpandPlaylist = true }, default); request = request with { PlaylistSnapshot = info.PlaylistSnapshot };
            if (operation == "stale") { f.Mode = "profile"; await ProviderFamiliesChecks.Expect<InvalidDataException>(() => f.Engine.DownloadAsync(request, null, null, default)); OrfBroadcastChecks.Assert(f.Transfers == 0 && !File.Exists(request.DownloadArchiveFile), "ORF downloaded changed inspected membership."); return; }
        }
        if (operation == "denied")
        {
            var denied = await f.Engine.DownloadAsync(request, null, null, default); OrfBroadcastChecks.Assert(!denied.Succeeded && f.Transfers == 0 && !File.Exists(request.DownloadArchiveFile), "A denied ORF episode published media.");
            foreach (var invalid in new[] { "short-media", "wrong-role" })
            {
                var damaged = new OrfBroadcastChecks.Fixture("on") { Mode = invalid, Data = f.Data, Files = f.Files };
                if (invalid == "wrong-role")
                {
                    var directory = Path.Combine(files.Directory, "audio-only"); Directory.CreateDirectory(directory);
                    await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-vn", "-c:a", "copy", "-f", "hls", "-hls_time", "1", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(directory, "segment%03d.ts"), Path.Combine(directory, "master.m3u8")]);
                    damaged.Files = Directory.GetFiles(directory).ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes);
                }
                var rejected = request with { OutputDirectory = Path.Combine(files.Directory, invalid) }; var completed = new List<string>(); await ProviderFamiliesChecks.Expect<Exception>(() => damaged.Engine.DownloadAsync(rejected, null, null, default, completed.Add));
                OrfBroadcastChecks.Assert(completed.Count == 0 && !File.Exists(request.DownloadArchiveFile), "An incomplete or wrong-role ORF source published success.");
            }
            return;
        }
        if (operation is "sequence-layout" or "sequence-corrupt" or "sequence-clock")
        {
            var completed = new List<string>(); await ProviderFamiliesChecks.Expect<Exception>(() => f.Engine.DownloadAsync(request, null, null, default, completed.Add)); OrfBroadcastChecks.Assert(completed.Count == 0 && !File.Exists(request.DownloadArchiveFile) && !Directory.GetFiles(request.OutputDirectory, "recording.*").Any(), "Invalid ordered MP3 audio published files or archive state."); return;
        }
        if (operation is "no-overwrite" or "sequence-no-overwrite")
        {
            Directory.CreateDirectory(request.OutputDirectory); var container = family == "on" ? "mp4" : "mp3"; var saved = Path.Combine(request.OutputDirectory, "recording." + container); await File.WriteAllTextAsync(saved, "previous owned media");
            var skipped = await f.Engine.DownloadAsync(request with { NoOverwrites = true, Container = container, KeepOriginal = false }, null, null, default); OrfBroadcastChecks.Assert(skipped.Succeeded && skipped.Skipped && f.PayloadTransfers == 0 && await File.ReadAllTextAsync(saved) == "previous owned media", "ORF transferred media during no-overwrite protection: " + skipped.Error); return;
        }
        if (operation is "interrupted" or "sequence-interrupted")
        {
            PublicationFaults.Observer = phase => phase == "processing" ? throw new IOException("Owned ORF interruption") : Task.CompletedTask;
            try { await ProviderFamiliesChecks.Expect<IOException>(() => f.Engine.DownloadAsync(request, null, null, default)); } finally { PublicationFaults.Observer = null; }
            OrfBroadcastChecks.Assert(!File.Exists(request.DownloadArchiveFile), "An interrupted ORF publication updated the archive.");
        }
        var callbacks = new List<string>(); var transferLog = new List<string>(); var result = await f.Engine.DownloadAsync(request, null, transferLog.Add, default, callbacks.Add);
        var main = result.OutputPath ?? (collection ? result.OutputPaths.SingleOrDefault(path => Path.GetFileNameWithoutExtension(path) == "001 - recording") : null);
        OrfBroadcastChecks.Assert(result.Succeeded && main is not null && callbacks.Count > 0, "ORF media workflow failed: " + result.Error + " " + string.Join("; ", transferLog));
        await Run(ffmpeg, ["-v", "error", "-xerror", "-i", main!, "-f", "null", "-"]);
        var transferred = f.Transfers; var repeat = await f.Engine.DownloadAsync(request, null, null, default); OrfBroadcastChecks.Assert(repeat.Succeeded && repeat.Skipped && transferred == f.Transfers, "An ORF archive repeat transferred media.");
        if (sequence)
        {
            foreach (var part in new[] { "source-part0.mp3", "source-part1.mp3" }) OrfBroadcastChecks.Assert(result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(f.Files[part])), "ORF did not retain an exact ordered radio source part.");
            OrfBroadcastChecks.Assert(new FileInfo(result.OutputPath!).Length > f.Data.Length, "ORF lost a complete ordered radio source part.");
        }
        if (operation == "caption") OrfBroadcastChecks.Assert(result.SidecarPaths.Any(path => path.EndsWith(".vtt", StringComparison.Ordinal)), "ORF did not save the selected caption.");
        if (retained is not null) await File.WriteAllTextAsync(Path.Combine(files.Directory, "verification.json"), JsonSerializer.Serialize(new { mode, Source = source, OutputPath = main, result.OutputPaths, result.SidecarPaths, f.Api, f.Transfers, f.Generation, Callbacks = callbacks.Count }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
