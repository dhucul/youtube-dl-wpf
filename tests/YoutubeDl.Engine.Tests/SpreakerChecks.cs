using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Spreaker;

internal static class SpreakerChecks
{
    internal const string Id = "60424856", Other = "60424857", ShowId = "6208766", Author = "17777613";
    internal const string Signed = "?literal=A%2Bb&case=C%2Fd";
    internal static string Media(string id = Id) => "https://api.spreaker.com/v2/episodes/" + id + "/download.mp3" + Signed;
    internal static NativeDownloadRequest Request(string directory, bool show = false) => new() { Url = new("https://www.spreaker.com/" + (show ? "podcast/fixture--" + ShowId : "episode/trailer--" + Id)), OutputDirectory = directory, OutputTemplate = "spreaker.%(ext)s" };
    internal static JsonNode Show() => new JsonObject { ["show_id"] = long.Parse(ShowId), ["author_id"] = long.Parse(Author), ["slug"] = "fixture", ["title"] = "Fixture show", ["site_url"] = "https://www.spreaker.com/podcast/fixture--" + ShowId,
        ["author"] = new JsonObject { ["user_id"] = long.Parse(Author), ["fullname"] = "Fixture author" } };
    internal static JsonNode Episode(string id = Id, double duration = 2) => new JsonObject { ["episode_id"] = long.Parse(id), ["show_id"] = long.Parse(ShowId), ["author_id"] = long.Parse(Author), ["type"] = "RECORDED", ["title"] = "Episode " + id,
        ["duration"] = duration * 1000, ["download_enabled"] = true, ["site_url"] = "https://www.spreaker.com/episode/trailer--" + id, ["download_url"] = Media(id), ["show"] = Show(), ["author"] = new JsonObject { ["user_id"] = long.Parse(Author), ["fullname"] = "Fixture author" } };
    internal static JsonNode List(string? next = null, params JsonNode[] items) => new JsonObject { ["items"] = new JsonArray(items.Length == 0 ? [Episode(), Episode(Other)] : items), ["next_url"] = next };
    internal static string Next(string last = Id) => "https://api.spreaker.com/v2/shows/" + ShowId + "/episodes?filter=listenable&sorting=newest&last_id=" + last + "&limit=100";
    internal static IEnumerable<(string Name, Func<Task> Test)> All => [
        ("Spreaker current recorded episodes bind stable show author and full audio duration", Facts),
        ("Spreaker numeric slug show and widget aliases select the same stable resource", Aliases),
        ("Spreaker ambiguous private user and unrelated routes remain explicit boundaries", Boundaries),
        ("Spreaker conflicting episode show author and public link identities fail before media", Identities),
        ("Spreaker private limited live draft processing disabled and malformed states fail", Restrictions),
        ("Spreaker supplied download origins paths and queries retain identity and literal bytes", Sources),
        ("Spreaker complete public show pages retain original positions and scoped cursors", Pagination),
        ("Spreaker changed repeated incomplete and excessive inventories cannot claim completion", Incomplete),
        ("Spreaker unavailable show entries retain inspected source positions", Positions),
        ("Spreaker inspected snapshots refresh only chosen episodes and archives skip discovery", Snapshots),
        ("Spreaker public metadata isolates credentials cancellation denial and redirects", Transport)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var page = await Extract(calls); var media = page.Media.Single();
        Assert(page.Id == Id && page.ArchiveIdentity!.Extractor == "Spreaker" && page.Uploader == "Fixture author" && page.DurationSeconds == 2 && media.IsAudioOnly && media.Url.AbsoluteUri == Media() && media.VerificationDurationSeconds == 2 && media.VerificationDemuxer == "mp3" && calls.Count == 1, "Spreaker metadata/source facts changed.");
        var old = Episode(); old["visibility"] = "PUBLIC"; old["hidden"] = false; old["encoding_status"] = "READY"; Assert((await Extract([], old)).Id == Id, "Older documented explicit public states failed.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "http://spreaker.com/episode/" + Id, "https://api.spreaker.com/v2/episodes/" + Id, "https://widget.spreaker.com/player?episode_id=" + Id + "&theme=dark&playlist=show" })
            Assert((await Extract([], request: Request(Path.GetTempPath()) with { Url = new(url) })).Id == Id, "Episode alias changed identity.");
        foreach (var url in new[] { "https://www.spreaker.com/show/fixture", "https://widget.spreaker.com/player?show_id=" + ShowId, "https://api.spreaker.com/v2/shows/" + ShowId })
            Assert((await Extract([], request: Request(Path.GetTempPath(), true) with { Url = new(url) })).Entries.Count == 2, "Show alias changed inventory.");
    }
    private static Task Boundaries()
    {
        var provider = new SpreakerProvider(); foreach (var url in new[] { "https://www.spreaker.com/episode/slug", "https://secret@www.spreaker.com/episode/" + Id, "https://www.spreaker.com:444/episode/" + Id,
            "https://widget.spreaker.com/player?episode_id=" + Id + "&episode_key=private", "https://widget.spreaker.com/player?episode_id=" + Id + "&show_id=" + ShowId,
            "https://widget.spreaker.com/player?episode_id=" + Id + "&episode_id=" + Other, "https://widget.spreaker.com/player?user_id=1", "https://www.spreaker.com/user/fixture" }) Assert(!provider.CanHandle(new(url)), "Unsupported Spreaker route was claimed.");
        Assert(YoutubeDl.Engine.Providers.MediaProviderRegistry.CreateDefault().Resolve(Request(Path.GetTempPath()).Url).Info.Id == "spreaker", "Default registry missed Spreaker."); return Task.CompletedTask;
    }
    private static async Task Identities()
    {
        foreach (var change in new Action<JsonNode>[] { e => e["episode_id"] = long.Parse(Other), e => e["show_id"] = 123, e => e["author_id"] = 123, e => e["show"]!["author_id"] = 123, e => e["author"]!["user_id"] = 123, e => e["site_url"] = "https://www.spreaker.com/episode/" + Other })
        { var e = Episode(); change(e); await Expect<InvalidDataException>(() => Extract([], e)); }
    }
    private static async Task Restrictions()
    {
        foreach (var change in new Action<JsonNode>[] { e => e["visibility"] = "PRIVATE", e => e["visibility"] = "LIMITED", e => e["hidden"] = true, e => e["site_limited_key"] = "private", e => e["type"] = "LIVE", e => e["type"] = "DRAFT", e => e["type"] = "EXTERNAL", e => e["encoding_status"] = "PROCESSING", e => e["download_enabled"] = false, e => ((JsonObject)e).Remove("download_enabled") })
        { var e = Episode(); change(e); await Expect<NotSupportedException>(() => Extract([], e)); }
        var malformed = Episode(); malformed["hidden"] = "false"; await Expect<InvalidDataException>(() => Extract([], malformed));
        using var bad = Http([], raw: "bad JSON"); await Expect<InvalidDataException>(() => new SpreakerProvider().ExtractAsync(Request(Path.GetTempPath()), bad, default));
        using var duplicate = Http([], raw: "{\"response\":{},\"response\":{}}"); await Expect<InvalidDataException>(() => new SpreakerProvider().ExtractAsync(Request(Path.GetTempPath()), duplicate, default));
    }
    private static async Task Sources()
    {
        foreach (var source in new[] { Media(Other), "https://unrelated.example/audio.mp3", Media().Replace("download.mp3", "play.mp3") }) { var e = Episode(); e["download_url"] = source; await Expect<NotSupportedException>(() => Extract([], e)); }
    }
    private static async Task Pagination()
    {
        var calls = new List<Uri>(); var page = await Extract(calls, request: Request(Path.GetTempPath(), true), first: List(Next(), Episode()), second: List(null, Episode(Other)));
        Assert(page.IsPlaylist && page.Id == ShowId && page.Entries.Count == 2 && page.Entries[1].Position == 2 && page.Entries[1].ExpectedArchiveIdentity!.Id == Other && calls.Count == 3 && calls[2].AbsoluteUri == Next(), "Complete public paging changed identity/order/scope.");
    }
    private static async Task Incomplete()
    {
        foreach (var next in new[] { Next(Other), Next().Replace(ShowId, "123"), Next().Replace("listenable", "editable"), Next() + "&token=secret", Next().Replace("limit=100", "limit=20") })
            await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: List(next, Episode())));
        await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: List(Next(), Episode()), second: List(null, Episode())));
        var missing = List(); ((JsonObject)missing).Remove("next_url"); await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: missing));
        var empty = List(Next()); empty["items"] = new JsonArray(); await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: empty));
        var wrong = Episode(); wrong["show_id"] = 123; await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: List(null, wrong)));
        await Expect<NotSupportedException>(() => Extract([], request: Request(Path.GetTempPath(), true) with { MaximumPlaylistEntries = 1 }));
    }
    private static async Task Positions()
    {
        var unavailable = Episode(); unavailable["type"] = "LIVE"; var page = await Extract([], request: Request(Path.GetTempPath(), true), first: List(null, unavailable, Episode(Other)));
        Assert(!page.Entries[0].Available && page.Entries[1].Position == 2 && page.Entries[1].Available, "Unavailable slots changed selected source numbering.");
    }
    private static async Task Snapshots()
    {
        var directory = Path.Combine(Path.GetTempPath(), "spreaker-check-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try
        {
            var calls = new List<Uri>(); var engine = new NativeEngine([new SpreakerProvider()], _ => Http(calls)); var request = Request(directory, true) with { PlaylistItems = "2", SkipDownload = true, WriteInfoJson = true };
            var info = await engine.InspectAsync(request, default); calls.Clear(); var result = await engine.DownloadAsync(request with { PlaylistSnapshot = info.PlaylistSnapshot }, null, null, default);
            Assert(result.Succeeded && calls.Count == 1 && calls[0].AbsolutePath.EndsWith(Other), "Inspected selection refreshed unselected metadata.");
            var archive = Path.Combine(directory, "archive.txt"); await File.WriteAllTextAsync(archive, "spreaker " + Id + "\nspreaker " + Other + "\n"); calls.Clear();
            result = await engine.DownloadAsync(Request(directory) with { DownloadArchiveFile = archive }, null, null, default); Assert(result.Skipped && calls.Count == 0, "Direct archive requested metadata.");
            result = await engine.DownloadAsync(request with { PlaylistSnapshot = info.PlaylistSnapshot, DownloadArchiveFile = archive }, null, null, default); Assert(result.Succeeded && calls.Count == 0, "Archived snapshot requested discovery.");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task Transport()
    {
        var options = new MediaHttpOptions(); options.Headers["Authorization"] = "Bearer fixture"; options.Headers["Cookie"] = "fixture=secret";
        using var http = Http([], options: options, observe: r => Assert(!r.Headers.Contains("Authorization") && !r.Headers.Contains("Cookie"), "Caller credentials reached public metadata.")); _ = await new SpreakerProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
        using var stop = new CancellationTokenSource(); stop.Cancel(); await Expect<OperationCanceledException>(() => new SpreakerProvider().ExtractAsync(Request(Path.GetTempPath()), http, stop.Token));
        using var denied = Http([], status: HttpStatusCode.Forbidden); await Expect<HttpRequestException>(() => new SpreakerProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
        using var redirected = Http([], redirect: true); await Expect<InvalidDataException>(() => new SpreakerProvider().ExtractAsync(Request(Path.GetTempPath()), redirected, default));
    }
    private static async Task<PageMetadata> Extract(List<Uri> calls, JsonNode? episode = null, NativeDownloadRequest? request = null, JsonNode? first = null, JsonNode? second = null)
    { using var http = Http(calls, episode, first, second); return await new SpreakerProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static MediaHttp Http(List<Uri> calls, JsonNode? episode = null, JsonNode? first = null, JsonNode? second = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null, MediaHttpOptions? options = null,
        Action<HttpRequestMessage>? observe = null, string? raw = null, HttpStatusCode status = HttpStatusCode.OK, bool redirect = false) => new(new Handler(r =>
        {
            calls.Add(r.RequestUri!); observe?.Invoke(r); var uri = r.RequestUri!; JsonNode? data = null;
            if (uri.AbsolutePath.EndsWith("download.mp3")) return media?.Invoke(r) ?? throw new InvalidOperationException("Unexpected playback request.");
            if (uri.AbsolutePath.Contains("/episodes/") && !uri.AbsolutePath.EndsWith("/episodes")) data = new JsonObject { ["episode"] = uri.AbsolutePath.EndsWith(Other) ? Episode(Other) : episode?.DeepClone() ?? Episode() };
            else if (uri.AbsolutePath.EndsWith("/episodes")) data = (uri.Query.Contains("last_id=") ? second ?? List(null, Episode(Other)) : first ?? List()).DeepClone();
            else data = new JsonObject { ["show"] = Show() };
            return new(status) { Content = new StringContent(raw ?? new JsonObject { ["response"] = data }.ToJsonString()), RequestMessage = redirect ? new(HttpMethod.Get, "https://other.example/data") : r };
        }), options ?? new());
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
}
