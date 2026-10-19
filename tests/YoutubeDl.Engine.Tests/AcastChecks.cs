using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Acast;

internal static class AcastChecks
{
    internal const string ShowId = "65ef3e234801c000172739fd";
    internal const string EpisodeId = "65f0e03aedbd610016916b2b";
    internal const string OtherId = "6ac103548da1db2e6f12938d";
    internal const string Signed = "?Key=A%2Bb&case=C%2Fd";
    internal static string Media(string episode = EpisodeId, string show = ShowId) => "https://sphinx.acast.com/p/open/s/" + show + "/e/" + episode + "/media.mp3" + Signed;
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new("https://shows.acast.com/fixture/episodes/trailer"), OutputDirectory = directory, OutputTemplate = "acast.%(ext)s" };
    internal static JsonNode Show() => new JsonObject { ["id"] = ShowId, ["showId"] = ShowId, ["showUrl"] = "fixture", ["link"] = "https://shows.acast.com/fixture", ["title"] = "Fixture Acast show",
        ["private"] = false, ["premium"] = false, ["author"] = "Fixture author", ["count"] = 2, ["checkSum"] = "stable-revision", ["image"] = "https://assets.pippa.io/show.jpg" };
    internal static JsonNode Episode(string id = EpisodeId, string alias = "trailer", double duration = 2, bool includeShow = true) => new JsonObject { ["id"] = id, ["acastId"] = id, ["showId"] = ShowId, ["episodeUrl"] = alias,
        ["link"] = "https://shows.acast.com/fixture/episodes/" + alias, ["title"] = "Episode " + alias, ["description"] = "Complete native audio", ["duration"] = duration, ["contentLength"] = 9999999,
        ["contentType"] = "audio/mpeg", ["url"] = Media(id), ["image"] = "https://assets.pippa.io/episode.jpg", ["premium"] = false, ["show"] = includeShow ? Show() : null };
    internal static JsonNode Collection(int count = 2, params JsonNode[] episodes)
    { var show = Show(); show["count"] = count; show["episodes"] = new JsonArray(episodes.Length == 0 ? [Episode(includeShow: false), Episode(OtherId, "second", includeShow: false)] : episodes); return show; }
    internal static IEnumerable<(string Name, Func<Task> Test)> All => [
        ("Acast current episode binds show and stable ID while preserving supplied audio metadata", Facts),
        ("Acast old play and embed slug or ID aliases retain the same episode identity", Aliases),
        ("Acast URL boundaries exclude private tokens external feeds and unrelated routes", Boundaries),
        ("Acast mismatched requested episode show alias and public links fail before media", Identities),
        ("Acast private premium missing-state and malformed records remain explicit", Restrictions),
        ("Acast public enclosure origins paths and owner IDs remain bound", Resources),
        ("Acast complete show counts and episode order remain stable across pages", Pagination),
        ("Acast incomplete changed repeated or oversized show inventories cannot be complete", Incomplete),
        ("Acast unavailable premium collection positions preserve original source numbering", PremiumPositions),
        ("Acast inspected show selection retains stable children while refreshing only chosen playback", Snapshots),
        ("Acast GUID and selected-child archives skip metadata and playback discovery", Archives),
        ("Acast public show episode sessions isolate caller credentials", Anonymous),
        ("Acast cancellation denial and redirected metadata cannot request later playback", Cancellation)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var page = await Extract(calls); var media = page.Media.Single();
        Assert(page.Id == EpisodeId && page.Extractor == "acast" && page.Uploader == "Fixture author" && page.DurationSeconds == 2 && media.IsAudioOnly && media.Url.AbsoluteUri == Media() && calls.Count == 1, "Acast episode/source facts changed.");
        Assert(media.FileSize == 9999999 && media.VerificationFileSize is null && media.VerificationDurationSeconds == 2 && media.VerificationDemuxer == "mp3", "Advertised size was treated as exact dynamically delivered bytes or full decode was skipped.");
        var external = Episode(); external["link"] = "https://publisher.example/episode"; external["show"]!["link"] = "https://publisher.example/show";
        Assert((await Extract([], external)).Id == EpisodeId, "A publisher website link was incorrectly treated as the Acast route identity.");
        var imported = Episode(); imported["id"] = "tag:publisher.example,2020:episode"; imported["episodeUrl"] = null; imported["link"] = "https://shows.acast.com/fixture/episodes/" + EpisodeId;
        Assert((await Extract([], imported, Request(Path.GetTempPath()) with { Url = new("https://embed.acast.com/" + ShowId + "/" + EpisodeId) })).Id == EpisodeId, "Imported RSS GUID displaced Acast's stable episode ID.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "http://play.acast.com/s/fixture/trailer", "https://embed.acast.com/fixture/trailer", "https://embed.acast.com/" + ShowId + "/" + EpisodeId,
            "https://shows.acast.com/" + ShowId + "/episodes/" + EpisodeId }) Assert((await Extract([], request: Request(Path.GetTempPath()) with { Url = new(url) })).Id == EpisodeId, "Acast alias changed the stable episode ID.");
    }
    private static Task Boundaries()
    {
        var provider = new AcastProvider(); foreach (var url in new[] { "https://acast.com/fixture", "https://shows.acast.com/fixture/other", "https://secret@shows.acast.com/fixture", "https://shows.acast.com:444/fixture",
            "https://embed.acast.com/fixture/trailer?token=private", "https://embed.acast.com/fixture?url=https%3A%2F%2Fprivate.example%2Ffeed" }) Assert(!provider.CanHandle(new(url)), "Unsupported/private Acast URL was claimed.");
        Assert(YoutubeDl.Engine.Providers.MediaProviderRegistry.CreateDefault().Resolve(Request(Path.GetTempPath()).Url).Info.Id == "acast", "Default dispatch missed Acast."); return Task.CompletedTask;
    }
    private static async Task Identities()
    {
        foreach (var change in new Action<JsonNode>[] { e => e["showId"] = OtherId, e => e["id"] = OtherId, e => e["episodeUrl"] = "other", e => e["link"] = "https://shows.acast.com/other/episodes/trailer", e => e["show"]!["showUrl"] = "other" })
        { var episode = Episode(); change(episode); await Expect<InvalidDataException>(() => Extract([], episode)); }
    }
    private static async Task Restrictions()
    {
        foreach (var change in new Action<JsonNode>[] { e => e["premium"] = true, e => e["show"]!["private"] = true, e => e["show"]!["premium"] = true, e => ((JsonObject)e).Remove("premium") })
        { var episode = Episode(); change(episode); await Expect<NotSupportedException>(() => Extract([], episode)); }
        using var malformed = Http([], raw: "bad JSON"); await Expect<InvalidDataException>(() => new AcastProvider().ExtractAsync(Request(Path.GetTempPath()), malformed, default));
        using var duplicate = Http([], raw: Episode().ToJsonString().Replace("\"premium\":false", "\"premium\":true,\"premium\":false")); await Expect<InvalidDataException>(() => new AcastProvider().ExtractAsync(Request(Path.GetTempPath()), duplicate, default));
    }
    private static async Task Resources()
    {
        foreach (var raw in new[] { Media(OtherId), "https://unrelated.example/media.mp3", Media().Replace("/p/open/", "/p/private/") })
        { var episode = Episode(); episode["url"] = raw; await Expect<NotSupportedException>(() => Extract([], episode)); }
    }
    private static NativeDownloadRequest ShowRequest(string directory) => Request(directory) with { Url = new("https://shows.acast.com/fixture") };
    private static async Task Pagination()
    {
        var calls = new List<Uri>(); var page = await Extract(calls, request: ShowRequest(Path.GetTempPath()), first: Collection(2, Episode(includeShow: false)), second: Collection(2, Episode(OtherId, "second", includeShow: false)));
        Assert(page.IsPlaylist && page.Entries.Count == 2 && page.Entries[1].Position == 2 && page.Entries[1].ExpectedArchiveIdentity!.Id == OtherId && calls.Count == 2 && calls[1].Query == "?limit=100&from=1", "Complete inventory count/order/canonical paging changed.");
    }
    private static async Task Incomplete()
    {
        var empty = Collection(); empty["episodes"] = new JsonArray();
        await Expect<InvalidDataException>(() => Extract([], request: ShowRequest(Path.GetTempPath()), first: Collection(2, Episode(includeShow: false)), second: empty));
        await Expect<InvalidDataException>(() => Extract([], request: ShowRequest(Path.GetTempPath()), first: Collection(2, Episode(includeShow: false)), second: Collection(3, Episode(OtherId, "second", includeShow: false))));
        await Expect<InvalidDataException>(() => Extract([], request: ShowRequest(Path.GetTempPath()), first: Collection(2, Episode(includeShow: false), Episode(includeShow: false))));
        await Expect<NotSupportedException>(() => Extract([], request: ShowRequest(Path.GetTempPath()) with { MaximumPlaylistEntries = 1 }, first: Collection()));
    }
    private static async Task PremiumPositions()
    {
        var premium = Episode(includeShow: false); premium["premium"] = true; var page = await Extract([], request: ShowRequest(Path.GetTempPath()), first: Collection(2, premium, Episode(OtherId, "second", includeShow: false)));
        Assert(!page.Entries[0].Available && page.Entries[1].Position == 2 && page.Entries[1].Available, "Premium slots were removed/replaced instead of keeping source positions.");
    }
    private static async Task Snapshots()
    {
        var directory = Path.Combine(Path.GetTempPath(), "acast-snapshot-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try { var calls = new List<Uri>(); var engine = new NativeEngine([new AcastProvider()], _ => Http(calls)); var request = ShowRequest(directory) with { PlaylistItems = "2", SkipDownload = true, WriteInfoJson = true };
            var preview = await engine.InspectAsync(request, default); calls.Clear(); var result = await engine.DownloadAsync(request with { PlaylistSnapshot = preview.PlaylistSnapshot }, null, null, default);
            Assert(result.Succeeded && calls.Count == 1 && calls[0].AbsolutePath.Contains(OtherId), "Snapshot refreshed unselected show/episode data."); }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task Archives()
    {
        var directory = Path.Combine(Path.GetTempPath(), "acast-archive-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try { var archive = Path.Combine(directory, "archive.txt"); await File.WriteAllTextAsync(archive, "acast " + EpisodeId + "\nacast " + OtherId + "\n"); var calls = new List<Uri>(); var engine = new NativeEngine([new AcastProvider()], _ => Http(calls));
            var direct = await engine.DownloadAsync(Request(directory) with { Url = new("https://embed.acast.com/" + ShowId + "/" + EpisodeId), DownloadArchiveFile = archive }, null, null, default); Assert(direct.Succeeded && direct.Skipped && calls.Count == 0, "GUID archive requested metadata.");
            var show = await engine.DownloadAsync(ShowRequest(directory) with { PlaylistItems = "2", DownloadArchiveFile = archive }, null, null, default); Assert(show.Succeeded && calls.Count == 1, "Archived selected child requested playback discovery."); }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task Anonymous()
    {
        var options = new MediaHttpOptions(); options.Headers["Authorization"] = "Bearer fixture"; options.Headers["Cookie"] = "fixture=secret"; using var http = Http([], options: options,
            observe: r => Assert(!r.Headers.Contains("Authorization") && !r.Headers.Contains("Cookie"), "Caller credentials reached Acast public metadata.")); _ = await new AcastProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
    }
    private static async Task Cancellation()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel(); using var http = Http([]); await Expect<OperationCanceledException>(() => new AcastProvider().ExtractAsync(Request(Path.GetTempPath()), http, stop.Token));
        using var denied = Http([], status: HttpStatusCode.Forbidden); await Expect<HttpRequestException>(() => new AcastProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
        using var redirected = Http([], redirect: true); await Expect<InvalidDataException>(() => new AcastProvider().ExtractAsync(Request(Path.GetTempPath()), redirected, default));
    }
    private static async Task<PageMetadata> Extract(List<Uri> calls, JsonNode? episode = null, NativeDownloadRequest? request = null, JsonNode? first = null, JsonNode? second = null)
    { using var http = Http(calls, episode, first, second); return await new AcastProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static MediaHttp Http(List<Uri> calls, JsonNode? episode = null, JsonNode? first = null, JsonNode? second = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null,
        MediaHttpOptions? options = null, Action<HttpRequestMessage>? observe = null, string? raw = null, HttpStatusCode status = HttpStatusCode.OK, bool redirect = false) => new(new Handler(r =>
    { calls.Add(r.RequestUri!); observe?.Invoke(r); var uri = r.RequestUri!;
        if (uri.Host == "phoenix.prod.ateam.acast.cloud") { var data = uri.AbsolutePath.Contains("/episodes/") ? uri.AbsolutePath.EndsWith(OtherId) ? Episode(OtherId, "second") : episode ?? Episode() : uri.Query.EndsWith("from=0") ? first ?? Collection() : second ?? Collection(2, Episode(OtherId, "second", includeShow: false));
            return new(status) { Content = new StringContent(raw ?? data.ToJsonString()), RequestMessage = redirect ? new(HttpMethod.Get, "https://other.example/metadata") : r }; }
        return media?.Invoke(r) ?? throw new InvalidOperationException("Unexpected Acast media request."); }), options ?? new());
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
}
