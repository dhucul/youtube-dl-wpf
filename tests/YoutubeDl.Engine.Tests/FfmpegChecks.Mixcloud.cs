using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Mixcloud;

internal static partial class FfmpegChecks
{
    private static async Task MixcloudMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.m4a");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=650:sample_rate=44100:duration=2", "-c:a", "aac", source]);
        var bytes = await File.ReadAllBytesAsync(source); var calls = new List<Uri>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var duration = mode == "incomplete" ? 20 : 2; var selected = mode is "selected" or "snapshot";
        var engine = new NativeEngine([new MixcloudProvider()], _ => MixcloudChecks.Http(calls, rest: MixcloudChecks.Show(selected ? MixcloudChecks.Other : MixcloudChecks.Slug, duration), playback: MixcloudChecks.Playback(selected ? MixcloudChecks.Other : MixcloudChecks.Slug, duration), media: r =>
        {
            if (r.Headers.Contains("Authorization") || r.Headers.Contains("Cookie") || r.RequestUri!.AbsoluteUri != MixcloudChecks.Media() || !r.Headers.UserAgent.ToString().StartsWith("YoutubeDlModernNative/")) throw new InvalidOperationException("Mixcloud clear source query identification or anonymous credentials changed.");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        var request = MixcloudChecks.Request(files.Directory, selected) with { FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true, WriteInfoJson = true, DownloadArchiveFile = archive, PlaylistItems = selected ? "2" : "", AudioOnly = mode == "convert", AudioFormat = "mp3",
            HttpHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer fixture", ["Cookie"] = "fixture=secret" } };
        if (mode == "snapshot") { var info = await engine.InspectAsync(request, default); request = request with { PlaylistSnapshot = info.PlaylistSnapshot }; }
        if (mode == "incomplete")
        {
            var prior = Path.Combine(files.Directory, "mixcloud.m4a"); await File.WriteAllTextAsync(prior, "previous complete media");
            try { await engine.DownloadAsync(request, null, null, default, callbacks.Add); throw new InvalidOperationException("Incomplete Mixcloud audio was published."); } catch (InvalidDataException) { }
            if (callbacks.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(prior) != "previous complete media") throw new InvalidOperationException("Mixcloud failed verification changed prior output or archive."); return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add); if (!result.Succeeded || result.OutputPaths.Count != 2 || callbacks.Count != 2) throw new InvalidOperationException("Mixcloud clear media/original publication failed: " + string.Join(";", result.Failures.Select(f => f.Message)));
        foreach (var path in result.OutputPaths) await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:a", "-f", "null", "-"], rejectDiagnostics: true);
        if (!result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(bytes))) throw new InvalidOperationException("Mixcloud original source bytes changed.");
        if (selected) { using var info = JsonDocument.Parse(await File.ReadAllTextAsync(result.SidecarPaths.Single(path => path.EndsWith(".info.json")))); if (info.RootElement.GetProperty("playlist_index").GetInt32() != 2) throw new InvalidOperationException("Mixcloud selected source position changed."); }
        if ((await File.ReadAllTextAsync(archive)).Trim() != "mixcloud fixture/" + (selected ? MixcloudChecks.Other : MixcloudChecks.Slug)) throw new InvalidOperationException("Mixcloud archive lost its show identity.");
        calls.Clear(); callbacks.Clear(); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add); if (!repeat.Succeeded || callbacks.Count != 0 || calls.Any(uri => uri.Host == "dl.mixcloud.stream")) throw new InvalidOperationException("Archived Mixcloud audio repeated transfers or file completions.");
    }
}
