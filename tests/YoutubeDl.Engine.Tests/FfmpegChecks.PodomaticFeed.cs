using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Podomatic;

internal static partial class FfmpegChecks
{
    private static async Task PodomaticFeedMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp3");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=650:sample_rate=44100:duration=2", "-c:a", "libmp3lame", "-write_xing", "0", source]);
        var bytes = await File.ReadAllBytesAsync(source); var feed = PodomaticFeedChecks.Feed("stale", mode == "incomplete" ? 20 : 2); var calls = new List<Uri>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        if (mode == "bad-chapters") feed = PodomaticFeedChecks.BadChapters(feed);
        var engine = new NativeEngine([new PodomaticProvider()], _ => PodomaticFeedChecks.Http(calls, () => feed, media: r =>
        {
            if (r.Headers.Contains("Authorization") || r.Headers.Contains("Cookie") || r.RequestUri!.AbsoluteUri != PodomaticFeedChecks.Media(true, mode == "snapshot" ? "fresh" : "stale")) throw new InvalidOperationException("Migrated selection transferred another source, stale query or caller credentials.");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        var request = PodomaticFeedChecks.Request(files.Directory) with { PlaylistItems = "2", FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true, WriteInfoJson = true, EmbedChapters = mode == "bad-chapters", DownloadArchiveFile = archive, AudioOnly = mode == "convert", AudioFormat = "m4a", HttpHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer fixture", ["Cookie"] = "fixture=secret" } };
        if (mode == "snapshot") { var info = await engine.InspectAsync(request, default); request = request with { PlaylistSnapshot = info.PlaylistSnapshot }; feed = PodomaticFeedChecks.Feed("fresh", movedOrder: true); }
        if (mode is "incomplete" or "bad-chapters")
        {
            var prior = Path.Combine(files.Directory, "002 - migrated.mp3"); await File.WriteAllTextAsync(prior, "previous complete audio"); var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
            if (result.Succeeded || callbacks.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(prior) != "previous complete audio") throw new InvalidOperationException("Incomplete migrated source changed prior output or recorded archive success."); return;
        }
        var downloaded = await engine.DownloadAsync(request, null, null, default, callbacks.Add); if (!downloaded.Succeeded || downloaded.OutputPaths.Count != 2 || callbacks.Count != 2) throw new InvalidOperationException("Migrated audio/original publication failed.");
        var output = downloaded.OutputPath ?? downloaded.OutputPaths[0];
        foreach (var path in downloaded.OutputPaths) await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:a", "-f", "null", "-"], rejectDiagnostics: true);
        if (!downloaded.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(bytes))) throw new InvalidOperationException("Migrated feed original bytes changed.");
        if (mode != "convert")
        {
            var one = await Run(ffmpeg, ["-v", "error", "-i", source, "-map", "0:a", "-c:a", "pcm_s16le", "-f", "hash", "-hash", "sha256", "-"], rejectDiagnostics: true);
            var two = await Run(ffmpeg, ["-v", "error", "-i", output, "-map", "0:a", "-c:a", "pcm_s16le", "-f", "hash", "-hash", "sha256", "-"], rejectDiagnostics: true); if (one != two) throw new InvalidOperationException("Migrated audio metadata processing changed boundary samples.");
        }
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(downloaded.SidecarPaths.Single(path => path.EndsWith(".info.json")))); if (json.RootElement.GetProperty("playlist_index").GetInt32() != 2 || json.RootElement.GetProperty("id").GetString() != PodomaticFeedChecks.Legacy) throw new InvalidOperationException("Migration changed GUID or inspected source position.");
        var record = (await File.ReadAllTextAsync(archive)).Trim(); if (!record.StartsWith("native:feed ")) throw new InvalidOperationException("Migration reinterpreted a legacy GUID as a numeric old-host archive.");
        calls.Clear(); callbacks.Clear(); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add); if (!repeat.Succeeded || callbacks.Count != 0 || calls.Any(uri => uri.Host == "www.buzzsprout.com")) throw new InvalidOperationException("Archived migrated selection repeated transfer or completions.");
    }
}
