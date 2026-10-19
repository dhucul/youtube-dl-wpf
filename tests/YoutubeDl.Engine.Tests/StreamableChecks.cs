using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Streamable;

internal static class StreamableChecks
{
    private static readonly Uri Url = new("https://streamable.com/abc123");
    private static JsonNode Metadata() => JsonNode.Parse("""
        {"status":2,"url":"streamable.com/abc123","title":"Video title","thumbnail_url":"//thumbs-east.streamable.com/image/abc123.jpg","audio_channels":2,
         "files":{"mp4":{"status":2,"url":"//cdn.streamable.com/video/mp4/abc123.mp4?token=a%2Bb%2Fc&expires=4102444800","width":1280,"height":720,"framerate":30,"duration":2.25,"bitrate":1000000,"size":1000},
                  "mp4-mobile":{"status":2,"url":"https://cdn.streamable.com/video/mp4-mobile/abc123.mp4","width":640,"height":360,"framerate":30,"duration":2.25,"bitrate":300000,"size":300},
                  "original":{"width":1920,"height":1080,"duration":2.25}}}
        """)!;
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Streamable native API retains exact identity, format facts and signed URLs", Facts),
        ("Streamable watch, embed and API aliases share their canonical archive identity", Aliases),
        ("Streamable quality limits and explicit IDs select only offered ready media", Selection),
        ("Streamable generic iframe discovery delegates through the native provider", Embed),
        ("Streamable unavailable or processing videos never transfer media", Unavailable),
        ("Streamable partial rendition readiness excludes unavailable and protected variants", ReadyFiles),
        ("Streamable malformed identities, resource boundaries and file lists fail", Invalid),
        ("Streamable archived embed aliases skip metadata and transfer", Archive),
        ("Streamable cancellation does not publish output", Cancellation)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var info = await Engine(calls).InspectAsync(Request(Path.GetTempPath()), default);
        var hd = info.Formats.Single(format => format.Id == "mp4");
        Assert(info.Id == "abc123" && info.Title == "Video title" && info.DurationSeconds == 2.25 && info.Formats.Count == 2 && hd.Width == 1280 && hd.Height == 720 && hd.FramesPerSecond == 30 && hd.FileSize == 1000,
            "Ready format facts or the original-without-URL boundary changed.");
        Assert(calls.Single().AbsoluteUri == "https://api.streamable.com/videos/abc123", "Metadata queried an unrelated endpoint.");
        using var files = new Files(); calls.Clear();
        var result = await Engine(calls).DownloadAsync(Request(files.Directory), null, null, default);
        Assert(result.Succeeded && calls[1].Query == "?token=a%2Bb%2Fc&expires=4102444800", "Signed URL query bytes were changed.");
    }
    private static async Task Aliases()
    {
        var provider = new StreamableProvider();
        foreach (var url in new[] { "https://www.streamable.com/abc123", "https://streamable.com/e/abc123?muted=1", "https://streamable.com/o/abc123", "https://api.streamable.com/videos/abc123" })
        {
            var request = Request(Path.GetTempPath()) with { Url = new(url) };
            Assert(provider.CanHandle(request.Url) && provider.GetArchiveIdentity(request)?.Id == "abc123" && (await Engine([]).InspectAsync(request, default)).Id == "abc123", "A supported alias lost its identity.");
        }
        foreach (var url in new[] { "https://streamable.com.evil.example/abc123", "https://user@streamable.com/abc123", "https://streamable.com/blog", "https://streamable.com/abc123/other", "https://streamable.com:444/abc123" })
            Assert(!provider.CanHandle(new(url)), "An unsupported Streamable URL was claimed.");
    }
    private static async Task Selection()
    {
        using var files = new Files();
        foreach (var selection in new[] { "worst", "height", "explicit" })
        {
            var calls = new List<Uri>();
            var request = Request(files.Directory) with { FileName = selection, Quality = selection == "worst" ? QualityPreference.Worst : QualityPreference.Best,
                MaximumHeight = selection == "height" ? 480 : null, FormatSelector = selection == "explicit" ? "mp4-mobile" : "" };
            var result = await Engine(calls).DownloadAsync(request, null, null, default);
            Assert(result.Succeeded && calls.Count == 2 && calls[1].AbsolutePath == "/video/mp4-mobile/abc123.mp4", "The request did not select the ready mobile rendition.");
        }
        var limited = new List<Uri>();
        await Expect<InvalidOperationException>(() => Engine(limited).DownloadAsync(Request(files.Directory) with { MaximumFrameRate = 24 }, null, null, default));
        Assert(limited.Count == 1, "A frame-rate mismatch fetched media anyway.");
    }
    private static async Task Embed()
    {
        using var files = new Files(); var calls = new List<Uri>();
        var result = await Engine(calls).DownloadAsync(Request(files.Directory) with { Url = new("https://article.example/post") }, null, null, default);
        Assert(result.Succeeded && calls.Count == 3 && calls[1].Host == "api.streamable.com", "An iframe bypassed native Streamable discovery.");
    }
    private static async Task Unavailable()
    {
        using var files = new Files();
        foreach (var status in new[] { 0, 1, 3 })
        {
            var data = Metadata(); data["status"] = status; var calls = new List<Uri>();
            await Expect<NotSupportedException>(() => Engine(calls, data).DownloadAsync(Request(files.Directory), null, null, default));
            Assert(calls.Count == 1 && !Directory.EnumerateFiles(files.Directory).Any(), "An unready video fetched or published media.");
        }
        var denied = new List<Uri>();
        await Expect<HttpRequestException>(() => Engine(denied, status: HttpStatusCode.Forbidden).InspectAsync(Request(files.Directory), default));
        Assert(denied.Count == 1, "An access denial was retried through another endpoint.");
    }
    private static async Task ReadyFiles()
    {
        var data = Metadata(); data["files"]!["mp4"]!["status"] = 1;
        var info = await Engine([], data).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Formats.Count == 1 && info.Formats[0].Id == "mp4-mobile" && info.Warnings.Count > 0, "A processing rendition was exposed as ready.");
        data = Metadata(); data["files"]!["mp4"]!["drm"] = new JsonObject { ["scheme"] = "protected" };
        info = await Engine([], data).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Formats.Count == 1 && info.Formats[0].Id == "mp4-mobile", "A protected rendition suppressed clear media or became ready.");
        data = Metadata(); data["files"]!["mp4"]!["framerate"] = 0;
        info = await Engine([], data).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Formats.Single(format => format.Id == "mp4").FramesPerSecond is null, "Unknown frame rate became a fabricated fact.");
    }
    private static async Task Invalid()
    {
        foreach (var change in new Action<JsonNode>[] { data => data["url"] = "streamable.com/other", data => data["shortcode"] = "other",
            data => data["files"] = null, data => data["files"]!["mp4"] = 1,
            data => data["files"]!["mp4"]!["url"] = "https://streamable.com.evil.example/video.mp4", data => data["thumbnail_url"] = "https://user@streamable.com/image.jpg" })
        { var data = Metadata(); change(data); await Expect<InvalidDataException>(() => Engine([], data).InspectAsync(Request(Path.GetTempPath()), default)); }
        var privateData = Metadata(); privateData["password_protected"] = true;
        await Expect<NotSupportedException>(() => Engine([], privateData).InspectAsync(Request(Path.GetTempPath()), default));
    }
    private static async Task Archive()
    {
        using var files = new Files(); var path = Path.Combine(files.Directory, "history.txt"); await File.WriteAllTextAsync(path, "streamable abc123\n");
        var calls = new List<Uri>();
        var result = await Engine(calls).DownloadAsync(Request(files.Directory) with { Url = new("https://streamable.com/e/abc123"), DownloadArchiveFile = path }, null, null, default);
        Assert(result.Succeeded && result.Skipped && calls.Count == 0, "An archived alias fetched the API.");
    }
    private static async Task Cancellation()
    {
        using var files = new Files(); using var stop = new CancellationTokenSource();
        await Expect<OperationCanceledException>(() => Engine([], before: _ => stop.Cancel()).DownloadAsync(Request(files.Directory), null, null, stop.Token));
        Assert(!Directory.EnumerateFiles(files.Directory).Any(), "Canceled Streamable discovery published files.");
    }
    private static NativeEngine Engine(List<Uri> calls, JsonNode? data = null, HttpStatusCode status = HttpStatusCode.OK, Action<Uri>? before = null) => new(httpFactory: _ => new MediaHttp(new Handler(request =>
    {
        var url = request.RequestUri!; calls.Add(url); before?.Invoke(url);
        var text = url.Host == "article.example" ? "<iframe src=\"https://streamable.com/e/abc123\"></iframe>" : url.Host == "api.streamable.com" ? (data ?? Metadata()).ToJsonString() : "media bytes";
        return new(url.Host == "api.streamable.com" ? status : HttpStatusCode.OK) { Content = new StringContent(text) };
    })));
    private static NativeDownloadRequest Request(string directory) => new() { Url = Url, OutputDirectory = directory, FileName = "video", Retries = 0 };
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() { var path = Path.GetFullPath(Directory); if (!path.StartsWith(Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Test folder escaped its root."); System.IO.Directory.Delete(path, true); }
    }
}
