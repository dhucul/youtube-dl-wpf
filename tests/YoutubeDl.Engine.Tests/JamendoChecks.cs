using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.Jamendo;

internal static class JamendoChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Jamendo public request checksums bind the endpoint before query data", Signature),
        ("Jamendo public tracks preserve supplied formats metadata and archive identity", Facts),
        ("Jamendo language slug and host aliases retain immutable track identity", Aliases),
        ("Jamendo unsupported routes and access parameters cannot reach generic extraction", Boundaries),
        ("Jamendo wrong track artist album and source ownership fail explicitly", Ownership),
        ("Jamendo unavailable denied preview and upcoming tracks cannot expose downloads", Restrictions),
        ("Jamendo single releases work without an album or invented parent metadata", Singles),
        ("Jamendo offered resource identities encodings and literal queries remain bound", Resources),
        ("Jamendo complete albums retain publisher order despite response ordering", Albums),
        ("Jamendo missing duplicate oversized and changing album inventories fail", Incomplete),
        ("Jamendo unavailable album tracks retain their original source positions", Positions),
        ("Jamendo inspected selections refresh only selected immutable track descriptors", Snapshots),
        ("Jamendo recorded direct and selected tracks skip metadata and completion effects", Archives),
        ("Jamendo public requests isolate credentials honor cancellation and refuse denial", Transport)
    ];

    private static Task Signature()
    {
        const string expected = "$4dbe872aa74a42929bf97e354f70284f49011406*0.7315240868012345~";
        Assert(JamendoRequestIdentity.Create(new("https://www.jamendo.com/api/tracks?id%5B%5D=42"), "0.7315240868012345") == expected &&
            JamendoRequestIdentity.Create(new("https://www.jamendo.com/api/tracks?id%5B%5D=43"), "0.7315240868012345") == expected, "Public checksum query timing changed.");
        foreach (var url in new[] { "https://other.example/api/tracks", "https://www.jamendo.com/api/oauth", "http://www.jamendo.com/api/tracks" })
            ExpectSync<ArgumentException>(() => JamendoRequestIdentity.Create(new(url), "0.5"));
        return Task.CompletedTask;
    }

    private static async Task Facts()
    {
        var calls = new List<Uri>(); var page = await Extract(calls);
        Assert(page.Id == "42" && page.ArchiveIdentity == new DownloadArchiveIdentity("jamendo", "42") && page.Uploader == "Artist" &&
            page.Media.Count == 6 && page.Media.All(media => media.HasAudio == true && media.HasVideo == false && media.VerificationDurationSeconds == 2) &&
            page.Media.Single(media => media.Id == "stream-mp32").Url.AbsoluteUri == Stream("42", "mp32") && page.Thumbnails.Count == 2 && page.Thumbnails.Max(image => image.Width) == 600 &&
            page.UploadDate == new DateOnly(2020, 1, 1) && page.ViewCount == 12 && (int)page.MetadataFields["track_number"]! == 1 && calls.Count == 3,
            "Public Jamendo descriptors or facts changed.");
        Assert(MediaProviderRegistry.CreateDefault().Resolve(new("https://www.jamendo.com/track/42")).Info.Id == "jamendo", "Jamendo was not registered.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "http://jamendo.com/track/42", "https://www.jamendo.com/en/track/42/fixture", "https://jamendo.com/fr/track/42/fixture?language=fr&utm_source=test" })
        {
            var page = await Extract([], request: Request(Path.GetTempPath()) with { Url = new(url), MaximumPlaylistEntries = 1 });
            Assert(page.Id == "42" && page.SourcePageUrl == new Uri("https://www.jamendo.com/track/42"), "Jamendo alias changed its media identity.");
        }
    }
    private static Task Boundaries()
    {
        foreach (var url in new[] { "https://www.jamendo.com/artist/9", "https://www.jamendo.com/track/0", "https://www.jamendo.com/track/42?token=secret", "https://www.jamendo.com/track/42#t=1",
            "https://user:secret@www.jamendo.com/track/42", "https://www.jamendo.com:8443/track/42", "https://www.jamendo.com/track/42/a%2Fb", "https://jamendo.com.evil.example/track/42" })
            Assert(!JamendoAddress.Matches(new(url)), "Unsupported Jamendo address accepted.");
        Assert(!new GenericPageProvider().CanHandle(new("https://www.jamendo.com/private/42")), "Jamendo's unsupported route reached generic extraction.");
        return Task.CompletedTask;
    }
    private static async Task Ownership()
    {
        foreach (var change in new Action<string, string, JsonObject>[] {
            (kind, id, node) => { if (kind == "tracks") node["id"] = 44; },
            (kind, id, node) => { if (kind == "artists") node["id"] = 10; },
            (kind, id, node) => { if (kind == "albums") node["artistId"] = 10; },
            (kind, id, node) => { if (kind == "albums") node["tracks"] = new JsonArray(new JsonObject { ["id"] = 43, ["position"] = 1 }); },
            (kind, id, node) => { if (kind == "tracks") node["download"]!["mp3"] = Download("43", "mp32"); } })
            await Expect<InvalidDataException>(() => Extract([], change));
    }
    private static async Task Restrictions()
    {
        foreach (var change in new Action<JsonObject>[] {
            node => node["status"]!["available"] = false, node => node["isDownloadable"] = 0,
            node => node["isPreview"] = true, node => node["requiresLogin"] = true, node => node["dateReleased"] = 4070908800L })
            await Expect<NotSupportedException>(() => Extract([], (kind, id, node) => { if (kind == "tracks") change(node); }));
        await Expect<InvalidDataException>(() => Extract([], (kind, id, node) => { if (kind == "tracks") node.Remove("isDownloadable"); }));
    }
    private static async Task Singles()
    {
        var calls = new List<Uri>(); var page = await Extract(calls, (kind, id, node) => { if (kind == "tracks") { node["albumId"] = null; node["cover"] = new JsonObject(); } });
        Assert(page.Id == "42" && page.MetadataFields["album_id"] is null && calls.All(uri => uri.AbsolutePath != "/api/albums"), "Standalone tracks acquired an invented album.");
    }
    private static async Task Resources()
    {
        foreach (var source in new[] { "https://prod-1.storage.jamendo.com/?trackid=42&format=mp33", "https://prod-1.storage.jamendo.com/?trackid=42&trackid=43&format=mp31",
            "http://prod-1.storage.jamendo.com/?trackid=42&format=mp31" })
            await Expect<InvalidDataException>(() => Extract([], (kind, id, node) => { if (kind == "tracks") node["stream"]!["mp3"] = source; }));
        await Expect<NotSupportedException>(() => Extract([], (kind, id, node) => { if (kind == "tracks") node["stream"]!["mp3"] = "https://unbound.example/42.mp3"; }));
        await Expect<InvalidDataException>(() => Extract([], (kind, id, node) => { if (kind == "tracks") node["cover"]!["big"]!["size600"] = "https://usercontent.jamendo.com?type=album&id=8&width=600"; }));
    }
    private static async Task Albums()
    {
        var calls = new List<Uri>(); var page = await Extract(calls, request: Request(Path.GetTempPath(), true));
        Assert(page.IsPlaylist && page.PlaylistId == "7" && page.Entries.Select(entry => entry.Id).SequenceEqual(new[] { "42", "43" }) &&
            page.Entries.Select(entry => entry.Position).SequenceEqual(new[] { 1, 2 }) && page.Entries.All(entry => entry.ResolvedMedia is null && entry.ExpectedArchiveIdentity == new DownloadArchiveIdentity("jamendo", entry.Id!)) &&
            calls.Count == 4 && calls.All(uri => uri.Host == "www.jamendo.com"), "Complete Jamendo album order or selected-only media scope changed.");
    }
    private static async Task Incomplete()
    {
        foreach (var change in new Action<JsonObject>[] {
            node => node["tracks"]![0]!["position"] = 1, node => node["tracks"]![0]!["id"] = 42,
            node => node.Remove("tracks") })
            await Expect<InvalidDataException>(() => Extract([], (kind, id, node) => { if (kind == "albums") change(node); }, Request(Path.GetTempPath(), true)));
        await Expect<NotSupportedException>(() => Extract([], request: Request(Path.GetTempPath(), true) with { MaximumPlaylistEntries = 1 }));
        var albums = 0;
        await Expect<InvalidDataException>(() => Extract([], (kind, id, node) => { if (kind == "albums" && ++albums == 2) node["dateUpdated"] = 1577836801; }, Request(Path.GetTempPath(), true)));
        await Expect<InvalidDataException>(() => Extract([], (kind, id, node) => { if (kind == "tracks" && id == "43") node["albumId"] = 8; }, Request(Path.GetTempPath(), true)));
    }
    private static async Task Positions()
    {
        var page = await Extract([], (kind, id, node) => { if (kind == "tracks" && id == "42") node["isDownloadable"] = 0; }, Request(Path.GetTempPath(), true));
        Assert(!page.Entries[0].Available && page.Entries[0].Url is null && page.Entries[1].Available && page.Entries[1].Position == 2, "Unavailable Jamendo tracks shifted source positions.");
    }
    private static async Task Snapshots()
    {
        using var files = new Files(); var calls = new List<Uri>(); var engine = new NativeEngine([new JamendoProvider()], _ => Http(calls));
        var request = Request(files.Path, true) with { PlaylistItems = "2", Simulate = true }; var inspected = await engine.InspectAsync(request, default); calls.Clear();
        var result = await engine.DownloadAsync(request with { PlaylistSnapshot = inspected.PlaylistSnapshot }, null, null, default);
        Assert(result.Succeeded && calls.Count(uri => uri.AbsolutePath == "/api/tracks") == 1 && calls.Where(uri => uri.AbsolutePath == "/api/tracks").All(uri => Ids(uri).SequenceEqual(new[] { "43" })),
            "Inspected Jamendo selection refreshed unselected descriptors.");
    }
    private static async Task Archives()
    {
        using var files = new Files(); var archive = Path.Combine(files.Path, "archive.txt"); await File.WriteAllTextAsync(archive, "jamendo 42\n");
        var engine = new NativeEngine([new JamendoProvider()], _ => throw new InvalidOperationException("Archived track contacted public metadata."));
        var result = await engine.DownloadAsync(Request(files.Path) with { DownloadArchiveFile = archive }, null, null, default, _ => throw new InvalidOperationException("Archived track emitted completion."));
        Assert(result.Skipped && result.Succeeded, "Jamendo archive identity did not skip.");
    }
    private static async Task Transport()
    {
        using var http = Http([], options: new MediaHttpOptions { Headers = { ["Authorization"] = "private fixture value", ["Cookie"] = "private=fixture" } });
        var page = await new JamendoProvider().ExtractAsync(Request(Path.GetTempPath()), http, default); Assert(page.Id == "42", "Anonymous public metadata failed.");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Expect<OperationCanceledException>(() => new JamendoProvider().ExtractAsync(Request(Path.GetTempPath()), http, cancel.Token));
        using var denied = new MediaHttp(new Handler(_ => new(HttpStatusCode.Forbidden) { Content = new StringContent("denied") }));
        await Expect<HttpRequestException>(() => new JamendoProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
    }

    internal static NativeDownloadRequest Request(string output, bool album = false) => new() { Url = new("https://www.jamendo.com/" + (album ? "album/7" : "track/42")), OutputDirectory = output, OutputTemplate = "%(id)s.%(ext)s" };
    private static async Task<PageMetadata> Extract(List<Uri> calls, Action<string, string, JsonObject>? mutate = null, NativeDownloadRequest? request = null)
    { using var http = Http(calls, mutate); return await new JamendoProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static MediaHttp Http(List<Uri> calls, Action<string, string, JsonObject>? mutate = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null, MediaHttpOptions? options = null)
    {
        return new MediaHttp(new Handler(request =>
        {
            var uri = request.RequestUri!; calls.Add(uri);
            Assert(!request.Headers.Contains("Authorization") && !request.Headers.Contains("Cookie"), "Jamendo public metadata or media leaked common credentials.");
            if (!uri.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal)) return media?.Invoke(request) ?? throw new InvalidOperationException("Core Jamendo check fetched media.");
            var header = request.Headers.GetValues("X-Jam-Call").Single(); var nonce = header.Split('*')[1].TrimEnd('~');
            Assert(header == JamendoRequestIdentity.Create(uri, nonce) && request.Headers.GetValues("X-Requested-With").Single() == "XMLHttpRequest", "Public Jamendo request identity was omitted.");
            var kind = uri.AbsolutePath[5..]; var data = new JsonArray();
            foreach (var id in Ids(uri).Reverse())
            {
                var node = kind switch { "tracks" => TrackData(id), "albums" => AlbumData(), "artists" => ArtistData(), _ => throw new InvalidOperationException("Unexpected metadata endpoint.") };
                mutate?.Invoke(kind, id, node); data.Add(node);
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(data.ToJsonString()), RequestMessage = request };
        }), options);
    }
    private static string[] Ids(Uri uri) => uri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).Where(pair => Uri.UnescapeDataString(pair[0]) == "id[]").Select(pair => pair[1]).ToArray();
    internal static string Download(string id, string format) => "https://prod-1.storage.jamendo.com/download/track/" + id + "/" + format + "/";
    internal static string Stream(string id, string format) => "https://prod-1.storage.jamendo.com/?trackid=" + id + "&format=" + format + "&from=literal%2Btoken%2Fvalue";
    internal static JsonObject TrackData(string id = "42") => new() {
        ["id"] = int.Parse(id), ["albumId"] = 7, ["artistId"] = 9, ["name"] = "Track " + id, ["duration"] = 2, ["dateReleased"] = 1577836800,
        ["status"] = new JsonObject { ["available"] = true }, ["isDownloadable"] = 1, ["licenseCCUrl"] = "https://creativecommons.org/licenses/by/4.0/",
        ["stats"] = new JsonObject { ["listenedAll"] = 12, ["favorited"] = 3, ["downloadedAll"] = 4 },
        ["cover"] = new JsonObject { ["big"] = new JsonObject { ["size300"] = "https://usercontent.jamendo.com?type=album&id=7&width=300&trackid=" + id, ["size600"] = "https://usercontent.jamendo.com?type=album&id=7&width=600&trackid=" + id } },
        ["stream"] = new JsonObject { ["mp3"] = Stream(id, "mp31"), ["mp32"] = Stream(id, "mp32"), ["mp33"] = Stream(id, "mp33"), ["ogg"] = Stream(id, "ogg") },
        ["download"] = new JsonObject { ["mp3"] = Download(id, "mp32"), ["ogg"] = Download(id, "ogg") } };
    internal static JsonObject AlbumData() => new() { ["id"] = 7, ["name"] = "Album", ["artistId"] = 9, ["dateReleased"] = 1577836800, ["dateUpdated"] = 1577836800,
        ["status"] = new JsonObject { ["available"] = true }, ["moderation"] = "publishable", ["tracks"] = new JsonArray(new JsonObject { ["id"] = 43, ["position"] = 2 }, new JsonObject { ["id"] = 42, ["position"] = 1 }) };
    internal static JsonObject ArtistData() => new() { ["id"] = 9, ["name"] = "Artist", ["status"] = "visible" };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
    private sealed class Files : IDisposable { internal string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "test-data", "jamendo-" + Guid.NewGuid().ToString("N")); internal Files() => Directory.CreateDirectory(Path); public void Dispose() => Directory.Delete(Path, true); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static void ExpectSync<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
