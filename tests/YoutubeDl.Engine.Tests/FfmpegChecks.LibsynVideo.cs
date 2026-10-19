using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Libsyn;

internal static partial class FfmpegChecks
{
    private static async Task LibsynVideo(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=128x72:rate=24:duration=2", "-f", "lavfi", "-i", "sine=frequency=650:sample_rate=48000:duration=2", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", source]);
        var bytes = await File.ReadAllBytesAsync(source); var calls = new List<Uri>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var engine = new NativeEngine([new LibsynProvider()], _ => LibsynChecks.Http(calls, LibsynChecks.Episode(video: true), media: r =>
        {
            if (r.Headers.Contains("Authorization") || r.Headers.Contains("Cookie") || r.RequestUri!.AbsoluteUri != LibsynChecks.Media(video: true)) throw new InvalidOperationException("Libsyn video changed its supplied source or leaked credentials.");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        var request = LibsynChecks.Request(files.Directory) with { FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true, WriteInfoJson = true, DownloadArchiveFile = archive, VideoOnly = mode == "video-only", AudioOnly = mode == "audio", AudioFormat = "m4a",
            HttpHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer fixture", ["Cookie"] = "fixture=secret" } };
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add); if (!result.Succeeded || result.OutputPaths.Count != 2 || callbacks.Count != 2) throw new InvalidOperationException("Libsyn video/original publication failed.");
        var output = result.OutputPath ?? result.OutputPaths[0]; var original = result.OutputPaths.Single(path => path != output); if (!File.ReadAllBytes(original).SequenceEqual(bytes)) throw new InvalidOperationException("Libsyn original video bytes changed.");
        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        foreach (var path in result.OutputPaths)
        {
            using var info = JsonDocument.Parse(await Run(probe, ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path], rejectDiagnostics: true));
            var streams = info.RootElement.GetProperty("streams").EnumerateArray().ToArray(); var video = streams.SingleOrDefault(stream => stream.GetProperty("codec_type").GetString() == "video"); var hasAudio = streams.Any(stream => stream.GetProperty("codec_type").GetString() == "audio");
            if (path == output && mode == "audio") { if (video.ValueKind != JsonValueKind.Undefined || !hasAudio) throw new InvalidOperationException("Libsyn extracted audio retained a video stream."); }
            else if (video.GetProperty("nb_read_frames").GetString() != "48" || path == output && mode == "video-only" && hasAudio) throw new InvalidOperationException("Libsyn video roles or full frame count changed.");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-f", "null", "-"], rejectDiagnostics: true);
        }
        if ((await File.ReadAllTextAsync(archive)).Trim() != "libsyn " + LibsynChecks.Id) throw new InvalidOperationException("Libsyn video archive lost its stable identity.");
        callbacks.Clear(); calls.Clear(); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add); if (!repeat.Succeeded || callbacks.Count != 0 || calls.Count != 0) throw new InvalidOperationException("Archived Libsyn video transferred playback or repeated file completions.");
    }
}
