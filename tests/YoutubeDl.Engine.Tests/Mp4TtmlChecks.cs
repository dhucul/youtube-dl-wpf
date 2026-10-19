using System.Buffers.Binary;
using System.Net;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;

internal static class Mp4TtmlChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("MP4 TTML clips each sample and joins only continuing visible text", () => Sync(Timing)),
        ("MP4 TTML uses track document time before presentation offsets", () => Sync(Offsets)),
        ("MP4 TTML empty documents preserve caption gaps and may describe an empty track", () => Sync(Empty)),
        ("MP4 TTML retains simultaneous identical paragraphs across sample boundaries", () => Sync(ConcurrentCues)),
        ("MP4 TTML validates namespaces, local references and clear text sample entries", () => Sync(Entries)),
        ("MP4 TTML rejects invalid samples and honors ranged absolute data offsets", () => Sync(Bounds)),
        ("DASH TTML advertises supported text profiles while refusing image profiles", () => Sync(Profiles)),
        ("DASH TTML indexed, template and ranged-list captions save without media", Saving),
        ("DASH TTML parallel caption resources retain manifest order and drain workers", Parallel),
        ("DASH TTML parallel failures, budgets and cancellation preserve published files", ParallelFailures),
        ("DASH TTML parse failures do not replace previous media or captions", Publication)
    ];

    internal static byte[] Xml(string paragraphs) => Encoding.UTF8.GetBytes("<tt xmlns='http://www.w3.org/ns/ttml'><body><div>" + paragraphs + "</div></body></tt>");
    internal static byte[] Movie(byte[][] samples, out int index, ulong decode = 0, bool absolute = false, int composition = 0, bool protectedTrack = false, string auxiliary = "") =>
        DashCaptionChecks.Movie(samples, out index, decode, absolute, composition, protectedTrack, ttml: true, auxiliary: auxiliary);
    internal static byte[] Samples(out int index) => Movie([Xml("<p begin='0s' end='1.5s'>First TTML cue.</p>"), Xml("<p begin='0s' end='1.5s'>First TTML cue.</p>"), Xml(""), Xml("<p begin='3s' end='4s'>Last TTML cue.</p>")], out index);

    private static void Timing()
    {
        var bytes = Samples(out _); var text = Mp4TtmlReader.Read(bytes, 3.5);
        Assert(text.Contains("00:00:00.000 --> 00:00:01.500\nFirst TTML cue.") && text.Contains("00:00:03.000 --> 00:00:03.500\nLast TTML cue.") && text.Split(" --> ").Length == 3,
            "TTML sample clipping, continuing-cue merging or presentation duration changed.");
        var old = Xml("<p begin='0s' end='10s'>Old</p>"); var updated = Xml("<p begin='0s' end='10s'>Updated</p>");
        var changed = Mp4TtmlReader.Read(Movie([old, updated], out _));
        Assert(changed.Contains("00:00:00.000 --> 00:00:01.000\nOld") && changed.Contains("00:00:01.000 --> 00:00:02.000\nUpdated"), "A document outlived its own MP4 sample.");
    }
    private static void Offsets()
    {
        var xml = Xml("<p begin='0s' end='10s'>Window</p>");
        var text = Mp4TtmlReader.Read(Movie([xml, xml, xml], out _, 2000), 2.5, 2);
        Assert(text.Contains("00:00:00.000 --> 00:00:02.500\nWindow") && text.Split(" --> ").Length == 2, "Sample time was incorrectly added to TTML document time.");
        const ulong epoch = 1_800_000_000_000;
        text = Mp4TtmlReader.Read(Movie([Xml("<p begin='1799999999.750s' end='1800000000.500s'>Epoch</p>")], out _, epoch, composition: -250), 1, epoch / 1000d);
        Assert(text.Contains("00:00:00.000 --> 00:00:00.500\nEpoch"), "Large track time, signed composition time or presentation offset lost precision.");
        text = Mp4TtmlReader.Read(Movie([Xml("<p>Bounded by its sample</p>")], out _, 2000), 1, 2);
        Assert(text.Contains("00:00:00.000 --> 00:00:01.000"), "The sample extent did not bound otherwise indefinite TTML text.");
    }
    private static void Empty()
    {
        var cue = Xml("<p begin='0s' end='9s'>Repeated</p>");
        var text = Mp4TtmlReader.Read(Movie([cue, Xml(""), cue], out _));
        Assert(text.Split(" --> ").Length == 3 && !text.Contains("00:00:00.000 --> 00:00:03.000"), "An empty sample did not end the previous caption.");
        Assert(Mp4TtmlReader.Read(Movie([Xml("")], out _)) == "WEBVTT\n\n", "A valid empty TTML document could not represent silence.");
    }
    private static void ConcurrentCues()
    {
        var xml = Xml("<p begin='0s' end='3s'>Same</p><p begin='0s' end='3s'>Same</p>");
        var text = Mp4TtmlReader.Read(Movie([xml, xml, xml], out _));
        Assert(text.Split("00:00:00.000 --> 00:00:03.000").Length == 3, "Identical concurrent TTML paragraphs were dropped or duplicated at sample boundaries.");
    }
    private static void Entries()
    {
        var sample = Xml("<p begin='0s' dur='1s'>Text</p>");
        Throws<NotSupportedException>(() => Mp4TtmlReader.Read(Movie([sample], out _, protectedTrack: true)));
        Throws<NotSupportedException>(() => Mp4TtmlReader.Read(Movie([sample], out _, auxiliary: "image/png")));
        Assert(Mp4TtmlReader.Read(DashCaptionChecks.Movie([sample], out _, ttml: true, ttmlNamespaces: "http://www.w3.org/ns/ttml#parameter http://www.w3.org/ns/ttml urn:ebu:metadata")).Contains("Text"),
            "An MP4 TTML namespace list was mistaken for a different XML language.");
        Throws<NotSupportedException>(() => Mp4TtmlReader.Read(DashCaptionChecks.Movie([DashCaptionChecks.Sample("VTT")], out _)));
        var reference = Movie([sample], out _); var entry = Find(reference, "stpp"); BinaryPrimitives.WriteUInt16BigEndian(reference.AsSpan(entry + 10, 2), 2);
        Throws<NotSupportedException>(() => Mp4TtmlReader.Read(reference));
        var wrongNamespace = Movie([sample], out _); var ns = Find(wrongNamespace, "http://www.w3.org/ns/ttml"); wrongNamespace[ns] = (byte)'x';
        Throws<NotSupportedException>(() => Mp4TtmlReader.Read(wrongNamespace));
        var foreignDefaults = Movie([sample], out _); var trex = Find(foreignDefaults, "trex");
        BinaryPrimitives.WriteUInt32BigEndian(foreignDefaults.AsSpan(trex + 8, 4), 99);
        Assert(Mp4TtmlReader.Read(foreignDefaults).Contains("Text"), "Explicit sample sizes/durations were ignored when no matching trex was present.");
        var trun = Find(foreignDefaults, "trun"); BinaryPrimitives.WriteUInt32BigEndian(foreignDefaults.AsSpan(trun + 4, 4), 0x01000001);
        Throws<InvalidDataException>(() => Mp4TtmlReader.Read(foreignDefaults));
    }
    private static void Bounds()
    {
        var bytes = Samples(out var index);
        Throws<InvalidDataException>(() => Mp4TtmlReader.Read(bytes[..^1]));
        var invalid = bytes.ToArray(); var run = Find(invalid, "trun"); BinaryPrimitives.WriteUInt32BigEndian(invalid.AsSpan(run + 12, 4), uint.MaxValue);
        Throws<InvalidDataException>(() => Mp4TtmlReader.Read(invalid));
        Throws<InvalidDataException>(() => Mp4TtmlReader.Read(Movie([Encoding.UTF8.GetBytes("<not-ttml/>")], out _)));
        var ranged = Movie([Xml("<p begin='0s' dur='1s'>Ranged</p>")], out index, absolute: true);
        var start = index + 44;
        Assert(Mp4TtmlReader.ReadSegments(ranged[..index], [new(ranged[start..], start)]).Contains("Ranged"), "Absolute ranged sample data offsets were lost.");
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Throws<OperationCanceledException>(() => Mp4TtmlReader.Read(bytes, ct: stop.Token));
    }
    private static void Profiles()
    {
        Samples(out var index);
        foreach (var codec in new[] { "stpp", "stpp.ttml", "stpp.ttml.im1t", "stpp.ttml.im2t" })
        {
            var parsed = DashManifestParser.Parse(DashCaptionChecks.Xml(index, codec), new("https://media.example/source.mpd"));
            Assert(parsed.CaptionTracks[0].UnavailableReason is null && parsed.CaptionTracks[0].Representation.SegmentBase is not null && parsed.CaptionTracks[1].UnavailableReason is not null,
                "DASH text-profile availability or image-profile refusal changed.");
        }
    }
    private static async Task Saving()
    {
        foreach (var mode in new[] { "indexed", "template", "ranges" })
        {
            using var files = new Files(); var bytes = Movie([Xml("<p begin='0s' end='2s'>Standalone TTML</p>")], out var index, absolute: mode == "ranges");
            var start = index + 44;
            var xml = mode == "indexed" ? DashCaptionChecks.Xml(index, "stpp") : Manifest(mode == "ranges" ?
                "<BaseURL>text.mp4</BaseURL><SegmentList><Initialization range='0-" + (index - 1) + "'/><SegmentURL mediaRange='" + start + "-" + (bytes.Length - 1) + "'/></SegmentList>" :
                "<SegmentTemplate duration='1' initialization='init.mp4' media='part-$Number$.m4s'/>", 1);
            var calls = new List<string>();
            var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request =>
            {
                var path = request.RequestUri!.AbsolutePath; calls.Add(path);
                return Task.FromResult(path switch {
                    "/source.mpd" => Response(Encoding.UTF8.GetBytes(xml)), "/text.mp4" => DashIndexChecks.Range(request, bytes),
                    "/init.mp4" => Response(bytes[..index]), "/part-1.m4s" => Response(bytes[start..]),
                    _ => throw new InvalidOperationException("Caption-only TTML transfer requested media.") });
            })));
            var result = await engine.DownloadAsync(Request(files.Directory), null, null, default);
            Assert(result.Succeeded && result.OutputPaths.Count == 0 && result.SidecarPaths.Count == 1 && File.ReadAllText(result.SidecarPaths[0]).Contains("00:00:00.000 --> 00:00:01.000\nStandalone TTML") && !calls.Contains("/video.mp4"),
                "TTML addressing or standalone output changed.");
        }
        using var preflightFiles = new Files(); var preflightCalls = 0;
        var protectedMovie = Movie([Xml("<p begin='0s' dur='1s'>Protected</p>")], out var protectedIndex, protectedTrack: true);
        var protectedEngine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request =>
        {
            preflightCalls++;
            return Task.FromResult(request.RequestUri!.AbsolutePath switch {
                "/source.mpd" => Response(Encoding.UTF8.GetBytes(Manifest("<SegmentTemplate duration='1' initialization='init.mp4' media='part-$Number$.m4s'/>", 1))),
                "/init.mp4" => Response(protectedMovie[..protectedIndex]),
                _ => throw new InvalidOperationException("Invalid caption initialization reached fragment transfer.") });
        })));
        await Expect<NotSupportedException>(() => protectedEngine.DownloadAsync(Request(preflightFiles.Directory), null, null, default));
        Assert(preflightCalls == 2, "The caption initialization was not validated before fetching media fragments.");
    }
    private static async Task Parallel()
    {
        using var files = new Files(); var active = 0; var peak = 0; var started = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fragments = Enumerable.Range(0, 3).Select(index => Movie([Xml("<p begin='" + index + "s' dur='1s'>Cue " + index + "</p>")], out _, (ulong)index * 1000)).ToArray();
        Movie([Xml("")], out var initLength);
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/source.mpd") return Response(Encoding.UTF8.GetBytes(Manifest("<SegmentTemplate duration='1' initialization='init.mp4' media='part-$Number$.m4s'/>", 3)));
            if (path == "/init.mp4") return Response(fragments[0][..initLength]);
            var number = int.Parse(Path.GetFileNameWithoutExtension(path).Split('-')[1]);
            var now = Interlocked.Increment(ref active); peak = Math.Max(peak, now);
            if (Interlocked.Increment(ref started) == 3) gate.TrySetResult();
            try { await gate.Task; await Task.Delay((4 - number) * 10); return Response(fragments[number - 1][(initLength + 44)..]); }
            finally { Interlocked.Decrement(ref active); }
        })));
        var result = await engine.DownloadAsync(Request(files.Directory) with { ConcurrentFragments = 3 }, null, null, default);
        var text = File.ReadAllText(result.SidecarPaths.Single());
        Assert(result.Succeeded && peak == 3 && active == 0 && text.IndexOf("Cue 0", StringComparison.Ordinal) < text.IndexOf("Cue 1", StringComparison.Ordinal) &&
            text.IndexOf("Cue 1", StringComparison.Ordinal) < text.IndexOf("Cue 2", StringComparison.Ordinal), "Parallel TTML transfer reordered samples or left workers running.");
    }
    private static async Task ParallelFailures()
    {
        foreach (var mode in new[] { "budget", "cancel", "failure" })
        {
            using var files = new Files(); using var stop = new CancellationTokenSource(); var active = 0; var started = 0; long received = 0;
            var target = Path.Combine(files.Directory, "result.en.vtt"); File.WriteAllText(target, "previous");
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var movie = Movie([Xml("")], out var index);
            var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(async (request, token) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path == "/source.mpd") return Response(Encoding.UTF8.GetBytes(Manifest("<SegmentTemplate duration='1' initialization='init.mp4' media='part-$Number$.m4s'/>", 3)));
                if (path == "/init.mp4") return Response(movie[..index]);
                Interlocked.Increment(ref active);
                if (Interlocked.Increment(ref started) == 3) gate.TrySetResult();
                try
                {
                    await gate.Task.WaitAsync(token);
                    if (mode == "cancel") { stop.Cancel(); await Task.Delay(1000, token); }
                    if (mode == "failure" && path.EndsWith("1.m4s")) return new(HttpStatusCode.Forbidden);
                    if (mode == "failure") await Task.Delay(1000, token);
                    return mode == "budget" ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new CountingStream(new byte[3 * 1024 * 1024], count => Interlocked.Add(ref received, count))) } : Response(movie[(index + 44)..]);
                }
                finally { Interlocked.Decrement(ref active); }
            })));
            await Expect<Exception>(() => engine.DownloadAsync(Request(files.Directory) with { ConcurrentFragments = 3 }, null, null, stop.Token));
            Assert(active == 0 && File.ReadAllText(target) == "previous", "A failed parallel TTML job changed published captions or left workers active.");
            Assert(received <= Mp4WebVttReader.MaximumInputBytes + 3 * 8192, "Parallel caption reads exceeded the shared byte budget plus in-flight chunks.");
        }
    }
    private static async Task Publication()
    {
        using var files = new Files(); File.WriteAllText(Path.Combine(files.Directory, "result.mp4"), "previous media");
        File.WriteAllText(Path.Combine(files.Directory, "result.en.vtt"), "previous captions");
        var bytes = Movie([Encoding.UTF8.GetBytes("<invalid>")], out var index);
        await Expect<InvalidDataException>(() => DashCaptionChecks.Engine(DashCaptionChecks.Xml(index, "stpp"), bytes).DownloadAsync(Request(files.Directory) with { SkipDownload = false }, null, null, default));
        Assert(File.ReadAllText(Path.Combine(files.Directory, "result.mp4")) == "previous media" && File.ReadAllText(Path.Combine(files.Directory, "result.en.vtt")) == "previous captions", "Invalid MP4 TTML replaced previously published output.");
    }
    private static string Manifest(string addressing, int duration) => "<MPD mediaPresentationDuration='PT" + duration + "S'><Period><AdaptationSet mimeType='video/mp4'><Representation id='v'><BaseURL>video.mp4</BaseURL></Representation></AdaptationSet><AdaptationSet lang='en' contentType='text'><Representation id='c' mimeType='application/mp4' codecs='stpp.ttml.im1t'>" + addressing + "</Representation></AdaptationSet></Period></MPD>";
    private static NativeDownloadRequest Request(string directory) => new() { Url = new("https://media.example/source.mpd"), OutputDirectory = directory, FileName = "result", WriteSubs = true, SubLanguages = "en", SkipDownload = true, Retries = 0 };
    private static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        internal Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : this((request, _) => respond(request)) { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request, ct);
    }
    private sealed class CountingStream(byte[] bytes, Action<int> received) : MemoryStream(bytes, writable: false)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { var count = await base.ReadAsync(buffer, ct); received(count); return count; }
    }
    private static int Find(byte[] bytes, string value) { var needle = Encoding.UTF8.GetBytes(value); for (var index = 0; index <= bytes.Length - needle.Length; index++) if (bytes.AsSpan(index, needle.Length).SequenceEqual(needle)) return index; throw new InvalidOperationException("Fixture field missing."); }
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, "stpp-" + Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() { var path = Path.GetFullPath(Directory); if (!path.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Test folder escaped its root."); System.IO.Directory.Delete(path, true); }
    }
}
