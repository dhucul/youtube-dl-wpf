using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Simplecast;

internal static class SimplecastChecks
{
    internal const string Id = "b2be5ffe-1122-4702-af9b-9bbec8f81fce", Other = "b2be5ffe-1122-4702-af9b-9bbec8f81fcf", PodcastId = "177a5eda-53fb-46d0-b550-90c36fe4bc9b", Signed = "?literal=A%2Bb&case=C%2Fd";
    internal static string Media(string id = Id, bool dynamic = false) => (dynamic ? "https://sxm.simplecastaudio.com/" : "https://cdn.simplecast.com/audio/") + PodcastId + "/episodes/" + id + "/audio/ba2931d7-3559-45bd-93bb-c797f6d2c341/default_tc.mp3" + Signed;
    internal static NativeDownloadRequest Request(string directory, bool podcast = false) => new() { Url = new("https://api.simplecast.com/" + (podcast ? "podcasts/" + PodcastId : "episodes/" + Id)), OutputDirectory = directory, OutputTemplate = "simplecast.%(ext)s" };
    internal static JsonNode Podcast() => new JsonObject { ["id"] = PodcastId, ["href"] = "https://api.simplecast.com/podcasts/" + PodcastId, ["title"] = "Fixture podcast", ["status"] = "published", ["is_private"] = false };
    internal static JsonNode Episode(string id = Id, double duration = 2, bool dynamic = false) => new JsonObject { ["id"] = id, ["href"] = "https://api.simplecast.com/episodes/" + id, ["podcast"] = Podcast(), ["title"] = "Fixture episode", ["slug"] = "trailer", ["status"] = "published", ["is_published"] = true, ["is_hidden"] = false,
        ["audio_status"] = "transcoded", ["duration"] = duration, ["audio_content_type"] = "audio/mpeg", ["audio_file_url"] = Media(id, dynamic), ["audio_file_size"] = 99999999, ["audio_file"] = new JsonObject { ["url"] = Media(id, dynamic), ["size"] = 99999999 } };
    internal static JsonNode Site() => new JsonObject { ["url"] = "https://fixture.simplecast.com", ["site_enabled"] = true, ["podcast"] = Podcast() };
    internal static string Next(int offset = 100) => "https://api.simplecast.com/podcasts/" + PodcastId + "/episodes?limit=100&offset=" + offset + "&private=false&sort=latest&status=published";
    internal static JsonNode List(int count = 2, int current = 1, string? next = null, params JsonNode[] episodes) => new JsonObject { ["href"] = "https://api.simplecast.com/podcasts/" + PodcastId + "/episodes?status=published", ["count"] = count, ["collection"] = new JsonArray(episodes.Length == 0 ? [Episode(), Episode(Other)] : episodes),
        ["pages"] = new JsonObject { ["current"] = current, ["total"] = Math.Max(1, (count + 99) / 100), ["limit"] = 100, ["next"] = next is null ? null : new JsonObject { ["href"] = next } } };
    internal static IEnumerable<(string Name, Func<Task> Test)> All => [
        ("Simplecast public episode metadata binds podcast and full supplied MP3 facts", Facts),
        ("Simplecast UUID API and player aliases retain stable episodes and podcast inventories", Aliases),
        ("Simplecast website searches bind exact public site podcast and episode slug", Websites),
        ("Simplecast private filtered ambiguous and unrelated URL routes remain explicit", Boundaries),
        ("Simplecast conflicting requested episode podcast href and source identities fail before media", Identities),
        ("Simplecast private hidden unpublished processing malformed and duplicate states fail", Restrictions),
        ("Simplecast complete public podcast pages preserve count source positions and scoped continuations", Pagination),
        ("Simplecast changed repeated premature oversized or redirected pages cannot claim complete inventory", Incomplete),
        ("Simplecast unavailable inventory slots preserve original source numbering", Positions),
        ("Simplecast inspected podcast snapshots refresh only selected child metadata", Snapshots),
        ("Simplecast known UUID and selected-child archives skip playback discovery", Archives),
        ("Simplecast anonymous searches isolate credentials and preserve cancellation denial redirect boundaries", Transport)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var page = await Extract(calls); var media = page.Media.Single();
        Assert(page.Id == Id && page.Uploader == "Fixture podcast" && page.DurationSeconds == 2 && page.AnonymousPlayback && media.Url.AbsoluteUri == Media() && media.IsAudioOnly && media.IsCompleteMedia && media.VerificationDurationSeconds == 2 && media.VerificationDemuxer == "mp3" && media.VerificationFileSize is null && calls.Count == 2, "Simplecast source/owner/verification facts changed.");
        var dynamic = (await Extract([], episode: Episode(dynamic: true))).Media.Single();
        Assert(dynamic.VerificationDurationSeconds is null && dynamic.VerificationMinimumDurationSeconds == 2 && dynamic.VerificationDemuxer == "mp3", "Dynamic audio omitted its complete program lower bound or full decoding.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "http://api.simplecast.com/episodes/" + Id, "https://player.simplecast.com/" + Id + "?dark=true&hide_share=true#FF0000" }) Assert((await Extract([], request: Request(Path.GetTempPath()) with { Url = new(url) })).Id == Id, "Episode alias changed identity.");
        Assert((await Extract([], request: Request(Path.GetTempPath(), true) with { Url = new("https://player.simplecast.com/" + PodcastId + "?show=true") })).Entries.Count == 2, "Show player changed complete public inventory.");
    }
    private static async Task Websites()
    {
        var calls = new List<Uri>(); var page = await Extract(calls, request: Request(Path.GetTempPath()) with { Url = new("https://fixture.simplecast.com/episodes/trailer?share=true") });
        Assert(page.Id == Id && calls.Count == 3 && calls[0].AbsolutePath == "/sites/search" && calls[1].AbsolutePath == "/episodes/search", "Website identity searches changed.");
        Assert((await Extract([], request: Request(Path.GetTempPath(), true) with { Url = new("http://fixture.simplecast.com/") })).Entries.Count == 2, "Website public podcast enumeration failed.");
        var site = Site(); site["url"] = "https://other.simplecast.com"; await Expect<InvalidDataException>(() => Extract([], site: site, request: Request(Path.GetTempPath()) with { Url = new("https://fixture.simplecast.com/episodes/trailer") }));
        var episode = Episode(); episode["slug"] = "other"; await Expect<InvalidDataException>(() => Extract([], episode: episode, request: Request(Path.GetTempPath()) with { Url = new("https://fixture.simplecast.com/episodes/trailer") }));
    }
    private static Task Boundaries()
    {
        var provider = new SimplecastProvider(); foreach (var url in new[] { "https://player.simplecast.com/unknown", "https://secret@player.simplecast.com/" + Id, "https://player.simplecast.com:444/" + Id, "https://player.simplecast.com/" + PodcastId + "?show=false", "https://player.simplecast.com/" + Id + "?token=private",
            "https://api.simplecast.com/episodes/" + Id + "?show=true", "https://player.simplecast.com/" + Id + "?dark=true&dark=false", "https://www.simplecast.com/episodes/trailer", "https://fixture.simplecast.com/?offset=100", "https://other.fixture.simplecast.com/" }) Assert(!provider.CanHandle(new(url)), "Unsupported Simplecast route was claimed.");
        Assert(YoutubeDl.Engine.Providers.MediaProviderRegistry.CreateDefault().Resolve(Request(Path.GetTempPath()).Url).Info.Id == "simplecast", "Default registry missed Simplecast."); return Task.CompletedTask;
    }
    private static async Task Identities()
    {
        foreach (var change in new Action<JsonNode>[] { e => e["id"] = Other, e => e["href"] = "https://api.simplecast.com/episodes/" + Other, e => e["podcast"]!["href"] = "https://api.simplecast.com/podcasts/" + Other, e => e["audio_file"]!["url"] = Media(Other) })
        { var e = Episode(); change(e); await Expect<InvalidDataException>(() => Extract([], episode: e)); }
        foreach (var url in new[] { Media(Other), Media().Replace(PodcastId, Other), "https://unrelated.example/audio.mp3" }) { var e = Episode(); e["audio_file_url"] = url; e["audio_file"]!["url"] = url; await Expect<NotSupportedException>(() => Extract([], episode: e)); }
    }
    private static async Task Restrictions()
    {
        foreach (var change in new Action<JsonNode>[] { e => e["status"] = "draft", e => e["is_published"] = false, e => e["is_hidden"] = true, e => e["audio_status"] = "processing", e => e["audio_content_type"] = "video/mp4" }) { var e = Episode(); change(e); await Expect<NotSupportedException>(() => Extract([], episode: e)); }
        var podcast = Podcast(); podcast["is_private"] = true; await Expect<NotSupportedException>(() => Extract([], podcast: podcast));
        var bad = Episode(); bad["is_hidden"] = "false"; await Expect<InvalidDataException>(() => Extract([], episode: bad));
        using var malformed = Http([], raw: "bad JSON"); await Expect<InvalidDataException>(() => new SimplecastProvider().ExtractAsync(Request(Path.GetTempPath()), malformed, default));
        using var duplicate = Http([], raw: "{\"id\":\"one\",\"id\":\"two\"}"); await Expect<InvalidDataException>(() => new SimplecastProvider().ExtractAsync(Request(Path.GetTempPath()), duplicate, default));
    }
    private static JsonNode[] Hundred() => Enumerable.Range(1, 100).Select(index => Episode(new Guid(index, 0x1122, 0x4702, [0xaf, 0x9b, 0x9b, 0xbe, 0xc8, 0xf8, 0x1f, 0xce]).ToString())).ToArray();
    private static async Task Pagination()
    {
        var calls = new List<Uri>(); var page = await Extract(calls, request: Request(Path.GetTempPath(), true), first: List(101, 1, Next(), Hundred()), second: List(101, 2, null, Episode(Other)));
        Assert(page.Entries.Count == 101 && page.Entries[^1].Position == 101 && page.Entries[^1].ExpectedArchiveIdentity!.Id == Other && calls.Count == 3 && calls[^1].AbsoluteUri == Next(), "Complete scoped podcast pages lost count or source positions.");
    }
    private static async Task Incomplete()
    {
        foreach (var next in new[] { Next(1), Next().Replace(PodcastId, Other), Next().Replace("private=false", "private=true"), Next() + "&token=secret", Next().Replace("published", "draft") }) await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: List(101, 1, next, Hundred())));
        await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: List(101, 1, null, Hundred())));
        await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: List(101, 1, Next(), Hundred()), second: List(102, 2, null, Episode(Other))));
        await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: List(2, 1, null, Episode(), Episode())));
        var missing = List(); ((JsonObject)missing["pages"]!).Remove("next"); await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: missing));
        var foreign = List(); foreign["href"] = "https://api.simplecast.com/podcasts/" + Other + "/episodes"; await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: foreign));
        await Expect<NotSupportedException>(() => Extract([], request: Request(Path.GetTempPath(), true) with { MaximumPlaylistEntries = 1 }));
    }
    private static async Task Positions()
    { var hidden = Episode(); hidden["is_hidden"] = true; var page = await Extract([], request: Request(Path.GetTempPath(), true), first: List(2, 1, null, hidden, Episode(Other))); Assert(!page.Entries[0].Available && page.Entries[1].Position == 2 && page.Entries[1].Available, "Hidden inventory positions changed source numbering."); }
    private static async Task Snapshots()
    {
        await Temporary(async directory => { var calls = new List<Uri>(); var engine = new NativeEngine([new SimplecastProvider()], _ => Http(calls)); var request = Request(directory, true) with { PlaylistItems = "2", SkipDownload = true, WriteInfoJson = true };
            var preview = await engine.InspectAsync(request, default); calls.Clear(); var result = await engine.DownloadAsync(request with { PlaylistSnapshot = preview.PlaylistSnapshot }, null, null, default); Assert(result.Succeeded && calls.Count == 2 && calls[0].AbsolutePath.EndsWith(Other), "Snapshot refreshed unselected list or playback."); });
    }
    private static async Task Archives()
    {
        await Temporary(async directory => { var archive = Path.Combine(directory, "archive.txt"); await File.WriteAllTextAsync(archive, "simplecast " + Id + "\nsimplecast " + Other + "\n"); var calls = new List<Uri>(); var engine = new NativeEngine([new SimplecastProvider()], _ => Http(calls));
            var result = await engine.DownloadAsync(Request(directory) with { DownloadArchiveFile = archive }, null, null, default); Assert(result.Skipped && calls.Count == 0, "UUID archive requested metadata.");
            result = await engine.DownloadAsync(Request(directory, true) with { PlaylistItems = "2", DownloadArchiveFile = archive }, null, null, default); Assert(result.Succeeded && calls.Count == 2, "Archived child requested playback discovery."); });
    }
    private static async Task Transport()
    {
        var options = new MediaHttpOptions(); options.Headers["Authorization"] = "Bearer fixture"; options.Headers["Cookie"] = "fixture=secret"; using var http = Http([], options: options,
            observe: r => Assert(!r.Headers.Contains("Authorization") && !r.Headers.Contains("Cookie"), "Caller credentials reached public searches.")); _ = await new SimplecastProvider().ExtractAsync(Request(Path.GetTempPath()) with { Url = new("https://fixture.simplecast.com/episodes/trailer") }, http, default);
        using var stop = new CancellationTokenSource(); stop.Cancel(); await Expect<OperationCanceledException>(() => new SimplecastProvider().ExtractAsync(Request(Path.GetTempPath()), http, stop.Token));
        using var denied = Http([], status: HttpStatusCode.Forbidden); await Expect<HttpRequestException>(() => new SimplecastProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
        using var redirected = Http([], redirect: true); await Expect<InvalidDataException>(() => new SimplecastProvider().ExtractAsync(Request(Path.GetTempPath()) with { Url = new("https://fixture.simplecast.com/episodes/trailer") }, redirected, default));
    }
    private static async Task<PageMetadata> Extract(List<Uri> calls, JsonNode? episode = null, JsonNode? podcast = null, JsonNode? site = null, NativeDownloadRequest? request = null, JsonNode? first = null, JsonNode? second = null)
    { using var http = Http(calls, episode, podcast, site, first, second); return await new SimplecastProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static MediaHttp Http(List<Uri> calls, JsonNode? episode = null, JsonNode? podcast = null, JsonNode? site = null, JsonNode? first = null, JsonNode? second = null,
        Func<HttpRequestMessage, HttpResponseMessage>? media = null, MediaHttpOptions? options = null, Action<HttpRequestMessage>? observe = null, string? raw = null, HttpStatusCode status = HttpStatusCode.OK, bool redirect = false) => new(new Handler(async r =>
        {
            calls.Add(r.RequestUri!); observe?.Invoke(r); var uri = r.RequestUri!; if (uri.Host != "api.simplecast.com") return media?.Invoke(r) ?? throw new InvalidOperationException("Unexpected Simplecast media request.");
            JsonNode data;
            if (uri.AbsolutePath == "/sites/search" || uri.AbsolutePath == "/episodes/search")
            { Assert(r.Method == HttpMethod.Post, "Read-only search did not use its documented request."); var body = JsonNode.Parse(await r.Content!.ReadAsStringAsync())!; Assert(body["url"]!.GetValue<string>() == (uri.AbsolutePath == "/sites/search" ? "https://fixture.simplecast.com/" : "https://fixture.simplecast.com/episodes/trailer"), "Search did not bind the exact normalized public URL."); data = uri.AbsolutePath == "/sites/search" ? site ?? Site() : episode ?? Episode(); }
            else if (uri.AbsolutePath.StartsWith("/episodes/")) data = uri.AbsolutePath.EndsWith(Other) ? Episode(Other) : episode ?? Episode();
            else if (uri.AbsolutePath.EndsWith("/episodes")) data = uri.Query.Contains("offset=100") ? second ?? List() : first ?? List();
            else data = podcast ?? Podcast();
            return new(status) { Content = new StringContent(raw ?? data.ToJsonString()), RequestMessage = redirect ? new(HttpMethod.Get, "https://other.example/data") : r };
        }), options ?? new());
    private static async Task Temporary(Func<string, Task> test) { var directory = Path.Combine(Path.GetTempPath(), "simplecast-check-" + Guid.NewGuid()); Directory.CreateDirectory(directory); try { await test(directory); } finally { Directory.Delete(directory, true); } }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return run(request); } }
}
