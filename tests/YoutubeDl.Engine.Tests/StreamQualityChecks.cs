using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Media;

internal static class StreamQualityChecks
{
    public static IEnumerable<(string Name, Func<Task> Test)> All => new (string, Action)[]
    {
        ("Stream quality native defaults preserve original audio", Defaults),
        ("Stream quality best video prefers higher frame rates", FrameRate),
        ("Stream quality best video distinguishes bitrates above one megabit", Bitrate),
        ("Stream quality best video retains resolution priority", Resolution),
        ("Stream quality best audio uses lossless sources when available", Lossless),
        ("Stream quality best audio keeps the default language", Language),
        ("Stream quality best audio is independent of video container", Pair),
        ("Stream quality unusual PCM keeps its codec without conversion", Pcm)
    }.Select(item => (item.Item1, (Func<Task>)(() => { item.Item2(); return Task.CompletedTask; })))
        .Append(("Stream quality HLS best video prefers higher frame rates", Hls));

    private static NativeDownloadRequest Request => new() { Url = new("https://media.example/watch"), OutputDirectory = Path.GetTempPath() };
    private static MediaCandidate Video(string id, int height, double fps, double bitrate) => new()
    { Id = id, Url = new("https://media.example/" + id), HasVideo = true, HasAudio = false, Height = height,
        FramesPerSecond = fps, Bandwidth = bitrate, Extension = "mp4", VideoCodec = "h264" };
    private static MediaCandidate Audio(string id, string extension, string codec, double bitrate, bool main = true) => new()
    { Id = id, Url = new("https://media.example/" + id), HasVideo = false, HasAudio = true, Extension = extension,
        AudioCodec = codec, Bandwidth = bitrate, IsDefaultAudio = main };
    private static MediaCandidate Best(params MediaCandidate[] candidates) => QualitySelector.Select(candidates, QualityPreference.Best, null, false);
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Defaults() => Assert(Request.AudioFormat == "best" && Request.RecodeVideo == "" &&
        Request.MaximumHeight is null && Request.MaximumFrameRate == 0 && Request.VideoCodec == "any", "Native defaults constrain or convert source streams.");
    private static void FrameRate() => Assert(Best(Video("30fps", 2160, 30, 12000000), Video("60fps", 2160, 60, 9000000)).Id == "60fps", "Best lost higher frame rate.");
    private static void Bitrate() => Assert(Best(Video("low", 2160, 60, 5000000), Video("high", 2160, 60, 20000000)).Id == "high", "Best capped bitrate ranking.");
    private static void Resolution() => Assert(Best(Video("1080", 1080, 120, 40000000), Video("2160", 2160, 30, 2000000)).Id == "2160", "Frame rate or bitrate overrode resolution.");
    private static void Lossless() => Assert(QualitySelector.Select([Audio("lossy", "m4a", "aac", 512000), Audio("lossless", "flac", "flac", 300000)], QualityPreference.Best, null, true).Id == "lossless", "Lossy rendition displaced the available original lossless audio.");
    private static void Language() => Assert(QualitySelector.Select([Audio("alternate", "flac", "flac", 900000, false), Audio("main", "opus", "opus", 256000)], QualityPreference.Best, null, true).Id == "main", "Quality switched to a different audio language.");
    private static void Pair()
    {
        var page = new PageMetadata { Media = [Video("video", 2160, 60, 12000000), Audio("aac", "m4a", "aac", 128000), Audio("opus", "opus", "opus", 256000)] };
        var plan = MediaDownloadPlan.Create(page, Request);
        Assert(plan.Audio?.Id == "opus" && plan.Extension == "mkv", "Container compatibility reduced audio quality.");
        var exact = MediaDownloadPlan.Create(page, Request with { FormatSelector = "'video'+'aac'" });
        Assert(exact.Audio?.Id == "aac" && exact.Extension == "mp4", "An explicit format override was ignored.");
    }
    private static void Pcm() => Assert(AudioOutputPlan.AutomaticExtension("pcm_f64be", "nut", "best") == "mka", "Best audio chose a WAV conversion for an unsupported PCM layout.");

    private static async Task Hls()
    {
        var directory = Path.Combine(Path.GetTempPath(), "stream-quality-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var fetched = new List<string>();
            var engine = new NativeEngine(httpFactory: _ => new YoutubeDl.Engine.Http.MediaHttp(new HlsHandler(fetched)));
            var result = await engine.DownloadAsync(Request with { Url = new("https://media.example/master.m3u8"), OutputDirectory = directory, Retries = 0 }, null, null, default);
            Assert(result.Succeeded && fetched.Contains("/fast.m3u8") && !fetched.Contains("/slow.m3u8"), "Best HLS video lost the higher frame-rate rendition.");
        }
        finally { Directory.Delete(directory, true); }
    }
    private sealed class HlsHandler(List<string> fetched) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; fetched.Add(path);
            var text = path == "/master.m3u8" ? "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=12000000,RESOLUTION=3840x2160,FRAME-RATE=30\nslow.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=9000000,RESOLUTION=3840x2160,FRAME-RATE=60\nfast.m3u8\n" :
                path.EndsWith(".m3u8") ? "#EXTM3U\n#EXTINF:1,\nsegment.ts\n#EXT-X-ENDLIST\n" : "source media";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(text) });
        }
    }
}
