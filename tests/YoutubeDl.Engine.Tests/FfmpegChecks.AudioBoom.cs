using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.AudioBoom;

internal static partial class FfmpegChecks
{
    private static async Task AudioBoomMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp3");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=650:sample_rate=44100:duration=2", "-c:a", "libmp3lame", "-write_xing", "0", source]);
        var bytes = await File.ReadAllBytesAsync(source); var calls = new List<Uri>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var selected = mode is "selected" or "snapshot"; var episode = AudioBoomChecks.Html(AudioBoomChecks.Clip(duration: mode == "incomplete" ? 20 : 2));
        var engine = new NativeEngine([new AudioBoomProvider()], _ => AudioBoomChecks.Http(calls, episode, media: r =>
        {
            if (r.Headers.Contains("Authorization") || r.Headers.Contains("Cookie") || r.RequestUri!.Query != AudioBoomChecks.Signed) throw new InvalidOperationException("AudioBoom media credentials or literal queries changed.");
            if (selected && !r.RequestUri.AbsolutePath.Contains(AudioBoomChecks.Other)) throw new InvalidOperationException("An unselected AudioBoom episode transferred media.");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        var request = AudioBoomChecks.Request(files.Directory) with { Url = selected ? new("https://audioboom.com/channels/5084695") : AudioBoomChecks.Request(files.Directory).Url,
            FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true, WriteInfoJson = true, DownloadArchiveFile = archive, PlaylistItems = selected ? "2" : "",
            HttpHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer fixture", ["Cookie"] = "fixture=secret" }, AudioOnly = mode == "convert", AudioFormat = "m4a" };
        if (mode == "snapshot") { var preview = await engine.InspectAsync(request, default); request = request with { PlaylistSnapshot = preview.PlaylistSnapshot }; }
        if (mode == "incomplete")
        {
            var prior = Path.Combine(files.Directory, "audioboom.mp3"); await File.WriteAllTextAsync(prior, "previous complete audio");
            try { await engine.DownloadAsync(request, null, null, default, callbacks.Add); throw new InvalidOperationException("Incomplete AudioBoom source was published."); } catch (InvalidDataException) { }
            if (callbacks.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(prior) != "previous complete audio") throw new InvalidOperationException("Failed AudioBoom verification changed prior output or archive."); return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!result.Succeeded || result.OutputPaths.Count != 2 || callbacks.Count != 2) throw new InvalidOperationException("AudioBoom media/original publication failed: " + string.Join("; ", result.Failures.Select(f => f.Message)));
        var output = result.OutputPath ?? result.OutputPaths[0];
        foreach (var path in result.OutputPaths)
        {
            using var probe = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path], rejectDiagnostics: true));
            var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            if (streams.Length != 1 || streams[0].GetProperty("codec_type").GetString() != "audio") throw new InvalidOperationException("AudioBoom audio gained another media role.");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:a:0", "-f", "null", "-"], rejectDiagnostics: true);
        }
        if (!result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(bytes))) throw new InvalidOperationException("AudioBoom source original bytes changed.");
        if (!selected && mode != "convert")
        {
            var probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
            var one = await Run(probe, ["-v", "error", "-show_packets", "-show_data_hash", "sha256", "-show_entries", "packet=data_hash", "-of", "json", source], rejectDiagnostics: true);
            var two = await Run(probe, ["-v", "error", "-show_packets", "-show_data_hash", "sha256", "-show_entries", "packet=data_hash", "-of", "json", output], rejectDiagnostics: true);
            if (one != two) throw new InvalidOperationException("AudioBoom metadata processing changed encoded audio payloads/order.");
            var originalPcm = await Run(ffmpeg, ["-v", "error", "-i", source, "-map", "0:a", "-c:a", "pcm_s16le", "-f", "hash", "-hash", "sha256", "-"], rejectDiagnostics: true);
            var outputPcm = await Run(ffmpeg, ["-v", "error", "-i", output, "-map", "0:a", "-c:a", "pcm_s16le", "-f", "hash", "-hash", "sha256", "-"], rejectDiagnostics: true);
            if (originalPcm != outputPcm) throw new InvalidOperationException("AudioBoom metadata processing changed decoded boundary samples.");
        }
        if ((await File.ReadAllTextAsync(archive)).Trim() != "audioboom " + (selected ? AudioBoomChecks.Other : AudioBoomChecks.Id)) throw new InvalidOperationException("AudioBoom archive lost its stable episode identity.");
        if (selected) { using var info = JsonDocument.Parse(await File.ReadAllTextAsync(result.SidecarPaths.Single(path => path.EndsWith(".info.json")))); if (info.RootElement.GetProperty("playlist_index").GetInt32() != 2) throw new InvalidOperationException("AudioBoom selected source position changed."); }
        calls.Clear(); callbacks.Clear(); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!repeat.Succeeded || callbacks.Count != 0 || calls.Any(uri => uri.AbsolutePath.EndsWith(".mp3"))) throw new InvalidOperationException("AudioBoom archive repeat transferred media or repeated callbacks.");
    }
}
