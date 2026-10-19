using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.Bandlab;

internal static class BandlabChecks
{
    internal const string RevisionId = "11111111-1111-4111-8111-111111111111", PostId = "22222222-2222-4222-8222-222222222222", SongId = "33333333-3333-4333-8333-333333333333", OwnerId = "44444444-4444-4444-8444-444444444444", MixId = "55555555-5555-4555-8555-555555555555", ListId = "66666666-6666-4666-8666-666666666666";
    internal const string SecondRevision = "77777777-7777-4777-8777-777777777777", SecondPost = "88888888-8888-4888-8888-888888888888";
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("BandLab public revision and post aliases retain stable identities and offered audio", Facts),
        ("BandLab canonical songs bind owner slug and explicitly selected public revisions", Canonical),
        ("BandLab public embed aliases and ordinary tracking preserve recording identity", Aliases),
        ("BandLab private keys unknown queries routes fragments and invalid identities cannot fall through", Boundaries),
        ("BandLab public publication owner song revision and mix identity conflicts fail", Ownership),
        ("BandLab private exclusive nonrevision incomplete and preview descriptors refuse", Restrictions),
        ("BandLab supplied media paths remain bound to their exact mixdown identity", Resources),
        ("BandLab complete public playlists retain source order owner and expected revision archives", Collections),
        ("BandLab missing counted repeated changed and overlimit playlist inventories fail", Incomplete),
        ("BandLab unavailable playlist entries retain their original source positions", Positions),
        ("BandLab inspected selections refresh selected public metadata without unselected delivery", Snapshots),
        ("BandLab known revision archives avoid discovery delivery and completion effects", Archives),
        ("BandLab anonymous isolation HTTP denial and cancellation remain explicit", Transport)
    ];
    private static async Task Facts()
    {
        foreach (var kind in new[] { "revision", "post" })
        {
            var calls = new List<Uri>(); using var http = Http(calls); var page = await new BandlabProvider().ExtractAsync(Request(Path.GetTempPath(), kind), http, default);
            Assert(page.Id == RevisionId && page.ArchiveIdentity == new DownloadArchiveIdentity("bandlab", RevisionId) && page.Media.Count == 1 && page.Media[0].Extension == "m4a" && page.Title == "Track" && page.DurationSeconds == 2 && page.UploadDate == new DateOnly(2020, 1, 1) && page.ViewCount == 12 && page.AgeLimit == 0, "BandLab public facts changed.");
            Assert(calls.Where(url => url.AbsolutePath.Contains("/revisions/", StringComparison.Ordinal)).All(url => url.Query == "?edit=false"), "BandLab used the account-only editing endpoint.");
        }
        Assert(MediaProviderRegistry.CreateDefault().Resolve(Request(Path.GetTempPath()).Url).Info.Id == "bandlab", "BandLab was not registered.");
    }
    private static async Task Canonical()
    {
        foreach (var query in new[] { "", "?revId=" + RevisionId })
        { using var http = Http([]); var page = await new BandlabProvider().ExtractAsync(Request(Path.GetTempPath(), "song") with { Url = new("https://www.bandlab.com/artist/track" + query) }, http, default); Assert(page.Id == RevisionId, "Canonical song selected another revision."); }
        using var wrong = Http([], (kind, node) => { if (kind == "song") node["author"]!["username"] = "other"; }); await Expect<InvalidDataException>(() => new BandlabProvider().ExtractAsync(Request(Path.GetTempPath(), "song"), wrong, default));
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "http://bandlab.com/revisions/" + RevisionId, "https://www.bandlab.com/embed/?id=" + RevisionId + "&blur=false&utm_source=test", "https://bandlab.com/post/" + PostId })
        { using var http = Http([]); var page = await new BandlabProvider().ExtractAsync(Request(Path.GetTempPath()) with { Url = new(url) }, http, default); Assert(page.Id == RevisionId, "BandLab alias changed recording identity."); }
    }
    private static Task Boundaries()
    {
        foreach (var url in new[] { "https://www.bandlab.com/track/" + PostId + "?sharedKey=private", "https://www.bandlab.com/embed/?id=invalid", "https://www.bandlab.com/track/" + PostId + "#t=4", "https://www.bandlab.com:8443/track/" + PostId, "https://user:secret@bandlab.com/track/" + PostId, "https://www.bandlab.com/artist", "https://www.bandlab.com/artist/track?token=private" })
            Assert(!new BandlabProvider().CanHandle(new(url)), "Unsupported BandLab address accepted.");
        Assert(!new GenericPageProvider().CanHandle(new("https://www.bandlab.com/private/recording")), "BandLab private URL reached generic extraction."); return Task.CompletedTask;
    }
    private static async Task Ownership()
    {
        foreach (var change in new Action<string, JsonObject>[] { (kind, node) => { if (kind == "revision") node["id"] = SecondRevision; }, (kind, node) => { if (kind == "post") node["revision"]!["song"]!["id"] = OwnerId; },
            (kind, node) => { if (kind == "post") node["creator"]!["id"] = SongId; }, (kind, node) => { if (kind == "post") node["revision"]!["mixdown"]!["duration"] = 3; } })
        { using var http = Http([], change); await Expect<InvalidDataException>(() => new BandlabProvider().ExtractAsync(Request(Path.GetTempPath()), http, default)); }
    }
    private static async Task Restrictions()
    {
        foreach (var change in new Action<JsonObject>[] { node => node["state"] = "Private", node => node["isExclusive"] = true, node => node["type"] = "Track", node => node["revision"]!["isPublic"] = false, node => node["revision"]!["mixdown"]!["status"] = "Processing", node => node["revision"]!["isPreview"] = true })
        { using var http = Http([], (kind, node) => { if (kind == "post") change(node); }); await Expect<NotSupportedException>(() => new BandlabProvider().ExtractAsync(Request(Path.GetTempPath()), http, default)); }
    }
    private static async Task Resources()
    {
        foreach (var file in new[] { Source.Replace(MixId + ".m4a", SongId + ".m4a"), Source.Replace("static.bandlab.com", "other.example"), Source + "#fragment" })
        { using var http = Http([], (kind, node) => { if (kind == "revision") node["mixdown"]!["file"] = file; else if (kind == "post") node["revision"]!["mixdown"]!["file"] = file; }); await Expect<InvalidDataException>(() => new BandlabProvider().ExtractAsync(Request(Path.GetTempPath()), http, default)); }
    }
    private static async Task Collections()
    {
        var calls = new List<Uri>(); using var http = Http(calls); var page = await new BandlabProvider().ExtractAsync(Request(Path.GetTempPath(), "collection"), http, default);
        Assert(page.IsPlaylist && page.Entries.Count == 2 && page.Entries.Select(entry => entry.Position).SequenceEqual(new[] { 1, 2 }) && page.Entries[1].ExpectedArchiveIdentity == new DownloadArchiveIdentity("bandlab", SecondRevision) && calls.Count == 2, "BandLab complete playlist inventory changed.");
    }
    private static async Task Incomplete()
    {
        foreach (var change in new Action<JsonObject>[] { node => node["counters"]!["items"] = 3, node => node["posts"]![1]!["id"] = PostId, node => node["creator"]!["username"] = "other", node => node.Remove("posts") })
        { using var http = Http([], (kind, node) => { if (kind == "collection") change(node); }); await Expect<InvalidDataException>(() => new BandlabProvider().ExtractAsync(Request(Path.GetTempPath(), "collection"), http, default)); }
        using var limited = Http([]); await Expect<NotSupportedException>(() => new BandlabProvider().ExtractAsync(Request(Path.GetTempPath(), "collection") with { MaximumPlaylistEntries = 1 }, limited, default));
        var count = 0; using var changed = Http([], (kind, node) => { if (kind == "collection" && ++count == 2) node["posts"]![0]!["revision"]!["mixdown"]!["id"] = SongId; }); await Expect<InvalidDataException>(() => new BandlabProvider().ExtractAsync(Request(Path.GetTempPath(), "collection"), changed, default));
    }
    private static async Task Positions()
    {
        using var http = Http([], (kind, node) => { if (kind == "collection") node["posts"]![0]!["state"] = "Private"; }); var page = await new BandlabProvider().ExtractAsync(Request(Path.GetTempPath(), "collection"), http, default);
        Assert(!page.Entries[0].Available && page.Entries[0].Url is null && page.Entries[1].Position == 2, "Unavailable BandLab source position moved.");
    }
    private static async Task Snapshots()
    {
        using var files = new Files(); var calls = new List<Uri>(); var engine = new NativeEngine([new BandlabProvider()], _ => Http(calls)); var request = Request(files.Path, "collection") with { PlaylistItems = "2", Simulate = true };
        var info = await engine.InspectAsync(request, default); calls.Clear(); var result = await engine.DownloadAsync(request with { PlaylistSnapshot = info.PlaylistSnapshot }, null, null, default);
        Assert(result.Succeeded && calls.All(url => !url.AbsolutePath.EndsWith(PostId, StringComparison.Ordinal)) && calls.Any(url => url.AbsolutePath.EndsWith(SecondPost, StringComparison.Ordinal)), "Inspected BandLab selection fetched an unselected post.");
    }
    private static async Task Archives()
    {
        using var files = new Files(); var archive = Path.Combine(files.Path, "archive.txt"); await File.WriteAllTextAsync(archive, "bandlab " + RevisionId + "\n"); var engine = new NativeEngine([new BandlabProvider()], _ => throw new InvalidOperationException("Known archived BandLab revision reached HTTP."));
        var result = await engine.DownloadAsync(Request(files.Path) with { DownloadArchiveFile = archive }, null, null, default, _ => throw new InvalidOperationException("Archived BandLab emitted completion.")); Assert(result.Skipped, "Known BandLab archive did not skip.");
    }
    private static async Task Transport()
    {
        using var http = Http([], options: new MediaHttpOptions { Headers = { ["Authorization"] = "fixture private", ["Cookie"] = "private=fixture" } }); var page = await new BandlabProvider().ExtractAsync(Request(Path.GetTempPath()), http, default); Assert(page.Id == RevisionId, "Anonymous BandLab metadata failed.");
        using var stop = new CancellationTokenSource(); stop.Cancel(); await Expect<OperationCanceledException>(() => new BandlabProvider().ExtractAsync(Request(Path.GetTempPath()), http, stop.Token));
        using var denied = new MediaHttp(new Handler(_ => new(HttpStatusCode.Forbidden))); await Expect<HttpRequestException>(() => new BandlabProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
    }
    internal const string Source = "https://static.bandlab.com/revisions-formatted/" + MixId + "/" + MixId + ".m4a";
    internal static NativeDownloadRequest Request(string output, string kind = "revision") => new() { Url = new(kind == "collection" ? "https://www.bandlab.com/artist/collections/" + ListId : kind == "song" ? "https://www.bandlab.com/artist/track" : kind == "post" ? "https://www.bandlab.com/track/" + PostId : "https://www.bandlab.com/revisions/" + RevisionId), OutputDirectory = output, OutputTemplate = "%(id)s.%(ext)s", Retries = 0 };
    internal static JsonObject Song() => new() { ["id"] = SongId, ["slug"] = "track", ["name"] = "Track", ["isPublic"] = true, ["author"] = new JsonObject { ["id"] = OwnerId, ["type"] = "User", ["username"] = "artist", ["name"] = "Artist" } };
    internal static JsonObject Revision(bool second = false) => new() { ["id"] = second ? SecondRevision : RevisionId, ["postId"] = second ? SecondPost : PostId, ["isPublic"] = true, ["createdOn"] = "2020-01-01T00:00:00Z", ["description"] = "Description", ["creator"] = new JsonObject { ["id"] = OwnerId, ["username"] = "artist", ["name"] = "Artist" }, ["song"] = Song(), ["counters"] = new JsonObject { ["plays"] = 12, ["likes"] = 4 }, ["mixdown"] = new JsonObject { ["id"] = MixId, ["status"] = "Ready", ["duration"] = 2, ["file"] = Source } };
    internal static JsonObject Post(bool second = false) => new() { ["id"] = second ? SecondPost : PostId, ["type"] = "Revision", ["state"] = "Public", ["isExclusive"] = false, ["isExplicit"] = false, ["creator"] = new JsonObject { ["id"] = OwnerId, ["username"] = "artist", ["name"] = "Artist" }, ["revision"] = Revision(second) };
    internal static JsonObject Collection() => new() { ["id"] = ListId, ["name"] = "Playlist", ["type"] = "Playlist", ["isPublic"] = true, ["lastUpdatedOn"] = "2020-01-01T00:00:00Z", ["creator"] = new JsonObject { ["id"] = OwnerId, ["username"] = "artist", ["name"] = "Artist" }, ["counters"] = new JsonObject { ["items"] = 2 }, ["posts"] = new JsonArray(Post(), Post(true)) };
    internal static MediaHttp Http(List<Uri> calls, Action<string, JsonObject>? mutate = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null, MediaHttpOptions? options = null) => new(new Handler(request =>
    {
        var url = request.RequestUri!; calls.Add(url); Assert(!request.Headers.Contains("Authorization") && !request.Headers.Contains("Cookie"), "BandLab leaked caller credentials.");
        if (url.Host == "static.bandlab.com") return media?.Invoke(request) ?? throw new InvalidOperationException("Core BandLab check fetched media.");
        JsonObject node; string kind; if (url.AbsolutePath.Contains("/collections/", StringComparison.Ordinal)) { kind = "collection"; node = Collection(); }
        else if (url.AbsolutePath.Contains("/posts/", StringComparison.Ordinal)) { kind = "post"; node = Post(url.AbsolutePath.EndsWith(SecondPost, StringComparison.Ordinal)); }
        else if (url.AbsolutePath.Contains("/songs/", StringComparison.Ordinal)) { kind = "song"; node = Song(); node["revision"] = Revision(); }
        else { kind = "revision"; node = Revision(url.AbsolutePath.EndsWith(SecondRevision, StringComparison.Ordinal)); }
        mutate?.Invoke(kind, node); return new(HttpStatusCode.OK) { Content = new StringContent(node.ToJsonString()), RequestMessage = request };
    }), options);
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
    private sealed class Files : IDisposable { internal string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "test-data", "bandlab-" + Guid.NewGuid().ToString("N")); internal Files() => Directory.CreateDirectory(Path); public void Dispose() => Directory.Delete(Path, true); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
