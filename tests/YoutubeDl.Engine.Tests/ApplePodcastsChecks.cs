using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.ApplePodcasts;

internal static class ApplePodcastsChecks
{
    private static readonly Uri Show = new("https://podcasts.apple.com/ca/podcast/fixture/id123");
    private static readonly Uri Episode = new("https://podcasts.apple.com/ca/podcast/fixture/id123?i=900");
    private const string Catalogue = """
        {"resultCount":1,"results":[{"kind":"podcast","collectionId":123,"trackId":123,"trackName":"Catalogue show","artistName":"Artist","feedUrl":"https://feeds.example/show.xml"}]}
        """;
    private static JsonNode State() => JsonNode.Parse("""
        {"data":[{"intent":{"$kind":"EpisodePageIntent","adamId":"900","showAdamId":"123"},"data":{
          "pageAdamId":"900","pageContext":"episodePage","isIncomplete":false,"headerButtonItems":[{"modelType":"EpisodeLockup","model":{
            "$kind":"LegacyEpisodeLockup","adamId":"900","showAdamId":"123","title":"Episode & literal","channelName":"Channel","summary":"<p>First &amp; literal.</p><p>Second.</p>",
            "duration":1.5,"mediaType":"audio","entitlementState":"free","episodeArtwork":{"template":"https://is1-ssl.mzstatic.com/image/{w}x{h}bb.{f}","width":1600,"height":1600},
            "mediaEnclosures":[{"streamUrl":"https://media.example/episode.mp3?signature=a%2Bb","duration":1.5,"priceType":"STDQ","mediaKinds":["audio"]}]
          }}],"shelves":[{"items":[{"adamId":"999","mediaEnclosures":[{"streamUrl":"https://media.example/related.mp3"}]}]}]
        }}]}
        """)!;
    private static JsonNode Model(JsonNode state) => state["data"]![0]!["data"]!["headerButtonItems"]![0]!["model"]!;
    private static string Html(JsonNode state) => "<script type=\"application/json\" id=\"serialized-server-data\">" + state.ToJsonString() + "</script>";
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Apple show discovery preserves the requested storefront and full publisher feed", Collection),
        ("An explicit Apple episode remains a single exact-ID download with metadata", EpisodeDownload),
        ("Apple episode archives skip known numeric aliases before HTTP", Archive),
        ("Apple embed, legacy iTunes and regionless aliases share the native episode path", Aliases),
        ("Apple subscription access, previews and protected enclosures remain unavailable", Restrictions),
        ("Apple related episodes, wrong parents and changed redirects cannot replace the requested episode", Identity),
        ("Apple malformed catalogue identities, counts and publisher addresses fail explicitly", InvalidCatalogue),
        ("Apple malformed identifiers and conflicting page documents fail explicitly", InvalidPage),
        ("Apple show selection downloads only the selected feed episode", Selection),
        ("Opaque Apple media addresses use response headers without reading the discovery body", OpaqueMedia),
        ("Apple feed limits and cancellation stop before episode publication", LimitsAndCancellation)
    ];
    private static async Task Collection()
    {
        var requests = new List<Uri>();
        var info = await Engine(requests).InspectAsync(Request(Path.GetTempPath()) with { Url = Show }, default);
        Assert(info.IsPlaylist && info.Id == "123" && info.PlaylistId == "123" && info.Extractor == "apple:podcasts" && info.Title == "Fixture show" && info.Entries.Count == 3,
            "Apple collection identity or complete feed entries were lost.");
        Assert(requests.Count == 2 && requests[0].Host == "itunes.apple.com" && requests[0].Query.Contains("country=ca") && requests[1] == FeedChecks.Url, "The storefront or publisher feed was changed.");
    }
    private static async Task EpisodeDownload()
    {
        using var files = new Files(); var calls = new List<Uri>(); var engine = Engine(calls);
        var info = await engine.InspectAsync(Request(files.Directory), default);
        Assert(!info.IsPlaylist && info.Id == "900" && info.PlaylistId == "123" && info.Description == "First & literal.\n\nSecond." && info.DurationSeconds == 1.5 &&
            info.ThumbnailUrl == "https://is1-ssl.mzstatic.com/image/1200x1200bb.jpg" && info.Formats.Single().AudioOnly, "Apple episode metadata or artwork was lost.");
        calls.Clear(); var completed = 0;
        var result = await engine.DownloadAsync(Request(files.Directory) with { DownloadPlaylist = true, WriteInfoJson = true }, null, null, default, _ => completed++);
        Assert(result.Succeeded && result.OutputPaths.Count == 1 && completed == 1 && calls.Count == 2 && calls[0].Host == "podcasts.apple.com" &&
            calls[1].AbsoluteUri == "https://media.example/episode.mp3?signature=a%2Bb" && File.ReadAllText(result.OutputPath!) == "episode bytes",
            "The explicit episode expanded the show, downloaded related media or changed its signed URL.");
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(result.OutputPath!, ".info.json")));
        Assert(metadata.RootElement.GetProperty("id").GetString() == "900", "The episode sidecar lost the Apple identifier.");
    }
    private static async Task Archive()
    {
        using var files = new Files(); var calls = new List<Uri>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var engine = Engine(calls); var request = Request(files.Directory) with { DownloadArchiveFile = archive };
        await engine.DownloadAsync(request, null, null, default);
        Assert(File.ReadAllText(archive) == "applepodcasts 900\n", "The Apple archive identity differs from its numeric episode key.");
        calls.Clear();
        var skipped = await engine.DownloadAsync(request with { Url = new("https://itunes.apple.com/us/podcast/id123?i=900") }, null, null, default);
        Assert(skipped.Skipped && calls.Count == 0, "An archived Apple alias made a network request.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "https://embed.podcasts.apple.com/ca/podcast/fixture/id123?i=900", "http://itunes.apple.com/us/podcast/id123?i=900", "https://podcasts.apple.com/podcast/id123?i=900" })
        {
            var calls = new List<Uri>(); var info = await Engine(calls).InspectAsync(Request(Path.GetTempPath()) with { Url = new(url) }, default);
            Assert(info.Id == "900" && calls.Single().Host == "podcasts.apple.com" && calls[0].Scheme == "https", "An Apple alias did not resolve the exact episode over HTTPS.");
        }
        var embedded = Engine([], request => request.RequestUri!.Host == "article.example" ? Response("<iframe src=\"https://embed.podcasts.apple.com/ca/podcast/id123?i=900\"></iframe>") : null);
        Assert((await embedded.InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://article.example/post") }, default)).Id == "900", "Generic embed discovery missed Apple Podcasts.");
    }
    private static async Task Restrictions()
    {
        foreach (var change in new Action<JsonNode>[] { state => Model(state)["entitlementState"] = "notEntitled",
            state => Model(state)["mediaEnclosures"]![0]!["isPreview"] = true, state => Model(state)["mediaEnclosures"]![0]!["priceType"] = "PRVW",
            state => Model(state)["mediaEnclosures"]![0]!["isProtected"] = true })
        {
            var state = State(); change(state); var calls = new List<Uri>();
            var info = await Engine(calls, state: state).InspectAsync(Request(Path.GetTempPath()), default);
            Assert(info.Formats.All(format => format.Availability != FormatAvailability.Ready) && info.Warnings.Count > 0 && calls.Count == 1, "A restricted Apple enclosure became downloadable or triggered media access.");
        }
    }
    private static async Task Identity()
    {
        foreach (var change in new Action<JsonNode>[] { state => state["data"]![0]!["intent"]!["adamId"] = "999",
            state => state["data"]![0]!["intent"]!["showAdamId"] = "124", state => state["data"]![0]!["data"]!["pageAdamId"] = "999",
            state => state["data"]![0]!["intent"]!["storefront"] = "gb",
            state => state["data"]![0]!["data"]!["isIncomplete"] = true })
        {
            var state = State(); change(state);
            await Expect<InvalidDataException>(() => Engine([], state: state).InspectAsync(Request(Path.GetTempPath()), default));
        }
        var wrongParent = State(); Model(wrongParent)["showAdamId"] = "124";
        await Expect<NotSupportedException>(() => Engine([], state: wrongParent).InspectAsync(Request(Path.GetTempPath()), default));
        var redirect = Engine([], _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Html(State())), RequestMessage = new(HttpMethod.Get, "https://podcasts.apple.com/ca/podcast/id124?i=900") });
        await Expect<InvalidDataException>(() => redirect.InspectAsync(Request(Path.GetTempPath()), default));
    }
    private static async Task InvalidCatalogue()
    {
        foreach (var json in new[] { Catalogue.Replace("\"collectionId\":123", "\"collectionId\":124"), Catalogue.Replace("\"trackId\":123", "\"trackId\":124"),
            Catalogue.Replace("\"resultCount\":1", "\"resultCount\":2"), Catalogue.Replace("https://feeds.example/show.xml", "file:///private.xml") })
            await Expect<InvalidDataException>(() => Engine([], request => request.RequestUri!.Host == "itunes.apple.com" ? Response(json) : null).InspectAsync(Request(Path.GetTempPath()) with { Url = Show }, default));
        await Expect<InvalidOperationException>(() => Engine([], request => request.RequestUri!.Host == "itunes.apple.com" ? Response("{\"resultCount\":0,\"results\":[]}") : null)
            .InspectAsync(Request(Path.GetTempPath()) with { Url = Show }, default));
    }
    private static async Task InvalidPage()
    {
        foreach (var url in new[] { "https://podcasts.apple.com:8443/ca/podcast/id123?i=900", "https://user@podcasts.apple.com/ca/podcast/id123?i=900", "https://podcasts.apple.com/ca/podcast/id123?i=900&i=901", "https://podcasts.apple.com/ca/podcast/id123?i=0" })
            await Expect<ArgumentException>(() => Engine([]).InspectAsync(Request(Path.GetTempPath()) with { Url = new(url) }, default));
        Assert(!new ApplePodcastsProvider().CanHandle(new("https://podcasts.apple.com.evil.example/ca/podcast/id123?i=900")), "A spoofed Apple host was accepted.");
        var multiple = State(); multiple["data"]!.AsArray().Add(multiple["data"]![0]!.DeepClone());
        await Expect<InvalidDataException>(() => Engine([], state: multiple).InspectAsync(Request(Path.GetTempPath()), default));
        var conflict = State(); var second = conflict["data"]![0]!["data"]!["headerButtonItems"]![0]!.DeepClone(); second["model"]!["mediaEnclosures"]![0]!["isPreview"] = true;
        conflict["data"]![0]!["data"]!["headerButtonItems"]!.AsArray().Add(second);
        await Expect<InvalidDataException>(() => Engine([], state: conflict).InspectAsync(Request(Path.GetTempPath()), default));
        var unsafeMedia = State(); Model(unsafeMedia)["mediaEnclosures"]![0]!["streamUrl"] = "file:///private.mp3";
        await Expect<InvalidDataException>(() => Engine([], state: unsafeMedia).InspectAsync(Request(Path.GetTempPath()), default));
        await Expect<NotSupportedException>(() => Engine([], _ => Response(Html(State()) + Html(State()))).InspectAsync(Request(Path.GetTempPath()), default));
    }
    private static async Task Selection()
    {
        using var files = new Files(); var calls = new List<Uri>();
        var result = await Engine(calls).DownloadAsync(Request(files.Directory) with { Url = Show, PlaylistItems = "3" }, null, null, default);
        Assert(result.Succeeded && result.OutputPaths.Count == 1 && calls.Count == 3 && calls[2].AbsolutePath == "/third.mp3" && calls.All(uri => uri.AbsolutePath != "/first.mp3"), "Apple show selection fetched unselected episodes.");
    }
    private static async Task LimitsAndCancellation()
    {
        await Expect<InvalidDataException>(() => Engine([]).InspectAsync(Request(Path.GetTempPath()) with { Url = Show, MaximumPlaylistEntries = 2 }, default));
        using var files = new Files(); using var cancellation = new CancellationTokenSource();
        var engine = Engine([], _ => { cancellation.Cancel(); return null; });
        await Expect<OperationCanceledException>(() => engine.DownloadAsync(Request(files.Directory), null, null, cancellation.Token));
        Assert(!Directory.EnumerateFiles(files.Directory).Any(), "Canceled Apple extraction published a file.");
    }
    private static async Task OpaqueMedia()
    {
        using var files = new Files(); var state = State(); Model(state)["mediaEnclosures"]![0]!["streamUrl"] = "https://media.example/download?token=a%2Bb";
        var accesses = 0;
        var engine = Engine([], request =>
        {
            if (request.RequestUri!.Host != "media.example") return null;
            accesses++;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = accesses == 1 ? new StreamContent(new HeaderOnlyBody()) : new StringContent("audio bytes") };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/x-m4a");
            return response;
        }, state);
        var result = await engine.DownloadAsync(Request(files.Directory), null, null, default);
        Assert(result.Succeeded && result.OutputPath!.EndsWith(".m4a") && accesses == 2 && File.ReadAllText(result.OutputPath) == "audio bytes", "Media headers were ignored or discovery consumed the body.");
    }
    private static NativeEngine Engine(List<Uri> calls, Func<HttpRequestMessage, HttpResponseMessage?>? custom = null, JsonNode? state = null) => new(httpFactory: _ => new MediaHttp(new Handler(request =>
    {
        calls.Add(request.RequestUri!); var response = custom?.Invoke(request); if (response is not null) return response;
        return Response(request.RequestUri!.Host switch { "itunes.apple.com" => Catalogue, "podcasts.apple.com" => Html(state ?? State()), "feeds.example" => FeedChecks.Rss, _ => "episode bytes" });
    })));
    private static NativeDownloadRequest Request(string folder) => new() { Url = Episode, OutputDirectory = folder, FileName = "episode", Retries = 0 };
    private static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private sealed class HeaderOnlyBody : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Discovery must not read media bytes.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Discovery must not read media bytes.");
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw new InvalidOperationException("Discovery must not read media bytes.");
    }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose()
        {
            var target = Path.GetFullPath(Directory);
            if (!target.StartsWith(Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Test directory escaped its root.");
            System.IO.Directory.Delete(target, true);
        }
    }
}
