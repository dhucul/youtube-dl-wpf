using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.Wistia;
using YoutubeDl.Engine.Providers.JwPlatform;

internal static class HostingChecks
{
    private const string WistiaJson = """
    {"media":{"hashedId":"e4a27b971d","name":"Wistia fixture","duration":16.5,"assets":[
      {"type":"original","url":"https://cdn.example/original.bin","status":2},
      {"type":"hd_mp4_video","container":"mp4","url":"https://cdn.example/video.bin","status":2,"width":1280,"height":720,"size":1000,"bitrate":2000,"codec":"h264"},
      {"type":"still_image","url":"https://cdn.example/image.bin","status":2}
    ]}}
    """;
    private const string JwJson = """
    {"title":"JW fixture","playlist":[{"mediaid":"AbCd1234","title":"Item title","duration":12,"image":"https://cdn.example/poster.jpg",
      "sources":[{"file":"https://cdn.example/file.mp4","type":"video/mp4","width":640,"height":360,"bitrate":200000,"filesize":12},
                 {"file":"https://cdn.example/protected.mpd","type":"application/dash+xml","drm":{"widevine":{}}}],
      "tracks":[{"kind":"captions","file":"https://cdn.example/en.vtt","language":"en"},{"kind":"thumbnails","file":"https://cdn.example/strip.vtt"}]
    }]}
    """;
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Wistia media URLs resolve hashed IDs without confusing share slugs", () => Sync(WistiaAddresses)),
        ("Wistia assets retain container, codec, dimensions, and sizes", () => Sync(WistiaMetadata)),
        ("JW delivery/player URLs preserve identities and signed endpoint queries", () => Sync(JwAddresses)),
        ("JW source parsing excludes DRM and retains caption tracks", () => Sync(JwMetadata)),
        ("JW playlist snapshots preserve per-entry formats without re-extraction", JwPlaylist),
        ("Native Wistia and JW providers complete exposed fixture media transfers", Download),
        ("HTML embed discovery handles supported iframe and script patterns", () => Sync(EmbedReader)),
        ("Generic pages download a single known-provider embed through C#", SingleEmbed),
        ("Pages with several embeds retain successful files and failed entry details", MultipleEmbeds),
        ("Recursive embedded media references fail without publishing files", Recursive)
    ];
    private static void WistiaAddresses()
    {
        foreach (var path in new[] { "medias/e4a27b971d", "medias/e4a27b971d/manage", "m/e4a27b971d", "embed/iframe/e4a27b971d", "embed/medias/e4a27b971d.jsonp" })
            Assert(WistiaProvider.ParseIdentifier(new Uri("https://home.wistia.com/" + path)) == "e4a27b971d", "Wistia media alias changed.");
        Throws<NotSupportedException>(() => WistiaProvider.ParseIdentifier(new Uri("https://home.wistia.com/s/slug")));
        Throws<ArgumentException>(() => WistiaProvider.ParseIdentifier(new Uri("https://wistia.com.evil.example/medias/e4a27b971d")));
    }
    private static void WistiaMetadata()
    {
        var page = WistiaProvider.ParseMedia(WistiaJson, "e4a27b971d");
        Assert(page.Media.Single() is { Extension: "mp4", Height: 720, FileSize: 1000, VideoCodec: "h264", Bandwidth: 2000000 } && page.ThumbnailUrl == "https://cdn.example/image.bin", "Wistia asset facts or thumbnail changed.");
        Throws<InvalidDataException>(() => WistiaProvider.ParseMedia(WistiaJson, "wrong"));
        Throws<InvalidDataException>(() => WistiaProvider.ParseMedia(WistiaJson.Replace("https://cdn.example/video.bin", "file:///private.bin"), "e4a27b971d"));
    }
    private static void JwAddresses()
    {
        var address = JwPlatformProvider.ParseAddress(new Uri("https://cdn.jwplayer.com/v2/media/AbCd1234?token=fixture"));
        Assert(address.Id == "AbCd1234" && address.Endpoint.Query == "?token=fixture", "Signed media endpoint query was dropped.");
        Assert(JwPlatformProvider.ParseAddress(new Uri("https://content.jwplatform.com/players/AbCd1234-EfGh5678.js")).Endpoint.AbsoluteUri == "https://cdn.jwplayer.com/v2/media/AbCd1234", "Player alias did not resolve media.");
        Throws<NotSupportedException>(() => JwPlatformProvider.ParseAddress(new Uri("https://cdn.jwplayer.com/players/AbCd1234-EfGh5678.html?token=fixture")));
        Throws<ArgumentException>(() => JwPlatformProvider.ParseAddress(new Uri("https://cdn.jwplayer.com.evil.example/v2/media/AbCd1234")));
    }
    private static void JwMetadata()
    {
        var address = JwPlatformProvider.ParseAddress(new Uri("https://cdn.jwplayer.com/v2/media/AbCd1234"));
        var page = JwPlatformProvider.ParseDelivery(JwJson, address);
        Assert(page.Media.Count == 1 && page.Media[0].Height == 360 && page.Subtitles.Single().Language == "en", "JW protected source or thumbnail strip became media/captions.");
        Assert(JwPlatformProvider.ParseDelivery(JwJson.Replace("\"filesize\":12", "\"filesize\":12,\"drm\":{}"), address).Media.Count == 1, "An empty DRM descriptor marked clear media as protected.");
        Throws<InvalidDataException>(() => JwPlatformProvider.ParseDelivery(JwJson.Replace("AbCd1234", "Other123"), address));
    }
    private static async Task JwPlaylist()
    {
        using var files = new Files();
        var calls = 0;
        var engine = Engine(request => { calls++; return Ok(request.RequestUri!.Host == "cdn.jwplayer.com" ? JwJson : "fixture media"); });
        var result = await engine.DownloadAsync(Request(files.Directory, "https://cdn.jwplayer.com/v2/playlists/List1234"), null, null, default);
        Assert(result.Succeeded && calls == 2 && result.OutputPaths.Count == 1 && Path.GetFileName(result.OutputPaths[0]).StartsWith("001 - "), "JW playlist entry was re-extracted or lost its original position.");
    }
    private static async Task Download()
    {
        using var files = new Files();
        foreach (var (url, json) in new[] { ("https://home.wistia.com/medias/e4a27b971d", WistiaJson), ("https://cdn.jwplayer.com/v2/media/AbCd1234", JwJson) })
        {
            var calls = new List<string>();
            var result = await Engine(request => { calls.Add(request.RequestUri!.Host); return Ok(request.RequestUri.Host is "fast.wistia.com" or "cdn.jwplayer.com" ? json : "fixture media"); })
                .DownloadAsync(Request(files.Directory, url), null, null, default);
            Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == "fixture media" && calls.Count == 2, "Native hosting provider transfer was changed.");
        }
    }
    private static void EmbedReader()
    {
        const string html = """
        <iframe title="Video" src="https://youtube.com/embed/abcdefghijk?start=1&amp;end=3"></iframe>
        <iframe src="https://youtube.com/embed/abcdefghijk?start=1&amp;end=3"></iframe>
        <div class="other wistia_embed wistia_async_e4a27b971d"></div>
        <wistia-player media-id="e4a27b971d"></wistia-player>
        <script data-video="x84sh87" src="https://geo.dailymotion.com/player/demo.js"></script>
        <script src="https://cdn.jwplayer.com/players/AbCd1234-EfGh5678.js"></script>
        <iframe src="javascript:sideEffect()"></iframe><iframe src="https://user@private.example/video"></iframe>
        """;
        var urls = EmbeddedMediaReader.Read(html, new Uri("https://article.example/"));
        Assert(urls.Count == 4 && urls[0].Query == "?start=1&end=3" && urls.Any(url => url.Host == "fast.wistia.com"), "Embed decoding/deduplication/boundaries changed.");
    }
    private static async Task SingleEmbed()
    {
        using var files = new Files();
        var calls = new List<string>();
        var engine = Engine(request =>
        {
            var host = request.RequestUri!.Host; calls.Add(host);
            return Ok(host == "article.example" ? "<title>Article</title><script>const capabilities='widevine';</script><iframe src='https://youtube.com/embed/abcdefghijk'></iframe>" : host == "www.youtube.com" ? YouTubeChecks.FixtureHtml : "combined");
        });
        var result = await engine.DownloadAsync(Request(files.Directory, "https://article.example/watch") with { FormatId = "18" }, null, null, default);
        Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == "combined" && calls.SequenceEqual(["article.example", "www.youtube.com", "r1.googlevideo.com"]), "The single embedded provider did not remain in the native path.");
    }
    private static async Task MultipleEmbeds()
    {
        using var files = new Files();
        var engine = Engine(request =>
        {
            if (request.RequestUri!.Host == "article.example") return Ok("<title>Article</title><iframe src='https://youtube.com/embed/abcdefghijk'></iframe><iframe src='https://youtube.com/embed/bbbbbbbbbbb'></iframe>");
            if (request.RequestUri.Host == "www.youtube.com") return request.RequestUri.Query.Contains("bbbbbbbbbbb") ? new(HttpStatusCode.NotFound) : Ok(YouTubeChecks.FixtureHtml);
            return Ok("combined");
        });
        var result = await engine.DownloadAsync(Request(files.Directory, "https://article.example/watch") with { FormatId = "18" }, null, null, default);
        Assert(!result.Succeeded && result.OutputPaths.Count == 1 && result.Failures.Count == 1 && result.Failures[0].Position == 2 && File.ReadAllText(result.OutputPaths[0]) == "combined", "One unavailable embed discarded completed media.");
    }
    private static async Task Recursive()
    {
        using var files = new Files();
        var engine = new NativeEngine([new RecursiveProvider()], _ => new MediaHttp(new Handler(_ => throw new InvalidOperationException("No media transfer is allowed."))));
        try { await engine.DownloadAsync(Request(files.Directory, "https://recursive.example/one"), null, null, default); }
        catch (InvalidDataException) { Assert(!Directory.EnumerateFiles(files.Directory).Any(), "Recursive media was published."); return; }
        throw new InvalidOperationException("Recursive media references were accepted.");
    }
    private sealed class RecursiveProvider : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("recursive-fixture", "Recursive fixture", 200);
        public bool CanHandle(Uri url) => url.Host == "recursive.example";
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(new PageMetadata
        { Title = "Recursive", EmbeddedPages = [new Uri("https://recursive.example/" + (request.Url.AbsolutePath == "/one" ? "two" : "one"))] });
    }
    private static NativeDownloadRequest Request(string directory, string url) => new() { Url = new Uri(url), OutputDirectory = directory, Retries = 0 };
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
