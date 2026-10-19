using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Bandlab;

internal static partial class FfmpegChecks
{
    private static async Task BandlabMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.m4a");
        await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=720:sample_rate=44100:duration=2", "-c:a", "aac", source]); var bytes = await File.ReadAllBytesAsync(source);
        var calls = new List<Uri>(); var requests = 0;
        var engine = new NativeEngine([new BandlabProvider()], _ => BandlabChecks.Http(calls, (kind, node) =>
        {
            if (mode != "short") return;
            if (kind == "revision") node["mixdown"]!["duration"] = 10;
            else if (kind == "post") node["revision"]!["mixdown"]!["duration"] = 10;
        }, request => { requests++; return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }; }));
        var collection = mode is "selected" or "snapshot" or "cancel"; var request = BandlabChecks.Request(files.Directory, collection ? "collection" : "revision") with
        { FfmpegPath = ffmpeg, OutputTemplate = collection ? "entry-%(playlist_index)s.%(ext)s" : "bandlab.%(ext)s", KeepOriginal = true, AddMetadata = true,
            AudioOnly = mode == "convert", AudioFormat = "mp3", AudioQuality = "128K", PlaylistItems = mode == "cancel" ? "1,2" : collection ? "2" : "", DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") };
        if (mode == "embed") request = request with { Url = new("https://www.bandlab.com/embed/?id=" + BandlabChecks.RevisionId) };
        if (mode == "snapshot") { var info = await engine.InspectAsync(request, default); request = request with { PlaylistSnapshot = info.PlaylistSnapshot }; calls.Clear(); }
        var callbacks = 0; using var stop = new CancellationTokenSource();
        if (mode == "short")
        {
            var prior = Path.Combine(files.Directory, "bandlab.m4a"); await File.WriteAllTextAsync(prior, "previous complete"); await File.WriteAllTextAsync(request.DownloadArchiveFile, "other preserved\n");
            try { await engine.DownloadAsync(request, null, null, default, _ => callbacks++); throw new InvalidOperationException("Short BandLab audio was published."); } catch (InvalidDataException) { }
            if (await File.ReadAllTextAsync(prior) != "previous complete" || await File.ReadAllTextAsync(request.DownloadArchiveFile) != "other preserved\n" || callbacks != 0) throw new InvalidOperationException("Incomplete BandLab audio changed prior publication."); return;
        }
        NativeDownloadResult result;
        if (mode == "cancel")
        {
            try { await engine.DownloadAsync(request, null, null, stop.Token, _ => { callbacks++; stop.Cancel(); }); throw new InvalidOperationException("BandLab selected-entry cancellation did not stop."); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            var completed = Directory.EnumerateFiles(files.Directory, "entry-1.*").ToArray();
            if (completed.Length != 2 || requests != 1 || !File.ReadAllText(request.DownloadArchiveFile).Contains(BandlabChecks.RevisionId) || File.ReadAllText(request.DownloadArchiveFile).Contains(BandlabChecks.SecondRevision)) throw new InvalidOperationException("BandLab cancellation lost completed media or archived an unstarted entry.");
            foreach (var path in completed) await Run(ffmpeg, ["-v", "error", "-xerror", "-i", path, "-f", "null", "-"], true); return;
        }
        result = await engine.DownloadAsync(request, null, null, default, _ => callbacks++);
        if (!result.Succeeded || result.OutputPaths.Count != 2 || callbacks != 2 || requests != 1 || collection && result.OutputPaths.Any(path => !Path.GetFileName(path).StartsWith("entry-2.", StringComparison.Ordinal))) throw new InvalidOperationException("BandLab complete media/original selection changed.");
        foreach (var path in result.OutputPaths) await Run(ffmpeg, ["-v", "error", "-xerror", "-i", path, "-f", "null", "-"], true);
        var original = result.OutputPaths.Single(path => File.ReadAllBytes(path).SequenceEqual(bytes));
        var processed = result.OutputPath ?? result.OutputPaths.Single(path => path != original);
        if (mode != "convert")
        {
            var processedHash = await Run(ffmpeg, ["-v", "error", "-i", processed, "-map", "0:a:0", "-c:a", "pcm_s16le", "-f", "hash", "-hash", "SHA256", "-"]);
            var originalHash = await Run(ffmpeg, ["-v", "error", "-i", original, "-map", "0:a:0", "-c:a", "pcm_s16le", "-f", "hash", "-hash", "SHA256", "-"]);
            if (processedHash != originalHash) throw new InvalidOperationException("BandLab metadata processing changed decoded audio samples.");
        }
        if (collection && calls.Any(url => url.AbsolutePath.EndsWith(BandlabChecks.PostId, StringComparison.Ordinal))) throw new InvalidOperationException("BandLab collection selection refreshed unselected playback.");
        var repeated = await engine.DownloadAsync(request, null, null, default, _ => callbacks++);
        if (!repeated.Skipped || callbacks != 2 || requests != 1) throw new InvalidOperationException("BandLab archive repeat fetched media or emitted callbacks.");
    }
}
