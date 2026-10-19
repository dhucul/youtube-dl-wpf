using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;

internal static class DashCaptionChecks
{
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("MP4 WebVTT preserves cue identifiers, settings, text and contiguous cue timing", () => Sync(Cues)),
        ("MP4 WebVTT handles 64-bit decode times, signed composition offsets and absolute data offsets", () => Sync(Times)),
        ("Invalid MP4 caption offsets, defaults, protection and sample data fail explicitly", () => Sync(Invalid)),
        ("DASH caption metadata retains languages and unsupported codec boundaries", () => Sync(Manifest)),
        ("Native DASH downloads save selected indexed WebVTT without another subtitle request", Save),
        ("Unselected and automatic-only DASH caption requests do not fetch authored tracks", Unselected),
        ("Corrupt DASH captions preserve the prior media and subtitle files", Failure),
        ("Plain DASH WebVTT uses the existing native caption writer", Plain),
        ("DASH WebVTT templates and ranged lists preserve fragment-relative and absolute offsets", Segments),
        ("Canceled MP4 caption conversion does not emit a document", () => Sync(Cancellation))
    ];

    internal static byte[] Sample(string text, string id = "cue", string settings = "align:start") => Box("vttc", Box("iden", Encoding.UTF8.GetBytes(id)), Box("sttg", Encoding.UTF8.GetBytes(settings)), Box("payl", Encoding.UTF8.GetBytes(text)));
    internal static byte[] Movie(byte[][] samples, out int indexOffset, ulong decodeTime = 0, bool absolute = false, int composition = 0, bool protectedTrack = false, bool ttml = false, string auxiliary = "", string ttmlNamespaces = "http://www.w3.org/ns/ttml")
    {
        var header = Join(U32(0), U32(0), U32(0), U32(1), new byte[64]);
        var mediaHeader = Join(U32(0), U32(0), U32(0), U32(1000), U32(0), new byte[4]);
        var entry = ttml ? Box("stpp", new byte[6], new byte[] { 0, 1 }, Encoding.UTF8.GetBytes(ttmlNamespaces + "\0\0" + auxiliary + "\0"), protectedTrack ? Box("sinf", []) : []) :
            Box("wvtt", new byte[6], new byte[] { 0, 1 }, Box("vttC", Encoding.UTF8.GetBytes("WEBVTT\n")), protectedTrack ? Box("sinf", []) : []);
        var stsd = Box("stsd", U32(0), U32(1), entry);
        var track = Box("trak", Box("tkhd", header), Box("mdia", Box("mdhd", mediaHeader), Box("minf", Box("stbl", stsd))));
        var moov = Box("moov", track, Box("mvex", Box("trex", U32(0), U32(1), U32(1), U32(1000), U32(0), U32(0))));
        indexOffset = moov.Length;
        var placeholder = Fragment(0, 0);
        var fragmentOffset = moov.Length + 44;
        var fragment = Fragment(placeholder.Length + 8, (ulong)(fragmentOffset + placeholder.Length + 8));
        var payload = Join(samples);
        var index = Box("sidx", U32(0), U32(1), U32(1000), U32(0), U32(0), U32(1), U32((uint)(fragment.Length + payload.Length + 8)), U32((uint)samples.Length * 1000), U32(0x90000000));
        return Join(moov, index, fragment, Box("mdat", payload));

        byte[] Fragment(int relativeOffset, ulong absoluteOffset)
        {
            var flags = absolute ? 1U : 0x20000U;
            var tfhd = Box("tfhd", U32(flags), U32(1), absolute ? U64(absoluteOffset) : []);
            var tfdt = Box("tfdt", U32(0x01000000), U64(decodeTime));
            var run = new List<byte[]> { U32(0x01000300U | (absolute ? 0U : 1U) | (composition != 0 ? 0x800U : 0U)), U32((uint)samples.Length) };
            if (!absolute) run.Add(U32((uint)relativeOffset));
            foreach (var sample in samples) { run.Add(U32(1000)); run.Add(U32((uint)sample.Length)); if (composition != 0) run.Add(U32(unchecked((uint)composition))); }
            return Box("moof", Box("traf", tfhd, tfdt, Box("trun", Join(run.ToArray()))));
        }
    }

    private static void Cues()
    {
        var sample = Sample("First line\nSecond line");
        var bytes = Movie([sample, sample, Box("vtte", []), sample], out _);
        var text = Mp4WebVttReader.Read(bytes, 3.5);
        Assert(text.Contains("cue\n00:00:00.000 --> 00:00:02.000 align:start\nFirst line\nSecond line") && text.Contains("00:00:03.000 --> 00:00:03.500"), "Cue text, settings, merge or clipping changed.");
        Assert(text.Split(" --> ").Length == 3, "Empty samples emitted cues or a continuing cue split unnecessarily.");
        var both = Join(Sample("one", "a"), Sample("two", "b"));
        var overlapping = Mp4WebVttReader.Read(Movie([both, both], out _));
        Assert(overlapping.Split("00:00:00.000 --> 00:00:02.000").Length == 3, "Concurrent cues were lost or merged together.");
    }
    private static void Times()
    {
        var start = 1UL << 33;
        var text = Mp4WebVttReader.Read(Movie([Sample("time")], out _, start, absolute: true, composition: -250), 1, (double)start / 1000);
        Assert(text.Contains("00:00:00.000 --> 00:00:00.750"), "Signed sample composition or 64-bit decode timing changed.");
    }
    private static void Invalid()
    {
        var valid = Movie([Sample("text")], out _);
        Throws<InvalidDataException>(() => Mp4WebVttReader.Read(valid[..^1]));
        Throws<NotSupportedException>(() => Mp4WebVttReader.Read(Movie([Sample("text")], out _, protectedTrack: true)));
        var offset = valid.ToArray(); var trun = Find(offset, "trun"); BinaryPrimitives.WriteUInt32BigEndian(offset.AsSpan(trun + 12), uint.MaxValue);
        Throws<InvalidDataException>(() => Mp4WebVttReader.Read(offset));
        var duration = valid.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(duration.AsSpan(trun + 16), 0);
        Throws<InvalidDataException>(() => Mp4WebVttReader.Read(duration));
        Throws<InvalidDataException>(() => Mp4WebVttReader.Read(Movie([Sample("text", "bad\nid")], out _)));
        Throws<InvalidDataException>(() => Mp4WebVttReader.Read(Movie([Sample("text", "bad --> id")], out _)));
        Throws<NotSupportedException>(() => Mp4WebVttReader.Read(Movie([Sample("first\n\nsecond")], out _)));
    }

    internal static string Xml(int offset, string codec = "wvtt") => "<MPD mediaPresentationDuration='PT3.5S'><Period><AdaptationSet mimeType='video/mp4'><Representation id='v' height='360'><BaseURL>video.mp4</BaseURL></Representation></AdaptationSet>" +
        "<AdaptationSet contentType='text' lang='en'><Representation id='en' mimeType='application/mp4' codecs='" + codec + "'><BaseURL>text.mp4</BaseURL><SegmentBase indexRange='" + offset + "-" + (offset + 43) + "' timescale='1000'><Initialization range='0-" + (offset - 1) + "'/></SegmentBase></Representation></AdaptationSet>" +
        "<AdaptationSet contentType='text' lang='fr'><Representation id='fr' mimeType='application/mp4' codecs='stpp.ttml.im1i'><BaseURL>unsupported.mp4</BaseURL></Representation></AdaptationSet></Period></MPD>";
    private static void Manifest()
    {
        var bytes = Movie([Sample("text")], out var offset);
        var manifest = DashManifestParser.Parse(Xml(offset), new Uri("https://media.example/source.mpd"));
        Assert(manifest.CaptionTracks.Count == 2 && manifest.CaptionTracks[0].Language == "en" && manifest.CaptionTracks[0].Representation.SegmentBase is not null &&
            manifest.CaptionTracks[1].UnavailableReason is not null && manifest.Representations.Count == 1, "Caption languages or codec availability were lost.");
    }
    private static async Task Save()
    {
        using var files = new Files(); var samples = new[] { Sample("hello"), Sample("hello"), Box("vtte", []), Sample("last") };
        var bytes = Movie(samples, out var offset); var textRequests = 0;
        var result = await Engine(Xml(offset), bytes, request => { if (request.RequestUri!.AbsolutePath == "/text.mp4") textRequests++; })
            .DownloadAsync(Request(files) with { WriteSubs = true, SubLanguages = "en" }, null, null, default);
        var caption = await File.ReadAllTextAsync(Path.Combine(files.Directory, "result.en.vtt"));
        Assert(result.Succeeded && textRequests == 3 && caption.Contains("00:00:03.000 --> 00:00:03.500") && !Directory.EnumerateFiles(files.Directory, "*.fr.vtt").Any(), "Indexed caption selection or saving failed.");
    }
    private static async Task Unselected()
    {
        using var files = new Files(); var bytes = Movie([Sample("hello")], out var offset);
        foreach (var request in new[] { Request(files), Request(files) with { WriteAutoSubs = true }, Request(files) with { WriteSubs = true, SubLanguages = "de" } })
            Assert((await Engine(Xml(offset), bytes, message => { if (message.RequestUri!.AbsolutePath.Contains("text") || message.RequestUri.AbsolutePath.Contains("unsupported")) throw new InvalidOperationException("Unselected captions were fetched."); }).DownloadAsync(request, null, null, default)).Succeeded, "Unselected captions blocked media.");
        await Expect<NotSupportedException>(() => Engine(Xml(offset), bytes, message => { if (message.RequestUri!.AbsolutePath != "/source.mpd") throw new InvalidOperationException("Unsupported caption selection reached transfer."); })
            .DownloadAsync(Request(files) with { WriteSubs = true, SubLanguages = "fr" }, null, null, default));
    }
    private static async Task Failure()
    {
        using var files = new Files(); await File.WriteAllTextAsync(Path.Combine(files.Directory, "result.mp4"), "previous media"); await File.WriteAllTextAsync(Path.Combine(files.Directory, "result.en.vtt"), "previous captions");
        var bytes = Movie([Sample("invalid\n\npayload")], out var offset);
        await Expect<NotSupportedException>(() => Engine(Xml(offset), bytes).DownloadAsync(Request(files) with { WriteSubs = true, SubLanguages = "en" }, null, null, default));
        Assert(File.ReadAllText(Path.Combine(files.Directory, "result.mp4")) == "previous media" && File.ReadAllText(Path.Combine(files.Directory, "result.en.vtt")) == "previous captions", "Invalid captions replaced prior output.");
    }
    private static async Task Plain()
    {
        using var files = new Files();
        var xml = "<MPD><Period><AdaptationSet mimeType='video/mp4'><Representation id='v'><BaseURL>video.mp4</BaseURL></Representation></AdaptationSet><AdaptationSet lang='en' mimeType='text/vtt'><Representation id='en'><BaseURL>plain.vtt</BaseURL></Representation></AdaptationSet></Period></MPD>";
        var result = await Engine(xml, []).DownloadAsync(Request(files) with { WriteSubs = true }, null, null, default);
        Assert(result.Succeeded && File.ReadAllText(Path.Combine(files.Directory, "result.en.vtt")).Contains("plain text"), "Plain DASH captions were not saved.");
    }
    private static void Cancellation()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Throws<OperationCanceledException>(() => Mp4WebVttReader.Read(Movie([Sample("text")], out _), ct: cancel.Token));
    }

    private static async Task Segments()
    {
        foreach (var ranged in new[] { false, true })
        {
            using var files = new Files();
            var bytes = Movie([Sample("segmented"), Sample("segmented"), Box("vtte", []), Sample("final")], out var offset, absolute: ranged);
            var fragmentStart = offset + 44;
            var addressing = ranged
                ? "<BaseURL>text.mp4</BaseURL><SegmentList><Initialization range='0-" + (offset - 1) + "'/><SegmentURL mediaRange='" + fragmentStart + "-" + (bytes.Length - 1) + "'/></SegmentList>"
                : "<SegmentTemplate timescale='1' duration='4' initialization='init.mp4' media='caption-$Number$.m4s'/>";
            var xml = "<MPD mediaPresentationDuration='PT3.5S'><Period><AdaptationSet mimeType='video/mp4'><Representation id='v'><BaseURL>video.mp4</BaseURL></Representation></AdaptationSet><AdaptationSet lang='en' contentType='text'><Representation id='c' mimeType='application/mp4' codecs='wvtt'>" + addressing + "</Representation></AdaptationSet></Period></MPD>";
            var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path == "/source.mpd") return new(HttpStatusCode.OK) { Content = new StringContent(xml) };
                if (path == "/text.mp4") return DashIndexChecks.Range(request, bytes);
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(path switch { "/init.mp4" => bytes[..offset], "/caption-1.m4s" => bytes[fragmentStart..], "/video.mp4" => Encoding.ASCII.GetBytes("video"), _ => throw new InvalidOperationException("Unknown caption resource.") }) };
            })));
            var result = await engine.DownloadAsync(Request(files) with { WriteSubs = true }, null, null, default);
            var text = File.ReadAllText(Path.Combine(files.Directory, "result.en.vtt"));
            Assert(result.Succeeded && text.Contains("00:00:00.000 --> 00:00:02.000") && text.Contains("00:00:03.000 --> 00:00:03.500"), "A segmented caption lost its media offsets or timing.");
        }
    }

    internal static NativeEngine Engine(string xml, byte[] bytes, Action<HttpRequestMessage>? observe = null, byte[]? video = null) => new(httpFactory: _ => new MediaHttp(new Handler(request =>
    {
        observe?.Invoke(request); var path = request.RequestUri!.AbsolutePath;
        if (path == "/source.mpd") return new(HttpStatusCode.OK) { Content = new StringContent(xml) };
        if (path == "/text.mp4") return DashIndexChecks.Range(request, bytes);
        if (path == "/plain.vtt") return new(HttpStatusCode.OK) { Content = new StringContent("WEBVTT\n\n00:00.000 --> 00:01.000\nplain text\n") };
        if (path == "/video.mp4") return new(HttpStatusCode.OK) { Content = new ByteArrayContent(video ?? Encoding.ASCII.GetBytes("video bytes")) };
        throw new InvalidOperationException("Unexpected caption or media resource.");
    })));
    private static NativeDownloadRequest Request(Files files) => new() { Url = new Uri("https://media.example/source.mpd"), OutputDirectory = files.Directory, FileName = "result", Retries = 0 };
    private static int Find(byte[] bytes, string type) { var needle = Encoding.ASCII.GetBytes(type); for (var i = 0; i <= bytes.Length - 4; i++) if (bytes.AsSpan(i, 4).SequenceEqual(needle)) return i; throw new InvalidOperationException("Fixture box missing."); }
    private static byte[] U32(uint value) { var bytes = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, value); return bytes; }
    private static byte[] U64(ulong value) { var bytes = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes, value); return bytes; }
    private static byte[] Join(params byte[][] parts) => parts.SelectMany(part => part).ToArray();
    private static byte[] Box(string type, params byte[][] parts) { var payload = Join(parts); return Join(U32((uint)payload.Length + 8), Encoding.ASCII.GetBytes(type), payload); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private sealed class Files : IDisposable
    { public string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", "dash-captions-" + Guid.NewGuid().ToString("N")); public Files() => System.IO.Directory.CreateDirectory(Directory); public void Dispose() => System.IO.Directory.Delete(Directory, true); }
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
