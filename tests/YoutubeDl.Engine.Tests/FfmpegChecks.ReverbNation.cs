using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.ReverbNation;

internal static partial class FfmpegChecks
{
    private static async Task ReverbNationMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp3");
        await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=740:sample_rate=44100:duration=2", "-c:a", "libmp3lame", "-b:a", "128k", source]); var bytes = await File.ReadAllBytesAsync(source);
        var calls = new List<Uri>(); var requests = 0; var engine = new NativeEngine([new ReverbNationProvider()], _ => ReverbNationChecks.Http(calls, (kind, node) => { if (mode == "short" && kind == "song") node["PRIMARY_SONG"]!["duration"] = 10; }, _ => { requests++; return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }; }));
        var artist = mode is "selected" or "snapshot"; var request = ReverbNationChecks.Request(files.Directory, artist) with { FfmpegPath = ffmpeg, OutputTemplate = artist ? "selected-%(playlist_index)s.%(ext)s" : "reverbnation.%(ext)s", KeepOriginal = true, AddMetadata = true,
            AudioOnly = mode == "convert", AudioFormat = "m4a", AudioQuality = "128K", PlaylistItems = artist ? "3" : "", DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") };
        if (mode == "widget") request = request with { Url = new(ReverbNationChecks.WidgetUrl) };
        if (mode == "snapshot") { var info = await engine.InspectAsync(request, default); request = request with { PlaylistSnapshot = info.PlaylistSnapshot }; calls.Clear(); }
        var callbacks = 0;
        if (mode == "short")
        {
            var prior = Path.Combine(files.Directory, "reverbnation.mp3"); await File.WriteAllTextAsync(prior, "previous complete"); await File.WriteAllTextAsync(request.DownloadArchiveFile, "other preserved\n");
            try { await engine.DownloadAsync(request, null, null, default, _ => callbacks++); throw new InvalidOperationException("Short ReverbNation source was published."); } catch (InvalidDataException) { }
            if (await File.ReadAllTextAsync(prior) != "previous complete" || await File.ReadAllTextAsync(request.DownloadArchiveFile) != "other preserved\n" || callbacks != 0) throw new InvalidOperationException("Incomplete ReverbNation changed previous media/archive."); return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, _ => callbacks++);
        if (!result.Succeeded || result.OutputPaths.Count != 2 || callbacks != 2 || requests != 1 || artist && result.OutputPaths.Any(path => !Path.GetFileName(path).StartsWith("selected-3.", StringComparison.Ordinal))) throw new InvalidOperationException("Complete ReverbNation selection/originals changed.");
        foreach (var path in result.OutputPaths) await Run(ffmpeg, ["-v", "error", "-xerror", "-i", path, "-f", "null", "-"], true);
        var original = result.OutputPaths.Single(path => File.ReadAllBytes(path).SequenceEqual(bytes)); var processed = result.OutputPath ?? result.OutputPaths.Single(path => path != original);
        if (mode != "convert")
        {
            var a = await Run(ffmpeg, ["-v", "error", "-i", processed, "-map", "0:a:0", "-c:a", "pcm_s16le", "-f", "hash", "-hash", "SHA256", "-"]);
            var b = await Run(ffmpeg, ["-v", "error", "-i", original, "-map", "0:a:0", "-c:a", "pcm_s16le", "-f", "hash", "-hash", "SHA256", "-"]);
            if (a != b) throw new InvalidOperationException("ReverbNation processing changed decoded audio.");
        }
        var repeat = await engine.DownloadAsync(request, null, null, default, _ => callbacks++);
        if (!repeat.Skipped || requests != 1 || callbacks != 2) throw new InvalidOperationException("ReverbNation archive repeated delivery/completion.");
    }
}
