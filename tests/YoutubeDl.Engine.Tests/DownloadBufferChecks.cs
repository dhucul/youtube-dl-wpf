using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class DownloadBufferChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Download buffers parse exact positive byte sizes and reject runtime overflow", Sizes),
        ("Download buffers fixed sizes control actual progressive reads and preserve all bytes", Fixed),
        ("Download buffers automatically grow from the configured initial size without losing bytes", Automatic),
        ("Download buffers shrink after slow reads while preserving the previous returned memory", Slow),
        ("Download buffers protocol readers preserve small fields and independent source ownership", Protocol),
        ("Download buffers independent concurrent responses cannot resize or overwrite one another", Concurrent),
        ("Download buffers indexed DASH ranges honor fixed read sizes and full boundaries", Indexed),
        ("Download buffers HLS ranges honor fixed read sizes and complete assembly", Hls),
        ("Download buffers native requests propagate fixed sizing and archive skips", Native),
        ("Download buffers invalid requests reject before discovery output or archive changes", Invalid),
        ("Download buffers explicit job arguments preserve sizing and resizing precedence", Arguments)
    ];
    private static readonly byte[] Bytes = Enumerable.Range(0, 256 * 1024 + 13).Select(value => (byte)(value * 17)).ToArray();
    private static readonly Uri Url = new("https://buffer.example/source.mp4");
    private static Task Sizes()
    {
        Assert(NativeReadBuffer.ParseSize(null) == 1024 && NativeReadBuffer.ParseSize("1.5KiB") == 1536 && NativeReadBuffer.ParseSize("65K") == 66560 && NativeReadBuffer.ParseSize("1") == 1, "Buffer sizes changed units or defaults.");
        foreach (var value in new[] { "0", "-1", "NaN", "2G", "999999999999999999999999Y", "custom-command" }) ExpectSync<NativeSelectionException>(() => NativeReadBuffer.ParseSize(value)); return Task.CompletedTask;
    }
    private static async Task Fixed()
    {
        foreach (var size in new[] { "1K", "4K", "65K" })
        {
            using var files = new Files(); var source = new RecordedStream(Bytes); using var http = Http(_ => Response(source));
            await new FileDownloader(http) { BufferSize = size, NoResizeBuffer = true }.DownloadAsync(Url, files.Output, null, null, default, 0);
            Assert(source.Sizes.Count > 0 && source.Sizes.All(length => length == NativeReadBuffer.ParseSize(size)) && File.ReadAllBytes(files.Output).SequenceEqual(Bytes), "Fixed buffers were ignored or changed media bytes.");
        }
    }
    private static async Task Automatic()
    {
        using var files = new Files(); var source = new RecordedStream(Bytes); using var http = Http(_ => Response(source));
        await new FileDownloader(http) { BufferSize = "2K" }.DownloadAsync(Url, files.Output, null, null, default, 0);
        Assert(source.Sizes[0] == 2048 && source.Sizes.Any(length => length > 2048) && File.ReadAllBytes(files.Output).SequenceEqual(Bytes), "Automatic sizing did not retain its configured starting size and complete payload.");
    }
    private static async Task Slow()
    {
        using var http = Http(_ => throw new InvalidOperationException()); using var source = new RecordedStream(Bytes, TimeSpan.FromMilliseconds(300)); var buffer = new NativeReadBuffer("4K", false);
        var first = await buffer.ReadAsync(http, source, default); var saved = first.ToArray();
        var second = await buffer.ReadAsync(http, source, default);
        Assert(source.Sizes[1] < source.Sizes[0] && first.Span.SequenceEqual(saved) && second.Span.SequenceEqual(Bytes.AsSpan(saved.Length, second.Length)), "Buffer adaptation replaced unconsumed memory or did not react to slow reads.");
    }
    private static async Task Protocol()
    {
        using var source = new RecordedStream(Bytes[..123]); using var http = Http(_ => throw new InvalidOperationException()); var output = new List<byte>();
        using (var buffered = new NativeBufferedReadStream(source, http, "32", true))
        {
            var field = new byte[1]; while (await buffered.ReadAsync(field) == 1) output.Add(field[0]);
        }
        Assert(output.SequenceEqual(Bytes[..123]) && source.Sizes.All(size => size == 32) && source.CanRead, "Protocol fields bypassed fixed buffering, lost bytes or disposed the caller's source.");
    }
    private static async Task Concurrent()
    {
        using var http = Http(_ => throw new InvalidOperationException()); using var left = new RecordedStream(Bytes); using var right = new RecordedStream(Bytes.Reverse().ToArray());
        var a = new NativeReadBuffer("3K", true); var b = new NativeReadBuffer("7K", false); using var x = new MemoryStream(); using var y = new MemoryStream();
        await Task.WhenAll(Copy(a, left, x), Copy(b, right, y));
        Assert(x.ToArray().SequenceEqual(Bytes) && y.ToArray().SequenceEqual(Bytes.Reverse()) && left.Sizes.All(size => size == 3072) && right.Sizes[0] == 7168, "Concurrent response buffers shared mutable bytes or sizing.");
        async Task Copy(NativeReadBuffer buffer, Stream input, Stream output) { while (true) { var data = await buffer.ReadAsync(http, input, default); if (data.Length == 0) return; await output.WriteAsync(data); await Task.Yield(); } }
    }
    private static async Task Indexed()
    {
        var source = new RecordedStream(Bytes); using var http = Http(_ => Range(source)); using var output = new MemoryStream();
        await IndexedDashDownloader.CopyRangeAsync(http, Url, new(4, Bytes.Length), output, null, null, 0, Bytes.Length, default, "3K", true);
        Assert(output.ToArray().SequenceEqual(Bytes) && source.Sizes.All(size => size == 3072), "Indexed DASH range buffering changed requested boundaries or complete bytes.");
    }
    private static async Task Hls()
    {
        using var files = new Files(); var source = new RecordedStream(Bytes); using var http = Http(_ => Range(source));
        var playlist = HlsPlaylistParser.Parse("#EXTM3U\n#EXTINF:1,\n#EXT-X-BYTERANGE:" + Bytes.Length + "@4\nsource.ts\n#EXT-X-ENDLIST\n", Url);
        await new HlsDownloader(http) { BufferSize = "5K", NoResizeBuffer = true }.DownloadAsync(playlist, files.Output, null, default);
        Assert(File.ReadAllBytes(files.Output).SequenceEqual(Bytes) && source.Sizes.All(size => size == 5120), "HLS ranges ignored fixed read sizes or lost assembled bytes.");
    }
    private static async Task Native()
    {
        using var files = new Files(); var sources = new List<RecordedStream>(); var engine = new NativeEngine([new Provider()], _ => Http(_ => { var source = new RecordedStream(Bytes); sources.Add(source); return Response(source); }));
        var request = Request(files.Directory) with { BufferSize = "6K", NoResizeBuffer = true, DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") }; var callbacks = 0;
        var result = await engine.DownloadAsync(request, null, null, default, _ => callbacks++); var repeat = await engine.DownloadAsync(request, null, null, default, _ => callbacks++);
        Assert(result.Succeeded && repeat.Skipped && callbacks == 1 && sources.Count == 1 && sources[0].Sizes.All(size => size == 6144) && File.ReadAllBytes(files.Output).SequenceEqual(Bytes), "Native buffer settings were not propagated or archived media was repeated.");
    }
    private static async Task Invalid()
    {
        using var files = new Files(); var output = Path.Combine(files.Directory, "unstarted"); var engine = new NativeEngine([new Provider()], _ => throw new InvalidOperationException("Invalid buffer reached discovery."));
        await Expect<NativeSelectionException>(() => engine.DownloadAsync(Request(output) with { BufferSize = "0", DownloadArchiveFile = Path.Combine(output, "archive.txt") }, null, null, default));
        Assert(!Directory.Exists(output), "Invalid buffer created output/archive state.");
    }
    private static Task Arguments()
    {
        var request = NativeExtraArguments.Apply(Request(Path.GetTempPath()) with { BufferSize = "8K", NoResizeBuffer = true }, ["--buffer-size", "16K", "--resize-buffer"]);
        Assert(request.BufferSize == "16K" && !request.NoResizeBuffer, "Explicit buffer arguments lost saved precedence."); request = NativeExtraArguments.Apply(request, ["--no-resize-buffer"]); Assert(request.NoResizeBuffer, "Fixed buffering override was ignored."); return Task.CompletedTask;
    }
    internal sealed class RecordedStream(byte[] bytes, TimeSpan? delay = null) : MemoryStream(bytes)
    {
        internal List<int> Sizes { get; } = [];
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { Sizes.Add(buffer.Length); if (delay is { } wait) await Task.Delay(wait, ct); return await base.ReadAsync(buffer, ct); }
    }
    private sealed class RecordedContent(RecordedStream source) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = source.Length; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => source.CopyToAsync(stream);
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(source);
    }
    private static MediaHttp Http(Func<HttpRequestMessage, HttpResponseMessage> run) => new(new Handler(run));
    private static HttpResponseMessage Response(RecordedStream source) => new(HttpStatusCode.OK) { Content = new RecordedContent(source) };
    private static HttpResponseMessage Range(RecordedStream source) { var response = Response(source); response.StatusCode = HttpStatusCode.PartialContent; response.Content.Headers.ContentRange = new(4, 3 + Bytes.Length, 4 + Bytes.Length); return response; }
    private static NativeDownloadRequest Request(string directory) => new() { Url = Url, OutputDirectory = directory, FileName = "buffer", VideoOnly = true, Retries = 0 };
    private sealed class Provider : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("buffer-fixture", "Buffer fixture", 1000);
        public bool CanHandle(Uri url) => true;
        public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => new("buffer-fixture", "buffer");
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(new PageMetadata { Id = "buffer", Extractor = "buffer-fixture", Title = "Buffer", Media = [new() { Url = Url, Extension = "mp4", HasVideo = true, HasAudio = false }] });
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(run(request)); }
    private sealed class Files : IDisposable
    {
        internal string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", "buffers-" + Guid.NewGuid().ToString("N"));
        internal string Output => Path.Combine(Directory, "buffer.mp4"); internal Files() => System.IO.Directory.CreateDirectory(Directory); public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void ExpectSync<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
