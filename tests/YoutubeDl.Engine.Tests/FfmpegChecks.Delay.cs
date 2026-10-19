using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static partial class FfmpegChecks
{
    private static async Task DelayMedia(string ffmpeg, string mode, bool noPart = false, string bufferSize = "", bool noResizeBuffer = false, string httpChunkSize = "")
    {
        using var files = new Files();
        var video = Path.Combine(files.Directory, "video.mp4"); var audio = Path.Combine(files.Directory, "audio.m4a");
        await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=12:duration=1", "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", video]);
        await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=720:sample_rate=44100:duration=1", "-c:a", "aac", audio]);
        var hls = mode == "hls";
        if (hls)
        {
            var videoTs = Path.Combine(files.Directory, "video.ts"); var audioTs = Path.Combine(files.Directory, "audio.ts");
            await Run(ffmpeg, ["-v", "error", "-y", "-i", video, "-c", "copy", "-f", "mpegts", videoTs]);
            await Run(ffmpeg, ["-v", "error", "-y", "-i", audio, "-c", "copy", "-f", "mpegts", audioTs]);
            video = videoTs; audio = audioTs;
        }
        var videoBytes = await File.ReadAllBytesAsync(video); var audioBytes = await File.ReadAllBytesAsync(audio);
        var videoUrl = new Uri("https://delay-media.example/video.mp4"); var audioUrl = new Uri("https://delay-media.example/audio.m4a");
        var media = mode is "pair" or "cancel" ? new MediaCandidate[] {
            new() { Id = "video", Url = videoUrl, Extension = "mp4", HasVideo = true, HasAudio = false },
            new() { Id = "audio", Url = audioUrl, Extension = "m4a", HasVideo = false, HasAudio = true }
        } : [new() { Url = new("https://delay-media.example/" + (hls ? "master.m3u8" : "manifest.mpd")),
            Kind = hls ? MediaKind.Hls : MediaKind.Dash, Extension = "mp4", HasVideo = true, HasAudio = true }];
        var page = new PageMetadata { Id = "delayed", Extractor = "delay-media", ArchiveIdentity = new("delay-media", "delayed"), Title = "Delayed media", Media = media };
        var order = new List<string>();
        var engine = new NativeEngine([new DelayMediaProvider(page)], _ => new MediaHttp(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var manifest = path switch {
                "/master.m3u8" => "#EXTM3U\n#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"audio\",NAME=\"English\",DEFAULT=YES,AUTOSELECT=YES,URI=\"audio.m3u8\"\n#EXT-X-STREAM-INF:BANDWIDTH=200000,RESOLUTION=160x90,CODECS=\"avc1.64000b,mp4a.40.2\",AUDIO=\"audio\"\nvideo.m3u8\n",
                "/video.m3u8" => "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXTINF:1,\nvideo.ts\n#EXT-X-ENDLIST\n",
                "/audio.m3u8" => "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXTINF:1.05,\naudio.ts\n#EXT-X-ENDLIST\n",
                "/manifest.mpd" => "<MPD xmlns='urn:mpeg:dash:schema:mpd:2011' type='static' mediaPresentationDuration='PT1S'><Period><AdaptationSet mimeType='video/mp4' codecs='avc1.64000b'><Representation id='video' bandwidth='100000' width='160' height='90'><BaseURL>video.mp4</BaseURL></Representation></AdaptationSet><AdaptationSet mimeType='audio/mp4' codecs='mp4a.40.2'><Representation id='audio' bandwidth='96000'><BaseURL>audio.m4a</BaseURL></Representation></AdaptationSet></Period></MPD>",
                _ => null };
            if (manifest is not null) return new(HttpStatusCode.OK) { Content = new StringContent(manifest) };
            var isVideo = path is "/video.mp4" or "/video.ts";
            if (!isVideo && path is not ("/audio.m4a" or "/audio.ts")) throw new InvalidOperationException("Unexpected media request: " + path);
            var role = isVideo ? "video" : "audio";
            if (httpChunkSize.Length == 0 || order.LastOrDefault() != role) order.Add(role);
            var bytes = isVideo ? videoBytes : audioBytes;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Headers.ETag = new("\"generated-" + role + "\"");
            if (request.Headers.Range?.Ranges.Single() is { From: { } from, To: { } requestedEnd })
            {
                var end = Math.Min(requestedEnd, bytes.Length - 1);
                response.StatusCode = HttpStatusCode.PartialContent;
                response.Content = new ByteArrayContent(bytes[(int)from..((int)end + 1)]);
                response.Content.Headers.ContentRange = new(from, end, bytes.Length);
            }
            return response;
        })));
        var archive = Path.Combine(files.Directory, "archive.txt"); var output = Path.Combine(files.Directory, "delayed.mp4");
        var request = new NativeDownloadRequest { Url = new("https://delay-media.example/watch"), OutputDirectory = files.Directory,
            OutputTemplate = "delayed.%(ext)s", FfmpegPath = ffmpeg, DownloadArchiveFile = archive, KeepOriginal = true, SleepIntervalSeconds = 0.03, NoPart = noPart, BufferSize = bufferSize, NoResizeBuffer = noResizeBuffer, HttpChunkSize = httpChunkSize };
        using var cancel = new CancellationTokenSource(); var waits = 0; var callbacks = 0;
        void Log(string line)
        {
            if (!line.StartsWith("[native] Waiting ", StringComparison.Ordinal)) return;
            order.Add("wait"); if (++waits == 2 && mode == "cancel") cancel.Cancel();
        }
        if (mode == "cancel")
        {
            await File.WriteAllTextAsync(output, "previous complete media"); await File.WriteAllTextAsync(archive, "other preserved\n");
            try { await engine.DownloadAsync(request, null, Log, cancel.Token, _ => callbacks++); throw new InvalidOperationException("Second-track cancellation was ignored."); }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            if (!order.SequenceEqual(new[] { "wait", "video", "wait" }) || callbacks != 0 || await File.ReadAllTextAsync(output) != "previous complete media" ||
                await File.ReadAllTextAsync(archive) != "other preserved\n") throw new InvalidOperationException("Second-track wait changed prior publication or fetched pending audio.");
            return;
        }
        var result = await engine.DownloadAsync(request, null, Log, cancel.Token, _ => callbacks++);
        if (!result.Succeeded || !order.SequenceEqual(new[] { "wait", "video", "wait", "audio" }) || callbacks != 3 || result.OutputPaths.Count != 3)
            throw new InvalidOperationException("Separate tracks did not wait independently or retain complete outputs.");
        foreach (var path in result.OutputPaths) await Run(ffmpeg, ["-v", "error", "-xerror", "-i", path, "-f", "null", "-"], true);
        var frames = await Run(Probe(ffmpeg), ["-v", "error", "-count_frames", "-select_streams", "v:0", "-show_entries", "stream=nb_read_frames", "-of", "csv=p=0", result.OutputPath!]);
        if (frames.Trim() != "12" || !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(videoBytes)) ||
            !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(audioBytes)))
            throw new InvalidOperationException("Delayed merge changed complete picture frames or original media bytes.");
        var repeat = await engine.DownloadAsync(request, null, Log, default, _ => callbacks++);
        if (!repeat.Skipped || waits != 2 || callbacks != 3) throw new InvalidOperationException("Archived separate tracks waited or emitted new completions.");
    }
    private sealed class DelayMediaProvider(PageMetadata page) : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("delay-media", "Real delayed media", 1000);
        public bool CanHandle(Uri url) => true;
        public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => page.ArchiveIdentity;
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(page);
    }
}
