using System.Net;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

internal static class RangeChunkChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Range chunks preserve parent offsets exact bytes and existing output prefixes", Bounded),
        ("Range chunks retry incomplete subranges without duplicate parent bytes", Retry),
        ("Range chunks reject changed or missing source validators and rewind parent output", Changed),
        ("Range chunks unidentifiable sources use one complete protocol range", Unidentified),
        ("Range chunks cancellation preserves previously completed output prefixes", Cancel),
        ("Range chunks bounds reject before requests or output changes", Bounds),
        ("Range chunks HLS byte ranges retain complete ordered payloads", Hls),
        ("Range chunks range-ignoring responses cannot become assembled media", Ignored)
    ];
    private static readonly Uri Url = new("https://range-chunks.example/source.mp4?Signature=literal%2Bbytes");
    private static readonly byte[] Bytes = Enumerable.Range(0, 40).Select(value => (byte)value).ToArray();
    private static async Task Bounded()
    {
        var requests = new List<(long?, long?)>(); using var http = Http(request => { Assert(request.RequestUri == Url, "Range resource query changed."); var range = request.Headers.Range!.Ranges.Single(); requests.Add((range.From, range.To)); if (requests.Count > 1) Assert(request.Headers.IfMatch.Single().ToString() == "\"stable\"", "Subranges lost their resource binding."); return Range(request); });
        using var output = Prefix(); await Copy(http, output);
        Assert(requests.SequenceEqual(new[] { ((long?)5, (long?)8), ((long?)9, (long?)12), ((long?)13, (long?)14) }) && output.ToArray().SequenceEqual(new byte[] { 90, 91 }.Concat(Bytes[5..15])), "Parent offsets or byte concatenation changed.");
    }
    private static async Task Retry()
    {
        var offsets = new List<long>(); var failed = false; using var http = Http(request =>
        { var start = request.Headers.Range!.Ranges.Single().From!.Value; offsets.Add(start); var response = Range(request); if (start == 9 && !failed) { failed = true; response.Content = new ByteArrayContent(Bytes[9..11]); response.Content.Headers.ContentRange = new(9, 12, 40); response.Content.Headers.ContentLength = 4; } return response; });
        using var output = Prefix(); await Copy(http, output, retries: 1);
        Assert(offsets.SequenceEqual(new long[] { 5, 9, 9, 13 }) && output.ToArray().SequenceEqual(new byte[] { 90, 91 }.Concat(Bytes[5..15])), "A retried piece duplicated or omitted parent-range bytes.");
    }
    private static async Task Changed()
    {
        foreach (var kind in new[] { "changed", "missing", "total" })
        {
            var count = 0; using var http = Http(request => { var response = Range(request); if (++count == 2) { if (kind == "changed") response.Headers.ETag = new("\"other\""); else if (kind == "missing") response.Headers.ETag = null; else response.Content.Headers.ContentRange = new(9, 12, 41); } return response; });
            using var output = Prefix(); await Expect<InvalidDataException>(() => Copy(http, output)); Assert(output.ToArray().SequenceEqual(new byte[] { 90, 91 }), "Conflicting subranges retained partial parent output.");
        }
    }
    private static async Task Unidentified()
    {
        var sizes = new List<long>(); using var http = Http(request => { var range = request.Headers.Range!.Ranges.Single(); sizes.Add(range.To!.Value - range.From!.Value + 1); var response = Range(request); response.Headers.ETag = null; return response; });
        using var output = Prefix(); await Copy(http, output);
        Assert(sizes.SequenceEqual(new long[] { 4, 10 }) && output.ToArray().SequenceEqual(new byte[] { 90, 91 }.Concat(Bytes[5..15])), "Unbound pieces were stitched or complete parent-range retrieval changed bytes.");
    }
    private static async Task Cancel()
    {
        using var stop = new CancellationTokenSource(); var count = 0; using var http = Http(request => { if (++count == 2) stop.Cancel(); return Range(request); }); using var output = Prefix();
        await Expect<OperationCanceledException>(() => Copy(http, output, ct: stop.Token)); Assert(output.ToArray().SequenceEqual(new byte[] { 90, 91 }), "Cancellation retained incomplete parent bytes.");
    }
    private static async Task Bounds()
    {
        using var http = Http(_ => throw new InvalidOperationException("Invalid range reached HTTP.")); using var output = Prefix();
        await Expect<InvalidDataException>(() => IndexedDashDownloader.CopyRangeAsync(http, Url, new(5, 10), output, null, null, 0, 9, default, httpChunkSize: "4"));
        Assert(output.ToArray().SequenceEqual(new byte[] { 90, 91 }), "Invalid parent bounds changed existing output.");
    }
    private static async Task Hls()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-data", "range-hls-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var sizes = new List<long>(); using var http = Http(request => { var range = request.Headers.Range!.Ranges.Single(); sizes.Add(range.To!.Value - range.From!.Value + 1); return Range(request); });
            var playlist = HlsPlaylistParser.Parse("#EXTM3U\n#EXTINF:1,\n#EXT-X-BYTERANGE:10@5\nsource.mp4?Signature=literal%2Bbytes\n#EXT-X-ENDLIST\n", Url);
            var output = Path.Combine(directory, "ranged.ts"); await new HlsDownloader(http) { HttpChunkSize = "4" }.DownloadAsync(playlist, output, null, default);
            Assert(sizes.SequenceEqual(new long[] { 4, 4, 2 }) && File.ReadAllBytes(output).SequenceEqual(Bytes[5..15]), "HLS imposed ranges did not subdivide and retain payload order.");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task Ignored()
    {
        using var http = Http(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes) }); using var output = Prefix();
        await Expect<InvalidDataException>(() => Copy(http, output)); Assert(output.ToArray().SequenceEqual(new byte[] { 90, 91 }), "A full response was published as a protocol range.");
    }
    private static async Task Copy(MediaHttp http, Stream output, int retries = 0, CancellationToken ct = default) =>
        await IndexedDashDownloader.CopyRangeAsync(http, Url, new(5, 10), output, null, null, retries, 100, ct, httpChunkSize: "4");
    private static MemoryStream Prefix() { var output = new MemoryStream(); output.Write(new byte[] { 90, 91 }); return output; }
    private static HttpResponseMessage Range(HttpRequestMessage request)
    {
        var range = request.Headers.Range!.Ranges.Single(); var start = (int)range.From!.Value; var end = (int)range.To!.Value;
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(Bytes[start..(end + 1)]) };
        response.Content.Headers.ContentRange = new(start, end, Bytes.Length); response.Headers.ETag = new("\"stable\""); return response;
    }
    private static MediaHttp Http(Func<HttpRequestMessage, HttpResponseMessage> run) => new(new Handler(run));
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
