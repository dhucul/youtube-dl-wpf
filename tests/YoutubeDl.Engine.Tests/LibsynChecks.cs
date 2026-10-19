using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Libsyn;

internal static class LibsynChecks
{
    internal const string Id = "22065683", Other = "22065684", ShowId = "397451", Destination = "3270797", Signed = "?literal=A%2Bb&case=C%2Fd";
    internal static string Media(string id = Id, bool video = false) => "https://pscrb.fm/rss/p/traffic.libsyn.com/fixture/" + id + (video ? ".mp4" : ".mp3") + Signed;
    internal static NativeDownloadRequest Request(string directory, bool destination = false) => new() { Url = new("https://play.libsyn.com/embed/" + (destination ? "destination/id/" + Destination : "episode/id/" + Id)), OutputDirectory = directory, OutputTemplate = "libsyn.%(ext)s" };
    internal static JsonNode Show() => new JsonObject { ["show_id"] = long.Parse(ShowId), ["show_title"] = "Fixture show", ["feed_url"] = "http://feeds.libsyn.com/" + ShowId + "/rss" };
    internal static JsonNode Episode(string id = Id, double duration = 2, bool video = false) => new JsonObject { ["item_id"] = long.Parse(id), ["_item"] = new JsonObject { ["item_id"] = long.Parse(id), ["item_title"] = "Fixture episode", ["item_body"] = "Public native media", ["show_id"] = long.Parse(ShowId), ["premium_state"] = "auto", ["_show"] = Show(),
        ["_primary_content"] = new JsonObject { ["content_id"] = 120834416, ["duration"] = duration, ["_url"] = Media(id, video), ["_download_url"] = Media(id, video), ["_format"] = new JsonObject { ["file_class"] = video ? "video" : "audio" } } } };
    internal static JsonNode List(params JsonNode[] items) => new JsonObject { ["destination_id"] = Destination, ["_destination"] = new JsonObject { ["destination_id"] = Destination, ["_show"] = Show() }, ["_items"] = new JsonArray(items) };
    internal static string Html(string id = Id) => "<meta property='og:title' content='Fixture episode'><iframe src='//html5-player.libsyn.com/embed/episode/id/" + id + "/tdest_id/" + Destination + "'></iframe>";
    internal static IEnumerable<(string Name, Func<Task> Test)> All => [
        ("Libsyn public player records bind item show content IDs and full audio facts", Facts),
        ("Libsyn current old player aliases and display paths retain the same stable episode", Aliases),
        ("Libsyn hosted site and old subdomain pages dispatch one identified episode player", Websites),
        ("Libsyn private filtered repeated cache-changing and unrelated routes remain explicit", Boundaries),
        ("Libsyn conflicting requested item show feed and content identities fail before media", Identities),
        ("Libsyn premium malformed duplicate and unsupported media records remain explicit", Restrictions),
        ("Libsyn literal tracking URLs and destination tags preserve nested source and query bytes", Resources),
        ("Libsyn complete destination pages retain stable owners original positions and terminal anchors", Pagination),
        ("Libsyn repeated changed missing oversized or unbounded destinations cannot claim completion", Incomplete),
        ("Libsyn unavailable destination slots retain original source numbering", Positions),
        ("Libsyn inspected destination snapshots refresh only selected episode playback", Snapshots),
        ("Libsyn known item and selected-child archives skip playback discovery", Archives),
        ("Libsyn removed media retains metadata without publishing files or false archive success", Removed),
        ("Libsyn anonymous metadata isolates credentials cancellation denial and redirected identities", Transport)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var page = await Extract(calls); var media = page.Media.Single();
        Assert(page.Id == Id && page.Uploader == "Fixture show" && page.DurationSeconds == 2 && page.AnonymousPlayback && media.Url.AbsoluteUri == Media() && media.IsAudioOnly && media.IsCompleteMedia && media.VerificationMinimumDurationSeconds == 2 && media.VerificationDemuxer == "mp3" && calls.Count == 1, "Libsyn source facts changed.");
        var minimal = Episode(); ((JsonObject)minimal["_item"]!["_show"]!).Remove("feed_url"); Assert((await Extract([], minimal)).Id == Id, "An older reduced show record discarded its explicit owner identity.");
        var video = (await Extract([], Episode(video: true))).Media.Single(); Assert(video.HasVideo == true && video.HasAudio is null && video.VerificationDurationSeconds == 2 && video.VerificationDemuxer == "mov", "Libsyn video invented dimensions/audio or omitted full decoding.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "http://html5-player.libsyn.com/embed/episode/id/" + Id, "https://play.libsyn.com/embed/episode/id/" + Id + "/height/90/theme/custom/thumbnail/yes/direction/forward/download/no/time-start/00:00:01/" }) Assert((await Extract([], request: Request(Path.GetTempPath()) with { Url = new(url) })).Id == Id, "Player alias changed identity.");
    }
    private static async Task Websites()
    {
        foreach (var url in new[] { "https://sites.libsyn.com/397451/trailer", "http://sites.libsyn.com/397451/website/trailer", "https://fixture.libsyn.com/trailer" })
        { var calls = new List<Uri>(); var page = await Extract(calls, request: Request(Path.GetTempPath()) with { Url = new(url) }); Assert(page.Id == Id && calls.Count == 2 && page.Media.Single().Url.Query == Signed + "&dest-id=" + Destination, "Hosted page lost its identified player or tracking context."); }
        await Expect<NotSupportedException>(() => Extract([], html: Html() + Html(Other), request: Request(Path.GetTempPath()) with { Url = new("https://fixture.libsyn.com/trailer") }));
        await Expect<NotSupportedException>(() => Extract([], html: "<!--" + Html() + "-->", request: Request(Path.GetTempPath()) with { Url = new("https://fixture.libsyn.com/trailer") }));
    }
    private static Task Boundaries()
    {
        var provider = new LibsynProvider(); foreach (var url in new[] { "https://secret@play.libsyn.com/embed/episode/id/" + Id, "https://play.libsyn.com:444/embed/episode/id/" + Id, "https://play.libsyn.com/embed/episode/id/" + Id + "/cache/purge", "https://play.libsyn.com/embed/destination/id/" + Destination + "/category/private", "https://play.libsyn.com/embed/episode/id/" + Id + "/tdest_id/1/tdest_id/2", "https://play.libsyn.com/embed/episode/id/" + Id + "?token=private", "https://sites.libsyn.com/397451", "https://traffic.libsyn.com/fixture" }) Assert(!provider.CanHandle(new(url)), "Unsupported Libsyn route was claimed.");
        Assert(YoutubeDl.Engine.Providers.MediaProviderRegistry.CreateDefault().Resolve(Request(Path.GetTempPath()).Url).Info.Id == "libsyn", "Default registry missed Libsyn."); return Task.CompletedTask;
    }
    private static async Task Identities()
    {
        foreach (var change in new Action<JsonNode>[] { e => e["item_id"] = long.Parse(Other), e => e["_item"]!["item_id"] = long.Parse(Other), e => e["_item"]!["show_id"] = 123, e => e["_item"]!["_show"]!["feed_url"] = "http://feeds.libsyn.com/123/rss", e => e["_item"]!["_primary_content"]!["content_id"] = 0 }) { var e = Episode(); change(e); await Expect<InvalidDataException>(() => Extract([], e)); }
    }
    private static async Task Restrictions()
    {
        var premium = Episode(); premium["_item"]!["premium_state"] = "premium"; await Expect<NotSupportedException>(() => Extract([], premium));
        var document = Episode(); document["_item"]!["_primary_content"]!["_format"]!["file_class"] = "document"; await Expect<NotSupportedException>(() => Extract([], document));
        var missing = Episode(); missing["_item"]!["_primary_content"]!["duration"] = null; await Expect<InvalidDataException>(() => Extract([], missing));
        using var malformed = Http([], raw: "bad JSON"); await Expect<InvalidDataException>(() => new LibsynProvider().ExtractAsync(Request(Path.GetTempPath()), malformed, default));
        using var duplicate = Http([], raw: "{\"item_id\":1,\"item_id\":2}"); await Expect<InvalidDataException>(() => new LibsynProvider().ExtractAsync(Request(Path.GetTempPath()), duplicate, default));
    }
    private static async Task Resources()
    {
        var page = await Extract([], request: Request(Path.GetTempPath()) with { Url = new("https://play.libsyn.com/embed/episode/id/" + Id + "/tdest_id/" + Destination) }); Assert(page.Media.Single().Url.Query == Signed + "&dest-id=" + Destination, "Tracking context rewrote literal source queries.");
        foreach (var url in new[] { "https://other.example/file.mp3", "https://pscrb.fm/rss/p/other.example/file.mp3", "https://traffic.libsyn.com.evil.example/file.mp3" }) { var e = Episode(); e["_item"]!["_primary_content"]!["_url"] = url; await Expect<NotSupportedException>(() => Extract([], e)); }
        var nested = Episode(); nested["_item"]!["_primary_content"]!["_url"] = "https://dts.podtrac.com/redirect.mp3/pscrb.fm/rss/p/traffic.libsyn.com/fixture.mp3" + Signed; Assert((await Extract([], nested)).Media.Single().Url.AbsoluteUri.EndsWith(Signed), "Nested tracker URLs were stripped or replaced.");
    }
    private static JsonNode[] Hundred() => Enumerable.Range(0, 100).Select(index => Episode((long.Parse(Id) + index).ToString())).ToArray();
    private static async Task Pagination()
    {
        var calls = new List<Uri>(); var page = await Extract(calls, request: Request(Path.GetTempPath(), true), first: List(Hundred()), second: List(Episode("22065783")));
        Assert(page.IsPlaylist && page.Entries.Count == 101 && page.Entries[^1].Position == 101 && calls.Count == 3 && calls[1].Query == "?page=2" && calls[^1].Query == "?page=1", "Complete public pagination lost source indexes, short terminal page or initial anchor.");
    }
    private static async Task Incomplete()
    {
        await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: List(Hundred()), second: List(Episode())));
        var owner = List(Episode("22065783")); owner["_destination"]!["_show"]!["show_id"] = 123; await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: List(Hundred()), second: owner));
        var missing = List(); ((JsonObject)missing).Remove("_items"); await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: missing));
        await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), anchor: List(Episode(Other), Episode())));
        await Expect<NotSupportedException>(() => Extract([], request: Request(Path.GetTempPath(), true) with { MaximumPlaylistEntries = 1 }));
        var oversized = List(Enumerable.Range(0, 101).Select(_ => Episode()).ToArray()); await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: oversized));
    }
    private static async Task Positions()
    { var unavailable = Episode(); unavailable["_item"]!["_primary_content"] = null; var page = await Extract([], request: Request(Path.GetTempPath(), true), first: List(unavailable, Episode(Other))); Assert(!page.Entries[0].Available && page.Entries[1].Position == 2 && page.Entries[1].Available, "Unavailable source slots changed original numbering."); }
    private static async Task Snapshots()
    {
        await Temporary(async directory => { var calls = new List<Uri>(); var engine = new NativeEngine([new LibsynProvider()], _ => Http(calls)); var request = Request(directory, true) with { PlaylistItems = "2", SkipDownload = true, WriteInfoJson = true };
            var preview = await engine.InspectAsync(request, default); calls.Clear(); var result = await engine.DownloadAsync(request with { PlaylistSnapshot = preview.PlaylistSnapshot }, null, null, default); Assert(result.Succeeded && calls.Count == 1 && calls[0].AbsolutePath.EndsWith(Other), "Snapshot refreshed unselected list or episodes."); });
    }
    private static async Task Archives()
    {
        await Temporary(async directory => { var archive = Path.Combine(directory, "archive.txt"); await File.WriteAllTextAsync(archive, "libsyn " + Id + "\nlibsyn " + Other + "\n"); var calls = new List<Uri>(); var engine = new NativeEngine([new LibsynProvider()], _ => Http(calls));
            var result = await engine.DownloadAsync(Request(directory) with { DownloadArchiveFile = archive }, null, null, default); Assert(result.Skipped && calls.Count == 0, "Item archive requested metadata.");
            result = await engine.DownloadAsync(Request(directory, true) with { PlaylistItems = "2", DownloadArchiveFile = archive }, null, null, default); Assert(result.Succeeded && calls.Count == 2, "Archived selected child requested playback discovery."); });
    }
    private static async Task Removed()
    {
        var e = Episode(); e["_item"]!["_primary_content"] = null; var page = await Extract([], e); Assert(page.Media.Count == 0 && page.Warnings.Count == 1, "Removed media fabricated playback or discarded public metadata.");
        await Temporary(async directory => { var engine = new NativeEngine([new LibsynProvider()], _ => Http([], e)); var archive = Path.Combine(directory, "archive.txt"); var request = Request(directory) with { SkipDownload = true, IgnoreNoFormatsError = true, WriteInfoJson = true, DownloadArchiveFile = archive };
            var result = await engine.DownloadAsync(request, null, null, default); Assert(result.Succeeded && result.SidecarPaths.Count == 1 && !File.Exists(archive), "Metadata-only removed episode recorded false media success."); });
    }
    private static async Task Transport()
    {
        var options = new MediaHttpOptions(); options.Headers["Authorization"] = "Bearer fixture"; options.Headers["Cookie"] = "fixture=secret"; using var http = Http([], options: options, observe: r => Assert(!r.Headers.Contains("Authorization") && !r.Headers.Contains("Cookie"), "Caller credentials reached public metadata.")); _ = await new LibsynProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
        using var stop = new CancellationTokenSource(); stop.Cancel(); await Expect<OperationCanceledException>(() => new LibsynProvider().ExtractAsync(Request(Path.GetTempPath()), http, stop.Token));
        using var denied = Http([], status: HttpStatusCode.Forbidden); await Expect<HttpRequestException>(() => new LibsynProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
        using var redirected = Http([], redirect: true); await Expect<InvalidDataException>(() => new LibsynProvider().ExtractAsync(Request(Path.GetTempPath()), redirected, default));
    }
    private static async Task<PageMetadata> Extract(List<Uri> calls, JsonNode? episode = null, NativeDownloadRequest? request = null, JsonNode? first = null, JsonNode? second = null, JsonNode? anchor = null, string? html = null)
    { using var http = Http(calls, episode, first, second, anchor, html); return await new LibsynProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static MediaHttp Http(List<Uri> calls, JsonNode? episode = null, JsonNode? first = null, JsonNode? second = null, JsonNode? anchor = null, string? html = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null,
        MediaHttpOptions? options = null, Action<HttpRequestMessage>? observe = null, string? raw = null, HttpStatusCode status = HttpStatusCode.OK, bool redirect = false)
    {
        var firstRequests = 0;
        return new(new Handler(r => { calls.Add(r.RequestUri!); observe?.Invoke(r); var uri = r.RequestUri!; string text;
            if (uri.Host == "html5-player.libsyn.com")
            { JsonNode data; if (uri.AbsolutePath.Contains("/episode/")) data = uri.AbsolutePath.EndsWith(Other) ? Episode(Other) : episode ?? Episode();
                else if (uri.Query == "?page=1") data = ++firstRequests == 1 ? first ?? List(Episode(), Episode(Other)) : anchor ?? first ?? List(Episode(), Episode(Other));
                else if (uri.Query == "?page=2") data = second ?? List(); else data = List(); text = raw ?? data.ToJsonString(); }
            else if (uri.Host is "fixture.libsyn.com" or "sites.libsyn.com") text = html ?? Html();
            else return media?.Invoke(r) ?? throw new InvalidOperationException("Unexpected Libsyn playback request.");
            return new(status) { Content = new StringContent(text), RequestMessage = redirect ? new(HttpMethod.Get, "https://other.example/data") : r };
        }), options ?? new());
    }
    private static async Task Temporary(Func<string, Task> test) { var directory = Path.Combine(Path.GetTempPath(), "libsyn-check-" + Guid.NewGuid()); Directory.CreateDirectory(directory); try { await test(directory); } finally { Directory.Delete(directory, true); } }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
}
