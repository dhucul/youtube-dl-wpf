using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.TikTok;

internal static class TikTokChecks
{
    internal const string Id = "6718335390845095173";
    private static readonly Uri Url = new("https://www.tiktok.com/@creator/video/" + Id);
    private static JsonNode Item(string id = Id) => new JsonObject
    {
        ["id"] = id, ["desc"] = "Caption & literal 🐾", ["author"] = new JsonObject { ["uniqueId"] = "creator", ["nickname"] = "Creator" },
        ["privateItem"] = false, ["isProhibited"] = false,
        ["video"] = new JsonObject { ["width"] = 360, ["height"] = 640, ["duration"] = 1.5, ["codecType"] = "h264", ["fps"] = 30,
            ["originCover"] = "https://p16.tiktokcdn.com/cover.jpg", ["playAddr"] = MediaUrl(id, "h264"), ["size"] = "123",
            ["bitrateInfo"] = new JsonArray(Bitrate(id, "h264", 20, 360, 640), Bitrate(id, "h265_hvc1", 14, 720, 1280)) }
    };
    private static JsonNode Bitrate(string id, string codec, int quality, int width, int height) => new JsonObject
    {
        ["QualityType"] = quality, ["CodecType"] = codec, ["Bitrate"] = quality * 100000, ["BitrateFPS"] = 30, ["Format"] = "mp4",
        ["PlayAddr"] = new JsonObject { ["Width"] = width, ["Height"] = height, ["DataSize"] = "123", ["UrlList"] = new JsonArray(MediaUrl(id, codec), "https://v19-webapp-prime.tiktok.com/alternative.mp4") }
    };
    private static string MediaUrl(string id, string codec) => "https://v16-webapp-prime.tiktok.com/video/" + id + "/" + codec + "?signed=a%2Bb&value=c%2Fd";
    private static string Page(JsonNode item, int status = 0) => "<script type=\"application/json\" id=\"__UNIVERSAL_DATA_FOR_REHYDRATION__\">" +
        new JsonObject { ["__DEFAULT_SCOPE__"] = new JsonObject { ["webapp.video-detail"] = new JsonObject { ["statusCode"] = status, ["itemInfo"] = new JsonObject { ["itemStruct"] = item.DeepClone() } }, ["languageKeys"] = new JsonObject { ["lt"] = 1, ["LT"] = 2 } } }.ToJsonString() + "</script>";
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("TikTok public page data retains real dimensions, codec choices, rates and literal URLs", Metadata),
        ("Native TikTok transfer applies provider playback headers and selected codec limits", Download),
        ("TikTok playback respects explicitly supplied user agent and referrer", ExplicitHeaders),
        ("TikTok short links, legacy paths and players resolve exact video identities", Aliases),
        ("TikTok standard blockquote embeds and multiple snapshots keep HTTP header scopes isolated", Embeds),
        ("TikTok access flags, status errors and verification pages cannot expose playable media", Restrictions),
        ("TikTok photo posts never become background-music downloads", Photos),
        ("Legacy TikTok page state selects only the requested item", Legacy),
        ("TikTok malformed identities, scripts and resource origins fail explicitly", Invalid),
        ("TikTok URL aliases share numeric download archive identity", Archive),
        ("Canceled TikTok extraction does not publish media", Cancellation)
    ];
    private static Task Metadata()
    {
        var page = TikTokProvider.ParsePage(Page(Item()), Id, Url);
        Assert(page.Id == Id && page.Title == "Caption & literal 🐾" && page.Uploader == "Creator" && page.DurationSeconds == 1.5 && page.Media.Count == 2, "TikTok identity or metadata was lost.");
        var avc = page.ReportedFormats.Single(format => format.VideoCodec == "avc1"); var hevc = page.ReportedFormats.Single(format => format.VideoCodec == "hvc1");
        Assert(avc.Width == 360 && avc.Height == 640 && avc.FramesPerSecond == 30 && avc.FileSize == 123 && hevc.Height == 1280 && avc.Url!.Query == "?signed=a%2Bb&value=c%2Fd", "TikTok format facts or URL escaping changed.");
        return Task.CompletedTask;
    }
    private static async Task Download()
    {
        using var files = new Files(); var calls = new List<Call>(); var disposed = 0;
        var result = await Engine(calls, onDispose: () => disposed++).DownloadAsync(Request(files.Directory) with { VideoCodec = "h264", MaximumHeight = 720, MaximumFrameRate = 30, WriteThumbnail = true }, null, null, default);
        Assert(result.Succeeded && calls.Count == 3 && File.ReadAllText(result.OutputPath!) == "video " + Id && File.Exists(Path.ChangeExtension(result.OutputPath!, ".jpg")), "TikTok did not save the selected video and artwork.");
        Assert(calls.All(call => call.Agent.StartsWith("YoutubeDlNative/")) && calls.Skip(1).All(call => call.Referer == "https://www.tiktok.com/") &&
            calls[1].Url.AbsoluteUri == MediaUrl(Id, "h264") && disposed == 1, "Playback headers changed the selected address or disposed the shared handler early.");
    }
    private static async Task ExplicitHeaders()
    {
        using var files = new Files(); var calls = new List<Call>();
        await Engine(calls).DownloadAsync(Request(files.Directory) with { UserAgent = "ChosenAgent/1.0", Referer = "https://chosen.example/", VideoCodec = "h264" }, null, null, default);
        Assert(calls.All(call => call.Agent == "ChosenAgent/1.0" && call.Referer == "https://chosen.example/"), "Provider defaults replaced explicit client settings.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "https://www.tiktok.com/player/v1/" + Id, "https://www.tiktok.com/embed/v2/" + Id,
            "http://m.tiktok.com/v/" + Id + ".html", "https://www.tiktok.com/share/video/" + Id, "https://vm.tiktok.com/fixture/", "https://vt.tiktok.com/fixture/", "https://www.tiktok.com/t/fixture/" })
            Assert((await Engine([]).InspectAsync(Request(Path.GetTempPath()) with { Url = new(url) }, default)).Id == Id, "A TikTok alias lost its requested video identity.");
        var provider = new TikTokProvider();
        foreach (var url in new[] { "https://www.tiktok.com.evil.example/@creator/video/" + Id, "https://user@www.tiktok.com/@creator/video/" + Id,
            "https://www.tiktok.com:8443/@creator/video/" + Id, "https://www.tiktok.com/@creator", "https://www.tiktok.com/@creator/photo/" + Id })
            Assert(!provider.CanHandle(new(url)), "An unsupported TikTok URL was advertised as a video.");
    }
    private static async Task Embeds()
    {
        using var files = new Files(); var calls = new List<Call>();
        const string other = "6718335390845095174";
        var article = "<blockquote class=\"tiktok-embed\" cite=\"https://www.tiktok.com/@creator/video/" + Id + "\" data-video-id=\"" + Id + "\"></blockquote>" +
            "<iframe src=\"https://www.tiktok.com/player/v1/" + other + "\"></iframe>";
        var result = await Engine(calls, article: article).DownloadAsync(Request(files.Directory) with { Url = new("https://article.example/post"), VideoCodec = "h264" }, null, null, default);
        Assert(result.Succeeded && result.OutputPaths.Count == 2 && calls.Count == 5, "The embedded videos were not resolved and downloaded as separate items.");
        Assert(calls[0].Agent == "ParentAgent/1.0" && calls.Skip(1).All(call => call.Agent.StartsWith("YoutubeDlNative/")) &&
            calls.Where(call => call.Url.Host.StartsWith("v16-")).All(call => call.Referer == "https://www.tiktok.com/"), "Scoped headers leaked or were lost between embedded items.");
        await Expect<InvalidDataException>(() => Engine([], article: article.Replace("data-video-id=\"" + Id, "data-video-id=\"" + other)).InspectAsync(Request(files.Directory) with { Url = new("https://article.example/post") }, default));
    }
    private static async Task Restrictions()
    {
        foreach (var field in new[] { "privateItem", "secret", "forFriend", "isProhibited", "takeDown", "isReviewing" })
        {
            var item = Item(); item[field] = true;
            Assert(TikTokProvider.ParsePage(Page(item), Id, Url).Media.Count == 0, "A restricted TikTok item exposed video formats.");
        }
        var drm = Item(); drm["video"]!["drmType"] = 1;
        Assert(TikTokProvider.ParsePage(Page(drm), Id, Url).Media.Count == 0, "Protected media became ready.");
        var protectedFormat = Item(); protectedFormat["video"]!["bitrateInfo"]![0]!["drmType"] = 1;
        Assert(TikTokProvider.ParsePage(Page(protectedFormat), Id, Url).Media.All(media => media.VideoCodec != "avc1"), "The generic playback address restored a protected format.");
        await Expect<InvalidOperationException>(() => Engine([], html: Page(Item(), 10216)).InspectAsync(Request(Path.GetTempPath()), default));
        var calls = new List<Call>();
        await Expect<NotSupportedException>(() => Engine(calls, html: "<html><p class=\"_wafchallengeid\">Please wait</p></html>").InspectAsync(Request(Path.GetTempPath()), default));
        Assert(calls.Count == 1, "A verification response triggered client rotation or repeated extraction.");
    }
    private static Task Photos()
    {
        var item = Item(); item.AsObject().Remove("video"); item["imagePost"] = new JsonObject { ["images"] = new JsonArray(new JsonObject()) };
        item["music"] = new JsonObject { ["playUrl"] = "https://v16.tiktokcdn.com/music.mp3" };
        var page = TikTokProvider.ParsePage(Page(item), Id, Url);
        Assert(page.Media.Count == 0 && page.Warnings.Single().Contains("Photo posts"), "A photo post was downloaded as audio.");
        return Task.CompletedTask;
    }
    private static Task Legacy()
    {
        var state = new JsonObject { ["ItemModule"] = new JsonObject { [Id] = Item(), ["6718335390845095174"] = Item("6718335390845095174") } };
        var page = TikTokProvider.ParsePage("<script id=\"SIGI_STATE\">" + state.ToJsonString() + "</script>", Id, Url);
        Assert(page.Id == Id && page.Media.All(media => media.Url.AbsolutePath.Contains(Id)), "Legacy parsing selected a related video.");
        return Task.CompletedTask;
    }
    private static async Task Invalid()
    {
        foreach (var item in new[] { Item("6718335390845095174"), BadMedia() })
            await Expect<InvalidDataException>(() => Engine([], html: Page(item)).InspectAsync(Request(Path.GetTempPath()), default));
        await Expect<InvalidDataException>(() => Engine([], html: Page(Item()) + Page(Item())).InspectAsync(Request(Path.GetTempPath()), default));
        await Expect<InvalidDataException>(() => Engine([], redirect: new("https://www.tiktok.com/@other/video/6718335390845095174")).InspectAsync(Request(Path.GetTempPath()), default));
        static JsonNode BadMedia() { var item = Item(); item["video"]!["bitrateInfo"]![0]!["PlayAddr"]!["UrlList"]![0] = "https://evil.example/video.mp4"; return item; }
    }
    private static async Task Archive()
    {
        using var files = new Files(); var archive = Path.Combine(files.Directory, "archive.txt"); var calls = new List<Call>();
        var engine = Engine(calls); var request = Request(files.Directory) with { DownloadArchiveFile = archive };
        await engine.DownloadAsync(request, null, null, default);
        Assert(File.ReadAllText(archive) == "tiktok " + Id + "\n", "The TikTok numeric archive identity was lost.");
        calls.Clear(); var skipped = await engine.DownloadAsync(request with { Url = new("https://www.tiktok.com/player/v1/" + Id) }, null, null, default);
        Assert(skipped.Skipped && calls.Count == 0, "An archived player alias made a network request.");
    }
    private static async Task Cancellation()
    {
        using var files = new Files(); using var cancellation = new CancellationTokenSource();
        var engine = Engine([], onRequest: _ => cancellation.Cancel());
        await Expect<OperationCanceledException>(() => engine.DownloadAsync(Request(files.Directory), null, null, cancellation.Token));
        Assert(!Directory.EnumerateFiles(files.Directory).Any(), "Canceled extraction published a file.");
    }
    private sealed record Call(Uri Url, string Agent, string? Referer);
    private static NativeEngine Engine(List<Call> calls, string? html = null, string? article = null, Uri? redirect = null, Action? onDispose = null, Action<HttpRequestMessage>? onRequest = null) =>
        new(httpFactory: request => new MediaHttp(new Handler(message =>
        {
            onRequest?.Invoke(message); var url = message.RequestUri!;
            calls.Add(new(url, message.Headers.UserAgent.ToString(), message.Headers.Referrer?.AbsoluteUri));
            if (url.Host == "article.example") return Response(article!);
            if (url.Host == "p16.tiktokcdn.com") return new(HttpStatusCode.OK) { Content = new ByteArrayContent([0xff, 0xd8, 0xff, 0xe0]) };
            if (url.Host.StartsWith("v16-")) return Response("video " + url.AbsolutePath.Split('/')[2]);
            var id = url.AbsolutePath.Split('/').LastOrDefault(part => part.Length == 19 && part.All(char.IsDigit)) ?? Id;
            var response = Response(html ?? Page(Item(id)));
            if (redirect is not null || url.Host is "vm.tiktok.com" or "vt.tiktok.com" || url.AbsolutePath.StartsWith("/t/"))
                response.RequestMessage = new(HttpMethod.Get, redirect ?? Url);
            return response;
        }, onDispose), new MediaHttpOptions { UserAgent = request.UserAgent ?? "ParentAgent/1.0", Referer = request.Referer ?? "https://article.example/" }));
    private static NativeDownloadRequest Request(string folder) => new() { Url = Url, OutputDirectory = folder, FileName = "tiktok", Retries = 0 };
    private static HttpResponseMessage Response(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response, Action? onDispose) : HttpMessageHandler
    {
        private bool _disposed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ObjectDisposedException.ThrowIf(_disposed, this); return Task.FromResult(response(request)); }
        protected override void Dispose(bool disposing) { if (!_disposed) { _disposed = true; onDispose?.Invoke(); } base.Dispose(disposing); }
    }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose()
        {
            var target = Path.GetFullPath(Directory); if (!target.StartsWith(Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Test folder escaped its root.");
            System.IO.Directory.Delete(target, true);
        }
    }
}
