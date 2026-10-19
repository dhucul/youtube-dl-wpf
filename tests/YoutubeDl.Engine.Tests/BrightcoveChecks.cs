using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.Brightcove;

internal static class BrightcoveChecks
{
    private const string Json = """
    {"account_id":"12345","id":"67890","reference_id":"fixture-ref","name":"Brightcove fixture","description":"Description","duration":8568,"poster":"https://img.example/poster.jpg",
      "sources":[{"src":"https://cdn.example/media.mp4","container":"MP4","codec":"H264","width":1280,"height":720,"size":1234},
                 {"src":"https://cdn.example/master.m3u8","type":"application/x-mpegURL"},
                 {"src":"https://cdn.example/protected.mpd","type":"application/dash+xml","key_systems":{"com.widevine.alpha":{}}}],
      "text_tracks":[{"src":"https://cdn.example/en.vtt","srclang":"en","kind":"captions"},{"src":"https://cdn.example/meta.vtt","kind":"metadata"}]}
    """;
    private const string Player = "https://players.brightcove.net/12345/default_default/index.html?videoId=67890";
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Brightcove player addresses retain account/player and reference identities", () => Sync(Addresses)),
        ("Brightcove playback metadata retains clear sources and millisecond duration", () => Sync(Metadata)),
        ("Brightcove mismatched identities and source URLs fail explicitly", () => Sync(Invalid)),
        ("Native Brightcove requests use the player policy only for playback metadata", Download),
        ("Brightcove playlists retain resolved video metadata and reject truncated pages", () => Sync(Playlist)),
        ("Brightcove in-page tags resolve to native player URLs", () => Sync(Embeds)),
        ("Audio-only content downloads in the default mode without changing explicit video requests", AudioContent),
        ("Generic page redirects use the final URI when resolving relative media", RedirectBase),
        ("Generic redirects to direct media use headers without reading the entire body", RedirectMedia)
    ];
    private static void Addresses()
    {
        var address = BrightcoveAddress.Parse(new Uri(Player));
        Assert(address.Account == "12345" && address.PlayerEmbed == "default_default" && address.Playback.AbsolutePath.EndsWith("/videos/67890"), "Player/account route changed.");
        var reference = BrightcoveAddress.Parse(new Uri(Player.Replace("67890", "ref:fixture-ref")));
        Assert(reference.MediaId == "ref:fixture-ref", "Reference identity lost.");
        Throws<ArgumentException>(() => BrightcoveAddress.Parse(new Uri(Player + "&videoId=other")));
        Throws<ArgumentException>(() => BrightcoveAddress.Parse(new Uri(Player.Replace("players.brightcove.net", "players.brightcove.net.evil.example"))));
    }
    private static void Metadata()
    {
        var page = BrightcoveProvider.ParsePlayback(Json, BrightcoveAddress.Parse(new Uri(Player)));
        Assert(page.DurationSeconds == 8.568 && page.Media.Count == 2 && page.Media[0] is { Height: 720, FileSize: 1234, VideoCodec: "H264" } && page.Subtitles.Single().Language == "en", "Playback facts were changed or protected/metadata tracks exposed.");
        Assert(BrightcoveProvider.ParsePlayback(Json, BrightcoveAddress.Parse(new Uri(Player.Replace("67890", "ref:fixture-ref")))).Id == "67890", "Reference response did not retain numeric identity.");
    }
    private static void Invalid()
    {
        var address = BrightcoveAddress.Parse(new Uri(Player));
        Throws<InvalidDataException>(() => BrightcoveProvider.ParsePlayback(Json.Replace("12345", "99999"), address));
        Throws<InvalidDataException>(() => BrightcoveProvider.ParsePlayback(Json.Replace("67890", "99999"), address));
        Throws<InvalidDataException>(() => BrightcoveProvider.ParsePlayback(Json.Replace("https://cdn.example/media.mp4", "file:///private.mp4"), address));
        Throws<InvalidOperationException>(() => BrightcoveProvider.ParsePlayback("[{\"error_code\":\"ACCESS_DENIED\"}]", address));
    }
    private static async Task Download()
    {
        using var files = new Files();
        var calls = new List<string>();
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request =>
        {
            calls.Add(request.RequestUri!.Host);
            if (request.RequestUri.Host == "players.brightcove.net")
                return Ok("{\"account_id\":\"12345\",\"video_cloud\":{\"policy_key\":\"fixture-policy\"}}");
            if (request.RequestUri.Host == "edge.api.brightcove.com")
            {
                var accept = request.Headers.Accept.Single();
                Assert(accept.MediaType == "application/json" && accept.Parameters.Single(parameter => parameter.Name == "pk").Value == "fixture-policy", "Playback policy was not supplied in its scoped request.");
                return Ok(Json);
            }
            Assert(!request.Headers.Contains("Accept"), "Player policy leaked into a media request.");
            return Ok("fixture media");
        })));
        var result = await engine.DownloadAsync(Request(files.Directory, Player) with { VideoCodec = "h264", FormatId = "progressive-0" }, null, null, default);
        Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == "fixture media" && calls.SequenceEqual(["players.brightcove.net", "edge.api.brightcove.com", "cdn.example"]), "The native playback/transfer path changed.");
    }
    private static void Playlist()
    {
        var address = BrightcoveAddress.Parse(new Uri(Player.Replace("videoId=67890", "playlistId=99999")));
        var json = "{\"account_id\":\"12345\",\"id\":\"99999\",\"name\":\"List\",\"videos\":[" + Json + "]}";
        var page = BrightcoveProvider.ParsePlayback(json, address);
        Assert(page.IsPlaylist && page.Entries.Single().ResolvedMedia!.Media.Count == 2, "Resolved playlist formats were lost.");
        Throws<NotSupportedException>(() => BrightcoveProvider.ParsePlayback(json.Replace("\"name\":\"List\"", "\"name\":\"List\",\"count\":2"), address));
    }
    private static void Embeds()
    {
        var urls = EmbeddedMediaReader.Read("<video-js data-account='12345' data-player='default' data-embed='default' data-video-id='67890'></video-js>", new Uri("https://article.example/"));
        Assert(urls.Single().AbsoluteUri == Player, "Brightcove in-page attributes did not resolve a player URL.");
    }
    private static async Task AudioContent()
    {
        using var files = new Files();
        var page = new PageMetadata { Id = "audio", Title = "Audio", Media = [new() { Url = new Uri("https://audio.example/file.mp3"), Extension = "mp3", HasAudio = true, HasVideo = false }] };
        var engine = new NativeEngine([new AudioProvider(page)], _ => new MediaHttp(new Handler(_ => Ok("audio bytes"))));
        var request = Request(files.Directory, "https://audio.example/file.mp3");
        var result = await engine.DownloadAsync(request, null, null, default);
        Assert(result.Succeeded && result.OutputPath!.EndsWith(".mp3"), "Audio-only content was rejected in the default download mode.");
        Throws<InvalidOperationException>(() => MediaDownloadPlan.Create(page, request with { VideoOnly = true }));
    }
    private static async Task RedirectBase()
    {
        using var files = new Files();
        var paths = new List<string>();
        var final = new Uri("https://cdn.example/show/index.html");
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request =>
        {
            paths.Add(request.RequestUri!.AbsoluteUri);
            if (request.RequestUri.Host == "article.example")
            {
                var response = Ok("<title>Redirected</title><video src='media/file.mp4'></video>");
                response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, final);
                response.Content.Headers.ContentType = new("text/html");
                return response;
            }
            return Ok("redirected media");
        })));
        var result = await engine.DownloadAsync(Request(files.Directory, "https://article.example/short"), null, null, default);
        Assert(result.Succeeded && paths[1] == "https://cdn.example/show/media/file.mp4", "Relative media resolved against the original redirected address.");
    }
    private static async Task RedirectMedia()
    {
        using var files = new Files();
        var final = new Uri("https://cdn.example/download");
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request =>
        {
            if (request.RequestUri!.Host == "article.example")
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new NeverReadStream()), RequestMessage = new HttpRequestMessage(HttpMethod.Get, final) };
                response.Content.Headers.ContentType = new("video/mp4");
                response.Content.Headers.ContentLength = 10_000_000;
                return response;
            }
            return Ok("direct redirected media");
        })));
        var result = await engine.DownloadAsync(Request(files.Directory, "https://article.example/download"), null, null, default);
        Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == "direct redirected media", "Direct redirect discovery read the body or lost its media MIME type.");
    }
    private sealed class NeverReadStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Discovery must not read a full media body.");
    }
    private sealed class AudioProvider(PageMetadata page) : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("fixture-audio-content", "Audio fixture", 200);
        public bool CanHandle(Uri url) => url.Host == "audio.example";
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(page);
    }
    private static NativeDownloadRequest Request(string directory, string url) => new() { Url = new Uri(url), OutputDirectory = directory, Retries = 0 };
    private static HttpResponseMessage Ok(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content) };
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
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
