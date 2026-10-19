using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static partial class FfmpegChecks
{
    private static async Task HlsCaptionMedia(string ffmpeg, string mode, string httpChunkSize = "")
    {
        using var files = new Files();
        var manifestPath = Path.Combine(files.Directory, "video.m3u8");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=green:s=128x96:r=10:d=4", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100:duration=4",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "10", "-bf", "0", "-c:a", "aac", "-f", "hls", "-hls_time", "2", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(files.Directory, "video%d.ts"), manifestPath]);
        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        var probe = await Run(ffprobe, ["-v", "error", "-show_entries", "format=start_time", "-of", "json", Path.Combine(files.Directory, "video0.ts")]);
        using var timing = JsonDocument.Parse(probe);
        var origin = decimal.Parse(timing.RootElement.GetProperty("format").GetProperty("start_time").GetString()!, CultureInfo.InvariantCulture);
        var mpeg = decimal.Round(origin * 90000).ToString(CultureInfo.InvariantCulture);
        var header = Encoding.UTF8.GetBytes("WEBVTT\nX-TIMESTAMP-MAP=LOCAL:00:00:00.000,MPEGTS:" + mpeg + "\n\n");
        var first = Encoding.UTF8.GetBytes("cue\n00:00.500 --> 00:03.000\nSpanning native caption.\n");
        var last = Encoding.UTF8.GetBytes("cue\n00:00.500 --> 00:03.000\nSpanning native caption.\n\n00:03.000 --> 00:04.500\nFinal native caption.\n");
        var assets = Directory.EnumerateFiles(files.Directory).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        string captions;
        if (mode == "encrypted")
        {
            var key = Enumerable.Range(1, 16).Select(value => (byte)value).ToArray(); assets["key.bin"] = key;
            byte[] Encrypt(byte[] content, ulong sequence)
            { using var aes = Aes.Create(); aes.Key = key; var iv = new byte[16]; BinaryPrimitives.WriteUInt64BigEndian(iv.AsSpan(8), sequence); return aes.EncryptCbc(content, iv); }
            assets["header.bin"] = Encrypt(header, 1); assets["one.bin"] = Encrypt(first, 7); assets["two.bin"] = Encrypt(last, 8);
            captions = "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:7\n#EXT-X-KEY:METHOD=AES-128,URI=\"key.bin\",IV=0x1\n#EXT-X-MAP:URI=\"header.bin\"\n#EXT-X-KEY:METHOD=AES-128,URI=\"key.bin\"\n#EXTINF:2,\none.bin\n#EXTINF:2,\ntwo.bin\n#EXT-X-ENDLIST\n";
        }
        else
        {
            assets["captions.bin"] = header.Concat(first).Concat(last).ToArray();
            captions = $"#EXTM3U\n#EXT-X-MAP:URI=\"captions.bin\",BYTERANGE=\"{header.Length}@0\"\n#EXTINF:2,\n#EXT-X-BYTERANGE:{first.Length}@{header.Length}\ncaptions.bin\n#EXTINF:2,\n#EXT-X-BYTERANGE:{last.Length}\ncaptions.bin\n#EXT-X-ENDLIST\n";
        }
        if (mode == "pair")
        {
            var audio = Path.Combine(files.Directory, "audio.m4a");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=550:sample_rate=44100:duration=4", "-c:a", "aac", audio]);
            assets["audio.m4a"] = await File.ReadAllBytesAsync(audio);
        }
        var requests = new List<string>();
        MediaHttp Http(NativeDownloadRequest _) => new(new Handler(request =>
        {
            var name = Path.GetFileName(request.RequestUri!.AbsolutePath); requests.Add(name);
            if (name == "master.m3u8") return new(HttpStatusCode.OK) { Content = new StringContent(HlsCaptionChecks.Master) };
            if (name == "en.m3u8") return new(HttpStatusCode.OK) { Content = new StringContent(captions) };
            if (name == "fr.m3u8" && mode == "failure") return new(HttpStatusCode.Forbidden);
            if (request.Headers.Range is not null)
            {
                if (httpChunkSize.Length > 0 && request.Headers.Range.Ranges.Single() is { From: { } from, To: { } to } && to - from + 1 > 8)
                    throw new InvalidOperationException("Caption ranges ignored the configured chunk bound.");
                return DashIndexChecks.Range(request, assets[name]);
            }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(assets[name]) };
        }));
        var engine = mode == "pair" ? new NativeEngine([new HlsPairProvider()], Http) : new NativeEngine(httpFactory: Http);
        var download = new NativeDownloadRequest { SubtitleSleepSeconds = PublicSubtitleFixtureDelay.Value,  Url = new Uri("https://media.example/master.m3u8"), OutputDirectory = files.Directory, FileName = "result",
            WriteSubs = true, EmbedSubs = true, SubLanguages = mode == "failure" ? "en,fr" : "en", FfmpegPath = ffmpeg, Retries = 0, FormatSelector = mode == "pair" ? "v+a" : "", HttpChunkSize = httpChunkSize };
        if (mode == "failure")
        {
            var oldMedia = Path.Combine(files.Directory, "result.mp4"); var oldCaption = Path.Combine(files.Directory, "result.en.vtt"); var archive = Path.Combine(files.Directory, "archive.txt");
            await File.WriteAllTextAsync(oldMedia, "previous media"); await File.WriteAllTextAsync(oldCaption, "previous captions");
            var notified = 0; var failed = false;
            try { await engine.DownloadAsync(download with { DownloadArchiveFile = archive }, null, null, default, _ => notified++); }
            catch (HttpRequestException) { failed = true; }
            if (!failed || notified != 0 || File.Exists(archive) || File.ReadAllText(oldMedia) != "previous media" || File.ReadAllText(oldCaption) != "previous captions")
                throw new InvalidOperationException("Failed HLS captions changed publication or recorded success.");
            return;
        }
        var result = await engine.DownloadAsync(download, null, null, default);
        if (!result.Succeeded || result.OutputPath is null) throw new InvalidOperationException(result.Error);
        var caption = await File.ReadAllTextAsync(Path.Combine(files.Directory, "result.en.vtt"));
        if (!caption.Contains("00:00:00.500 --> 00:00:03.000") || !caption.Contains("00:00:03.000 --> 00:00:04.000") || caption.Split(" --> ").Length != 3 || requests.Contains("fr.m3u8"))
            throw new InvalidOperationException("HLS caption timing, duplication, clipping or selection changed.");
        var extracted = Path.Combine(files.Directory, "extracted.vtt");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", result.OutputPath, "-map", "0:s:0", extracted]);
        var embedded = await File.ReadAllTextAsync(extracted);
        if (!embedded.Contains("00:00.500 --> 00:03.000") || !embedded.Contains("Spanning native caption.")) throw new InvalidOperationException("Embedded HLS captions drifted or changed text.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", result.OutputPath, "-f", "null", "-"]);
    }
    private sealed class HlsPairProvider : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("fixture-hls-pair", "Fixture", 1);
        public bool CanHandle(Uri url) => true;
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(new PageMetadata { Title = "Pair", Media =
        [new() { Id = "v", Url = request.Url, Kind = MediaKind.Hls, Extension = "m3u8", HasVideo = true, HasAudio = false },
         new() { Id = "a", Url = new Uri("https://media.example/audio.m4a"), Kind = MediaKind.Progressive, Extension = "m4a", HasVideo = false, HasAudio = true }] });
    }
}
