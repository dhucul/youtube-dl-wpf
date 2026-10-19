using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.AudioBoom;

internal static class AudioBoomChecks
{
    internal const string Id = "8143686", Other = "8143687", Channel = "5084695", Signed = "?literal=A%2Bb&case=C%2Fd";
    internal static string Media(string id = Id) => "https://audioboom.com/posts/" + id + ".mp3" + Signed;
    internal static NativeDownloadRequest Request(string directory, bool channel = false) => new() { Url = new("https://audioboom.com/" + (channel ? "channels/" + Channel : "posts/" + Id + "-trailer")), OutputDirectory = directory, OutputTemplate = "audioboom.%(ext)s" };
    internal static JsonNode Clip(string id = Id, double duration = 2) => new JsonObject { ["clipId"] = long.Parse(id), ["clipURLPriorToLoading"] = Media(id), ["staticLink"] = "https://audioboom.com/posts/" + id,
        ["title"] = "Fixture trailer", ["author"] = "Fixture author", ["duration"] = duration, ["embedDisabled"] = false, ["downloadURL"] = null, ["formattedDescription"] = "Native public audio" };
    internal static string Html(JsonNode? clip = null, bool embed = false, string id = Id, JsonNode? extra = null)
    {
        var root = new JsonObject { ["clips"] = new JsonArray(clip?.DeepClone() ?? Clip(id)) };
        if (extra is not null) ((JsonArray)root["clips"]!).Add(extra.DeepClone());
        if (embed) { root["isSingle"] = true; root["isChannel"] = false; root["isPlaylist"] = false; }
        var payload = WebUtility.HtmlEncode(root.ToJsonString());
        return "<link rel='canonical' href='https://audioboom.com/posts/" + id + "-trailer'><meta property='og:title' content='Fixture trailer'>" +
            (embed ? "<main data-jsclass='ReactBoomEmbedPlayerV5' data-player-data='" + payload + "'></main>" : "<ab-react data-react-class='V5DetailPagePlayer' data-react-props='" + payload + "'></ab-react>");
    }
    internal static string ChannelHtml(int count = 2) => "<link rel='canonical' href='https://audioboom.com/channels/" + Channel + "-fixture'><meta property='og:title' content='Fixture channel'><div class='f7 mbm'>" + count + " episodes</div><link rel='alternate' type='application/rss+xml' href='https://audioboom.com/channels/" + Channel + ".rss'>";
    internal static string Feed(int count = 2, bool unavailable = false)
    {
        var items = Enumerable.Range(0, count).Select(index => { var id = index == 0 ? Id : (long.Parse(Other) + index - 1).ToString(); return "<item><title>Episode " + index + "</title><guid>tag:imported.example:episode-" + id + "</guid><link>https://audioboom.com/posts/" + id + "</link>" + (unavailable && index == 0 ? "" : "<enclosure url='https://audioboom.com/posts/" + id + ".mp3' type='audio/mpeg' length='9999999'/>") + "</item>"; });
        return "<rss version='2.0'><channel><title>Fixture channel</title><link>https://audioboom.com/channels/" + Channel + "</link>" + string.Concat(items) + "</channel></rss>";
    }
    internal static IEnumerable<(string Name, Func<Task> Test)> All => [
        ("Audioboom current literal episode players retain exact source identity and full audio facts", Facts),
        ("Audioboom public post embed and channel aliases select stable resources", Aliases),
        ("Audioboom unrelated private filtered ambiguous and legacy routes remain explicit", Boundaries),
        ("Audioboom requested canonical clip and source identities cannot be replaced", Identities),
        ("Audioboom duplicate malformed hidden and unavailable literal player records fail explicitly", Malformed),
        ("Audioboom complete channel counts bind owned feeds and imported episode identities", Collections),
        ("Audioboom incomplete repeated foreign oversized and external-entity feeds fail before media", Incomplete),
        ("Audioboom unavailable channel positions retain original numbering", Positions),
        ("Audioboom inspected channel snapshots refresh only selected episode playback", Snapshots),
        ("Audioboom known episode and selected-child archives avoid playback discovery", Archives),
        ("Audioboom anonymous metadata cancellation denial and redirect boundaries remain isolated", Transport)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var page = await Extract(calls); var media = page.Media.Single();
        Assert(page.Id == Id && page.Uploader == "Fixture author" && page.DurationSeconds == 2 && page.AnonymousPlayback && media.Url.AbsoluteUri == Media() && media.IsAudioOnly && media.IsCompleteMedia && media.VerificationDurationSeconds == 2 && media.VerificationDemuxer == "mp3" && media.VerificationFileSize is null && calls.Count == 1, "Audioboom source/verification facts changed.");
        Assert((await Extract([], episode: Html(extra: Clip(Other)))).Media.Single().Url.AbsoluteUri == Media(), "A related clip replaced the requested audio.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "http://www.audioboom.com/posts/" + Id, "https://audioboom.com/posts/" + Id + "/embed", "https://embeds.audioboom.com/posts/" + Id + "-trailer/embed?v=202301&player_theme=dark" })
            Assert((await Extract([], request: Request(Path.GetTempPath()) with { Url = new(url) })).Id == Id, "Post/embed alias changed stable identity.");
        foreach (var url in new[] { "https://audioboom.com/channels/" + Channel + "/embed", "https://audioboom.com/channel/fixture" })
            Assert((await Extract([], request: Request(Path.GetTempPath(), true) with { Url = new(url) })).Entries.Count == 2, "Channel alias changed the complete inventory.");
    }
    private static Task Boundaries()
    {
        var provider = new AudioBoomProvider(); foreach (var url in new[] { "https://audioboom.com/posts/slug", "https://secret@audioboom.com/posts/" + Id, "https://audioboom.com:444/posts/" + Id, "https://audioboom.com/posts/" + Id + "?token=private", "https://audioboom.com/channels/" + Channel + "?page=2", "https://audioboom.com/posts/" + Id + "/embed/v4", "https://audioboom.com/users/fixture" }) Assert(!provider.CanHandle(new(url)), "Unsupported Audioboom route was claimed.");
        Assert(YoutubeDl.Engine.Providers.MediaProviderRegistry.CreateDefault().Resolve(Request(Path.GetTempPath()).Url).Info.Id == "audioboom", "Default registry missed Audioboom."); return Task.CompletedTask;
    }
    private static async Task Identities()
    {
        foreach (var change in new Action<JsonNode>[] { c => c["clipId"] = long.Parse(Other), c => c["staticLink"] = "https://audioboom.com/posts/" + Other }) { var c = Clip(); change(c); await Expect<InvalidDataException>(() => Extract([], episode: Html(c))); }
        await Expect<InvalidDataException>(() => Extract([], episode: Html().Replace("posts/" + Id + "-trailer", "posts/" + Other + "-trailer")));
        foreach (var url in new[] { Media(Other), "https://other.example/audio.mp3", Media().Replace("posts/", "private/") }) { var c = Clip(); c["clipURLPriorToLoading"] = url; await Expect<NotSupportedException>(() => Extract([], episode: Html(c))); }
    }
    private static async Task Malformed()
    {
        var c = Clip(); c["embedDisabled"] = true; await Expect<NotSupportedException>(() => Extract([], episode: Html(c))); ((JsonObject)c).Remove("embedDisabled"); await Expect<NotSupportedException>(() => Extract([], episode: Html(c)));
        c = Clip(); c["duration"] = null; await Expect<InvalidDataException>(() => Extract([], episode: Html(c)));
        await Expect<InvalidDataException>(() => Extract([], episode: "<!--" + Html() + "-->"));
        await Expect<NotSupportedException>(() => Extract([], episode: Html() + Html()));
        await Expect<InvalidDataException>(() => Extract([], episode: Html().Replace("data-react-props=", "data-react-class='other' data-react-props=")));
        var duplicate = Html().Replace("&quot;clipId&quot;:8143686", "&quot;clipId&quot;:8143687,&quot;clipId&quot;:8143686"); await Expect<InvalidDataException>(() => Extract([], episode: duplicate));
        await Expect<InvalidDataException>(() => Extract([], episode: Html(extra: Clip())));
        var embed = Html(embed: true).Replace("&quot;isSingle&quot;:true", "&quot;isSingle&quot;:false"); await Expect<NotSupportedException>(() => Extract([], episode: embed, request: Request(Path.GetTempPath()) with { Url = new("https://audioboom.com/posts/" + Id + "/embed") }));
    }
    private static async Task Collections()
    {
        var calls = new List<Uri>(); var page = await Extract(calls, request: Request(Path.GetTempPath(), true));
        Assert(page.IsPlaylist && page.Id == Channel && page.Entries.Count == 2 && page.Entries[1].Id == Other && page.Entries[1].ExpectedArchiveIdentity!.Record == "audioboom " + Other && page.Entries.All(entry => entry.ResolvedMedia is null) && calls.Count == 2, "Count-scoped channel/imported identifiers changed.");
    }
    private static async Task Incomplete()
    {
        await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), feed: Feed(1)));
        await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), feed: Feed().Replace("posts/" + Other, "posts/" + Id)));
        await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), feed: Feed().Replace("https://audioboom.com/posts/", "https://other.example/posts/")));
        await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), channel: ChannelHtml().Replace("channels/" + Channel + ".rss", "channels/123.rss")));
        await Expect<NotSupportedException>(() => Extract([], request: Request(Path.GetTempPath(), true) with { MaximumPlaylistEntries = 1 }));
        await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), feed: "<!DOCTYPE rss [<!ENTITY external SYSTEM 'file:///private'>]>" + Feed().Replace("Fixture channel", "&external;")));
    }
    private static async Task Positions()
    { var page = await Extract([], request: Request(Path.GetTempPath(), true), feed: Feed(unavailable: true)); Assert(!page.Entries[0].Available && page.Entries[1].Position == 2 && page.Entries[1].Available, "Unavailable feed slots changed source numbering."); }
    private static async Task Snapshots()
    {
        await Temporary(async directory => { var calls = new List<Uri>(); var engine = new NativeEngine([new AudioBoomProvider()], _ => Http(calls)); var request = Request(directory, true) with { PlaylistItems = "2", SkipDownload = true, WriteInfoJson = true };
            var info = await engine.InspectAsync(request, default); calls.Clear(); var result = await engine.DownloadAsync(request with { PlaylistSnapshot = info.PlaylistSnapshot }, null, null, default);
            Assert(result.Succeeded && calls.Count == 1 && calls[0].AbsolutePath.EndsWith(Other), "Inspected Audioboom selection refreshed unselected metadata."); });
    }
    private static async Task Archives()
    {
        await Temporary(async directory => { var archive = Path.Combine(directory, "archive.txt"); await File.WriteAllTextAsync(archive, "audioboom " + Id + "\naudioboom " + Other + "\n"); var calls = new List<Uri>(); var engine = new NativeEngine([new AudioBoomProvider()], _ => Http(calls));
            var result = await engine.DownloadAsync(Request(directory) with { DownloadArchiveFile = archive }, null, null, default); Assert(result.Skipped && calls.Count == 0, "Known post archive requested discovery.");
            result = await engine.DownloadAsync(Request(directory, true) with { PlaylistItems = "2", DownloadArchiveFile = archive }, null, null, default); Assert(result.Succeeded && calls.Count == 2, "Archived selected child requested player discovery."); });
    }
    private static async Task Transport()
    {
        var options = new MediaHttpOptions(); options.Headers["Authorization"] = "Bearer fixture"; options.Headers["Cookie"] = "fixture=secret";
        using var http = Http([], options: options, observe: r => Assert(!r.Headers.Contains("Authorization") && !r.Headers.Contains("Cookie"), "Caller credentials reached public Audioboom metadata.")); _ = await new AudioBoomProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
        using var stop = new CancellationTokenSource(); stop.Cancel(); await Expect<OperationCanceledException>(() => new AudioBoomProvider().ExtractAsync(Request(Path.GetTempPath()), http, stop.Token));
        using var denied = Http([], status: HttpStatusCode.Forbidden); await Expect<HttpRequestException>(() => new AudioBoomProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
        using var redirected = Http([], redirect: true); await Expect<InvalidDataException>(() => new AudioBoomProvider().ExtractAsync(Request(Path.GetTempPath()), redirected, default));
    }
    private static async Task<PageMetadata> Extract(List<Uri> calls, string? episode = null, NativeDownloadRequest? request = null, string? channel = null, string? feed = null)
    { using var http = Http(calls, episode, channel, feed); return await new AudioBoomProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static MediaHttp Http(List<Uri> calls, string? episode = null, string? channel = null, string? feed = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null, MediaHttpOptions? options = null, Action<HttpRequestMessage>? observe = null, HttpStatusCode status = HttpStatusCode.OK, bool redirect = false) => new(new Handler(r =>
    {
        calls.Add(r.RequestUri!); observe?.Invoke(r); var uri = r.RequestUri!;
        if (uri.AbsolutePath.EndsWith(".mp3")) return media?.Invoke(r) ?? throw new InvalidOperationException("Unexpected Audioboom media request.");
        var text = uri.AbsolutePath.EndsWith(".rss") ? feed ?? Feed() : uri.AbsolutePath.Contains("/channels/") || uri.AbsolutePath.Contains("/channel/") ? channel ?? ChannelHtml() : uri.AbsolutePath.EndsWith(Other) ? Html(id: Other) : episode ?? Html(embed: uri.AbsolutePath.EndsWith("/embed"));
        return new(status) { Content = new StringContent(text), RequestMessage = redirect ? new(HttpMethod.Get, "https://audioboom.com/posts/123") : r };
    }), options ?? new());
    private static async Task Temporary(Func<string, Task> test) { var directory = Path.Combine(Path.GetTempPath(), "audioboom-check-" + Guid.NewGuid()); Directory.CreateDirectory(directory); try { await test(directory); } finally { Directory.Delete(directory, true); } }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
}
