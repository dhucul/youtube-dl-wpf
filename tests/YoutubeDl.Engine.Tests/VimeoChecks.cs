using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Vimeo;

internal static class VimeoChecks
{
    private const string Json = """
        {"video":{"id":12345,"title":"Vimeo fixture","duration":12.5,"thumbnail_url":"https://i.vimeocdn.com/fixture.jpg","owner":{"name":"Creator"}},
         "request":{"files":{"progressive":[{"quality":"360p","url":"https://cdn.example/fixture.mp4","mime":"video/mp4","height":360,"width":640}],
          "hls":{"default_cdn":"cdn","cdns":{"cdn":{"url":"https://cdn.example/master.m3u8"}}},
          "dash":{"default_cdn":"cdn","cdns":{"cdn":{"url":"https://cdn.example/master.mpd"}}}},
          "text_tracks":[{"lang":"en","url":"https://cdn.example/caption.vtt"}]}}
        """;
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Native Vimeo addresses retain unlisted-video access identifiers", () => Sync(Addresses)),
        ("Vimeo configuration preserves metadata and exposed transports", () => Sync(Configuration)),
        ("Native Vimeo inspection uses its C# configuration provider", Provider),
        ("Native Vimeo downloads exposed streams with unlisted access preserved", Download),
        ("Mismatched Vimeo responses and malformed addresses fail explicitly", () => Sync(Invalid)),
        ("Vimeo reports protected manifests without offering them for native transfer", Protected),
        ("Vimeo retains clear alternatives, codec facts and unique format identities", () => Sync(FormatFacts)),
        ("Vimeo current player states and selected-CDN alternatives are validated", () => Sync(CurrentStates)),
        ("Vimeo declarative embeds retain unlisted access and reject conflicting identities", Embeds),
        ("Vimeo provided file links retain signatures and validate redirected media headers", FileLinks),
        ("Expired or non-media Vimeo file links fail without alternate access attempts", FileFailures)
    ];
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Addresses()
    {
        foreach (var url in new[] { "https://vimeo.com/12345", "https://player.vimeo.com/video/12345", "https://vimeo.com/channels/staffpicks/12345", "https://vimeo.com/groups/shortfilms/videos/12345", "https://vimeo.com/showcase/555/video/12345" })
            Assert(VimeoAddress.Parse(new Uri(url)).VideoId == "12345", "A Vimeo URL alias was lost.");
        Assert(VimeoAddress.Parse(new Uri("https://vimeo.com/12345/abc123")).ConfigurationUrl.Query == "?h=abc123", "The unlisted hash was lost.");
        Assert(VimeoAddress.Parse(new Uri("https://player.vimeo.com/video/12345?h=abc123")).UnlistedHash == "abc123", "The embedded unlisted hash was lost.");
        Assert(!VimeoAddress.IsVimeoHost(new Uri("https://vimeo.com.evil.example/12345")), "Spoofed Vimeo host accepted.");
        Assert(VimeoAddress.Parse(new("https://vimeo.com/00012345?%68=abc123")).ConfigurationUrl.AbsoluteUri == "https://player.vimeo.com/video/12345/config?h=abc123", "Normalized identity or encoded access parameter was lost.");
    }
    private static void Configuration()
    {
        var metadata = VimeoProvider.ParseConfiguration(Json, "12345");
        Assert(metadata.Id == "12345" && metadata.Extractor == "vimeo" && metadata.DurationSeconds == 12.5 && metadata.Uploader == "Creator", "Vimeo metadata was lost.");
        Assert(metadata.Media.Count == 3 && metadata.Media[0].HasAudio == true && metadata.Media[0].Height == 360 && metadata.Subtitles.Single().Language == "en", "Vimeo formats/captions were lost.");
    }
    private static async Task Provider()
    {
        var requests = new List<Uri>();
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request =>
        {
            requests.Add(request.RequestUri!); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json) };
        })));
        var info = await engine.InspectAsync(new NativeDownloadRequest { Url = new Uri("https://vimeo.com/12345/abc123"), OutputDirectory = Path.GetTempPath() }, default);
        Assert(info.Extractor == "vimeo" && requests.Single().AbsoluteUri == "https://player.vimeo.com/video/12345/config?h=abc123", "The native provider did not use the expected configuration URL.");
    }
    private static void Invalid()
    {
        Throws<InvalidDataException>(() => VimeoProvider.ParseConfiguration(Json, "other"));
        Throws<ArgumentException>(() => VimeoAddress.Parse(new Uri("https://vimeo.com/12345/abc?h=def")));
        Throws<NotSupportedException>(() => VimeoAddress.Parse(new Uri("https://vimeo.com/channels/staffpicks")));
        Throws<NotSupportedException>(() => VimeoAddress.Parse(new Uri("https://vimeo.com/showcase/12345")));
        Throws<NotSupportedException>(() => VimeoAddress.Parse(new Uri("https://player.vimeo.com/video/12345/config")));
        Throws<InvalidDataException>(() => VimeoProvider.ParseConfiguration(new string(' ', 2 * 1024 * 1024 + 1), "12345"));
    }
    private static async Task Download()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var requests = new List<Uri>();
            var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request =>
            {
                requests.Add(request.RequestUri!);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.Host == "player.vimeo.com" ? Json : "vimeo-media") };
            })));
            var result = await engine.DownloadAsync(new NativeDownloadRequest
            { Url = new Uri("https://vimeo.com/12345/abc123"), OutputDirectory = directory, FormatId = "360p", Retries = 0 }, null, null, default);
            Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == "vimeo-media" && requests.Count == 2 && requests[0].Query == "?h=abc123",
                "The native Vimeo path lost access context or delegated media transfer.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
    private static JsonNode Data() => JsonNode.Parse(Json)!;
    private static async Task Protected()
    {
        var data = Data(); data["view"] = 1; data["request"]!["files"]!["progressive"] = new JsonArray();
        data["request"]!["files"]!["hls"]!["cdns"]!["cdn"]!["url"] = "https://vod.example/v2/playlist/drm/cbcs,fixture/playlist.m3u8?signature=a%2Bb";
        data["request"]!["files"]!["dash"]!["cdns"]!["cdn"]!["url"] = "https://vod.example/v2/playlist/drm/cenc,fixture/playlist.mpd?signature=c%2Bd";
        var metadata = VimeoProvider.ParseConfiguration(data.ToJsonString(), "12345");
        Assert(metadata.Media.Count == 0 && metadata.DrmRefused && metadata.ReportedFormats.Count == 2 && metadata.ReportedFormats.All(format => format.Availability == FormatAvailability.DrmProtected && format.Url is null),
            "Protected Vimeo formats were advertised as downloadable.");
        using var files = new Files(); var calls = 0;
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(_ => { calls++; return Response(data.ToJsonString()); })));
        var info = await engine.InspectAsync(Request(files.Directory), default);
        var result = await engine.DownloadAsync(Request(files.Directory), null, null, default);
        Assert(info.Formats.All(format => format.Availability == FormatAvailability.DrmProtected) && !result.Succeeded && calls == 2 && !Directory.EnumerateFiles(files.Directory).Any(),
            "A protected configuration fetched media or published a successful output.");
    }
    private static void FormatFacts()
    {
        var data = Data(); var file = data["request"]!["files"]!["progressive"]![0]!;
        file["fps"] = 29.97; file["size"] = 123456; file["mime"] = "video/mp4; codecs=\"avc1.4d401e,mp4a.40.2\"";
        data["request"]!["files"]!["progressive"]!.AsArray().Add(file.DeepClone());
        data["request"]!["files"]!["hls"]!["drm"] = new JsonObject { ["scheme"] = "protected" };
        var page = VimeoProvider.ParseConfiguration(data.ToJsonString(), "12345");
        Assert(!page.DrmRefused && page.Media.Count == 3 && page.ReportedFormats.Count == 4 && page.ReportedFormats.Select(format => format.Id).Distinct().Count() == 4, "A protected alternative hid clear files or format IDs collided.");
        var first = page.Media[0];
        Assert(first.Id == "360p" && first.FramesPerSecond == 29.97 && first.VideoCodec == "avc1.4d401e" && first.AudioCodec == "mp4a.40.2" && first.FileSize == 123456,
            "Vimeo discarded offered FPS/codec/size facts.");
        file["height"] = 360.5;
        Assert(VimeoProvider.ParseConfiguration(data.ToJsonString(), "12345").Media[0].Height is null, "A fractional height became a fabricated integer resolution.");
    }
    private static void CurrentStates()
    {
        var data = Data(); data["view"] = 2;
        Throws<NotSupportedException>(() => VimeoProvider.ParseConfiguration(data.ToJsonString(), "12345"));
        data = Data(); var cdn = data["request"]!["files"]!["hls"]!["cdns"]!["cdn"]!;
        cdn["avc_url"] = "https://cdn.example/avc.m3u8?literal=a%2Bb"; cdn["fallback_url"] = "https://cdn.example/fallback.m3u8";
        var page = VimeoProvider.ParseConfiguration(data.ToJsonString(), "12345");
        Assert(page.Media.Single(format => format.Id == "hls-avc").VideoCodec == "avc1" && page.Media.Single(format => format.Id == "hls-fallback").IsFallback,
            "Vimeo's offered alternate manifests lost their facts or priority.");
        var preferred = VimeoProvider.ParseConfiguration(data.ToJsonString(), "12345", "h264");
        Assert(preferred.Media.Single(format => format.Id == "hls").IsFallback && !preferred.Media.Single(format => format.Id == "hls-avc").IsFallback, "The requested AVC preference did not prioritize its supplied manifest.");
        cdn["fallback_url"] = cdn["url"]!.DeepClone();
        Assert(VimeoProvider.ParseConfiguration(data.ToJsonString(), "12345").Media.Count == 4, "Duplicate CDN variants were not collapsed.");
    }
    private static async Task Embeds()
    {
        using var files = new Files(); var requests = new List<Uri>();
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request =>
        {
            requests.Add(request.RequestUri!);
            return Response(request.RequestUri!.Host == "article.example" ? "<div data-vimeo-id=\"12345\" data-vimeo-url=\"https://player.vimeo.com/video/12345?h=abc123\"></div>" : request.RequestUri.Host == "player.vimeo.com" ? Json : "vimeo-media");
        })));
        var result = await engine.DownloadAsync(Request(files.Directory) with { Url = new("https://article.example/post"), FormatId = "360p" }, null, null, default);
        Assert(result.Succeeded && requests.Count == 3 && requests[1].Query == "?h=abc123", "A declarative Vimeo embed lost its access context or native dispatch.");
        Throws<InvalidDataException>(() => YoutubeDl.Engine.Discovery.EmbeddedMediaReader.Read("<div data-vimeo-id=\"54321\" data-vimeo-url=\"https://vimeo.com/12345\"></div>", new("https://article.example/")));
    }
    private static async Task FileLinks()
    {
        using var files = new Files(); var calls = new List<Uri>();
        var original = new Uri("https://player.vimeo.com/progressive_redirect/playback/12345/container/fixture-container/asset?signature=a%2Bb%2Fc&expires=4102444800");
        var final = new Uri("https://cdn.example/signed.mp4?signature=literal%2Bvalue");
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request =>
        {
            calls.Add(request.RequestUri!); var response = Response("vimeo-media"); response.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            if (request.RequestUri!.Host == "player.vimeo.com") response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, final);
            return response;
        })));
        var result = await engine.DownloadAsync(Request(files.Directory) with { Url = original }, null, null, default);
        Assert(result.Succeeded && calls.SequenceEqual(new[] { original, final }) && File.ReadAllText(result.OutputPath!) == "vimeo-media", "A signed file link was rewritten, re-resolved as a player, or downloaded incorrectly.");
        foreach (var link in new[] { "https://player.vimeo.com/external/12345.hd.mp4?signature=test", "https://player.vimeo.com/play/c/12345/hls.m3u8?signature=test", "https://player.vimeo.com/play/c/12345/dash.mpd?signature=test" })
            Assert(new VimeoProvider().GetArchiveIdentity(Request(files.Directory) with { Url = new(link) })!.Id == "12345", "A supplied file-link alias lost the underlying video archive identity.");
    }
    private static async Task FileFailures()
    {
        using var files = new Files(); var url = new Uri("https://player.vimeo.com/external/12345.mp4?signature=test");
        foreach (var mime in new[] { "text/html", "application/json" })
        {
            var calls = 0; var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(_ => { calls++; var response = Response("unavailable"); response.Content.Headers.ContentType = new(mime); return response; })));
            await Expect<NotSupportedException>(() => engine.DownloadAsync(Request(files.Directory) with { Url = url }, null, null, default));
            Assert(calls == 1 && !Directory.EnumerateFiles(files.Directory).Any(), "A non-media response became a Vimeo download.");
        }
        var deniedCalls = 0; var denied = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(_ => { deniedCalls++; return new(HttpStatusCode.Forbidden); })));
        await Expect<HttpRequestException>(() => denied.InspectAsync(Request(files.Directory) with { Url = url }, default));
        Assert(deniedCalls == 1, "A denied Vimeo link was retried through an alternate access route.");
    }
    private static NativeDownloadRequest Request(string folder) => new() { Url = new("https://vimeo.com/12345"), OutputDirectory = folder, FileName = "vimeo", Retries = 0 };
    private static HttpResponseMessage Response(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, "vimeo-" + Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() { var path = Path.GetFullPath(Directory); if (!path.StartsWith(Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Test folder escaped its root."); System.IO.Directory.Delete(path, true); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request));
    }
}
