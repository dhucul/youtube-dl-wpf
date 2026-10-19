using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Podomatic;

internal static partial class FfmpegChecks
{
    private static async Task PodomaticMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp3");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=650:sample_rate=44100:duration=2", "-c:a", "libmp3lame", source]);
        var bytes = await File.ReadAllBytesAsync(source); var calls = new List<Uri>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var selected = mode is "selected" or "snapshot"; var episode = PodomaticChecks.Episode(duration: mode == "incomplete" ? 20 : 2);
        var engine = new NativeEngine([new PodomaticProvider()], _ => PodomaticChecks.Http(calls, episode, media: r =>
        {
            if (r.Headers.Contains("Authorization") || r.Headers.Contains("Cookie") || r.RequestUri!.Query != PodomaticChecks.Signed) throw new InvalidOperationException("Podomatic media credentials or literal queries changed.");
            if (selected && !r.RequestUri.AbsolutePath.Contains(PodomaticChecks.Other)) throw new InvalidOperationException("An unselected Podomatic episode transferred media.");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        var request = PodomaticChecks.Request(files.Directory) with { Url = selected ? new("https://www.podomatic.com/embed/html5/podcast/6347936") : PodomaticChecks.Request(files.Directory).Url,
            FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true, WriteInfoJson = true, DownloadArchiveFile = archive, PlaylistItems = selected ? "2" : "",
            HttpHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer fixture", ["Cookie"] = "fixture=secret" }, AudioOnly = mode == "convert", AudioFormat = "m4a" };
        if (mode == "snapshot") { var preview = await engine.InspectAsync(request, default); request = request with { PlaylistSnapshot = preview.PlaylistSnapshot }; }
        if (mode == "incomplete")
        {
            var prior = Path.Combine(files.Directory, "podomatic.mp3"); await File.WriteAllTextAsync(prior, "previous complete audio");
            try { await engine.DownloadAsync(request, null, null, default, callbacks.Add); throw new InvalidOperationException("Incomplete Podomatic source was published."); } catch (InvalidDataException) { }
            if (callbacks.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(prior) != "previous complete audio") throw new InvalidOperationException("Failed Podomatic verification changed prior output or archive."); return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!result.Succeeded || result.OutputPaths.Count != 2 || callbacks.Count != 2) throw new InvalidOperationException("Podomatic media/original publication failed: " + string.Join("; ", result.Failures.Select(f => f.Message)));
        var output = result.OutputPath ?? result.OutputPaths[0];
        foreach (var path in result.OutputPaths)
        {
            using var probe = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path], rejectDiagnostics: true));
            var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            if (streams.Length != 1 || streams[0].GetProperty("codec_type").GetString() != "audio") throw new InvalidOperationException("Podomatic audio gained another media role.");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:a:0", "-f", "null", "-"], rejectDiagnostics: true);
        }
        if (!result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(bytes))) throw new InvalidOperationException("Podomatic source original bytes changed.");
        if (!selected && mode != "convert")
        {
            var probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
            var one = await Run(probe, ["-v", "error", "-show_packets", "-show_data_hash", "sha256", "-show_entries", "packet=data_hash", "-of", "json", source], rejectDiagnostics: true);
            var two = await Run(probe, ["-v", "error", "-show_packets", "-show_data_hash", "sha256", "-show_entries", "packet=data_hash", "-of", "json", output], rejectDiagnostics: true);
            if (one != two) throw new InvalidOperationException("Podomatic metadata processing changed encoded audio payloads/order.");
        }
        if ((await File.ReadAllTextAsync(archive)).Trim() != "podomatic " + (selected ? PodomaticChecks.Other : PodomaticChecks.Id)) throw new InvalidOperationException("Podomatic archive lost its stable episode identity.");
        if (selected) { using var info = JsonDocument.Parse(await File.ReadAllTextAsync(result.SidecarPaths.Single(path => path.EndsWith(".info.json")))); if (info.RootElement.GetProperty("playlist_index").GetInt32() != 2) throw new InvalidOperationException("Podomatic selected source position changed."); }
        calls.Clear(); callbacks.Clear(); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!repeat.Succeeded || callbacks.Count != 0 || calls.Any(uri => uri.Host == "streams.podomatic.com")) throw new InvalidOperationException("Podomatic archive repeat transferred media or repeated callbacks.");
    }
}
