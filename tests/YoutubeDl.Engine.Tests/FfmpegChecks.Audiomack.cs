using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Audiomack;

internal static partial class FfmpegChecks
{
    private static async Task AudiomackMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var mp3 = mode == "standard"; var source = Path.Combine(files.Directory, mp3 ? "source.mp3" : "source.m4a");
        await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=580:sample_rate=44100:duration=2", "-c:a", mp3 ? "libmp3lame" : "aac", source]);
        var bytes = await File.ReadAllBytesAsync(source); var calls = new List<Uri>(); var reads = 0; using var cancel = new CancellationTokenSource();
        var engine = new NativeEngine([new AudiomackProvider()], _ => AudiomackChecks.Http(calls, (kind, node) => { if (kind == "song" && mode == "short") node["duration"] = "30"; }, request =>
        {
            if (mode is "album" or "snapshot" && !Path.GetFileName(request.RequestUri!.AbsolutePath).StartsWith("second-", StringComparison.Ordinal)) throw new InvalidOperationException("Audiomack fetched unselected media.");
            if (++reads == 2 && mode == "cancel") { cancel.Cancel(); throw new OperationCanceledException(cancel.Token); }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        var collection = mode is "album" or "snapshot" or "cancel"; var archive = Path.Combine(files.Directory, "archive.txt");
        var request = AudiomackChecks.Request(files.Directory, collection) with { FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true, DownloadArchiveFile = archive,
            FormatId = mp3 ? "standard" : "hq", AudioOnly = mode == "convert", AudioFormat = "mp3", AudioQuality = "128K", PlaylistItems = mode == "cancel" ? "1:2" : collection ? "2" : "",
            OutputTemplate = collection ? "%(playlist_index)s-%(id)s.%(ext)s" : "%(id)s.%(ext)s" };
        if (mode == "embed") request = request with { Url = new("https://audiomack.com/embed/artist/song/track") };
        if (mode == "snapshot") { var inspected = await engine.InspectAsync(request, default); request = request with { PlaylistSnapshot = inspected.PlaylistSnapshot }; calls.Clear(); }
        var completions = new List<string>();
        if (mode == "short")
        {
            var prior = Path.Combine(files.Directory, "42.m4a"); await File.WriteAllTextAsync(prior, "previous complete"); await File.WriteAllTextAsync(archive, "other preserved\n");
            try { await engine.DownloadAsync(request, null, null, default, completions.Add); throw new InvalidOperationException("Short Audiomack media was accepted."); } catch (InvalidDataException) { }
            if (await File.ReadAllTextAsync(prior) != "previous complete" || await File.ReadAllTextAsync(archive) != "other preserved\n" || completions.Count != 0) throw new InvalidOperationException("Short source changed prior publication."); return;
        }
        if (mode == "cancel")
        {
            try { await engine.DownloadAsync(request, null, null, cancel.Token, completions.Add); throw new InvalidOperationException("Audiomack cancellation was ignored."); } catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            if (completions.Count != 2 || completions.Any(path => !File.Exists(path)) || !(await File.ReadAllTextAsync(archive)).Contains("audiomack 42", StringComparison.Ordinal) ||
                (await File.ReadAllTextAsync(archive)).Contains("audiomack 43", StringComparison.Ordinal)) throw new InvalidOperationException("Cancellation lost earlier outputs or archived a pending source.");
            foreach (var path in completions) await Run(ffmpeg, ["-v", "error", "-xerror", "-i", path, "-map", "0:a", "-f", "null", "-"], true); return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, completions.Add);
        if (!result.Succeeded || result.OutputPaths.Count != 2 || completions.Count != 2 || !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(bytes))) throw new InvalidOperationException("Audiomack did not retain complete processed media and exact source originals.");
        foreach (var path in result.OutputPaths) await Run(ffmpeg, ["-v", "error", "-xerror", "-i", path, "-map", "0:a", "-f", "null", "-"], true);
        if (collection && !Path.GetFileName(result.OutputPaths[0]).StartsWith("2-43", StringComparison.Ordinal)) throw new InvalidOperationException("Album source position changed.");
        if (mode != "convert")
        {
            var hashes = new List<string>(); foreach (var path in result.OutputPaths) hashes.Add(await Run(ffmpeg, ["-v", "error", "-i", path, "-map", "0:a:0", "-c:a", "pcm_s16le", "-f", "hash", "-hash", "sha256", "-"], true));
            if (hashes.Distinct(StringComparer.Ordinal).Count() != 1) throw new InvalidOperationException("Unconverted Audiomack audio samples changed.");
        }
        var before = calls.Count(uri => uri.AbsolutePath.Contains("/play/", StringComparison.Ordinal)); var repeat = await engine.DownloadAsync(request, null, null, default, completions.Add);
        if (!repeat.Skipped || !repeat.Succeeded || completions.Count != 2 || reads != 1 || calls.Count(uri => uri.AbsolutePath.Contains("/play/", StringComparison.Ordinal)) != before)
            throw new InvalidOperationException("Archive repeat rediscovered playback or emitted completions.");
    }
}
