using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Vidyard;

internal static class VidyardChecks
{
    private const string Player = "MT1jv9tg2Cq1qWepjXv71v";
    private const string Video = "4er9G5PCXV7T0TXACMBJRA";
    private static JsonNode Data(bool playlist = false)
    {
        var first = JsonNode.Parse("""
            {"videoUuid":"4er9G5PCXV7T0TXACMBJRA","videoId":10,"chapterId":100,"name":"First video","description":"Description","status":"ready","type":"normal","videoJwt":null,"seconds":3,"milliseconds":3250,
             "thumbnailUrls":{"normal":"https://cdn.vidyard.com/thumbnail.jpg"},"captions":[{"language":"en","vttUrl":"https://cdn.vidyard.com/en.vtt?token=a%2Bb"}],
             "sources":{"mp4":[{"id":11,"profile":"1080p","url":"https://cdn.vidyard.com/first.mp4?token=a%2Bb","width":640,"height":360,"framerate":24}],
                        "hls":[{"id":null,"profile":"auto","url":"https://cdn.vidyard.com/master.m3u8?token=c%2Bd"}]}}
            """)!;
        var chapters = new JsonArray(first);
        if (playlist)
        {
            chapters.Add(null);
            var second = first.DeepClone(); second["videoUuid"] = "bbbbbbbbbbbbbbbbbbbbbb"; second["chapterId"] = 101; second["name"] = "Third video";
            second["sources"]!["mp4"]![0]!["url"] = "https://cdn.vidyard.com/third.mp4"; chapters.Add(second);
        }
        return new JsonObject { ["type"] = "pomo-player", ["payload"] = new JsonObject { ["playerUuid"] = Player, ["name"] = "Collection", ["chapters"] = chapters,
            ["hasAccessCodes"] = false, ["whitelistedEmbedDomains"] = new JsonArray(), ["player"] = new JsonObject { ["description"] = "Collection description" } } };
    }
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Vidyard separates player identity, video identity and format measurements", Metadata),
        ("Vidyard primary HLS and explicit progressive downloads retain selected captions", Downloads),
        ("Vidyard player playlists preserve positions and transfer only selected videos", Playlist),
        ("Vidyard share, player, background and hub aliases retain public identity", Aliases),
        ("Vidyard responsive-image and iframe embeds resolve through native discovery", Embeds),
        ("Vidyard restricted workflows and link offsets fail before media access", Access),
        ("Vidyard malformed player identities, sources and playlist snapshots fail", Invalid),
        ("Vidyard video archives skip media across parent-player aliases", Archive),
        ("Vidyard canceled discovery cannot publish files", Cancellation)
    ];
    private static async Task Metadata()
    {
        var calls = new List<Uri>(); var info = await Engine(calls).InspectAsync(Request(Path.GetTempPath()), default);
        var format = info.Formats.Single(format => format.Id == "mp4-11");
        Assert(info.Id == Video && !info.IsPlaylist && info.Title == "First video" && info.DurationSeconds == 3.25 && info.Formats.Count == 2 && info.Subtitles.Count == 1,
            "Player and video identity or timed metadata were confused.");
        Assert(format.Height == 360 && format.Width == 640 && format.FramesPerSecond == 24 && calls.Single().AbsolutePath == "/player/" + Player + ".json",
            "A profile label replaced offered format measurements or an unrelated endpoint was fetched.");
    }
    private static async Task Downloads()
    {
        using var files = new Files();
        foreach (var selector in new[] { "", "mp4-11" })
        {
            var calls = new List<Uri>();
            var result = await Engine(calls).DownloadAsync(Request(files.Directory) with { FileName = selector.Length == 0 ? "stream" : "direct", FormatSelector = selector, WriteSubs = true, SubLanguages = "en" }, null, null, default);
            Assert(result.Succeeded && File.ReadAllText(Path.ChangeExtension(result.OutputPath!, ".en.vtt")) == CaptionChecks.Caption, "The selected Vidyard media or captions were not saved.");
            Assert(selector.Length == 0 ? calls.Any(url => url.AbsolutePath == "/master.m3u8") && calls.All(url => url.AbsolutePath != "/first.mp4") : calls.Any(url => url.AbsolutePath == "/first.mp4" && url.Query == "?token=a%2Bb"),
                "Primary streaming preference or a signed progressive selection changed.");
        }
    }
    private static async Task Playlist()
    {
        using var files = new Files(); var calls = new List<Uri>(); var engine = Engine(calls, Data(true));
        var info = await engine.InspectAsync(Request(files.Directory), default);
        Assert(info.IsPlaylist && info.Id == Player && info.Entries.Count == 3 && !info.Entries[1].Available && calls.Count == 1, "Player videos were concatenated or unavailable positions disappeared.");
        calls.Clear(); var result = await engine.DownloadAsync(Request(files.Directory) with { PlaylistItems = "3", FormatSelector = "mp4-11" }, null, null, default);
        Assert(result.Succeeded && result.OutputPaths.Count == 1 && calls.Count == 2 && calls[1].AbsolutePath == "/third.mp4", "An unselected player video was transferred.");
        await Expect<InvalidDataException>(() => Engine([], Data(true)).InspectAsync(Request(files.Directory) with { MaximumPlaylistEntries = 2 }, default));
    }
    private static async Task Aliases()
    {
        var provider = new VidyardProvider();
        foreach (var url in new[] { "https://play.vidyard.com/" + Player, "https://embed.vidyard.com/share/" + Player, "https://share.vidyard.com/watch/" + Player,
            "https://example.hubs.vidyard.com/watch/" + Player, "https://play.vidyard.com/" + Player + "/type/background" })
        {
            Assert(provider.CanHandle(new(url)) && (await Engine([]).InspectAsync(Request(Path.GetTempPath()) with { Url = new(url) }, default)).Id == Video, "A public Vidyard alias changed the selected video.");
        }
        Assert(!provider.CanHandle(new("https://play.vidyard.com.evil.example/" + Player)) && !provider.CanHandle(new("https://user@play.vidyard.com/" + Player)), "An invalid Vidyard origin was accepted.");
    }
    private static async Task Embeds()
    {
        using var files = new Files();
        foreach (var kind in new[] { "image", "iframe" })
        {
            var calls = new List<Uri>();
            var result = await Engine(calls).DownloadAsync(Request(files.Directory) with { Url = new("https://article.example/" + kind), FormatSelector = "mp4-11", FileName = kind }, null, null, default);
            Assert(result.Succeeded && calls.Count == 3 && calls[1].Host == "play.vidyard.com", "A supported Vidyard embed did not use native player discovery.");
        }
        var offset = YoutubeDl.Engine.Discovery.EmbeddedMediaReader.Read("<img class=\"vidyard-player-embed\" data-uuid=\"" + Player + "\" data-second=\"15\">", new("https://article.example/offset"));
        Assert(offset.Single().Query == "?second=15", "A responsive embed's unsupported time selection was silently discarded.");
    }
    private static async Task Access()
    {
        foreach (var change in new Action<JsonNode>[] { data => data["type"] = "password", data => data["payload"]!["hasAccessCodes"] = true,
            data => data["payload"]!["whitelistedEmbedDomains"] = new JsonArray("secure.example") })
        { var data = Data(); change(data); var calls = new List<Uri>(); await Expect<NotSupportedException>(() => Engine(calls, data).InspectAsync(Request(Path.GetTempPath()), default)); Assert(calls.Count == 1, "Access checks fetched another endpoint."); }
        foreach (var query in new[] { "?vyac=code", "?chapter=1", "?start=10", "?second=15", "#vyac_token=token" })
        {
            var calls = new List<Uri>(); await Expect<NotSupportedException>(() => Engine(calls).InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://play.vidyard.com/" + Player + ".html" + query) }, default));
            Assert(calls.Count == 0, "An unsupported offset or access token was silently removed.");
        }
        var restricted = Data(); restricted["payload"]!["chapters"]![0]!["videoJwt"] = "authorization-required";
        Assert((await Engine([], restricted).InspectAsync(Request(Path.GetTempPath()), default)).Formats.Count == 0, "A token-requiring video was exposed as playable.");
    }
    private static async Task Invalid()
    {
        foreach (var change in new Action<JsonNode>[] { data => data["payload"]!["playerUuid"] = "bbbbbbbbbbbbbbbbbbbbbb", data => data["payload"]!["chapters"] = null,
            data => data["payload"]!["chapters"]![0]!["videoUuid"] = "invalid", data => data["payload"]!["chapters"]![0]!["sources"]!["mp4"]![0] = 1,
            data => data["payload"]!["chapters"]![0]!["sources"]!["mp4"]![0]!["url"] = "https://evil.example/video.mp4" })
        { var data = Data(); change(data); await Expect<InvalidDataException>(() => Engine([], data).InspectAsync(Request(Path.GetTempPath()), default)); }
        var duplicate = Data(true); duplicate["payload"]!["chapters"]![2]!["chapterId"] = 100;
        await Expect<InvalidDataException>(() => Engine([], duplicate).InspectAsync(Request(Path.GetTempPath()), default));
    }
    private static async Task Archive()
    {
        using var files = new Files(); var path = Path.Combine(files.Directory, "archive.txt"); await File.WriteAllTextAsync(path, "vidyard " + Video + "\n");
        var calls = new List<Uri>(); var result = await Engine(calls).DownloadAsync(Request(files.Directory) with { DownloadArchiveFile = path }, null, null, default);
        Assert(result.Succeeded && result.Skipped && calls.Count == 1, "The archive did not use the underlying video identity.");
    }
    private static async Task Cancellation()
    {
        using var files = new Files(); using var stop = new CancellationTokenSource();
        await Expect<OperationCanceledException>(() => Engine([], before: _ => stop.Cancel()).DownloadAsync(Request(files.Directory), null, null, stop.Token));
        Assert(!Directory.EnumerateFiles(files.Directory).Any(), "Canceled Vidyard discovery published files.");
    }
    private static NativeEngine Engine(List<Uri> calls, JsonNode? data = null, Action<Uri>? before = null) => new(httpFactory: _ => new MediaHttp(new Handler(request =>
    {
        var url = request.RequestUri!; calls.Add(url); before?.Invoke(url);
        var text = url.Host == "article.example" ? url.AbsolutePath == "/image" ? "<img class=\"vidyard-player-embed\" data-uuid=\"" + Player + "\" src=\"https://play.vidyard.com/" + Player + ".jpg\">" : "<iframe src=\"https://play.vidyard.com/" + Player + ".html\"></iframe>" :
            url.Host == "play.vidyard.com" ? (data ?? Data()).ToJsonString() : url.AbsolutePath.EndsWith(".vtt") ? CaptionChecks.Caption :
            url.AbsolutePath == "/master.m3u8" ? "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=100000,RESOLUTION=640x360\nlow.m3u8" :
            url.AbsolutePath == "/low.m3u8" ? "#EXTM3U\n#EXTINF:3.25,\nsegment.ts\n#EXT-X-ENDLIST" : "media bytes";
        return new(HttpStatusCode.OK) { Content = new StringContent(text) };
    })));
    private static NativeDownloadRequest Request(string directory) => new() { Url = new("https://play.vidyard.com/" + Player + ".html"), OutputDirectory = directory, FileName = "video", Retries = 0 };
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
