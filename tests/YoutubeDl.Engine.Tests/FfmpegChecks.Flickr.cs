using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Flickr;

internal static partial class FfmpegChecks
{
    private static async Task FlickrMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x96:r=24:d=2", "-f", "lavfi", "-i", "sine=frequency=600:sample_rate=48000:duration=2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "12", "-c:a", "aac", source]);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", source, "-c", "copy", "-f", "hls", "-hls_time", "0.5", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(files.Directory, "part%d.ts"), Path.Combine(files.Directory, "media.m3u8")]);
        var assets = System.IO.Directory.EnumerateFiles(files.Directory).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        var master = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=300000,RESOLUTION=160x96,FRAME-RATE=24\nmedia.m3u8" + FlickrChecks.Query + "\n";
        var playlist = string.Join('\n', (await File.ReadAllLinesAsync(Path.Combine(files.Directory, "media.m3u8"))).Select(line => line.StartsWith('#') || string.IsNullOrWhiteSpace(line) ? line : line + FlickrChecks.Query)) + "\n";
        var calls = new List<Uri>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var engine = new NativeEngine([new FlickrProvider()], _ => FlickrChecks.Http(metadata: FlickrChecks.Metadata(mode == "incomplete" ? 20 : 2), calls: calls, media: request =>
        {
            var url = request.RequestUri!; var name = Path.GetFileName(url.AbsolutePath);
            if (url.Query != FlickrChecks.Query) throw new InvalidOperationException("Flickr offered media queries changed.");
            return new(HttpStatusCode.OK) { Content = name == "master.m3u8" ? new StringContent(master) : name == "media.m3u8" ? new StringContent(playlist) :
                new ByteArrayContent(name.EndsWith(".mp4") ? assets["source.mp4"] : assets[name]) };
        }));
        var request = FlickrChecks.Request(files.Directory) with { FormatId = mode == "hls" ? "hls" : "96p", FfmpegPath = ffmpeg, KeepOriginal = true,
            AddMetadata = true, WriteInfoJson = true, DownloadArchiveFile = archive };
        if (mode == "incomplete")
        {
            var previous = Path.Combine(files.Directory, "flickr.mp4"); await File.WriteAllTextAsync(previous, "previous complete output");
            try { await engine.DownloadAsync(request, null, null, default, callbacks.Add); throw new InvalidOperationException("Incomplete Flickr playback was published."); }
            catch (InvalidDataException) { }
            if (callbacks.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(previous) != "previous complete output")
                throw new InvalidOperationException("Failed Flickr verification changed published files or archive state.");
            return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        var primary = result.OutputPaths.FirstOrDefault(path => !path.Contains(".original."));
        if (!result.Succeeded || primary is null || result.OutputPaths.Count != 2 || callbacks.Count != 2) throw new InvalidOperationException("Flickr media/original publication was incomplete.");
        using var probe = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", primary], rejectDiagnostics: true));
        var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        if (streams.Single(stream => stream.GetProperty("codec_type").GetString() == "video").GetProperty("nb_read_frames").GetString() != "48" ||
            !streams.Any(stream => stream.GetProperty("codec_type").GetString() == "audio")) throw new InvalidOperationException("Flickr processing lost frames or audio.");
        foreach (var path in result.OutputPaths) await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v:0", "-map", "0:a:0", "-f", "null", "-"], rejectDiagnostics: true);
        if (mode == "mp4" && !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(assets["source.mp4"]))) throw new InvalidOperationException("Flickr MP4 original bytes changed.");
        if ((await File.ReadAllTextAsync(archive)).Trim() != "flickr " + FlickrChecks.Id) throw new InvalidOperationException("Flickr archive ownership changed.");
        calls.Clear(); callbacks.Clear(); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!repeat.Succeeded || callbacks.Count != 0 || calls.Count != 0) throw new InvalidOperationException("Flickr archive repeat fetched metadata/media or emitted callbacks.");
    }
}
