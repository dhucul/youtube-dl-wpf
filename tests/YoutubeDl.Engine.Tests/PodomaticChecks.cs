using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Podomatic;

internal static class PodomaticChecks
{
    internal const string Id = "10085572", Other = "10085573", Collection = "6347936", Name = "alliancehpsr", Stamp = "2021-07-27T02_54_01-07_00", Signed = "?literal=A%2Bb&case=C%2Fd";
    internal static string Media(string id = Id, bool video = false) => "https://streams.podomatic.com/vod/UDS13/57/f6/f5/" + Name + "/media/published/" + id + (video ? ".mp4" : ".mp3") + Signed;
    internal static NativeDownloadRequest Request(string directory, bool podcast = false) => new() { Url = new("https://www.podomatic.com/embed/html5/" + (podcast ? "podcast/" + Collection : "episode/" + Id)), OutputDirectory = directory, OutputTemplate = "podomatic.%(ext)s" };
    internal static JsonNode Podcast() => new JsonObject { ["collection_guid"] = Collection, ["subdomain"] = Name, ["posting_count"] = 2, ["title"] = "Fixture podcast", ["podcast_url"] = "https://www.podomatic.com/podcasts/" + Name, ["feed_url"] = "https://" + Name + ".podomatic.com/rss2.xml", ["profile"] = new JsonObject { ["profile_name"] = "Fixture author" } };
    internal static JsonNode Episode(string id = Id, double duration = 2, bool video = false) => new JsonObject { ["episode_guid"] = id, ["collection_guid"] = Collection, ["podcast_subdomain"] = Name, ["permalink_url"] = "https://www.podomatic.com/podcasts/" + Name + "/episodes/" + Stamp, ["duration"] = duration, ["has_dynamic_ads"] = false,
        ["media_type"] = video ? "video" : "audio", ["media_url"] = Media(id, video), ["title"] = "Fixture episode", ["description"] = "Public native media", ["profile"] = new JsonObject { ["profile_name"] = "Fixture author" } };
    internal static JsonNode List(int count = 2, int page = 1, int pages = 1, params JsonNode[] episodes) => new JsonObject { ["status"] = "success", ["pagination"] = new JsonObject { ["item_count"] = count, ["page"] = page, ["page_count"] = pages }, ["episodes"] = new JsonArray(episodes.Length == 0 ? [Episode(), Episode(Other)] : episodes) };
    internal static IEnumerable<(string Name, Func<Task> Test)> All => [
        ("Podomatic current public episode records bind media publisher and complete playback facts", Facts),
        ("Podomatic current timestamp legacy subdomain and embed aliases retain stable episode IDs", Aliases),
        ("Podomatic private malformed manually paged and unrelated routes remain explicit", Boundaries),
        ("Podomatic conflicting episode collection publisher permalink and source identities fail", Identities),
        ("Podomatic unavailable malformed dynamic state and unsupported media remain explicit", Malformed),
        ("Podomatic complete public episode counts page numbers and ownership preserve source positions", Pagination),
        ("Podomatic changed repeated premature or oversized public inventories cannot claim completion", Incomplete),
        ("Podomatic unavailable podcast entries retain original source numbering", Positions),
        ("Podomatic inspected podcast snapshots refresh only chosen episode and owner metadata", Snapshots),
        ("Podomatic known embed and selected-child archives skip playback discovery", Archives),
        ("Podomatic anonymous metadata isolates credentials cancellation denial and redirected identity", Transport)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var info = await Extract(calls); var media = info.Media.Single(); Assert(info.Id == Id && info.Uploader == "Fixture author" && info.DurationSeconds == 2 && info.AnonymousPlayback && media.Url.AbsoluteUri == Media() && media.IsAudioOnly && media.VerificationDurationSeconds == 2 && media.VerificationDemuxer == "mp3" && calls.Count == 2, "Podomatic source/owner/verification facts changed.");
        var dynamic = Episode(); dynamic["has_dynamic_ads"] = true; media = (await Extract([], dynamic)).Media.Single(); Assert(media.VerificationDurationSeconds is null && media.VerificationMinimumDurationSeconds == 2, "Explicit dynamic delivery omitted its program lower bound.");
        var video = (await Extract([], Episode(video: true))).Media.Single(); Assert(video.HasVideo == true && video.HasAudio is null && video.VerificationDemuxer == "mov", "Podomatic video roles or local demuxer changed.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "https://www.podomatic.com/podcasts/" + Name + "/episodes/" + Stamp, "http://" + Name + ".podomatic.com/entry/" + Stamp, "https://podomatic.com/embed/html5/episode/" + Id + "?square=true" }) Assert((await Extract([], request: Request(Path.GetTempPath()) with { Url = new(url) })).Id == Id, "Episode alias changed stable identity.");
        foreach (var url in new[] { "https://www.podomatic.com/podcasts/" + Name, "https://" + Name + ".podomatic.com/" }) Assert((await Extract([], request: Request(Path.GetTempPath(), true) with { Url = new(url) })).Entries.Count == 2, "Podcast alias changed inventory.");
    }
    private static Task Boundaries()
    {
        var provider = new PodomaticProvider(); foreach (var url in new[] { "https://secret@www.podomatic.com/embed/html5/episode/" + Id, "https://www.podomatic.com:444/embed/html5/episode/" + Id, "https://www.podomatic.com/embed/html5/episode/" + Id + "?token=private", "https://www.podomatic.com/podcasts/" + Name + "?page=2", "https://www.podomatic.com/podcasts/" + Name + "/episodes/2021-99-99T99_99_99-07_00", "https://www.podomatic.com/embed/html5/playlist/1", "https://www.podomatic.com/help-center" }) Assert(!provider.CanHandle(new(url)), "Unsupported Podomatic route was claimed.");
        var registry = YoutubeDl.Engine.Providers.MediaProviderRegistry.CreateDefault(); Assert(registry.Resolve(Request(Path.GetTempPath()).Url).Info.Id == "podomatic" && !registry.CanHandle(new("https://www.podomatic.com/embed/html5/episode/" + Id + "?token=private")) && registry.CanHandle(new("https://" + Name + ".podomatic.com/rss2.xml")), "Default dispatch bypassed player restrictions or removed RSS support."); return Task.CompletedTask;
    }
    private static async Task Identities()
    {
        foreach (var change in new Action<JsonNode>[] { e => e["episode_guid"] = Other, e => e["collection_guid"] = "123", e => e["podcast_subdomain"] = "other", e => e["permalink_url"] = "https://www.podomatic.com/podcasts/other/episodes/" + Stamp }) { var e = Episode(); change(e); await Expect<InvalidDataException>(() => Extract([], e)); }
        foreach (var url in new[] { Media().Replace(Name, "other"), Media().Replace("published", "private"), "https://other.example/media.mp3" }) { var e = Episode(); e["media_url"] = url; await Expect<NotSupportedException>(() => Extract([], e)); }
    }
    private static async Task Malformed()
    {
        var unavailable = Episode(); unavailable["media_url"] = null; Assert((await Extract([], unavailable)).Media.Count == 0, "Unavailable media fabricated a source.");
        var bad = Episode(); bad["has_dynamic_ads"] = "false"; await Expect<InvalidDataException>(() => Extract([], bad));
        bad = Episode(); bad["duration"] = null; await Expect<InvalidDataException>(() => Extract([], bad));
        bad = Episode(); bad["media_type"] = "document"; await Expect<NotSupportedException>(() => Extract([], bad));
        using var malformed = Http([], raw: "bad JSON"); await Expect<InvalidDataException>(() => new PodomaticProvider().ExtractAsync(Request(Path.GetTempPath()), malformed, default));
        using var duplicate = Http([], raw: "{\"status\":\"success\",\"status\":\"error\"}"); await Expect<InvalidDataException>(() => new PodomaticProvider().ExtractAsync(Request(Path.GetTempPath()), duplicate, default));
    }
    private static async Task Pagination()
    {
        var calls = new List<Uri>(); var show = Podcast(); show["posting_count"] = 10;
        using var http = Http(calls, first: List(2, 1, 2, Episode()), second: List(2, 2, 2, Episode(Other)), podcast: show);
        var page = await new PodomaticProvider().ExtractAsync(Request(Path.GetTempPath(), true), http, default); Assert(page.IsPlaylist && page.Entries.Count == 2 && page.Entries[1].Position == 2 && page.Entries[1].ExpectedArchiveIdentity!.Id == Other && calls.Count == 3 && calls[^1].Query == "?per_page=100&page=2", "Public listing count was replaced by a general metadata count or lost page/source boundaries.");
        var empty = List(0, 1, 0); empty["episodes"] = new JsonArray(); Assert((await Extract([], request: Request(Path.GetTempPath(), true), first: empty)).Entries.Count == 0, "A zero-page empty public podcast was treated as incomplete.");
    }
    private static async Task Incomplete()
    {
        foreach (var change in new Action<JsonNode>[] { p => p["pagination"]!["item_count"] = 3, p => p["pagination"]!["page"] = 1, p => p["pagination"]!["page_count"] = 3, p => p["episodes"] = new JsonArray(Episode()) }) { var second = List(2, 2, 2, Episode(Other)); change(second); await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: List(2, 1, 2, Episode()), second: second)); }
        await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: List(2, 1, 1, Episode())));
        var missing = List(); ((JsonObject)missing).Remove("episodes"); await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), first: missing));
        await Expect<NotSupportedException>(() => Extract([], request: Request(Path.GetTempPath(), true) with { MaximumPlaylistEntries = 1 }));
    }
    private static async Task Positions()
    { var missing = Episode(); missing["media_type"] = "none"; var page = await Extract([], request: Request(Path.GetTempPath(), true), first: List(2, 1, 1, missing, Episode(Other))); Assert(!page.Entries[0].Available && page.Entries[1].Position == 2 && page.Entries[1].Available, "Unavailable posts changed original numbering."); }
    private static async Task Snapshots()
    {
        await Temporary(async directory => { var calls = new List<Uri>(); var engine = new NativeEngine([new PodomaticProvider()], _ => Http(calls)); var request = Request(directory, true) with { PlaylistItems = "2", SkipDownload = true, WriteInfoJson = true };
            var info = await engine.InspectAsync(request, default); calls.Clear(); var result = await engine.DownloadAsync(request with { PlaylistSnapshot = info.PlaylistSnapshot }, null, null, default); Assert(result.Succeeded && calls.Count == 2 && calls[0].AbsolutePath.EndsWith(Other), "Snapshot refreshed unselected listing or another episode."); });
    }
    private static async Task Archives()
    {
        await Temporary(async directory => { var archive = Path.Combine(directory, "archive.txt"); await File.WriteAllTextAsync(archive, "podomatic " + Id + "\npodomatic " + Other + "\n"); var calls = new List<Uri>(); var engine = new NativeEngine([new PodomaticProvider()], _ => Http(calls));
            var result = await engine.DownloadAsync(Request(directory) with { DownloadArchiveFile = archive }, null, null, default); Assert(result.Skipped && calls.Count == 0, "Known embed archive requested metadata.");
            result = await engine.DownloadAsync(Request(directory, true) with { PlaylistItems = "2", DownloadArchiveFile = archive }, null, null, default); Assert(result.Succeeded && calls.Count == 2, "Archived selected child requested playback discovery."); });
    }
    private static async Task Transport()
    {
        var options = new MediaHttpOptions(); options.Headers["Authorization"] = "Bearer fixture"; options.Headers["Cookie"] = "fixture=secret"; using var http = Http([], options: options, observe: r => Assert(!r.Headers.Contains("Authorization") && !r.Headers.Contains("Cookie"), "Caller credentials reached public metadata.")); _ = await new PodomaticProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
        using var stop = new CancellationTokenSource(); stop.Cancel(); await Expect<OperationCanceledException>(() => new PodomaticProvider().ExtractAsync(Request(Path.GetTempPath()), http, stop.Token));
        using var denied = Http([], status: HttpStatusCode.Forbidden); await Expect<HttpRequestException>(() => new PodomaticProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
        using var redirected = Http([], redirect: true); await Expect<InvalidDataException>(() => new PodomaticProvider().ExtractAsync(Request(Path.GetTempPath()), redirected, default));
    }
    private static async Task<PageMetadata> Extract(List<Uri> calls, JsonNode? episode = null, NativeDownloadRequest? request = null, JsonNode? first = null, JsonNode? second = null)
    { using var http = Http(calls, episode, first, second); return await new PodomaticProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static MediaHttp Http(List<Uri> calls, JsonNode? episode = null, JsonNode? first = null, JsonNode? second = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null, MediaHttpOptions? options = null, Action<HttpRequestMessage>? observe = null, string? raw = null, HttpStatusCode status = HttpStatusCode.OK, bool redirect = false, JsonNode? podcast = null) => new(new Handler(r =>
    {
        calls.Add(r.RequestUri!); observe?.Invoke(r); var uri = r.RequestUri!; if (uri.Host != "www.podomatic.com") return media?.Invoke(r) ?? throw new InvalidOperationException("Unexpected Podomatic media request.");
        JsonNode data;
        if (uri.AbsolutePath.StartsWith("/v2/episodes/") || uri.AbsolutePath.Contains("/episodes/") && uri.Query.Length == 0) data = new JsonObject { ["status"] = "success", ["episode"] = uri.AbsolutePath.EndsWith(Other) ? Episode(Other) : episode?.DeepClone() ?? Episode() };
        else if (uri.AbsolutePath.EndsWith("/episodes")) data = (uri.Query.EndsWith("page=2") ? second ?? List() : first ?? List()).DeepClone();
        else data = new JsonObject { ["status"] = "success", ["podcast"] = podcast?.DeepClone() ?? Podcast() };
        return new(status) { Content = new StringContent(raw ?? data.ToJsonString()), RequestMessage = redirect ? new(HttpMethod.Get, "https://other.example/data") : r };
    }), options ?? new());
    private static async Task Temporary(Func<string, Task> test) { var directory = Path.Combine(Path.GetTempPath(), "podomatic-check-" + Guid.NewGuid()); Directory.CreateDirectory(directory); try { await test(directory); } finally { Directory.Delete(directory, true); } }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
}
