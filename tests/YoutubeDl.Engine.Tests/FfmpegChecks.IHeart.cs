using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.IHeart;

internal static partial class FfmpegChecks
{
    private static async Task IHeartMedia(string ffmpeg, string mode)
    {
        using var files = new Files();
        var extension = mode is "m4a" or "aac" ? mode : "mp3";
        var source = Path.Combine(files.Directory, "source." + extension);
        var duration = mode == "dynamic" ? 3 : 2;
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=530:sample_rate=44100:duration=" + duration,
            "-c:a", extension == "mp3" ? "libmp3lame" : "aac", source]);
        var bytes = await File.ReadAllBytesAsync(source); var calls = new List<Uri>(); var completions = new List<string>();
        var selected = mode is "selected" or "snapshot"; var id = selected ? IHeartChecks.OtherId : IHeartChecks.EpisodeId;
        var episode = IHeartChecks.Episode(id, mode == "incomplete" ? 20 : 2, extension);
        var engine = new NativeEngine([new IHeartProvider()], _ => IHeartChecks.Http(calls, episode: episode, media: request =>
        {
            if (request.RequestUri!.AbsoluteUri != IHeartChecks.Source(id, extension) || request.Headers.Contains("Authorization") || request.Headers.Contains("Cookie"))
                throw new InvalidOperationException("iHeart changed an offered source URL or leaked caller credentials.");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        var archive = Path.Combine(files.Directory, "archive.txt");
        var request = IHeartChecks.Request(files.Directory, selected) with
        {
            FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true, WriteInfoJson = true, DownloadArchiveFile = archive,
            PlaylistItems = selected ? "2" : "", AudioOnly = mode is "convert" or "aac", AudioFormat = mode == "aac" ? "best" : "mp3",
            HttpHeaders = new Dictionary<string,string> { ["Authorization"] = "Bearer fixture", ["Cookie"] = "private=fixture" }
        };
        if (mode == "snapshot") { var inspected = await engine.InspectAsync(request, default); request = request with { PlaylistSnapshot = inspected.PlaylistSnapshot }; calls.Clear(); }
        if (mode == "incomplete")
        {
            var prior = Path.Combine(files.Directory, "iheart.mp3"); await File.WriteAllTextAsync(prior, "previous complete audio");
            try { await engine.DownloadAsync(request, null, null, default, completions.Add); throw new InvalidOperationException("Short iHeart audio was published as a complete episode."); }
            catch (InvalidDataException) { }
            if (completions.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(prior) != "previous complete audio")
                throw new InvalidOperationException("iHeart source refusal changed previous output or archive success.");
            return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, completions.Add);
        if (!result.Succeeded || result.OutputPaths.Count != 2 || !result.OutputPaths.SequenceEqual(completions))
            throw new InvalidOperationException("iHeart did not publish complete audio and its original: " + result.Error + string.Join(";", result.Failures.Select(f => f.Message)));
        foreach (var output in result.OutputPaths)
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", output, "-map", "0:a", "-f", "null", "-"], rejectDiagnostics: true);
        if (!result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(bytes))) throw new InvalidOperationException("iHeart did not preserve the exact supplied audio bytes.");
        if (mode != "convert")
        {
            var processed = result.OutputPath ?? result.OutputPaths.Single(path => !Path.GetFileName(path).Contains(".original", StringComparison.Ordinal));
            var before = await Run(ffmpeg, ["-v", "error", "-xerror", "-i", source, "-map", "0:a", "-c:a", "pcm_s16le", "-f", "hash", "-hash", "sha256", "-"], true);
            var after = await Run(ffmpeg, ["-v", "error", "-xerror", "-i", processed, "-map", "0:a", "-c:a", "pcm_s16le", "-f", "hash", "-hash", "sha256", "-"], true);
            if (before != after) throw new InvalidOperationException("iHeart metadata or best-audio copying changed decoded audio samples.");
        }
        using var info = JsonDocument.Parse(await File.ReadAllTextAsync(result.SidecarPaths.Single(path => path.EndsWith(".info.json"))));
        if (info.RootElement.GetProperty("id").GetString() != id || selected && info.RootElement.GetProperty("playlist_index").GetInt32() != 2)
            throw new InvalidOperationException("iHeart published another episode or changed its original collection position.");
        if (selected && calls.Any(uri => uri.AbsolutePath.Contains("/episodes/" + IHeartChecks.EpisodeId))) throw new InvalidOperationException("iHeart fetched unselected playback metadata.");
        if ((await File.ReadAllTextAsync(archive)).Trim() != "iheartradio " + id) throw new InvalidOperationException("iHeart archive identity changed.");
        calls.Clear(); completions.Clear();
        var repeat = await engine.DownloadAsync(request, null, null, default, completions.Add);
        if (!repeat.Succeeded || completions.Count != 0 || calls.Any(uri => uri.Host == "cdn.example" || uri.AbsolutePath.Contains("/episodes/")))
            throw new InvalidOperationException("An archived iHeart repeat transferred media or playback metadata.");
    }
}
