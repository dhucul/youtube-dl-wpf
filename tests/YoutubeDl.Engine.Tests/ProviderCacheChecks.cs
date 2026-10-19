using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Rutube;
using YoutubeDl.Engine.Providers.YouTube;

internal static class ProviderCacheChecks
{
    internal const string First = "11111111111111111111111111111111", Second = "22222222222222222222222222222222";
    internal static IEnumerable<(string Name, Func<Task> Test)> All
    {
        get
        {
            foreach (var kind in new[] { "person", "channel", "movie", "tags", "playlist" }) yield return ("Provider cache Rutube complete inventory " + kind, () => Inventory(kind));
            foreach (var mode in new[] { "foreign", "path", "page", "client", "query", "cycle", "count", "owner", "identity", "missing-next", "empty-next", "limit", "permalink", "redirect", "page-count" })
                yield return ("Provider cache Rutube inventory refusal " + mode, () => BadInventory(mode));
            foreach (var mode in new[] { "video", "embed", "dash", "duplicate", "denied", "drm", "paid", "hidden", "deleted", "locked", "club", "live", "identity", "effective", "track", "foreign-media", "redirect" })
                yield return ("Provider cache Rutube playback " + mode, () => Playback(mode));
            foreach (var mode in new[] { "reorder", "removed", "changed", "renew", "renew-identity", "renew-denied" }) yield return ("Provider cache Rutube selected contract " + mode, () => Selected(mode));
            foreach (var mode in new[] { "reuse", "concurrent", "disabled", "corrupt", "hash", "expired", "future", "identity", "runtime", "write-denied", "authenticated", "cookies", "headers", "query", "cancel", "unknown", "oversized", "opaque-cookies", "pre-canceled", "disabled-invalid" })
                yield return ("Provider cache public script " + mode, () => Cache(mode));
            foreach (var url in new[] { "https://rutube.ru/video/not-an-id", "https://rutube.ru/channel/0/", "https://rutube.ru:444/video/" + First, "https://user@rutube.ru/video/" + First, "https://rutube.ru/video/" + First + "?p=private", "https://rutube.ru/video/" + First + "?plst_id=9", "https://rutube.ru/video/" + First + "#fragment", "https://rutube.ru.evil.example/video/" + First })
                yield return ("Provider cache Rutube address refusal " + url, () => Address(url));
            yield return ("Provider cache argument order and invalid values", Arguments);
            yield return ("Provider cache YouTube uses fresh watch context and cached public source", YouTubeCache);
            yield return ("Provider cache operational choices preserve download recovery identity", RecoveryIdentity);
            yield return ("Provider cache Rutube follows growing page estimates to actual end", EstimatedPages);
            yield return ("Provider cache Rutube mirrors retain one offered CDN per quality", () => Mirrors(false));
            yield return ("Provider cache Rutube refuses unequal equal-fact mirror paths", () => Mirrors(true));
        }
    }
    internal static JsonNode Video(string id = First) => new JsonObject { ["id"] = id, ["video_url"] = "https://rutube.ru/video/" + id + "/", ["title"] = "Fixture " + id,
        ["description"] = "Fixture description", ["track_id"] = id == First ? 101 : 102, ["duration"] = 3, ["author"] = new JsonObject { ["id"] = 7, ["name"] = "Fixture artist" }, ["action_reason"] = new JsonObject { ["id"] = 0 } };
    internal static JsonNode Options(string id = First, string? media = null) => new JsonObject { ["video_id"] = id, ["effective_video"] = id, ["id"] = id == First ? 101 : 102,
        ["acl_access"] = new JsonObject { ["allowed"] = true }, ["video_balancer"] = new JsonObject { ["m3u8"] = media ?? "https://bl.rutube.ru/fixture.m3u8?expire=4102444800&sign=%2B" } };
    internal static Uri Source(string kind) => new("https://rutube.ru/" + (kind switch { "person" => "video/person/7", "channel" => "channel/7", "movie" => "metainfo/tv/7", "tags" => "tags/video/7", _ => "plst/7" }) + "/");
    internal static string Connection(string kind) => kind switch { "person" or "channel" => "/api/video/person/7/", "movie" => "/api/metainfo/tv/7/video/", "tags" => "/api/tags/video/7/", _ => "/api/playlist/custom/7/videos/" };
    internal static JsonNode Parent(string kind, int count = 2) => new JsonObject { ["id"] = 7, ["name"] = "Collection", ["title"] = "Collection", ["videos_count"] = count, ["content"] = "https://rutube.ru/api/metainfo/tv/7/video" };
    internal static JsonNode Page(string kind, int page, IEnumerable<JsonNode> items, bool more = false) => new JsonObject { ["page"] = page, ["per_page"] = 20,
        ["results"] = new JsonArray(items.Select(item => item.DeepClone()).ToArray()), ["has_next"] = more, ["next"] = more ? "https://rutube.ru" + Connection(kind) + "?page=" + (page + 1) + (kind is "person" or "channel" ? "&client=wdp" : "") : null };
    internal static HttpResponseMessage Json(JsonNode value) => SoundCloudChecks.Response(value.ToJsonString());
    internal static MediaHttp Http(Func<HttpRequestMessage, HttpResponseMessage> action) => AdaptiveAccountChecks.Http((message, _) => Task.FromResult(action(message)));
    internal static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static NativeDownloadRequest Request(string directory, string kind = "person") => new() { Url = Source(kind), OutputDirectory = directory, Retries = 0 };
    private static async Task Inventory(string kind)
    {
        var calls = new List<Uri>();
        var engine = new NativeEngine(httpFactory: _ => Http(message =>
        {
            var uri = message.RequestUri!; calls.Add(uri);
            if (uri.AbsolutePath != Connection(kind)) return Json(Parent(kind));
            return Json(uri.Query.Contains("page=2") ? Page(kind, 2, [Video(Second)]) : Page(kind, 1, [Video()], true));
        }));
        var info = await engine.InspectAsync(Request(Path.GetTempPath(), kind), default);
        Check(info.IsPlaylist && info.PlaylistSnapshot?.Count == 2 && info.Entries.Select(entry => entry.Id).SequenceEqual(new[] { First, Second }) && info.Formats.Count == 0, "Complete ordered inventory was lost.");
        Check(calls.Count == (kind is "movie" or "playlist" ? 3 : 2) && calls.All(uri => !uri.AbsolutePath.StartsWith("/api/play/")), "Inventory fetched selected playback.");
    }
    private static async Task BadInventory(string mode)
    {
        var kind = mode == "owner" ? "person" : "playlist"; var pages = 0;
        var engine = new NativeEngine(httpFactory: _ => Http(message =>
        {
            var uri = message.RequestUri!;
            if (uri.AbsolutePath != Connection(kind)) { var parent = Parent(kind, mode == "count" ? 3 : 2); if (mode == "identity") parent["id"] = 8; return Json(parent); }
            pages++; Check(pages <= 2, "Unsafe pagination loop."); var row = Video(pages == 1 ? First : Second);
            if (mode == "owner") row["author"]!["id"] = 8; if (mode == "permalink") row["video_url"] = "https://rutube.ru/video/" + Second;
            var node = Page(kind, pages, mode == "empty-next" ? [] : [row], pages == 1);
            if (pages == 1) node["next"] = mode switch { "foreign" => "https://evil.example/api/playlist/custom/7/videos/?page=2", "path" => "https://rutube.ru/api/playlist/custom/8/videos/?page=2", "query" => "https://rutube.ru" + Connection(kind) + "?page=2&secret=x", "client" => "https://rutube.ru" + Connection(kind) + "?page=2&client=other", "cycle" => uri.AbsoluteUri, _ => node["next"]!.GetValue<string>() };
            if (mode == "page") node["page"] = 9; if (mode == "missing-next") node.AsObject().Remove("next");
            if (mode == "page-count") node["num_pages"] = 0;
            var response = Json(node); if (mode == "redirect") response.RequestMessage = new(HttpMethod.Get, "https://rutube.ru/api/other/"); return response;
        }));
        await Fails(() => engine.InspectAsync(Request(Path.GetTempPath(), kind) with { MaximumPlaylistEntries = mode == "limit" ? 1 : 100 }, default));
    }
    private static async Task Playback(string mode)
    {
        var calls = new List<Uri>(); var video = Video(); var options = Options();
        if (mode is "paid" or "hidden" or "deleted" or "locked" or "club") video["is_" + mode] = true;
        if (mode == "live") video["is_livestream"] = true;
        if (mode == "identity") video["id"] = Second;
        if (mode == "effective") options["effective_video"] = Second;
        if (mode == "track") options["id"] = 999;
        if (mode == "denied") options["acl_access"]!["allowed"] = false;
        if (mode == "drm") options["drm_token"] = "fixture-protected";
        if (mode == "dash") options["video_balancer"] = new JsonObject { ["dash"] = "https://bl.rutube.ru/fixture.mpd?sign=%2B" };
        if (mode == "duplicate") options["video_balancer"]!["default"] = options["video_balancer"]!["m3u8"]!.DeepClone();
        if (mode == "foreign-media") options["video_balancer"]!["m3u8"] = "https://evil.example/fixture.m3u8";
        var engine = new NativeEngine(httpFactory: _ => Http(message =>
        {
            calls.Add(message.RequestUri!); var response = Json(message.RequestUri!.AbsolutePath.StartsWith("/api/video/") ? video : options);
            if (message.RequestUri.Host == "bl.rutube.ru") return SoundCloudChecks.Response("#EXTM3U\n#EXT-X-TARGETDURATION:3\n#EXTINF:3,\nsegment.ts\n#EXT-X-ENDLIST");
            if (mode == "redirect") response.RequestMessage = new(HttpMethod.Get, "https://rutube.ru/api/other/"); return response;
        }));
        var request = Request(Path.GetTempPath()) with { Url = new("https://rutube.ru/" + (mode == "embed" ? "play/embed/" : "video/") + First + "/") };
        if (mode is "identity" or "effective" or "track" or "denied" or "foreign-media" or "redirect" or "live") { await Fails(() => engine.InspectAsync(request, default)); return; }
        var info = await engine.InspectAsync(request, default);
        if (mode is "paid" or "hidden" or "deleted" or "locked" or "club" or "drm") Check(info.Formats.Count == 0, "Restricted playback became ready.");
        else Check(info.Id == First && info.Formats.Count == 1 && info.Formats[0].Url!.Query.Contains("sign=%2B"), "Identity, signed query or duplicate format handling changed.");
        Check(calls.All(uri => uri.Host == "rutube.ru" || uri.AbsolutePath == "/fixture.m3u8"), "Inspection fetched media segments.");
    }
    private static async Task Selected(string mode)
    {
        using var files = new Owned(); var fresh = false; var children = 0; var media = 0; var callbacks = new List<string>();
        var engine = new NativeEngine(httpFactory: _ => Http(message =>
        {
            var uri = message.RequestUri!;
            if (uri.AbsolutePath == Connection("person")) return Json(Page("person", 1, fresh && mode == "removed" ? [Video()] : fresh ? [Video(Second), Video()] : [Video(), Video(Second)]));
            if (uri.AbsolutePath.StartsWith("/api/video/")) { children++; return Json(Video(mode == "changed" || children > 1 && mode == "renew-identity" ? First : Second)); }
            if (uri.AbsolutePath.StartsWith("/api/play/")) return Json(Options(Second, "https://video.rtbcdn.ru/audio-" + children + ".mp4?sign=%2B"));
            Check(uri.Host == "video.rtbcdn.ru", "Unexpected transfer host."); media++;
            return mode.StartsWith("renew", StringComparison.Ordinal) && (children == 1 || mode == "renew-denied") ? new(HttpStatusCode.Forbidden) : SoundCloudChecks.Response("complete fixture media");
        }));
        var output = Path.Combine(files.Path, "002 - recording.mp4"); var archive = Path.Combine(files.Path, "archive.txt"); await File.WriteAllTextAsync(output, "previous"); await File.WriteAllTextAsync(archive, "prior 1\n");
        var request = Request(files.Path) with { PlaylistItems = "2", OutputTemplate = "%(playlist_index)03d - recording.%(ext)s", DownloadArchiveFile = archive };
        var info = await engine.InspectAsync(request, default); fresh = true; request = request with { PlaylistSnapshot = info.PlaylistSnapshot }; var bad = mode is "removed" or "changed" or "renew-identity" or "renew-denied";
        NativeDownloadResult? result = null; try { result = await engine.DownloadAsync(request, null, null, default, callbacks.Add); } catch (Exception ex) when (bad && ex is InvalidDataException or HttpRequestException) { }
        if (bad) { Check(result?.Succeeded != true && callbacks.Count == 0 && await File.ReadAllTextAsync(output) == "previous" && await File.ReadAllTextAsync(archive) == "prior 1\n", "Failure changed publication."); return; }
        Check(result!.Succeeded && media == (mode == "renew" ? 2 : 1) && await File.ReadAllTextAsync(output) == "complete fixture media", "Fresh selected playback failed: " + result.Error);
        var before = (children, media, callbacks.Count); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add); Check(repeat.Skipped && before == (children, media, callbacks.Count), "Archived member fetched playback/media or emitted callbacks.");
    }
    private static readonly Uri ScriptUrl = new("https://www.youtube.com/s/player/cache-fixture/player_ias.vflset/en_US/base.js");
    private static async Task Cache(string mode)
    {
        using var files = new Owned(); var calls = 0; var runtime = new Runtime(); using var cancel = new CancellationTokenSource();
        var request = Request(files.Path) with { CacheDirectory = files.Path };
        if (mode == "write-denied") { var path = Path.Combine(files.Path, "file"); await File.WriteAllTextAsync(path, "preserved"); request = request with { CacheDirectory = path }; }
        if (mode == "disabled") request = request with { NoCache = true };
        if (mode == "disabled-invalid") request = request with { CacheDirectory = "unused\0invalid", NoCache = true };
        if (mode == "authenticated") request = request with { Authentication = new() { Username = "fixture-user" } };
        if (mode == "cookies") request = request with { CookieFile = "fixture-cookie-selection" };
        if (mode == "headers") request = request with { HttpHeaders = new Dictionary<string, string> { ["X-Fixture"] = "private-context" } };
        var url = mode == "query" ? new Uri(ScriptUrl.AbsoluteUri + "?session=private") : ScriptUrl;
        HttpResponseMessage Respond(HttpRequestMessage _) { calls++; if (mode == "cancel") cancel.Cancel(); return SoundCloudChecks.Response(mode == "unknown" ? "invalid" : "valid public source"); }
        var cookies = new CookieContainer(); if (mode == "opaque-cookies") cookies.Add(new Cookie("fixture", "private-context", "/", ".youtube.com"));
        using var http = AdaptiveAccountChecks.Http((message, _) => Task.FromResult(Respond(message)), cookies);
        async Task Use() { using var session = await NativePlayerScriptCache.CreateAsync(request, http, url, runtime, true, cancel.Token); }
        if (mode == "pre-canceled") cancel.Cancel();
        if (mode is "cancel" or "unknown" or "pre-canceled") { await Fails(Use); Check(!Directory.EnumerateFiles(files.Path, "*.json", SearchOption.AllDirectories).Any(), "Canceled/invalid source entered cache."); if (mode == "pre-canceled") Check(calls == 0, "Pre-canceled cache contacted HTTP."); return; }
        await Use();
        if (mode == "concurrent") { await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Use())); Check(calls == 1 && runtime.Count == 13, "Concurrent cache reads fetched source repeatedly or shared interpreter state."); return; }
        if (mode is "corrupt" or "hash" or "expired" or "future" or "identity" or "runtime" or "oversized")
        {
            var path = Directory.EnumerateFiles(files.Path, "*.json", SearchOption.AllDirectories).Single(); var node = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            if (mode == "corrupt") await File.WriteAllTextAsync(path, "{");
            else if (mode == "oversized") { using var large = new FileStream(path, FileMode.Open, FileAccess.Write); large.SetLength(3 * 1024 * 1024 * 7L + 1); }
            else
            {
                if (mode == "hash") node["Hash"] = "bad"; if (mode == "expired") node["Created"] = DateTimeOffset.UtcNow.AddDays(-31); if (mode == "future") node["Created"] = DateTimeOffset.UtcNow.AddDays(1); if (mode == "identity") node["Url"] = "https://evil.example/base.js";
                if (mode == "runtime") { node["Source"] = "invalid"; node["Hash"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("invalid"))); }
                await File.WriteAllTextAsync(path, node.ToJsonString());
            }
        }
        NativePlayerScriptCache.Validate(request);
        await Use(); var bypass = mode is "disabled" or "write-denied" or "authenticated" or "cookies" or "headers" or "query" or "opaque-cookies" or "disabled-invalid";
        Check(calls == (mode == "reuse" ? 1 : 2), "Cache reuse/recovery/bypass request count differs.");
        if (bypass) Check(!Directory.EnumerateFiles(files.Path, "*.json", SearchOption.AllDirectories).Any(), "Private/disabled/unwritable context entered cache.");
        else { await Use(); Check(calls == (mode == "reuse" ? 1 : 2), "Fresh repaired source was not cached."); }
    }
    private static Task Address(string source)
    {
        try { _ = RutubeAddress.Parse(new(source)); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return Task.CompletedTask; }
        throw new Exception("Invalid Rutube source was accepted.");
    }
    private static Task Arguments()
    {
        var request = Request(Path.GetTempPath()); var result = NativeExtraArguments.Apply(request, ["--cache-dir=first", "--no-cache-dir", "--cache-dir", "last"]);
        Check(result.CacheDirectory == "last" && !result.NoCache, "Cache argument precedence changed.");
        result = NativeExtraArguments.Apply(result, ["--no-cache-dir"]); Check(result.NoCache && result.CacheDirectory == "last", "Disabled cache lost its configured path.");
        try { NativeExtraArguments.Apply(request, ["--cache-dir="]); throw new Exception("An empty cache path was accepted."); } catch (ArgumentException) { }
        return Fails(() => new NativeEngine().InspectAsync(request with { CacheDirectory = "bad\0path" }, default));
    }
    private static Task RecoveryIdentity()
    {
        var request = Request(Path.GetTempPath());
        Check(YoutubeDl.Engine.Download.DownloadWorkspace.Recipe(request) == YoutubeDl.Engine.Download.DownloadWorkspace.Recipe(request with { CacheDirectory = "changed operational location", NoCache = true }), "Cache configuration invalidated media recovery.");
        return Task.CompletedTask;
    }
    private static async Task EstimatedPages()
    {
        var engine = new NativeEngine(httpFactory: _ => Http(message =>
        {
            var page = message.RequestUri!.Query.Contains("page=2") ? 2 : 1; var node = Page("person", page, [Video(page == 1 ? First : Second)], page == 1); node["num_pages"] = page + 1; return Json(node);
        }));
        Check((await engine.InspectAsync(Request(Path.GetTempPath()), default)).Entries.Count == 2, "An estimated page count replaced actual pagination termination.");
    }
    private static async Task Mirrors(bool invalid)
    {
        var engine = new NativeEngine(httpFactory: _ => Http(message =>
        {
            if (message.RequestUri!.Host == "rutube.ru") return Json(message.RequestUri.AbsolutePath.StartsWith("/api/video/") ? Video() : Options());
            const string facts = "#EXT-X-STREAM-INF:BANDWIDTH=300000,RESOLUTION=128x72,CODECS=\"avc1.42c01e,mp4a.40.2\"\n";
            return SoundCloudChecks.Response("#EXTM3U\n" + facts + "https://video.rtbcdn.ru/path/stream.m3u8?sign=%2B\n" + facts + "https://river.rutube.ru/path/" + (invalid ? "other" : "stream") + ".m3u8?sign=%2B\n");
        }));
        var request = Request(Path.GetTempPath()) with { Url = new("https://rutube.ru/video/" + First) };
        if (invalid) { await Fails(() => engine.InspectAsync(request, default)); return; }
        var info = await engine.InspectAsync(request, default); Check(info.Formats.Count == 1 && info.Formats[0].Height == 72 && info.Formats[0].Url!.Host == "video.rtbcdn.ru", "Offered equal CDN mirrors became ambiguous or changed quality.");
    }
    private static async Task YouTubeCache()
    {
        using var files = new Owned(); var watches = 0; var scripts = 0;
        var engine = new NativeEngine(playerRuntime: new YoutubeDl.Engine.PlayerRuntime.ManagedYouTubePlayerRuntime(), httpFactory: _ => Http(message =>
        {
            if (message.RequestUri!.AbsolutePath == "/watch") { watches++; return SoundCloudChecks.Response("<script>ytInitialPlayerResponse=" + JsonSerializer.Serialize(new { videoDetails = new { videoId = "abcdefghijk", title = "Fresh watch " + watches }, playabilityStatus = new { status = "OK" }, streamingData = new { formats = new[] { new { itag = 18, url = "https://r1.googlevideo.com/videoplayback?n=fixture", mimeType = "video/mp4; codecs=\"avc1.42c01e,mp4a.40.2\"" } } } }) + ";var config={\"jsUrl\":\"" + ScriptUrl.AbsoluteUri + "\"};</script>"); }
            Check(message.RequestUri == ScriptUrl, "Unexpected public cache request."); scripts++; return SoundCloudChecks.Response(PlayerRuntimeChecks.Script);
        }));
        var request = Request(files.Path) with { Url = new("https://www.youtube.com/watch?v=abcdefghijk"), CacheDirectory = files.Path };
        var first = await engine.InspectAsync(request, default); var second = await engine.InspectAsync(request, default);
        Check(watches == 2 && scripts == 1 && second.Title == "Fresh watch 2" && first.Formats.Single().Availability == FormatAvailability.Ready, "Cache reused watch data or failed public player resolution.");
    }
    private sealed class Runtime : IYouTubePlayerRuntime
    {
        internal int Count;
        public IYouTubePlayerSession CreateSession(string source, CancellationToken ct) { Interlocked.Increment(ref Count); ct.ThrowIfCancellationRequested(); if (source == "invalid") throw new NotSupportedException("Unknown fixture layout."); return new Session(); }
        private sealed class Session : IYouTubePlayerSession { public Uri Transform(Uri url, string parameter, string signature, CancellationToken ct) => url; public void Dispose() { } }
    }
    private sealed class Owned : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "provider-cache-" + Guid.NewGuid().ToString("N"));
        internal Owned() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
    private static async Task Fails(Func<Task> action)
    {
        try { await action(); } catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentException or OperationCanceledException) { return; }
        throw new Exception("An invalid provider/cache contract succeeded.");
    }
}
