using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Pbs;

internal static class PbsChecks
{
    internal const string Id = "3108688573";
    internal const string Cid = "743dae10-679d-4a88-810c-afa8827b73dd";
    internal const string OtherCid = "079b385c-f26e-4e71-8ac1-d8790290a2af";
    internal const string Signed = "?Key=A%2Bb&case=C%2Fd";
    internal const string Media = "https://ga.pbs-video.pbs.org/videos/fixture/" + Cid + "/";
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new("https://www.pbs.org/video/fixture-preview/"), OutputDirectory = directory, OutputTemplate = "pbs.%(ext)s" };
    internal static JsonNode Asset(string slug = "fixture-preview", string id = Id, string cid = Cid) => new JsonObject { ["cid"] = cid, ["slug"] = slug, ["item_type"] = "video",
        ["legacy_tp_media_id"] = long.Parse(id), ["url"] = "https://content.services.pbs.org/v3/pbsorg/screens/video-assets/" + slug + "/", ["title"] = "Fixture preview" };
    internal static string Page(JsonNode? selected = null, JsonNode? duplicate = null)
    {
        var frame = new JsonObject { ["video"] = selected ?? Asset(), ["related"] = new JsonArray(Asset("related-preview", "42", OtherCid)) };
        if (duplicate is not null) frame["duplicate"] = duplicate;
        var wire = "0:" + frame.ToJsonString() + "\n";
        return "<html><head><title>PBS fixture</title></head><body><script>self.__next_f.push([1," + JsonSerializer.Serialize(wire) + "])</script></body></html>";
    }
    internal static JsonNode Video(string mode = "both", double duration = 2) => new JsonObject { ["id"] = Id, ["cid"] = Cid, ["slug"] = "fixture-preview", ["title"] = "Native PBS preview", ["duration"] = duration,
        ["availability"] = "available", ["is_playable"] = true, ["is_mvod"] = false, ["has_drm"] = false, ["video_type"] = "preview", ["short_description"] = "Native PBS description", ["image_url"] = "https://image.pbs.org/video-assets/poster.jpg",
        ["program"] = new JsonObject { ["title"] = "Fixture PBS program" }, ["encodings"] = mode == "mp4" ? new JsonArray("https://urs.pbs.org/redirect/22222222222222222222222222222222/") :
            mode == "hls" ? new JsonArray("https://urs.pbs.org/redirect/11111111111111111111111111111111/") : new JsonArray("https://urs.pbs.org/redirect/11111111111111111111111111111111/", "https://urs.pbs.org/redirect/22222222222222222222222222222222/"),
        ["cc_multiple_languages"] = new JsonArray(Track("WebVTT", "en", "captions.vtt"), Track("SRT", "en", "captions.srt"), Track("DFXP", "en", "captions.dfxp")), ["cc"] = new JsonObject(),
        ["related_videos"] = new JsonArray(new JsonObject { ["id"] = 42, ["encodings"] = new JsonArray("https://urs.pbs.org/redirect/33333333333333333333333333333333/") }) };
    internal static JsonNode Track(string profile, string language, string file) => new JsonObject { ["profile"] = profile, ["language"] = language,
        ["url"] = "https://ga.pbs-video.pbs.org/captions/fixture/" + Cid + "/" + file + Signed };
    internal static string Player(JsonNode? video = null) => "<html><script>window.staticUrl = \"/static-files/\";\nwindow.contextBridge = {\"country_id\":\"CA\"};\nwindow.videoBridge = " + (video ?? Video()).ToJsonString() + ";</script></html>";
    internal static IEnumerable<(string Name, Func<Task> Test)> All => [
        ("PBS native requested page and player identities retain media captions and metadata", Facts),
        ("PBS legacy numeric portal partner and HTTP aliases normalize to the HTTPS player", Aliases),
        ("PBS address boundaries exclude unrelated hosts profiles and invalid video IDs", Boundaries),
        ("PBS streamed page data isolates requested records and refuses conflicting identities", PageIdentity),
        ("PBS page and player content IDs cannot be substituted by related videos", PlayerIdentity),
        ("PBS literal data readers reject executable malformed and repeated declarations", Literals),
        ("PBS Passport protected unavailable and missing state records stop before resolver requests", Restrictions),
        ("PBS public encoding resolution preserves literal URLs and actual rendition facts", Resolution),
        ("PBS failed redirected unrelated or mismatched encoding metadata cannot become playback", ResolverFailures),
        ("PBS current and legacy caption profiles retain selectable formats and languages", Captions),
        ("PBS captions refuse ambiguous same-language or mismatched content assets", CaptionFailures),
        ("PBS public discovery isolates caller cookies and authorization across all origins", Anonymous),
        ("PBS known numeric archives skip page metadata and all playback lookups", Archives),
        ("PBS cancellation and denied public metadata cannot request later playback", Cancellation)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var page = await Extract(calls);
        Assert(page.Id == Id && page.Title == "Native PBS preview" && page.Uploader == "Fixture PBS program" && page.DurationSeconds == 2 && page.Media.Count == 2 && page.Subtitles.Count == 3, "PBS requested video facts changed.");
        Assert(calls.Count == 4 && calls.All(uri => !uri.AbsolutePath.Contains("333333")) && page.SourcePageUrl == Request(Path.GetTempPath()).Url, "PBS requested a related video or lost its source page.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "https://player.pbs.org/portalplayer/", "https://player.pbs.org/partnerplayer/", "https://player.pbs.org/viralplayer/", "https://player.pbs.org/widget/partnerplayer/", "https://player.pbs.org/widget/", "https://video.pbs.org/video/", "http://video.pbs.org/video/", "https://pbs.org/video/" })
        { var calls = new List<Uri>(); var page = await Extract(calls, request: Request(Path.GetTempPath()) with { Url = new(url + Id + "/?autoplay=false") }); Assert(page.Id == Id && calls[0].AbsoluteUri == "https://player.pbs.org/portalplayer/" + Id + "/", "A numeric alias changed its authoritative player."); }
        Assert((await Extract([], request: Request(Path.GetTempPath()) with { Url = new("http://www.pbs.org/video/fixture-preview/") })).Id == Id, "HTTP slug alias did not normalize safely.");
    }
    private static Task Boundaries()
    {
        var provider = new PbsProvider(); foreach (var url in new[] { "https://pbs.org/show/nature/", "https://pbskids.org/video/fixture/", "https://evil.pbs.org/video/fixture-preview/", "https://www.pbs.org:444/video/fixture-preview/",
            "https://secret@www.pbs.org/video/fixture-preview/", "https://player.pbs.org/portalplayer/0/", "https://player.pbs.org/portalplayer/01/", "https://player.pbs.org/portalplayer/9223372036854775808/", "https://www.pbs.org/video/fixture-preview/extra" })
            Assert(!provider.CanHandle(new(url)), "An unrelated or invalid PBS address was claimed.");
        Assert(YoutubeDl.Engine.Providers.MediaProviderRegistry.CreateDefault().Resolve(Request(Path.GetTempPath()).Url).Info.Id == "pbs", "Default native routing missed PBS."); return Task.CompletedTask;
    }
    private static async Task PageIdentity()
    {
        await Expect<NotSupportedException>(() => Extract([], page: Page(Asset("other"))));
        await Expect<NotSupportedException>(() => Extract([], page: Page(duplicate: Asset(id: "43", cid: OtherCid))));
        var wrong = Asset(); wrong["url"] = "https://content.services.pbs.org/v3/pbsorg/screens/video-assets/other/"; await Expect<InvalidDataException>(() => Extract([], page: Page(wrong)));
        Assert((await Extract([], page: Page(duplicate: Asset()))).Id == Id, "Identical repeated page references were treated as another asset.");
    }
    private static async Task PlayerIdentity()
    {
        foreach (var change in new Action<JsonNode>[] { v => v["id"] = "42", v => v["cid"] = OtherCid, v => v["slug"] = "related-preview" })
        { var video = Video(); change(video); await Expect<InvalidDataException>(() => Extract([], video: video)); }
    }
    private static async Task Literals()
    {
        await Expect<InvalidDataException>(() => Extract([], page: Page().Replace("])</script>", "]);evil()</script>")));
        await Expect<InvalidDataException>(() => Extract([], player: Player().Replace(";</script>", ";evil();</script>")));
        await Expect<NotSupportedException>(() => Extract([], player: "<script>window.contextBridge={};</script>"));
        await Expect<InvalidDataException>(() => Extract([], player: Player().Replace("\"has_drm\":false", "\"has_drm\":true,\"has_drm\":false")));
        await Expect<NotSupportedException>(() => Extract([], player: Player() + "<script>window.videoBridge=" + Video().ToJsonString() + ";</script>"));
    }
    private static async Task Restrictions()
    {
        foreach (var change in new Action<JsonNode>[] { v => v["is_mvod"] = true, v => v["has_drm"] = true, v => v["is_playable"] = false, v => v["availability"] = "unavailable", v => ((JsonObject)v).Remove("has_drm"), v => v["video_type"] = "live" })
        { var video = Video(); change(video); var calls = new List<Uri>(); await Expect<NotSupportedException>(() => Extract(calls, video: video)); Assert(calls.Count == 2, "Restricted PBS metadata requested an encoding."); }
    }
    private static async Task Resolution()
    {
        var page = await Extract([]); var mp4 = page.Media.Single(media => media.Kind == MediaKind.Progressive);
        Assert(mp4.Id == "mp4-96" && mp4.Height == 96 && mp4.Width is null && mp4.Url.Query == Signed && mp4.VerificationDurationSeconds == 2, "PBS encoded output facts or literal query changed.");
        var legacy = await Extract([], resolvedUrl: "https://ga.video.cdn.pbs.org/videos/fixture/" + Cid + "/legacy.mp4"); Assert(legacy.Media[0].Height is null, "Legacy source acquired invented quality metrics.");
    }
    private static async Task ResolverFailures()
    {
        await Expect<NotSupportedException>(() => Extract([], resolverStatus: "error"));
        await Expect<InvalidDataException>(() => Extract([], redirectResolver: true));
        await Expect<NotSupportedException>(() => Extract([], resolvedUrl: "https://unrelated.example/movie.mp4"));
        await Expect<InvalidDataException>(() => Extract([], resolvedUrl: Media.Replace(Cid, OtherCid) + "movie.mp4"));
        var video = Video(); video["encodings"] = new JsonArray("https://urs.pbs.org/redirect/not-an-id/"); await Expect<NotSupportedException>(() => Extract([], video: video));
    }
    private static async Task Captions()
    {
        var page = await Extract([]); var selected = YoutubeDl.Engine.Media.SubtitleSelection.Select(page.Subtitles, Request(Path.GetTempPath()) with { WriteSubs = true, SubFormat = "srt" });
        Assert(selected.Single().Extension == "srt", "PBS's offered SRT resource was not selectable.");
        var legacy = Video(); legacy["cc_multiple_languages"] = new JsonArray(); legacy["cc"] = new JsonObject { ["WebVTT"] = Media + "legacy.vtt" }; Assert((await Extract([], video: legacy)).Subtitles.Single().Language == "en", "Legacy caption mapping failed.");
    }
    private static async Task CaptionFailures()
    {
        var video = Video(); video["cc_multiple_languages"]![0]!["language"] = "../en"; await Expect<InvalidDataException>(() => Extract([], video: video));
        video = Video(); ((JsonArray)video["cc_multiple_languages"]!).Add(Track("WebVTT", "en", "other.vtt")); await Expect<InvalidDataException>(() => Extract([], video: video));
        video = Video(); video["cc_multiple_languages"]![0]!["url"] = Media.Replace(Cid, OtherCid) + "captions.vtt"; await Expect<InvalidDataException>(() => Extract([], video: video));
    }
    private static async Task Anonymous()
    {
        var options = new MediaHttpOptions(); options.Headers["Authorization"] = "Bearer fixture"; options.Headers["Cookie"] = "fixture=secret"; using var http = Http([], options: options,
            observe: r => Assert(!r.Headers.Contains("Authorization") && !r.Headers.Contains("Cookie"), "Caller credentials reached PBS public discovery.")); _ = await new PbsProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
    }
    private static async Task Archives()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pbs-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
        try { var archive = Path.Combine(dir, "archive.txt"); await File.WriteAllTextAsync(archive, "pbs " + Id + "\n"); var calls = new List<Uri>(); var engine = new NativeEngine([new PbsProvider()], _ => Http(calls));
            var result = await engine.DownloadAsync(Request(dir) with { Url = new("https://video.pbs.org/video/" + Id), DownloadArchiveFile = archive }, null, null, default); Assert(result.Succeeded && result.Skipped && calls.Count == 0, "An archived PBS numeric alias requested discovery."); }
        finally { Directory.Delete(dir, true); }
    }
    private static async Task Cancellation()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel(); using var http = Http([]); await Expect<OperationCanceledException>(() => new PbsProvider().ExtractAsync(Request(Path.GetTempPath()), http, stop.Token));
        using var denied = Http([], status: HttpStatusCode.Forbidden); await Expect<HttpRequestException>(() => new PbsProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
    }
    private static async Task<PageMetadata> Extract(List<Uri> calls, string? page = null, string? player = null, JsonNode? video = null, NativeDownloadRequest? request = null,
        string? resolvedUrl = null, string resolverStatus = "ok", bool redirectResolver = false)
    { using var http = Http(calls, page, player, video, resolvedUrl: resolvedUrl, resolverStatus: resolverStatus, redirectResolver: redirectResolver); return await new PbsProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static MediaHttp Http(List<Uri> calls, string? page = null, string? player = null, JsonNode? video = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null,
        MediaHttpOptions? options = null, Action<HttpRequestMessage>? observe = null, string? resolvedUrl = null, string resolverStatus = "ok", bool redirectResolver = false, HttpStatusCode status = HttpStatusCode.OK) => new(new Handler(r =>
    {
        calls.Add(r.RequestUri!); observe?.Invoke(r); var uri = r.RequestUri!;
        if (uri.Host == "www.pbs.org") return new(status) { Content = new StringContent(page ?? Page()), RequestMessage = r };
        if (uri.Host == "player.pbs.org") return new(status) { Content = new StringContent(player ?? Player(video)), RequestMessage = r };
        if (uri.Host == "urs.pbs.org")
        { if (uri.Query != "?format=json") throw new InvalidOperationException("PBS resolver mode changed."); return new(status) { Content = new StringContent(new JsonObject { ["status"] = resolverStatus, ["http_code"] = resolverStatus == "ok" ? 302 : 403,
            ["url"] = resolvedUrl ?? Media + (uri.AbsolutePath.Contains("111111") ? "master.m3u8" : "fixture-mp4-96p-300k.mp4") + Signed }.ToJsonString()), RequestMessage = redirectResolver ? new(HttpMethod.Get, "https://other.example/resolver") : r }; }
        return media?.Invoke(r) ?? throw new InvalidOperationException("Unexpected PBS media request.");
    }), options ?? new());
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
}
