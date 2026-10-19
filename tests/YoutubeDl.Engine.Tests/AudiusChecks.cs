using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Audius;

internal static class AudiusChecks
{
    internal const string Id = "XB4rzbJ", Other = "GEG9ZWx", UserId = "LMMQV", PlaylistId = "V0xv5kQ", Cid = "bafyFixtureContent123456789";
    internal static string Media(string id = Id, string cid = Cid, long numeric = 321426724)
    { var data = new JsonObject { ["cid"] = cid, ["trackId"] = numeric, ["userId"] = 0, ["timestamp"] = 1791617020000L }; var signature = new JsonObject { ["data"] = data.ToJsonString(), ["signature"] = "0xfixture-opaque-public-signature" }; return "https://node.example/tracks/cidstream/" + cid + "?signature=" + Uri.EscapeDataString(signature.ToJsonString()); }
    internal static NativeDownloadRequest Request(string directory, bool collection = false) => new() { Url = new(collection ? "https://api.audius.co/v1/playlists/" + PlaylistId : "audius:" + Id), OutputDirectory = directory, OutputTemplate = "audius.%(ext)s" };
    internal static JsonNode User(int count = 2) => new JsonObject { ["id"] = UserId, ["handle"] = "fixture", ["name"] = "Fixture artist", ["is_available"] = true, ["is_deactivated"] = false, ["track_count"] = count };
    internal static JsonNode Track(string id = Id, double duration = 2) => new JsonObject { ["id"] = id, ["track_id"] = 321426724, ["track_cid"] = Cid, ["permalink"] = "/fixture/song", ["title"] = "Fixture track", ["description"] = "Native public audio", ["duration"] = duration, ["user_id"] = UserId, ["user"] = User(),
        ["is_available"] = true, ["is_delete"] = false, ["is_scheduled_release"] = false, ["is_streamable"] = true, ["is_stream_gated"] = false, ["is_unlisted"] = false, ["allowed_api_keys"] = new JsonArray(), ["access"] = new JsonObject { ["stream"] = true, ["download"] = false }, ["stream"] = new JsonObject { ["url"] = Media(id) } };
    internal static JsonNode Playlist(params string[] ids) => new JsonObject { ["id"] = PlaylistId, ["playlist_name"] = "Fixture playlist", ["permalink"] = "/fixture/playlist/set", ["user"] = User(), ["track_count"] = ids.Length == 0 ? 2 : ids.Length, ["is_private"] = false, ["is_delete"] = false, ["is_scheduled_release"] = false, ["is_stream_gated"] = false,
        ["playlist_contents"] = new JsonArray((ids.Length == 0 ? new[] { Id, Other } : ids).Select(id => (JsonNode)new JsonObject { ["track_id"] = id, ["timestamp"] = 0 }).ToArray()) };
    internal static IEnumerable<(string Name, Func<Task> Test)> All => [
        ("Audius public tracks bind stable IDs artist CID signed descriptor and full MP3 facts", Facts),
        ("Audius pseudo-scheme API and canonical website aliases retain stable track identity", Aliases),
        ("Audius private preview manually paged and unrelated addresses remain explicit", Boundaries),
        ("Audius mismatched requested track artist permalink CID and signed payload identities fail", Identities),
        ("Audius gated scheduled deleted unavailable deactivated and API-key restrictions fail", Restrictions),
        ("Audius public playlists retain complete membership unavailable slots and repeated tracks", Playlists),
        ("Audius changed incomplete foreign or oversized playlist memberships remain explicit", PlaylistBoundaries),
        ("Audius artist public pagination binds owner counts and stable source numbering", Artists),
        ("Audius changed repeated or incomplete artist pages cannot claim a complete inventory", ArtistBoundaries),
        ("Audius inspected collection snapshots refresh only selected track playback", Snapshots),
        ("Audius known track and selected-child archives avoid signed playback discovery", Archives),
        ("Audius anonymous metadata cancellation denial malformed and resolver redirects remain scoped", Transport)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var page = await Extract(calls); var media = page.Media.Single(); Assert(page.Id == Id && page.Uploader == "Fixture artist" && page.DurationSeconds == 2 && page.AnonymousPlayback && media.Url.AbsoluteUri == Media() && media.IsAudioOnly && media.VerificationDurationSeconds == 2 && media.VerificationDemuxer == "mp3" && calls.Count == 1, "Audius track/source facts changed.");
        var track = Track(); track["stream"] = null; Assert((await Extract([], track)).Media.Single().Url.AbsoluteUri == "https://api.audius.co/v1/tracks/" + Id + "/stream", "Documented public stream fallback did not retain its track ID.");
    }
    private static async Task Aliases()
    { foreach (var url in new[] { "audius:" + Id, "https://api.audius.co/v1/tracks/" + Id, "http://www.audius.co/fixture/song" }) Assert((await Extract([], request: Request(Path.GetTempPath()) with { Url = new(url) })).Id == Id, "Audius alias changed track identity."); }
    private static Task Boundaries()
    {
        var provider = new AudiusProvider(); foreach (var url in new[] { "audius:invalid/path", "https://secret@audius.co/fixture/song", "https://audius.co:444/fixture/song", "https://audius.co/fixture/song?user_id=1", "https://api.audius.co/v1/tracks/" + Id + "?preview=true", "https://audius.co/trending", "https://api.audius.co/v1/tracks/" + Id + "/stream" }) Assert(!provider.CanHandle(new(url)), "Unsupported Audius address was claimed.");
        var registry = YoutubeDl.Engine.Providers.MediaProviderRegistry.CreateDefault(); Assert(registry.Resolve(Request(Path.GetTempPath()).Url).Info.Id == "audius" && registry.Resolve(new Uri("lbry://fixture")).Info.Id == "lbry" && !registry.CanHandle(new Uri("https://audius.co/fixture/song?preview=true")), "Scheme dispatch or generic preview boundaries changed."); return Task.CompletedTask;
    }
    private static async Task Identities()
    {
        foreach (var change in new Action<JsonNode>[] { t => t["id"] = Other, t => t["user_id"] = "other", t => t["permalink"] = "/other/song", t => t["track_cid"] = "bafyAnotherContent12345678", t => t["track_id"] = 1 }) { var track = Track(); change(track); await Expect<InvalidDataException>(() => Extract([], track)); }
        var wrong = Track(); wrong["stream"]!["url"] = Media(cid: "bafyAnotherContent12345678"); await Expect<InvalidDataException>(() => Extract([], wrong));
    }
    private static async Task Restrictions()
    {
        foreach (var change in new Action<JsonNode>[] { t => t["is_stream_gated"] = true, t => t["is_scheduled_release"] = true, t => t["is_delete"] = true, t => t["is_available"] = false, t => t["is_streamable"] = false, t => t["access"]!["stream"] = false, t => t["allowed_api_keys"] = new JsonArray("restricted") }) { var track = Track(); change(track); await Expect<NotSupportedException>(() => Extract([], track)); }
        var artist = Track(); artist["user"]!["is_deactivated"] = true; await Expect<NotSupportedException>(() => Extract([], artist));
    }
    private static async Task Playlists()
    {
        var page = await Extract([], request: Request(Path.GetTempPath(), true)); Assert(page.IsPlaylist && page.Entries.Count == 2 && page.Entries[1].Position == 2 && page.Entries[1].ExpectedArchiveIdentity!.Id == Other, "Playlist membership/source numbering changed.");
        page = await Extract([], request: Request(Path.GetTempPath(), true), playlist: Playlist(Id, Other, Id)); Assert(page.Entries.Count == 3 && page.Entries[2].Id == Id, "Repeated playlist track positions were discarded.");
        page = await Extract([], request: Request(Path.GetTempPath(), true), playlistTracks: [Track()]); Assert(!page.Entries[1].Available && page.Entries[1].Id == Other, "Missing tracks replaced or removed original membership slots.");
    }
    private static async Task PlaylistBoundaries()
    {
        var privateList = Playlist(); privateList["is_private"] = true; await Expect<NotSupportedException>(() => Extract([], request: Request(Path.GetTempPath(), true), playlist: privateList));
        var incomplete = Playlist(); incomplete["track_count"] = 3; await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), playlist: incomplete));
        await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath(), true), playlistTracks: [Track("foreign")]));
        await Expect<NotSupportedException>(() => Extract([], request: Request(Path.GetTempPath(), true) with { MaximumPlaylistEntries = 1 }));
    }
    private static async Task Artists()
    {
        var calls = new List<Uri>(); var page = await Extract(calls, request: Request(Path.GetTempPath()) with { Url = new("https://audius.co/fixture") }); Assert(page.IsPlaylist && page.Id == UserId && page.Entries.Count == 2 && calls.Count == 3 && calls[1].Query.Contains("filter_tracks=public") && calls[^1].AbsolutePath.EndsWith(UserId), "Artist owner/count/page scope or final identity check changed.");
    }
    private static async Task ArtistBoundaries()
    {
        var shortUser = User(3); await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath()) with { Url = new("https://audius.co/fixture") }, user: shortUser));
        await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath()) with { Url = new("https://audius.co/fixture") }, artistTracks: [Track(), Track()]));
        var foreign = Track(); foreign["user"]!["id"] = "other"; await Expect<InvalidDataException>(() => Extract([], request: Request(Path.GetTempPath()) with { Url = new("https://audius.co/fixture") }, artistTracks: [foreign]));
    }
    private static async Task Snapshots()
    {
        await Temporary(async directory => { var calls = new List<Uri>(); var engine = new NativeEngine([new AudiusProvider()], _ => Http(calls)); var request = Request(directory, true) with { PlaylistItems = "2", SkipDownload = true, WriteInfoJson = true }; var info = await engine.InspectAsync(request, default); calls.Clear(); var result = await engine.DownloadAsync(request with { PlaylistSnapshot = info.PlaylistSnapshot }, null, null, default); Assert(result.Succeeded && calls.Count == 1 && calls[0].AbsolutePath.EndsWith(Other), "Snapshot fetched an unselected list or track."); });
    }
    private static async Task Archives()
    {
        await Temporary(async directory => { var archive = Path.Combine(directory, "archive.txt"); await File.WriteAllTextAsync(archive, "audius " + Id + "\naudius " + Other + "\n"); var calls = new List<Uri>(); var engine = new NativeEngine([new AudiusProvider()], _ => Http(calls)); var result = await engine.DownloadAsync(Request(directory) with { DownloadArchiveFile = archive }, null, null, default); Assert(result.Skipped && calls.Count == 0, "Known ID archive fetched signed resources."); result = await engine.DownloadAsync(Request(directory, true) with { PlaylistItems = "2", DownloadArchiveFile = archive }, null, null, default); Assert(result.Succeeded && calls.Count == 2, "Archived selected child fetched playback discovery."); });
    }
    private static async Task Transport()
    {
        var options = new MediaHttpOptions(); options.Headers["Authorization"] = "Bearer fixture"; options.Headers["Cookie"] = "fixture=secret"; using var http = Http([], options: options, observe: r => Assert(!r.Headers.Contains("Authorization") && !r.Headers.Contains("Cookie"), "Caller credentials reached public metadata.")); _ = await new AudiusProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
        using var stop = new CancellationTokenSource(); stop.Cancel(); await Expect<OperationCanceledException>(() => new AudiusProvider().ExtractAsync(Request(Path.GetTempPath()), http, stop.Token));
        using var denied = Http([], status: HttpStatusCode.Forbidden); await Expect<HttpRequestException>(() => new AudiusProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
        using var bad = Http([], raw: "bad JSON"); await Expect<InvalidDataException>(() => new AudiusProvider().ExtractAsync(Request(Path.GetTempPath()), bad, default));
        using var redirected = Http([], redirect: "https://other.example/data"); await Expect<InvalidDataException>(() => new AudiusProvider().ExtractAsync(Request(Path.GetTempPath()), redirected, default));
        using var canonical = Http([], redirect: "https://api.audius.co/v1/tracks/by_permalink/fixture/song"); Assert((await new AudiusProvider().ExtractAsync(Request(Path.GetTempPath()) with { Url = new("https://audius.co/fixture/song") }, canonical, default)).Id == Id, "Bound publisher resolver redirect was rejected.");
    }
    private static async Task<PageMetadata> Extract(List<Uri> calls, JsonNode? track = null, NativeDownloadRequest? request = null, JsonNode? playlist = null, JsonNode[]? playlistTracks = null, JsonNode? user = null, JsonNode[]? artistTracks = null)
    { using var http = Http(calls, track, playlist, playlistTracks, user, artistTracks); return await new AudiusProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static MediaHttp Http(List<Uri> calls, JsonNode? track = null, JsonNode? playlist = null, JsonNode[]? playlistTracks = null, JsonNode? user = null, JsonNode[]? artistTracks = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null, MediaHttpOptions? options = null, Action<HttpRequestMessage>? observe = null, string? raw = null, HttpStatusCode status = HttpStatusCode.OK, string? redirect = null) => new(new Handler(r =>
    {
        calls.Add(r.RequestUri!); observe?.Invoke(r); var uri = r.RequestUri!; if (uri.Host != "api.audius.co" || uri.AbsolutePath.EndsWith("/stream")) return media?.Invoke(r) ?? throw new InvalidOperationException("Unexpected Audius playback request.");
        JsonNode data;
        if (uri.AbsolutePath == "/v1/resolve") data = uri.Query.Contains("playlist") ? new JsonArray(playlist?.DeepClone() ?? Playlist()) : track?.DeepClone() ?? Track();
        else if (uri.AbsolutePath.StartsWith("/v1/tracks/")) data = uri.AbsolutePath.EndsWith(Other) ? Track(Other) : track?.DeepClone() ?? Track();
        else if (uri.AbsolutePath.StartsWith("/v1/playlists/")) data = uri.AbsolutePath.EndsWith("/tracks") ? new JsonArray((playlistTracks ?? [Track(), Track(Other)]).Select(node => node.DeepClone()).ToArray()) : new JsonArray(playlist?.DeepClone() ?? Playlist());
        else data = uri.AbsolutePath.EndsWith("/tracks") ? new JsonArray((artistTracks ?? [Track(), Track(Other)]).Select(node => node.DeepClone()).ToArray()) : user?.DeepClone() ?? User();
        return new(status) { Content = new StringContent(raw ?? new JsonObject { ["data"] = data }.ToJsonString()), RequestMessage = redirect is null ? r : new(HttpMethod.Get, redirect) };
    }), options ?? new());
    private static async Task Temporary(Func<string, Task> test) { var directory = Path.Combine(Path.GetTempPath(), "audius-check-" + Guid.NewGuid()); Directory.CreateDirectory(directory); try { await test(directory); } finally { Directory.Delete(directory, true); } }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
}
