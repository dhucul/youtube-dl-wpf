using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.ReverbNation;

internal static class ReverbNationChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("ReverbNation public literal metadata retains numeric identities and complete audio", Facts),
        ("ReverbNation current legacy slug and tracking aliases keep recording identity", Aliases),
        ("ReverbNation selected widgets bind declared public artist and recording IDs", Widgets),
        ("ReverbNation unknown private routes selectors fragments and widgets cannot fall through", Boundaries),
        ("ReverbNation wrong artist song canonical and supplied media IDs fail", Ownership),
        ("ReverbNation nonpublic restricted download-only and incomplete states refuse", Restrictions),
        ("ReverbNation metadata protection flags remain distinct from stream access", Protection),
        ("ReverbNation literal bootstraps reject executable expressions and ambiguity", Literals),
        ("ReverbNation counted artist pages retain complete source order and archives", Artists),
        ("ReverbNation repeated truncated changed and overlimit artist inventories fail", Incomplete),
        ("ReverbNation unavailable artist entries preserve original source positions", Positions),
        ("ReverbNation inspected selections refresh only chosen source metadata", Snapshots),
        ("ReverbNation numeric archives prevent repeated delivery and completion", Archives),
        ("ReverbNation anonymous isolation cancellation and HTTP denial stay explicit", Transport)
    ];
    private static async Task Facts()
    {
        using var http = Http([]); var page = await new ReverbNationProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
        Assert(page.Id == "42" && page.ArchiveIdentity == new DownloadArchiveIdentity("reverbnation", "42") && page.Title == "Track" && page.DurationSeconds == 2 && page.Media.Count == 1 && page.Media[0].Url == new Uri(Source), "ReverbNation source facts changed.");
        Assert(MediaProviderRegistry.CreateDefault().Resolve(Request(Path.GetTempPath()).Url).Info.Id == "reverbnation", "ReverbNation was not registered.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "http://www.reverbnation.com/artist/song/42-title", "https://reverbnation.com/artist/song/42?utm_source=test", "https://legacy.reverbnation.com/artist/song/42" })
        { using var http = Http([]); var page = await new ReverbNationProvider().ExtractAsync(Request(Path.GetTempPath()) with { Url = new(url) }, http, default); Assert(page.Id == "42", "Song alias changed identity."); }
    }
    private static async Task Widgets()
    {
        using var http = Http([]); var page = await new ReverbNationProvider().ExtractAsync(Request(Path.GetTempPath()) with { Url = new(WidgetUrl) }, http, default); Assert(page.Id == "42", "Widget changed recording.");
        using var wrong = Http([], (kind, node) => { if (kind == "widget") node["PLAYLIST"]![0]!["id"] = 99; }); await Expect<InvalidDataException>(() => new ReverbNationProvider().ExtractAsync(Request(Path.GetTempPath()) with { Url = new(WidgetUrl) }, wrong, default));
    }
    private static Task Boundaries()
    {
        foreach (var url in new[] { "https://reverbnation.com/artist/song/42?private=key", "https://reverbnation.com/artist/song/42#t=4", "https://user:secret@reverbnation.com/artist/song/42", "https://reverbnation.com:8443/artist/song/42", "https://reverbnation.com/control_room/artist/42", "https://reverbnation.com/artist/album/42", WidgetUrl.Replace("42", "42,43") }) Assert(!new ReverbNationProvider().CanHandle(new(url)), "Unsupported route accepted.");
        Assert(!new GenericPageProvider().CanHandle(new("https://legacy.reverbnation.com/private/song/access/key")), "Private route reached generic extraction."); return Task.CompletedTask;
    }
    private static async Task Ownership()
    {
        foreach (var change in new Action<JsonObject>[] { node => node["ARTIST"]!["homepage"] = "other", node => node["PRIMARY_SONG"]!["id"] = 99, node => node["PRIMARY_SONG"]!["artist"]!["id"] = 8, node => node["PRIMARY_SONG"]!["homepage_url"] = "https://legacy.reverbnation.com/other/song/42", node => node["PRIMARY_SONG"]!["url"] = Source.Replace("/42/", "/43/") })
        { using var http = Http([], (kind, node) => { if (kind == "song") change(node); }); await Expect<InvalidDataException>(() => new ReverbNationProvider().ExtractAsync(Request(Path.GetTempPath()), http, default)); }
    }
    private static async Task Restrictions()
    {
        foreach (var change in new Action<JsonObject>[] { node => node["public"] = false, node => node["streaming_restricted"] = true, node => node["access"] = "download_only" })
        { using var http = Http([], (kind, node) => { if (kind == "song") change(node["PRIMARY_SONG"]!.AsObject()); }); await Expect<NotSupportedException>(() => new ReverbNationProvider().ExtractAsync(Request(Path.GetTempPath()), http, default)); }
        using var invalid = Http([], (kind, node) => { if (kind == "song") node["PRIMARY_SONG"]!["duration"] = 0; }); await Expect<InvalidDataException>(() => new ReverbNationProvider().ExtractAsync(Request(Path.GetTempPath()), invalid, default));
    }
    private static async Task Protection()
    {
        using var http = Http([], (kind, node) => { if (kind == "song") node["PRIMARY_SONG"]!["protected"] = true; }); var page = await new ReverbNationProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
        Assert(page.Media.Count == 1 && page.MetadataFields["protected"] is true, "Metadata protection was mistaken for missing public streaming.");
    }
    private static async Task Literals()
    {
        foreach (var html in new[] { "<script>require(['modules/song_page']);var config=getData();</script>", Page(SongBootstrap(), false) + Page(SongBootstrap(), false), Page(SongBootstrap(), false).Replace(";</script>", "+ call();</script>") })
        { using var http = new MediaHttp(new Handler(request => new(HttpStatusCode.OK) { Content = new StringContent(html), RequestMessage = request })); await Expect<InvalidDataException>(() => new ReverbNationProvider().ExtractAsync(Request(Path.GetTempPath()), http, default)); }
    }
    private static async Task Artists()
    {
        var calls = new List<Uri>(); using var http = Http(calls, (kind, node) => { if (kind == "artist") foreach (var song in node["SONGS_WITH_PAGINATION"]!["results"]!.AsArray().OfType<JsonObject>()) song.Remove("url"); else if (kind == "api") foreach (var song in node["results"]!.AsArray().OfType<JsonObject>()) song.Remove("url"); }); var page = await new ReverbNationProvider().ExtractAsync(Request(Path.GetTempPath(), true), http, default);
        Assert(page.IsPlaylist && page.Entries.Count == 3 && page.Entries[2].Position == 3 && page.Entries[2].ExpectedArchiveIdentity == new DownloadArchiveIdentity("reverbnation", "44") && calls.Any(url => url.AbsolutePath == "/api/artist/9/songs"), "Artist pagination/order changed.");
    }
    private static async Task Incomplete()
    {
        foreach (var mode in new[] { "duplicate", "truncated", "changed" })
        { var count = 0; using var http = Http([], (kind, node) => { if (kind == "artist" && mode == "changed" && ++count == 2) node["SONGS_WITH_PAGINATION"]!["pagination"]!["result_count"] = 4; }, mode: mode); await Expect<InvalidDataException>(() => new ReverbNationProvider().ExtractAsync(Request(Path.GetTempPath(), true), http, default)); }
        using var limited = Http([]); await Expect<NotSupportedException>(() => new ReverbNationProvider().ExtractAsync(Request(Path.GetTempPath(), true) with { MaximumPlaylistEntries = 2 }, limited, default));
    }
    private static async Task Positions()
    {
        using var http = Http([], (kind, node) => { if (kind == "artist") node["SONGS_WITH_PAGINATION"]!["results"]![0]!["public"] = false; }); var page = await new ReverbNationProvider().ExtractAsync(Request(Path.GetTempPath(), true), http, default);
        Assert(!page.Entries[0].Available && page.Entries[1].Position == 2, "Unavailable entry shifted source positions.");
    }
    private static async Task Snapshots()
    {
        using var files = new Files(); var calls = new List<Uri>(); var engine = new NativeEngine([new ReverbNationProvider()], _ => Http(calls)); var request = Request(files.Path, true) with { PlaylistItems = "3", Simulate = true };
        var info = await engine.InspectAsync(request, default); calls.Clear(); var result = await engine.DownloadAsync(request with { PlaylistSnapshot = info.PlaylistSnapshot }, null, null, default);
        Assert(result.Succeeded && calls.Count == 1 && calls[0].AbsolutePath == "/artist/song/44", "Selection refreshed unselected sources.");
    }
    private static async Task Archives()
    {
        using var files = new Files(); var archive = Path.Combine(files.Path, "archive.txt"); await File.WriteAllTextAsync(archive, "reverbnation 42\n"); var calls = new List<Uri>(); var engine = new NativeEngine([new ReverbNationProvider()], _ => Http(calls));
        var result = await engine.DownloadAsync(Request(files.Path) with { DownloadArchiveFile = archive }, null, null, default, _ => throw new InvalidOperationException("Archive emitted completion.")); Assert(result.Skipped && calls.Count == 1, "Archive repeated delivery.");
    }
    private static async Task Transport()
    {
        using var http = Http([], options: new MediaHttpOptions { Headers = { ["Authorization"] = "private fixture", ["Cookie"] = "private=fixture" } }); var page = await new ReverbNationProvider().ExtractAsync(Request(Path.GetTempPath()), http, default); Assert(page.Id == "42", "Anonymous discovery failed.");
        using var stop = new CancellationTokenSource(); stop.Cancel(); await Expect<OperationCanceledException>(() => new ReverbNationProvider().ExtractAsync(Request(Path.GetTempPath()), http, stop.Token)); using var denied = new MediaHttp(new Handler(_ => new(HttpStatusCode.Forbidden))); await Expect<HttpRequestException>(() => new ReverbNationProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
    }
    internal const string Source = "https://dnma3z6c8ojev.cloudfront.net/songs/mp3_files/42/original/track.mp3?signed%2Bquery";
    internal const string WidgetUrl = "https://legacy.reverbnation.com/widget_code/html_widget/artist_9?widget_id=55&pwc[included_songs]=0&pwc[song_ids]=42";
    internal static NativeDownloadRequest Request(string output, bool artist = false) => new() { Url = new(artist ? "https://legacy.reverbnation.com/artist" : "https://legacy.reverbnation.com/artist/song/42"), OutputDirectory = output, OutputTemplate = "%(id)s.%(ext)s", Retries = 0 };
    internal static JsonObject Artist() => new() { ["id"] = 9, ["name"] = "Artist", ["homepage"] = "artist", ["type"] = "artist" };
    internal static JsonObject Song(int id = 42) => new() { ["id"] = id, ["name"] = " Track ", ["artist"] = new JsonObject { ["id"] = 9, ["name"] = "Artist" }, ["duration"] = 2, ["public"] = true, ["protected"] = false, ["streaming_restricted"] = false, ["access"] = "streaming_only", ["url"] = Source.Replace("/42/", "/" + id + "/"), ["homepage_url"] = "https://legacy.reverbnation.com/artist/song/" + id };
    internal static JsonObject SongBootstrap(int id = 42) => new() { ["ARTIST"] = Artist(), ["PRIMARY_SONG"] = Song(id) };
    internal static JsonObject ArtistBootstrap() => new() { ["ARTIST"] = Artist(), ["SONGS_WITH_PAGINATION"] = Inventory(false) };
    internal static JsonObject Inventory(bool second) => new() { ["pagination"] = new JsonObject { ["page"] = second ? 2 : 1, ["per_page"] = 2, ["page_count"] = 2, ["result_count"] = 3 }, ["results"] = second ? new JsonArray(Song(44)) : new JsonArray(Song(), Song(43)) };
    internal static string Page(JsonObject data, bool artist) => "<script>require(['jquery','modules/" + (artist ? "artist_profile" : "song_page") + "']);var config = " + data.ToJsonString() + ";</script>";
    internal static MediaHttp Http(List<Uri> calls, Action<string, JsonObject>? mutate = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null, MediaHttpOptions? options = null, string? mode = null) => new(new Handler(request =>
    {
        var url = request.RequestUri!; calls.Add(url); Assert(!request.Headers.Contains("Authorization") && !request.Headers.Contains("Cookie"), "ReverbNation leaked credentials.");
        if (url.Host == "dnma3z6c8ojev.cloudfront.net") return media?.Invoke(request) ?? throw new InvalidOperationException("Core check fetched audio.");
        JsonObject node; string kind;
        if (url.AbsolutePath.StartsWith("/widget_code", StringComparison.Ordinal)) { kind = "widget"; node = new() { ["PLAYLIST"] = new JsonArray(new JsonObject { ["id"] = 42, ["artist"] = new JsonObject { ["id"] = 9, ["homepage"] = "https://legacy.reverbnation.com/artist" } }) }; mutate?.Invoke(kind, node); return new(HttpStatusCode.OK) { Content = new StringContent("<script>var configuration = " + node.ToJsonString() + ";</script>"), RequestMessage = request }; }
        if (url.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal)) { node = Inventory(true); if (mode == "duplicate") node["results"]![0]!["id"] = 42; if (mode == "truncated") node["results"] = new JsonArray(); mutate?.Invoke("api", node); return new(HttpStatusCode.OK) { Content = new StringContent(node.ToJsonString()), RequestMessage = request }; }
        var artist = url.AbsolutePath == "/artist"; kind = artist ? "artist" : "song"; node = artist ? ArtistBootstrap() : SongBootstrap(url.AbsolutePath.EndsWith("44", StringComparison.Ordinal) ? 44 : 42); mutate?.Invoke(kind, node); return new(HttpStatusCode.OK) { Content = new StringContent(Page(node, artist)), RequestMessage = request };
    }), options);
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
    private sealed class Files : IDisposable { internal string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "test-data", "reverbnation-" + Guid.NewGuid().ToString("N")); internal Files() => Directory.CreateDirectory(Path); public void Dispose() => Directory.Delete(Path, true); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
