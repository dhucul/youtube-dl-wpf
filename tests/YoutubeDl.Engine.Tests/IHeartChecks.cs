using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.IHeart;

internal static class IHeartChecks
{
    internal const string PodcastId = "123456", EpisodeId = "234567", OtherId = "234568", Api = "https://ca.api.iheart.com/";
    internal static string Source(string id = EpisodeId, string extension = "mp3") => "https://cdn.example/iheart/" + id + "." + extension + "?sig=A%2Bb&case=C%2Fd";
    internal static NativeDownloadRequest Request(string output, bool collection = false) => new()
    {
        Url = collection ? IHeartAddress.Podcast(PodcastId) : IHeartAddress.Episode(PodcastId, EpisodeId), OutputDirectory = output,
        OutputTemplate = "iheart.%(ext)s", DownloadPlaylist = collection
    };
    internal static JsonNode Podcast(string id = PodcastId) => new JsonObject { ["id"] = long.Parse(id), ["title"] = "Fixture podcast", ["slug"] = "867-fixture", ["description"] = "Independent native audio" };
    internal static JsonNode Episode(string id = EpisodeId, double duration = 2, string extension = "mp3") => new JsonObject
    {
        ["id"] = long.Parse(id), ["podcastId"] = long.Parse(PodcastId), ["podcastSlug"] = "867-fixture", ["title"] = "Fixture episode " + id,
        ["description"] = "Native episode", ["duration"] = duration, ["startDate"] = 1577836800000L, ["endDate"] = 4931366997531L,
        ["mediaUrl"] = Source(id, extension), ["mimeTypes"] = new JsonArray(extension == "mp3" ? "audio/mpeg" : extension == "m4a" ? "audio/mp4" : "audio/aac"),
        ["transcriptionAvailable"] = false
    };
    internal static JsonNode List(string? next = null, params JsonNode[] episodes) => new JsonObject
    {
        ["data"] = new JsonArray(episodes.Length == 0 ? [Episode(), Episode(OtherId)] : episodes),
        ["links"] = next is null ? new JsonObject() : new JsonObject { ["next"] = next }, ["meta"] = new JsonObject()
    };
    internal static string Html(string api = Api)
    {
        object[] table = [new Dictionary<string,int>{{"_1",2}}, "loaderData", new Dictionary<string,int>{{"_3",4}}, "root",
            new Dictionary<string,int>{{"_5",6}}, "config", new Dictionary<string,int>{{"_7",8}}, "api",
            new Dictionary<string,int>{{"_9",10},{"_13",12}}, "amp", new Dictionary<string,int>{{"_11",12}}, "clientEndpoint", api.TrimEnd('/'), "apiUrl"];
        return "<html><script>window.__reactRouterContext.streamController.enqueue(" + JsonSerializer.Serialize(JsonSerializer.Serialize(table)) + ");</script></html>";
    }
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("iHeart public episodes bind podcast IDs audio roles source URLs and duration verification", Facts),
        ("iHeart numeric slug and embedded aliases preserve stable episode archives", Aliases),
        ("iHeart unsupported private manually paged and station routes stay explicit", Boundaries),
        ("iHeart literal router metadata binds a public regional API without evaluating scripts", Hydration),
        ("iHeart conflicting requested episode podcast and redirect identities fail", Identities),
        ("iHeart private previews expired upcoming live protected and video-only recordings fail", Restrictions),
        ("iHeart declared types malformed media resources and requested transcripts fail explicitly", Sources),
        ("iHeart complete public continuation retains source ordering and terminal state", Pagination),
        ("iHeart opaque continuations remain encoded on the advertised parent API", OpaqueCursor),
        ("iHeart duplicate changed unordered empty-continuing and excessive inventories fail", Incomplete),
        ("iHeart unavailable original episode positions remain selectable without renumbering", Positions),
        ("iHeart selected inspected episodes refresh only their own playback data", Snapshots),
        ("iHeart archived direct and selected episodes skip website playback requests", Archives),
        ("iHeart cancellation denial and anonymous public requests retain transport boundaries", Transport)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var page = await Extract(calls); var media = page.Media.Single();
        Assert(page.Id == EpisodeId && page.Extractor == "iheartradio" && page.Uploader == "Fixture podcast" && page.DurationSeconds == 2 && page.AnonymousPlayback &&
            media.Url.AbsoluteUri == Source() && media.Id == "audio" && media.IsAudioOnly && media.VerificationDemuxer == "mp3" && media.VerificationMinimumDurationSeconds == 2 && calls.Count == 3,
            "Native iHeart episode facts or complete source binding changed.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "http://iheart.com/podcast/867-fixture-123456/episode/sample-234567/", "https://www.iheart.com/podcast/123456/episode/234567/?embed=true&theme=dark", "https://www.iheart.com/podcast/123456/episode/234567/?utm_source=fixture" })
        {
            var address = IHeartAddress.Parse(new(url)); var page = await Extract([], request: Request(Path.GetTempPath()) with { Url = new(url) });
            Assert(address.PodcastId == PodcastId && address.EpisodeId == EpisodeId && page.ArchiveIdentity!.Id == EpisodeId, "An iHeart alias changed the requested identity.");
        }
    }
    private static Task Boundaries()
    {
        foreach (var url in new[] { "https://www.iheart.com/live/station-123456/", "https://other.example/podcast/123456/episode/234567/", "https://user:secret@www.iheart.com/podcast/123456/", "https://www.iheart.com:8443/podcast/123456/",
            "https://www.iheart.com/podcast/123456/?token=secret", "https://www.iheart.com/podcast/123456/?pageKey=next", "https://www.iheart.com/podcast/123456/?embed=true&embed=false", "https://www.iheart.com/podcast/123456/#start=30", "https://www.iheart.com/podcast/0/" })
            Assert(!IHeartAddress.Matches(new(url)), "An unsupported iHeart request was accepted.");
        Assert(!new YoutubeDl.Engine.Providers.GenericPageProvider().CanHandle(new("https://www.iheart.com/live/station-123456/")), "An unsupported iHeart route reached generic media discovery.");
        Assert(EmbeddedMediaReader.Read("<iframe src='https://www.iheart.com/podcast/123456/episode/234567/?embed=true'></iframe>", new("https://publisher.example/")).Single().Query == "?embed=true", "An iHeart widget did not reach native embedded dispatch.");
        return Task.CompletedTask;
    }
    private static async Task Hydration()
    {
        Assert(IHeartHydration.Api(Html(), default).AbsoluteUri == Api, "The advertised region was replaced or discarded.");
        foreach (var bad in new[] { Html("https://attacker.example/"), Html("http://ca.api.iheart.com/"), Html("https://ca.api.iheart.com/?token=private"), Html() + Html(), Html().Replace(");</script>", " + getPrivateToken());</script>"), Html().Replace("enqueue(\"", "enqueue(eval(\"") })
            await Expect<InvalidDataException>(() => Task.FromResult(IHeartHydration.Api(bad, default)));
        await Expect<NotSupportedException>(() => Task.FromResult(IHeartHydration.Api("<script>computedSiteConfiguration()</script>", default)));
        await Expect<InvalidDataException>(() => Task.FromResult(IHeartHydration.Parse("{\"id\":1,\"id\":2}", default)));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Expect<OperationCanceledException>(() => Task.FromResult(IHeartHydration.Api(Html(), canceled.Token)));
    }
    private static async Task Identities()
    {
        foreach (var change in new Action<JsonNode>[] { e => e["id"] = long.Parse(OtherId), e => e["podcastId"] = 999999, e => e["id"] = "wrong" })
        { var episode = Episode(); change(episode); await Expect<InvalidDataException>(() => Extract([], episode: episode)); }
        await Expect<InvalidDataException>(() => Extract([], podcast: Podcast("999999")));
        using var changed = Http([], websiteRedirect: "https://www.iheart.com/podcast/123456/episode/234568/");
        await Expect<InvalidDataException>(() => new IHeartProvider().ExtractAsync(Request(Path.GetTempPath()), changed, default));
        using var apiRedirect = Http([], apiRedirect: true);
        await Expect<InvalidDataException>(() => new IHeartProvider().ExtractAsync(Request(Path.GetTempPath()), apiRedirect, default));
    }
    private static async Task Restrictions()
    {
        foreach (var field in new[] { "isPrivate", "isPremium", "requiresLogin", "subscriptionRequired", "isPreview", "isLive", "drmProtected" })
        { var episode = Episode(); episode[field] = true; await Expect<NotSupportedException>(() => Extract([], episode: episode)); }
        var upcoming = Episode(); upcoming["startDate"] = 4102444800000L; await Expect<NotSupportedException>(() => Extract([], episode: upcoming));
        var expired = Episode(); expired["endDate"] = 1609459200000L; await Expect<NotSupportedException>(() => Extract([], episode: expired));
        var video = Episode(); video["mimeTypes"] = new JsonArray("video/mp4"); await Expect<NotSupportedException>(() => Extract([], episode: video));
        var invalid = Episode(); invalid["endDate"] = 1; await Expect<InvalidDataException>(() => Extract([], episode: invalid));
    }
    private static async Task Sources()
    {
        foreach (var source in new[] { "file:///secret.mp3", "https://user:secret@cdn.example/audio.mp3", "https://localhost/audio.mp3", "https://127.0.0.1/audio.mp3", "https://cdn.example/audio.m3u8", "https://cdn.example/audio.mp3#clip=20" })
        { var episode = Episode(); episode["mediaUrl"] = source; await Expect<InvalidDataException>(() => Extract([], episode: episode)); }
        var malformed = Episode(); malformed["mimeTypes"] = "audio/mpeg"; await Expect<InvalidDataException>(() => Extract([], episode: malformed));
        var transcript = Episode(); transcript["transcriptionAvailable"] = true;
        await Expect<NotSupportedException>(() => Extract([], episode: transcript, request: Request(Path.GetTempPath()) with { WriteSubs = true }));
        var multiple = Episode(); multiple["mimeTypes"] = new JsonArray("audio/mpeg", "video/mp4"); await Expect<NotSupportedException>(() => Extract([], episode: multiple));
    }
    private static async Task Pagination()
    {
        var calls = new List<Uri>(); var page = await Extract(calls, request: Request(Path.GetTempPath(), true), first: List("next-2", Episode()), second: List(null, Episode(OtherId)));
        Assert(page.IsPlaylist && page.Id == PodcastId && page.Entries.Count == 2 && page.Entries[1].Position == 2 && page.Entries[1].ExpectedArchiveIdentity!.Id == OtherId && calls.Count == 6,
            "iHeart terminal pagination changed scope, order or validation requests.");
    }
    private static async Task OpaqueCursor()
    {
        var calls = new List<Uri>(); var token = "opaque/value + &=#";
        await Extract(calls, request: Request(Path.GetTempPath(), true), first: List(token, Episode()), second: List(null, Episode(OtherId)));
        Assert(calls.All(url => url.Host is "www.iheart.com" or "ca.api.iheart.com") && calls.Single(url => url.Query.Contains("pageKey=")).Query.EndsWith("pageKey=" + Uri.EscapeDataString(token)),
            "An opaque iHeart continuation changed the origin or injected query parameters.");
    }
    private static async Task Incomplete()
    {
        await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: List("next", Episode()), second: List(null, Episode())));
        await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: List("next", Episode()), second: List("next", Episode(OtherId))));
        var empty = List("next"); empty["data"] = new JsonArray(); await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: empty));
        var missing = List(); ((JsonObject)missing).Remove("links"); await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: missing));
        var owner = Episode(); owner["podcastId"] = 999999; await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: List(null, owner)));
        var later = Episode(OtherId); later["startDate"] = 1609459200000L;
        await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: List("next", Episode()), second: List(null, later)));
        await Expect<NotSupportedException>(() => Extract([], request: Request(Path.GetTempPath(), true) with { MaximumPlaylistEntries = 1 }));
        await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), recheck: List(null, Episode(OtherId))));
    }
    private static async Task Positions()
    {
        var unavailable = Episode(); unavailable["isPremium"] = true;
        var page = await Extract([], request: Request(Path.GetTempPath(), true), first: List(null, unavailable, Episode(OtherId)));
        Assert(!page.Entries[0].Available && page.Entries[0].Position == 1 && page.Entries[1].Available && page.Entries[1].Position == 2 && page.Entries[1].Id == OtherId,
            "An unavailable iHeart episode renumbered later source entries.");
    }
    private static async Task Snapshots()
    {
        await Temporary(async directory =>
        {
            var calls = new List<Uri>(); var engine = new NativeEngine([new IHeartProvider()], _ => Http(calls));
            var request = Request(directory, true) with { PlaylistItems = "2", Simulate = true };
            var inspected = await engine.InspectAsync(request, default); calls.Clear();
            var result = await engine.DownloadAsync(request with { PlaylistSnapshot = inspected.PlaylistSnapshot }, null, null, default);
            Assert(result.Succeeded && calls.Any(uri => uri.AbsolutePath.EndsWith("/" + OtherId)) && !calls.Any(uri => uri.AbsolutePath.EndsWith("/" + EpisodeId)),
                "An inspected iHeart selection refreshed an unselected playback record.");
        });
    }
    private static async Task Archives()
    {
        await Temporary(async directory =>
        {
            var archive = Path.Combine(directory, "archive.txt"); await File.WriteAllTextAsync(archive, "iheartradio " + EpisodeId + "\n");
            var engine = new NativeEngine([new IHeartProvider()], _ => throw new InvalidOperationException("An archived iHeart job contacted the website."));
            var result = await engine.DownloadAsync(Request(directory) with { DownloadArchiveFile = archive }, null, null, default);
            Assert(result.Succeeded && result.Skipped && result.OutputPaths.Count == 0, "An archived iHeart episode did not skip.");
            await File.WriteAllTextAsync(archive, "iheartradio " + OtherId + "\n"); var calls = new List<Uri>();
            var selected = new NativeEngine([new IHeartProvider()], _ => Http(calls));
            var collectionResult = await selected.DownloadAsync(Request(directory, true) with { DownloadArchiveFile = archive, PlaylistItems = "2" }, null, null, default);
            Assert(collectionResult.Succeeded && !calls.Any(uri => uri.AbsolutePath.EndsWith("/" + OtherId)), "An archived selected iHeart child repeated playback discovery.");
        });
    }
    private static async Task Transport()
    {
        using var http = Http([], observe: request =>
        {
            if (request.Headers.Contains("Authorization") || request.Headers.Contains("Cookie")) throw new InvalidOperationException("Public iHeart requests received caller credentials.");
            Assert(request.Headers.Contains("X-Transport"), "iHeart discarded an ordinary caller header.");
        }, options: new() { UserAgent = "NativeFixture/1", Headers = { ["Authorization"] = "Bearer fixture", ["Cookie"] = "private=fixture", ["X-Transport"] = "scoped" } });
        await new IHeartProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Expect<OperationCanceledException>(() => new IHeartProvider().ExtractAsync(Request(Path.GetTempPath()), http, canceled.Token));
        using var denied = Http([], status: HttpStatusCode.Forbidden); await Expect<HttpRequestException>(() => new IHeartProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
    }
    private static async Task<PageMetadata> Extract(List<Uri> calls, JsonNode? episode = null, JsonNode? podcast = null, NativeDownloadRequest? request = null,
        JsonNode? first = null, JsonNode? second = null, JsonNode? recheck = null)
    {
        using var http = Http(calls, episode, podcast, first, second, recheck);
        return await new IHeartProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default);
    }
    internal static MediaHttp Http(List<Uri> calls, JsonNode? episode = null, JsonNode? podcast = null, JsonNode? first = null, JsonNode? second = null,
        JsonNode? recheck = null, Func<HttpRequestMessage,HttpResponseMessage>? media = null, Action<HttpRequestMessage>? observe = null,
        MediaHttpOptions? options = null, HttpStatusCode status = HttpStatusCode.OK, string? websiteRedirect = null, bool apiRedirect = false)
    {
        var firstReads = 0;
        return new(new Handler(request =>
        {
            var url = request.RequestUri!; calls.Add(url); observe?.Invoke(request);
            if (url.Host == "www.iheart.com") return new(status) { Content = new StringContent(Html(), System.Text.Encoding.UTF8, "text/html"), RequestMessage = websiteRedirect is null ? request : new(HttpMethod.Get, websiteRedirect) };
            if (url.Host != "ca.api.iheart.com") return media?.Invoke(request) ?? throw new InvalidOperationException("Unexpected iHeart media request.");
            JsonNode data;
            if (url.AbsolutePath.EndsWith("/episodes")) data = url.Query.Contains("pageKey=") ? second ?? List(null, Episode(OtherId)) :
                ++firstReads > 1 && recheck is not null ? recheck : first ?? List();
            else if (url.AbsolutePath.Contains("/episodes/")) data = new JsonObject { ["episode"] = episode?.DeepClone() ?? Episode(url.AbsolutePath.Split('/').Last()), ["pageKey"] = "episode-key" };
            else data = podcast ?? Podcast();
            return new(status) { Content = new StringContent(data.ToJsonString()), RequestMessage = apiRedirect ? new(HttpMethod.Get, "https://other.example/metadata") : request };
        }), options ?? new());
    }
    private static async Task Temporary(Func<string,Task> test)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-data", "iheart-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try { await test(directory); } finally { Directory.Delete(directory, true); }
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(response(request)); }
    }
}
