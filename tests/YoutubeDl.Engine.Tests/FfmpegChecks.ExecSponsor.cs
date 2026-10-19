using System.Globalization;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;

internal static partial class FfmpegChecks
{
    internal static readonly string[] ExecSponsorModes = ["mark", "remove", "overlap", "prefix", "suffix", "arbitrary", "tail", "vfr", "empty", "denied", "short", "hevc", "extra-audio", "srt", "ttml", "audio", "video", "exec", "cancel", "retry", "no-overwrite"];
    internal static IEnumerable<(string Name, Func<Task> Test)> ExecSponsorMediaChecks(string ffmpeg) => ExecSponsorModes.Select(mode => ("Exec Sponsor media " + mode, (Func<Task>)(() => ExecSponsorMedia(ffmpeg, mode))));
    private static async Task ExecSponsorMedia(string ffmpeg, string mode, string? retained = null)
    {
        using var files = new Files(retained, retained is null); var source = Path.Combine(files.Directory, "source.mp4"); await MixedSource(ffmpeg, source);
        if (mode == "vfr") { var path = Path.Combine(files.Directory, "vfr.mp4"); await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-vf", "select=not(eq(n\\,3)+eq(n\\,8)+eq(n\\,17))", "-fps_mode", "passthrough", "-c:v", "libx264", "-c:a", "copy", path]); source = path; }
        if (mode == "hevc") { var path = Path.Combine(files.Directory, "hevc.mp4"); await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-c:v", "libx265", "-x265-params", "log-level=error", "-c:a", "copy", path]); source = path; }
        if (mode == "extra-audio") { var path = Path.Combine(files.Directory, "tracks.mp4"); await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-map", "0:v", "-map", "0:a", "-map", "0:a", "-c", "copy", path]); source = path; }
        if (mode == "short") { var path = Path.Combine(files.Directory, "short.mp4"); await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-t", "0.5", "-c", "copy", path]); source = path; }
        var extension = mode == "audio" ? "m4a" : "mp4";
        if (mode == "audio") { var path = Path.Combine(files.Directory, "audio.m4a"); await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-map", "0:a", "-c", "copy", path]); source = path; }
        var fixture = new ExecSponsorChecks.Fixture { Youtube = true, Duration = mode == "tail" ? 2.5 : 3, Data = await File.ReadAllBytesAsync(source), Extension = extension, CaptionFormat = mode == "ttml" ? "ttml" : mode == "srt" ? "srt" : "vtt", Denied = mode == "denied" };
        var cuts = mode switch { "overlap" => new[] { (0.5m, 1.5m), (1m, 2m) }, "prefix" => [(0m, 1m)], "suffix" => [(2m, 3m)], "arbitrary" => [(0.317m, 1.713m)], "tail" => [(2.6m, 2.8m)], "empty" => [], _ => [(1m, 2m)] };
        fixture.Segments = cuts.Select(cut => ExecSponsorChecks.Segment(cut.Item1, cut.Item2)).ToArray();
        var marker = Path.Combine(files.Directory, "commands.jsonl"); var output = Path.Combine(files.Directory, "recording." + extension); await File.WriteAllTextAsync(output, "previous output");
        var request = ExecSponsorChecks.Request(files.Directory) with { FfmpegPath = ffmpeg, KeepOriginal = true, WriteSubs = true, EmbedSubs = mode is not ("audio" or "video"), AddMetadata = true, WriteInfoJson = true,
            SponsorBlockMark = mode == "mark" ? "all" : null, SponsorBlockRemove = mode == "mark" ? null : "all", ConvertSubs = mode == "srt" ? "srt" : "", AudioOnly = mode == "audio", AudioFormat = "best", VideoOnly = mode == "video", ExecCommands = mode == "exec" ? [ExecSponsorChecks.Command("success", marker)] : null };
        if (mode == "ttml") request = request with { ConvertSubs = "vtt" };
        if (mode == "no-overwrite") { request = request with { NoOverwrites = true, KeepOriginal = false }; var skipped = await fixture.Engine.DownloadAsync(request, null, null, default); ExecSponsorChecks.Assert(skipped.Succeeded && skipped.Skipped && fixture.Api == 0 && fixture.Transfers == 0 && await File.ReadAllTextAsync(output) == "previous output", "A protected SponsorBlock output fetched an API or media."); return; }
        var callbacks = new List<string>(); var engine = fixture.Engine; using var cancel = new CancellationTokenSource();
        if (mode == "cancel") PublicationFaults.Observer = phase => { if (phase == "processor-started") cancel.Cancel(); return Task.CompletedTask; };
        if (mode == "retry")
        {
            PublicationFaults.Observer = phase => phase == "processing" ? throw new IOException("Owned processing interruption") : Task.CompletedTask;
            try { await engine.DownloadAsync(request, null, null, default); throw new Exception("The processing interruption did not occur."); } catch (IOException) { } finally { PublicationFaults.Observer = null; }
            fixture.Denied = true; engine = fixture.Engine;
        }
        NativeDownloadResult? result = null;
        try { result = await engine.DownloadAsync(request, null, null, cancel.Token, callbacks.Add); }
        catch (Exception ex) when (mode is "denied" or "short" or "hevc" or "extra-audio" or "cancel" && ex is HttpRequestException or InvalidDataException or NotSupportedException or OperationCanceledException) { }
        finally { PublicationFaults.Observer = null; }
        if (mode is "denied" or "short" or "hevc" or "extra-audio" or "cancel") { ExecSponsorChecks.Assert(result?.Succeeded != true && await File.ReadAllTextAsync(output) == "previous output" && !File.Exists(request.DownloadArchiveFile) && callbacks.Count == 0, "A failed SponsorBlock job changed prior publication state."); return; }
        ExecSponsorChecks.Assert(result!.Succeeded && result.OutputPaths.Count == 2 && callbacks.Count == 2, "SponsorBlock did not publish complete processed/original media.");
        if (mode == "retry") ExecSponsorChecks.Assert(fixture.Api == 1, "SponsorBlock retry replaced its pinned service plan.");
        var main = result.OutputPaths[0]; var original = result.OutputPaths[1];
        foreach (var path in result.OutputPaths) await Run(ffmpeg, ["-v", "error", "-xerror", "-i", path, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"]);
        foreach (var role in extension == "m4a" ? new[] { "a:0" } : ["v:0", "a:0"])
            ExecSponsorChecks.Assert((await AdaptivePacketHashes(ffmpeg, source, role)).AsEnumerable().SequenceEqual(await AdaptivePacketHashes(ffmpeg, original, role)), "SponsorBlock changed its retained original packets.");
        var remove = mode != "mark" && cuts.Length > 0;
        var spans = new List<(decimal Start, decimal End)>(); decimal cursor = 0;
        foreach (var cut in cuts.OrderBy(c => c.Item1)) { if (cut.Item1 > cursor) spans.Add((cursor, cut.Item1)); cursor = Math.Max(cursor, cut.Item2); } if (cursor < 3) spans.Add((cursor, 3));
        if (!remove) spans = [(0, 3)];
        var frames = 0; var samples = 0;
        if (mode != "audio")
        {
            var sourceFrames = await SponsorFrames(ffmpeg, source); var times = await SponsorFrameTimes(ffmpeg, source); var expected = sourceFrames.Where((_, index) => spans.Any(span => times[index] >= span.Start && times[index] < span.End)).ToArray(); var actual = await SponsorFrames(ffmpeg, main);
            ExecSponsorChecks.Assert(expected.SequenceEqual(actual), "SponsorBlock did not preserve the exact kept decoded pictures."); frames = actual.Length;
        }
        if (mode != "video")
        {
            if (remove)
            {
                var sourcePcm = await SponsorPcm(ffmpeg, source); var actual = await SponsorPcm(ffmpeg, main); var expected = spans.SelectMany(span => sourcePcm.Skip(checked((int)(span.Start * 48000)) * 3).Take(checked((int)(span.End * 48000) - (int)(span.Start * 48000)) * 3)).ToArray();
                ExecSponsorChecks.Assert(expected.SequenceEqual(actual), "SponsorBlock audio did not match the exact kept decoded source samples."); samples = actual.Length / 3;
            }
            else ExecSponsorChecks.Assert((await AdaptivePacketHashes(ffmpeg, source, "a:0")).AsEnumerable().SequenceEqual(await AdaptivePacketHashes(ffmpeg, main, "a:0")), "SponsorBlock marking changed coded audio.");
        }
        var caption = await File.ReadAllTextAsync(result.SidecarPaths.Single(path => path.EndsWith(mode == "srt" ? ".srt" : ".vtt", StringComparison.Ordinal)));
        if (mode is not ("mark" or "empty" or "prefix" or "suffix" or "arbitrary" or "tail")) ExecSponsorChecks.Assert(!caption.Contains("Removed payload", StringComparison.Ordinal), "Removed captions remained on the edited timeline.");
        if (mode == "exec") { using var json = JsonDocument.Parse(File.ReadAllLines(marker).Single()); ExecSponsorChecks.Assert(json.RootElement.GetProperty("Exists").GetBoolean(), "Exec did not see completed SponsorBlock media."); }
        var before = (fixture.Api, fixture.Transfers, callbacks.Count); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add); ExecSponsorChecks.Assert(repeat.Succeeded && repeat.Skipped && before == (fixture.Api, fixture.Transfers, callbacks.Count), "Archived SponsorBlock repeated API/media/exec/callback work.");
        if (retained is not null) await File.WriteAllTextAsync(Path.Combine(files.Directory, "verification.json"), JsonSerializer.Serialize(new { Version = NativeEngine.Version, Mode = mode, Source = source, Files = result.OutputPaths, Sidecars = result.SidecarPaths, Kept = spans.Select(span => new { span.Start, span.End }), KeptPictures = frames, ExactAudioSamples = samples, fixture.Api, fixture.Transfers, RepeatApi = 0, RepeatMedia = 0, RepeatCallbacks = 0 }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static async Task<string[]> SponsorFrames(string ffmpeg, string path)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".framemd5";
        try { await Run(ffmpeg, ["-v", "error", "-y", "-i", path, "-map", "0:v:0", "-fps_mode", "passthrough", "-f", "framemd5", temporary]); return (await File.ReadAllLinesAsync(temporary)).Where(line => line.Length > 0 && !line.StartsWith('#')).Select(line => line.Split(',')[^1].Trim()).ToArray(); }
        finally { File.Delete(temporary); }
    }
    private static async Task<decimal[]> SponsorFrameTimes(string ffmpeg, string path)
    {
        var start = new System.Diagnostics.ProcessStartInfo(YoutubeDl.Engine.Media.FfmpegTool.RequireProbe(ffmpeg)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var value in new[] { "-v", "error", "-select_streams", "v:0", "-show_frames", "-show_entries", "frame=best_effort_timestamp_time", "-of", "json", path }) start.ArgumentList.Add(value);
        using var process = System.Diagnostics.Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); if (process.ExitCode != 0) throw new Exception(await error);
        using var json = JsonDocument.Parse(await output); return json.RootElement.GetProperty("frames").EnumerateArray().Select(frame => decimal.Parse(frame.GetProperty("best_effort_timestamp_time").GetString()!, CultureInfo.InvariantCulture)).ToArray();
    }
    private static async Task<byte[]> SponsorPcm(string ffmpeg, string path)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".pcm";
        try { await Run(ffmpeg, ["-v", "error", "-y", "-i", path, "-map", "0:a:0", "-c:a", "pcm_s24le", "-f", "s24le", temporary]); return await File.ReadAllBytesAsync(temporary); } finally { File.Delete(temporary); }
    }
}
