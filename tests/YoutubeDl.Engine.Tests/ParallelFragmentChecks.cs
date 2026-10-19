using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

internal static class ParallelFragmentChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Parallel HLS fragments overlap but preserve initialization, order and progress", () => Ordered("hls")),
        ("Parallel encrypted HLS preserves key rotation and implicit sequence IVs", () => Ordered("aes")),
        ("Parallel HLS byte ranges retain exact ordered resources", () => Ordered("ranges")),
        ("Parallel segmented DASH uses the requested transfer count and ordered initialization", () => Ordered("dash")),
        ("Parallel indexed DASH preserves resource offsets and entity conditions", () => Indexed(false)),
        ("Parallel indexed DASH rejects a changed resource before replacing output", () => Indexed(true)),
        ("Parallel fragment buffering stays bounded behind a slow first fragment", Backpressure),
        ("Parallel fragment failure cancels and drains every active transfer", () => Failure(false, false)),
        ("Parallel fragment cancellation retains previous media and archive state", () => Failure(true, false)),
        ("Parallel fragment consumer failure drains remaining network work", () => Failure(false, true)),
        ("Fragment retry settings remain separate from ordinary request retries", Retries),
        ("Concurrent rate limiting retains reservations across wall-clock windows", Rate),
        ("Invalid fragment counts, retry counts and rate limits fail before HTTP", InvalidOptions)
    ];
    private static async Task Ordered(string mode)
    {
        using var files = new Files(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var gate = new ReverseBatch(3); var keyCalls = 0;
        var firstKey = Enumerable.Repeat((byte)1, 16).ToArray(); var secondKey = Enumerable.Repeat((byte)2, 16).ToArray();
        var manifest = "#EXTM3U\n" + (mode == "aes" ? "#EXT-X-MEDIA-SEQUENCE:42\n#EXT-X-KEY:METHOD=AES-128,URI=\"key\"\n" : mode == "ranges" ? "#EXT-X-MAP:URI=\"asset.bin\",BYTERANGE=\"4@0\"\n" : "#EXT-X-MAP:URI=\"init.bin\"\n");
        for (var index = 1; index <= 3; index++)
        {
            if (mode == "aes" && index == 3) manifest += "#EXT-X-KEY:METHOD=AES-128,URI=\"key\"\n";
            manifest += "#EXTINF:1,\n" + (mode == "ranges" ? "#EXT-X-BYTERANGE:1@" + (3 + index) + "\nasset.bin\n" : index + ".ts\n");
        }
        manifest += "#EXT-X-ENDLIST\n";
        if (mode == "dash") manifest = "<MPD mediaPresentationDuration='PT3S'><Period><AdaptationSet mimeType='video/mp4'><Representation id='video'><SegmentList timescale='1' duration='1'><Initialization sourceURL='init.bin'/><SegmentURL media='1.ts'/><SegmentURL media='2.ts'/><SegmentURL media='3.ts'/></SegmentList></Representation></AdaptationSet></Period></MPD>";
        var progress = new SynchronousProgress();
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(async (request, ct) =>
        {
            var name = Path.GetFileName(request.RequestUri!.AbsolutePath);
            if (name.StartsWith("source.")) return Text(manifest);
            if (name == "key") return Bytes(++keyCalls == 1 ? firstKey : secondKey);
            if (name == "init.bin") return Bytes("INIT"u8.ToArray());
            if (mode == "ranges")
            {
                var from = request.Headers.Range!.Ranges.Single().From!.Value;
                if (from > 0) await gate.WaitAsync((int)from - 3, ct);
                return DashIndexChecks.Range(request, "INITABC"u8.ToArray());
            }
            var number = int.Parse(Path.GetFileNameWithoutExtension(name));
            await gate.WaitAsync(number, ct);
            var data = new[] { (byte)('A' + number - 1) };
            return Bytes(mode == "aes" ? Encrypt(data, number < 3 ? firstKey : secondKey, (ulong)(41 + number)) : data);
        })));
        var result = await engine.DownloadAsync(new() { Url = new("https://media.example/source." + (mode == "dash" ? "mpd" : "m3u8")), OutputDirectory = files.Directory, FileName = "ordered", Retries = 0, ConcurrentFragments = 3 }, progress, null, timeout.Token);
        Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == (mode == "aes" ? "ABC" : "INITABC"), "Out-of-order transfers changed the assembled media.");
        Assert(gate.Peak == 3 && gate.Completed.SequenceEqual(new[] { 3, 2, 1 }), "The selected fragment transfers did not actually overlap or finish out of order.");
        Assert(progress.Values.Count > 0 && progress.Values.SequenceEqual(progress.Values.Order()) && progress.Values[^1] == new FileInfo(result.OutputPath!).Length, "Ordered progress became nonmonotonic or lost bytes.");
        if (mode == "aes") Assert(keyCalls == 2, "Key rotation was collapsed or repeated due to concurrent work.");
    }
    private static async Task Indexed(bool changed)
    {
        using var files = new Files(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var resource = "INIT"u8.ToArray().Concat(DashIndexChecks.Index(firstOffset: 2)).Concat("XXabc12345"u8.ToArray()).ToArray();
        var gate = new ReverseBatch(2);
        const string manifest = "<MPD mediaPresentationDuration='PT3S'><Period><AdaptationSet mimeType='video/mp4'><Representation id='v'><BaseURL>track.mp4</BaseURL><SegmentBase timescale='1000' indexRange='4-59'><Initialization range='0-3'/></SegmentBase></Representation></AdaptationSet></Period></MPD>";
        var target = Path.Combine(files.Directory, "indexed.mp4"); File.WriteAllText(target, "previous");
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(".mpd")) return Text(manifest);
            var from = request.Headers.Range!.Ranges.Single().From!.Value;
            if (from >= 62)
            {
                Assert(request.Headers.IfMatch.Single().Tag == "\"v1\"", "A parallel range omitted its entity condition.");
                await gate.WaitAsync(from == 62 ? 1 : 2, ct);
            }
            var response = DashIndexChecks.Range(request, resource);
            if (changed && from == 65) response.Headers.ETag = new("\"v2\"");
            return response;
        })));
        var request = new NativeDownloadRequest { Url = new("https://media.example/source.mpd"), OutputDirectory = files.Directory, FileName = "indexed", Retries = 0, ConcurrentFragments = 2 };
        if (changed)
        {
            await Expect<InvalidDataException>(() => engine.DownloadAsync(request, null, null, timeout.Token));
            Assert(File.ReadAllText(target) == "previous", "Changed parallel media replaced a published file.");
        }
        else
        {
            var result = await engine.DownloadAsync(request, null, null, timeout.Token);
            Assert(result.Succeeded && File.ReadAllBytes(target).AsSpan().SequenceEqual(resource) && gate.Peak == 2, "Indexed parallel assembly moved or omitted resource bytes.");
        }
    }
    private static async Task Backpressure()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0; var buffered = 0; var peak = 0; var consumed = new List<int>();
        var transfer = OrderedFragmentTransfer.RunAsync(Enumerable.Range(0, 100).ToArray(), 3, async (item, _, ct) =>
        {
            if (Interlocked.Increment(ref started) == 3) ready.TrySetResult();
            if (item == 0) await release.Task.WaitAsync(ct);
            peak = Math.Max(peak, Interlocked.Increment(ref buffered));
        }, (item, _, _) => { consumed.Add(item); Interlocked.Decrement(ref buffered); return Task.CompletedTask; }, stop.Token);
        await ready.Task.WaitAsync(stop.Token);
        Assert(started == 3 && buffered <= 3, "A blocked first fragment allowed unbounded lookahead.");
        release.TrySetResult(); await transfer;
        Assert(peak <= 3 && consumed.SequenceEqual(Enumerable.Range(0, 100)), "Buffered fragments escaped their bound or manifest order.");
    }
    private static async Task Failure(bool cancel, bool consumer)
    {
        using var files = new Files(); using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var active = 0; var started = 0;
        var target = Path.Combine(files.Directory, "protected.ts"); File.WriteAllText(target, "previous");
        var archive = Path.Combine(files.Directory, "archive.txt"); File.WriteAllText(archive, "existing entry\n");
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(".m3u8")) return Text("#EXTM3U\n#EXTINF:1,\n1.ts\n#EXTINF:1,\n2.ts\n#EXTINF:1,\n3.ts\n#EXT-X-ENDLIST");
            Interlocked.Increment(ref active);
            try
            {
                if (Interlocked.Increment(ref started) == 3) ready.TrySetResult();
                await ready.Task.WaitAsync(ct);
                if (!cancel && !consumer && request.RequestUri.AbsolutePath == "/2.ts") throw new HttpRequestException("controlled fragment failure");
                if (consumer && request.RequestUri.AbsolutePath == "/1.ts") return Bytes("first"u8.ToArray());
                await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException("Unreachable");
            }
            finally { Interlocked.Decrement(ref active); }
        })));
        var transfer = engine.DownloadAsync(new() { Url = new("https://media.example/source.m3u8"), OutputDirectory = files.Directory, FileName = "protected", DownloadArchiveFile = archive, Retries = 0, ConcurrentFragments = 3 },
            consumer ? new SynchronousProgress { Throw = true } : null, null, stop.Token);
        await ready.Task.WaitAsync(stop.Token);
        if (cancel) { stop.Cancel(); await Expect<OperationCanceledException>(() => transfer); }
        else
        {
            try { await transfer; throw new InvalidOperationException("A parallel transfer unexpectedly succeeded."); }
            catch (IOException ex) { Assert(ex.ToString().Contains(consumer ? "controlled progress failure" : "controlled fragment failure"), "Cancellation hid the original parallel-transfer failure."); }
        }
        Assert(active == 0 && File.ReadAllText(target) == "previous" && File.ReadAllText(archive) == "existing entry\n" && !Directory.EnumerateDirectories(files.Directory, ".hls-*", SearchOption.AllDirectories).Any(),
            "Parallel failure returned with active workers, leaked staging or changed completed outputs.");
    }
    private static async Task Retries()
    {
        using var files = new Files();
        foreach (var permitted in new[] { 0, 1 })
        {
            var calls = 0;
            var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler((request, _) =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith(".m3u8")) return Task.FromResult(Text("#EXTM3U\n#EXTINF:1,\nmedia.ts\n#EXT-X-ENDLIST"));
                return Task.FromResult(++calls == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Bytes("media"u8.ToArray()));
            })));
            var request = new NativeDownloadRequest { Url = new("https://media.example/source.m3u8"), OutputDirectory = files.Directory, FileName = "retry-" + permitted, Retries = permitted == 0 ? 3 : 0, FragmentRetries = permitted, ConcurrentFragments = 2 };
            if (permitted == 0) await Expect<IOException>(() => engine.DownloadAsync(request, null, null, default));
            else Assert((await engine.DownloadAsync(request, null, null, default)).Succeeded, "Enabled fragment retry was not honored.");
            Assert(calls == permitted + 1, "Fragment retry limits were replaced by general retry settings.");
        }
    }
    private static async Task Rate()
    {
        var limiter = new ByteRateLimiter(100); var timer = Stopwatch.StartNew();
        var first = limiter.ThrottleAsync(300, default);
        await Task.Delay(1100);
        await Task.WhenAll(first, limiter.ThrottleAsync(100, default));
        Assert(timer.Elapsed >= TimeSpan.FromMilliseconds(2850), "An elapsed wall-clock window erased pending byte reservations.");
        using var stop = new CancellationTokenSource(); var waiting = limiter.ThrottleAsync(1000, stop.Token); stop.Cancel();
        await Expect<OperationCanceledException>(() => waiting);
    }
    private static async Task InvalidOptions()
    {
        using var files = new Files(); var request = new NativeDownloadRequest { Url = new("https://media.example/source.m3u8"), OutputDirectory = files.Directory };
        var engine = new NativeEngine(httpFactory: _ => throw new InvalidOperationException("Invalid options reached HTTP."));
        foreach (var count in new[] { 0, 33 }) await Expect<ArgumentOutOfRangeException>(() => engine.InspectAsync(request with { ConcurrentFragments = count }, default));
        foreach (var count in new[] { -1, 1001 }) await Expect<ArgumentOutOfRangeException>(() => engine.InspectAsync(request with { FragmentRetries = count }, default));
        foreach (var rate in new[] { "Infinity", "NaN", "1e1000", "-1", "0", "0.1", "invalid" }) await Expect<FormatException>(() => engine.InspectAsync(request with { RateLimit = rate }, default));
    }
    private static byte[] Encrypt(byte[] value, byte[] key, ulong sequence) { var iv = new byte[16]; BinaryPrimitives.WriteUInt64BigEndian(iv.AsSpan(8), sequence); using var aes = Aes.Create(); aes.Key = key; return aes.EncryptCbc(value, iv, PaddingMode.PKCS7); }
    private static HttpResponseMessage Text(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    private static HttpResponseMessage Bytes(byte[] value) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(value) };
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => response(request, ct); }
    private sealed class SynchronousProgress : IProgress<DownloadProgress>
    { internal List<long> Values { get; } = []; internal bool Throw { get; init; } public void Report(DownloadProgress value) { if (Throw) throw new IOException("controlled progress failure"); Values.Add(value.DownloadedBytes); } }
    private sealed class ReverseBatch(int count)
    {
        private readonly object _sync = new(); private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource[] _finished = Enumerable.Range(0, count + 1).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        private int _started; private int _active; internal int Peak { get; private set; } internal List<int> Completed { get; } = [];
        internal async Task WaitAsync(int number, CancellationToken ct)
        {
            lock (_sync) { Peak = Math.Max(Peak, ++_active); if (++_started == count) _ready.TrySetResult(); }
            try
            {
                await _ready.Task.WaitAsync(ct); if (number < count) await _finished[number + 1].Task.WaitAsync(ct);
                lock (_sync) Completed.Add(number); _finished[number].TrySetResult();
            }
            finally { lock (_sync) _active--; }
        }
    }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, "parallel-" + Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() { var path = Path.GetFullPath(Directory); if (!path.StartsWith(Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Test folder escaped its root."); System.IO.Directory.Delete(path, true); }
    }
}
