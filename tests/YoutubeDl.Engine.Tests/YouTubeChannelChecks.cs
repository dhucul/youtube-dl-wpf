using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.YouTube;

internal static class YouTubeChannelChecks
{
    private const string Channel = "UCabcdefghijklmnopqrstuv";
    private static readonly string[] Ids = ["aaaaaaaaaaa", "bbbbbbbbbbb", "ccccccccccc", "ddddddddddd"];
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("YouTube channels parse current video and Shorts cards without nested menu suggestions", Cards),
        ("YouTube channels paginate only the selected feed using the page client context", Pagination),
        ("YouTube channel roots combine upload tabs once with stable video identities", Aggregate),
        ("YouTube channel aliases preserve identity and reject unsupported query semantics", Aliases),
        ("YouTube channels reject missing tabs, wrong identities and unsupported feed layouts", Invalid),
        ("YouTube channel repeated or oversized feeds never return truncated success", Limits),
        ("YouTube channel selection downloads only selected videos with normal archive identities", Download),
        ("YouTube channel cancellation cannot publish a partial feed as complete", Cancellation),
        ("YouTube channel explicit empty tabs retain an empty inventory", Empty)
    ];

    private static Task Cards()
    {
        var page = Parse("videos", [Video(Ids[0]), Legacy(Ids[1]), new { videoRenderer = new { isPlayable = false, title = new { simpleText = "Unavailable" } } }]);
        Assert(page.Batch!.Entries.Count == 3 && page.Batch.Entries[0] is { Id: "aaaaaaaaaaa", Title: "Video aaaaaaaaaaa", DurationSeconds: 75, ThumbnailUrl: "https://img.example/large.jpg" } &&
            page.Batch.Entries[1].DurationSeconds == 20 && !page.Batch.Entries[2].Available, "Video card metadata or unavailable source positions were lost.");
        var shorts = Parse("shorts", [Short(Ids[2])]);
        Assert(shorts.Batch!.Entries.Single() is { Id: "ccccccccccc", Title: "Short ccccccccccc", DurationSeconds: null, ThumbnailUrl: "https://img.example/short.jpg" }, "Shorts identity/title/artwork was inferred from the wrong nested object.");
        return Task.CompletedTask;
    }

    private static async Task Pagination()
    {
        var calls = new List<string>();
        var engine = Engine(request =>
        {
            calls.Add(request.Method.Method + " " + request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Get) return Html("videos", [Video(Ids[0])], "next");
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert(body.RootElement.GetProperty("continuation").GetString() == "next" && body.RootElement.GetProperty("context").GetProperty("client").GetProperty("clientVersion").GetString() == "fixture.1" &&
                request.Headers.GetValues("X-YouTube-Client-Version").Single() == "fixture.1", "The channel pagination lost its published client context.");
            return Continuation("videos", [Video(Ids[0]), Video(Ids[1])]);
        });
        var result = await engine.InspectAsync(Request() with { Url = new("https://www.youtube.com/@native/videos") }, default);
        Assert(result.IsPlaylist && result.Id == Channel + ":videos" && result.Entries.Select(entry => entry.Id).SequenceEqual(Ids.Take(2)) &&
            result.Entries.Select(entry => entry.Position).SequenceEqual([1, 2]) && calls.Count == 2, "Continuation duplicated videos, collected other targets, or lost channel identity.");
    }

    private static async Task Aggregate()
    {
        var calls = new List<string>();
        var info = await Engine(request =>
        {
            var path = request.RequestUri!.AbsolutePath; calls.Add(path);
            return path.EndsWith("/videos") ? Html("videos", [Video(Ids[0]), Video(Ids[1])]) : path.EndsWith("/shorts") ? Html("shorts", [Short(Ids[1]), Short(Ids[2])]) :
                path.EndsWith("/streams") ? Html("streams", [Video(Ids[3])]) : Html("featured", []);
        }).InspectAsync(Request(), default);
        Assert(info.Id == Channel && info.Title == "Native channel" && info.Uploader == "Native channel" && info.Entries.Select(entry => entry.Id).SequenceEqual(Ids) &&
            info.Entries.Select(entry => entry.Position).SequenceEqual([1, 2, 3, 4]), "Root channel enumeration omitted a media tab or duplicated an upload.");
        Assert(calls.SequenceEqual(["/@native", "/@native/videos", "/@native/shorts", "/@native/streams"]), "Root enumeration fetched a non-upload tab.");
    }

    private static async Task Aliases()
    {
        foreach (var path in new[] { "@native/videos", "channel/" + Channel + "/videos", "c/Legacy/videos", "user/Legacy/videos", "@日本語/videos" })
        {
            var info = await Engine(_ => Html("videos", [Video(Ids[0])])).InspectAsync(Request() with { Url = new("https://m.youtube.com/" + path) }, default);
            Assert(info.Id == Channel + ":videos", "A channel alias lost its server-confirmed identity.");
        }
        foreach (var suffix in new[] { "/videos?sort=p", "/search?query=video", "/playlists", "/videos?list=PLother", "/videos#other" })
            await Expect<NotSupportedException>(() => Engine(_ => throw new InvalidOperationException("Unsupported channel address reached HTTP.")).InspectAsync(Request() with { Url = new("https://www.youtube.com/@native" + suffix) }, default));
    }

    private static async Task Invalid()
    {
        await Expect<NotSupportedException>(() => Engine(_ => Html("shorts", [Short(Ids[0])])).InspectAsync(Request() with { Url = new("https://www.youtube.com/@native/videos") }, default));
        await Expect<InvalidDataException>(() => Engine(_ => Html("videos", [Video(Ids[0])])).InspectAsync(Request() with { Url = new("https://www.youtube.com/channel/UC" + new string('z', 22) + "/videos") }, default));
        await Expect<InvalidDataException>(() => Engine(_ => Html("videos", [Video("bad-id")])).InspectAsync(Request() with { Url = new("https://www.youtube.com/@native/videos") }, default));
        await Expect<NotSupportedException>(() => Engine(_ => Html("videos", [new { richSectionRenderer = new { contents = new[] { Video(Ids[0]) } } }])).InspectAsync(Request() with { Url = new("https://www.youtube.com/@native/videos") }, default));
        await Expect<NotSupportedException>(() => Engine(_ => Html("videos", [new { messageRenderer = new { text = new { simpleText = "Access is required" } } }])).InspectAsync(Request() with { Url = new("https://www.youtube.com/@native/videos") }, default));
        await Expect<InvalidDataException>(() => Engine(request => request.Method == HttpMethod.Get ? Html("videos", [Video(Ids[0])], "next") : Continuation("wrong", [Video(Ids[1])]))
            .InspectAsync(Request() with { Url = new("https://www.youtube.com/@native/videos") }, default));
        await Expect<InvalidDataException>(() => Engine(request => request.RequestUri!.AbsolutePath == "/@native" ? Html("featured", []) :
            Html("videos", [Video(Ids[0])]).Replace(Channel, "UC" + new string('z', 22))).InspectAsync(Request(), default));
        var deniedCalls = 0;
        await Expect<InvalidDataException>(() => Engine(_ => { deniedCalls++; return "<html>Consent required</html>"; }).InspectAsync(Request(), default));
        Assert(deniedCalls == 1, "An inaccessible channel was probed through alternate identities or endpoints.");
        var malformed = System.Text.Json.Nodes.JsonNode.Parse(Json("videos", []))!;
        malformed["contents"]!["twoColumnBrowseResultsRenderer"]!["tabs"]![1]!["tabRenderer"]!["content"]!["richGridRenderer"]!.AsObject().Remove("contents");
        await Expect<InvalidDataException>(() => Engine(_ => "<script>ytInitialData=" + malformed.ToJsonString() + ";</script>").InspectAsync(Request() with { Url = new("https://www.youtube.com/@native/videos") }, default));
    }

    private static async Task Limits()
    {
        await Expect<InvalidDataException>(() => Engine(request => request.Method == HttpMethod.Get ? Html("videos", [Video(Ids[0])], "repeat") : Continuation("videos", [Video(Ids[1])], "repeat"))
            .InspectAsync(Request() with { Url = new("https://www.youtube.com/@native/videos") }, default));
        await Expect<InvalidDataException>(() => Engine(_ => Html("videos", Ids.Select(Video).ToArray())).InspectAsync(Request() with
        { Url = new("https://www.youtube.com/@native/videos"), MaximumPlaylistEntries = 2 }, default));
        await Expect<InvalidDataException>(() => Engine(request => request.Method == HttpMethod.Get ? Html("videos", [Video(Ids[0])], "first") : Continuation("videos", [Video(Ids[0])], "second"))
            .InspectAsync(Request() with { Url = new("https://www.youtube.com/@native/videos") }, default));
    }

    private static async Task Download()
    {
        using var files = new Files(); var watched = new List<string>(); var completed = new List<string>();
        var archive = Path.Combine(files.Directory, "archive.txt");
        var engine = Engine(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/watch")
            {
                var id = request.RequestUri.Query.Split("v=")[1]; watched.Add(id);
                return YouTubeChecks.FixtureHtml.Replace("abcdefghijk", id);
            }
            return request.RequestUri.Host.Contains("youtube.com") ? Html("shorts", [Short(Ids[0]), Short(Ids[1])]) : "combined";
        });
        var request = Request() with { Url = new("https://www.youtube.com/@native/shorts"), OutputDirectory = files.Directory, PlaylistItems = "2", FormatId = "18", DownloadArchiveFile = archive };
        var result = await engine.DownloadAsync(request, null, null, default, completed.Add);
        Assert(result.Succeeded && watched.SequenceEqual([Ids[1]]) && completed.Count == 1 && Path.GetFileName(completed[0]).StartsWith("002 - ") &&
            File.ReadAllText(completed[0]) == "combined" && File.ReadAllText(archive).Trim() == "youtube " + Ids[1], "Channel selection did not retain normal video download/archive semantics.");
        watched.Clear(); completed.Clear();
        var repeat = await engine.DownloadAsync(request, null, null, default, completed.Add);
        Assert(repeat.Succeeded && watched.Count == 0 && completed.Count == 0, "An archived channel item was downloaded again.");
    }

    private static async Task Cancellation()
    {
        using var stop = new CancellationTokenSource(); var calls = 0;
        var engine = Engine(request => { calls++; if (request.Method == HttpMethod.Post) stop.Cancel(); return request.Method == HttpMethod.Get ? Html("videos", [Video(Ids[0])], "next") : Continuation("videos", [Video(Ids[1])]); });
        await Expect<OperationCanceledException>(() => engine.InspectAsync(Request() with { Url = new("https://www.youtube.com/@native/videos") }, stop.Token));
        Assert(calls == 2, "Cancellation started another channel page.");
    }

    private static async Task Empty()
    {
        var info = await Engine(_ => Html("shorts", [])).InspectAsync(Request() with { Url = new("https://www.youtube.com/@native/shorts") }, default);
        Assert(info.IsPlaylist && info.Entries.Count == 0 && info.Id == Channel + ":shorts", "An explicitly empty tab became another feed or a fabricated entry.");
    }

    private static object Video(string id) => new { richItemRenderer = new { content = new { lockupViewModel = new {
        contentId = id, contentType = "LOCKUP_CONTENT_TYPE_VIDEO", metadata = new { lockupMetadataViewModel = new { title = new { content = "Video " + id } } },
        contentImage = new { thumbnailViewModel = new { image = new { sources = new[] { new { url = "https://img.example/small.jpg", width = 10 }, new { url = "https://img.example/large.jpg", width = 100 } } },
            overlays = new[] { new { thumbnailBottomOverlayViewModel = new { badges = new[] { new { thumbnailBadgeViewModel = new { text = "1:15" } } } } } } } },
        menu = new { videoRenderer = new { videoId = Ids[3], title = new { simpleText = "Never collect this menu" } } }
    } } } };
    private static object Short(string id) => new { richItemRenderer = new { content = new { shortsLockupViewModel = new {
        onTap = new { innertubeCommand = new { reelWatchEndpoint = new { videoId = id } } }, overlayMetadata = new { primaryText = new { content = "Short " + id } },
        thumbnailViewModel = new { thumbnailViewModel = new { image = new { sources = new[] { new { url = "https://img.example/short.jpg", width = 100 } } } } }
    } } } };
    private static object Legacy(string id) => new { gridVideoRenderer = new { videoId = id, title = new { simpleText = "Legacy" }, lengthText = new { simpleText = "0:20" } } };
    private static object Next(string token) => new { continuationItemRenderer = new { continuationEndpoint = new { continuationCommand = new { token } } } };
    private static string Json(string active, object[] items, string? next = null)
    {
        var contents = items.ToList(); if (next is not null) contents.Add(Next(next));
        return JsonSerializer.Serialize(new {
            metadata = new { channelMetadataRenderer = new { externalId = Channel, title = "Native channel", description = "Fixture", ownerUrls = new[] { "https://www.youtube.com/@native" } } },
            contents = new { twoColumnBrowseResultsRenderer = new { tabs = new[] { "featured", "videos", "shorts", "streams" }.Select(kind => new { tabRenderer = new {
                title = kind, selected = kind == active, endpoint = new { commandMetadata = new { webCommandMetadata = new { url = "/@native/" + kind } }, browseEndpoint = new { browseId = Channel } },
                content = new { richGridRenderer = new { contents = kind == active ? contents : [], targetId = "feed-" + kind } }
            } }) } }
        });
    }
    private static string Html(string active, object[] items, string? next = null) => "<script>ytInitialData=" + Json(active, items, next) + ";</script>{\"INNERTUBE_CLIENT_VERSION\":\"fixture.1\"}";
    private static YouTubeChannelPage Parse(string kind, object[] items)
    { YouTubeChannelAddress.TryParse(new("https://www.youtube.com/@native/" + kind), out var address); return YouTubeChannelParser.ParsePage(Json(kind, items), address!, 1000, default); }
    private static string Continuation(string kind, object[] items, string? next = null)
    {
        var values = items.ToList(); if (next is not null) values.Add(Next(next));
        return JsonSerializer.Serialize(new { onResponseReceivedActions = new[] {
            new { appendContinuationItemsAction = new { targetId = "sidebar", continuationItems = new[] { Video(Ids[3]) }.ToList() } },
            new { appendContinuationItemsAction = new { targetId = "feed-" + kind, continuationItems = values } }
        } });
    }
    private static NativeDownloadRequest Request() => new() { Url = new("https://www.youtube.com/@native"), OutputDirectory = Path.GetTempPath(), Retries = 0 };
    private static NativeEngine Engine(Func<HttpRequestMessage, string> respond) => new(httpFactory: _ => new MediaHttp(new Handler(respond)));
    private sealed class Handler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request)) }); }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, "channel-" + Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() { var path = Path.GetFullPath(Directory); if (!path.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Test folder escaped its root."); System.IO.Directory.Delete(path, true); }
    }
}
