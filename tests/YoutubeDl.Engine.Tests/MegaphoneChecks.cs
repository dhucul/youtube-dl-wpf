using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Megaphone;

internal static class MegaphoneChecks
{
    internal const string Id = "GLT8924910686", Other = "GLT8924910687", Signed = "?literal=A%2Bb&case=C%2Fd";
    internal static string Media(string id = Id) => "https://chrt.fm/track/15E3G4/traffic.megaphone.fm/" + id + ".mp3" + Signed;
    internal static NativeDownloadRequest Request(string directory, bool podcast = false) => new() { Url = new(podcast ? "https://playlist.megaphone.fm/?p=fixture" : "https://player.megaphone.fm/" + Id), OutputDirectory = directory, OutputTemplate = "megaphone.%(ext)s" };
    internal static JsonNode Episode(string id = Id, double duration = 2) => new JsonObject { ["uid"] = id, ["title"] = "Fixture episode", ["summary"] = "Complete public audio", ["duration"] = duration.ToString(System.Globalization.CultureInfo.InvariantCulture), ["audioUrl"] = Media(id), ["imageUrl"] = "https://megaphone.imgix.net/podcasts/fixture/image.jpg",
        ["episodeUrlHRef"] = "https://traffic.megaphone.fm/" + id + ".mp3", ["dataClipboardText"] = "https://megaphone.link/" + id };
    internal static JsonNode Response(int count = 1, int limit = 1, int page = 1, params JsonNode[] items) => new JsonObject { ["podcastTitle"] = "Fixture podcast", ["podcastFeedUrl"] = "https://feeds.megaphone.fm/fixture", ["totalCount"] = count, ["limit"] = limit, ["page"] = page, ["episodes"] = new JsonArray(items.Length == 0 ? [Episode()] : items) };
    internal static IEnumerable<(string Name, Func<Task> Test)> All => [
        ("Megaphone public episode responses retain full audio duration stable identity and literal source", Facts),
        ("Megaphone classic modern origin and short-link aliases select the same episode", Aliases),
        ("Megaphone private clips manually paged repeated and unrelated routes remain explicit", Boundaries),
        ("Megaphone conflicting requested episode media filename and share identities fail before playback", Identities),
        ("Megaphone malformed repeated unavailable and unsupported resource data remain explicit", Malformed),
        ("Megaphone complete public counts pages and canonical feeds retain original episode positions", Pagination),
        ("Megaphone changed owners counts limits repeated or incomplete pages cannot claim completion", Incomplete),
        ("Megaphone latest-episode scopes and named channel selections retain exact requested views", Scoped),
        ("Megaphone unavailable public slots retain original source numbering", Positions),
        ("Megaphone inspected podcast snapshots refresh only chosen episode playback", Snapshots),
        ("Megaphone known episode and selected-child archives skip playback discovery", Archives),
        ("Megaphone anonymous metadata isolates credentials cancellation denial and redirected identity", Transport)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var info = await Extract(calls); var media = info.Media.Single();
        Assert(info.Id == Id && info.Extractor == "megaphone.fm" && info.Uploader == "Fixture podcast" && info.DurationSeconds == 2 && info.AnonymousPlayback && media.Url.AbsoluteUri == Media() && media.IsAudioOnly && media.VerificationMinimumDurationSeconds == 2 && media.VerificationDemuxer == "mp3" && calls.Count == 1, "Megaphone source/verification facts changed.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "http://player-origin.megaphone.fm/" + Id, "https://playlist.megaphone.fm/?e=" + Id + "&light=true", "https://megaphone.link/" + Id, "https://player.megaphone.fm/playlist/episode/" + Id }) Assert((await Extract([], request: Request(Path.GetTempPath()) with { Url = new(url) })).Id == Id, "Episode alias changed stable identity.");
    }
    private static Task Boundaries()
    {
        var provider = new MegaphoneProvider(); foreach (var url in new[] { "https://player.megaphone.fm/unknown", "https://secret@player.megaphone.fm/" + Id, "https://player.megaphone.fm:444/" + Id, "https://playlist.megaphone.fm/?e=" + Id + "&e=" + Other, "https://playlist.megaphone.fm/?e=" + Id + "&p=fixture", "https://playlist.megaphone.fm/?e=" + Id + "&clip=true", "https://playlist.megaphone.fm/?p=fixture&page=2", "https://player.megaphone.fm/" + Id + "?token=private", "https://feeds.megaphone.fm/fixture", "https://traffic.megaphone.fm/" + Id + ".mp3" }) Assert(!provider.CanHandle(new(url)), "Unsupported Megaphone route was claimed.");
        var registry = YoutubeDl.Engine.Providers.MediaProviderRegistry.CreateDefault();
        Assert(registry.Resolve(Request(Path.GetTempPath()).Url).Info.Id == "megaphone", "Default registry missed Megaphone.");
        Assert(!registry.CanHandle(new("https://player.megaphone.fm/" + Id + "?clip=true")) && registry.CanHandle(new("https://feeds.megaphone.fm/fixture")), "Generic discovery bypassed the clip boundary or removed public RSS support."); return Task.CompletedTask;
    }
    private static async Task Identities()
    {
        foreach (var change in new Action<JsonNode>[] { e => e["uid"] = Other, e => e["audioUrl"] = Media(Other), e => e["dataClipboardText"] = "https://megaphone.link/" + Other, e => e["episodeUrlHRef"] = "https://traffic.megaphone.fm/" + Other + ".mp3" }) { var e = Episode(); change(e); await Expect<InvalidDataException>(() => Extract([], episode: Response(items: [e]))); }
    }
    private static async Task Malformed()
    {
        var e = Episode(); e["duration"] = null; await Expect<InvalidDataException>(() => Extract([], episode: Response(items: [e])));
        foreach (var url in new[] { "https://unrelated.example/audio.mp3", "https://chrt.fm/track/key/unrelated.example/audio.mp3", "https://traffic.megaphone.fm.evil.example/" + Id + ".mp3" }) { e = Episode(); e["audioUrl"] = url; await Expect<NotSupportedException>(() => Extract([], episode: Response(items: [e]))); }
        e = Episode(); e["audioUrl"] = null; Assert((await Extract([], episode: Response(items: [e]))).Media.Count == 0, "Unavailable audio fabricated a source.");
        using var malformed = Http([], raw: "bad JSON"); await Expect<InvalidDataException>(() => new MegaphoneProvider().ExtractAsync(Request(Path.GetTempPath()), malformed, default));
        using var duplicate = Http([], raw: "{\"totalCount\":1,\"totalCount\":2}"); await Expect<InvalidDataException>(() => new MegaphoneProvider().ExtractAsync(Request(Path.GetTempPath()), duplicate, default));
    }
    private static async Task Pagination()
    {
        var calls = new List<Uri>(); var page = await Extract(calls, request: Request(Path.GetTempPath(), true), first: Response(2, 1, 1, Episode()), second: Response(2, 1, 2, Episode(Other)));
        Assert(page.IsPlaylist && page.Entries.Count == 2 && page.Entries[1].Position == 2 && page.Entries[1].ExpectedArchiveIdentity!.Id == Other && calls.Count == 2 && calls[1].Query == "?page=2", "Complete public counts or page/source positions changed.");
    }
    private static async Task Incomplete()
    {
        foreach (var change in new Action<JsonNode>[] { p => p["totalCount"] = 3, p => p["page"] = 1, p => p["limit"] = 2, p => p["podcastFeedUrl"] = "https://feeds.megaphone.fm/other", p => p["episodes"] = new JsonArray(Episode()) }) { var second = Response(2, 1, 2, Episode(Other)); change(second); await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: Response(2, 1, 1, Episode()), second: second)); }
        var shortPage = Response(2, 2, 1, Episode()); await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: shortPage));
        var missing = Response(); ((JsonObject)missing).Remove("episodes"); await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: missing));
        await Expect<NotSupportedException>(() => Extract([], request: Request(Path.GetTempPath(), true) with { MaximumPlaylistEntries = 1 }));
    }
    private static async Task Scoped()
    {
        var page = await Extract([], request: Request(Path.GetTempPath(), true) with { Url = new("https://playlist.megaphone.fm/?p=fixture&episodes=1") }, first: Response()); Assert(page.Entries.Count == 1, "Latest-only public view ignored its declared count.");
        await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true) with { Url = new("https://playlist.megaphone.fm/?p=fixture&episodes=1") }, first: Response(2, 2, 1, Episode(), Episode(Other))));
        page = await Extract([], request: Request(Path.GetTempPath()) with { Url = new("https://cms.megaphone.fm/channel/fixture?selected=" + Other) }); Assert(page.Id == Other && !page.IsPlaylist, "Named channel selection returned another or whole inventory.");
        await Expect<NotSupportedException>(() => Extract([], request: Request(Path.GetTempPath()) with { Url = new("https://cms.megaphone.fm/channel/fixture?selected=GLT8924910688") }));
    }
    private static async Task Positions()
    { var missing = Episode(); missing["audioUrl"] = null; var page = await Extract([], request: Request(Path.GetTempPath(), true), first: Response(2, 2, 1, missing, Episode(Other))); Assert(!page.Entries[0].Available && page.Entries[1].Position == 2 && page.Entries[1].Available, "Missing audio changed original source numbering."); }
    private static async Task Snapshots()
    {
        await Temporary(async directory => { var calls = new List<Uri>(); var engine = new NativeEngine([new MegaphoneProvider()], _ => Http(calls)); var request = Request(directory, true) with { PlaylistItems = "2", SkipDownload = true, WriteInfoJson = true };
            var preview = await engine.InspectAsync(request, default); calls.Clear(); var result = await engine.DownloadAsync(request with { PlaylistSnapshot = preview.PlaylistSnapshot }, null, null, default); Assert(result.Succeeded && calls.Count == 1 && calls[0].AbsolutePath.EndsWith(Other), "Snapshot refreshed unselected metadata or another episode."); });
    }
    private static async Task Archives()
    {
        await Temporary(async directory => { var archive = Path.Combine(directory, "archive.txt"); await File.WriteAllTextAsync(archive, "megaphone " + Id + "\nmegaphone " + Other + "\n"); var calls = new List<Uri>(); var engine = new NativeEngine([new MegaphoneProvider()], _ => Http(calls));
            var result = await engine.DownloadAsync(Request(directory) with { DownloadArchiveFile = archive }, null, null, default); Assert(result.Skipped && calls.Count == 0, "Known episode archive requested discovery.");
            result = await engine.DownloadAsync(Request(directory, true) with { PlaylistItems = "2", DownloadArchiveFile = archive }, null, null, default); Assert(result.Succeeded && calls.Count == 1, "Archived child requested playback discovery."); });
    }
    private static async Task Transport()
    {
        var options = new MediaHttpOptions(); options.Headers["Authorization"] = "Bearer fixture"; options.Headers["Cookie"] = "fixture=secret"; using var http = Http([], options: options, observe: r => Assert(!r.Headers.Contains("Authorization") && !r.Headers.Contains("Cookie"), "Caller credentials reached public metadata.")); _ = await new MegaphoneProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
        using var stop = new CancellationTokenSource(); stop.Cancel(); await Expect<OperationCanceledException>(() => new MegaphoneProvider().ExtractAsync(Request(Path.GetTempPath()), http, stop.Token));
        using var denied = Http([], status: HttpStatusCode.Forbidden); await Expect<HttpRequestException>(() => new MegaphoneProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
        using var redirected = Http([], redirect: true); await Expect<InvalidDataException>(() => new MegaphoneProvider().ExtractAsync(Request(Path.GetTempPath()), redirected, default));
    }
    private static async Task<PageMetadata> Extract(List<Uri> calls, JsonNode? episode = null, NativeDownloadRequest? request = null, JsonNode? first = null, JsonNode? second = null)
    { using var http = Http(calls, episode, first, second); return await new MegaphoneProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static MediaHttp Http(List<Uri> calls, JsonNode? episode = null, JsonNode? first = null, JsonNode? second = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null,
        MediaHttpOptions? options = null, Action<HttpRequestMessage>? observe = null, string? raw = null, HttpStatusCode status = HttpStatusCode.OK, bool redirect = false) => new(new Handler(r =>
        {
            calls.Add(r.RequestUri!); observe?.Invoke(r); var uri = r.RequestUri!; if (uri.Host != "player.megaphone.fm") return media?.Invoke(r) ?? throw new InvalidOperationException("Unexpected Megaphone playback request.");
            var data = uri.AbsolutePath.Contains("/episode/") ? uri.AbsolutePath.EndsWith(Other) ? Response(items: [Episode(Other)]) : episode ?? Response() : uri.Query.StartsWith("?page=1") ? first ?? Response(2, 2, 1, Episode(), Episode(Other)) : second ?? Response(2, 2, 2);
            return new(status) { Content = new StringContent(raw ?? data.ToJsonString()), RequestMessage = redirect ? new(HttpMethod.Get, "https://other.example/data") : r };
        }), options ?? new());
    private static async Task Temporary(Func<string, Task> test) { var directory = Path.Combine(Path.GetTempPath(), "megaphone-check-" + Guid.NewGuid()); Directory.CreateDirectory(directory); try { await test(directory); } finally { Directory.Delete(directory, true); } }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
}
