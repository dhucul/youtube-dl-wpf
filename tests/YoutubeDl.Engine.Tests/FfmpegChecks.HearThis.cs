using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.HearThis;

internal static partial class FfmpegChecks
{
    private static async Task HearThisMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp3");
        await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=730:sample_rate=44100:duration=2", "-c:a", "libmp3lame", "-b:a", "128k", source]); var bytes = await File.ReadAllBytesAsync(source);
        var calls = new List<Uri>(); var mediaRequests = 0; var engine = new NativeEngine([new HearThisProvider()], _ => HearThisChecks.Http(calls, (kind, node) => { if (kind == "track" && mode == "short") node["duration"] = "10"; },
            _ => { mediaRequests++; return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }; }));
        var artist = mode is "selected" or "snapshot"; var request = HearThisChecks.Request(files.Directory, artist) with { FfmpegPath = ffmpeg, OutputTemplate = artist ? "selected-%(playlist_index)s.%(ext)s" : "hearthis.%(ext)s", KeepOriginal = true,
            AddMetadata = true, AudioOnly = mode == "convert", AudioFormat = "m4a", AudioQuality = "128K", PlaylistItems = artist ? "51" : "", DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") };
        if (mode == "embed") request = request with { Url = new("https://hearthis.at/embed/42/") };
        if (mode == "snapshot") { var info = await engine.InspectAsync(request, default); request = request with { PlaylistSnapshot = info.PlaylistSnapshot }; calls.Clear(); }
        var callbacks = 0;
        if (mode == "short")
        {
            var prior = Path.Combine(files.Directory, "hearthis.mp3"); await File.WriteAllTextAsync(prior, "previous complete"); await File.WriteAllTextAsync(request.DownloadArchiveFile, "other preserved\n");
            try { await engine.DownloadAsync(request, null, null, default, _ => callbacks++); throw new InvalidOperationException("Short hearthis audio was published."); } catch (InvalidDataException) { }
            if (await File.ReadAllTextAsync(prior) != "previous complete" || await File.ReadAllTextAsync(request.DownloadArchiveFile) != "other preserved\n" || callbacks != 0) throw new InvalidOperationException("Incomplete hearthis source changed previous publication."); return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, _ => callbacks++);
        if (!result.Succeeded || result.OutputPaths.Count != 2 || callbacks != 2 || mediaRequests != 1 || artist && result.OutputPaths.Any(path => !Path.GetFileName(path).StartsWith("selected-51.", StringComparison.Ordinal))) throw new InvalidOperationException("hearthis selected complete playback/originals changed.");
        foreach (var path in result.OutputPaths) await Run(ffmpeg, ["-v", "error", "-xerror", "-i", path, "-f", "null", "-"], true);
        var original = result.OutputPaths.Single(path => File.ReadAllBytes(path).SequenceEqual(bytes)); var processed = result.OutputPath ?? result.OutputPaths.Single(path => path != original);
        if (mode != "convert")
        {
            var a = await Run(ffmpeg, ["-v", "error", "-i", processed, "-map", "0:a:0", "-c:a", "pcm_s16le", "-f", "hash", "-hash", "SHA256", "-"]);
            var b = await Run(ffmpeg, ["-v", "error", "-i", original, "-map", "0:a:0", "-c:a", "pcm_s16le", "-f", "hash", "-hash", "SHA256", "-"]);
            if (a != b) throw new InvalidOperationException("hearthis metadata processing changed decoded samples.");
        }
        var repeat = await engine.DownloadAsync(request, null, null, default, _ => callbacks++);
        if (!repeat.Skipped || mediaRequests != 1 || callbacks != 2) throw new InvalidOperationException("hearthis archive repeat fetched media or emitted completions.");
    }
}
