using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Coub;

internal static partial class FfmpegChecks
{
    private static async Task CoubMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var videoFile = Path.Combine(files.Directory, "source.mp4"); var audioFile = Path.Combine(files.Directory, "source.mp3");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x96:r=24:d=2", "-c:v", "libx264", "-pix_fmt", "yuv420p", videoFile]);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=650:sample_rate=44100:duration=" + (mode == "short-audio" ? "1" : "6"), "-c:a", "libmp3lame", audioFile]);
        var video = await File.ReadAllBytesAsync(videoFile); var audio = await File.ReadAllBytesAsync(audioFile); var silent = mode == "silent"; var record = CoubChecks.Record(!silent);
        foreach (var quality in new[] { "high", "med" })
        {
            record["file_versions"]!["html5"]!["video"]![quality]!["size"] = video.Length + (mode == "wrong-size" ? 1 : 0);
            if (!silent) record["file_versions"]!["html5"]!["audio"]![quality]!["size"] = audio.Length;
        }
        var calls = new List<Uri>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var engine = new NativeEngine([new CoubProvider()], _ => CoubChecks.Http(calls, record, media: r =>
        {
            if (r.Headers.Contains("Authorization") || r.Headers.Contains("Cookie") || r.RequestUri!.Query != CoubChecks.Signed) throw new InvalidOperationException("Coub playback credentials or literal queries changed.");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(r.RequestUri!.AbsolutePath.EndsWith(".mp3") ? audio : video) };
        }));
        var request = CoubChecks.Request(files.Directory) with { FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true, WriteInfoJson = true, DownloadArchiveFile = archive,
            AudioOnly = mode == "audio", AudioFormat = "best", VideoOnly = mode == "video", FormatSelector = mode == "single" ? "video-high" : mode == "pair" ? "video-med+audio-med" : "" };
        if (mode == "wrong-size")
        {
            var prior = Path.Combine(files.Directory, "coub.mkv"); await File.WriteAllTextAsync(prior, "previous complete movie");
            try { await engine.DownloadAsync(request, null, null, default, callbacks.Add); throw new InvalidOperationException("Changed Coub source size was published."); } catch (InvalidDataException) { }
            if (callbacks.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(prior) != "previous complete movie") throw new InvalidOperationException("Failed Coub verification changed prior output or archive."); return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!result.Succeeded || result.OutputPath is null || callbacks.Count != result.OutputPaths.Count || result.OutputPaths.Count < 2) throw new InvalidOperationException("Coub complete files/originals were not published once.");
        var probeExe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        using var probe = JsonDocument.Parse(await Run(probeExe, ["-v", "error", "-count_frames", "-show_entries", "format=duration:stream=codec_type,nb_read_frames", "-of", "json", result.OutputPath], rejectDiagnostics: true));
        var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray(); var outputVideo = streams.FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "video"); var outputAudio = streams.FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "audio");
        var paired = mode is "default" or "pair" or "short-audio";
        if (paired)
        {
            if (result.OutputPaths.Count != 4 || outputAudio.ValueKind == JsonValueKind.Undefined || int.Parse(outputVideo.GetProperty("nb_read_frames").GetString()!) < (mode == "short-audio" ? 48 : 144)) throw new InvalidOperationException("Coub froze or stopped before complete source cycles.");
            foreach (var kind in new[] { "v:0", "a:0" })
            {
                using var before = JsonDocument.Parse(await Run(probeExe, ["-v", "error", "-select_streams", kind, "-show_packets", "-show_data_hash", "sha256", "-show_entries", "packet=data_hash", "-of", "json", kind == "v:0" ? videoFile : audioFile], rejectDiagnostics: true));
                using var after = JsonDocument.Parse(await Run(probeExe, ["-v", "error", "-select_streams", kind, "-show_packets", "-show_data_hash", "sha256", "-show_entries", "packet=data_hash", "-of", "json", result.OutputPath], rejectDiagnostics: true));
                var original = before.RootElement.GetProperty("packets").EnumerateArray().Select(p => p.GetProperty("data_hash").GetString()).ToArray();
                var rendered = after.RootElement.GetProperty("packets").EnumerateArray().Select(p => p.GetProperty("data_hash").GetString()).ToArray();
                if (kind == "a:0" && mode != "short-audio" ? !rendered.SequenceEqual(original) : rendered.Where((hash, index) => hash != original[index % original.Length]).Any()) throw new InvalidOperationException("Coub rendering changed encoded loop/audio payloads or order.");
            }
        }
        else if (mode == "audio" ? outputVideo.ValueKind != JsonValueKind.Undefined : outputVideo.GetProperty("nb_read_frames").GetString() != "48" || outputAudio.ValueKind != JsonValueKind.Undefined)
            throw new InvalidOperationException("Single-track/silent Coub selection changed its source cycle or roles.");
        foreach (var path in result.OutputPaths) await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"], rejectDiagnostics: true);
        if (paired && (!result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(video)) || !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(audio)))) throw new InvalidOperationException("Coub exact source originals changed.");
        if ((await File.ReadAllTextAsync(archive)).Trim() != "coub " + CoubChecks.Id) throw new InvalidOperationException("Coub archive lost its permalink identity.");
        calls.Clear(); callbacks.Clear(); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!repeat.Succeeded || calls.Count != 0 || callbacks.Count != 0) throw new InvalidOperationException("Coub archive repeat requested old media or repeated completion events.");
    }
}
