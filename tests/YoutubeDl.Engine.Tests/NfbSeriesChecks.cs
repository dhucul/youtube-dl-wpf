using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;

internal static class NfbSeriesChecks
{
    internal const string Url = "https://www.nfb.ca/series/fixture-series/";
    internal static JsonObject Episode(int id, string slug, int number) => new() { ["id"] = id, ["slug"] = slug, ["number"] = number, ["title"] = "Episode " + number,
        ["category"] = "film", ["duration"] = 2, ["thumbnail"] = "thumbs/" + slug + ".jpg", ["geoblocked"] = false, ["coming_soon"] = null,
        ["availability"] = new JsonObject { ["is_public"] = true, ["resource_url"] = "https://www.nfb.ca/film/" + slug + "/", ["buy"] = false, ["rent"] = false },
        ["part_of"] = new JsonArray(new JsonObject { ["category"] = "series", ["registry_id"] = 100, ["slug"] = "fixture-series" }) };
    internal static JsonNode Data() => new JsonObject {
        ["item"] = new JsonObject { ["id"] = 100, ["slug"] = "fixture-series", ["category"] = "series", ["title"] = "Series &amp; title", ["description"] = "Series description",
            ["availability"] = new JsonObject { ["is_public"] = true, ["resource_url"] = Url }, ["thumbnail"] = "thumbs/series.jpg", ["seasons_count"] = 2, ["episodes_count"] = 3,
            ["seasons"] = new JsonArray(new JsonObject { ["id"] = 10, ["number"] = 1, ["title"] = "Season 1", ["episodes"] = new JsonArray(Episode(213821, "first", 1), Episode(213822, "fixture", 2)) },
                new JsonObject { ["id"] = 20, ["number"] = 2, ["title"] = "Season 2", ["episodes"] = new JsonArray(Episode(213823, "third", 1)) }) },
        ["meta"] = new JsonObject { ["media"] = new JsonObject { ["original_url"] = "https://assets.nfbcdn.ca/medias/norman/{path}" } } };
    internal static string Html(int season = 1, int? episode = null, int displayed = 100) => "<script>window.PUBLIC_API_ENDPOINT=\"https://publicapi.nfb.ca\";window.SERIES_TYPE='series';window.SERIES_ID=100;window.SERIES_SLUG='fixture-series';" +
        "window.SEASON_NUMBER='" + season + "';window.EPISODE_INDEX=" + (episode?.ToString() ?? "null") + ";window.DISPLAYED_WORK_ID=" + displayed + ";</script>";
    internal static NativeDownloadRequest Request(string output) => new() { Url = new(Url), OutputDirectory = output, FileName = "series", Retries = 0 };
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("NFB series preserve complete season and episode order without playback lookups", Inventory),
        ("NFB explicit seasons retain their own collection scope", Season),
        ("NFB numeric and slug episode links resolve the selected registry", EpisodeLinks),
        ("NFB series detect identity count membership and URL inconsistencies", Invalid),
        ("NFB series retain unavailable episode positions", Availability),
        ("NFB series limits apply to the requested collection without truncation", Limits),
        ("NFB series selection and known archives avoid unselected film lookups", Selection),
        ("NFB series bind selected film metadata to advertised registry identities", ResolvedIdentity),
        ("NFB series HTTP failures and cancellation return no partial inventory", Failures)
    ];
    private static async Task Inventory()
    {
        var calls = new List<Uri>(); var info = await Engine(calls).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.IsPlaylist && info.Id == "100" && info.Title == "Series & title" && info.Entries.Count == 3 && calls.Count == 2, "Series inventory or identity changed.");
        Assert(info.Entries.Select(entry => entry.Id).SequenceEqual(new[] { "213821", "213822", "213823" }) && info.Entries.All(entry => entry.Available && entry.ThumbnailUrl!.StartsWith("https://assets.nfbcdn.ca/medias/norman/")), "Episode order, availability or supplied image templates changed.");
    }
    private static async Task Season()
    {
        var calls = new List<Uri>(); var info = await Engine(calls).InspectAsync(Request(Path.GetTempPath()) with { Url = new(Url + "season2/") }, default);
        Assert(info.IsPlaylist && info.Id == "100:season:2" && info.Entries.Count == 1 && info.Entries[0].Id == "213823" && info.Entries[0].Position == 1, "An explicit season returned another scope.");
    }
    private static async Task EpisodeLinks()
    {
        foreach (var token in new[] { "episode2", "fixture" })
        {
            var calls = new List<Uri>(); var info = await Engine(calls).InspectAsync(Request(Path.GetTempPath()) with { Url = new(Url + "season1/" + token + "/"), MaximumPlaylistEntries = 1 }, default);
            Assert(!info.IsPlaylist && info.Id == "213822" && calls.Count == 4 && calls.All(url => !url.AbsolutePath.Contains("/first/") && !url.AbsolutePath.Contains("/third/")), "An episode link fetched another episode or required a full-list limit.");
        }
    }
    private static async Task Invalid()
    {
        foreach (var mutate in new Action<JsonNode>[] { data => data["item"]!["id"] = 101, data => data["item"]!["episodes_count"] = 4, data => data["item"]!["seasons_count"] = 3,
            data => data["item"]!["seasons"]![1]!["number"] = 1, data => data["item"]!["seasons"]![0]!["episodes"]![1]!["id"] = 213821,
            data => data["item"]!["seasons"]![0]!["episodes"]![1]!["part_of"]![0]!["registry_id"] = 101,
            data => data["item"]!["seasons"]![0]!["episodes"]![1]!["availability"]!["resource_url"] = "https://www.nfb.ca/film/other/" })
        { var data = Data(); mutate(data); await Expect<InvalidDataException>(() => Engine([], data).InspectAsync(Request(Path.GetTempPath()), default)); }
        await Expect<InvalidDataException>(() => Engine([]).InspectAsync(Request(Path.GetTempPath()) with { Url = new(Url + "season9/") }, default));
        await Expect<InvalidDataException>(() => Engine([], page: Html(1, 2, 213821)).InspectAsync(Request(Path.GetTempPath()) with { Url = new(Url + "season1/fixture/") }, default));
    }
    private static async Task Availability()
    {
        var data = Data(); data["item"]!["seasons"]![0]!["episodes"]![0]!["geoblocked"] = true; data["item"]!["seasons"]![1]!["episodes"]![0]!["coming_soon"] = "future";
        var info = await Engine([], data).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Entries.Select(entry => entry.Available).SequenceEqual(new[] { false, true, false }) && info.Entries[2].Position == 3, "Unavailable episodes were omitted or exposed as ready.");
    }
    private static async Task Limits()
    {
        var calls = new List<Uri>(); await Expect<InvalidDataException>(() => Engine(calls).InspectAsync(Request(Path.GetTempPath()) with { MaximumPlaylistEntries = 2 }, default));
        Assert(calls.Count == 2, "A collection limit fetched episode playback.");
        var selected = await Engine([]).InspectAsync(Request(Path.GetTempPath()) with { Url = new(Url + "season2/"), MaximumPlaylistEntries = 1 }, default);
        Assert(selected.Entries.Count == 1, "A valid season was rejected because other seasons exist.");
    }
    private static async Task Selection()
    {
        using var files = new Files(); var calls = new List<Uri>();
        var result = await Engine(calls).DownloadAsync(Request(files.Directory) with { PlaylistItems = "2", Simulate = true }, null, null, default);
        Assert(result.Succeeded && result.Simulated && calls.Count == 4 && calls.All(url => !url.AbsolutePath.Contains("/first/")), "Unselected episode playback was requested.");
        var archive = Path.Combine(files.Directory, "archive.txt"); await File.WriteAllTextAsync(archive, "nfb:registry 213822\n"); calls.Clear();
        result = await Engine(calls).DownloadAsync(Request(files.Directory) with { PlaylistItems = "2", DownloadArchiveFile = archive }, null, null, default);
        Assert(result.Succeeded && result.Skipped && calls.Count == 2, "An advertised archived registry required film or player lookup.");
    }
    private static async Task ResolvedIdentity()
    {
        using var files = new Files(); var calls = new List<Uri>();
        var result = await Engine(calls, wrongFilm: true).DownloadAsync(Request(files.Directory) with { PlaylistItems = "2", Simulate = true }, null, null, default);
        Assert(!result.Succeeded && result.Failures.Count == 1 && calls.Count == 4 && result.OutputPaths.Count == 0, "Another resolved registry was accepted as the selected episode.");
    }
    private static async Task Failures()
    {
        var calls = new List<Uri>(); await Expect<HttpRequestException>(() => Engine(calls, apiStatus: HttpStatusCode.Forbidden).InspectAsync(Request(Path.GetTempPath()), default));
        Assert(calls.Count == 2, "API denial triggered another endpoint.");
        using var stop = new CancellationTokenSource(); calls.Clear();
        await Expect<OperationCanceledException>(() => Engine(calls, observe: request => { if (request.RequestUri!.Host == "publicapi.nfb.ca") stop.Cancel(); }).InspectAsync(Request(Path.GetTempPath()), stop.Token));
        Assert(calls.Count == 2, "Canceled series discovery continued.");
    }
    internal static NativeEngine Engine(List<Uri> calls, JsonNode? data = null, string? page = null, bool wrongFilm = false, HttpStatusCode apiStatus = HttpStatusCode.OK,
        Action<HttpRequestMessage>? observe = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null) => new(httpFactory: options => new MediaHttp(new Handler(request =>
    {
        var url = request.RequestUri!; lock (calls) calls.Add(url); observe?.Invoke(request);
        if (url.Host == "publicapi.nfb.ca") return new(apiStatus) { Content = new StringContent((data ?? Data()).ToJsonString()) };
        if (url.AbsolutePath.StartsWith("/series/"))
        {
            var segments = url.AbsolutePath.Trim('/').Split('/'); var season = segments.Length > 2 ? int.Parse(segments[2][6..]) : 1;
            return new(HttpStatusCode.OK) { Content = new StringContent(page ?? Html(season, segments.Length > 3 ? 2 : null, segments.Length > 3 ? 213822 : 100)) };
        }
        var id = wrongFilm ? 213823 : 213822;
        if (url.AbsolutePath == "/film/fixture/") return new(HttpStatusCode.OK) { Content = new StringContent(NfbChecks.Film(id)),
            RequestMessage = new(HttpMethod.Get, new Uri(Url + "season1/fixture/")) };
        if (url.AbsolutePath == "/film/fixture/embed/player/") return new(HttpStatusCode.OK) { Content = new StringContent(NfbChecks.Player(NfbChecks.Options(id: id), id)) };
        return media?.Invoke(request) ?? throw new InvalidOperationException("Unexpected NFB series media request.");
    }), new MediaHttpOptions { UserAgent = options.UserAgent ?? "", Referer = options.Referer ?? "" }));
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() { var path = Path.GetFullPath(Directory); if (!path.StartsWith(Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Fixture escaped its root."); System.IO.Directory.Delete(path, true); }
    }
}
