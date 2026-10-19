using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Dailymotion;

internal static class DailymotionChecks
{
    private const string Json = """
    {"id":"xfixture","title":"Dailymotion fixture","duration":25,"description":"Description","owner":{"screenname":"Creator"},"stream_type":"recorded","thumbnails":{"180":"https://img.example/small.jpg","1080":"https://img.example/large.jpg"},
      "qualities":{"360":[{"type":"video/mp4; codecs=\"avc1.42001e, mp4a.40.2\"","url":"https://cdn.example/360.mp4","fps":30}],"auto":[{"type":"application/x-mpegURL","url":"https://cdn.example/master.m3u8"}]},
      "subtitles":{"enable":true,"data":{"en":{"url":"https://cdn.example/en.vtt"},"fr":{"urls":["https://cdn.example/fr.srt"],"automatic":true}}}}
    """;
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Dailymotion URLs retain video, short-link, embed, and playlist context", () => Sync(Addresses)),
        ("Dailymotion player metadata retains exposed streams, codecs, and captions", () => Sync(Metadata)),
        ("Dailymotion error, identity, and resource boundaries fail explicitly", () => Sync(Invalid)),
        ("Native Dailymotion exposed-stream downloading remains inside C#", Download),
        ("Dailymotion playlist pagination follows complete public page sequences", Playlist),
        ("Dailymotion repeated pages and configured limits cannot silently truncate", Bounds)
    ];
    private static void Addresses()
    {
        foreach (var url in new[] { "https://www.dailymotion.com/video/xfixture", "https://dai.ly/xfixture", "https://www.dailymotion.com/embed/video/xfixture", "https://dailymotion.com/video/xfixture_slug", "https://geo.dailymotion.com/player/demo.html?video=xfixture" })
            Assert(DailymotionAddress.Parse(new Uri(url)).VideoId == "xfixture", "A video URL alias was changed.");
        Assert(DailymotionAddress.Parse(new Uri("https://dailymotion.com/video/xfixture?playlist=xlist")).PlaylistId == "xlist", "Playlist context was lost.");
        Assert(DailymotionAddress.Parse(new Uri("https://dailymotion.com/playlist/xlist")).VideoId is null, "Playlist became a video.");
        Throws<ArgumentException>(() => DailymotionAddress.Parse(new Uri("https://dailymotion.com.evil.example/video/xfixture")));
        Throws<ArgumentException>(() => DailymotionAddress.Parse(new Uri("https://geo.dailymotion.com/player/demo.html?video=xfixture&video=other")));
    }
    private static void Metadata()
    {
        var page = DailymotionProvider.ParseMetadata(Json, "xfixture");
        Assert(page.Title == "Dailymotion fixture" && page.Uploader == "Creator" && page.DurationSeconds == 25 && page.ThumbnailUrl == "https://img.example/large.jpg", "Player metadata changed.");
        Assert(page.Media.Count == 2 && page.Media[0].VideoCodec == "avc1.42001e" && page.Media[0].AudioCodec == "mp4a.40.2" && page.Media[0].FramesPerSecond == 30, "Codec/frame-rate facts lost.");
        Assert(page.Subtitles.Count == 2 && page.Subtitles[1].Automatic && page.Subtitles[1].Extension == "srt", "Caption descriptors lost.");
    }
    private static void Invalid()
    {
        Throws<InvalidDataException>(() => DailymotionProvider.ParseMetadata(Json, "wrong"));
        Throws<InvalidDataException>(() => DailymotionProvider.ParseMetadata(Json.Replace("https://cdn.example/360.mp4", "file:///private.mp4"), "xfixture"));
        Throws<InvalidOperationException>(() => DailymotionProvider.ParseMetadata("{\"error\":{\"title\":\"Unavailable\"}}", "xfixture"));
        Throws<InvalidDataException>(() => DailymotionProvider.ParseMetadata(new string(' ', 4 * 1024 * 1024 + 1), "xfixture"));
    }
    private static async Task Download()
    {
        using var files = new Files();
        var requests = new List<string>();
        var engine = Engine(request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            return Ok(request.RequestUri.Host == "www.dailymotion.com" ? Json : "dailymotion media");
        });
        var result = await engine.DownloadAsync(Request(files.Directory) with { FormatId = "360-progressive-0", MaximumFrameRate = 30, VideoCodec = "h264" }, null, null, default);
        Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == "dailymotion media" && requests.Count == 2 && requests[0] == "https://www.dailymotion.com/player/metadata/video/xfixture", "The native Dailymotion transfer selected the wrong source.");
    }
    private static async Task Playlist()
    {
        var pages = new List<int>();
        var info = await PlaylistEngine(pages).InspectAsync(Request(Path.GetTempPath()) with { Url = new Uri("https://dailymotion.com/playlist/xlist") }, default);
        Assert(info.IsPlaylist && info.Entries.Select(entry => entry.Position).SequenceEqual([1, 2, 3]) && info.Entries[2].Id == "xthree" && pages.SequenceEqual([1, 2]), "Playlist pagination changed.");
    }
    private static async Task Bounds()
    {
        foreach (var repeat in new[] { false, true })
        {
            try { await PlaylistEngine([], repeat).InspectAsync(Request(Path.GetTempPath()) with { Url = new Uri("https://dailymotion.com/playlist/xlist"), MaximumPlaylistEntries = repeat ? 1000 : 2 }, default); }
            catch (InvalidDataException) { continue; }
            throw new InvalidOperationException("Incomplete playlist pagination was accepted.");
        }
    }
    private static NativeEngine PlaylistEngine(List<int> pages, bool repeated = false) => Engine(request =>
    {
        var uri = request.RequestUri!;
        if (!uri.AbsolutePath.EndsWith("/videos")) return Ok("{\"id\":\"xlist\",\"name\":\"Playlist fixture\"}");
        var page = uri.Query.Contains("page=1&") ? 1 : 2; pages.Add(page);
        var first = page == 1 || repeated;
        var list = first ? new[] { new { id = "xone", title = "First", duration = 1 }, new { id = "xtwo", title = "Second", duration = 2 } } : [new { id = "xthree", title = "Third", duration = 3 }];
        return Ok(JsonSerializer.Serialize(new { page, has_more = first, list }));
    });
    private static NativeDownloadRequest Request(string directory) => new() { Url = new Uri("https://dai.ly/xfixture"), OutputDirectory = directory, Retries = 0 };
    private static NativeEngine Engine(Func<HttpRequestMessage, HttpResponseMessage> response) => new(httpFactory: _ => new MediaHttp(new Handler(response)));
    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private sealed class Files : IDisposable
    {
        public string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        public Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
