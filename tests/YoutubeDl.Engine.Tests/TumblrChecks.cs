using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.Tumblr;

internal static class TumblrChecks
{
    internal const string Id = "826039965713612800";
    internal const string Query = "?token=A%2Bb&case=C%2Fd";
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new("https://fixture.tumblr.com/post/" + Id), OutputDirectory = directory, FileName = "tumblr", Retries = 0 };
    internal static JsonNode Video(string asset = "fixture", double duration = 2, bool audio = false) => new JsonObject { ["type"] = audio ? "audio" : "video", ["provider"] = "tumblr", ["duration"] = duration,
        ["url"] = "https://ve.media.tumblr.com/tumblr_" + asset + (audio ? ".mp3" : ".mp4") + Query,
        ["media"] = new JsonObject { ["url"] = "https://ve.media.tumblr.com/tumblr_" + asset + (audio ? ".mp3" : ".mp4") + Query,
            ["type"] = audio ? "audio/mpeg" : "video/mp4", ["width"] = 160, ["height"] = 96 },
        ["poster"] = new JsonArray(new JsonObject { ["url"] = "https://66.media.tumblr.com/tumblr_" + asset + "_frame1.jpg" }) };
    internal static string Html(params JsonNode[] blocks) => "<meta property='og:title' content='Fixture Tumblr title'><meta property='og:description' content='Fixture description'>" +
        "<article data-post-id='" + Id + "'>" + string.Join("", blocks.Select(block => "<figure data-npf='" + WebUtility.HtmlEncode(block.ToJsonString()) + "'></figure>")) + "</article>" +
        "<article data-post-id='2'><figure data-npf='" + WebUtility.HtmlEncode(Video("suggested").ToJsonString()) + "'></figure></article>";
    internal static MediaHttp Http(string html, List<Uri>? calls = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null, Uri? finalPage = null) => new(new Handler(request =>
    {
        var url = request.RequestUri!; calls?.Add(url);
        if (request.Headers.Authorization is not null) throw new InvalidOperationException("Account authorization reached public Tumblr discovery.");
        if (url.Host == "fixture.tumblr.com") return new(HttpStatusCode.OK) { Content = new StringContent(html), RequestMessage = new(HttpMethod.Get, finalPage ?? Request(Path.GetTempPath()).Url) };
        if (url.Host == "article.example") return new(HttpStatusCode.OK) { Content = new StringContent("<iframe src='https://www.tumblr.com/video/fixture/" + Id + "/700/'></iframe>") };
        return media?.Invoke(request) ?? new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
    }), new() { AllowAutoRedirect = false });
    private static async Task<PageMetadata> Read(string? html = null, NativeDownloadRequest? request = null, Uri? finalPage = null)
    { using var http = Http(html ?? Html(Video()), finalPage: finalPage); return await new TumblrProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Tumblr scoped NPF media preserves post facts queries duration and excludes suggested posts", Facts),
        ("Tumblr post frontend player and embed aliases retain canonical identity", Aliases),
        ("Tumblr changed post blog or conflicting article identities fail", Identity),
        ("Tumblr templates comments malformed JSON and duplicate resource attributes fail", JsonOnly),
        ("Tumblr NPF origins and conflicting native media URLs remain bound", Origins),
        ("Tumblr resized movie resources do not claim original recording dimensions", Dimensions),
        ("Tumblr native audio blocks remain audio-only and retain offered format", Audio),
        ("Tumblr multi-media positions retain non-media blocks and asset archives", Multiple),
        ("Tumblr external wrapped players delegate to original provider identities", External),
        ("Tumblr archives skip before post media and sidecar transfers", Archive)
    ];
    private static async Task Facts()
    {
        var page = await Read(); Assert(page.Id == Id && page.Media.Count == 1 && page.Media[0].Url.Query == Query && page.DurationSeconds == 2 && page.Media[0].Height == 96 &&
            page.ArchiveIdentity?.Extractor == "tumblr" && page.Title == "Fixture Tumblr title" && page.AnonymousPlayback, "Scoped Tumblr media metadata or archive identity changed.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "https://fixture.tumblr.com/post/" + Id + "/slug", "https://www.tumblr.com/fixture/" + Id + "/slug", "https://www.tumblr.com/blog/view/fixture/" + Id,
            "https://www.tumblr.com/video/fixture/" + Id + "/700/" }) Assert((await Read(request: Request(Path.GetTempPath()) with { Url = new(url) })).Id == Id, "Tumblr alias changed identity.");
        var engine = new NativeEngine(httpFactory: _ => Http(Html(Video()))); var info = await engine.InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://article.example/post") }, default);
        Assert(info.Extractor == "tumblr" && info.Id == Id, "Tumblr player iframe did not delegate to its native provider.");
        await Reject<NotSupportedException>(() => Read(request: Request(Path.GetTempPath()) with { Url = new("https://fixture.tumblr.com/") }));
    }
    private static async Task Identity()
    {
        await Reject<InvalidDataException>(() => Read(finalPage: new("https://other.tumblr.com/post/" + Id)));
        await Reject<InvalidDataException>(() => Read(finalPage: new("https://fixture.tumblr.com/post/2")));
        await Reject<NotSupportedException>(() => Read(Html(Video()).Replace("data-post-id='" + Id + "'", "data-post-id='3'", StringComparison.Ordinal)));
        await Reject<NotSupportedException>(() => Read(Html(Video()) + Html(Video())));
    }
    private static async Task JsonOnly()
    {
        await Reject<NotSupportedException>(() => Read("<template>" + Html(Video()) + "</template>"));
        await Reject<NotSupportedException>(() => Read("<!--" + Html(Video()) + "-->"));
        var altered = Html(Video()).Replace("<figure data-npf=", "<figure src='https://one.example' src='https://two.example' data-npf=", StringComparison.Ordinal);
        await Reject<InvalidDataException>(() => Read(altered));
        await Reject<System.Text.Json.JsonException>(() => Read(Html(Video()).Replace("&quot;provider&quot;:", "bad():", StringComparison.Ordinal)));
    }
    private static async Task Origins()
    {
        var data = Video(); data["media"]!["url"] = "https://evil.example/video.mp4"; await Reject<InvalidDataException>(() => Read(Html(data)));
        data = Video(); data["url"] = "https://ve.media.tumblr.com/other.mp4"; await Reject<InvalidDataException>(() => Read(Html(data)));
        data = Video(); data["media"]!["media_key"] = "other:revision"; await Reject<InvalidDataException>(() => Read(Html(data)));
    }
    private static async Task Dimensions()
    {
        var data = Video(); var scaled = "https://64.media.tumblr.com/hash/revision/s720/asset.mov" + Query; data["media"]!["url"] = scaled; data["url"] = scaled;
        data["media"]!["width"] = 1920; data["media"]!["height"] = 1080; var page = await Read(Html(data));
        Assert(page.Media[0].Height == 720 && page.Media[0].Width is null && page.Media[0].Extension == "mov", "Resized movie used original dimensions or a wrong extension.");
        data["media"]!["media_key"] = "hash:revision"; data["url"] = "https://64.media.tumblr.com/hash/revision/canonical.mov" + Query;
        page = await Read(Html(data)); Assert(page.Media.Count == 2 && page.Media[1].Id == "canonical" && page.Media[1].Width is null && page.Media[1].Height is null,
            "Canonical preview links incorrectly inherited original recording dimensions.");
        data = Video(); data["media"]!["original_dimensions_missing"] = true; page = await Read(Html(data)); Assert(page.Media[0].Width is null && page.Media[0].Height is null, "Tumblr default dimensions became actual format facts.");
    }
    private static async Task Audio()
    { var page = await Read(Html(Video(audio: true))); Assert(page.Media[0].IsAudioOnly && page.Media[0].Extension == "mp3" && page.Media[0].HasVideo == false, "Native audio was mistaken for video."); }
    private static async Task Multiple()
    {
        var page = await Read(Html(new JsonObject { ["type"] = "text", ["text"] = "caption" }, Video(), Video("other")));
        Assert(page.IsPlaylist && page.Entries.Count == 3 && !page.Entries[0].Available && page.Entries[2].Position == 3 && page.Entries[1].ExpectedArchiveIdentity?.Extractor == "tumblr:media", "Block positions or media identities collapsed.");
    }
    private static async Task External()
    {
        var html = "<article data-post-id='" + Id + "'><iframe src='https://safe.txmblr.com/svc/embed/inline/https%3A%2F%2Fwww.youtube.com%2Fembed%2FjNQXAC9IVRw'></iframe></article>";
        var page = await Read(html); Assert(page.Media.Count == 0 && page.EmbeddedPages.Single().Host == "www.youtube.com" && page.ArchiveIdentity is null, "Wrapped third-party video was attributed to a Tumblr media file.");
        await Reject<NotSupportedException>(() => Read(Html(new JsonObject { ["type"] = "paywall" }, Video())));
    }
    private static async Task Archive()
    {
        var directory = Path.Combine(Path.GetTempPath(), "native-tumblr-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try { var archive = Path.Combine(directory, "archive.txt"); await File.WriteAllTextAsync(archive, "tumblr " + Id + "\n"); var calls = new List<Uri>();
            var result = await new NativeEngine([new TumblrProvider()], _ => Http(Html(Video()), calls)).DownloadAsync(Request(directory) with { DownloadArchiveFile = archive }, null, null, default);
            Assert(result.Succeeded && result.Skipped && calls.Count == 0, "Recorded Tumblr post fetched metadata or media."); }
        finally { Directory.Delete(directory, true); }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(run(request)); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Reject<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
