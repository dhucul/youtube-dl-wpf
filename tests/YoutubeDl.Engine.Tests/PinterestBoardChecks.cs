using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Pinterest;

internal static class PinterestBoardChecks
{
    internal const string BoardId = "626281960621547297";
    internal static Uri Page => new("https://www.pinterest.com/fixture/videos/");
    internal static NativeDownloadRequest Request(string directory) => PinterestChecks.Request(directory) with { Url = Page };
    internal static JsonNode Row(string id, bool hosted = false) => new JsonObject { ["type"] = "pin", ["id"] = id, ["node_id"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("Pin:" + id)),
        ["title"] = "Pin " + id, ["videos"] = hosted ? new JsonObject { ["duration"] = 2000, ["video_list"] = new JsonObject { ["V_720P"] = new JsonObject() } } : null };
    internal static JsonObject Options() => new() { ["board_id"] = BoardId, ["field_set_key"] = "react_grid_pin", ["filter_section_pins"] = false, ["gated"] = true,
        ["is_react"] = true, ["page_size"] = 25, ["prepend"] = false, ["redux_normalize_feed"] = true, ["add_vase"] = true };
    private static string Key(JsonObject options) => new JsonArray(options.Select(property => (JsonNode)new JsonArray(JsonValue.Create(property.Key), property.Value?.DeepClone())).ToArray()).ToJsonString();
    internal static JsonNode Props(JsonNode[] rows, int count, string cursor = "-end-", string revision = "fixture-revision-1")
    {
        var metadataOptions = new JsonObject { ["username"] = "fixture", ["slug"] = "videos", ["field_set_key"] = "detailed" };
        return new JsonObject { ["initialReduxState"] = new JsonObject { ["resources"] = new JsonObject {
            ["BoardResource"] = new JsonObject { [Key(metadataOptions)] = new JsonObject { ["fetching"] = false, ["data"] = new JsonObject { ["id"] = BoardId,
                ["node_id"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("Board:" + BoardId)), ["url"] = Page.AbsolutePath, ["privacy"] = "public", ["section_count"] = 0,
                ["pin_count"] = count, ["board_order_modified_at"] = revision, ["name"] = "Fixture board", ["description"] = "Public board fixture",
                ["owner"] = new JsonObject { ["username"] = "fixture", ["full_name"] = "Fixture owner" } } } },
            ["BoardFeedResource"] = new JsonObject { [Key(Options())] = new JsonObject { ["fetching"] = false, ["data"] = new JsonArray(rows.Select(row => row.DeepClone()).ToArray()), ["nextBookmark"] = cursor } } } } };
    }
    internal static string Html(JsonNode props) => "<script id='__PWS_INITIAL_PROPS__' type='application/json'>" + props.ToJsonString() + "</script>";
    internal static JsonNode Api(JsonNode[] rows, string cursor = "-end-")
    { var options = Options(); options["bookmarks"] = new JsonArray(cursor); return new JsonObject { ["resource"] = new JsonObject { ["name"] = "BoardFeedResource", ["options"] = options },
        ["resource_response"] = new JsonObject { ["status"] = "success", ["data"] = new JsonArray(rows.Select(row => row.DeepClone()).ToArray()) } }; }
    internal static MediaHttp Http(JsonNode props, JsonNode? api = null, List<Uri>? calls = null, Func<int, JsonNode>? boardState = null,
        Func<HttpRequestMessage, HttpResponseMessage>? media = null, HttpStatusCode apiStatus = HttpStatusCode.OK, string? pinHtml = null) => new(new Handler(request =>
    {
        var url = request.RequestUri!; calls?.Add(url);
        if (request.Headers.Authorization is not null) throw new InvalidOperationException("Account authorization reached public board discovery.");
        if (url.AbsolutePath == Page.AbsolutePath || url.Host == "pin.it") return new(HttpStatusCode.OK) { Content = new StringContent(Html(boardState?.Invoke(calls?.Count(item => item.AbsolutePath == Page.AbsolutePath) ?? 1) ?? props)), RequestMessage = new(HttpMethod.Get, Page) };
        if (url.AbsolutePath == "/resource/BoardFeedResource/get/")
        {
            if (request.Headers.GetValues("X-Pinterest-Pws-Handler").Single() != "www/[username]/[slug].js" || !request.Headers.Accept.Any())
                throw new InvalidOperationException("Pinterest page-generated handler/response headers were lost.");
            var data = url.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
            var body = JsonNode.Parse(data["data"])!;
            if (data["source_url"] != Page.AbsolutePath || body["options"]!["board_id"]!.GetValue<string>() != BoardId || body["options"]!["bookmarks"]![0]!.GetValue<string>().Length == 0)
                throw new InvalidOperationException("Pinterest board pagination changed source identity or cursor.");
            return new(apiStatus) { Content = new StringContent((api ?? Api([])).ToJsonString()) };
        }
        if (url.Host == "www.pinterest.com" && url.AbsolutePath.StartsWith("/pin/")) return new(HttpStatusCode.OK) { Content = new StringContent(pinHtml ?? PinterestChecks.Html(PinterestChecks.Pin())) };
        return media?.Invoke(request) ?? new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
    }), new() { AllowAutoRedirect = false });
    private static async Task<PageMetadata> Read(JsonNode props, JsonNode? api = null, NativeDownloadRequest? request = null, List<Uri>? calls = null, Func<int, JsonNode>? boardState = null, HttpStatusCode apiStatus = HttpStatusCode.OK)
    { using var http = Http(props, api, calls, boardState, apiStatus: apiStatus); return await new PinterestProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Pinterest board public complete inventories preserve pin order and stable source identities", Facts),
        ("Pinterest board pagination uses offered cursors and rechecks the same complete board", Pagination),
        ("Pinterest board declared totals cannot become silent partial or empty inventories", Gaps),
        ("Pinterest board request global owner and resource identities must agree", Binding),
        ("Pinterest board private pending section and unknown scopes fail explicitly", Scopes),
        ("Pinterest board repeated pins or cursors cannot duplicate positions", Repeated),
        ("Pinterest board changed revisions or pagination scopes fail before returning a playlist", Changes),
        ("Pinterest board configured limits stop before a pagination transfer", Limits),
        ("Pinterest board regional tracking and short aliases preserve board identity", Aliases),
        ("Pinterest board official anchor widgets delegate to complete native inventory", Widgets),
        ("Pinterest board unavailable source positions remain explicit", Unavailable),
        ("Pinterest board archive skips selected known videos before fresh pin playback", Archive),
        ("Pinterest board empty complete inventories remain distinct from account refusals", Empty)
    ];
    private static async Task Facts()
    {
        var module = new JsonObject { ["type"] = "story", ["story_type"] = "related_interests_module" }; var page = await Read(Props([Row("1"), module, Row(PinterestChecks.Id, true)], 2));
        Assert(page.IsPlaylist && page.Extractor == "pinterest:board" && page.Id == BoardId && page.Entries.Count == 2 && page.Entries[1].Position == 2 && page.Entries[1].Id == PinterestChecks.Id &&
            page.Entries[1].ResolvedMedia is null && page.Entries[1].ExpectedArchiveIdentity?.Id == PinterestChecks.Id, "Board source order, UI-module handling, selected-only discovery or archive identities changed.");
    }
    private static async Task Pagination()
    {
        var calls = new List<Uri>(); var page = await Read(Props([Row("1"), Row("2")], 4, "opaque+A/B"), Api([Row("3"), Row("4")]), calls: calls);
        Assert(page.Entries.Select(item => item.Id).SequenceEqual(new[] { "1", "2", "3", "4" }) && calls.Count == 3 && calls.Count(url => url.AbsolutePath == Page.AbsolutePath) == 2,
            "Complete pagination did not preserve order or recheck the same board.");
    }
    private static async Task Gaps()
    {
        await Reject<NotSupportedException>(() => Read(Props([Row("1")], 4)));
        await Reject<NotSupportedException>(() => Read(Props([Row("1")], 4, "next"), Api([])));
        await Reject<InvalidDataException>(() => Read(Props([], 4, "next")));
    }
    private static JsonObject Board(JsonNode props) => (JsonObject)((JsonObject)props["initialReduxState"]!["resources"]!["BoardResource"]!).First().Value!["data"]!;
    private static async Task Binding()
    {
        foreach (var field in new[] { "id", "node_id", "url" }) { var props = Props([Row("1")], 1); Board(props)[field] = "wrong"; await Reject<InvalidDataException>(() => Read(props)); }
        var missing = Props([Row("1")], 1); Board(missing)["owner"] = new JsonObject(); await Reject<InvalidDataException>(() => Read(missing));
        var api = Api([Row("2")]); api["resource"]!["options"]!["board_id"] = "2"; await Reject<InvalidDataException>(() => Read(Props([Row("1")], 2, "next"), api));
    }
    private static async Task Scopes()
    {
        foreach (var field in new[] { "privacy", "section_count" }) { var props = Props([Row("1")], 1); Board(props)[field] = field == "privacy" ? JsonValue.Create("private") : JsonValue.Create(1); await Reject<NotSupportedException>(() => Read(props)); }
        var unknown = Props([new JsonObject { ["type"] = "board_section", ["id"] = "1" }], 1); await Reject<NotSupportedException>(() => Read(unknown));
        var pending = Props([Row("1")], 1); ((JsonObject)pending["initialReduxState"]!["resources"]!["BoardFeedResource"]!).First().Value!["fetching"] = true;
        await Reject<NotSupportedException>(() => Read(pending));
    }
    private static async Task Repeated()
    {
        await Reject<InvalidDataException>(() => Read(Props([Row("1"), Row("1")], 2)));
        await Reject<InvalidDataException>(() => Read(Props([Row("1")], 4, "next"), Api([Row("2")], "next")));
    }
    private static async Task Changes()
    {
        var first = Props([Row("1")], 2, "next"); var changed = Props([Row("1")], 2, "next", "changed"); var calls = new List<Uri>();
        await Reject<InvalidDataException>(() => Read(first, Api([Row("2")]), calls: calls, boardState: reads => reads > 1 ? changed : first));
        var api = Api([Row("2")]); api["resource"]!["options"]!["field_set_key"] = "other"; await Reject<InvalidDataException>(() => Read(first, api));
    }
    private static async Task Limits()
    { var calls = new List<Uri>(); await Reject<NotSupportedException>(() => Read(Props([Row("1")], 4, "next"), request: Request(Path.GetTempPath()) with { MaximumPlaylistEntries = 3 }, calls: calls)); Assert(calls.Count == 1, "Oversized board continued fetching."); }
    private static async Task Aliases()
    {
        var props = Props([Row("1")], 1);
        foreach (var url in new[] { "https://br.pinterest.com/fixture/videos/?utm_source=test", "https://pin.it/fixtureBoard" })
            Assert((await Read(props, request: Request(Path.GetTempPath()) with { Url = new(url) })).Id == BoardId, "Board alias changed identity.");
        await Reject<NotSupportedException>(() => Read(props, request: Request(Path.GetTempPath()) with { Url = new(Page.AbsoluteUri + "?sort=other") }));
    }
    private static async Task Unavailable()
    { var row = Row("1"); row["is_hidden"] = true; var page = await Read(Props([row, Row(PinterestChecks.Id, true)], 2)); Assert(!page.Entries[0].Available && page.Entries[1].Position == 2, "Hidden board positions were dropped or renumbered."); }
    private static async Task Widgets()
    {
        var html = "<a data-pin-do='embedBoard' href='" + Page.AbsoluteUri + "'></a>";
        var embeds = EmbeddedMediaReader.Read(html + "<a href='https://www.pinterest.com/other/board/'></a>", new("https://article.example/board"));
        Assert(embeds.Count == 1 && embeds[0] == Page, "A board widget followed undeclared board links.");
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request => new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.Host == "article.example" ?
            html : Html(Props([Row("1")], 1))) })));
        var info = await engine.InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://article.example/board") }, default);
        Assert(info.IsPlaylist && info.Extractor == "pinterest:board" && info.Entries.Count == 1, "Board widget did not reach native collection discovery.");
    }
    private static async Task Archive()
    {
        var directory = Path.Combine(Path.GetTempPath(), "native-pin-board-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try { var archive = Path.Combine(directory, "archive.txt"); await File.WriteAllTextAsync(archive, "pinterest " + PinterestChecks.Id + "\n"); var calls = new List<Uri>();
            var engine = new NativeEngine([new PinterestProvider()], _ => Http(Props([Row("1"), Row(PinterestChecks.Id, true)], 2), calls: calls));
            var result = await engine.DownloadAsync(Request(directory) with { PlaylistItems = "2", DownloadArchiveFile = archive }, null, null, default);
            Assert(result.Succeeded && calls.Count == 1 && calls[0].AbsolutePath == Page.AbsolutePath, "Board archive retry fetched fresh pin or media."); }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task Empty()
    {
        Assert((await Read(Props([], 0))).Entries.Count == 0, "Empty complete board was refused.");
        var malformed = Props([], 0); ((JsonObject)malformed["initialReduxState"]!["resources"]!["BoardFeedResource"]!).First().Value!["data"] = new JsonObject();
        await Reject<InvalidDataException>(() => Read(malformed));
        await Reject<NotSupportedException>(() => Read(Props([Row("1")], 2, "next"), apiStatus: HttpStatusCode.Forbidden));
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(run(request)); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Reject<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
