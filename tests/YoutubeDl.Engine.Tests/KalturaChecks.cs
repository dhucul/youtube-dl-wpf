using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Kaltura;

internal static class KalturaChecks
{
    private const string Player = "https://cdnapisec.kaltura.com/p/309/embedPlaykitJs?entry_id=1_fixture";
    private const string Media = """
        {"objectType":"KalturaMediaEntry","id":"1_fixture","partnerId":309,"mediaType":1,"name":"Fixture film","description":"Fixture description","msDuration":2500,"duration":3,"creatorId":"Creator","thumbnailUrl":"https://cdn.example/thumb.jpg"}
        """;
    private const string Context = """
        {"objectType":"KalturaPlaybackContext","actions":[],"sources":[
        {"format":"url","flavorIds":"1_low,1_high","url":"https://cdn.example/default.mov","drm":[]},
        {"format":"applehttp","deliveryProfileId":"20","flavorIds":"1_low,1_high","url":"https://cdn.example/master.m3u8","drm":[]},
        {"format":"mpegdash","deliveryProfileId":"30","flavorIds":"1_high","url":"https://cdn.example/protected.mpd","drm":[{"scheme":"widevine"}]}],
        "flavorAssets":[
        {"objectType":"KalturaFlavorAsset","id":"1_low","entryId":"1_fixture","partnerId":309,"status":2,"fileExt":"mp4","width":640,"height":360,"frameRate":24,"bitrate":500,"size":10,"sizeInBytes":"13","videoCodecId":"avc1"},
        {"objectType":"KalturaFlavorAsset","id":"1_high","entryId":"1_fixture","partnerId":309,"status":2,"fileExt":"mp4","width":1280,"height":720,"frameRate":30,"bitrate":1000,"size":20,"videoCodecId":"avc1"}],
        "playbackCaptions":[{"languageCode":"en","webVttUrl":"https://cdn.example/captions.vtt"}]}
        """;
    private static readonly Dictionary<string, string> Urls = new() { ["1_low"] = "https://cdn.example/low.mp4?ticket=literal%2Bvalue", ["1_high"] = "https://cdn.example/high.mp4" };
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Kaltura legacy and current player URLs retain identities and supplied sessions", () => Sync(Addresses)),
        ("Kaltura flavors preserve quality facts and exact sizes without rounding kilobytes", () => Sync(Metadata)),
        ("Kaltura protected sources and playback restrictions cannot expose direct formats", () => Sync(Restrictions)),
        ("Kaltura mismatched identities and invalid resources fail explicitly", () => Sync(Invalid)),
        ("Native Kaltura guest sessions resolve and download selected media with captions", Download),
        ("Supplied Kaltura sessions stay scoped to API bodies", Session),
        ("Kaltura API errors and restrictions stop flavor requests", Errors),
        ("Kaltura iframe, autoembed and flat legacy widgets resolve native media", () => Sync(Embeds)),
        ("Embedded Kaltura access checks use the actual page after redirects", Referrer),
        ("Canceled Kaltura discovery stops before the next API request", Cancellation)
    ];
    private static void Addresses()
    {
        foreach (var url in new[] { Player, "https://www.kaltura.com/index.php/kwidget/wid/_309/uiconf_id/123/entry_id/1_fixture",
            "https://cdnapi.kaltura.com/p/309/sp/30900/embedIframeJs/uiconf_id/123/partner_id/309?entry_id=1_fixture" })
        { var value = KalturaAddress.Parse(new Uri(url)); Assert(value.PartnerId == 309 && value.EntryId == "1_fixture", "Player identity changed."); }
        Assert(KalturaAddress.Parse(new Uri(Player + "&ks=abc%2Bdef")).Session == "abc+def", "Supplied session was double decoded.");
        Assert(KalturaAddress.Parse(KalturaAddress.Parse(new Uri(Player + "&ks=abc%2Bdef")).PlayerUrl).Session == "abc+def", "Canonical player URL discarded access context.");
        foreach (var url in new[] { Player.Replace("cdnapisec.kaltura.com", "cdnapisec.kaltura.com.evil.example"), Player + "&partner_id=100", Player + "&entryId=1_other", Player.Replace("/309/", "/0/"), Player + "&ks=a&ks=b" })
            Throws<ArgumentException>(() => KalturaAddress.Parse(new Uri(url)));
        Throws<NotSupportedException>(() => KalturaAddress.Parse(new Uri(Player + "&playlist_id=1_list")));
        Assert(!KalturaAddress.IsPlayer(new Uri("https://cdnapisec.kaltura.com/p/309/playManifest/video.mp4")), "Direct media was intercepted as a player.");
    }
    private static PageMetadata Parse(string? media = null, string? context = null, IReadOnlyDictionary<string,string>? urls = null) => KalturaProvider.ParsePlayback(media ?? Media, context ?? Context, KalturaAddress.Parse(new Uri(Player)), urls ?? Urls);
    private static void Metadata()
    {
        var page = Parse(); var low = page.Media.Single(item => item.Id == "1_low");
        Assert(page.Id == "1_fixture" && page.Title == "Fixture film" && page.DurationSeconds == 2.5 && page.Uploader == "Creator" && page.Subtitles.Single().Language == "en", "Metadata or captions were lost.");
        Assert(low.Width == 640 && low.Height == 360 && low.FramesPerSecond == 24 && low.Bandwidth == 500000 && low.FileSize == 13 && low.VideoCodec == "avc1", "Flavor facts changed.");
        Assert(page.Media.Single(item => item.Id == "1_high").FileSize is null && low.Url.Query.Contains("literal%2Bvalue"), "Rounded size or signed URL was changed.");
        Assert(page.Media.Count == 3 && page.Media.Any(item => item.Kind == MediaKind.Hls) && page.Media.All(item => item.Kind != MediaKind.Dash), "Protected or unsupported sources were exposed.");
        var numericProfile = Parse(context: Context.Replace("\"deliveryProfileId\":\"20\"", "\"deliveryProfileId\":20").Replace("\"size\":20", "\"size\":20,\"sizeInBytes\":0"));
        Assert(numericProfile.Media.Any(item => item.Id == "hls-20") && numericProfile.Media.Single(item => item.Id == "1_high").FileSize is null, "API default zero sizes or numeric profile identifiers were misread.");
        var unknown = Parse(media: Media.Replace("\"msDuration\":2500", "\"msDuration\":0"), context: Context.Replace("\"frameRate\":24", "\"frameRate\":0"));
        Assert(unknown.DurationSeconds == 3 && unknown.Media.Single(item => item.Id == "1_low").FramesPerSecond is null, "Unknown zero values were treated as measured duration or frame rate.");
    }
    private static void Restrictions()
    {
        Throws<NotSupportedException>(() => Parse(context: Context.Replace("\"actions\":[]", "\"actions\":[{\"objectType\":\"KalturaAccessControlBlockAction\"}]")));
        var restricted = JsonNode.Parse(Context)!;
        restricted["sources"]![0]!["drm"] = new JsonArray(new JsonObject { ["scheme"] = "widevine" });
        Throws<InvalidDataException>(() => Parse(context: restricted.ToJsonString()));
        var page = Parse(context: restricted.ToJsonString(), urls: new Dictionary<string,string>());
        Assert(page.Media.Count == 1 && page.Media[0].Kind == MediaKind.Hls, "A protected direct flavor was exposed.");
        Throws<InvalidDataException>(() => Parse(context: Context.Replace("\"drm\":[]", "\"drm\":{}")));
        Throws<InvalidDataException>(() => Parse(context: Context.Replace("\"actions\":[]", "\"actions\":{}")));
    }
    private static void Invalid()
    {
        Throws<InvalidDataException>(() => Parse(media: Media.Replace("309", "310")));
        Throws<InvalidDataException>(() => Parse(context: Context.Replace("\"entryId\":\"1_fixture\"", "\"entryId\":\"1_other\"")));
        Throws<InvalidDataException>(() => Parse(context: Context.Replace("1_high", "1_low")));
        Throws<InvalidDataException>(() => Parse(urls: new Dictionary<string,string> { ["1_low"] = "file:///private.mp4" }));
        Throws<NotSupportedException>(() => Parse(media: Media.Replace("\"mediaType\":1", "\"mediaType\":201")));
        Throws<InvalidOperationException>(() => Parse(media: "{\"objectType\":\"KalturaAPIException\",\"message\":\"denied\"}"));
    }
    private static async Task Download()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "test-data", "kaltura-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var calls = new List<string>();
        try
        {
            var engine = Engine(calls);
            var result = await engine.DownloadAsync(Request(folder) with { FormatId = "1_low", WriteSubs = true, SubLanguages = "en", VideoCodec = "h264" }, null, null, default);
            Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == "fixture media" && Directory.GetFiles(folder, "*.en.vtt").Length == 1, "The native Kaltura media/caption flow did not complete.");
            Assert(calls.Count(value => value == "startWidgetSession") == 1 && calls.Count(value => value == "getUrl") == 2 && calls.Contains("media:low.mp4"), "The discovery/selection flow changed.");
        }
        finally { Directory.Delete(folder, true); }
    }
    private static async Task Session()
    {
        var calls = new List<string>();
        var info = await Engine(calls, expectedSession: "supplied+session").InspectAsync(Request(Path.GetTempPath()) with { Url = new Uri(Player + "&ks=supplied%2Bsession") }, default);
        Assert(info.Formats.Count == 3 && !calls.Contains("startWidgetSession"), "A supplied session was replaced with a guest session.");
    }
    private static async Task Errors()
    {
        var calls = new List<string>();
        await Expect<NotSupportedException>(() => Engine(calls, context: Context.Replace("\"actions\":[]", "\"actions\":[{\"type\":1}]")).InspectAsync(Request(Path.GetTempPath()), default));
        Assert(!calls.Contains("getUrl"), "Restricted media reached flavor URL resolution.");
        calls.Clear();
        await Expect<InvalidOperationException>(() => Engine(calls, context: "{\"objectType\":\"KalturaAPIException\"}").InspectAsync(Request(Path.GetTempPath()), default));
        Assert(!calls.Contains("getUrl"), "An API error reached media resolution.");
    }
    private static void Embeds()
    {
        foreach (var html in new[] { "<iframe src='" + Player + "'></iframe>", "<script src='" + Player + "&amp;autoembed=true'></script>", "<script>kWidget.embed({wid:'_309',entry_id:'1_fixture',targetId:'player'});</script>" })
        {
            var urls = EmbeddedMediaReader.Read(html, new Uri("https://article.example/"));
            Assert(urls.Count == 1 && KalturaAddress.Parse(urls[0]).EntryId == "1_fixture", "An embedded Kaltura player was lost.");
        }
        Assert(EmbeddedMediaReader.Read("<script src='https://cdnapisec.kaltura.com/p/309/embedPlaykitJs/uiconf_id/123'></script>", new Uri("https://article.example/")).Count == 0, "A library script without an entry was treated as media.");
        foreach (var config in new[] { "wid:'_309', entry_id:'1_first', entry_id:'1_other'", "wid:'_309', description:\"hello, entry_id:'1_wrong'\"", "wid:widgetVariable, entry_id:'1_wrong'" })
            Assert(EmbeddedMediaReader.Read("<script>kWidget.embed({" + config + "});</script>", new Uri("https://article.example/")).Count == 0, "Ambiguous, quoted or computed widget text selected the wrong video.");
    }
    private static async Task Referrer()
    {
        var calls = new List<string>();
        var info = await Engine(calls, expectedReferer: "https://final.example/article").InspectAsync(Request(Path.GetTempPath()) with { Url = new Uri("https://article.example/"), Referer = "" }, default);
        Assert(info.Extractor == "kaltura" && info.Formats.Count == 3, "The embedded player did not resolve.");
        await Engine([], expectedReferer: "https://explicit.example/").InspectAsync(Request(Path.GetTempPath()) with { Url = new Uri("https://article.example/"), Referer = "https://explicit.example/" }, default);
    }
    private static async Task Cancellation()
    {
        using var cancel = new CancellationTokenSource();
        using var http = new MediaHttp(new Handler(request =>
        {
            Assert(request.RequestUri!.AbsolutePath.EndsWith("startWidgetSession"), "Cancellation allowed another API call."); cancel.Cancel(); return Ok("{\"partnerId\":309,\"ks\":\"guest-session\"}");
        }));
        await Expect<OperationCanceledException>(() => new KalturaProvider().ExtractAsync(Request(Path.GetTempPath()), http, cancel.Token));
    }
    private static NativeEngine Engine(List<string> calls, string? context = null, string expectedSession = "guest-session", string? expectedReferer = null) => new(httpFactory: _ => new MediaHttp(new Handler(request =>
    {
        var url = request.RequestUri!;
        if (url.Host == "article.example") { var response = Ok("<iframe src='" + Player + "'></iframe>"); response.RequestMessage = new(HttpMethod.Get, "https://final.example/article"); return response; }
        if (url.Host == "cdn.example")
        {
            Assert(request.Method == HttpMethod.Get && request.Content is null && !request.Headers.Contains("ks"), "Session credentials leaked into media headers or bodies.");
            calls.Add("media:" + Path.GetFileName(url.AbsolutePath)); return Ok(url.AbsolutePath.EndsWith(".vtt") ? "WEBVTT\n\n00:00.000 --> 00:01.000\nFixture\n" : "fixture media");
        }
        Assert(url.Host == "www.kaltura.com" && request.Method == HttpMethod.Post, "The API address changed.");
        Assert(request.Content!.Headers.ContentType?.MediaType == "application/json" && request.Content.Headers.ContentType.CharSet is null, "Kaltura requires the bare JSON content type to read the request body.");
        var action = url.Segments.Last(); calls.Add(action);
        using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()); var root = body.RootElement;
        if (action == "startWidgetSession") return Ok("{\"partnerId\":309,\"ks\":\"guest-session\"}");
        Assert(root.GetProperty("ks").GetString() == expectedSession, "Session credentials were changed.");
        if (action == "get") return Ok(Media);
        if (action == "getPlaybackContext")
        {
            if (expectedReferer is not null) Assert(root.GetProperty("contextDataParams").GetProperty("referrer").GetString() == expectedReferer, "Access checks lost the embedding referrer.");
            return Ok(context ?? Context);
        }
        if (action == "getUrl") return Ok(JsonSerializer.Serialize(Urls[root.GetProperty("id").GetString()!]));
        throw new InvalidOperationException("Unexpected Kaltura API call.");
    }),new(){AllowAutoRedirect=false}));
    private static NativeDownloadRequest Request(string folder) => new() { Url = new Uri(Player), OutputDirectory = folder, Retries = 0 };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(response(request)); } }
    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
