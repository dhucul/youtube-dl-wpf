using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Medal;

internal static class MedalChecks
{
    internal const string Id = "1HnFOn78iK1FqT";
    internal const string Owner = "71455820";
    internal static string Auth(string id = Id, string owner = Owner) => "exp=2000000000~data=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(owner + "," + id + ",game,-1")).TrimEnd('=').Replace('+', '-').Replace('/', '_') + "~hmac=" + new string('a', 64);
    internal static string Media(string file = "source.mp4", string id = Id, string owner = Owner) => "https://cdn.medal.tv/mediac/" + file + "?auth=" + Auth(id, owner);
    internal static string Poster(string id = Id) => "https://cdn.medal.tv/ugcc/content-thumbnail/poster.jpg?auth=" + Auth(id);
    internal static string Hls(string id = Id) => "https://medal.tv/api/hls/" + id + "/master.m3u8?bebit=" + Uri.EscapeDataString(Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('x', 80))));
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new("https://medal.tv/games/minecraft/clips/" + Id), OutputDirectory = directory, OutputTemplate = "medal.%(ext)s" };
    internal static JsonNode Clip(double duration = 2) => new JsonObject { ["contentId"] = Id, ["contentShareUrl"] = "https://medal.tv/games/minecraft/clips/" + Id, ["category"] = new JsonObject { ["slug"] = "minecraft" },
        ["contentTitle"] = "Native Medal clip", ["contentDescription"] = "Exact public clip", ["contentType"] = 15, ["hasAccess"] = 1, ["processed"] = 1, ["requireLogin"] = false, ["privacy"] = 0,
        ["state"] = new JsonObject { ["type"] = "published", ["isSuccess"] = true, ["isShareable"] = true }, ["archivedAt"] = null, ["videoLengthSeconds"] = duration, ["sourceWidth"] = 160, ["sourceHeight"] = 96,
        ["hasSound"] = 1, ["contentUrl"] = Media(), ["contentUrl720p"] = Media("720.mp4") + "&t=720p&missing", ["thumbnailUrl"] = Poster(), ["poster"] = new JsonObject { ["userId"] = Owner, ["userName"] = "Fixture owner" } };
    internal static string Html(JsonNode? clip = null, JsonNode? conflicting = null)
    {
        var root = new JsonObject { ["clip"] = clip?.DeepClone() ?? Clip(), ["related"] = new JsonObject { ["contentId"] = "RelatedId", ["contentUrl"] = Media("unrelated.mp4", "RelatedId") } };
        if (conflicting is not null) root["duplicate"] = conflicting.DeepClone();
        var prefix = "{\"description\":\"UTF8 日本語\"}"; var wire = "a:T" + Encoding.UTF8.GetByteCount(prefix).ToString("x") + "," + prefix + "b:" + root.ToJsonString() + "\n";
        return "<html><script>self.__next_f.push([1," + JsonSerializer.Serialize(wire) + "])</script></html>";
    }
    internal static IEnumerable<(string Name, Func<Task> Test)> All => [
        ("Medal requested clip data survives byte-counted Flight rows and excludes related records", Facts),
        ("Medal URL aliases normalize while binding the returned requested clip identity", Aliases),
        ("Medal address boundaries exclude feeds profiles unrelated hosts and malformed clips", Boundaries),
        ("Medal route category share and duplicate clip identity mismatches fail before media", Identities),
        ("Medal private unfinished missing-access and login records cannot expose ready formats", Access),
        ("Medal source dimensions duration and sound roles retain supplied facts", Dimensions),
        ("Medal unavailable rendition markers do not become guessed playback or resolution fallbacks", Missing),
        ("Medal media authorization payloads must match requested clip and owner", AuthBinding),
        ("Medal malformed signature descriptors origins and query duplicates fail explicitly", InvalidResources),
        ("Medal only ready supplied rendition aliases contribute downloadable source candidates", Renditions),
        ("Medal supplied HLS service binds exact clip paths and preserves its opaque token", HlsBinding),
        ("Medal malformed duplicated or unrelated HLS service descriptors cannot expose playback", HlsFailures),
        ("Medal public discovery isolates caller credentials", Anonymous),
        ("Medal known clip archives skip public discovery and all media requests", Archives),
        ("Medal cancellation HTTP denial and redirected page identity prevent later playback", Cancellation)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var page = await Extract(calls); Assert(page.Id == Id && page.Title == "Native Medal clip" && page.Media.Count == 1 && page.Uploader == "Fixture owner" && calls.Count == 1, "Requested clip or owner metadata changed.");
        Assert(page.ReportedFormats.Count == 2 && page.ReportedFormats.Count(format => format.Availability == FormatAvailability.Ready) == 1 && page.Media[0].Url.AbsoluteUri == Media(), "Ready/missing formats or exact signed URL changed.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "http://www.medal.tv/games/minecraft/clips/" + Id, "https://medal.tv/games/minecraft/clip/" + Id })
            Assert((await Extract([], request: Request(Path.GetTempPath()) with { Url = new(url) })).Id == Id, "A Medal alias changed its clip identity.");
    }
    private static Task Boundaries()
    {
        var provider = new MedalProvider(); foreach (var url in new[] { "https://medal.tv/games/minecraft", "https://medal.tv/users/123", "https://evil.medal.tv/games/minecraft/clips/" + Id,
            "https://secret@medal.tv/games/minecraft/clips/" + Id, "https://medal.tv:444/games/minecraft/clips/" + Id, "https://medal.tv/games/minecraft/clips/invalid!" }) Assert(!provider.CanHandle(new(url)), "An unsupported Medal address was claimed.");
        Assert(YoutubeDl.Engine.Providers.MediaProviderRegistry.CreateDefault().Resolve(Request(Path.GetTempPath()).Url).Info.Id == "medaltv", "Default provider dispatch missed Medal."); return Task.CompletedTask;
    }
    private static async Task Identities()
    {
        foreach (var change in new Action<JsonNode>[] { c => c["category"]!["slug"] = "other", c => c["contentShareUrl"] = "https://medal.tv/games/minecraft/clips/OtherId" })
        { var clip = Clip(); change(clip); await Expect<InvalidDataException>(() => Extract([], clip)); }
        var conflicting = Clip(); conflicting["contentTitle"] = "Conflicting requested clip"; await Expect<NotSupportedException>(() => Extract([], html: Html(conflicting: conflicting)));
        var other = Clip(); other["contentId"] = "OtherId"; await Expect<NotSupportedException>(() => Extract([], other));
    }
    private static async Task Access()
    {
        foreach (var change in new Action<JsonNode>[] { c => c["privacy"] = 1, c => c["requireLogin"] = true, c => c["hasAccess"] = 0, c => c["processed"] = 0, c => c["state"]!["type"] = "processing", c => c["archivedAt"] = 123, c => ((JsonObject)c).Remove("requireLogin") })
        { var clip = Clip(); change(clip); await Expect<NotSupportedException>(() => Extract([], clip)); }
    }
    private static async Task Dimensions()
    {
        var page = await Extract([]); Assert(page.DurationSeconds == 2 && page.Media[0].Height == 96 && page.Media[0].Width == 160 && page.Media[0].VerificationDurationSeconds == 2, "Source clip facts changed.");
        var silent = Clip(); silent["hasSound"] = 0; Assert((await Extract([], silent)).Media[0].HasAudio == false, "Silent clip acquired audio.");
        var invalid = Clip(); invalid["sourceWidth"] = 0; await Expect<InvalidDataException>(() => Extract([], invalid));
    }
    private static async Task Missing()
    {
        var page = await Extract([]); Assert(page.ReportedFormats.Single(format => format.Id == "720p").Availability == FormatAvailability.MissingUrl && !page.Media.Any(media => media.Id == "720p"), "Unavailable rendition became playback.");
        await Expect<InvalidOperationException>(() => Task.FromResult(YoutubeDl.Engine.Download.MediaDownloadPlan.Create(page, Request(Path.GetTempPath()) with { MaximumHeight = 48 })));
    }
    private static async Task AuthBinding()
    {
        foreach (var raw in new[] { Media(id: "OtherId"), Media(owner: "999") }) { var clip = Clip(); clip["contentUrl"] = raw; await Expect<InvalidDataException>(() => Extract([], clip)); }
        var image = Clip(); image["thumbnailUrl"] = Poster("OtherId"); await Expect<InvalidDataException>(() => Extract([], image));
    }
    private static async Task InvalidResources()
    {
        foreach (var raw in new[] { "https://unrelated.example/source.mp4?auth=" + Auth(), "https://cdn.medal.tv/mediac/source.webm?auth=" + Auth(), "https://secret@cdn.medal.tv/mediac/source.mp4?auth=" + Auth() })
        { var clip = Clip(); clip["contentUrl"] = raw; await Expect<NotSupportedException>(() => Extract([], clip)); }
        var repeated = Clip(); repeated["contentUrl"] = Media() + "&auth=" + Auth(); await Expect<InvalidDataException>(() => Extract([], repeated));
        var malformed = Clip(); malformed["contentUrl"] = "https://cdn.medal.tv/mediac/source.mp4?auth=bad"; await Expect<InvalidDataException>(() => Extract([], malformed));
    }
    private static async Task Renditions()
    {
        var clip = Clip(); clip["contentUrl720p"] = Media("720.mp4") + "&t=720p"; var page = await Extract([], clip);
        Assert(page.Media.Count == 2 && page.Media.Single(media => media.Id == "720p").Height == 720 && page.Media.Single(media => media.Id == "720p").Width is null, "A supplied ready rendition was dropped or given invented width.");
    }
    private static async Task Anonymous()
    {
        var options = new MediaHttpOptions(); options.Headers["Authorization"] = "Bearer fixture"; options.Headers["Cookie"] = "fixture=secret"; using var http = Http([], options: options,
            observe: r => Assert(!r.Headers.Contains("Authorization") && !r.Headers.Contains("Cookie"), "Caller credentials reached public Medal metadata.")); _ = await new MedalProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
    }
    private static async Task HlsBinding()
    {
        var clip = Clip(); clip["contentUrlHls"] = Hls(); var page = await Extract([], clip);
        var hls = page.Media.Single(media => media.Id == "hls");
        Assert(hls.Kind == MediaKind.Hls && hls.Url.AbsoluteUri == Hls() && hls.Width is null && hls.Height is null && hls.VerificationDurationSeconds == 2,
            "HLS token changed or source dimensions became manifest rendition facts.");
        Assert(page.ReportedFormats.Single(format => format.Id == "hls").Protocol == "m3u8", "HLS was reported as a progressive format.");
    }
    private static async Task HlsFailures()
    {
        foreach (var url in new[] { Hls("OtherId"), Hls().Replace("medal.tv", "unrelated.example"), Hls() + "&bebit=duplicate", Hls().Replace("master.m3u8", "source.m3u8"), "https://medal.tv/api/hls/" + Id + "/master.m3u8?bebit=invalid" })
        { var clip = Clip(); clip["contentUrlHls"] = url; await Expect<InvalidDataException>(() => Extract([], clip)); }
    }
    private static async Task Archives()
    {
        var directory = Path.Combine(Path.GetTempPath(), "medal-archive-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try { var archive = Path.Combine(directory, "archive.txt"); await File.WriteAllTextAsync(archive, "medaltv " + Id + "\n"); var calls = new List<Uri>(); var engine = new NativeEngine([new MedalProvider()], _ => Http(calls));
            var result = await engine.DownloadAsync(Request(directory) with { DownloadArchiveFile = archive }, null, null, default); Assert(result.Succeeded && result.Skipped && calls.Count == 0, "Archived clip fetched metadata or playback."); }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task Cancellation()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel(); using var http = Http([]); await Expect<OperationCanceledException>(() => new MedalProvider().ExtractAsync(Request(Path.GetTempPath()), http, stop.Token));
        using var denied = Http([], status: HttpStatusCode.Forbidden); await Expect<HttpRequestException>(() => new MedalProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
        await Expect<InvalidDataException>(() => Extract([], redirect: true));
    }
    private static async Task<PageMetadata> Extract(List<Uri> calls, JsonNode? clip = null, string? html = null, NativeDownloadRequest? request = null, bool redirect = false)
    { using var http = Http(calls, html ?? Html(clip), redirect: redirect); return await new MedalProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static MediaHttp Http(List<Uri> calls, string? html = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null, MediaHttpOptions? options = null,
        Action<HttpRequestMessage>? observe = null, bool redirect = false, HttpStatusCode status = HttpStatusCode.OK) => new(new Handler(r =>
    { calls.Add(r.RequestUri!); observe?.Invoke(r); if (r.RequestUri!.Host == "medal.tv" && !r.RequestUri.AbsolutePath.StartsWith("/api/hls/", StringComparison.Ordinal)) return new(status) { Content = new StringContent(html ?? Html()), RequestMessage = redirect ? new(HttpMethod.Get, "https://medal.tv/games/minecraft/clips/OtherId") : r };
        return media?.Invoke(r) ?? throw new InvalidOperationException("Unexpected Medal media request."); }), options ?? new());
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
}
