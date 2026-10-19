using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Newgrounds;

internal static partial class FfmpegChecks
{
    private static async Task NewgroundsMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var video = mode is "video" or "video-audio"; var source = Path.Combine(files.Directory, video ? "source.mp4" : "source.mp3");
        if (video) await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=12:duration=2", "-f", "lavfi", "-i", "sine=frequency=520:sample_rate=44100:duration=2", "-c:v", "libx264", "-c:a", "aac", "-shortest", source]);
        else await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=640:sample_rate=44100:duration=2", "-c:a", "libmp3lame", source]);
        var bytes = await File.ReadAllBytesAsync(source); using var cancel = new CancellationTokenSource(); var reads = 0; var calls = new List<Uri>();
        var engine = new NativeEngine([new NewgroundsProvider()], _ => NewgroundsChecks.Http(audio: mode == "short" ? NewgroundsChecks.AudioHtml(duration: 30) : null, calls: calls, media: request =>
        {
            if (mode == "selected" || mode == "snapshot")
                if (!request.RequestUri!.AbsolutePath.Contains("43_Test.mp3", StringComparison.Ordinal)) throw new InvalidOperationException("Newgrounds fetched unselected media.");
            if (++reads == 2 && mode == "cancel") { cancel.Cancel(); throw new OperationCanceledException(cancel.Token); }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        var collection = mode is "selected" or "snapshot" or "cancel"; var archive = Path.Combine(files.Directory, "archive.txt");
        var request = NewgroundsChecks.Request(files.Directory, video ? "video" : collection ? "user" : "audio") with { FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true,
            DownloadArchiveFile = archive, FormatId = video ? "720p" : "mp3", AudioOnly = mode is "convert" or "video-audio", AudioFormat = "m4a", AudioQuality = "128K",
            PlaylistItems = mode == "cancel" ? "1:2" : collection ? "2" : "", OutputTemplate = collection ? "%(playlist_index)s-%(id)s.%(ext)s" : "%(id)s.%(ext)s" };
        if (mode == "snapshot") { var inspected = await engine.InspectAsync(request, default); request = request with { PlaylistSnapshot = inspected.PlaylistSnapshot }; calls.Clear(); }
        var callbacks = new List<string>();
        if (mode == "short")
        {
            var prior = Path.Combine(files.Directory, "42.mp3"); await File.WriteAllTextAsync(prior, "previous complete media"); await File.WriteAllTextAsync(archive, "other preserved\n");
            try { await engine.DownloadAsync(request, null, null, default, callbacks.Add); throw new InvalidOperationException("Short Newgrounds source was accepted."); } catch (InvalidDataException) { }
            if (await File.ReadAllTextAsync(prior) != "previous complete media" || await File.ReadAllTextAsync(archive) != "other preserved\n" || callbacks.Count != 0) throw new InvalidOperationException("Short audio changed prior publication.");
            return;
        }
        if (mode == "cancel")
        {
            try { await engine.DownloadAsync(request, null, null, cancel.Token, callbacks.Add); throw new InvalidOperationException("Newgrounds cancellation was ignored."); } catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            if (callbacks.Count != 2 || callbacks.Any(path => !File.Exists(path)) || !(await File.ReadAllTextAsync(archive)).Contains("newgrounds:audio 42", StringComparison.Ordinal) ||
                (await File.ReadAllTextAsync(archive)).Contains("newgrounds:audio 43", StringComparison.Ordinal)) throw new InvalidOperationException("Cancellation lost earlier complete files or recorded a pending source.");
            foreach (var path in callbacks) await Run(ffmpeg, ["-v", "error", "-xerror", "-i", path, "-map", "0:a", "-f", "null", "-"], true);
            return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!result.Succeeded || result.OutputPaths.Count != 2 || callbacks.Count != 2 || !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(bytes))) throw new InvalidOperationException("Newgrounds did not retain complete original and processed media.");
        foreach (var path in result.OutputPaths) await Run(ffmpeg, ["-v", "error", "-xerror", "-i", path, "-f", "null", "-"], true);
        if (collection && !Path.GetFileName(result.OutputPaths[0]).StartsWith("2-43", StringComparison.Ordinal)) throw new InvalidOperationException("Newgrounds changed selected source position.");
        if (mode == "video")
        {
            var frames = await Run(Probe(ffmpeg), ["-v", "error", "-count_frames", "-select_streams", "v:0", "-show_entries", "stream=nb_read_frames", "-of", "csv=p=0", result.OutputPath!]);
            if (frames.Trim() != "24") throw new InvalidOperationException("Newgrounds video publication lost picture frames.");
        }
        if (!request.AudioOnly)
        {
            var hashes = new List<string>(); foreach (var path in result.OutputPaths) hashes.Add(await Run(ffmpeg, ["-v", "error", "-i", path, "-map", "0:a:0", "-c:a", "pcm_s16le", "-f", "hash", "-hash", "sha256", "-"], true));
            if (hashes.Distinct(StringComparer.Ordinal).Count() != 1) throw new InvalidOperationException("Unconverted Newgrounds audio samples changed.");
        }
        var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!repeat.Succeeded || !repeat.Skipped || callbacks.Count != 2 || reads != 1) throw new InvalidOperationException("Newgrounds archive repeated media or completions.");
    }
}
