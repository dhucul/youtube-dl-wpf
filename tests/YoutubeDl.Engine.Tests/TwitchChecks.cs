using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Twitch;

internal static class TwitchChecks
{
    internal const string Slug = "FixtureClip";
    private static readonly Uri ClipUrl = new("https://clips.twitch.tv/" + Slug);
    private static readonly Uri ListUrl = new("https://www.twitch.tv/channel/clips?range=all");
    private static JsonNode Clip(string slug = Slug)
    {
        var token = new JsonObject { ["clip_slug"] = slug, ["clip_uri"] = "https://clips-media-assets2.twitch.tv/avc/360/clip.mp4",
            ["authorization"] = new JsonObject { ["forbidden"] = false }, ["expires"] = 4102444800L, ["literal"] = "a+b/=\"" };
        return new JsonObject { ["id"] = "111", ["slug"] = slug, ["title"] = "Clip & title", ["durationSeconds"] = 1.5,
            ["broadcaster"] = new JsonObject { ["login"] = "channel", ["displayName"] = "Channel" }, ["thumbnailURL"] = "https://static-cdn.jtvnw.net/cover.jpg",
            ["playbackAccessToken"] = new JsonObject { ["value"] = token.ToJsonString(), ["signature"] = "signature+a/==" },
            ["videoQualities"] = new JsonArray(Quality("1080", 60), Quality("720", 30), Quality("360", 0)) };
    }
    private static JsonNode Quality(string height, int fps) => new JsonObject { ["quality"] = height, ["frameRate"] = fps,
        ["sourceURL"] = "https://clips-media-assets2.twitch.tv/avc/" + height + "/clip.mp4?kept=a%2Bb&sig=old&token=stale" };
    private static string Response(JsonNode clip) => new JsonObject { ["data"] = new JsonObject { ["clip"] = clip.DeepClone() } }.ToJsonString();
    private static string List(string slug, bool more, string cursor = "cursor+/=") => new JsonObject { ["data"] = new JsonObject { ["user"] = new JsonObject
    {
        ["id"] = "12", ["login"] = "channel", ["displayName"] = "Channel", ["clips"] = new JsonObject { ["pageInfo"] = new JsonObject { ["hasNextPage"] = more },
            ["edges"] = new JsonArray(new JsonObject { ["cursor"] = cursor, ["node"] = Clip(slug) }) }
    } } }.ToJsonString();
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Twitch clip metadata retains authorized qualities and treats zero FPS as unknown", Metadata),
        ("Native Twitch selection preserves signed values and constrained complete publication", Download),
        ("Twitch clip aliases and generic embeds resolve the same native identity", Aliases),
        ("Twitch playback denial and expiration never expose downloadable formats", Denial),
        ("Twitch clip/token identity and media origins are validated before transfer", Invalid),
        ("Twitch clip archives skip scoped URL identities before HTTP", Archive),
        ("Twitch clip listing pagination preserves opaque cursors and selected-only playback", Pagination),
        ("Twitch integrity challenges cannot publish an incomplete listing or start a selected download", Challenge),
        ("Twitch listing loops, missing state, changed owners and item limits fail explicitly", ListingFailures),
        ("Twitch time-range URLs map only to supported public listing periods", Periods),
        ("Canceled Twitch metadata requests do not publish files", Cancellation)
    ];
    private static async Task Metadata()
    {
        var calls = new List<string>(); var info = await Engine(calls).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Id == Slug && info.Uploader == "Channel" && info.DurationSeconds == 1.5 && info.Formats.Count == 3 &&
            info.Formats.Single(format => format.Id == "360p").FramesPerSecond is null && info.Formats.Single(format => format.Id == "1080p60").FramesPerSecond == 60,
            "Clip identity, duration or FPS facts were changed.");
        Assert(calls.SequenceEqual(new[] { "NativeClip" }) && info.Formats.All(format => format.VideoCodec == "avc1"), "Unexpected discovery calls or lost route codec facts.");
    }
    private static async Task Download()
    {
        using var files = new Files(); var calls = new List<string>(); var state = Clip();
        var engine = Engine(calls, clip: state, onMedia: uri =>
        {
            Assert(uri.AbsolutePath.Contains("/720/") && Query(uri, "sig") == "signature+a/==" && Query(uri, "token") == state["playbackAccessToken"]!["value"]!.GetValue<string>() &&
                Query(uri, "kept") == "a+b" && uri.Query.Split('&').Count(part => part.StartsWith("token=")) == 1, "Quality selection or literal playback token values were changed.");
        });
        var result = await engine.DownloadAsync(Request(files.Directory) with { MaximumHeight = 720, MaximumFrameRate = 30, VideoCodec = "h264" }, null, null, default);
        Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == "clip bytes" && calls.Count == 2, "The native clip did not publish exactly once.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "https://www.twitch.tv/channel/clip/FixtureClip", "https://m.twitch.tv/channel/clip/FixtureClip", "https://clips.twitch.tv/channel/FixtureClip", "https://clips.twitch.tv/embed?clip=FixtureClip&parent=one.example&parent=two.example" })
            Assert((await Engine([]).InspectAsync(Request(Path.GetTempPath()) with { Url = new(url) }, default)).Id == Slug, "A Twitch alias lost its clip identity.");
        var engine = Engine([], page: "<iframe src=\"https://clips.twitch.tv/embed?clip=FixtureClip&amp;parent=article.example\"></iframe>");
        Assert((await engine.InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://article.example/post") }, default)).Id == Slug, "Generic HTML did not dispatch a Twitch clip embed.");
        var provider = new TwitchProvider();
        foreach (var url in new[] { "https://clips.twitch.tv.evil.example/FixtureClip", "https://user@clips.twitch.tv/FixtureClip", "https://clips.twitch.tv:8443/FixtureClip", "https://clips.twitch.tv/embed?clip=A&clip=B", "https://www.twitch.tv/channel", "https://www.twitch.tv/videos/123" })
            Assert(!provider.CanHandle(new(url)), "An invalid or unfinished Twitch URL was advertised as supported.");
    }
    private static async Task Denial()
    {
        foreach (var edit in new Action<JsonNode>[] { token => token["authorization"]!["forbidden"] = true, token => token["authorization"] = new JsonObject(), token => token["expires"] = 1 })
        {
            var clip = Clip(); var token = JsonNode.Parse(clip["playbackAccessToken"]!["value"]!.GetValue<string>())!; edit(token); clip["playbackAccessToken"]!["value"] = token.ToJsonString();
            var calls = new List<string>(); var info = await Engine(calls, clip: clip).InspectAsync(Request(Path.GetTempPath()), default);
            Assert(info.Formats.Count == 0 && info.Warnings.Count > 0 && calls.Count == 1, "Denied or expired playback exposed ready media.");
        }
    }
    private static async Task Invalid()
    {
        foreach (var edit in new Action<JsonNode>[] { clip => clip["slug"] = "Different", clip => clip["id"] = "invalid",
            clip => clip["playbackAccessToken"]!["value"] = "{\"clip_slug\":\"Different\"}",
            clip => { var token = JsonNode.Parse(clip["playbackAccessToken"]!["value"]!.GetValue<string>())!; token["expires"] = -1; clip["playbackAccessToken"]!["value"] = token.ToJsonString(); },
            clip => clip["videoQualities"]![0]!["sourceURL"] = "https://evil.example/clip.mp4",
            clip => clip["videoQualities"]![0]!["sourceURL"] = "https://another.cloudfront.net/clip.mp4" })
        {
            var clip = Clip(); edit(clip);
            await Expect<InvalidDataException>(() => Engine([], clip: clip).InspectAsync(Request(Path.GetTempPath()), default));
        }
    }
    private static async Task Archive()
    {
        using var files = new Files(); var archive = Path.Combine(files.Directory, "archive.txt"); var calls = new List<string>();
        var engine = Engine(calls); var request = Request(files.Directory) with { DownloadArchiveFile = archive };
        await engine.DownloadAsync(request, null, null, default);
        Assert(File.ReadAllText(archive) == "native:twitch:clips FixtureClip\n", "The scoped Twitch archive identity was lost.");
        calls.Clear(); var skipped = await engine.DownloadAsync(request with { Url = new("https://www.twitch.tv/channel/clip/FixtureClip") }, null, null, default);
        Assert(skipped.Skipped && calls.Count == 0, "An archived clip alias made an HTTP request.");
    }
    private static async Task Pagination()
    {
        using var files = new Files(); var calls = new List<string>(); var pages = 0;
        var engine = Engine(calls, onQuery: body =>
        {
            if (body.GetProperty("operationName").GetString() != "NativeClipList") return null;
            var after = body.GetProperty("variables").GetProperty("after");
            if (pages++ == 0) { Assert(after.ValueKind == JsonValueKind.Null, "Fresh listing claimed an existing cursor."); return List("FirstClip", true); }
            Assert(after.GetString() == "cursor+/=", "The opaque listing cursor was changed."); return List(Slug, false);
        });
        var result = await engine.DownloadAsync(Request(files.Directory) with { Url = ListUrl, PlaylistItems = "2" }, null, null, default);
        Assert(result.Succeeded && result.OutputPaths.Count == 1 && calls.SequenceEqual(new[] { "NativeClipList", "NativeClipList", "NativeClip", "media" }), "Listing selection resolved an unselected clip or lost an entry.");
    }
    private static async Task Challenge()
    {
        using var files = new Files(); var calls = new List<string>(); var page = 0;
        var engine = Engine(calls, onQuery: _ => page++ == 0 ? List(Slug, true) : """{"errors":[{"message":"failed integrity check","extensions":{"code":"IntegrityCheckFailed"}}],"extensions":{"challenge":{"type":"integrity"}},"data":{"user":{"clips":null}}}""");
        await Expect<NotSupportedException>(() => engine.DownloadAsync(Request(files.Directory) with { Url = ListUrl, PlaylistItems = "1" }, null, null, default));
        Assert(calls.Count == 2 && calls.All(call => call == "NativeClipList") && !Directory.EnumerateFiles(files.Directory).Any(), "An incomplete challenged listing published media or started a new session.");
    }
    private static async Task ListingFailures()
    {
        foreach (var next in new[] { List(Slug, false), List("SecondClip", true), List("SecondClip", false).Replace("\"id\":\"12\"", "\"id\":\"13\""),
            List("SecondClip", false).Replace("\"edges\":", "\"missingEdges\":") })
        {
            var page = 0;
            await Expect<InvalidDataException>(() => Engine([], onQuery: _ => page++ == 0 ? List(Slug, true) : next).InspectAsync(Request(Path.GetTempPath()) with { Url = ListUrl }, default));
        }
        var index = 0;
        await Expect<InvalidDataException>(() => Engine([], onQuery: _ => index++ == 0 ? List(Slug, true) : List("SecondClip", false)).InspectAsync(Request(Path.GetTempPath()) with { Url = ListUrl, MaximumPlaylistEntries = 1 }, default));
    }
    private static async Task Periods()
    {
        foreach (var (range, period) in new[] { ("24hr", "LAST_DAY"), ("7d", "LAST_WEEK"), ("30d", "LAST_MONTH"), ("all", "ALL_TIME") })
        {
            var engine = Engine([], onQuery: body => { Assert(body.GetProperty("query").GetString()!.Contains("period:" + period), "The requested listing period was changed."); return List(Slug, false); });
            var info = await engine.InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://www.twitch.tv/channel/clips?range=" + range) }, default);
            Assert(info.Entries.Count == 1 && info.IsPlaylist, "The clip listing was lost.");
        }
    }
    private static async Task Cancellation()
    {
        using var files = new Files(); using var cancellation = new CancellationTokenSource();
        var engine = Engine([], onQuery: _ => { cancellation.Cancel(); return null; });
        await Expect<OperationCanceledException>(() => engine.DownloadAsync(Request(files.Directory), null, null, cancellation.Token));
        Assert(!Directory.EnumerateFiles(files.Directory).Any(), "Canceled clip discovery published a file.");
    }
    private static NativeEngine Engine(List<string> calls, JsonNode? clip = null, Func<JsonElement, string?>? onQuery = null, Action<Uri>? onMedia = null, string? page = null) =>
        new(httpFactory: _ => new MediaHttp(new Handler(async request =>
        {
            if (request.RequestUri!.Host == "article.example") return Http(page!);
            if (request.RequestUri.Host != "gql.twitch.tv") { calls.Add("media"); onMedia?.Invoke(request.RequestUri); return Http("clip bytes"); }
            Assert(request.Method == HttpMethod.Post && request.Headers.Contains("Client-ID") && request.Headers.GetValues("Origin").Single() == "https://www.twitch.tv", "The native query headers or method are missing.");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()); calls.Add(body.RootElement.GetProperty("operationName").GetString()!);
            return Http(onQuery?.Invoke(body.RootElement) ?? Response(clip ?? Clip()));
        }), new MediaHttpOptions { AllowAutoRedirect = false }));
    private static NativeDownloadRequest Request(string folder) => new() { Url = ClipUrl, OutputDirectory = folder, FileName = "clip", Retries = 0 };
    private static HttpResponseMessage Http(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private static string? Query(Uri url, string key) => url.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).Where(part => part[0] == key).Select(part => Uri.UnescapeDataString(part[1])).SingleOrDefault();
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => response(request); }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose()
        {
            var target = Path.GetFullPath(Directory);
            if (!target.StartsWith(Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Test folder escaped its root.");
            System.IO.Directory.Delete(target, true);
        }
    }
}
