using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Ted;

internal static class TedChecks
{
    private static readonly Uri Url = new("https://www.ted.com/talks/fixture_talk");
    private static JsonNode Video() => JsonNode.Parse("""
        {"id":"814","slug":"fixture_talk","title":"Talk title","canonicalUrl":"https://www.ted.com/talks/fixture_talk","presenterDisplayName":"Speaker","duration":2,
         "primaryImageSet":[{"url":"https://pe.tedcdn.com/image.jpg","aspectRatioName":"16x9"}],"description":"Description","hlsUrl":"https://hls.ted.com/full.m3u8?intro=1",
         "videoPlayerData":{"id":"35572","slug":"fixture_talk","canonical":"https://www.ted.com/talks/fixture_talk","duration":1,
            "resources":{"h264":[{"file":"https://py.tedcdn.com/fallback.mp4","bitrate":1200}],"hls":{"metadata":"https://hls.ted.com/metadata.json?intro=1"}}}}
        """)!;
    private const string Captions = """
        {"domains":[{"duration":0.5,"primaryDomain":false},{"duration":1.5,"primaryDomain":true}],"subtitles":[{"code":"en","webvtt":"https://hls.ted.com/en.vtt?intro=1"},{"code":"fr","webvtt":"https://hls.ted.com/fr.vtt?intro=1"}]}
        """;
    private static string Page(JsonNode value, bool playlist = false, bool series = false) => "<script id=\"__NEXT_DATA__\" type=\"application/json\">" + new JsonObject { ["props"] = new JsonObject
        { ["pageProps"] = series ? value.DeepClone() : new JsonObject { [playlist ? "playlist" : "videoData"] = value.DeepClone() } } }.ToJsonString() + "</script>";
    private static JsonNode Playlist() => new JsonObject { ["id"] = "171", ["title"] = "Collection", ["videos"] = new JsonObject { ["totalCount"] = 2, ["nodes"] = new JsonArray(Video(), null) } };
    private static JsonNode SeriesData() => new JsonObject { ["series"] = new JsonObject { ["id"] = "8", ["slug"] = "fixture_series", ["name"] = "Series title" },
        ["seasons"] = new JsonArray(Playlist(), new JsonObject { ["id"] = "172", ["videos"] = new JsonObject { ["totalCount"] = 1, ["nodes"] = new JsonArray(Video()) } }) };
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("TED metadata distinguishes talk identity from player identity and exposes full caption URLs", Metadata),
        ("TED fallback selection stays explicit while normal quality choices prefer the main stream", Preference),
        ("Native TED progressive fallback saves matching caption documents", Download),
        ("TED playlists preserve unavailable positions and resolve only selected talks", Collection),
        ("TED series preserve season order, unavailable positions and selected-talk discovery", Series),
        ("TED series enforce whole-series limits and complete, distinct season snapshots", InvalidSeries),
        ("TED embed, transcript, language and numeric aliases retain exact identity", Aliases),
        ("TED malformed identities, counts and resource boundaries fail explicitly", Invalid),
        ("TED external-player offsets are not silently downloaded as a different clip", External),
        ("Canceled TED metadata requests do not publish files", Cancellation)
    ];
    private static async Task Metadata()
    {
        var calls = new List<string>(); var info = await Engine(calls).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Id == "814" && info.Title == "Talk title" && info.Uploader == "Speaker" && info.DurationSeconds == 2 && info.Subtitles.Count == 2 && info.Formats.Count == 2,
            "Talk and player identities, presentation duration or captions were lost.");
        Assert(calls.SequenceEqual(new[] { "/talks/fixture_talk", "/metadata.json" }) && info.Subtitles.All(track => track.Url.Query == "?intro=1"), "Metadata discovery changed caption parameters or requested unrelated media.");
    }
    private static async Task Preference()
    {
        foreach (var quality in new[] { QualityPreference.Best, QualityPreference.Worst })
        {
            var calls = new List<string>();
            using var files = new Files();
            var engine = Engine(calls);
            var result = await engine.DownloadAsync(Request(files.Directory) with { Quality = quality }, null, null, default);
            Assert(result.Succeeded && calls.Contains("/full.m3u8") && !calls.Contains("/fallback.mp4"), "A generic quality choice picked the service fallback over the primary stream.");
        }
    }
    private static async Task Download()
    {
        using var files = new Files(); var calls = new List<string>();
        var result = await Engine(calls).DownloadAsync(Request(files.Directory) with { FormatSelector = "mp4-1", WriteSubs = true, SubLanguages = "en,fr" }, null, null, default);
        Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == "media bytes" && calls.Contains("/fallback.mp4") && !calls.Contains("/full.m3u8") &&
            File.ReadAllText(Path.ChangeExtension(result.OutputPath!, ".en.vtt")) == CaptionChecks.Caption, "The selected TED fallback or its captions were changed.");
    }
    private static async Task Collection()
    {
        var calls = new List<string>(); using var files = new Files(); var engine = Engine(calls);
        var info = await engine.InspectAsync(Request(files.Directory) with { Url = new("https://www.ted.com/playlists/171/collection") }, default);
        Assert(info.IsPlaylist && info.Entries.Count == 2 && !info.Entries[1].Available && calls.Count == 1, "Playlist metadata fetched unselected talk media or dropped an unavailable position.");
        calls.Clear(); var result = await engine.DownloadAsync(Request(files.Directory) with { Url = new("https://www.ted.com/playlists/171/collection"), PlaylistItems = "1", FormatSelector = "mp4-1" }, null, null, default);
        Assert(result.Succeeded && result.OutputPaths.Count == 1 && calls.Count == 4, "Selected playlist playback was incomplete or fetched an unselected item.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "https://embed.ted.com/talks/fixture_talk", "https://www.ted.com/talks/lang/fr/fixture_talk", "https://www.ted.com/talks/fixture_talk/transcript", "https://www.ted.com/talks/814" })
            Assert((await Engine([]).InspectAsync(Request(Path.GetTempPath()) with { Url = new(url) }, default)).Id == "814", "A TED alias changed the talk identity.");
        var provider = new TedProvider();
        Assert(!provider.CanHandle(new("https://ted.com.evil.example/talks/fixture_talk")) && !provider.CanHandle(new("https://user@ted.com/talks/fixture_talk")), "An invalid TED host was accepted.");
    }
    private static async Task Series()
    {
        var calls = new List<string>(); using var files = new Files(); var engine = Engine(calls);
        var request = Request(files.Directory) with { Url = new("https://www.ted.com/series/fixture_series") };
        var info = await engine.InspectAsync(request, default);
        Assert(info.Id == "8" && info.Title == "Series title" && info.IsPlaylist && info.Entries.Count == 3 && !info.Entries[1].Available && calls.Count == 1,
            "A series lost its title, season order or unavailable positions.");
        calls.Clear();
        var result = await engine.DownloadAsync(request with { PlaylistItems = "3", FormatSelector = "mp4-1" }, null, null, default);
        Assert(result.Succeeded && result.OutputPaths.Count == 1 && calls.SequenceEqual(new[] { "/series/fixture_series", "/talks/fixture_talk", "/metadata.json", "/fallback.mp4" }),
            "Series selection fetched unselected talks or failed to resolve the selected talk.");
    }
    private static async Task InvalidSeries()
    {
        var request = Request(Path.GetTempPath()) with { Url = new("https://www.ted.com/series/fixture_series") };
        foreach (var edit in new Action<JsonNode>[] { data => data["series"]!["slug"] = "other_series", data => data["seasons"] = null,
            data => data["seasons"]![1]!["id"] = "171", data => data["seasons"]![1]!["videos"]!["nodes"] = null })
        { var data = SeriesData(); edit(data); await Expect<InvalidDataException>(() => Engine([], series: data).InspectAsync(request, default)); }
        var incomplete = SeriesData(); incomplete["seasons"]![1]!["videos"]!["totalCount"] = 2;
        await Expect<NotSupportedException>(() => Engine([], series: incomplete).InspectAsync(request, default));
        await Expect<InvalidDataException>(() => Engine([]).InspectAsync(request with { MaximumPlaylistEntries = 2 }, default));
    }
    private static async Task Invalid()
    {
        foreach (var edit in new Action<JsonNode>[] { video => video["slug"] = "another_talk", video => video["videoPlayerData"]!["slug"] = "another_talk",
            video => video["canonicalUrl"] = "https://www.ted.com/talks/another_talk", video => video["hlsUrl"] = "https://evil.example/source.m3u8" })
        { var video = Video(); edit(video); await Expect<InvalidDataException>(() => Engine([], video).InspectAsync(Request(Path.GetTempPath()), default)); }
        var list = Playlist(); list["videos"]!["totalCount"] = 3;
        await Expect<NotSupportedException>(() => Engine([], playlist: list).InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://www.ted.com/playlists/171/collection") }, default));
        await Expect<InvalidDataException>(() => Engine([]).InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://www.ted.com/playlists/171/collection"), MaximumPlaylistEntries = 1 }, default));
    }
    private static async Task External()
    {
        var video = Video(); video["hlsUrl"] = null; video["videoPlayerData"]!["resources"] = new JsonObject();
        video["videoPlayerData"]!["external"] = new JsonObject { ["service"] = "YouTube", ["code"] = "abcdefghijk", ["startTime"] = 10 };
        await Expect<NotSupportedException>(() => Engine([], video).InspectAsync(Request(Path.GetTempPath()), default));
    }
    private static async Task Cancellation()
    {
        using var files = new Files(); using var cancellation = new CancellationTokenSource();
        await Expect<OperationCanceledException>(() => Engine([], onRequest: path => { if (path == "/metadata.json") cancellation.Cancel(); }).DownloadAsync(Request(files.Directory), null, null, cancellation.Token));
        Assert(!Directory.EnumerateFiles(files.Directory).Any(), "Canceled TED discovery published media.");
    }
    private static NativeEngine Engine(List<string> calls, JsonNode? video = null, JsonNode? playlist = null, Action<string>? onRequest = null, JsonNode? series = null) => new(httpFactory: _ => new MediaHttp(new Handler(request =>
    {
        var path = request.RequestUri!.AbsolutePath; calls.Add(path); onRequest?.Invoke(path);
        var text = path.StartsWith("/talks/") ? Page(video ?? Video()) : path.StartsWith("/playlists/") ? Page(playlist ?? Playlist(), true) : path.StartsWith("/series/") ? Page(series ?? SeriesData(), series: true) : path == "/metadata.json" ? Captions :
            path.EndsWith(".vtt") ? CaptionChecks.Caption : path == "/full.m3u8" ? "#EXTM3U\n#EXTINF:2,\nhttps://hls.ted.com/segment.ts\n#EXT-X-ENDLIST" : "media bytes";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) };
    })));
    private static NativeDownloadRequest Request(string directory) => new() { Url = Url, OutputDirectory = directory, FileName = "talk", Retries = 0 };
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
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
