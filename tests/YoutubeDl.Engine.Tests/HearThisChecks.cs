using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.HearThis;

internal static class HearThisChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("hearthis public tracks bind source identities dates counters and offered audio", Facts),
        ("hearthis public players bind declared numeric tiles to canonical recording metadata", Embeds),
        ("hearthis unknown access collection selectors hosts and routes cannot fall through", Boundaries),
        ("hearthis wrong owner recording canonical source and player identities fail", Ownership),
        ("hearthis private fan-gated live video and missing availability states refuse", Restrictions),
        ("hearthis media gateways and original formats stay bound to supplied recording paths", Resources),
        ("hearthis preview resources never replace the published complete audio descriptor", Previews),
        ("hearthis complete counted artist pages preserve original source ordering", Artists),
        ("hearthis repeated missing changed and overlimit artist inventories fail", Incomplete),
        ("hearthis unavailable artist recordings preserve original source positions", Positions),
        ("hearthis inspected artist selections refresh only chosen recording metadata", Snapshots),
        ("hearthis numeric recording archives skip delivery and completion effects", Archives),
        ("hearthis anonymous credentials cancellation and HTTP denial remain explicit", Transport)
    ];
    private static async Task Facts()
    {
        using var http = Http([]); var page = await new HearThisProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
        Assert(page.Id == "42" && page.ArchiveIdentity == new DownloadArchiveIdentity("hearthisat", "42") && page.Title == "Track" && page.DurationSeconds == 2 && page.UploadDate == new DateOnly(2020, 1, 1) && page.ViewCount == 12 && page.Media.Count == 1 && page.Media[0].Url == new Uri(Stream), "hearthis source facts changed.");
        Assert(MediaProviderRegistry.CreateDefault().Resolve(Request(Path.GetTempPath()).Url).Info.Id == "hearthisat", "hearthis was not registered.");
    }
    private static async Task Embeds()
    {
        foreach (var url in new[] { "https://hearthis.at/embed/42/", "https://hearthis.at/embed/42/transparent_black/?autoplay=0&color=123" })
        { using var http = Http([]); var page = await new HearThisProvider().ExtractAsync(Request(Path.GetTempPath()) with { Url = new(url) }, http, default); Assert(page.Id == "42", "hearthis embed changed source identity."); }
    }
    private static Task Boundaries()
    {
        foreach (var url in new[] { "https://hearthis.at/artist/track/?key=private", "https://hearthis.at/artist/?page=2", "https://hearthis.at/artist/set/list/", "https://hearthis.at/artist/track/#t=2", "https://user:secret@hearthis.at/artist/track/", "https://hearthis.at:8443/artist/track/", "https://hearthis.at/embed/invalid/", "https://hearthis.at/search/", "https://hearthis.at.evil.example/artist/track/" })
            Assert(!new HearThisProvider().CanHandle(new(url)), "Unsupported hearthis address was accepted.");
        Assert(!new GenericPageProvider().CanHandle(new("https://hearthis.at/private/key/path")), "Unknown hearthis access reached generic extraction."); return Task.CompletedTask;
    }
    private static async Task Ownership()
    {
        foreach (var change in new Action<JsonObject>[] { node => node["user_id"] = "10", node => node["user"]!["permalink"] = "other", node => node["permalink"] = "other", node => node["permalink_url"] = "https://hearthis.at/other/track/", node => node["uri"] = "https://api-v2.hearthis.at/other/track/" })
        { using var http = Http([], (kind, node) => { if (kind == "track") change(node); }); await Expect<InvalidDataException>(() => new HearThisProvider().ExtractAsync(Request(Path.GetTempPath()), http, default)); }
        using var wrong = Http([], (kind, node) => { if (kind == "track") node["id"] = "43"; }); await Expect<InvalidDataException>(() => new HearThisProvider().ExtractAsync(Request(Path.GetTempPath()) with { Url = new("https://hearthis.at/embed/42/") }, wrong, default));
    }
    private static async Task Restrictions()
    {
        foreach (var change in new Action<JsonObject>[] { node => node["private"] = "1", node => node["fan_exclusive_play"] = 1, node => node["is_live"] = true, node => node["is_live_video"] = true, node => node["video_stream"] = "https://video.example/file" })
        { using var http = Http([], (kind, node) => { if (kind == "track") change(node); }); await Expect<NotSupportedException>(() => new HearThisProvider().ExtractAsync(Request(Path.GetTempPath()), http, default)); }
        using var missing = Http([], (kind, node) => { if (kind == "track") node.Remove("private"); }); await Expect<InvalidDataException>(() => new HearThisProvider().ExtractAsync(Request(Path.GetTempPath()), missing, default));
    }
    private static async Task Resources()
    {
        foreach (var source in new[] { Stream.Replace("/artist/", "/other/"), Stream.Replace("/track/", "/other/"), Stream.Replace("hearthis.app", "other.example"), Stream + "#fragment" })
        { using var http = Http([], (kind, node) => { if (kind == "track") node["stream_url"] = source; }); await Expect<InvalidDataException>(() => new HearThisProvider().ExtractAsync(Request(Path.GetTempPath()), http, default)); }
        using var original = Http([], (kind, node) => { if (kind == "track") { node["downloadable"] = "1"; node["download_url"] = "https://hearthis.at/artist/track/download/?s=literal"; node["download_filename"] = "original.flac"; } });
        var page = await new HearThisProvider().ExtractAsync(Request(Path.GetTempPath()), original, default); Assert(page.Media.Count == 2 && page.Media.Single(media => media.Id == "download").Extension == "flac", "An offered permitted original format was dropped.");
    }
    private static async Task Previews()
    {
        using var http = Http([], (kind, node) => { if (kind == "track") node["preview_url"] = "https://preview.hearthis.at/files/preview.mp3"; }); var page = await new HearThisProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
        Assert(page.Media.All(media => media.Url.Host != "preview.hearthis.at"), "Preview audio was reported as complete playback.");
        using var unavailable = Http([], (kind, node) => { if (kind == "track") { node["stream_url"] = ""; node["preview_url"] = "https://preview.hearthis.at/files/preview.mp3"; } }); await Expect<NotSupportedException>(() => new HearThisProvider().ExtractAsync(Request(Path.GetTempPath()), unavailable, default));
    }
    private static async Task Artists()
    {
        var calls = new List<Uri>(); using var http = Http(calls); var page = await new HearThisProvider().ExtractAsync(Request(Path.GetTempPath(), true), http, default);
        Assert(page.IsPlaylist && page.Entries.Count == 52 && page.Entries[50].Position == 51 && page.Entries[50].Id == "51" && calls.Count == 5, "hearthis artist pagination/count/order changed.");
    }
    private static async Task Incomplete()
    {
        foreach (var mode in new[] { "repeated", "short", "owner", "changed" })
        {
            var firstPages = 0; using var http = Http([], (kind, node) => { if (kind == "artist" && mode == "owner") node["permalink"] = "other"; if (kind == "first" && mode == "changed" && ++firstPages == 2) node["stream_url"] = "https://hearthis.app/artist/track/stream.mp3?s=changed"; }, pageMode: mode);
            await Expect<InvalidDataException>(() => new HearThisProvider().ExtractAsync(Request(Path.GetTempPath(), true), http, default));
        }
        using var limited = Http([]); await Expect<NotSupportedException>(() => new HearThisProvider().ExtractAsync(Request(Path.GetTempPath(), true) with { MaximumPlaylistEntries = 10 }, limited, default));
    }
    private static async Task Positions()
    {
        using var http = Http([], (kind, node) => { if (kind == "first") node["private"] = "1"; }); var page = await new HearThisProvider().ExtractAsync(Request(Path.GetTempPath(), true), http, default);
        Assert(!page.Entries[0].Available && page.Entries[0].Position == 1 && page.Entries[1].Position == 2, "Private artist entry moved other source positions.");
    }
    private static async Task Snapshots()
    {
        using var files = new Files(); var calls = new List<Uri>(); var engine = new NativeEngine([new HearThisProvider()], _ => Http(calls)); var request = Request(files.Path, true) with { PlaylistItems = "51", Simulate = true };
        var info = await engine.InspectAsync(request, default); calls.Clear(); var result = await engine.DownloadAsync(request with { PlaylistSnapshot = info.PlaylistSnapshot }, null, null, default);
        Assert(result.Succeeded && calls.Count == 1 && calls[0].AbsolutePath == "/artist/track51/", "hearthis snapshot refreshed unselected metadata.");
    }
    private static async Task Archives()
    {
        using var files = new Files(); var archive = Path.Combine(files.Path, "archive.txt"); await File.WriteAllTextAsync(archive, "hearthisat 42\n"); var calls = new List<Uri>(); var engine = new NativeEngine([new HearThisProvider()], _ => Http(calls));
        var result = await engine.DownloadAsync(Request(files.Path) with { DownloadArchiveFile = archive }, null, null, default, _ => throw new InvalidOperationException("Archived hearthis emitted completion.")); Assert(result.Skipped && calls.All(url => url.Host == "api-v2.hearthis.at"), "hearthis archive repeated media delivery.");
    }
    private static async Task Transport()
    {
        using var http = Http([], options: new MediaHttpOptions { Headers = { ["Authorization"] = "private fixture", ["Cookie"] = "private=fixture" } }); var page = await new HearThisProvider().ExtractAsync(Request(Path.GetTempPath()), http, default); Assert(page.Id == "42", "Anonymous hearthis discovery failed.");
        using var stop = new CancellationTokenSource(); stop.Cancel(); await Expect<OperationCanceledException>(() => new HearThisProvider().ExtractAsync(Request(Path.GetTempPath()), http, stop.Token)); using var denied = new MediaHttp(new Handler(_ => new(HttpStatusCode.Forbidden))); await Expect<HttpRequestException>(() => new HearThisProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
    }
    internal const string Stream = "https://hearthis.app/artist/track/listen/?s=literal%2Bquery";
    internal static NativeDownloadRequest Request(string output, bool artist = false) => new() { Url = new(artist ? "https://hearthis.at/artist/" : "https://hearthis.at/artist/track/"), OutputDirectory = output, OutputTemplate = "%(id)s.%(ext)s", Retries = 0 };
    internal static JsonObject Track(string id = "42", string slug = "track") => new() { ["id"] = id, ["private"] = "0", ["user_id"] = "9", ["duration"] = "2", ["permalink"] = slug, ["permalink_url"] = "https://hearthis.at/artist/" + slug + "/", ["uri"] = "https://api-v2.hearthis.at/artist/" + slug + "/", ["title"] = "Track", ["description"] = "Description", ["unix_created_at"] = 1577836800,
        ["stream_url"] = Stream.Replace("/track/", "/" + slug + "/"), ["downloadable"] = "0", ["download_url"] = "", ["download_filename"] = "track.mp3", ["playback_count"] = "12", ["is_live"] = false, ["is_live_video"] = false, ["fan_exclusive_play"] = 0, ["user"] = new JsonObject { ["id"] = "9", ["permalink"] = "artist", ["username"] = "Artist" } };
    internal static MediaHttp Http(List<Uri> calls, Action<string, JsonObject>? mutate = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null, MediaHttpOptions? options = null, string? pageMode = null) => new(new Handler(request =>
    {
        var url = request.RequestUri!; calls.Add(url); Assert(!request.Headers.Contains("Authorization") && !request.Headers.Contains("Cookie"), "hearthis leaked caller credentials.");
        if (url.Host is "hearthis.app" || url.Host == "hearthis.at" && url.AbsolutePath.Contains("/download/", StringComparison.Ordinal)) return media?.Invoke(request) ?? throw new InvalidOperationException("Core hearthis check fetched media.");
        if (url.Host == "hearthis.at") return new(HttpStatusCode.OK) { Content = new StringContent("<li data-trackid='42'><a data-detail='https://hearthis.at/artist/track/'>Play</a></li>"), RequestMessage = request };
        if (url.Query.Length > 0)
        {
            var second = url.Query.Contains("page=2", StringComparison.Ordinal); var data = new JsonArray(); var begin = second ? 51 : 1; var end = second ? pageMode == "short" ? 51 : 52 : 50;
            for (var id = begin; id <= end; id++) { var item = Track(id.ToString(), "track" + id); if (second && pageMode == "repeated") item = Track("1", "track1"); if (!second && id == 1) mutate?.Invoke("first", item); data.Add(item); }
            return new(HttpStatusCode.OK) { Content = new StringContent(data.ToJsonString()), RequestMessage = request };
        }
        JsonObject node; string kind;
        if (url.AbsolutePath.Trim('/').Split('/').Length == 1) { kind = "artist"; node = new() { ["id"] = "9", ["permalink"] = "artist", ["username"] = "Artist", ["uri"] = "https://api-v2.hearthis.at/artist/", ["permalink_url"] = "https://hearthis.at/artist/", ["track_count"] = "52" }; }
        else { kind = "track"; var slug = url.AbsolutePath.Trim('/').Split('/')[1]; var id = slug.StartsWith("track", StringComparison.Ordinal) && int.TryParse(slug[5..], out var number) ? number.ToString() : "42"; node = Track(id, slug); }
        mutate?.Invoke(kind, node); return new(HttpStatusCode.OK) { Content = new StringContent(node.ToJsonString()), RequestMessage = request };
    }), options);
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
    private sealed class Files : IDisposable { internal string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "test-data", "hearthis-" + Guid.NewGuid().ToString("N")); internal Files() => Directory.CreateDirectory(Path); public void Dispose() => Directory.Delete(Path, true); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
