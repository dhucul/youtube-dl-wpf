using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;

internal static class RumbleCollectionChecks
{
    internal const string Url = "https://rumble.com/c/Example";
    internal static JsonObject Item(string id, long number) => new() { ["object_type"] = "video", ["id"] = number, ["permalink_id"] = id,
        ["url"] = "https://rumble.com/" + id + "-public-film.html", ["relative_url"] = "/" + id + "-public-film.html", ["title"] = "Video " + id,
        ["duration"] = 2, ["thumb"] = "https://test.cdn.rumble.cloud/cover.jpg", ["visibility"] = "public", ["availability"] = null,
        ["live"] = false, ["live_placeholder"] = false, ["is_processing"] = false, ["has_access_to_premium"] = false, ["downloadable"] = false };
    internal static string Page(IEnumerable<JsonNode> items, int page = 1, string? next = null, string address = Url, string type = "channel", string slug = "Example", string id = "99")
    {
        var canonical = address + (address.Contains('?') ? "&" : "?") + "page=" + page;
        var payload = new JsonObject { ["items"] = new JsonArray(items.Select(item => item.DeepClone()).ToArray()), ["analytics"] = new JsonObject { ["unrelated"] = "ignored" } };
        return "<meta property=og:url content=\"" + WebUtility.HtmlEncode(canonical) + "\"><meta property=og:description content='Channel &amp; description'>" +
            (next is null ? "" : "<link rel=next href=\"" + WebUtility.HtmlEncode(next) + "\">") +
            "<div class='decoy' class='ignored'></div><a href='https://rumble.com/v9-unrelated.html'>Sidebar</a>" +
            "<div class='followed-channel flex' data-type='" + type + "' data-slug='" + slug + "' data-id='" + id + "' data-title='Example &amp; creator'></div>" +
            "<script type='application/json'>{\"items\":[{\"unrelated\":true}]}</script><rum-videos-grid><script type='application/json'>" + payload.ToJsonString() + "</script></rum-videos-grid>";
    }
    internal static NativeDownloadRequest Request(string output) => new() { Url = new(Url), OutputDirectory = output, FileName = "collection", Retries = 0 };
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Rumble collections read only the scoped video grid and confirmed header", Grid),
        ("Rumble collections preserve ordered positions across complete pagination", Pagination),
        ("Rumble collection tabs filters and user identities remain bound during pagination", Filters),
        ("Rumble collection explicit starting pages retain their scope and warning", StartingPage),
        ("Rumble collection unavailable and unsupported entries keep their positions", Availability),
        ("Rumble collection identity and video-link mismatches fail explicitly", Identities),
        ("Rumble collection repeated skipped stalled and missing continuations fail", Continuations),
        ("Rumble collection limits and mid-pagination denial never return a truncated inventory", LimitsAndDenial),
        ("Rumble collection empty grids and selected-only simulation preserve intent", Selection),
        ("Rumble collection cancellation stops pagination before any playback lookup", Cancellation)
    ];
    private static async Task Grid()
    {
        var calls = new List<Uri>(); var info = await Engine(calls, _ => Page([Item("v1", 101), Item("v2", 102)])).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.IsPlaylist && info.Id == "channel:99" && info.PlaylistId == "channel:99" && info.Title == "Example & creator" && info.Description == "Channel & description" && info.Entries.Count == 2 && calls.Count == 1, "Scoped collection metadata changed.");
        Assert(info.Entries.All(entry => entry.Available && entry.DurationSeconds == 2 && entry.ThumbnailUrl is not null) && info.Entries.Select(entry => entry.Id).SequenceEqual(new[] { "v1", "v2" }), "Sidebar links, download-button flags or tracking data changed the inventory.");
    }
    private static async Task Pagination()
    {
        var calls = new List<Uri>(); var info = await Engine(calls, page => Page([Item("v" + page, 100 + page)], page, page < 3 ? Url + "?page=" + (page + 1) : null)).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(calls.Count == 3 && info.Entries.Select(entry => entry.Position).SequenceEqual(new[] { 1, 2, 3 }) && info.Entries.Select(entry => entry.Id).SequenceEqual(new[] { "v1", "v2", "v3" }), "Pagination skipped or reordered source entries.");
    }
    private static async Task Filters()
    {
        const string address = "https://rumble.com/user/Example/videos?sort=views";
        var calls = new List<Uri>(); var engine = Engine(calls, page => Page([Item("v" + page, 100 + page)], page, page == 1 ? address + "&page=2" : null, address, "user"));
        var info = await engine.InspectAsync(Request(Path.GetTempPath()) with { Url = new(address + "&e9s=tracking") }, default);
        Assert(info.Id == "user:99" && calls.Count == 2 && calls.All(url => url.Query.Contains("sort=views")) && info.Entries.Count == 2, "A tab, filter or user listing changed identity.");
        await Expect<InvalidDataException>(() => Engine([], _ => Page([Item("v1", 101)], next: Url + "?sort=views&page=2")).InspectAsync(Request(Path.GetTempPath()), default));
    }
    private static async Task StartingPage()
    {
        var calls = new List<Uri>(); var info = await Engine(calls, page => Page([Item("v" + page, page)], page, page == 2 ? Url + "?page=3" : null))
            .InspectAsync(Request(Path.GetTempPath()) with { Url = new(Url + "?page=2") }, default);
        Assert(calls.Count == 2 && calls[0].Query == "?page=2" && info.Entries[0].Position == 1 && info.Entries[0].Id == "v2" && info.Warnings.Count == 1, "The requested starting page was silently reset or represented as a full root listing.");
    }
    private static async Task Availability()
    {
        var live = Item("v2", 102); live["live"] = true;
        var restricted = Item("v3", 103); restricted["availability"] = "premium";
        var processing = Item("v4", 104); processing["is_processing"] = true;
        var info = await Engine([], _ => Page([Item("v1", 101), live, restricted, processing, new JsonObject { ["object_type"] = "post", ["title"] = "Text post" }]))
            .InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Entries.Count == 5 && info.Entries.Select(entry => entry.Available).SequenceEqual(new[] { true, false, false, false, false }) && info.Entries[4].Position == 5 && info.Entries[4].Url is null, "Unavailable source positions disappeared or became downloadable.");
    }
    private static async Task Identities()
    {
        foreach (var html in new[] { Page([Item("v1", 101)], address: "https://rumble.com/c/Other"), Page([Item("v1", 101)], slug: "Other"),
            Page([Item("v1", 101)], id: "0"), Page([Item("v1", 101)], type: "user") })
            await Expect<InvalidDataException>(() => Engine([], _ => html).InspectAsync(Request(Path.GetTempPath()), default));
        var wrong = Item("v1", 101); wrong["relative_url"] = "/v2-other.html";
        await Expect<InvalidDataException>(() => Engine([], _ => Page([wrong])).InspectAsync(Request(Path.GetTempPath()), default));
        await Expect<InvalidDataException>(() => Engine([], page => Page([Item("v" + page, page)], page, page == 1 ? Url + "?page=2" : null, id: page == 1 ? "99" : "100"))
            .InspectAsync(Request(Path.GetTempPath()), default));
        await Expect<InvalidDataException>(() => Engine([], _ => Page([Item("v1", 101)], address: "https://rumble.com/c/c-99", slug: "c-99", id: "100"))
            .InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://rumble.com/c/c-99") }, default));
    }
    private static async Task Continuations()
    {
        foreach (var next in new[] { Url, Url + "?page=3", "https://rumble.com/c/Other?page=2" })
            await Expect<InvalidDataException>(() => Engine([], _ => Page([Item("v1", 101)], next: next)).InspectAsync(Request(Path.GetTempPath()), default));
        await Expect<InvalidDataException>(() => Engine([], _ => Page([], next: Url + "?page=2")).InspectAsync(Request(Path.GetTempPath()), default));
        await Expect<InvalidDataException>(() => Engine([], page => Page([Item("v1", 101)], page, page == 1 ? Url + "?page=2" : null)).InspectAsync(Request(Path.GetTempPath()), default));
        await Expect<InvalidDataException>(() => Engine([], _ => Page([Item("v1", 101)]) + "<nav><a href='" + Url + "?page=2'>Next</a></nav>").InspectAsync(Request(Path.GetTempPath()), default));
    }
    private static async Task LimitsAndDenial()
    {
        var calls = new List<Uri>();
        await Expect<InvalidDataException>(() => Engine(calls, _ => Page([Item("v1", 101), Item("v2", 102)])).InspectAsync(Request(Path.GetTempPath()) with { MaximumPlaylistEntries = 1 }, default));
        Assert(calls.Count == 1, "An excessive first page triggered further requests.");
        calls.Clear(); await Expect<HttpRequestException>(() => Engine(calls, page => Page([Item("v" + page, page)], page, Url + "?page=2"), deniedPage: 2).InspectAsync(Request(Path.GetTempPath()), default));
        Assert(calls.Count == 2, "A pagination denial triggered an alternate route.");
    }
    private static async Task Selection()
    {
        var empty = await Engine([], _ => Page([])).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(empty.IsPlaylist && empty.Entries.Count == 0, "An explicit empty grid was not preserved.");
        var calls = new List<Uri>();
        var result = await Engine(calls, _ => Page([Item("v1", 101), Item("v71gwwi", 422061706)])).DownloadAsync(Request(Path.GetTempPath()) with { Simulate = true, PlaylistItems = "2" }, null, null, default);
        Assert(result.Succeeded && result.Simulated && calls.Count == 3 && !calls.Any(url => url.AbsolutePath.StartsWith("/v1-")), "Selection requested unselected playback.");
        await Expect<InvalidOperationException>(() => Engine([], _ => Page([Item("v1", 101)])).DownloadAsync(Request(Path.GetTempPath()) with { DownloadPlaylist = false }, null, null, default));
    }
    private static async Task Cancellation()
    {
        using var stop = new CancellationTokenSource(); var calls = new List<Uri>();
        await Expect<OperationCanceledException>(() => Engine(calls, page => { if (page == 2) stop.Cancel(); return Page([Item("v" + page, page)], page, Url + "?page=2"); }).InspectAsync(Request(Path.GetTempPath()), stop.Token));
        Assert(calls.Count == 2 && calls.All(url => url.AbsolutePath.StartsWith("/c/")), "Cancellation requested playback or additional pages.");
    }
    internal static NativeEngine Engine(List<Uri> calls, Func<int, string> page, int? deniedPage = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null) =>
        new(httpFactory: options => new MediaHttp(new Handler(request =>
        {
            var url = request.RequestUri!; calls.Add(url);
            if (url.AbsolutePath.StartsWith("/c/") || url.AbsolutePath.StartsWith("/user/"))
            {
                var part = url.Query.TrimStart('?').Split('&').FirstOrDefault(value => value.StartsWith("page=")); var number = part is null ? 1 : int.Parse(part[5..]);
                return new(number == deniedPage ? HttpStatusCode.Forbidden : HttpStatusCode.OK) { Content = new StringContent(page(number)) };
            }
            if (url.AbsolutePath == "/v71gwwi-public-film.html") return new(HttpStatusCode.OK) { Content = new StringContent(RumbleChecks.WatchPage) };
            if (url.AbsolutePath.StartsWith("/embedJS/")) return new(HttpStatusCode.OK) { Content = new StringContent(RumbleChecks.Playback().ToJsonString()) };
            return media?.Invoke(request) ?? throw new InvalidOperationException("Unexpected collection playback request.");
        }), new MediaHttpOptions { UserAgent = options.UserAgent ?? "", Referer = options.Referer ?? "" }));
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
}
