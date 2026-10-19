using System.Net;
using System.Text;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

internal static class DashTransferChecks
{
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Static DASH number templates expand inherited parameters", () => Sync(NumberTemplate)),
        ("DASH timelines expand repeated samples within a finite period", () => Sync(Timeline)),
        ("Explicit DASH segment lists retain every resource and byte range", () => Sync(SegmentList)),
        ("Native DASH segments include initialization before media", Transfer),
        ("Unbounded DASH addressing is rejected before transfer", () => Sync(Limits))
    ];
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static DashManifest Parse(string body) => DashManifestParser.Parse(body, new Uri("https://media.example/manifest.mpd"));
    private static void NumberTemplate()
    {
        var manifest = Parse("<MPD mediaPresentationDuration=\"PT5S\"><Period><AdaptationSet mimeType=\"video/mp4\"><SegmentTemplate timescale=\"1\" duration=\"2\" startNumber=\"3\" initialization=\"init-$RepresentationID$.mp4\" media=\"$RepresentationID$-$Number%05d$.m4s\"/><Representation id=\"video\" height=\"720\" bandwidth=\"1000\"/></AdaptationSet></Period></MPD>");
        var representation = manifest.Representations.Single();
        Assert(representation.Initialization!.AbsoluteUri.EndsWith("init-video.mp4") && representation.Segments.Count == 3, "Initialization or finite count differs.");
        Assert(representation.Segments[0].Url.AbsoluteUri.EndsWith("video-00003.m4s") && representation.Segments[2].Url.AbsoluteUri.EndsWith("video-00005.m4s"), "Number padding/start differs.");
    }
    private static void Timeline()
    {
        var manifest = Parse("<MPD mediaPresentationDuration=\"PT6S\"><Period><AdaptationSet mimeType=\"video/mp4\"><Representation id=\"v\"><SegmentTemplate timescale=\"10\" initialization=\"init.mp4\" media=\"chunk-$Time$.m4s\"><SegmentTimeline><S t=\"0\" d=\"20\" r=\"-1\"/></SegmentTimeline></SegmentTemplate></Representation></AdaptationSet></Period></MPD>");
        Assert(manifest.Representations[0].Segments.Select(segment => segment.Url.AbsolutePath).SequenceEqual(new[] { "/chunk-0.m4s", "/chunk-20.m4s", "/chunk-40.m4s" }), "Negative repetition did not stop at period end.");
    }
    private static void SegmentList()
    {
        var manifest = Parse("<MPD><Period><AdaptationSet mimeType=\"video/mp4\"><Representation id=\"v\"><BaseURL>media.bin</BaseURL><SegmentList><Initialization range=\"0-3\"/><SegmentURL mediaRange=\"4-6\"/><SegmentURL mediaRange=\"7-9\"/></SegmentList></Representation></AdaptationSet></Period></MPD>");
        var representation = manifest.Representations.Single();
        Assert(representation.InitializationRange == new HlsByteRange(0, 4) && representation.Segments.Count == 2 && representation.Segments[1].Range == new HlsByteRange(7, 3), "SegmentList dropped a resource or range.");
    }
    private static async Task Transfer()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var manifest = Parse("<MPD mediaPresentationDuration=\"PT2S\"><Period><AdaptationSet mimeType=\"video/mp4\"><Representation id=\"v\" height=\"360\"><SegmentTemplate duration=\"1\" initialization=\"init.mp4\" media=\"chunk-$Number$.m4s\"/></Representation></AdaptationSet></Period></MPD>");
            using var http = new MediaHttp(new Handler(request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.ASCII.GetBytes(request.RequestUri!.AbsolutePath switch { "/init.mp4" => "INIT", "/chunk-1.m4s" => "A", "/chunk-2.m4s" => "B", _ => throw new InvalidOperationException("Unexpected DASH URL.") }))
            }));
            var output = await new DashDownloader(http).DownloadAsync(manifest, Path.Combine(folder, "result.mpd"), QualityPreference.Best, null, false, null, null, null, default, 0);
            Assert(Path.GetExtension(output) == ".mp4" && File.ReadAllText(output) == "INITAB", "DASH initialization/media order differs.");
        }
        finally { foreach (var file in Directory.EnumerateFiles(folder)) File.Delete(file); Directory.Delete(folder); }
    }
    private static void Limits()
    {
        Throws<NotSupportedException>(() => Parse("<MPD><Period><AdaptationSet mimeType=\"video/mp4\"><Representation id=\"v\"><SegmentTemplate initialization=\"init.mp4\" media=\"$Time$.m4s\"><SegmentTimeline><S d=\"1\" r=\"-1\"/></SegmentTimeline></SegmentTemplate></Representation></AdaptationSet></Period></MPD>"));
        Throws<InvalidDataException>(() => Parse("<MPD mediaPresentationDuration=\"PT1S\"><Period><AdaptationSet><Representation id=\"v\"><SegmentTemplate duration=\"0\" initialization=\"init.mp4\" media=\"$Number$.m4s\"/></Representation></AdaptationSet></Period></MPD>"));
        Throws<InvalidDataException>(() => Parse("<MPD mediaPresentationDuration=\"PT100001S\"><Period><AdaptationSet><Representation id=\"v\"><SegmentTemplate duration=\"1\" initialization=\"init.mp4\" media=\"$Number$.m4s\"/></Representation></AdaptationSet></Period></MPD>"));
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request));
    }
}
