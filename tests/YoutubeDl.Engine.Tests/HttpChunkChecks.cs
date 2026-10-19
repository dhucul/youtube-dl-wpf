using System.Net;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class HttpChunkChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("HTTP chunks parse binary sizes disabled zero and bounded integer ranges", Sizes),
        ("HTTP chunks request bounded contiguous ranges with full progress bytes and dates", Bounded),
        ("HTTP chunks keep supplied URL queries and strong validators exact", Identity),
        ("HTTP chunks use only sufficiently old date validators without entity tags", Dates),
        ("HTTP chunks resume incomplete middle responses from validated actual bytes", Resume),
        ("HTTP chunks restart retries and direct transfer modes without duplicate bytes", Restart),
        ("HTTP chunks discard changed identities before acquiring one complete representation", Changed),
        ("HTTP chunks accept complete server responses when ranges are ignored", Ignored),
        ("HTTP chunks invalid ranges cannot change prior publication after access denial", Invalid),
        ("HTTP chunks complete resource size bounds reject before partial writes", Limits),
        ("HTTP chunks cancellation restores previous direct files and receipts", Cancel),
        ("HTTP chunks native incomplete sources cannot publish media archive or callbacks", Native),
        ("HTTP chunks HLS and whole DASH propagate bounded transfer requests", Adaptive),
        ("HTTP chunks argument overrides can explicitly disable saved chunking", Arguments)
    ];
    private static readonly Uri Url = new("https://chunks.example/source.mp4?Signature=literal%2Bquery&Expires=4102444800");
    private static readonly byte[] Bytes = Encoding.ASCII.GetBytes("abcdefghij");
    private static readonly DateTimeOffset Modified = new(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static Task Sizes()
    {
        Assert(NativeHttpChunkSize.Parse(null) is null && NativeHttpChunkSize.Parse("0") is null && NativeHttpChunkSize.Parse("1.5K") == 1536 && NativeHttpChunkSize.Parse("3G") == 3L * 1024 * 1024 * 1024, "Chunk sizes changed units or disabled semantics.");
        foreach (var value in new[] { "-1", "NaN", "9999999999999999999999Y" }) ExpectSync<NativeSelectionException>(() => NativeHttpChunkSize.Parse(value)); return Task.CompletedTask;
    }
    private static async Task Bounded()
    {
        using var files = new Files(); var ranges = new List<(long?, long?)>(); var progress = new List<DownloadProgress>();
        using var http = Http(request => { var range = request.Headers.Range!.Ranges.Single(); ranges.Add((range.From, range.To)); return Range(request); });
        await new FileDownloader(http) { HttpChunkSize = "4", MinimumBytes = 10, MaximumBytes = 10 }.DownloadAsync(Url, files.Output, null, new Progress(progress.Add), default, 0);
        Assert(ranges.SequenceEqual(new[] { ((long?)0, (long?)3), ((long?)4, (long?)7), ((long?)8, (long?)9) }) && File.ReadAllBytes(files.Output).SequenceEqual(Bytes) &&
            progress.All(value => value.TotalBytes == 10) && progress[^1].DownloadedBytes == 10 && File.GetLastWriteTimeUtc(files.Output) == Modified.UtcDateTime, "Chunk boundaries, complete bytes, progress or dates changed.");
    }
    private static async Task Identity()
    {
        using var files = new Files(); var calls = 0; using var http = Http(request =>
        { Assert(request.RequestUri == Url, "Chunk signing queries were changed."); Assert(++calls == 1 ? request.Headers.IfRange is null : request.Headers.IfRange?.EntityTag?.ToString() == "\"v1\"", "Strong chunk identity was omitted or changed."); return Range(request); });
        await new FileDownloader(http) { HttpChunkSize = "4" }.DownloadAsync(Url, files.Output, null, null, default, 0); Assert(calls == 3, "Unexpected chunk inventory.");
    }
    private static async Task Dates()
    {
        foreach (var kind in new[] { "strong-date", "recent-date", "weak-tag" })
        {
            using var files = new Files(); var calls = 0; var restarted = false;
            using var http = Http(request =>
            {
                calls++; if (request.Headers.Range is null) { restarted = true; return Full(); }
                if (calls > 1) Assert(request.Headers.IfRange?.Date == Modified, "Date-only chunks omitted their strong validator.");
                var response = Range(request); response.Headers.ETag = kind == "weak-tag" ? new("\"v1\"", isWeak: true) : null;
                response.Headers.Date = Modified.AddSeconds(kind == "recent-date" ? 10 : 120); return response;
            });
            await new FileDownloader(http) { HttpChunkSize = "4" }.DownloadAsync(Url, files.Output, null, null, default, 0);
            Assert(restarted == (kind != "strong-date") && File.ReadAllBytes(files.Output).SequenceEqual(Bytes), "Unreliable dates/weak entity tags were used to stitch media.");
        }
    }
    private static async Task Resume()
    {
        using var files = new Files(); var calls = 0; using var first = Http(request => { var response = Range(request); if (++calls == 2) response.Content = Content(Bytes[4..7], 4, 7, 10, declared: 4); return response; });
        await Expect<IOException>(() => new FileDownloader(first) { HttpChunkSize = "4" }.DownloadAsync(Url, files.Output, null, null, default, 0));
        Assert(File.ReadAllText(files.Output + ".part") == "abcdefg" && !File.Exists(files.Output), "Incomplete chunk bytes were published or lost.");
        using var retry = Http(request => { Assert(request.Headers.Range?.Ranges.Single().From == 7, "Chunk resume skipped or repeated partial bytes."); return Range(request); });
        await new FileDownloader(retry) { HttpChunkSize = "4" }.DownloadAsync(Url, files.Output, null, null, default, 0);
        Assert(File.ReadAllBytes(files.Output).SequenceEqual(Bytes), "Chunk resume changed complete media.");
    }
    private static async Task Restart()
    {
        foreach (var direct in new[] { false, true })
        {
            using var files = new Files(); var calls = 0; var starts = new List<long>(); using var http = Http(request =>
            { var start = request.Headers.Range!.Ranges.Single().From!.Value; starts.Add(start); var response = Range(request); if (++calls == 2) response.Content = Content(Bytes[4..7], 4, 7, 10, declared: 4); return response; });
            await new FileDownloader(http) { HttpChunkSize = "4", NoContinue = true, NoPart = direct }.DownloadAsync(Url, files.Output, null, null, default, 1);
            Assert(starts.SequenceEqual(new long[] { 0, 4, 0, 4, 8 }) && File.ReadAllBytes(files.Output).SequenceEqual(Bytes), "Chunk retries ignored fresh-start/direct policies.");
        }
    }
    private static async Task Changed()
    {
        foreach (var changed in new[] { "tag", "size" })
        {
            using var files = new Files(); var calls = 0; using var http = Http(request =>
            {
                if (++calls == 3) { Assert(request.Headers.Range is null, "Changed chunks were not discarded before a complete restart."); return Full(); }
                var response = Range(request); if (calls == 2) { if (changed == "tag") response.Headers.ETag = new("\"other\""); else response.Content.Headers.ContentRange = new(4, 7, 8); } return response;
            });
            await new FileDownloader(http) { HttpChunkSize = "4" }.DownloadAsync(Url, files.Output, null, null, default, 0);
            Assert(calls == 3 && File.ReadAllBytes(files.Output).SequenceEqual(Bytes), "Changed representations were combined.");
        }
    }
    private static async Task Ignored()
    {
        using var files = new Files(); var calls = 0; using var http = Http(request => { calls++; Assert(request.Headers.Range?.Ranges.Single().To == 3, "Chunk bound was not requested."); return Full(); });
        await new FileDownloader(http) { HttpChunkSize = "4" }.DownloadAsync(Url, files.Output, null, null, default, 0);
        Assert(calls == 1 && File.ReadAllBytes(files.Output).SequenceEqual(Bytes), "Complete range-ignoring responses were mistaken for a partial chunk.");
    }
    private static async Task Invalid()
    {
        using var files = new Files(); await File.WriteAllTextAsync(files.Output, "previous complete"); var calls = 0;
        using var http = Http(request => { if (++calls == 2) { Assert(request.Headers.Range is null, "Invalid ranges did not request a full restart."); return new(HttpStatusCode.Forbidden); } var response = Range(request); response.Content.Headers.ContentRange = new(1, 4, 10); return response; });
        await Expect<IOException>(() => new FileDownloader(http) { HttpChunkSize = "4", NoPart = true }.DownloadAsync(Url, files.Output, null, null, default, 0));
        Assert(File.ReadAllText(files.Output) == "previous complete" && !File.Exists(files.Output + ".native-resume.json"), "Invalid range/access failure changed prior media.");
    }
    private static async Task Limits()
    {
        using var files = new Files(); await File.WriteAllTextAsync(files.Output + ".part", "preserved part"); await File.WriteAllTextAsync(files.Output + ".part.json", "preserved receipt"); using var http = Http(Range);
        await Expect<NativeFileSizeException>(() => new FileDownloader(http) { HttpChunkSize = "4", MaximumBytes = 9 }.DownloadAsync(Url, files.Output, null, null, default, 0));
        Assert(File.ReadAllText(files.Output + ".part") == "preserved part" && File.ReadAllText(files.Output + ".part.json") == "preserved receipt", "Whole-resource size refusal truncated partial data.");
    }
    private static async Task Cancel()
    {
        using var files = new Files(); await File.WriteAllTextAsync(files.Output, "previous complete"); using var stop = new CancellationTokenSource(); using var http = Http(Range);
        await Expect<OperationCanceledException>(() => new FileDownloader(http) { HttpChunkSize = "4", NoPart = true }.DownloadAsync(Url, files.Output, null, new Progress(_ => stop.Cancel()), stop.Token, 0));
        Assert(File.ReadAllText(files.Output) == "previous complete" && !File.Exists(files.Output + ".native-resume.json"), "Chunk cancellation replaced prior direct media.");
    }
    private static async Task Native()
    {
        using var files = new Files(); var archive = Path.Combine(files.Directory, "archive.txt"); await File.WriteAllTextAsync(files.Output, "previous complete"); await File.WriteAllTextAsync(archive, "other preserved\n"); var calls = 0; var callbacks = 0;
        var engine = Engine(MediaKind.Progressive, request => { var response = Range(request); if (++calls == 2) response.Content = Content(Bytes[4..7], 4, 7, 10, declared: 4); return response; });
        var request = Request(files.Directory) with { DownloadArchiveFile = archive };
        await Expect<IOException>(() => engine.DownloadAsync(request, null, null, default, _ => callbacks++));
        Assert(File.ReadAllText(files.Output) == "previous complete" && File.ReadAllText(archive) == "other preserved\n" && callbacks == 0, "Incomplete native chunks published media/archive success.");
        var result = await engine.DownloadAsync(request, null, null, default, _ => callbacks++); var repeat = await engine.DownloadAsync(request, null, null, default, _ => callbacks++);
        Assert(result.Succeeded && repeat.Skipped && callbacks == 1 && File.ReadAllBytes(files.Output).SequenceEqual(Bytes), "Native chunk publication/archive semantics changed.");
    }
    private static async Task Adaptive()
    {
        foreach (var dash in new[] { false, true })
        {
            using var files = new Files(); var ranges = 0; var engine = Engine(dash ? MediaKind.Dash : MediaKind.Hls, request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith(".mpd")) return new(HttpStatusCode.OK) { Content = new StringContent("<MPD><Period><AdaptationSet mimeType='video/mp4'><Representation id='v'><BaseURL>source.mp4</BaseURL></Representation></AdaptationSet></Period></MPD>") };
                if (path.EndsWith(".m3u8")) return new(HttpStatusCode.OK) { Content = new StringContent("#EXTM3U\n#EXTINF:1,\nsource.ts\n#EXT-X-ENDLIST\n") };
                Assert(request.Headers.Range is not null, "HLS/whole DASH dropped chunk sizing."); ranges++; return Range(request);
            });
            var result = await engine.DownloadAsync(Request(files.Directory) with { Url = new("https://chunks.example/manifest." + (dash ? "mpd" : "m3u8")) }, null, null, default);
            Assert(result.Succeeded && ranges == 3 && File.ReadAllBytes(result.OutputPath!).SequenceEqual(Bytes), "Adaptive chunk propagation changed complete assembly.");
        }
    }
    private static Task Arguments()
    {
        var request = NativeExtraArguments.Apply(Request(Path.GetTempPath()), ["--http-chunk-size", "2M"]); Assert(request.HttpChunkSize == "2M", "Chunk override was dropped.");
        request = NativeExtraArguments.Apply(request, ["--http-chunk-size", "0"]); Assert(NativeHttpChunkSize.Parse(request.HttpChunkSize) is null, "Explicit zero did not disable chunking."); return Task.CompletedTask;
    }
    private static HttpResponseMessage Range(HttpRequestMessage request)
    {
        var range = request.Headers.Range!.Ranges.Single(); var start = range.From!.Value; var end = Math.Min(range.To!.Value, Bytes.Length - 1); var response = Full(); response.StatusCode = HttpStatusCode.PartialContent;
        response.Content = Content(Bytes[(int)start..((int)end + 1)], start, end, Bytes.Length); return response;
    }
    private static HttpResponseMessage Full() { var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes) }; response.Headers.ETag = new("\"v1\""); response.Headers.Date = Modified.AddMinutes(2); response.Content.Headers.LastModified = Modified; return response; }
    private static ByteArrayContent Content(byte[] bytes, long from, long to, long total, long? declared = null) { var content = new ByteArrayContent(bytes); content.Headers.ContentRange = new(from, to, total); content.Headers.LastModified = Modified; if (declared is not null) content.Headers.ContentLength = declared; return content; }
    private static MediaHttp Http(Func<HttpRequestMessage, HttpResponseMessage> run) => new(new Handler(run));
    private static NativeEngine Engine(MediaKind kind, Func<HttpRequestMessage, HttpResponseMessage> run) => new([new Provider(kind)], _ => Http(run));
    private static NativeDownloadRequest Request(string directory) => new() { Url = Url, OutputDirectory = directory, FileName = "chunks", VideoOnly = true, HttpChunkSize = "4", Retries = 0 };
    private sealed class Provider(MediaKind kind) : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("chunk-fixture", "Chunk fixture", 1000); public bool CanHandle(Uri url) => true;
        public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => new("chunk-fixture", "chunks");
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(new PageMetadata { Id = "chunks", Extractor = "chunk-fixture", Title = "Chunks", Media = [new() { Url = request.Url, Extension = "mp4", Kind = kind, HasVideo = true, HasAudio = false }] });
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
    private sealed class Progress(Action<DownloadProgress> report) : IProgress<DownloadProgress> { public void Report(DownloadProgress value) => report(value); }
    private sealed class Files : IDisposable { internal string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", "chunks-" + Guid.NewGuid().ToString("N")); internal string Output => Path.Combine(Directory, "chunks.mp4"); internal Files() => System.IO.Directory.CreateDirectory(Directory); public void Dispose() => System.IO.Directory.Delete(Directory, true); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void ExpectSync<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
