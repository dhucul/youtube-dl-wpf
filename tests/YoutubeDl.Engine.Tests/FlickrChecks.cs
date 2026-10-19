using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Flickr;

internal static class FlickrChecks
{
    internal const string Id = "7753690742";
    internal const string Secret = "f63508bf7e";
    internal const string Query = "?s=fixture%2BA%2Fb&case=X%2BY";
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new("https://www.flickr.com/photos/gsfc/" + Id + "/"), OutputDirectory = directory, FileName = "flickr", Retries = 0 };
    internal static JsonNode State() => new JsonObject { ["main"] = new JsonObject {
        ["photo-models"] = new JsonArray(new JsonObject { ["data"] = new JsonObject { ["id"] = Id, ["mediaType"] = "video", ["secret"] = Secret, ["title"] = "Fixture title",
            ["owner"] = new JsonObject { ["data"] = new JsonObject { ["id"] = "fixture@N01" } },
            ["sizes"] = new JsonObject { ["data"] = new JsonObject { ["s"] = new JsonObject { ["data"] = new JsonObject { ["displayUrl"] = "//live.staticflickr.com/123/" + Id + "_abcdef_s.jpg", ["width"] = 160, ["height"] = 96 } } } } } }),
        ["photo-privacy-models"] = new JsonArray(new JsonObject { ["data"] = new JsonObject { ["id"] = Id, ["isPublic"] = true } }) } };
    internal static JsonNode Metadata(double duration = 2) => new JsonObject { ["stat"] = "ok", ["photo"] = new JsonObject { ["id"] = Id, ["secret"] = Secret, ["media"] = "video",
        ["visibility"] = new JsonObject { ["ispublic"] = 1 }, ["video"] = new JsonObject { ["ready"] = 1, ["failed"] = 0, ["pending"] = 0, ["duration"] = duration, ["width"] = 1920, ["height"] = 1080 },
        ["owner"] = new JsonObject { ["nsid"] = "fixture@N01", ["realname"] = "Fixture uploader" }, ["title"] = new JsonObject { ["_content"] = "Fixture API title" },
        ["description"] = new JsonObject { ["_content"] = "Fixture description" } } };
    internal static JsonNode Playback() => new JsonObject { ["stat"] = "ok", ["streams"] = new JsonObject { ["stream"] = new JsonArray(
        Stream("96p"), Stream("hls")) } };
    private static JsonNode Stream(string type) => new JsonObject { ["type"] = type, ["_content"] = "https://live.staticflickr.com/" + (type == "hls" ? "stream/" : "video/") + Id + "/" + Secret + "/" +
        (type == "hls" ? "master.m3u8" : type + ".mp4") + Query };
    internal static string Html(JsonNode state) => "<script>root.YUI_config.flickr.api.site_key = \"" + new string('a', 32) + "\";</script><script class='modelExport'>setup({modelExport: " + state.ToJsonString() + ", next: function(){throw 'never execute';}});</script>";
    internal static MediaHttp Http(JsonNode? state = null, JsonNode? metadata = null, JsonNode? playback = null, List<Uri>? calls = null,
        Func<HttpRequestMessage, HttpResponseMessage>? media = null, string? html = null) => new(new Handler(request =>
    {
        var url = request.RequestUri!; calls?.Add(url);
        if (request.Headers.Authorization is not null) throw new InvalidOperationException("Unrelated account authorization reached public Flickr discovery.");
        if (url.Host is "www.flickr.com" or "article.example") return new(HttpStatusCode.OK) { Content = new StringContent(url.Host == "article.example" ? "<iframe src='https://embedr.flickr.com/photos/" + Id + "'></iframe>" : html ?? Html(state ?? State())) };
        if (url.Host == "api.flickr.com")
        {
            var values = url.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
            if (request.Method != HttpMethod.Get || values["photo_id"] != Id || values["secret"] != Secret || values["api_key"] != new string('a', 32) || values["nojsoncallback"] != "1")
                throw new InvalidOperationException("The Flickr read-only lookup changed its offered identity/key.");
            return new(HttpStatusCode.OK) { Content = new StringContent((values["method"] == "flickr.photos.getInfo" ? metadata ?? Metadata() :
                values["method"] == "flickr.video.getStreamInfo" ? playback ?? Playback() : throw new InvalidOperationException("Unexpected Flickr API method.")).ToJsonString()) };
        }
        return media?.Invoke(request) ?? new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
    }), new() { AllowAutoRedirect = false });
    private static async Task<PageMetadata> Read(JsonNode? state = null, JsonNode? metadata = null, JsonNode? playback = null, NativeDownloadRequest? request = null, string? html = null)
    { using var http = Http(state, metadata, playback, html: html); return await new FlickrProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Flickr public video metadata binds offered streams titles duration posters and archives", Facts),
        ("Flickr public page embed mobile and base58 aliases preserve video identity", Aliases),
        ("Flickr pending failed private and still-image posts do not become playable videos", Access),
        ("Flickr page API ownership and resource secrets must agree", Binding),
        ("Flickr offered resources preserve queries and reject another video or origin", Resources),
        ("Flickr legacy numeric and named streams do not invent source dimensions", Legacy),
        ("Flickr recorded archives skip before any public-page lookup", Archive),
        ("Flickr player embeds delegate to the native provider", Embedded),
        ("Flickr public state reads bounded JSON without executing adjacent scripts", JsonOnly),
        ("Flickr API refusals stop before media transfer with bounded diagnostics", ApiFailure),
        ("Flickr playback and poster inventories reject excessive or repeated declarations", Limits)
    ];
    private static async Task Facts()
    {
        using var owner = Http(media: request => throw new InvalidOperationException("Unexpected media transfer during inspection."));
        using var authenticated = owner.WithHeaders(new Dictionary<string, string> { ["Authorization"] = "Bearer fixture-private-value" });
        var page = await new FlickrProvider().ExtractAsync(Request(Path.GetTempPath()), authenticated, default);
        Assert(page.Id == Id && page.Extractor == "flickr" && page.Title == "Fixture API title" && page.Uploader == "Fixture uploader" && page.DurationSeconds == 2 &&
            page.AnonymousPlayback && page.ArchiveIdentity?.Id == Id && page.ThumbnailUrl?.EndsWith(Id + "_abcdef_s.jpg") == true, "Flickr metadata/ownership changed.");
        Assert(page.Media.Count == 2 && page.Media.All(item => item.Url.Query == Query && item.IsCompleteMedia) && page.Media[0].Height == 96 && page.Media[0].Width is null &&
            page.Media[1].Height is null, "Per-format dimensions, literal queries or complete roles changed.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "https://flickr.com/photos/gsfc/" + Id, "https://m.flickr.com/photos/gsfc/" + Id + "/in/album-123/", "https://embedr.flickr.com/photos/" + Id,
            "https://www.flickr.com/photo.gne?id=" + Id }) Assert((await Read(request: Request(Path.GetTempPath()) with { Url = new(url) })).Id == Id, "Flickr alias lost identity.");
        foreach (var vector in new[] { ("2", "1"), ("Z", "57"), ("21", "58") }) Assert(FlickrAddress.Parse(new("https://flic.kr/p/" + vector.Item1)).Id == vector.Item2, "Flickr documented base58 mapping changed.");
        await Reject<NotSupportedException>(() => Read(request: Request(Path.GetTempPath()) with { Url = new("https://www.flickr.com/photos/gsfc/albums/123/") }));
        foreach (var bad in new[] { "https://flic.kr/p/0", "https://flic.kr/p/ZZZZZZZZZZZZZZZZ", "http://flickr.com/photos/gsfc/" + Id,
            "https://flickr.com:8443/photos/gsfc/" + Id, "https://www.flickr.com/photo.gne?id=" + Id + "&id=" + Id })
            await Reject<NotSupportedException>(() => Read(request: Request(Path.GetTempPath()) with { Url = new(bad) }));
    }
    private static async Task Access()
    {
        foreach (var field in new[] { "failed", "pending", "ready" }) { var api = Metadata(); api["photo"]!["video"]![field] = field == "ready" ? 0 : 1; await Reject<NotSupportedException>(() => Read(metadata: api)); }
        var state = State(); state["main"]!["photo-privacy-models"]![0]!["data"]!["isPublic"] = false; await Reject<NotSupportedException>(() => Read(state));
        state = State(); state["main"]!["photo-models"]![0]!["data"]!["mediaType"] = "photo"; await Reject<NotSupportedException>(() => Read(state));
        var metadata = Metadata(); metadata["photo"]!["visibility"]!["ispublic"] = 0; await Reject<NotSupportedException>(() => Read(metadata: metadata));
    }
    private static async Task Binding()
    {
        var state = State(); state["main"]!["photo-models"]![0]!["data"]!["id"] = "2"; await Reject<InvalidDataException>(() => Read(state));
        foreach (var field in new[] { "id", "secret" }) { var api = Metadata(); api["photo"]![field] = "wrong"; await Reject<InvalidDataException>(() => Read(metadata: api)); }
        var data = Metadata(); data["photo"]!["owner"]!["nsid"] = "other@N01"; await Reject<InvalidDataException>(() => Read(metadata: data));
    }
    private static async Task Resources()
    {
        foreach (var url in new[] { "https://other.example/video/" + Id + "/" + Secret + "/96p.mp4", "https://live.staticflickr.com/video/2/" + Secret + "/96p.mp4",
            "https://live.staticflickr.com/video/" + Id + "/deadbeef/96p.mp4", "https://live.staticflickr.com:8443/video/" + Id + "/" + Secret + "/96p.mp4" })
        { var playback = Playback(); playback["streams"]!["stream"]![0]!["_content"] = url; await Reject<InvalidDataException>(() => Read(playback: playback)); }
    }
    private static async Task Legacy()
    {
        var data = Playback(); data["streams"]!["stream"] = new JsonArray(Stream("700"), Stream("appletv")); data["streams"]!["stream"]![0]!["type"] = 700;
        var page = await Read(playback: data); Assert(page.Media.Select(item => item.Id).SequenceEqual(new[] { "700", "appletv" }) && page.Media.All(item => item.Width is null && item.Height is null),
            "Legacy stream labels were mistaken for dimensions or lost.");
    }
    private static async Task Archive()
    {
        var directory = Path.Combine(Path.GetTempPath(), "native-flickr-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try { var archive = Path.Combine(directory, "archive.txt"); await File.WriteAllTextAsync(archive, "flickr " + Id + "\n"); var calls = new List<Uri>();
            var engine = new NativeEngine([new FlickrProvider()], _ => Http(calls: calls)); var result = await engine.DownloadAsync(Request(directory) with { DownloadArchiveFile = archive }, null, null, default);
            Assert(result.Succeeded && result.Skipped && calls.Count == 0, "Flickr archive skip requested its page or media."); }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task Embedded()
    {
        var engine = new NativeEngine(httpFactory: _ => Http()); var info = await engine.InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://article.example/video") }, default);
        Assert(info.Extractor == "flickr" && info.Id == Id, "Flickr embed was treated as generic page media.");
    }
    private static async Task JsonOnly()
    {
        var state = State(); state["main"]!["photo-models"]![0]!["data"]!["description"] = "Text containing } and escaped \" quotes";
        Assert((await Read(state)).Id == Id, "JSON string punctuation escaped the bounded model reader.");
        await Reject<NotSupportedException>(() => Read(html: "<template>" + Html(State()) + "</template>"));
        await Reject<NotSupportedException>(() => Read(html: Html(State()) + Html(State())));
        await Reject<InvalidDataException>(() => Read(html: Html(State()).Replace("modelExport: {", "modelExport: {bad()", StringComparison.Ordinal)));
    }
    private static async Task ApiFailure()
    {
        var refused = new JsonObject { ["stat"] = "fail", ["code"] = 100, ["message"] = "untrusted credential " + Secret };
        try { await Read(metadata: refused); throw new InvalidOperationException("Flickr accepted an API denial."); }
        catch (NotSupportedException ex) { Assert(ex.Message.Contains("100") && !ex.Message.Contains(Secret), "Flickr printed untrusted API diagnostics or lost the error code."); }
        await Reject<NotSupportedException>(() => Read(playback: refused));
    }
    private static async Task Limits()
    {
        var data = Playback(); data["streams"]!["stream"] = new JsonArray(Stream("96p"), Stream("96p")); await Reject<InvalidDataException>(() => Read(playback: data));
        data = Playback(); data["streams"]!["stream"] = new JsonArray(Enumerable.Range(1, 65).Select(index => Stream(index + "p")).ToArray()); await Reject<InvalidDataException>(() => Read(playback: data));
        var state = State(); var sizes = (JsonObject)state["main"]!["photo-models"]![0]!["data"]!["sizes"]!["data"]!;
        for (var index = 0; index < 256; index++) sizes["size" + index] = sizes["s"]!.DeepClone(); await Reject<InvalidDataException>(() => Read(state));
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(run(request)); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Reject<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
