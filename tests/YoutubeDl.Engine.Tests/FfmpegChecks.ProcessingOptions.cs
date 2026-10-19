using System.Text.Json;
using YoutubeDl.Engine;

internal static partial class FfmpegChecks
{
    internal static readonly string[] ProcessingModes = ["metadata", "audio", "recode", "remux", "fix-adts", "fix-ts", "warn", "never", "force", "failure", "no-overwrite", "no-post-overwrite", "concurrent"];
    internal static IEnumerable<(string Name, Func<Task> Test)> ProcessingOptionsMediaChecks(string ffmpeg) => ProcessingModes.Select(mode => ("Processing options media " + mode, (Func<Task>)(() => ProcessingOptionsMedia(ffmpeg, mode))));
    private static async Task ProcessingOptionsMedia(string ffmpeg, string mode, string? retained = null)
    {
        using var files = new Files(retained, retained is null); var source = Path.Combine(files.Directory, "source.mp4");
        if (mode == "fix-adts") { source = Path.Combine(files.Directory, "source.aac"); await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=duration=3:sample_rate=48000", "-c:a", "aac", source]); }
        else { await MixedSource(ffmpeg, source); if (mode is "fix-ts" or "warn" or "never") { var ts = Path.Combine(files.Directory, "source.ts"); await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-c", "copy", ts]); source = ts; } }
        var extension = mode == "fix-adts" ? "m4a" : "mp4"; var reads = 0; var callbacks = new List<string>(); var logs = new List<string>();
        var engine = ProcessingOptionsChecks.Engine(await File.ReadAllBytesAsync(source), extension, () => reads++);
        var request = ProcessingOptionsChecks.Request with { OutputDirectory = files.Directory, FileName = "result", FfmpegPath = ffmpeg, DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt"), KeepOriginal = true, AddMetadata = mode is "metadata" or "failure" or "concurrent", Fixup = mode switch { "fix-adts" or "fix-ts" => "detect_or_fix", "warn" => "detect_or_warn", "never" => "never", "force" => "force", _ => null }, PostprocessorArguments = mode switch { "metadata" or "concurrent" => ["Metadata+ffmpeg_o:-metadata title='configured title' -metadata comment=processing-proof"], "audio" => ["ExtractAudio:-b:a 192k -metadata comment=processing-proof"], "recode" => ["VideoConvertor:-crf 20 -preset fast -metadata comment=processing-proof"], "remux" => ["VideoRemuxer:-metadata comment=processing-proof"], "failure" => ["Metadata:-movflags invalid-native-option"], _ => null }, AudioOnly = mode == "audio", AudioFormat = "mp3", RecodeVideo = mode == "recode" ? "mkv" : "", Container = mode == "remux" ? "mkv" : "" };
        if (mode is "no-overwrite" or "no-post-overwrite")
        {
            var path = Path.Combine(files.Directory, "result.mp4"); await File.WriteAllTextAsync(path, "existing");
            request = request with { Fixup = "force", KeepOriginal = false, NoOverwrites = mode == "no-overwrite", NoPostOverwrites = mode == "no-post-overwrite" };
            var skipped = await engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add);
            ProcessingOptionsChecks.Assert(skipped.Succeeded && skipped.Skipped && reads == 0 && callbacks.Count == 0 && await File.ReadAllTextAsync(path) == "existing", "A protected processing target fetched or replaced media."); return;
        }
        if (mode == "failure")
        {
            var path = Path.Combine(files.Directory, "result.mp4"); await File.WriteAllTextAsync(path, "previous");
            try { await engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add); throw new Exception("Invalid FFmpeg override succeeded."); } catch (InvalidOperationException ex) when (!ex.Message.StartsWith("Invalid FFmpeg override", StringComparison.Ordinal)) { }
            ProcessingOptionsChecks.Assert(await File.ReadAllTextAsync(path) == "previous" && callbacks.Count == 0 && !File.Exists(request.DownloadArchiveFile), "Failed override published media/archive/callbacks."); return;
        }
        NativeDownloadResult result;
        if (mode == "concurrent")
        {
            var other = request with { FileName = "other", DownloadArchiveFile = "", PostprocessorArguments = ["Metadata:-metadata title=other-job"] };
            var both = await Task.WhenAll(engine.DownloadAsync(request, null, null, default, callbacks.Add), engine.DownloadAsync(other, null, null, default)); result = both[0];
            var otherInfo = await ProbeText(ffmpeg, both[1].OutputPath!); ProcessingOptionsChecks.Assert(otherInfo.Contains("other-job", StringComparison.Ordinal) && !otherInfo.Contains("configured title", StringComparison.Ordinal), "Concurrent FFmpeg jobs exchanged metadata.");
        }
        else result = await engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add);
        ProcessingOptionsChecks.Assert(result.Succeeded && callbacks.SequenceEqual(result.NewOutputPaths ?? result.OutputPaths), "Processing publication failed.");
        foreach (var path in result.OutputPaths) await Run(ffmpeg, ["-v", "error", "-xerror", "-i", path, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"]);
        var probe = await ProbeText(ffmpeg, result.OutputPath!);
        if (mode is "metadata" or "concurrent") ProcessingOptionsChecks.Assert(probe.Contains("configured title", StringComparison.Ordinal), "Configured metadata was not written.");
        if (mode is "audio" or "recode" or "remux") ProcessingOptionsChecks.Assert(probe.Contains("processing-proof", StringComparison.OrdinalIgnoreCase), "The selected processing stage did not receive its arguments.");
        if (mode is "fix-adts" or "fix-ts") ProcessingOptionsChecks.Assert(probe.Contains("mov,mp4", StringComparison.Ordinal), "Container mismatch was not repaired.");
        if (mode is "warn" or "never") ProcessingOptionsChecks.Assert(probe.Contains("mpegts", StringComparison.Ordinal) && logs.Any(line => line.Contains("warning mode", StringComparison.Ordinal)) == (mode == "warn"), "Warning/never policy changed the media or warning behavior.");
        var inventories = new List<object>();
        if (mode is not ("audio" or "recode"))
            foreach (var role in extension == "m4a" ? new[] { "a:0" } : ["v:0", "a:0"])
            {
                var expected = await AdaptivePacketHashes(ffmpeg, source, role); var actual = await AdaptivePacketHashes(ffmpeg, result.OutputPath!, role);
                if (mode is "fix-adts" or "fix-ts")
                {
                    var comparison = new List<string>();
                    foreach (var item in new[] { source, result.OutputPath! })
                    {
                        var canonical = Path.Combine(files.Directory, Guid.NewGuid().ToString("N") + (role == "a:0" ? ".aac" : ".hash"));
                        await Run(ffmpeg, role == "a:0" ? ["-v", "error", "-y", "-i", item, "-map", "0:a:0", "-c:a", "copy", "-f", "adts", canonical] : ["-v", "error", "-y", "-i", item, "-map", "0:v:0", "-f", "hash", "-hash", "sha256", canonical]);
                        comparison.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(canonical))));
                    }
                    ProcessingOptionsChecks.Assert(comparison[0] == comparison[1], "Container fixup changed canonical audio frames or decoded pictures.");
                }
                else ProcessingOptionsChecks.Assert(expected.SequenceEqual(actual), "A lossless processing override changed coded packets.");
                inventories.Add(new { Role = role, SourcePackets = expected.Length, OutputPackets = actual.Length });
            }
        var before = (reads, callbacks.Count); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        ProcessingOptionsChecks.Assert(repeat.Succeeded && repeat.Skipped && before == (reads, callbacks.Count), "Recorded processing job transferred media or repeated callbacks.");
        if (retained is not null) await File.WriteAllTextAsync(Path.Combine(files.Directory, "verification.json"), JsonSerializer.Serialize(new { Version = NativeEngine.Version, Mode = mode, Source = source, Files = result.OutputPaths, Probe = probe, Inventories = inventories, RepeatMediaRequests = 0, RepeatCallbacks = 0 }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static async Task<string> ProbeText(string ffmpeg, string path)
    {
        var output = Path.Combine(Path.GetDirectoryName(path)!, Guid.NewGuid().ToString("N") + ".probe.json");
        var start = new System.Diagnostics.ProcessStartInfo(YoutubeDl.Engine.Media.FfmpegTool.RequireProbe(ffmpeg)) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-v", "error", "-show_format", "-show_streams", "-of", "json", path }) start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!; var text = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); if (process.ExitCode != 0) throw new Exception(await error); return await text;
    }
}
