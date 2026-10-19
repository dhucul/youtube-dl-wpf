using System.Net;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class NoPartChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("No-part transfer writes its named file while retaining complete bytes and dates", Direct),
        ("No-part validated receipts resume only the matching remote entity", Resume),
        ("No-part retries honor continue and explicit restart without duplicate bytes", Retry),
        ("No-part failed direct replacement restores prior media receipt and timestamp", Rollback),
        ("No-part cancellation protects previous files and unstarted paths", Cancellation),
        ("No-part declared and streaming size failures protect prior files", Limits),
        ("No-part mode preserves separately recoverable legacy part data", Modes),
        ("No-part native partial retries cannot publish partial media or archive success", Native),
        ("No-part HLS and segmented DASH retries avoid part files and retain order", Adaptive),
        ("No-part argument overrides retain explicit saved and job precedence", Arguments)
    ];
    private static readonly Uri Url = new("https://no-part.example/source.mp4");
    private static readonly DateTimeOffset Modified = new(2021, 5, 6, 7, 8, 9, TimeSpan.Zero);
    private static async Task Direct()
    {
        using var files = new Files(); using var http = Http(_ => Response("abcdef")); var updates = 0;
        await new FileDownloader(http) { NoPart = true }.DownloadAsync(Url, files.Output, null, new Progress(value =>
        { updates++; Assert(File.Exists(files.Output) && !Directory.EnumerateFiles(files.Directory, "*.part", SearchOption.AllDirectories).Any(), "The direct transfer used a part file."); }), default, 0);
        Assert(updates > 0 && File.ReadAllText(files.Output) == "abcdef" && File.GetLastWriteTimeUtc(files.Output) == Modified.UtcDateTime && !File.Exists(Receipt(files.Output)), "Direct completion changed bytes, date or retained a receipt.");
    }
    private static async Task Resume()
    {
        using var files = new Files(); using var first = Http(_ => Response("abc", 6));
        await Expect<IOException>(() => new FileDownloader(first) { NoPart = true }.DownloadAsync(Url, files.Output, null, null, default, 0));
        Assert(File.ReadAllText(files.Output) == "abc" && File.Exists(Receipt(files.Output)) && !File.Exists(files.Output + ".part"), "Incomplete direct bytes were not recoverable.");
        using var next = Http(request => { Assert(request.Headers.Range?.Ranges.Single().From == 3 && request.Headers.IfRange?.ToString() == "\"entity\"", "Direct resume omitted its bound offset/validator."); return Partial("def"); });
        await new FileDownloader(next) { NoPart = true }.DownloadAsync(Url, files.Output, null, null, default, 0);
        Assert(File.ReadAllText(files.Output) == "abcdef" && !File.Exists(Receipt(files.Output)), "Direct resume lost or duplicated bytes.");
    }
    private static async Task Retry()
    {
        foreach (var restart in new[] { false, true })
        {
            using var files = new Files(); var calls = 0; using var http = Http(request =>
            { if (++calls == 1) return Response("abc", 6); Assert((request.Headers.Range is null) == restart, "Direct retry ignored restart policy."); return restart ? Response("abcdef") : Partial("def"); });
            await new FileDownloader(http) { NoPart = true, NoContinue = restart }.DownloadAsync(Url, files.Output, null, null, default, 1);
            Assert(calls == 2 && File.ReadAllText(files.Output) == "abcdef", "Direct retry changed bytes.");
        }
    }
    private static async Task Rollback()
    {
        using var files = new Files(); await File.WriteAllTextAsync(files.Output, "previous complete"); File.SetLastWriteTimeUtc(files.Output, Modified.AddDays(-1).UtcDateTime);
        await File.WriteAllTextAsync(Receipt(files.Output), "previous receipt"); using var http = Http(_ => Response("new", 6));
        await Expect<IOException>(() => new FileDownloader(http) { NoPart = true }.DownloadAsync(Url, files.Output, null, null, default, 0));
        Assert(File.ReadAllText(files.Output) == "previous complete" && File.ReadAllText(Receipt(files.Output)) == "previous receipt" && File.GetLastWriteTimeUtc(files.Output) == Modified.AddDays(-1).UtcDateTime &&
            !Directory.EnumerateFiles(files.Directory, ".native-transfer-backup-*").Any(), "Failed direct replacement lost previous data/date or leaked recovery files.");
    }
    private static async Task Cancellation()
    {
        using var files = new Files(); await File.WriteAllTextAsync(files.Output, "previous complete"); using var stop = new CancellationTokenSource();
        using var http = Http(_ => Response(new string('x', 70000))); await Expect<OperationCanceledException>(() => new FileDownloader(http) { NoPart = true }.DownloadAsync(Url, files.Output, null,
            new Progress(_ => stop.Cancel()), stop.Token, 0));
        Assert(File.ReadAllText(files.Output) == "previous complete" && !File.Exists(Receipt(files.Output)), "Direct cancellation replaced prior media.");
        var missing = Path.Combine(files.Directory, "unstarted", "media.mp4"); using var canceled = Http(_ => throw new InvalidOperationException("Pre-canceled direct transfer reached HTTP."));
        await Expect<OperationCanceledException>(() => new FileDownloader(canceled) { NoPart = true }.DownloadAsync(Url, missing, null, null, stop.Token, 0));
        Assert(!Directory.Exists(Path.GetDirectoryName(missing)), "Pre-canceled direct transfer created output state.");
    }
    private static async Task Limits()
    {
        foreach (var mode in new[] { "declared", "stream-max", "stream-min" })
        {
            using var files = new Files(); await File.WriteAllTextAsync(files.Output, "previous complete"); using var http = Http(_ => mode == "declared" ? Response("abcdef") :
                new(HttpStatusCode.OK) { Content = new UnknownContent(Encoding.UTF8.GetBytes("abcdef")) });
            await Expect<NativeFileSizeException>(() => new FileDownloader(http) { NoPart = true, MaximumBytes = mode == "stream-min" ? null : 3, MinimumBytes = mode == "stream-min" ? 9 : null }.DownloadAsync(Url, files.Output, null, null, default, 0));
            Assert(File.ReadAllText(files.Output) == "previous complete" && !File.Exists(Receipt(files.Output)), "A direct size failure changed previous data.");
        }
    }
    private static async Task Modes()
    {
        using var files = new Files(); await File.WriteAllTextAsync(files.Output + ".part", "recoverable old part"); await File.WriteAllTextAsync(files.Output + ".part.json", "recoverable old receipt");
        using var http = Http(_ => Response("abcdef")); await new FileDownloader(http) { NoPart = true }.DownloadAsync(Url, files.Output, null, null, default, 0);
        Assert(File.ReadAllText(files.Output + ".part") == "recoverable old part" && File.ReadAllText(files.Output + ".part.json") == "recoverable old receipt", "Direct mode cleared another mode's recoverable data.");
    }
    private static async Task Native()
    {
        using var files = new Files(); var count = 0; var callbacks = 0; var archive = Path.Combine(files.Directory, "archive.txt"); await File.WriteAllTextAsync(files.Output, "previous complete"); await File.WriteAllTextAsync(archive, "other kept\n");
        var engine = Engine(MediaKind.Progressive, request => { if (++count == 1) return Response("abc", 6); Assert(request.Headers.Range?.Ranges.Single().From == 3, "Native direct retry did not resume."); return Partial("def"); });
        var request = Request(files.Directory) with { DownloadArchiveFile = archive };
        await Expect<IOException>(() => engine.DownloadAsync(request, null, null, default, _ => callbacks++));
        Assert(File.ReadAllText(files.Output) == "previous complete" && File.ReadAllText(archive) == "other kept\n" && callbacks == 0 && !Directory.EnumerateFiles(files.Directory, "*.part", SearchOption.AllDirectories).Any(), "Native direct failure published incomplete media or archive success.");
        var result = await engine.DownloadAsync(request, null, null, default, _ => callbacks++); var repeat = await engine.DownloadAsync(request, null, null, default, _ => callbacks++);
        Assert(result.Succeeded && repeat.Skipped && callbacks == 1 && count == 2 && File.ReadAllText(files.Output) == "abcdef", "Native direct completion/archive behavior changed.");
    }
    private static async Task Adaptive()
    {
        foreach (var dash in new[] { false, true })
        {
            using var files = new Files(); var fragments = 0; var observed = 0;
            var engine = Engine(dash ? MediaKind.Dash : MediaKind.Hls, request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith(".mpd")) return Response("<MPD mediaPresentationDuration='PT1S'><Period><AdaptationSet mimeType='video/mp4'><Representation id='v'><SegmentList duration='1'><Initialization sourceURL='init.mp4'/><SegmentURL media='fragment.m4s'/></SegmentList></Representation></AdaptationSet></Period></MPD>");
                if (path.EndsWith(".m3u8")) return Response("#EXTM3U\n#EXTINF:1,\nfragment.ts\n#EXT-X-ENDLIST\n");
                Assert(!Directory.EnumerateFiles(files.Directory, "*.part", SearchOption.AllDirectories).Any(), "Adaptive direct mode created part files."); observed++;
                if (path.EndsWith("init.mp4")) return Response("INIT");
                if (++fragments == 1) return Response("abc", 6);
                Assert(request.Headers.Range?.Ranges.Single().From == 3, "Adaptive direct retry did not resume its fragment."); return Partial("def");
            });
            var result = await engine.DownloadAsync(Request(files.Directory) with { Url = new("https://no-part.example/manifest." + (dash ? "mpd" : "m3u8")), FragmentRetries = 1 }, new Progress(_ =>
                Assert(!Directory.EnumerateFiles(files.Directory, "*.part", SearchOption.AllDirectories).Any(), "Adaptive assembly ignored direct mode.")), null, default);
            Assert(result.Succeeded && observed >= 2 && File.ReadAllText(result.OutputPath!) == (dash ? "INITabcdef" : "abcdef"), "Adaptive direct mode lost assembled bytes.");
        }
    }
    private static Task Arguments()
    {
        var request = NativeExtraArguments.Apply(Request(Path.GetTempPath()), ["--part", "--no-part", "--no-continue"]);
        Assert(request.NoPart && request.NoContinue, "Direct argument settings were dropped."); request = NativeExtraArguments.Apply(request, ["--part", "--continue"]);
        Assert(!request.NoPart && !request.NoContinue, "Explicit job arguments lost precedence."); return Task.CompletedTask;
    }
    private static string Receipt(string path) => path + ".native-resume.json";
    private static NativeDownloadRequest Request(string directory) => new() { Url = Url, OutputDirectory = directory, FileName = "result", NoPart = true, Retries = 0, VideoOnly = true };
    private static NativeEngine Engine(MediaKind kind, Func<HttpRequestMessage, HttpResponseMessage> run) => new([new Provider(kind)], _ => Http(run));
    private static MediaHttp Http(Func<HttpRequestMessage, HttpResponseMessage> run) => new(new Handler(run));
    private static HttpResponseMessage Response(string value, long? length = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(value)) };
        response.Headers.ETag = new("\"entity\""); response.Content.Headers.LastModified = Modified; if (length is not null) response.Content.Headers.ContentLength = length; return response;
    }
    private static HttpResponseMessage Partial(string value) { var response = Response(value); response.StatusCode = HttpStatusCode.PartialContent; response.Content.Headers.ContentRange = new(3, 5, 6); return response; }
    private sealed class UnknownContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes));
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
    private sealed class Provider(MediaKind kind) : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("no-part-fixture", "Direct transfer fixture", 1000);
        public bool CanHandle(Uri url) => true;
        public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => new("no-part-fixture", "fixture");
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(new PageMetadata
        { Id = "fixture", Extractor = "no-part-fixture", Title = "Fixture", Media = [new() { Url = request.Url, Kind = kind, Extension = "mp4", HasVideo = true, HasAudio = false }] });
    }
    private sealed class Progress(Action<DownloadProgress> report) : IProgress<DownloadProgress> { public void Report(DownloadProgress value) => report(value); }
    private sealed class Files : IDisposable
    {
        internal string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", "no-part-" + Guid.NewGuid().ToString("N"));
        internal string Output => Path.Combine(Directory, "result.mp4");
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
