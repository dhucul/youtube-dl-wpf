using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.Audiomack;

internal static class AudiomackChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Audiomack native OAuth signs encoded anonymous parameters within music scope", Signing),
        ("Audiomack literal public configuration rejects ambiguous computed and foreign endpoints", Configuration),
        ("Audiomack public tracks retain numeric identities metadata and offered sources", Facts),
        ("Audiomack host embed and ordinary tracking aliases retain canonical identity", Aliases),
        ("Audiomack private selected manually paged and unsupported routes cannot fall through", Boundaries),
        ("Audiomack wrong owner recording album and source identities fail", Ownership),
        ("Audiomack explicit private premium regional incomplete and preview states refuse", Restrictions),
        ("Audiomack signed media expiry and literal query bindings stay exact", Resources),
        ("Audiomack complete ordered albums preserve selected source identities", Albums),
        ("Audiomack duplicate oversized and changed album inventories fail", Incomplete),
        ("Audiomack unavailable album slots retain original source positions", Positions),
        ("Audiomack inspected album selections refresh only chosen fresh playback", Snapshots),
        ("Audiomack numeric archives skip playback and completion effects", Archives),
        ("Audiomack public credential isolation cancellation and denial remain explicit", Transport)
    ];
    private static Task Signing()
    {
        var endpoint = new Uri("https://api.audiomack.com/v1/music/play/42"); var client = new AudiomackPublicClient("public-key", "public-secret");
        var a = AudiomackOAuth.Sign(endpoint, new Dictionary<string, string> { ["hq"] = "true", ["session"] = "space + slash/" }, client, 1700000000, "nonce");
        var b = AudiomackOAuth.Sign(endpoint, new Dictionary<string, string> { ["session"] = "space + slash/", ["hq"] = "true" }, client, 1700000000, "nonce");
        Assert(a == b && a.Query.Contains("space%20%2B%20slash%2F", StringComparison.Ordinal) && a.Query.Contains("oauth_signature=", StringComparison.Ordinal), "OAuth normalization or encoding changed.");
        Assert(a.Query.EndsWith("oauth_signature=XCMecPKrqz7nHU0xcRxQb7VM8cw%3D", StringComparison.Ordinal), "OAuth encoded parameters did not match the independently calculated HMAC-SHA1 vector.");
        foreach (var url in new[] { "https://other.example/v1/music/play/42", "https://api.audiomack.com/v1/user/favorites", "https://api.audiomack.com/v1/music/42/delete", "http://api.audiomack.com/v1/music/play/42" })
            ExpectSync<ArgumentException>(() => AudiomackOAuth.Sign(new(url), new Dictionary<string, string>(), client));
        ExpectSync<ArgumentException>(() => AudiomackOAuth.Sign(endpoint, new Dictionary<string, string> { ["key"] = "private" }, client)); return Task.CompletedTask;
    }
    private static Task Configuration()
    {
        var config = AudiomackClientConfiguration.Parse(Client); Assert(config.Key == "public-key" && config.Secret == "public-secret", "Public literal configuration changed.");
        foreach (var invalid in new[] { Client.Replace("api.audiomack.com", "other.example"), Client + ",API_CONSUMER_KEY:\"other\"", Client.Replace("\"public-secret\"", "computed()") })
            ExpectSync<InvalidDataException>(() => AudiomackClientConfiguration.Parse(invalid)); return Task.CompletedTask;
    }
    private static async Task Facts()
    {
        var calls = new List<Uri>(); using var http = Http(calls); var page = await new AudiomackProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
        Assert(page.Id == "42" && page.ArchiveIdentity == new DownloadArchiveIdentity("audiomack", "42") && page.Title == "Track" && page.Uploader == "Artist" && page.Media.Count == 2 &&
            page.Media.Single(media => media.Id == "hq").Extension == "m4a" && page.Media.Single(media => media.Id == "standard").Extension == "mp3" && page.UploadDate == new DateOnly(2020, 1, 1) && page.ViewCount == 12 && calls.Count == 5,
            "Audiomack facts or format inventories changed.");
        Assert(MediaProviderRegistry.CreateDefault().Resolve(Request(Path.GetTempPath()).Url).Info.Id == "audiomack", "Audiomack was not registered.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "http://www.audiomack.com/artist/song/track", "https://audiomack.com/embed/artist/song/track?autoplay=1&utm_source=test" })
        { using var http = Http([]); var page = await new AudiomackProvider().ExtractAsync(Request(Path.GetTempPath()) with { Url = new(url) }, http, default); Assert(page.Id == "42", "Audiomack alias changed recording identity."); }
    }
    private static Task Boundaries()
    {
        foreach (var url in new[] { "https://audiomack.com/artist/song/track?key=private", "https://audiomack.com/artist/song/track?track=2", "https://audiomack.com/artist/song/track#t=1",
            "https://user:secret@audiomack.com/artist/song/track", "https://audiomack.com:8443/artist/song/track", "https://audiomack.com/artist/playlist/list", "https://audiomack.com.evil.example/artist/song/track" })
            Assert(!new AudiomackProvider().CanHandle(new(url)), "Unsupported Audiomack route accepted.");
        Assert(!new GenericPageProvider().CanHandle(new("https://audiomack.com/private/42")), "Audiomack private route reached generic extraction."); return Task.CompletedTask;
    }
    private static async Task Ownership()
    {
        foreach (var mutate in new Action<string, JsonObject>[] {
            (kind, node) => { if (kind == "song") node["url_slug"] = "other"; }, (kind, node) => { if (kind == "song") node["uploader"]!["url_slug"] = "other"; },
            (kind, node) => { if (kind == "song") node["links"]!["self"] = "https://audiomack.com/other/song/track"; },
            (kind, node) => { if (kind == "song") node["album_details"]!["uploader_url_slug"] = "other"; },
            (kind, node) => { if (kind == "play") node["signedUrl"] = Source("other", "album", "track", true); } })
        { using var http = Http([], mutate); await Expect<InvalidDataException>(() => new AudiomackProvider().ExtractAsync(Request(Path.GetTempPath()), http, default)); }
    }
    private static async Task Restrictions()
    {
        foreach (var change in new Action<JsonObject>[] { node => node["private"] = "yes", node => node["premium_user_only"] = "yes", node => node["geo_restricted"] = true,
            node => node["status"] = "processing", node => node["live"] = false, node => node["preview"] = "short", node => node["errors"] = new JsonArray("unavailable") })
        { using var http = Http([], (kind, node) => { if (kind == "song") change(node); }); await Expect<NotSupportedException>(() => new AudiomackProvider().ExtractAsync(Request(Path.GetTempPath()), http, default)); }
    }
    private static async Task Resources()
    {
        foreach (var source in new[] { Source("artist", "album", "other", true), Source("artist", "album", "track-more", true), Source("artist", "album", "track", true).Replace("Expires=4102444800", "Expires=1"),
            Source("artist", "album", "track", true).Replace("Signature=literal", "Signature=first&Signature=literal") })
        { using var http = Http([], (kind, node) => { if (kind == "play") node["signedUrl"] = source; }); await Expect<InvalidDataException>(() => new AudiomackProvider().ExtractAsync(Request(Path.GetTempPath()), http, default)); }
        using var foreign = Http([], (kind, node) => { if (kind == "play") node["signedUrl"] = Source("artist", "album", "track", true).Replace("music.audiomack.com", "other.example"); });
        await Expect<NotSupportedException>(() => new AudiomackProvider().ExtractAsync(Request(Path.GetTempPath()), foreign, default));
    }
    private static async Task Albums()
    {
        var calls = new List<Uri>(); using var http = Http(calls); var page = await new AudiomackProvider().ExtractAsync(Request(Path.GetTempPath(), true), http, default);
        Assert(page.IsPlaylist && page.PlaylistId == "7" && page.Entries.Select(entry => entry.Id).SequenceEqual(new[] { "42", "43" }) && page.Entries[1].Position == 2 &&
            calls.All(uri => !uri.AbsolutePath.Contains("/play/", StringComparison.Ordinal)), "Audiomack inspection fetched playback or changed publisher order.");
    }
    private static async Task Incomplete()
    {
        foreach (var change in new Action<JsonObject>[] { node => node["tracks"]![1]!["song_id"] = 42, node => node["tracks"]![1]!["uploader_url_slug"] = "other", node => node.Remove("tracks") })
        { using var http = Http([], (kind, node) => { if (kind == "album") change(node); }); await Expect<InvalidDataException>(() => new AudiomackProvider().ExtractAsync(Request(Path.GetTempPath(), true), http, default)); }
        using var limit = Http([]); await Expect<NotSupportedException>(() => new AudiomackProvider().ExtractAsync(Request(Path.GetTempPath(), true) with { MaximumPlaylistEntries = 1 }, limit, default));
        var albums = 0; using var changed = Http([], (kind, node) => { if (kind == "album" && ++albums == 2) node["updated"] = "1577836801"; });
        await Expect<InvalidDataException>(() => new AudiomackProvider().ExtractAsync(Request(Path.GetTempPath(), true), changed, default));
    }
    private static async Task Positions()
    {
        using var http = Http([], (kind, node) => { if (kind == "album") node["tracks"]![0]!["premium_user_only"] = "yes"; });
        var page = await new AudiomackProvider().ExtractAsync(Request(Path.GetTempPath(), true), http, default);
        Assert(!page.Entries[0].Available && page.Entries[0].Url is null && page.Entries[1].Position == 2, "Unavailable album slots moved.");
    }
    private static async Task Snapshots()
    {
        using var files = new Files(); var calls = new List<Uri>(); var engine = new NativeEngine([new AudiomackProvider()], _ => Http(calls));
        var request = Request(files.Path, true) with { PlaylistItems = "2", Simulate = true }; var inspected = await engine.InspectAsync(request, default); calls.Clear();
        var result = await engine.DownloadAsync(request with { PlaylistSnapshot = inspected.PlaylistSnapshot }, null, null, default);
        Assert(result.Succeeded && calls.Any(uri => uri.AbsolutePath.EndsWith("/second", StringComparison.Ordinal)) && calls.Where(uri => uri.AbsolutePath.Contains("/play/", StringComparison.Ordinal)).All(uri => uri.AbsolutePath.EndsWith("/43", StringComparison.Ordinal)), "Snapshot refreshed unselected playback.");
    }
    private static async Task Archives()
    {
        using var files = new Files(); var archive = Path.Combine(files.Path, "archive.txt"); await File.WriteAllTextAsync(archive, "audiomack 42\n"); var calls = new List<Uri>();
        var engine = new NativeEngine([new AudiomackProvider()], _ => Http(calls));
        var result = await engine.DownloadAsync(Request(files.Path) with { DownloadArchiveFile = archive }, null, null, default, _ => throw new InvalidOperationException("Archived track emitted completion."));
        Assert(result.Skipped && calls.All(uri => !uri.AbsolutePath.Contains("/play/", StringComparison.Ordinal)), "Numeric archive repeated playback discovery.");
    }
    private static async Task Transport()
    {
        using var http = Http([], options: new MediaHttpOptions { Headers = { ["Authorization"] = "private fixture", ["Cookie"] = "private=fixture" } });
        var page = await new AudiomackProvider().ExtractAsync(Request(Path.GetTempPath()), http, default); Assert(page.Id == "42", "Anonymous metadata failed.");
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Expect<OperationCanceledException>(() => new AudiomackProvider().ExtractAsync(Request(Path.GetTempPath()), http, cancel.Token));
        using var denied = new MediaHttp(new Handler(_ => new(HttpStatusCode.Forbidden) { Content = new StringContent("denied") })); await Expect<HttpRequestException>(() => new AudiomackProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
    }
    internal const string Client = "let o={API_PUBLIC_API_URL:\"https://api.audiomack.com/v1\",API_CONSUMER_KEY:\"public-key\",API_CONSUMER_SECRET:\"public-secret\"}";
    internal static string Source(string artist, string album, string slug, bool high) => "https://music.audiomack.com/albums/" + artist + "/" + album + "/" + (high ? "hq/" : "") + slug + "-__123." + (high ? "m4a" : "mp3") + "?Expires=4102444800&Signature=literal%2Bquery&Key-Pair-Id=public";
    internal static NativeDownloadRequest Request(string output, bool album = false) => new() { Url = new("https://audiomack.com/artist/" + (album ? "album/album" : "song/track")), OutputDirectory = output, OutputTemplate = "%(id)s.%(ext)s" };
    internal static JsonObject Track(string slug = "track", string id = "42") => new() { ["id"] = int.Parse(id), ["type"] = "song", ["title"] = "Track", ["url_slug"] = slug,
        ["uploader"] = new JsonObject { ["id"] = "9", ["url_slug"] = "artist", ["name"] = "Artist", ["status"] = "active" }, ["status"] = "complete", ["live"] = true, ["private"] = "no",
        ["premium_user_only"] = "no", ["preview"] = "", ["geo_lock"] = "", ["geo_premium"] = "", ["geo_restricted"] = false, ["duration"] = "2", ["uploaded"] = "1577836800", ["released"] = "1577836800",
        ["explicit"] = "no", ["stats"] = new JsonObject { ["plays-raw"] = 12 }, ["image"] = "https://i.audiomack.com/artist/cover.webp",
        ["album_details"] = new JsonObject { ["id"] = "7", ["title"] = "Album", ["url_slug"] = "album", ["uploader_url_slug"] = "artist" }, ["links"] = new JsonObject { ["self"] = "https://audiomack.com/artist/song/" + slug }, ["errors"] = new JsonArray() };
    internal static JsonObject Album()
    {
        var root = Track(); root["id"] = 7; root["type"] = "album"; root["title"] = "Album"; root["url_slug"] = "album"; root["duration"] = ""; root["updated"] = "1577836800"; root.Remove("album_details"); root["links"]!["self"] = "https://audiomack.com/artist/album/album";
        root["tracks"] = new JsonArray(new JsonObject { ["song_id"] = 42, ["title"] = "Track", ["url_slug"] = "track", ["uploader_url_slug"] = "artist", ["status"] = "complete", ["duration"] = "2", ["premium_user_only"] = "no" },
            new JsonObject { ["song_id"] = 43, ["title"] = "Second", ["url_slug"] = "second", ["uploader_url_slug"] = "artist", ["status"] = "complete", ["duration"] = "2", ["premium_user_only"] = "no" }); return root;
    }
    internal static MediaHttp Http(List<Uri> calls, Action<string, JsonObject>? mutate = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null, MediaHttpOptions? options = null)
        => new(new Handler(request =>
        {
            var uri = request.RequestUri!; calls.Add(uri); Assert(!request.Headers.Contains("Authorization") && !request.Headers.Contains("Cookie"), "Audiomack public source leaked common credentials.");
            if (uri.Host == "music.audiomack.com") return media?.Invoke(request) ?? throw new InvalidOperationException("Core check fetched media.");
            if (uri.Host == "audiomack.com") return new(HttpStatusCode.OK) { Content = new StringContent(uri.AbsolutePath.StartsWith("/_next/", StringComparison.Ordinal) ? Client : "<script src='/_next/static/chunks/7919-fixture.js'></script>"), RequestMessage = request };
            JsonObject node; string kind;
            if (uri.AbsolutePath.Contains("/play/", StringComparison.Ordinal)) { kind = "play"; var id = uri.AbsolutePath.EndsWith("/43", StringComparison.Ordinal) ? "43" : "42"; var high = uri.Query.Contains("hq=true", StringComparison.Ordinal); node = new() { ["signedUrl"] = Source("artist", "album", id == "43" ? "second" : "track", high), ["playUuid"] = "public-session" }; }
            else if (uri.AbsolutePath.Contains("/album/", StringComparison.Ordinal)) { kind = "album"; node = Album(); }
            else { kind = "song"; var second = uri.AbsolutePath.EndsWith("/second", StringComparison.Ordinal); node = Track(second ? "second" : "track", second ? "43" : "42"); }
            mutate?.Invoke(kind, node); var body = kind == "play" ? node.ToJsonString() : new JsonObject { ["results"] = node }.ToJsonString();
            return new(HttpStatusCode.OK) { Content = new StringContent(body), RequestMessage = request };
        }), options);
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
    private sealed class Files : IDisposable { internal string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "test-data", "audiomack-" + Guid.NewGuid().ToString("N")); internal Files() => Directory.CreateDirectory(Path); public void Dispose() => Directory.Delete(Path, true); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static void ExpectSync<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
