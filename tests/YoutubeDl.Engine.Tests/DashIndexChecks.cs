using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

internal static class DashIndexChecks
{
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("MP4 sidx vectors retain absolute byte offsets and media times", () => Sync(Vectors)),
        ("Extended MP4 indexes preserve 64-bit offsets and presentation times", () => Sync(Extended)),
        ("Truncated, overflowing, nested and ambiguous MP4 indexes fail explicitly", () => Sync(Invalid)),
        ("DASH SegmentBase inherits ranges and overrides other addressing modes", () => Sync(Manifest)),
        ("Embedded DASH text tracks do not block media or silently satisfy subtitle requests", Captions),
        ("Unsupported DASH alternatives cannot discard required audio or video tracks", Alternatives),
        ("Unsupported indexed DASH layouts fail before media transfer", () => Sync(Unsupported)),
        ("Native indexed DASH retrieves exact ranges and preserves file offsets", Transfer),
        ("Indexed DASH accepts codec padding but refuses a clipped presentation", Timing),
        ("Indexed DASH uses modification dates when a strong entity tag is unavailable", ModificationDate),
        ("Redirected adaptive manifests resolve resources against the final address", Redirects),
        ("Changed entities and ignored ranges preserve prior indexed DASH output", Boundaries),
        ("Indexed DASH range retries discard partial attempt bytes", Retry),
        ("Canceled indexed DASH transfers retain the previous output", Cancellation)
    ];

    internal static byte[] Index(bool version1 = false, ulong firstOffset = 0, ulong earliest = 0, bool extended = false)
    {
        var size = (extended ? 16 : 8) + (version1 ? 32 : 24) + 24;
        var bytes = new byte[size]; var position = 0;
        Put(extended ? 1U : (uint)size); Encoding.ASCII.GetBytes("sidx").CopyTo(bytes, position); position += 4;
        if (extended) { BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(position), (ulong)size); position += 8; }
        Put(version1 ? 0x01000000U : 0); Put(1); Put(1000);
        if (version1) { Wide(earliest); Wide(firstOffset); } else { Put((uint)earliest); Put((uint)firstOffset); }
        Put(2); Put(3); Put(1000); Put(0x90000000); Put(5); Put(2000); Put(0x90000000);
        return bytes;
        void Put(uint value) { BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(position), value); position += 4; }
        void Wide(ulong value) { BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(position), value); position += 8; }
    }
    private static void Vectors()
    {
        var index = Mp4SegmentIndexParser.Parse(Index(firstOffset: 7, earliest: 50), 100);
        Assert(index.TrackId == 1 && index.Timescale == 1000 && index.EarliestTime == 50, "Index header facts changed.");
        Assert(index.Segments[0] == new Mp4IndexedSegment(new(163, 3), 50, 1000) && index.Segments[1] == new Mp4IndexedSegment(new(166, 5), 1050, 2000), "The first offset was not relative to the actual sidx end.");
        var free = new byte[] { 0, 0, 0, 8, (byte)'f', (byte)'r', (byte)'e', (byte)'e' };
        var prefixed = Mp4SegmentIndexParser.Parse(free.Concat(Index()).ToArray(), 100);
        Assert(prefixed.Segments[0].Range.Offset == 164, "Prefix boxes were omitted from absolute positioning.");
    }
    private static void Extended()
    {
        var index = Mp4SegmentIndexParser.Parse(Index(true, 1UL << 33, 1UL << 40, true), 1000, 1000);
        Assert(index.Segments[0].Range.Offset == 1000 + 72 + (1L << 33) && index.Segments[1].StartTime == (1UL << 40) + 1000, "64-bit index fields were narrowed.");
    }
    private static void Invalid()
    {
        foreach (var length in new[] { 0, 7, 12, 30, 55 }) Throws<InvalidDataException>(() => Mp4SegmentIndexParser.Parse(Index()[..length], 0));
        var nested = Index(); BinaryPrimitives.WriteUInt32BigEndian(nested.AsSpan(32), 0x80000003);
        Throws<NotSupportedException>(() => Mp4SegmentIndexParser.Parse(nested, 0));
        var zeroScale = Index(); BinaryPrimitives.WriteUInt32BigEndian(zeroScale.AsSpan(16), 0);
        Throws<InvalidDataException>(() => Mp4SegmentIndexParser.Parse(zeroScale, 0));
        Throws<InvalidDataException>(() => Mp4SegmentIndexParser.Parse(Index(), 0, 48000));
        Throws<InvalidDataException>(() => Mp4SegmentIndexParser.Parse(Index(true, ulong.MaxValue), 0));
        Throws<InvalidDataException>(() => Mp4SegmentIndexParser.Parse(Index(true, 0, ulong.MaxValue), 0));
        Throws<NotSupportedException>(() => Mp4SegmentIndexParser.Parse(Index().Concat(Index()).ToArray(), 0));
        var version = Index(); version[8] = 2;
        Throws<NotSupportedException>(() => Mp4SegmentIndexParser.Parse(version, 0));
    }

    private static string Xml(string extra = "", string init = "0-3", string range = "4-59") =>
        "<MPD mediaPresentationDuration='PT3S'><Period><AdaptationSet mimeType='video/mp4'><Representation id='v' height='360'><BaseURL>track.mp4</BaseURL><SegmentBase timescale='1000' indexRange='" + range + "' " + extra + "><Initialization range='" + init + "'/></SegmentBase></Representation></AdaptationSet></Period></MPD>";
    private static DashManifest Parse(string xml) => DashManifestParser.Parse(xml, new Uri("https://media.example/source.mpd"));
    private static void Manifest()
    {
        var rep = Parse(Xml()).Representations.Single();
        Assert(rep.SegmentBase?.IndexRange == new HlsByteRange(4, 56) && rep.InitializationRange == new HlsByteRange(0, 4) && rep.Segments.Count == 0, "SegmentBase ranges were not retained.");
        var overrideXml = Xml().Replace("<Representation", "<SegmentTemplate duration='1' initialization='wrong.mp4' media='wrong-$Number$.m4s'/><Representation");
        Assert(Parse(overrideXml).Representations.Single().SegmentBase is not null, "An inherited template hid explicit indexed addressing.");
        var inherited = "<MPD><Period><AdaptationSet mimeType='video/mp4'><SegmentBase timescale='1000' indexRange='4-59'><Initialization range='0-3'/></SegmentBase><Representation id='v'><BaseURL>track.mp4</BaseURL></Representation></AdaptationSet></Period></MPD>";
        Assert(Parse(inherited).Representations.Single().SegmentBase?.Timescale == 1000, "Indexed addressing did not inherit from the adaptation set.");
    }
    private static void Unsupported()
    {
        Throws<NotSupportedException>(() => Parse(Xml("presentationTimeOffset='1'")));
        Throws<NotSupportedException>(() => Parse(Xml().Replace("video/mp4", "video/webm")));
        Throws<NotSupportedException>(() => Parse(Xml(init: "1-3")));
        Throws<NotSupportedException>(() => Parse(Xml(init: "0-10")));
        Throws<InvalidDataException>(() => Parse(Xml().Replace("</SegmentBase>", "</SegmentBase><SegmentList/>")));
        Throws<NotSupportedException>(() => Parse(Xml().Replace("<Initialization", "<RepresentationIndex sourceURL='index.bin'/><Initialization")));
    }

    private static async Task Captions()
    {
        var text = "<AdaptationSet contentType='text'><Representation id='text' mimeType='application/mp4' codecs='stpp.ttml.im1i'><BaseURL>text.mp4</BaseURL><SegmentBase indexRange='4-59'><Initialization range='0-3'/></SegmentBase></Representation></AdaptationSet>";
        var xml = Xml().Replace("</Period>", text + "</Period>");
        var parsed = Parse(xml);
        Assert(parsed.Representations.Count == 1 && parsed.HasSegmentedSubtitles, "An embedded text track was mistaken for media or erased.");
        using var files = new Files(); var calls = 0;
        var engine = new YoutubeDl.Engine.NativeEngine(httpFactory: _ => new MediaHttp(new Handler(_ =>
        { calls++; return new(HttpStatusCode.OK) { Content = new StringContent(xml) }; })));
        await Expect<NotSupportedException>(() => engine.DownloadAsync(new YoutubeDl.Engine.NativeDownloadRequest
        { Url = new Uri("https://media.example/source.mpd"), OutputDirectory = files.Directory, WriteSubs = true }, null, null, default));
        Assert(calls == 1 && !File.Exists(files.Output), "An unsupported caption request fetched media or published a result.");
    }

    private static async Task Alternatives()
    {
        var alternate = "<AdaptationSet mimeType='video/webm'><Representation id='webm'><BaseURL>webm.webm</BaseURL><SegmentBase indexRange='4-59'><Initialization range='0-3'/></SegmentBase></Representation></AdaptationSet>";
        var manifest = Parse(Xml().Replace("</Period>", alternate + "</Period>"));
        Assert(manifest.Representations.Count == 2 && DashDownloader.ToCandidates(manifest).Count == 1 && manifest.Representations.Last().UnavailableReason is not null,
            "Unsupported alternatives erased a usable representation or appeared ready.");
        using var files = new Files();
        using var http = new MediaHttp(new Handler(_ => throw new InvalidOperationException("An unsupported required track reached HTTP.")));
        var missingAudio = Parse(Xml().Replace("</Period>", alternate.Replace("video/webm", "audio/webm") + "</Period>"));
        await Expect<NotSupportedException>(() => new DashDownloader(http).DownloadAsync(missingAudio, files.Output, QualityPreference.Best, null, false, null, null, null, default, 0));
    }

    private static byte[] Resource => Encoding.ASCII.GetBytes("INIT").Concat(Index(firstOffset: 2)).Concat(Encoding.ASCII.GetBytes("XXabc12345")).ToArray();
    private static async Task Transfer()
    {
        using var files = new Files(); var resource = Resource; var ranges = new List<(long, long)>();
        using var http = new MediaHttp(new Handler(request =>
        {
            var range = request.Headers.Range!.Ranges.Single(); ranges.Add((range.From!.Value, range.To!.Value));
            if (ranges.Count > 1) Assert(request.Headers.IfMatch.Single().Tag == "\"v1\"", "The index entity validator was not carried into media requests.");
            return Range(request, resource);
        }));
        var output = await Download(http, files);
        Assert(File.ReadAllBytes(output).AsSpan().SequenceEqual(resource), "Index/prefix/media positions changed in the assembled file.");
        Assert(ranges.SequenceEqual(new[] { (4L, 59L), (0L, 3L), (60L, 61L), (62L, 64L), (65L, 69L) }), "The native downloader requested the wrong byte ranges.");
    }
    private static async Task Boundaries()
    {
        foreach (var mode in new[] { "whole", "etag", "length", "short" })
        {
            using var files = new Files(); await File.WriteAllTextAsync(files.Output, "previous"); var count = 0;
            using var http = new MediaHttp(new Handler(request =>
            {
                count++; var response = Range(request, Resource);
                if (mode == "whole") response.StatusCode = HttpStatusCode.OK;
                if (count > 1 && mode == "etag") response.Headers.ETag = new("\"v2\"");
                if (count > 1 && mode == "length") response.Content.Headers.ContentRange = new(0, 3, Resource.Length + 1);
                if (mode == "short") { response.Content = new ByteArrayContent(new byte[] { 1 }); response.Content.Headers.ContentRange = new(4, 59, Resource.Length); response.Content.Headers.ContentLength = 56; }
                return response;
            }));
            if (mode == "short") await Expect<EndOfStreamException>(() => Download(http, files));
            else await Expect<InvalidDataException>(() => Download(http, files));
            Assert(File.ReadAllText(files.Output) == "previous" && !Directory.GetFiles(files.Directory, ".dash-indexed-*").Any(), "A failed indexed transfer replaced the prior output.");
        }
    }
    private static async Task Timing()
    {
        using var files = new Files();
        using var http = new MediaHttp(new Handler(request => Range(request, Resource)));
        var downloader = new DashDownloader(http);
        await downloader.DownloadAsync(Parse(Xml().Replace("PT3S", "PT2.95S")), files.Output, QualityPreference.Best, null, false, null, null, null, default, 0);
        var before = File.ReadAllBytes(files.Output);
        await Expect<NotSupportedException>(() => downloader.DownloadAsync(Parse(Xml().Replace("PT3S", "PT2S")), files.Output, QualityPreference.Best, null, false, null, null, null, default, 0));
        Assert(File.ReadAllBytes(files.Output).AsSpan().SequenceEqual(before), "A clipped presentation replaced the complete prior file.");
    }
    private static async Task ModificationDate()
    {
        using var files = new Files(); var requests = 0; var modified = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var http = new MediaHttp(new Handler(request =>
        {
            if (requests++ > 0) Assert(request.Headers.IfUnmodifiedSince == modified && request.Headers.IfMatch.Count == 0, "Weak entity tags were used as strong validators or the modification date was lost.");
            var response = Range(request, Resource); response.Headers.ETag = new("\"weak\"", isWeak: true); response.Content.Headers.LastModified = modified; return response;
        }));
        await Download(http, files);
        Assert(requests == 5, "The indexed date-validated flow did not request each resource range.");
    }
    private static async Task Redirects()
    {
        using var http = new MediaHttp(new Handler(request =>
        {
            var dash = request.RequestUri!.AbsolutePath.EndsWith(".mpd");
            return new(HttpStatusCode.OK) { Content = new StringContent(dash ? Xml() : "#EXTM3U\n#EXT-X-TARGETDURATION:1\n#EXTINF:1,\nsegment.ts\n#EXT-X-ENDLIST\n"),
                RequestMessage = new(HttpMethod.Get, dash ? "https://cdn.example/show/index.mpd" : "https://cdn.example/show/index.m3u8") };
        }));
        var manifest = await new DashDownloader(http).ReadAsync(new Uri("https://origin.example/old.mpd"), default);
        var playlist = await new HlsDownloader(http).ReadAsync(new Uri("https://origin.example/old.m3u8"), default);
        Assert(manifest.Representations.Single().Url.AbsoluteUri == "https://cdn.example/show/track.mp4" && playlist.Segments.Single().AbsoluteUri == "https://cdn.example/show/segment.ts", "An adaptive redirect retained the stale resource base.");
    }
    private static async Task Retry()
    {
        using var files = new Files(); var prefixRequests = 0;
        using var http = new MediaHttp(new Handler(request =>
        {
            var response = Range(request, Resource);
            if (request.Headers.Range!.Ranges.Single().From == 0 && prefixRequests++ == 0)
            { response.Content = new ByteArrayContent(Encoding.ASCII.GetBytes("IN")); response.Content.Headers.ContentLength = 4; response.Content.Headers.ContentRange = new(0, 3, Resource.Length); }
            return response;
        }));
        var output = await Download(http, files, retries: 1);
        Assert(prefixRequests == 2 && File.ReadAllBytes(output).AsSpan().SequenceEqual(Resource), "A retried range duplicated partial bytes.");
    }
    private static async Task Cancellation()
    {
        using var files = new Files(); await File.WriteAllTextAsync(files.Output, "previous"); using var cancel = new CancellationTokenSource();
        using var http = new MediaHttp(new Handler(request => { if (request.Headers.Range!.Ranges.Single().From == 62) cancel.Cancel(); return Range(request, Resource); }));
        await Expect<OperationCanceledException>(() => Download(http, files, ct: cancel.Token));
        Assert(File.ReadAllText(files.Output) == "previous", "Cancellation published a partial indexed track.");
    }
    private static Task<string> Download(MediaHttp http, Files files, int retries = 0, CancellationToken ct = default) => new DashDownloader(http).DownloadAsync(Parse(Xml()), files.Output,
        QualityPreference.Best, null, false, null, null, null, ct, retries);
    internal static HttpResponseMessage Range(HttpRequestMessage request, byte[] data)
    {
        var range = request.Headers.Range!.Ranges.Single(); var start = checked((int)range.From!.Value); var end = checked((int)range.To!.Value);
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(data[start..(end + 1)]) };
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, data.Length); response.Headers.ETag = new("\"v1\""); return response;
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private sealed class Files : IDisposable
    {
        public string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", "sidx-" + Guid.NewGuid().ToString("N"));
        public string Output => Path.Combine(Directory, "result.mp4");
        public Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
