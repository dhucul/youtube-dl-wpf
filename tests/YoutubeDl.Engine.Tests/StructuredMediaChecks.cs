using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;

internal static class StructuredMediaChecks
{
    internal static NativeDownloadRequest Request(string output) => new() { Url = new("https://publisher.example/story"), OutputDirectory = output, FileName = "structured", Retries = 0 };
    internal static string Html(string json) => "<title>Page title</title><script data-purpose='metadata' type='application/ld+json'>" + json + "</script>";
    internal static string Video(string id = "one", string file = "one.mp4") => "{\"@context\":\"https://schema.org\",\"@type\":\"VideoObject\",\"@id\":\"#" + id + "\",\"name\":\"Video " + id + "\",\"contentUrl\":\"https://cdn.example/" + file + "\",\"duration\":\"PT2S\"}";
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Structured media reads literal JSON metadata and MIME-typed extensionless URLs", Facts),
        ("Structured graph main entities exclude related media and unrelated page URLs", MainEntity),
        ("Structured media graph encodings remain alternatives of one video", Encodings),
        ("Structured separate media objects form a selected-only playlist", Multiple),
        ("Structured AudioObject discovery retains its audio role", Audio),
        ("Structured embed URLs delegate to a registered native provider", Embed),
        ("Structured image objects malformed JSON and inert examples never become media", Exclusions),
        ("Structured remote contexts are not fetched or treated as Schema.org", Contexts),
        ("Structured subscriptions live broadcasts and clip ranges do not fall through to loose URLs", Restrictions),
        ("Structured unresolved recursive and conflicting graph references fail safely", References),
        ("Structured identities remain scoped and case-sensitive access URLs stay distinct", Identity),
        ("Structured media inventory limits reject truncation before transfer", Limits)
    ];
    private static async Task Facts()
    {
        var html = Html("""
            {"@context":"https://schema.org","@type":["VideoObject","MediaObject"],"@id":"#main","name":"A \"quoted\" & title","description":"Line one\nLine two",
             "contentUrl":"https:\/\/cdn.example\/get?token=A%2Bb\u0026x=1","encodingFormat":"video/mp4","duration":"PT1M2.5S",
             "width":1280.0,"height":{"@type":"QuantitativeValue","value":720,"unitText":"px"},"contentSize":"12345 bytes","author":{"@type":"Person","name":"Creator"},"thumbnailUrl":["/cover.jpg"],
             "caption":{"@type":"MediaObject","contentUrl":"/captions","encodingFormat":"text/vtt","inLanguage":"en"}}
            """);
        var calls = new List<Uri>(); var info = await Engine(html, calls).InspectAsync(Request(Path.GetTempPath()), default); var format = info.Formats.Single();
        Assert(info.Extractor == "generic:jsonld" && info.Title == "A \"quoted\" & title" && info.Description == "Line one\nLine two" && info.DurationSeconds == 62.5 && info.Uploader == "Creator", "Structured text, duration or creator metadata changed.");
        Assert(format.Url!.Query == "?token=A%2Bb&x=1" && format.Extension == "mp4" && format.Width == 1280 && format.Height == 720 && format.FileSize == 12345 && info.ThumbnailUrl == "https://publisher.example/cover.jpg" && calls.Count == 1, "Literal URL decoding or declared media facts changed.");
        Assert(info.Subtitles.Count == 1 && info.Subtitles[0].Language == "en" && info.Subtitles[0].Extension == "vtt" && info.Subtitles[0].Url.AbsolutePath == "/captions", "A declared caption media object lost its format or language.");
    }
    private static async Task MainEntity()
    {
        var graph = """
            {"@context":"https://schema.org","@graph":[{"@type":"WebPage","url":"https://publisher.example/story","mainEntity":{"@id":"#main"}},
            {"@type":"VideoObject","@id":"#main","name":"Main","contentUrl":"/main.mp4","author":{"@id":"#author"}},
            {"@type":"VideoObject","@id":"#related","name":"Related","contentUrl":"/related-2160.mp4"},{"@type":"Person","@id":"#author","name":"Named author"}]}
            """;
        var info = await Engine(Html(graph) + "<video src='https://advert.example/ad-2160.mp4'></video>", []).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(!info.IsPlaylist && info.Title == "Main" && info.Uploader == "Named author" && info.Formats.Single().Url!.AbsolutePath == "/main.mp4", "Main-entity selection mixed unrelated media into its formats.");
    }
    private static async Task Encodings()
    {
        var graph = """
            {"@context":"https://schema.org","@graph":[{"@type":"VideoObject","@id":"#main","name":"One video","contentUrl":"/low.mp4","width":1920,"height":1080,"encoding":[{"@id":"#low"},{"@id":"#high"}]},
            {"@type":"VideoObject","@id":"#low","contentUrl":"/low.mp4","height":360,"width":640},
            {"@type":"MediaObject","@id":"#high","contentUrl":"/high","encodingFormat":"video/mp4","height":720,"width":1280}]}
            """;
        var info = await Engine(Html(graph), []).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(!info.IsPlaylist && info.Title == "One video" && info.Formats.Count == 2 && info.Formats.Select(item => item.Height).SequenceEqual(new int?[] { 360, 720 }), "Encodings became separate videos or lost their own quality facts.");
    }
    private static async Task Multiple()
    {
        using var files = new Files(); var calls = new List<Uri>(); var html = Html("[" + Video() + "," + Video("two", "two.mp4") + "]");
        var info = await Engine(html, calls).InspectAsync(Request(files.Directory), default);
        Assert(info.IsPlaylist && info.Title == "Page title" && info.Entries.Count == 2 && info.Entries.Select(item => item.Title).SequenceEqual(new[] { "Video one", "Video two" }), "Distinct videos became quality alternatives.");
        var anchored = await Engine(html, []).InspectAsync(Request(files.Directory) with { Url = new("https://publisher.example/story#two") }, default);
        Assert(!anchored.IsPlaylist && anchored.Title == "Video two" && anchored.Formats.Single().Url!.AbsolutePath == "/two.mp4", "An explicit media-object fragment selected the wrong video.");
        calls.Clear(); var result = await Engine(html, calls).DownloadAsync(Request(files.Directory) with { PlaylistItems = "2", SkipDownload = true, WriteInfoJson = true }, null, null, default);
        Assert(result.Succeeded && result.SidecarPaths.Count == 1 && calls.Count == 1, "Unselected media was fetched during metadata-only selection.");
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(result.SidecarPaths[0]));
        Assert(json.RootElement.GetProperty("title").GetString() == "Video two" && json.RootElement.GetProperty("playlist_index").GetInt32() == 2, "Selected metadata lost its original position.");
    }
    private static async Task Audio()
    {
        var info = await Engine(Html("{\"@context\":\"http://schema.org/\",\"@type\":\"AudioObject\",\"name\":\"Episode\",\"contentUrl\":\"/download\",\"encodingFormat\":\"audio/mpeg\"}"), []).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Formats.Single().AudioOnly && info.Formats[0].HasVideo == false && info.Formats[0].Extension == "mp3", "Audio metadata became video or required a URL extension.");
    }
    private static async Task Embed()
    {
        var html = Html("{\"@context\":\"https://schema.org\",\"@type\":\"VideoObject\",\"name\":\"Hosted\",\"embedUrl\":\"https://streamable.com/e/abc123\"}"); var calls = new List<Uri>();
        var info = await Engine(html, calls, request => new(HttpStatusCode.OK) { Content = new StringContent("{\"status\":2,\"url\":\"https://streamable.com/abc123\",\"title\":\"Hosted title\",\"files\":{\"mp4\":{\"status\":2,\"url\":\"https://cdn.streamable.com/abc123.mp4\"}}}") }).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Id == "abc123" && info.Extractor == "streamable" && calls.Count == 2, "A player URL was mistaken for media bytes or ignored.");
    }
    private static async Task Exclusions()
    {
        foreach (var html in new[] { Html("{\"@context\":\"https://schema.org\",\"@type\":\"ImageObject\",\"contentUrl\":\"https://cdn.example/image.mp4\"}"),
            Html("{ invalid: \"https://cdn.example/broken.mp4\" }"), "<!--" + Html(Video()) + "-->", "<template>" + Html(Video()) + "<video src='https://cdn.example/also-inert.mp4'></video></template>",
            "<textarea>" + Html(Video()) + "</textarea>" })
        { var info = await Engine(html, []).InspectAsync(Request(Path.GetTempPath()), default); Assert(info.Formats.Count == 0, "An inert, malformed or non-media declaration fell through to loose URL scanning."); }
        var unrelated = Html("[{\"@context\":\"https://schema.org\",\"@type\":\"ImageObject\",\"@id\":\"#image\",\"url\":\"/a.jpg\"},{\"@context\":\"https://schema.org\",\"@type\":\"ImageObject\",\"@id\":\"#image\",\"url\":\"/b.jpg\"}]") + "<video src='https://cdn.example/actual.mp4'></video>";
        Assert((await Engine(unrelated, []).InspectAsync(Request(Path.GetTempPath()), default)).Formats.Single().Url!.AbsolutePath == "/actual.mp4", "Unrelated conflicting image metadata blocked ordinary media discovery.");
        var emptyUrl = Html("{\"@context\":\"https://schema.org\",\"@type\":\"VideoObject\",\"contentUrl\":\"\",\"encodingFormat\":\"video/mp4\",\"thumbnail\":{\"@type\":\"ImageObject\"}}");
        var emptyInfo = await Engine(emptyUrl, []).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(emptyInfo.Formats.Count == 0 && emptyInfo.ThumbnailUrl is null, "An empty structured resource became the HTML page itself.");
    }
    private static async Task Contexts()
    {
        var calls = new List<Uri>(); var info = await Engine(Html(Video().Replace("https://schema.org", "https://other.example/context")), calls).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Formats.Count == 0 && calls.Count == 1, "An unrelated context was treated as Schema.org or fetched remotely.");
        info = await Engine(Html("{\"@type\":\"https://schema.org/VideoObject\",\"contentUrl\":\"/qualified.mp4\"}"), []).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Formats.Count == 1, "A fully qualified schema type required a fetched context.");
        info = await Engine(Html(Video().Replace("\"@context\":\"https://schema.org\"", "\"@context\":[\"https://schema.org\",\"https://other.example/context\"]")), []).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Formats.Count == 0, "A later unrelated context did not replace the earlier vocabulary.");
        info = await Engine(Html(Video().Replace("\"@context\":\"https://schema.org\"", "\"@context\":[\"https://schema.org\",{\"@language\":\"en\"}]")), []).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Formats.Count == 1, "A language-only context incorrectly removed the inherited vocabulary.");
    }
    private static async Task Restrictions()
    {
        foreach (var property in new[] { "\"requiresSubscription\":true", "\"isAccessibleForFree\":false", "\"isLiveBroadcast\":true", "\"startTime\":\"00:01:00\"" })
        {
            var html = Html(Video()[..^1] + "," + property + "}") + "<video src='https://cdn.example/preview.mp4'></video>"; var calls = new List<Uri>();
            var result = await Engine(html, calls).DownloadAsync(Request(Path.GetTempPath()), null, null, default);
            Assert(!result.Succeeded && calls.Count == 1, "An unavailable declaration fell through to another resource.");
        }
    }
    private static async Task References()
    {
        var missing = "{\"@context\":\"https://schema.org\",\"@graph\":[{\"@type\":\"WebPage\",\"mainEntity\":[{\"@id\":\"#one\"},{\"@id\":\"#missing\"}]}," + Video() + "]}";
        var info = await Engine(Html(missing), []).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Formats.Count == 0 && !info.IsPlaylist && info.Warnings.Count > 0, "An unresolved main entity was silently omitted.");
        var conflict = Html("[" + Video() + "," + Video().Replace("one.mp4", "other.mp4") + "]");
        await Expect<InvalidDataException>(() => Engine(conflict, []).InspectAsync(Request(Path.GetTempPath()), default));
        var recursive = "{\"@context\":\"https://schema.org\",\"@graph\":[{\"@type\":\"WebPage\",\"@id\":\"#page\",\"mainEntity\":{\"@id\":\"#page\"}}," + Video() + "]}";
        await Expect<InvalidDataException>(() => Engine(Html(recursive), []).InspectAsync(Request(Path.GetTempPath()), default));
    }
    private static async Task Identity()
    {
        var html = Html(Video()); var first = await Engine(html, []).InspectAsync(Request(Path.GetTempPath()), default);
        var second = await Engine(html, []).InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://different.example/story") }, default);
        Assert(first.Id != second.Id, "Unrelated publishers shared a local media identity.");
        var variants = Html(Video()[..^1] + ",\"encoding\":[{\"contentUrl\":\"https://cdn.example/one.mp4?token=A\"},{\"contentUrl\":\"https://cdn.example/one.mp4?token=a\"}]}");
        Assert((await Engine(variants, []).InspectAsync(Request(Path.GetTempPath()), default)).Formats.Count == 3, "Case-sensitive access variants were merged.");
    }
    private static async Task Limits()
    {
        var calls = new List<Uri>(); var html = Html("[" + Video() + "," + Video("two", "two.mp4") + "]");
        await Expect<InvalidDataException>(() => Engine(html, calls).InspectAsync(Request(Path.GetTempPath()) with { MaximumPlaylistEntries = 1 }, default));
        Assert(calls.Count == 1, "A bounded inventory was truncated or fetched media.");
    }
    internal static NativeEngine Engine(string html, List<Uri> calls, Func<HttpRequestMessage, HttpResponseMessage>? response = null) => new(httpFactory: _ => new MediaHttp(new Handler(request =>
    {
        calls.Add(request.RequestUri!);
        if (request.RequestUri!.AbsolutePath == "/story") return new(HttpStatusCode.OK) { Content = new StringContent(html) };
        return response?.Invoke(request) ?? throw new InvalidOperationException("Unexpected structured-media request.");
    })));
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() { var path = Path.GetFullPath(Directory); if (!path.StartsWith(Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Fixture escaped its root."); System.IO.Directory.Delete(path, true); }
    }
}
