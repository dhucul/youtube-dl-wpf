using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;

internal static partial class FfmpegChecks
{
    private static async Task HlsRenditions(string ffmpeg, string mode, string mergeFormat = "")
    {
        using var files = new Files();
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=green:s=128x96:r=10:d=0.6", "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "1",
            "-f", "hls", "-hls_time", "0.2", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(files.Directory, "video%d.ts"), Path.Combine(files.Directory, "video.m3u8")]);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100:duration=0.6", "-vn", "-c:a", "aac",
            "-f", "hls", "-hls_time", "0.2", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(files.Directory, "audio%d.ts"), Path.Combine(files.Directory, "audio.m3u8")]);
        const string master = """
            #EXTM3U
            #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="sound",NAME="Other",LANGUAGE="fr",DEFAULT=NO,URI="unused.m3u8"
            #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="sound",NAME="Default",LANGUAGE="en",DEFAULT=YES,URI="audio.m3u8"
            #EXT-X-STREAM-INF:BANDWIDTH=200000,RESOLUTION=128x96,AUDIO="sound"
            video.m3u8
            """;
        var assets = System.IO.Directory.EnumerateFiles(files.Directory).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        var requests = new List<string>();
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request =>
        {
            var name = Path.GetFileName(request.RequestUri!.AbsolutePath);
            requests.Add(name);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = name == "master.m3u8" ? new StringContent(master) : new ByteArrayContent(assets[name]) };
        })));
        var result = await engine.DownloadAsync(new NativeDownloadRequest
        {
            Url = new Uri("https://media.example/master.m3u8"), OutputDirectory = files.Directory, FileName = "result", Retries = 0,
            FfmpegPath = ffmpeg, AudioOnly = mode == "audio", AudioFormat = "copy", VideoOnly = mode == "video", KeepOriginal = mode == "keep", MergeOutputFormat = mergeFormat
        }, null, null, default);
        if (!result.Succeeded || result.OutputPath is null) throw new InvalidOperationException(result.Error);
        if (mergeFormat.Length > 0 && Path.GetExtension(result.OutputPath) != ".mkv") throw new InvalidOperationException("HLS ignored its compatible merge fallback.");
        var probe = await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-show_entries", "stream=codec_type", "-of", "json", result.OutputPath]);
        using var document = JsonDocument.Parse(probe);
        var types = document.RootElement.GetProperty("streams").EnumerateArray().Select(stream => stream.GetProperty("codec_type").GetString()).ToArray();
        if (types.Contains("audio") != (mode != "video") || types.Contains("video") != (mode != "audio"))
            throw new InvalidOperationException("The HLS output contains the wrong track types.");
        if (requests.Contains("unused.m3u8") || mode == "audio" && requests.Any(name => name.StartsWith("video") && name.EndsWith(".ts")) ||
            mode == "video" && requests.Any(name => name.StartsWith("audio"))) throw new InvalidOperationException("An excluded rendition was transferred.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", result.OutputPath, "-f", "null", "-"]);
        if (mode == "keep") await VerifyKeptComponents(ffmpeg, result, files.Directory);
        if (mergeFormat.Length > 0)
        {
            await File.WriteAllTextAsync(result.OutputPath, "previous processed output"); var callbacks = 0;
            var repeat = await engine.DownloadAsync(new NativeDownloadRequest { Url = new("https://media.example/master.m3u8"), OutputDirectory = files.Directory,
                FileName = "result", Retries = 0, FfmpegPath = ffmpeg, MergeOutputFormat = mergeFormat, NoPostOverwrites = true }, null, null, default, _ => callbacks++);
            if (!repeat.Succeeded || callbacks != 0 || await File.ReadAllTextAsync(result.OutputPath) != "previous processed output") throw new InvalidOperationException("HLS replaced a protected merged file.");
        }
    }

    private static async Task HlsMedia(string ffmpeg, bool encrypted, int concurrentFragments = 1)
    {
        using var files = new Files();
        var playlist = Path.Combine(files.Directory, "source.m3u8");
        var segmentPattern = Path.Combine(files.Directory, encrypted ? "segment%d.ts" : "segment%d.m4s");
        var args = new List<string> { "-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=green:s=128x96:r=10:d=0.6", "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "1", "-f", "hls", "-hls_time", "0.2", "-hls_list_size", "0", "-hls_segment_filename", segmentPattern };
        if (encrypted)
        {
            var key = Path.Combine(files.Directory, "key.bin");
            await File.WriteAllBytesAsync(key, RandomNumberGenerator.GetBytes(16));
            var keyInfo = Path.Combine(files.Directory, "key-info.txt");
            await File.WriteAllTextAsync(keyInfo, "https://media.example/key.bin\n" + key.Replace('\\', '/') + "\n");
            args.AddRange(["-hls_key_info_file", keyInfo]);
        }
        else args.AddRange(["-hls_segment_type", "fmp4", "-hls_fmp4_init_filename", "init.mp4"]);
        args.Add(playlist);
        await Run(ffmpeg, args);
        var playlistText = await File.ReadAllTextAsync(playlist);
        var assets = System.IO.Directory.EnumerateFiles(files.Directory).ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes);
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request =>
        {
            var name = Path.GetFileName(request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = name == "source.m3u8" ? new StringContent(playlistText) : new ByteArrayContent(assets[name]) };
        })));
        var result = await engine.DownloadAsync(new NativeDownloadRequest
        {
            Url = new Uri("https://media.example/source.m3u8"), OutputDirectory = files.Directory, FileName = "result", Retries = 0, ConcurrentFragments = concurrentFragments
        }, null, null, default);
        if (!result.Succeeded || result.OutputPath is null) throw new InvalidOperationException(result.Error);
        if (!encrypted && Path.GetExtension(result.OutputPath) != ".mp4") throw new InvalidOperationException("Fragmented MP4 was mislabeled as a transport stream.");
        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        var probe = await Run(ffprobe, ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", result.OutputPath]);
        using var document = JsonDocument.Parse(probe);
        if (!document.RootElement.GetProperty("streams").EnumerateArray().Any(stream => stream.GetProperty("codec_type").GetString() == "video"))
            throw new InvalidOperationException("The native HLS output lost its video track.");
        if (document.RootElement.GetProperty("streams").EnumerateArray().Single(stream => stream.GetProperty("codec_type").GetString() == "video").GetProperty("nb_read_frames").GetString() != "6")
            throw new InvalidOperationException("The native HLS transfer changed the complete video frame count.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", result.OutputPath, "-f", "null", "-"]);
    }
}
