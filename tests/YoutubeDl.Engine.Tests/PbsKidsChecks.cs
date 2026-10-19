using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.PbsKids;

internal static class PbsKidsChecks
{
    internal const string Id = "35256";
    internal const string OtherId = "52718";
    internal const string Cid = "c7a4e218-bf77-4775-b7bc-3d181c6557cb";
    internal const string OtherCid = "e811a6b0-bd3a-46a2-b10d-c65ddb728ad2";
    internal const string Signed = "?Key=A%2Bb&case=C%2Fd";
    internal static string Origin(string cid = Cid) => "https://kids.video.cdn.pbs.org/videos/fixture/" + cid + "/";
    internal static NativeDownloadRequest Request(string output) => new() { Url = new("https://pbskids.org/videos/watch/fixture-first/" + Id), OutputDirectory = output, OutputTemplate = "kids.%(ext)s" };
    internal static JsonNode Video(string id = Id, string cid = Cid, string slug = "fixture-first", double duration = 2) => new JsonObject { ["id"] = id, ["slug"] = slug, ["title"] = "Native PBS Kids", ["videoType"] = "clip", ["expiryDate"] = null,
        ["mediaManagerAsset"] = new JsonObject { ["id"] = cid, ["duration"] = duration, ["drm_enabled"] = false, ["description_short"] = "Native exact video", ["images"] = new JsonArray(new JsonObject { ["image"] = "https://image.pbs.org/poster.jpg", ["profile"] = "asset-kids-mezzanine-16x9" }),
            ["videos"] = new JsonArray(Encoding("mp4-16x9-96p", "11111111111111111111111111111111"), Encoding("hls-16x9-96p", "22222222222222222222222222222222")) }, ["properties"] = new JsonArray(new JsonObject { ["title"] = "Fixture series" }) };
    internal static JsonNode Encoding(string profile, string resolver) => new JsonObject { ["profile"] = profile, ["url"] = "https://urs.pbs.org/redirect/" + resolver + "/", ["widevine_license"] = null, ["fairplay_license"] = null, ["fairplay_certificate"] = null };
    internal static JsonNode Entry(string id = Id, string cid = Cid, string slug = "fixture-first", bool available = true) => new JsonObject { ["__typename"] = "PbsKidsVideo", ["id"] = id, ["guid"] = available ? cid : null,
        ["slug"] = slug, ["title"] = "Episode " + id, ["mediaManagerAsset"] = available ? new JsonObject { ["duration"] = 2, ["images"] = new JsonArray() } : null };
    internal static JsonNode Collection(params JsonNode[] entries) => new JsonObject { ["__typename"] = "PbsKidsVideoPlaylist", ["id"] = "1567131", ["slug"] = "fixture-collection", ["title"] = "Fixture collection",
        ["shortDescriptionPlainText"] = "Complete declared entries", ["entries"] = new JsonArray(entries.Length == 0 ? [Entry(), Entry(OtherId, OtherCid, "fixture-second")] : entries) };
    internal static string Html(JsonNode? record = null, bool collection = false, string? context = null)
    {
        record ??= collection ? Collection() : Video(); var props = new JsonObject { ["queriedContextSlug"] = context ?? "", [collection ? "queriedCollectionSlug" : "queriedVideoSlug"] = record["slug"]!.GetValue<string>(),
            [collection ? "collectionData" : "videoData"] = record.DeepClone() };
        var slugs = new JsonArray(); if (context is not null) slugs.Add(context); slugs.Add(record["slug"]!.GetValue<string>()); slugs.Add(record["id"]!.GetValue<string>());
        var root = new JsonObject { ["page"] = "/videos/" + (collection ? "playlist" : "watch") + "/[...slugsIds]", ["query"] = new JsonObject { ["slugsIds"] = slugs }, ["isFallback"] = false,
            ["props"] = new JsonObject { ["pageProps"] = props } };
        return "<html><script id='__NEXT_DATA__' type='application/json'>" + root.ToJsonString() + "</script></html>";
    }
    internal static IEnumerable<(string Name, Func<Task> Test)> All => [
        ("PBS Kids native current video binds the requested CMS and media asset identities", Facts),
        ("PBS Kids HTTP host and property-context aliases normalize to exact canonical pages", Aliases),
        ("PBS Kids address boundaries exclude homepages legacy GUID and unrelated video contexts", Boundaries),
        ("PBS Kids mismatched route query record and redirected identities fail before encodings", Identities),
        ("PBS Kids protected expired missing-state and unsupported profiles remain explicit", Restrictions),
        ("PBS Kids repeated declarations and malformed page data cannot become playback", Invalid),
        ("PBS Kids clear profile alternatives exclude protected variants without licensing requests", Profiles),
        ("PBS Kids collection entries preserve source positions and expected media archives", Collections),
        ("PBS Kids incomplete repeated mismatched or oversized collections fail explicitly", CollectionFailures),
        ("PBS Kids inspected collection snapshots retain linked source identities without parent refresh", Snapshots),
        ("PBS Kids selected collection archives skip child discovery and resolver requests", Archives),
        ("PBS Kids public metadata and encoding resolution isolate caller credentials", Anonymous),
        ("PBS Kids region denial stops without guessing alternate media or publishing artifacts", Denied),
        ("PBS Kids cancellation and HTTP errors prevent later discovery", Cancellation)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var page = await Extract(calls);
        Assert(page.Id == Id && page.ArchiveIdentity!.Id == Cid && page.DurationSeconds == 2 && page.Media.Count == 2 && page.Uploader == "Fixture series" && calls.Count == 3, "PBS Kids requested record/source identity changed.");
        Assert(page.Media.Single(media => media.Kind == MediaKind.Progressive).Height == 96 && page.Media.All(media => media.Url.Query == Signed) && page.Subtitles.Count == 0, "Source facts changed or captions were invented.");
    }
    private static async Task Aliases()
    {
        foreach (var host in new[] { "https://www.pbskids.org", "http://pbskids.org" })
        { var calls = new List<Uri>(); _ = await Extract(calls, request: Request(Path.GetTempPath()) with { Url = new(host + "/videos/watch/fixture-first/" + Id + "?autoplay=false") }); Assert(calls[0] == Request(Path.GetTempPath()).Url, "An alias did not use the canonical HTTPS record."); }
        var scoped = Request(Path.GetTempPath()) with { Url = new("https://pbskids.org/videos/watch/fixture-show/fixture-first/" + Id) }; Assert((await Extract([], html: Html(context: "fixture-show"), request: scoped)).Id == Id, "Property context failed.");
    }
    private static Task Boundaries()
    {
        var provider = new PbsKidsProvider(); foreach (var url in new[] { "https://pbskids.org/videos", "https://pbskids.org/video/?guid=" + Cid, "https://pbskids.org/games/fixture/" + Id,
            "https://secret@pbskids.org/videos/watch/fixture/" + Id, "https://pbskids.org:444/videos/watch/fixture/" + Id, "https://pbskids.org/videos/watch/fixture/0", "https://pbskids.org/videos/watch/fixture/01", "https://evil.pbskids.org/videos/watch/fixture/" + Id })
            Assert(!provider.CanHandle(new(url)), "An unsupported or unsafe PBS Kids URL was claimed.");
        Assert(YoutubeDl.Engine.Providers.MediaProviderRegistry.CreateDefault().Resolve(Request(Path.GetTempPath()).Url).Info.Id == "pbskids", "Native default routing missed PBS Kids."); return Task.CompletedTask;
    }
    private static async Task Identities()
    {
        await Expect<InvalidDataException>(() => Extract([], html: Html(Video(id: OtherId))));
        await Expect<InvalidDataException>(() => Extract([], html: Html().Replace("\"queriedVideoSlug\":\"fixture-first\"", "\"queriedVideoSlug\":\"other\"")));
        await Expect<InvalidDataException>(() => Extract([], html: Html().Replace("\"isFallback\":false", "\"isFallback\":true")));
        await Expect<InvalidDataException>(() => Extract([], redirect: true));
    }
    private static async Task Restrictions()
    {
        foreach (var change in new Action<JsonNode>[] { v => v["mediaManagerAsset"]!["drm_enabled"] = true, v => ((JsonObject)v["mediaManagerAsset"]!).Remove("drm_enabled"),
            v => v["expiryDate"] = "2000-01-01T00:00:00Z", v => v["videoType"] = "live", v => v["mediaManagerAsset"]!["videos"]![0]!["profile"] = "unknown-format" })
        { var record = Video(); change(record); var calls = new List<Uri>(); await Expect<NotSupportedException>(() => Extract(calls, html: Html(record))); Assert(calls.Count == 1, "A restricted/malformed record requested playback."); }
    }
    private static async Task Invalid()
    {
        await Expect<NotSupportedException>(() => Extract([], html: "<html>Missing page data</html>"));
        await Expect<NotSupportedException>(() => Extract([], html: Html() + Html()));
        await Expect<InvalidDataException>(() => Extract([], html: Html().Replace("\"drm_enabled\":false", "\"drm_enabled\":true,\"drm_enabled\":false")));
    }
    private static async Task Profiles()
    {
        var record = Video(); var profiles = (JsonArray)record["mediaManagerAsset"]!["videos"]!; profiles.Add(profiles[0]!.DeepClone());
        var drm = Encoding("hls-16x9-96p-drm", "33333333333333333333333333333333"); drm["widevine_license"] = "https://license.example/private"; profiles.Add(drm);
        var calls = new List<Uri>(); var page = await Extract(calls, html: Html(record)); Assert(page.Media.Count == 2 && page.Warnings.Count == 1 && calls.Count == 3 && !calls.Any(uri => uri.AbsolutePath.Contains("333333")), "Repeated/protected profile handling requested unrelated assets.");
    }
    private static NativeDownloadRequest CollectionRequest(string directory) => Request(directory) with { Url = new("https://pbskids.org/videos/playlist/fixture-collection/1567131") };
    private static async Task Collections()
    {
        var record = Collection(Entry(), Entry("42", OtherCid, "unavailable", false), Entry(OtherId, OtherCid, "fixture-second"));
        var page = await Extract([], html: Html(record, true), request: CollectionRequest(Path.GetTempPath()));
        Assert(page.IsPlaylist && page.Entries.Count == 3 && !page.Entries[1].Available && page.Entries[2].Position == 3 && page.Entries[2].ExpectedArchiveIdentity!.Id == OtherCid, "Collection positions or media binding changed.");
    }
    private static async Task CollectionFailures()
    {
        await Expect<InvalidDataException>(() => Extract([], html: Html(Collection(Entry(), Entry()), true), request: CollectionRequest(Path.GetTempPath())));
        var incomplete = Collection(); incomplete["hasNextPage"] = true;
        await Expect<NotSupportedException>(() => Extract([], html: Html(incomplete, true), request: CollectionRequest(Path.GetTempPath())));
        var wrong = Collection(); wrong["id"] = "42";
        await Expect<InvalidDataException>(() => Extract([], html: Html(wrong, true), request: CollectionRequest(Path.GetTempPath())));
        var mismatch = Collection(); mismatch["count"] = 3; await Expect<InvalidDataException>(() => Extract([], html: Html(mismatch, true), request: CollectionRequest(Path.GetTempPath())));
        await Expect<NotSupportedException>(() => Extract([], html: Html(Collection(), true), request: CollectionRequest(Path.GetTempPath()) with { MaximumPlaylistEntries = 1 }));
    }
    private static async Task Snapshots()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kids-snapshot-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
        try { var calls = new List<Uri>(); var engine = new NativeEngine([new PbsKidsProvider()], _ => Http(calls)); var request = CollectionRequest(dir) with { PlaylistItems = "2", SkipDownload = true, WriteInfoJson = true };
            var preview = await engine.InspectAsync(request, default); Assert(preview.PlaylistSnapshot is not null && preview.PlaylistSnapshot.Count == 2, "Collection snapshot was omitted.");
            calls.Clear(); var result = await engine.DownloadAsync(request with { PlaylistSnapshot = preview.PlaylistSnapshot }, null, null, default);
            Assert(result.Succeeded && calls.Count == 3 && calls.All(uri => !uri.AbsolutePath.Contains("fixture-collection")) && calls[0].AbsolutePath.Contains("fixture-second"), "Snapshot refreshed the parent or another child."); }
        finally { Directory.Delete(dir, true); }
    }
    private static async Task Archives()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kids-archive-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
        try { var archive = Path.Combine(dir, "archive.txt"); await File.WriteAllTextAsync(archive, "pbskids " + OtherCid + "\n"); var calls = new List<Uri>(); var engine = new NativeEngine([new PbsKidsProvider()], _ => Http(calls));
            var result = await engine.DownloadAsync(CollectionRequest(dir) with { PlaylistItems = "2", DownloadArchiveFile = archive }, null, null, default);
            Assert(result.Succeeded && calls.Count == 1 && !calls.Any(uri => uri.Host == "urs.pbs.org"), "Archived collection child requested discovery or playback."); }
        finally { Directory.Delete(dir, true); }
    }
    private static async Task Anonymous()
    {
        var options = new MediaHttpOptions(); options.Headers["Authorization"] = "Bearer fixture"; options.Headers["Cookie"] = "fixture=secret"; using var http = Http([], options: options,
            observe: r => Assert(!r.Headers.Contains("Authorization") && !r.Headers.Contains("Cookie"), "Caller credentials reached public PBS Kids discovery.")); _ = await new PbsKidsProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
    }
    private static async Task Denied()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kids-denied-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
        try { var calls = new List<Uri>(); var engine = new NativeEngine([new PbsKidsProvider()], _ => Http(calls, denied: true)); var callbacks = 0;
            await Expect<NotSupportedException>(() => engine.DownloadAsync(Request(dir) with { FfmpegPath = "unused", DownloadArchiveFile = Path.Combine(dir, "archive.txt") }, null, null, default, _ => callbacks++));
            Assert(calls.Count == 2 && callbacks == 0 && !File.Exists(Path.Combine(dir, "archive.txt")) && !calls.Any(uri => uri.Host.Contains("cdn")), "Denied playback guessed alternate sources or published files."); }
        finally { Directory.Delete(dir, true); }
    }
    private static async Task Cancellation()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel(); using var http = Http([]); await Expect<OperationCanceledException>(() => new PbsKidsProvider().ExtractAsync(Request(Path.GetTempPath()), http, stop.Token));
        using var denied = Http([], status: HttpStatusCode.Forbidden); await Expect<HttpRequestException>(() => new PbsKidsProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
    }
    private static async Task<PageMetadata> Extract(List<Uri> calls, string? html = null, NativeDownloadRequest? request = null, bool redirect = false)
    { using var http = Http(calls, html, redirect: redirect); return await new PbsKidsProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static MediaHttp Http(List<Uri> calls, string? html = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null, MediaHttpOptions? options = null,
        Action<HttpRequestMessage>? observe = null, bool redirect = false, bool denied = false, HttpStatusCode status = HttpStatusCode.OK) => new(new Handler(r =>
    {
        calls.Add(r.RequestUri!); observe?.Invoke(r); var uri = r.RequestUri!;
        if (uri.Host == "pbskids.org")
        {
            var record = uri.AbsolutePath.Contains("fixture-second") ? Video(OtherId, OtherCid, "fixture-second") : Video();
            return new(status) { Content = new StringContent(html ?? (uri.AbsolutePath.Contains("playlist") ? Html(Collection(), true) : Html(record))), RequestMessage = redirect ? new(HttpMethod.Get, "https://pbskids.org/videos/watch/other/42") : r };
        }
        if (uri.Host == "urs.pbs.org")
        { if (uri.Query != "?format=json") throw new InvalidOperationException("PBS Kids resolver mode changed."); var cid = calls.Any(uri => uri.AbsolutePath.Contains("fixture-second")) ? OtherCid : Cid;
            return new(HttpStatusCode.OK) { Content = new StringContent(new JsonObject { ["status"] = denied ? "error" : "ok", ["http_code"] = denied ? 403 : 302, ["url"] = denied ? null : Origin(cid) + (uri.AbsolutePath.Contains("111111") ? "source.mp4" : "master.m3u8") + Signed }.ToJsonString()) }; }
        return media?.Invoke(r) ?? throw new InvalidOperationException("Unexpected PBS Kids media request.");
    }), options ?? new());
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
}
