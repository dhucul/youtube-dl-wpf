using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Rumble;

internal static class RumbleChecks
{
    internal const string Id = "v6za8wa";
    internal const string WatchUrl = "https://rumble.com/v71gwwi-public-film.html";
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new("https://rumble.com/embed/" + Id + "/"), OutputDirectory = directory, FileName = "rumble", Retries = 0 };
    internal static string WatchPage => """
        <meta property=og:url content=https://rumble.com/v71gwwi-public-film.html>
        <meta name=description content='Film &amp; description'>
        <div hidden>Premium Only Content</div>
        <script>Rumble("play", {"video":"v6za8wa","div":"videoPlayer","opts":["skip_ga_load"],"api":function(api){ throw new Error('never execute site code'); }});</script>
        """;
    internal static JsonNode Playback() => JsonNode.Parse("""
        {"vid":422061706,"loaded":1,"live":0,"live_placeholder":false,"restrict":[-3,0],"timeline":[0,0],"title":"Public &amp; film","duration":2,
         "author":{"name":"Creator","url":"https://rumble.com/user/creator"},"i":"https://test.cdn.rumble.cloud/cover.jpg","l":"/v71gwwi-public-film.html","fps":60,"w":1920,"h":1080,"cc":[],
         "ua":{"mp4":{"360":{"url":"https://test.cdn.rumble.cloud/video.mp4?token=a%2Bb%2Fc&x=1&x=2","meta":{"w":128,"h":96,"fps":24,"bitrate":300,"size":1000}}},
         "hls":{"auto":{"url":"https://rumble.com/hls-vod/fixture/playlist.m3u8?token=literal%2Bvalue"}},
         "tar":{"480":{"url":"https://test.cdn.rumble.cloud/raw.tar","meta":{"w":854,"h":480}}},
         "timeline":{"180":{"url":"https://test.cdn.rumble.cloud/preview.mp4","meta":{"w":320,"h":180}}}}}
        """)!;
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Rumble offered playback excludes preview timelines and raw archives", Facts),
        ("Rumble watch share and monetized embed aliases resolve the actual player identity", Aliases),
        ("Rumble URL boundaries reject unrelated hosts and invalid video identifiers", Addresses),
        ("Rumble watch declarations ignore strings comments and callbacks without executing them", Literals),
        ("Rumble iframe and literal SDK embeds delegate through the native provider", Embeds),
        ("Rumble access live preview and trim states fail before media requests", Restrictions),
        ("Rumble identity and watch-page mismatches fail instead of selecting another video", Identities),
        ("Rumble protected renditions are excluded while complete clear playback remains", Protection),
        ("Rumble missing playback cannot fall back to thumbnails or tar archives", Missing),
        ("Rumble archived embed aliases skip discovery while watch aliases skip media", Archive),
        ("Rumble caller session headers and metadata errors remain explicit", HeadersAndErrors),
        ("Rumble cancellation cannot publish files or fetch another endpoint", Cancellation)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var info = await Engine(calls).InspectAsync(Request(Path.GetTempPath()), default);
        var progressive = info.Formats.Single(format => format.Id == "mp4-360");
        Assert(info.Id == Id && info.Title == "Public & film" && info.DurationSeconds == 2 && info.Uploader == "Creator" && info.Formats.Count == 2, "Playback metadata or complete-resource filtering changed.");
        Assert(progressive.Width == 128 && progressive.Height == 96 && progressive.FramesPerSecond == 24 && progressive.Bandwidth == 300000 && progressive.FileSize == 1000,
            "Root defaults or quality labels replaced actual rendition facts.");
        Assert(progressive.Url!.Query == "?token=a%2Bb%2Fc&x=1&x=2" && calls.Count == 2 && calls[1].AbsolutePath == "/embedJS/u4/", "Identity lookup or literal access parameters changed.");
    }
    private static async Task Aliases()
    {
        foreach (var address in new[] { WatchUrl, "https://rumble.com/share/v71gwwi?src=provided", "https://www.rumble.com/embed/ucfsd." + Id + "/?autoplay=0", "https://rumble.com/embed/" + Id })
        {
            var calls = new List<Uri>(); var info = await Engine(calls).InspectAsync(Request(Path.GetTempPath()) with { Url = new(address) }, default);
            Assert(info.Id == Id && info.Extractor == "rumble" && calls.Count == 2, "A Rumble alias lost its player identity.");
            if (!address.Contains("/embed/")) Assert(info.Description == "Film & description", "Unquoted HTML attributes or entities lost the page description.");
        }
    }
    private static Task Addresses()
    {
        var provider = new RumbleProvider();
        foreach (var address in new[] { "https://rumble.com.evil.example/embed/" + Id, "https://user@rumble.com/embed/" + Id, "https://rumble.com:444/embed/" + Id,
            "https://rumble.com/embed/v0", "https://rumble.com/embed/v!bad", "https://rumble.com/embed/vzzzzzzzzzzzzz", "https://rumble.com/c/channel/about", "https://rumble.com/user/creator/playlists", "https://rumble.com/embed/" + Id + "/other" })
            Assert(!provider.CanHandle(new(address)), "An unsupported Rumble address was claimed.");
        return Task.CompletedTask;
    }
    private static async Task Literals()
    {
        var page = """
            <script>
            // Rumble("play", {"video":"v1"});
            const example = 'Rumble("play", {"video":"v2"});';
            /* Rumble("play", {"video":"v3"}); */
            Rumble("play", {video:{"id":"v6za8wa"},opts:[],api:function(api){ let text='}'; ignored(api); }});
            </script>
            """;
        var info = await Engine([], html: page).InspectAsync(Request(Path.GetTempPath()) with { Url = new(WatchUrl) }, default);
        Assert(info.Id == Id, "Non-code examples or ignored callbacks changed the selected video.");
        await Expect<InvalidDataException>(() => Engine([], html: "<script>Rumble('play', {\"video\":\"v6za8wa\",\"video\":\"v1\"});</script>")
            .InspectAsync(Request(Path.GetTempPath()) with { Url = new(WatchUrl) }, default));
        await Expect<NotSupportedException>(() => Engine([], html: "<script>Rumble('play', {video:getVideo()});</script>")
            .InspectAsync(Request(Path.GetTempPath()) with { Url = new(WatchUrl) }, default));
    }
    private static async Task Embeds()
    {
        foreach (var html in new[] { "<iframe src=\"https://rumble.com/embed/ucfsd.v6za8wa/\"></iframe>", "<script>Rumble('play', {\"video\":\"v6za8wa\",\"div\":\"player\"});</script>" })
        {
            var calls = new List<Uri>(); var info = await Engine(calls, html: html).InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://article.example/post") }, default);
            Assert(info.Id == Id && calls.Count == 3 && calls[2].Host == "rumble.com", "HTML embedding bypassed the native Rumble provider.");
        }
        var blocked = new List<Uri>();
        var unavailable = await Engine(blocked, html: "<script>Rumble('play', {\"video\":\"v6za8wa\",\"opts\":{\"region_blocked\":true}});</script>")
            .InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://article.example/post") }, default);
        Assert(unavailable.Formats.Count == 0 && blocked.Count == 1, "Generic embedding ignored an explicit regional restriction.");
    }
    private static async Task Restrictions()
    {
        foreach (var change in new Action<JsonNode>[] { data => data["restrict"] = new JsonArray(0, new JsonArray("allowed.example")), data => data["live"] = 2,
            data => data["live_placeholder"] = true, data => data["loaded"] = 0, data => data["is_preview"] = true, data => data["premium_only"] = true,
            data => data["timeline"] = new JsonArray(1, 20), data => data["timeline"] = "unknown", data => data["timeline"] = new JsonArray(0), data => data["drm"] = true })
        {
            var data = Playback(); change(data); var calls = new List<Uri>();
            await Expect<NotSupportedException>(() => Engine(calls, data).InspectAsync(Request(Path.GetTempPath()), default));
            Assert(calls.Count == 2, "A restricted video caused another request.");
        }
        foreach (var opts in new[] { "{\"region_blocked\":true}", "[\"region_blocked\"]" })
        {
            var calls = new List<Uri>(); var html = "<script>Rumble('play', {\"video\":\"v6za8wa\",\"opts\":" + opts + "});</script>";
            await Expect<NotSupportedException>(() => Engine(calls, html: html).InspectAsync(Request(Path.GetTempPath()) with { Url = new(WatchUrl) }, default));
            Assert(calls.Count == 1, "A regional restriction fetched playback metadata.");
        }
    }
    private static async Task Identities()
    {
        var data = Playback(); data["vid"] = 1;
        await Expect<InvalidDataException>(() => Engine([], data).InspectAsync(Request(Path.GetTempPath()), default));
        data = Playback(); data["l"] = "/v1-other.html";
        await Expect<InvalidDataException>(() => Engine([], data).InspectAsync(Request(Path.GetTempPath()) with { Url = new(WatchUrl) }, default));
        await Expect<InvalidDataException>(() => Engine([], html: WatchPage.Replace("og:url content=https://rumble.com/v71gwwi", "og:url content=https://rumble.com/v1"))
            .InspectAsync(Request(Path.GetTempPath()) with { Url = new(WatchUrl) }, default));
        data = Playback(); data["ua"]!["mp4"]!["360"]!["url"] = "https://rumble.com.evil.example/video.mp4";
        await Expect<InvalidDataException>(() => Engine([], data).InspectAsync(Request(Path.GetTempPath()), default));
    }
    private static async Task Protection()
    {
        var data = Playback(); data["ua"]!["mp4"]!["360"]!["drm"] = true;
        var info = await Engine([], data).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Formats.Count == 1 && info.Formats[0].Id == "hls-auto" && info.Warnings.Count > 0, "Protected media became ready or hid clear playback.");
    }
    private static async Task Missing()
    {
        var data = Playback(); data["ua"]!.AsObject().Remove("mp4"); data["ua"]!.AsObject().Remove("hls");
        await Expect<NotSupportedException>(() => Engine([], data).InspectAsync(Request(Path.GetTempPath()), default));
        data = Playback(); data["duration"] = 0;
        await Expect<InvalidDataException>(() => Engine([], data).InspectAsync(Request(Path.GetTempPath()), default));
    }
    private static async Task Archive()
    {
        using var files = new Files(); var archive = Path.Combine(files.Directory, "archive.txt"); await File.WriteAllTextAsync(archive, "rumble " + Id + "\n");
        var calls = new List<Uri>(); var request = Request(files.Directory) with { DownloadArchiveFile = archive, Url = new("https://rumble.com/embed/ucfsd." + Id + "/") };
        var result = await Engine(calls).DownloadAsync(request, null, null, default);
        Assert(result.Succeeded && result.Skipped && calls.Count == 0, "An archived embed queried metadata.");
        result = await Engine(calls).DownloadAsync(request with { Url = new(WatchUrl) }, null, null, default);
        Assert(result.Succeeded && result.Skipped && calls.Count == 2, "An archived watch alias fetched media or lost the canonical archive ID.");
    }
    private static async Task HeadersAndErrors()
    {
        var count = 0;
        await Engine([], observe: message => { count++; Assert(message.Headers.UserAgent.ToString() == "NativeTest/1.0" && message.Headers.Referrer?.AbsoluteUri == "https://embedding.example/", "Caller headers changed."); })
            .InspectAsync(Request(Path.GetTempPath()) with { Url = new(WatchUrl), UserAgent = "NativeTest/1.0", Referer = "https://embedding.example/" }, default);
        Assert(count == 2, "A discovery request was missing.");
        var calls = new List<Uri>(); await Expect<HttpRequestException>(() => Engine(calls, status: HttpStatusCode.Forbidden).InspectAsync(Request(Path.GetTempPath()), default));
        Assert(calls.Count == 2, "HTTP denial triggered an alternate metadata route.");
    }
    private static async Task Cancellation()
    {
        using var files = new Files(); using var stop = new CancellationTokenSource(); var calls = new List<Uri>();
        await Expect<OperationCanceledException>(() => Engine(calls, observe: _ => stop.Cancel()).DownloadAsync(Request(files.Directory), null, null, stop.Token));
        Assert(calls.Count == 1 && !Directory.EnumerateFiles(files.Directory).Any(), "Canceled discovery fetched or published more data.");
    }
    internal static NativeEngine Engine(List<Uri> calls, JsonNode? data = null, string? html = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null,
        Action<HttpRequestMessage>? observe = null, HttpStatusCode status = HttpStatusCode.OK, string? embedHtml = null) => new(httpFactory: options => new MediaHttp(new Handler(request =>
    {
        var url = request.RequestUri!; lock (calls) calls.Add(url); observe?.Invoke(request);
        if (url.AbsolutePath.StartsWith("/embedJS/", StringComparison.Ordinal)) return new(status) { Content = new StringContent((data ?? Playback()).ToJsonString()) };
        if (url.Host == "article.example" || url.Host.EndsWith("rumble.com") && !url.AbsolutePath.StartsWith("/hls-vod/"))
            return new(HttpStatusCode.OK) { Content = new StringContent(url.AbsolutePath.StartsWith("/embed/") ? embedHtml ?? WatchPage : html ?? WatchPage) };
        return media?.Invoke(request) ?? throw new InvalidOperationException("Unexpected Rumble media request.");
    }), new MediaHttpOptions { UserAgent = options.UserAgent ?? "", Referer = options.Referer ?? "" }));
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
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
