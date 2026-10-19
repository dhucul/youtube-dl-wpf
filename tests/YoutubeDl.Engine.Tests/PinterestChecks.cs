using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.Pinterest;

internal static class PinterestChecks
{
    internal const string Id = "871728071625487182";
    internal const string Asset = "604447f5803a65f4ff20f13a17b97fa7";
    internal const string Other = "604447f5803a65f4ff20f13a17b97fa8";
    internal const string Query = "?signature=A%2Bb%2Fc&case=D%2BE";
    internal static string VideoPrefix => "https://v1.pinimg.com/videos/iht/";
    internal static string Resource(string family, string asset = Asset) => VideoPrefix + family + "/60/44/47/" + asset + (family == "hls" ? ".m3u8" : "_720w.mp4") + Query;
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new("https://www.pinterest.com/pin/" + Id + "/"), OutputDirectory = directory, FileName = "pinterest", Retries = 0 };
    internal static JsonNode Pin(double duration = 2) => new JsonObject { ["entityId"] = Id, ["id"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("Pin:" + Id)), ["__typename"] = "Pin", ["isHidden"] = false,
        ["title"] = "Fixture pin title", ["description"] = "Fixture description", ["nativeCreator"] = new JsonObject { ["fullName"] = "Fixture creator" },
        ["videos"] = new JsonObject { ["entityId"] = "5419948631190992482", ["duration"] = duration * 1000,
            ["videoList"] = new JsonObject { ["__typename"] = "VideoList", ["v720P"] = Format(false, duration), ["vHLSV4"] = Format(true, duration) },
            ["videoListForCaptions"] = new JsonObject { ["v720P"] = new JsonObject { ["captionsUrls"] = new JsonObject { ["en-us"] = "https://v1.pinimg.com/videos/captions/en-us/60/44/47/" + Asset + ".vtt" + Query } },
                ["vHLSV4"] = new JsonObject { ["captionsUrls"] = new JsonObject { ["en-us"] = "https://v1.pinimg.com/videos/captions/en-us/60/44/47/" + Asset + ".vtt" + Query } } } } };
    private static JsonNode Format(bool hls, double duration) => new JsonObject { ["width"] = 160, ["height"] = 96, ["duration"] = duration * 1000,
        ["url"] = Resource(hls ? "hls" : "expMp4"), ["thumbnail"] = "https://i.pinimg.com/videos/thumbnails/originals/60/44/47/" + Asset + ".0000000.jpg" };
    internal static string Html(params JsonNode[] pins) => string.Join('\n', pins.Select(pin => Declaration(pin)));
    internal static string Declaration(JsonNode pin, string queryId = Id, bool? final = true)
    {
        var query = new JsonObject { ["queryID"] = "fixture-query", ["variables"] = new JsonObject { ["pinId"] = queryId, ["isAuth"] = false } };
        var response = new JsonObject { ["data"] = new JsonObject { ["v3GetPinQueryv2"] = new JsonObject { ["data"] = pin.DeepClone() } } };
        if (final is { } value) response["extensions"] = new JsonObject { ["is_final"] = value };
        return "<script data-relay-completed-request=\"true\">window.__PWS_RELAY_REGISTER_COMPLETED_REQUEST__(" +
            JsonSerializer.Serialize(Uri.EscapeDataString(query.ToJsonString())) + "," + response.ToJsonString() + ");</script>";
    }
    internal static MediaHttp Http(string? html = null, List<Uri>? calls = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null, Uri? finalPage = null) => new(new Handler(request =>
    {
        var url = request.RequestUri!; calls?.Add(url);
        if (request.Headers.Authorization is not null) throw new InvalidOperationException("Unrelated account authorization reached public Pinterest requests.");
        if (url.Host == "article.example") return new(HttpStatusCode.OK) { Content = new StringContent("<iframe src='https://www.pinterest.com/pin/" + Id + "/'></iframe>") };
        if (PinterestAddress.Matches(url)) return new(HttpStatusCode.OK) { Content = new StringContent(html ?? Html(Pin())), RequestMessage = new(HttpMethod.Get, finalPage ?? Request(Path.GetTempPath()).Url) };
        return media?.Invoke(request) ?? new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
    }), new() { AllowAutoRedirect = false });
    private static async Task<PageMetadata> Read(JsonNode? pin = null, NativeDownloadRequest? request = null, string? html = null, Uri? finalPage = null)
    { using var http = Http(html ?? Html(pin ?? Pin()), finalPage: finalPage); return await new PinterestProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Pinterest completed public records preserve hosted video metadata formats queries and archive identity", Facts),
        ("Pinterest streamed partial pin records combine only consistent requested metadata", Partial),
        ("Pinterest regional slug embed and short aliases resolve the same pin", Aliases),
        ("Pinterest official anchor widgets delegate only declared pin embeds", Widgets),
        ("Pinterest redirects native global and query identities remain bound", Binding),
        ("Pinterest conflicting streamed declarations do not silently select another media asset", Conflicts),
        ("Pinterest literal relay JSON cannot execute surrounding or injected script arguments", JsonOnly),
        ("Pinterest hidden incomplete and multi-page inventories remain explicit unsupported layouts", Access),
        ("Pinterest offered video origins assets shards durations and dimensions must agree", Resources),
        ("Pinterest automatic captions deduplicate by language and bind to their declared video", Captions),
        ("Pinterest posters remain bound to the offered video and declared image origin", Posters),
        ("Pinterest external player pins delegate to the original native provider identity", External),
        ("Pinterest image-only pins retain metadata without inventing playable media", Images),
        ("Pinterest archived hosted videos skip media and related-file requests", Archive),
        ("Pinterest excessive formats and incomplete or missing requested responses fail explicitly", Limits)
    ];
    private static async Task Facts()
    {
        using var owner = Http(); using var auth = owner.WithHeaders(new Dictionary<string, string> { ["Authorization"] = "Bearer unrelated-fixture" });
        var page = await new PinterestProvider().ExtractAsync(Request(Path.GetTempPath()), auth, default);
        Assert(page.Id == Id && page.Title == "Fixture pin title" && page.Uploader == "Fixture creator" && page.DurationSeconds == 2 && page.AnonymousPlayback && page.ArchiveIdentity?.Id == Id,
            "Pinterest metadata, anonymous context or archive identity changed.");
        Assert(page.Media.Count == 2 && page.Media.All(item => item.Url.Query == Query && item.IsCompleteMedia) && page.Media[0].Height == 96 && page.Media[1].Height is null,
            "Pinterest selected dimensions, offered queries or complete roles changed.");
    }
    private static async Task Partial()
    {
        var first = Pin(); ((JsonObject)first["videos"]!["videoList"]!).Remove("vHLSV4");
        var second = Pin(); ((JsonObject)second["videos"]!["videoList"]!).Remove("v720P"); ((JsonObject)second).Remove("title");
        var page = await Read(html: Declaration(second, final: null) + Declaration(first));
        Assert(page.Media.Count == 2 && page.Title == "Fixture pin title" && page.Subtitles.Count == 1, "Streamed response order lost formats or title/captions.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "https://br.pinterest.com/pin/" + Id + "/", "https://www.pinterest.ca/pin/fixture-title--" + Id + "/", "https://assets.pinterest.com/ext/embed.html?id=" + Id,
            "https://pin.it/fixtureAlias" }) Assert((await Read(request: Request(Path.GetTempPath()) with { Url = new(url) })).Id == Id, "A Pinterest alias changed its post identity.");
        foreach (var url in new[] { "https://pinterest.com/user/_saved/", "https://pinterest.com.evil.example/pin/" + Id, "http://pinterest.com/pin/" + Id,
            "https://pinterest.com:8443/pin/" + Id, "https://assets.pinterest.com/ext/embed.html?id=" + Id + "&id=2" })
            await Reject<NotSupportedException>(() => Read(request: Request(Path.GetTempPath()) with { Url = new(url) }));
        var engine = new NativeEngine(httpFactory: _ => Http()); var info = await engine.InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://article.example/pin") }, default);
        Assert(info.Extractor == "pinterest" && info.Id == Id, "Pinterest iframe did not delegate to native extraction.");
    }
    private static async Task Binding()
    {
        var pin = Pin(); pin["entityId"] = "2"; await Reject<InvalidDataException>(() => Read(pin));
        pin = Pin(); pin["id"] = "UGluOjI="; await Reject<InvalidDataException>(() => Read(pin));
        await Reject<NotSupportedException>(() => Read(html: Declaration(Pin(), queryId: "2")));
        await Reject<InvalidDataException>(() => Read(finalPage: new("https://www.pinterest.com/pin/2/")));
    }
    private static async Task Widgets()
    {
        var html = "<a data-pin-do='embedPin' href='https://www.pinterest.com/pin/" + Id + "/'></a>" +
            "<a href='https://www.pinterest.com/pin/2/'></a><a data-pin-do='embedBoard' href='https://www.pinterest.com/user/board/?sort=unsupported'></a>";
        var embeds = EmbeddedMediaReader.Read(html, new("https://article.example/video"));
        Assert(embeds.Count == 1 && embeds[0] == Request(Path.GetTempPath()).Url, "Pinterest widget parsing followed ordinary links or a board as playable pin media.");
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request => new(HttpStatusCode.OK) {
            Content = new StringContent(request.RequestUri!.Host == "article.example" ? html : Html(Pin())) })));
        var info = await engine.InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://article.example/video") }, default);
        Assert(info.Id == Id && info.Extractor == "pinterest", "The official anchor widget did not reach native pin extraction.");
    }
    private static async Task Conflicts()
    {
        var changed = Pin(); changed["videos"]!["videoList"]!["v720P"]!["url"] = Resource("expMp4", Other);
        await Reject<InvalidDataException>(() => Read(html: Html(Pin(), changed)));
        changed = Pin(); changed["title"] = "Conflicting title"; await Reject<InvalidDataException>(() => Read(html: Html(Pin(), changed)));
    }
    private static async Task JsonOnly()
    {
        var pin = Pin(); pin["description"] = "Literal } punctuation and \"quoted\" text";
        Assert((await Read(pin)).Description == pin["description"]!.GetValue<string>(), "Literal JSON text escaped its data boundary.");
        await Reject<NotSupportedException>(() => Read(html: "<template>" + Html(Pin()) + "</template>"));
        await Reject<InvalidDataException>(() => Read(html: Html(Pin()).Replace(");</script>", ", execute());</script>", StringComparison.Ordinal)));
        await Reject<InvalidDataException>(() => Read(html: Html(Pin()).Replace("\"data\":", "execute():", StringComparison.Ordinal)));
    }
    private static async Task Access()
    {
        var pin = Pin(); pin["isHidden"] = true; await Reject<NotSupportedException>(() => Read(pin));
        pin = Pin(); ((JsonObject)pin).Remove("isHidden"); await Reject<NotSupportedException>(() => Read(pin));
        foreach (var field in new[] { "storyPinData", "carouselData" }) { pin = Pin(); pin[field] = new JsonObject { ["pages"] = new JsonArray(new JsonObject()) }; await Reject<NotSupportedException>(() => Read(pin)); }
        var first = Pin(); first["storyPinData"] = new JsonObject { ["pages"] = new JsonArray(new JsonObject { ["pageId"] = "page-one", ["blocks"] = new JsonArray(new JsonObject { ["type"] = "video" }) }) };
        var second = Pin(); second["storyPinData"] = new JsonObject { ["pages"] = new JsonArray(new JsonObject { ["blocks"] = new JsonArray(new JsonObject { ["layout"] = "partial" }) }) };
        await Reject<NotSupportedException>(() => Read(html: Html(first, second)));
        await Reject<NotSupportedException>(() => Read(html: Declaration(Pin(), final: false)));
    }
    private static async Task Resources()
    {
        foreach (var url in new[] { Resource("expMp4", Other), Resource("expMp4").Replace("v1.pinimg.com", "evil.example"), Resource("expMp4").Replace("/60/", "/61/"),
            Resource("expMp4").Replace("v1.pinimg.com", "v1.pinimg.com:8443") })
        { var pin = Pin(); pin["videos"]!["videoList"]!["v720P"]!["url"] = url; await Reject<InvalidDataException>(() => Read(pin)); }
        var changed = Pin(); changed["videos"]!["videoList"]!["v720P"]!["duration"] = 3000; await Reject<InvalidDataException>(() => Read(changed));
        changed = Pin(); changed["videos"]!["videoList"]!["v720P"]!["height"] = 0; await Reject<InvalidDataException>(() => Read(changed));
    }
    private static async Task Captions()
    {
        var page = await Read(); Assert(page.Subtitles.Count == 1 && page.Subtitles[0].Automatic && page.Subtitles[0].Language == "en-us" && page.Subtitles[0].Url.Query == Query,
            "Pinterest captions lost provenance, language, literal queries or deduplication.");
        var pin = Pin(); pin["videos"]!["videoListForCaptions"]!["v720P"]!["captionsUrls"]!["en-us"] = "https://v1.pinimg.com/videos/captions/en-us/60/44/47/" + Other + ".vtt";
        await Reject<InvalidDataException>(() => Read(pin));
        pin = Pin(); pin["videos"]!["videoListForCaptions"]!["vHLSV4"]!["captionsUrls"]!["en-us"] = "https://v1.pinimg.com/videos/captions/en-us/60/44/47/" + Asset + ".vtt?different=1";
        await Reject<InvalidDataException>(() => Read(pin));
    }
    private static async Task Posters()
    {
        var page = await Read(); Assert(page.Thumbnails.Count == 2 && page.ThumbnailUrl?.Contains(Asset) == true, "Pinterest omitted its declared video poster.");
        var pin = Pin(); pin["videos"]!["videoList"]!["v720P"]!["thumbnail"] = "https://i.pinimg.com/videos/thumbnails/originals/60/44/47/" + Other + ".jpg";
        await Reject<InvalidDataException>(() => Read(pin));
    }
    private static async Task External()
    {
        var pin = Pin(); pin["videos"] = null; pin["embed"] = new JsonObject { ["src"] = "http://www.youtube.com/embed/jNQXAC9IVRw" };
        var provider = new EmbeddedProvider(); var engine = new NativeEngine([new PinterestProvider(), provider], _ => Http(Html(pin)));
        var info = await engine.InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Extractor == "youtube" && info.Id == "jNQXAC9IVRw" && provider.Referer == Request(Path.GetTempPath()).Url.AbsoluteUri, "External pin invented a Pinterest media identity or lost its actual referrer.");
    }
    private static async Task Images()
    { var pin = Pin(); pin["videos"] = null; var page = await Read(pin); Assert(page.Media.Count == 0 && page.EmbeddedPages.Count == 0 && page.Id == Id && page.Warnings.Count > 0, "An image-only pin became playable video."); }
    private static async Task Archive()
    {
        var directory = Path.Combine(Path.GetTempPath(), "native-pinterest-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try { var archive = Path.Combine(directory, "archive.txt"); await File.WriteAllTextAsync(archive, "pinterest " + Id + "\n"); var calls = new List<Uri>();
            var engine = new NativeEngine([new PinterestProvider()], _ => Http(calls: calls)); var result = await engine.DownloadAsync(Request(directory) with { DownloadArchiveFile = archive }, null, null, default);
            Assert(result.Succeeded && result.Skipped && calls.Count == 1 && calls[0].Host == "www.pinterest.com", "An archived pin fetched media/captions/posters."); }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task Limits()
    {
        var pin = Pin(); var formats = (JsonObject)pin["videos"]!["videoList"]!; for (var index = 0; index < 64; index++) formats["format" + index] = Format(false, 2);
        await Reject<InvalidDataException>(() => Read(pin));
        await Reject<NotSupportedException>(() => Read(html: "<script>window.notACompletedRequest = {};</script>"));
    }
    private sealed class EmbeddedProvider : IMediaProvider
    {
        public string? Referer { get; private set; }
        public MediaProviderInfo Info { get; } = new("youtube", "Fixture original player", 200);
        public bool CanHandle(Uri url) => url.Host == "www.youtube.com";
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
        { Referer = request.Referer; return Task.FromResult(new PageMetadata { Id = "jNQXAC9IVRw", Extractor = "youtube", Title = "Original video", Media = [new() { Url = new("https://media.example/video.mp4"), Kind = MediaKind.Progressive, Extension = "mp4", HasVideo = true }] }); }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(run(request)); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Reject<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
