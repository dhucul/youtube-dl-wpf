using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Jamendo;

internal static partial class FfmpegChecks
{
    private static async Task JamendoMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var ogg = mode == "ogg"; var source = Path.Combine(files.Directory, ogg ? "source.ogg" : "source.mp3");
        await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=670:sample_rate=44100:duration=2", "-c:a", ogg ? "libvorbis" : "libmp3lame", source]);
        var bytes = await File.ReadAllBytesAsync(source); var calls = new List<Uri>(); using var cancel = new CancellationTokenSource(); var mediaReads = 0;
        var engine = new NativeEngine([new JamendoProvider()], _ => JamendoChecks.Http(calls, (kind, id, node) =>
        {
            if (kind == "tracks" && mode.StartsWith("short", StringComparison.Ordinal)) node["duration"] = mode == "short-rounding" ? 3 : 30;
        }, request =>
        {
            if (request.Headers.Contains("X-Jam-Call")) throw new InvalidOperationException("Jamendo metadata identity leaked to storage.");
            if (mode is "selected" or "snapshot" && !request.RequestUri!.AbsolutePath.Contains("/43/", StringComparison.Ordinal))
                throw new InvalidOperationException("Jamendo selected an unrequested media track.");
            if (++mediaReads == 2 && mode == "cancel") { cancel.Cancel(); throw new OperationCanceledException(cancel.Token); }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        var collection = mode is "selected" or "snapshot" or "cancel"; var archive = Path.Combine(files.Directory, "archive.txt");
        var request = JamendoChecks.Request(files.Directory, collection) with { FfmpegPath = ffmpeg, DownloadArchiveFile = archive, KeepOriginal = true, AddMetadata = true,
            OutputTemplate = collection ? "%(playlist_index)s-%(id)s.%(ext)s" : "%(id)s.%(ext)s", PlaylistItems = mode == "cancel" ? "1:2" : collection ? "2" : "",
            FormatId = ogg ? "download-ogg" : mode == "stream" ? "stream-mp32" : "download-mp3", AudioOnly = mode == "convert", AudioFormat = "m4a", AudioQuality = "128K" };
        if (mode == "snapshot") { var inspected = await engine.InspectAsync(request, default); request = request with { PlaylistSnapshot = inspected.PlaylistSnapshot }; calls.Clear(); }
        var callbacks = new List<string>();
        if (mode.StartsWith("short", StringComparison.Ordinal))
        {
            var prior = Path.Combine(files.Directory, "42.mp3"); await File.WriteAllTextAsync(prior, "prior complete media"); await File.WriteAllTextAsync(archive, "other preserved\n");
            try { await engine.DownloadAsync(request, null, null, default, callbacks.Add); throw new InvalidOperationException("Short Jamendo track was accepted."); }
            catch (InvalidDataException) { }
            if (await File.ReadAllTextAsync(prior) != "prior complete media" || await File.ReadAllTextAsync(archive) != "other preserved\n" || callbacks.Count != 0)
                throw new InvalidOperationException("Short Jamendo audio changed prior publication.");
            return;
        }
        if (mode == "cancel")
        {
            try { await engine.DownloadAsync(request, null, null, cancel.Token, callbacks.Add); throw new InvalidOperationException("Jamendo cancellation was ignored."); }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            if (callbacks.Count != 2 || callbacks.Any(path => !File.Exists(path)) || !(await File.ReadAllTextAsync(archive)).Contains("jamendo 42", StringComparison.Ordinal) ||
                (await File.ReadAllTextAsync(archive)).Contains("jamendo 43", StringComparison.Ordinal)) throw new InvalidOperationException("Jamendo cancellation lost prior track outputs/archive identity.");
            foreach (var path in callbacks) await Run(ffmpeg, ["-v", "error", "-xerror", "-i", path, "-map", "0:a", "-f", "null", "-"], true);
            return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!result.Succeeded || result.OutputPaths.Count != 2 || callbacks.Count != 2 || !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(bytes)))
            throw new InvalidOperationException("Jamendo did not retain complete selected media and exact originals.");
        if (collection && !Path.GetFileName(result.OutputPaths[0]).StartsWith("2-43", StringComparison.Ordinal)) throw new InvalidOperationException("Jamendo changed the selected album source position.");
        foreach (var path in result.OutputPaths) await Run(ffmpeg, ["-v", "error", "-xerror", "-i", path, "-map", "0:a", "-f", "null", "-"], true);
        if (mode != "convert")
        {
            var hashes = new List<string>();
            foreach (var path in result.OutputPaths) hashes.Add(await Run(ffmpeg, ["-v", "error", "-i", path, "-map", "0:a:0", "-c:a", "pcm_s16le", "-f", "hash", "-hash", "sha256", "-"], true));
            if (hashes.Distinct(StringComparer.Ordinal).Count() != 1) throw new InvalidOperationException("Unconverted Jamendo audio samples changed during metadata publication.");
        }
        var before = calls.Count; var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!repeat.Succeeded || !repeat.Skipped || callbacks.Count != 2 || calls.Count != before && !collection) throw new InvalidOperationException("Jamendo archive repeat fetched direct media or emitted new completion.");
        if (mediaReads != 1) throw new InvalidOperationException("Jamendo archive repeated or fetched unselected media.");
    }
}
