using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.YouTube;

internal static class FormatSelectorChecks
{
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Native format selectors preserve explicit track pairs and alternative fallback", () => Sync(Explicit)),
        ("Best/worst aliases, ranked choices, and extension choices select exposed formats", () => Sync(Aliases)),
        ("Native numeric/text/regex filters support quoted values and unknown-field rules", () => Sync(Filters)),
        ("Merged selectors ignore later duplicate stream kinds in expression order", () => Sync(DuplicateStreams)),
        ("Codec and frame-rate constraints reject incompatible or unknown progressive formats", () => Sync(Constraints)),
        ("A single explicit selector does not silently add an audio track", () => Sync(Single)),
        ("Malformed and unsupported selector syntax fails before HTTP", Invalid),
        ("HLS master selection enforces codec and frame-rate limits before segment transfer", Hls),
        ("DASH rational frame rates and inherited codecs constrain representation selection", Dash)
    ];
    private static PageMetadata Page => YouTubePlayerParser.ParseResponse(YouTubeChecks.FixtureJson, "abcdefghijk");
    private static NativeDownloadRequest Request => new() { Url = new Uri("https://youtube.com/watch?v=abcdefghijk"), OutputDirectory = Path.GetTempPath() };
    private static IReadOnlyList<MediaCandidate> Select(string selector, PageMetadata? page = null) => NativeFormatSelector.Select(page ?? Page, selector, Request);
    private static void Explicit()
    {
        var pair = MediaDownloadPlan.Create(Page, Request with { FormatSelector = "137+140:en.0" });
        Assert(pair.Primary.Id == "137" && pair.Audio!.Id == "140:en.0" && pair.Extension == "mp4", "Explicit pairing was changed.");
        Assert(Select("299/248/18").Single().Id == "18", "An unresolved format prevented an available fallback.");
        Assert(Select("(299/137)+140:en.0").Select(format => format.Id).SequenceEqual(["137", "140:en.0"]), "Parenthesized alternatives lost their pairing.");
        Assert(Select("'137'").Single().Id == "137", "Quoted exact identifier changed.");
    }
    private static void Aliases()
    {
        Assert(Select("bv").Single().Id == "137" && Select("b").Single().Id == "18" && Select("ba").Single().Id == "251:en.0", "Best media-type aliases changed.");
        Assert(Select("b*.2").Single().Id == "18" && Select("mp4").Single().Id == "137", "Rank or extension selection changed.");
        Assert(Select("wa").Single().Id == "140:en.0" && Select("wv*").Single().Id == "18", "Worst media-type selection changed.");
    }
    private static void Filters()
    {
        Assert(Select("bv*[height<=720][fps<=30]+ba/b").Single().Id == "18", "Combined filtered fallback was changed.");
        Assert(Select("bv[vcodec~='^(avc|hvc)'][height>=1080]").Single().Id == "137", "Quoted regex filtering failed.");
        Assert(Select("ba[ext!=webm][language^=en]").Single().Id == "140:en.0", "Negation or language prefixes changed.");
        Assert(Select("b*[filesize<=8]").Single().Id == "137", "File-size filtering ignored known values.");
        var unknown = new PageMetadata { Media = [Candidate("unknown", null, null, null, true, true)] };
        Throws<NotSupportedException>(() => Select("b[fps<=30]", unknown));
        Assert(Select("b[fps<=?30]", unknown).Single().Id == "unknown", "The allow-missing filter flag was ignored.");
        Assert(Select("b*[filesize<1KiB]").Count == 1, "Binary numeric units failed.");
    }
    private static void DuplicateStreams()
    {
        Assert(Select("18+251:en.0").Single().Id == "18", "A redundant audio track was downloaded.");
        Assert(Select("140:en.0+18").Single().Id == "140:en.0", "Expression order was ignored when suppressing duplicate kinds.");
        Assert(Select("137+18+140:en.0").Select(format => format.Id).SequenceEqual(["137", "140:en.0"]), "A later combined stream duplicated selected video.");
    }
    private static void Constraints()
    {
        var page = new PageMetadata { Media =
        [Candidate("avc30", 1080, 30, "avc1.640028", false, true), Candidate("avc60", 1080, 60, "avc1.640028", false, true),
         Candidate("hevc", 2160, 30, "hvc1.1.6", false, true), Candidate("vp9", 1080, 30, "vp09.00.40", false, true)] };
        var limited = MediaDownloadPlan.Create(page, Request with { VideoOnly = true, VideoCodec = "h264", MaximumFrameRate = 30 });
        Assert(limited.Primary.Id == "avc30", "Codec/frame-rate limits selected an incompatible video.");
        Assert(MediaDownloadPlan.Create(page, Request with { VideoOnly = true, VideoCodec = "h265" }).Primary.Id == "hevc", "HEVC codec aliases failed.");
        Assert(MediaDownloadPlan.Create(page, Request with { VideoOnly = true, VideoCodec = "vp9" }).Primary.Id == "vp9", "VP09 spelling failed.");
        Throws<InvalidOperationException>(() => MediaDownloadPlan.Create(page, Request with { FormatId = "avc60", VideoOnly = true, MaximumFrameRate = 30 }));
        Throws<InvalidOperationException>(() => MediaDownloadPlan.Create(page, Request with { VideoOnly = true, VideoCodec = "av1" }));
    }
    private static void Single()
    {
        var plan = MediaDownloadPlan.Create(Page, Request with { FormatSelector = "137" });
        Assert(plan.Primary.Id == "137" && plan.Audio is null, "A silent explicit format was changed into an automatic pair.");
        var audio = MediaDownloadPlan.Create(Page, Request with { FormatSelector = "140:en.0" });
        Assert(audio.Primary.IsAudioOnly && audio.Audio is null, "An explicit audio-only format was changed.");
        Throws<ArgumentException>(() => MediaDownloadPlan.Create(Page, Request with { FormatId = "18", FormatSelector = "best" }));
    }
    private static async Task Invalid()
    {
        var engine = new NativeEngine(httpFactory: _ => throw new InvalidOperationException("Invalid selectors must fail before HTTP."));
        foreach (var selector in new[] { "137+", "(bv+ba", "bv[fps=wrong]", "bv[vcodec~='[']", "bv[unsupported=1]", "all", "18,137", "best.999999999999999" })
        {
            try { await engine.InspectAsync(Request with { FormatSelector = selector }, default); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { continue; }
            throw new InvalidOperationException("Invalid selector reached HTTP: " + selector);
        }
    }
    private static async Task Hls()
    {
        using var files = new Files();
        var requests = new List<string>();
        const string master = """
        #EXTM3U
        #EXT-X-STREAM-INF:BANDWIDTH=2000,RESOLUTION=1280x720,FRAME-RATE=60,CODECS="av01.0.05M.08,mp4a.40.2"
        high.m3u8
        #EXT-X-STREAM-INF:BANDWIDTH=1000,RESOLUTION=640x360,FRAME-RATE=29.97,CODECS="avc1.42001e,mp4a.40.2"
        low.m3u8
        """;
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request =>
        {
            var name = Path.GetFileName(request.RequestUri!.AbsolutePath); requests.Add(name);
            return Response(name == "master.m3u8" ? master : name.EndsWith(".m3u8") ? "#EXTM3U\nlow.ts\n#EXT-X-ENDLIST\n" : "low media");
        })));
        var request = Request with { Url = new Uri("https://media.example/master.m3u8"), OutputDirectory = files.Directory, VideoCodec = "h264", MaximumFrameRate = 30, Retries = 0 };
        var result = await engine.DownloadAsync(request, null, null, default);
        Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == "low media" && requests.SequenceEqual(["master.m3u8", "low.m3u8", "low.ts"]), "HLS constraints were ignored.");
        requests.Clear();
        await Expect<InvalidOperationException>(() => engine.DownloadAsync(request with { VideoCodec = "av1" }, null, null, default));
        Assert(requests.SequenceEqual(["master.m3u8"]), "An incompatible HLS format started segment transfer.");
    }
    private static async Task Dash()
    {
        using var files = new Files();
        const string xml = """
        <MPD><Period><AdaptationSet mimeType="video/mp4" codecs="avc1.640028" frameRate="30000/1001">
          <Representation id="low" height="360" bandwidth="100"><BaseURL>low.mp4</BaseURL></Representation>
          <Representation id="fast" height="1080" frameRate="60" bandwidth="1000"><BaseURL>fast.mp4</BaseURL></Representation>
        </AdaptationSet><AdaptationSet mimeType="video/mp4" codecs="av01.0.08M.08" frameRate="30">
          <Representation id="av1" height="2160"><BaseURL>av1.mp4</BaseURL></Representation>
        </AdaptationSet></Period></MPD>
        """;
        var manifest = DashManifestParser.Parse(xml, new Uri("https://media.example/master.mpd"));
        Assert(Math.Abs(manifest.Representations[0].FramesPerSecond!.Value - 29.97002997002997) < 0.00001, "DASH rational frame rate changed.");
        var requests = new List<string>();
        using var http = new MediaHttp(new Handler(request => { requests.Add(request.RequestUri!.AbsolutePath); return Response("chosen video"); }));
        var path = await new DashDownloader(http).DownloadAsync(manifest, Path.Combine(files.Directory, "result.mp4"), QualityPreference.Best, null, false, null, null, null, default, 0,
            videoOnly: true, maximumFrameRate: 30, videoCodec: "h264");
        Assert(File.ReadAllText(path) == "chosen video" && requests.SequenceEqual(["/low.mp4"]), "DASH representation constraints were ignored.");
    }
    private static MediaCandidate Candidate(string id, int? height, double? fps, string? codec, bool audio, bool video) => new()
    { Id = id, Url = new Uri("https://media.example/" + id + ".mp4"), Extension = "mp4", Height = height, FramesPerSecond = fps, VideoCodec = codec, HasAudio = audio, HasVideo = video };
    private static HttpResponseMessage Response(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Files : IDisposable
    {
        public string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        public Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
