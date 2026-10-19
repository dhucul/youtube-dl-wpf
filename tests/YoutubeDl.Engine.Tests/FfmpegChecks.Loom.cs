using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Loom;

internal static partial class FfmpegChecks
{
    private static async Task LoomMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x96:r=24:d=2", "-f", "lavfi", "-i", "sine=frequency=500:sample_rate=48000:duration=2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-g", "12", source]);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", source, "-c", "copy", "-f", "hls", "-hls_time", "0.5", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(files.Directory, "part%d.ts"), Path.Combine(files.Directory, "media.m3u8")]);
        var assets = System.IO.Directory.EnumerateFiles(files.Directory).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes); var calls = new List<Uri>(); var callbacks = new List<string>();
        var master = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=300000,RESOLUTION=160x96,FRAME-RATE=24\nmedia.m3u8\n";
        var state = LoomChecks.State(mode == "hls", mode == "incomplete" ? 4 : 2); var archive = Path.Combine(files.Directory, "archive.txt");
        var engine = new NativeEngine([new LoomProvider()], _ => LoomChecks.Http(state, calls, media: request =>
        {
            var url = request.RequestUri!; var name = Path.GetFileName(url.AbsolutePath);
            if (url.Host == "luna.loom.com" && !ScopedMediaQueryChecks.Parameters.All(parameter => url.Query.Contains(parameter)))
                throw new InvalidOperationException("A relative Loom HLS resource lost its scoped playback credentials.");
            return new(HttpStatusCode.OK) { Content = name == "master.m3u8" ? new StringContent(master) : name.EndsWith(".vtt") ?
                new StringContent("WEBVTT\n\n00:00:00.100 --> 00:00:01.900\nFixture caption.\n") : new ByteArrayContent(name.EndsWith(".mp4") ? assets["source.mp4"] : assets[name]) };
        }));
        var request = LoomChecks.Request(files.Directory) with { FormatId = mode == "hls" ? "hls" : "mp4", FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true,
            WriteAutoSubs = true, EmbedSubs = true, SubLanguages = "en", WriteInfoJson = true, DownloadArchiveFile = archive };
        if (mode == "incomplete")
        {
            var previous = Path.Combine(files.Directory, "loom.mp4"); await File.WriteAllTextAsync(previous, "previous complete output");
            try { await engine.DownloadAsync(request, null, null, default, callbacks.Add); throw new InvalidOperationException("Incomplete Loom playback was published."); } catch (InvalidDataException) { }
            if (callbacks.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(previous) != "previous complete output") throw new InvalidOperationException("Failed verification changed published files or archive state.");
            return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!result.Succeeded || result.OutputPath is null || result.OutputPaths.Count != 2 || callbacks.Count != 2 || !result.SidecarPaths.Any(path => path.EndsWith(".vtt")))
            throw new InvalidOperationException("Loom media/original/caption publication was incomplete.");
        using var json = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", result.OutputPath], rejectDiagnostics: true));
        if (json.RootElement.GetProperty("streams").EnumerateArray().Single(stream => stream.GetProperty("codec_type").GetString() == "video").GetProperty("nb_read_frames").GetString() != "48")
            throw new InvalidOperationException("Loom playback lost complete video frames.");
        foreach (var path in result.OutputPaths) await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v:0", "-map", "0:a:0", "-f", "null", "-"], rejectDiagnostics: true);
        var captions = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", result.OutputPath, "-map", "0:s:0", "-f", "srt", "-"], rejectDiagnostics: true);
        if (!captions.Contains("Fixture caption.") || !captions.Contains("00:00:00,100 --> 00:00:01,900") || (await File.ReadAllTextAsync(archive)).Trim() != "loom " + LoomChecks.Id)
            throw new InvalidOperationException("Loom caption timing or archive identity changed.");
        if (mode == "mp4" && !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(assets["source.mp4"]))) throw new InvalidOperationException("The original Loom MP4 bytes changed.");
        calls.Clear(); callbacks.Clear(); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!repeat.Succeeded || callbacks.Count > 0 || calls.Any(url => url.Host != "www.loom.com")) throw new InvalidOperationException("An archive repeat transferred Loom media or captions.");
    }
}
