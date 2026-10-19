using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Mastodon;

internal static class MastodonChecks
{
    internal const string Id = "123456789012345678";
    internal const string Query = "?Key=A%2Bb&case=C%2Fd";
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new("https://social.example/@fixture/" + Id), OutputDirectory = directory, OutputTemplate = "mastodon.%(ext)s", DownloadPlaylist = true };
    internal static JsonNode Attachment(string id = "attachment-a", string type = "video", double duration = 2) => new JsonObject {
        ["id"] = id, ["type"] = type, ["url"] = "https://cdn.example/attachments/" + id + "/original." + (type == "audio" ? "mp3" : "mp4") + Query,
        ["remote_url"] = "https://origin.example/remote.mp4", ["preview_url"] = "https://cdn.example/attachments/" + id + "/small.png", ["description"] = "Attachment alt text",
        ["meta"] = new JsonObject { ["original"] = new JsonObject { ["width"] = 160, ["height"] = 96, ["duration"] = duration, ["frame_rate"] = "24/1", ["bitrate"] = 100000 },
            ["small"] = new JsonObject { ["width"] = 64, ["height"] = 36 }, ["duration"] = duration, ["width"] = 160, ["height"] = 96 } };
    internal static JsonNode Post(params JsonNode[] attachments) => new JsonObject { ["id"] = Id, ["uri"] = "https://social.example/users/fixture/statuses/" + Id,
        ["url"] = "https://social.example/@fixture/" + Id, ["visibility"] = "public", ["content"] = "<p>Native &amp; complete</p><p>Second line<br>third</p>",
        ["spoiler_text"] = "", ["reblog"] = null, ["account"] = new JsonObject { ["id"] = "42", ["username"] = "fixture", ["acct"] = "fixture", ["display_name"] = "Fixture author", ["url"] = "https://social.example/@fixture" },
        ["media_attachments"] = new JsonArray(attachments.Length == 0 ? [Attachment()] : attachments) };
    internal static IEnumerable<(string Name, Func<Task> Test)> All => [
        ("Mastodon public video metadata retains supplied original facts and literal URLs", Facts),
        ("Mastodon federated status and embed aliases retain exact API routing", Aliases),
        ("Mastodon address boundaries exclude account feeds unrelated paths and unsafe origins", Boundaries),
        ("Mastodon status author origin and web identity mismatches fail before media", Mismatches),
        ("Mastodon cached remote posts retain original identity across local IDs", Remote),
        ("Mastodon boosts bind original media identity and reject private or recursive chains", Boosts),
        ("Mastodon multi-attachment inventory preserves unavailable positions and stable archives", Inventory),
        ("Mastodon inspected attachment selection survives reordered refreshed post inventories", Snapshot),
        ("Mastodon silent animations and audio attachments preserve their declared media roles", Roles),
        ("Mastodon malformed private unavailable and unsupported media remain explicit", Unavailable),
        ("Mastodon public discovery isolates caller cookies and authorization", Anonymous),
        ("Mastodon archive identities remain distinct across federated origins", Archives),
        ("Mastodon cancellation HTTP denial and redirected API cannot trigger playback", Cancellation)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var page = await Extract(Post(), calls);
        Assert(page.Id == Id && page.Title == "Native & complete" && page.Description == "Native & complete\nSecond line\nthird\n\nAttachment alt text" && page.Uploader == "Fixture author", "Public post text changed.");
        var media = page.Media.Single(); Assert(media.Url.Query == Query && media.Width == 160 && media.Height == 96 && media.FramesPerSecond == 24 && media.VerificationDurationSeconds == 2, "Preview facts or remote URL replaced the offered original.");
        Assert(calls.Single().AbsolutePath == "/api/v1/statuses/" + Id && page.ArchiveIdentity!.Id == "https://social.example/users/fixture/statuses/" + Id, "Status API/archive identity changed.");
    }
    private static async Task Aliases()
    {
        foreach (var path in new[] { "/@fixture/", "/users/fixture/statuses/", "/web/statuses/", "/statuses/" })
        foreach (var suffix in new[] { "", "/", "/embed" })
        { var page = await Extract(Post(), [], Request(Path.GetTempPath()) with { Url = new("https://social.example" + path + Id + suffix + "?utm_source=fixture") }); Assert(page.Id == Id, "A status alias changed its identity."); }
    }
    private static Task Boundaries()
    {
        var provider = new MastodonProvider();
        foreach (var value in new[] { "http://social.example/@fixture/" + Id, "https://social.example/@fixture", "https://social.example/@fixture/0", "https://social.example/@fixture/01",
            "https://social.example/@fixture/" + Id + "embed", "https://social.example/@fixture/" + Id + "/edit", "https://social.example/notes/" + Id,
            "https://social.example:444/@fixture/" + Id, "https://secret@social.example/@fixture/" + Id, "https://127.0.0.1/@fixture/" + Id, "https://social.example/@fixture/" + Id + "#x" })
            Assert(!provider.CanHandle(new(value)), "An unrelated or unsafe status address was claimed.");
        Assert(YoutubeDl.Engine.Providers.MediaProviderRegistry.CreateDefault().Resolve(new("https://fosstodon.org/@fixture/" + Id)).Info.Id == "mastodon", "Default dispatch missed Mastodon."); return Task.CompletedTask;
    }
    private static async Task Mismatches()
    {
        foreach (var change in new Action<JsonNode>[] { p => p["id"] = "42", p => p["url"] = "https://social.example/@fixture/42", p => p["uri"] = "https://social.example/users/other/statuses/" + Id,
            p => p["account"]!["url"] = "https://other.example/@fixture", p => p["account"]!["acct"] = "other", p => p["account"]!["username"] = "other" })
        { var post = Post(); change(post); await Expect<InvalidDataException>(() => Extract(post, [])); }
        await Expect<InvalidDataException>(() => Extract(Post(), [], Request(Path.GetTempPath()) with { Url = new("https://social.example/@other/" + Id) }));
    }
    private static JsonNode Cached()
    {
        var post = Post(); post["uri"] = "https://origin.example/users/fixture/statuses/987"; post["url"] = "https://origin.example/@fixture/987";
        post["account"]!["acct"] = "fixture@origin.example"; post["account"]!["url"] = "https://origin.example/@fixture"; return post;
    }
    private static async Task Remote()
    {
        var page = await Extract(Cached(), [], Request(Path.GetTempPath()) with { Url = new("https://social.example/@fixture@origin.example/" + Id) });
        Assert(page.Id == "987" && page.ArchiveIdentity!.Id == "https://origin.example/users/fixture/statuses/987", "Remote cache ID became an original media identity.");
        var anonymous = await Extract(Cached(), [], Request(Path.GetTempPath()) with { Url = new("https://social.example/web/statuses/" + Id) }); Assert(anonymous.Id == "987", "Anonymous cached status route failed.");
        await Expect<InvalidDataException>(() => Extract(Cached(), []));
    }
    private static JsonNode Boost()
    { var post = Post(); post["media_attachments"] = new JsonArray(); post["uri"] = "https://social.example/users/fixture/statuses/" + Id + "/activity";
        post["url"] = "https://social.example/users/fixture/statuses/" + Id + "/activity"; post["reblog"] = Cached(); return post; }
    private static async Task Boosts()
    {
        var page = await Extract(Boost(), []); Assert(page.Id == "987", "A boost downloaded the wrapper identity.");
        var privatePost = Boost(); privatePost["reblog"]!["visibility"] = "private"; await Expect<NotSupportedException>(() => Extract(privatePost, []));
        var mixed = Boost(); mixed["media_attachments"] = new JsonArray(Attachment()); await Expect<InvalidDataException>(() => Extract(mixed, []));
        var cycle = Boost(); cycle["reblog"] = Boost(); await Expect<InvalidDataException>(() => Extract(cycle, []));
        var repeatedLeaf = Boost(); var leaf = Post(); leaf["uri"] = "https://social.example/users/fixture/statuses/" + Id + "/activity"; repeatedLeaf["reblog"] = leaf; await Expect<InvalidDataException>(() => Extract(repeatedLeaf, []));
    }
    private static async Task Inventory()
    {
        var notReady = Attachment("pending"); notReady["url"] = null;
        var page = await Extract(Post(Attachment("image", "image"), Attachment(), notReady, Attachment("second")), []);
        Assert(page.IsPlaylist && page.Entries.Count == 4 && !page.Entries[0].Available && page.Entries[1].Position == 2 && !page.Entries[2].Available && page.Entries[3].Position == 4, "Attachment source positions changed.");
        Assert(page.Entries.Where(e => e.Available).Select(e => e.ExpectedArchiveIdentity!.Id).Distinct().Count() == 2, "Attachments share archive identity.");
        await Expect<NotSupportedException>(() => Extract(Post(Attachment(), Attachment("second")), [], Request(Path.GetTempPath()) with { MaximumPlaylistEntries = 1 }));
        await Expect<InvalidDataException>(() => Extract(Post(Attachment(), Attachment()), []));
    }
    private static async Task Roles()
    {
        var animation = (await Extract(Post(Attachment(type: "gifv")), [])).Media.Single(); Assert(animation.HasVideo == true && animation.HasAudio == false, "GIFV gained audio.");
        var audio = (await Extract(Post(Attachment(type: "audio")), [])).Media.Single(); Assert(audio.IsAudioOnly && audio.Height is null && audio.Width is null && audio.FramesPerSecond is null && audio.VerificationDemuxer == "mp3", "Audio inherited video or preview facts.");
        var unknown = Post(); unknown["media_attachments"]![0]!["meta"] = null; var media = (await Extract(unknown, [])).Media.Single(); Assert(media.Width is null && media.Height is null && media.VerificationDurationSeconds is null, "Absent metadata became invented dimensions.");
    }
    private static async Task Snapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mastodon-snapshot-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try
        {
            var current = Post(Attachment("image", "image"), Attachment(), Attachment("second")); var calls = new List<Uri>();
            var engine = new NativeEngine([new MastodonProvider()], _ => Http(current.ToJsonString(), calls));
            var request = Request(directory) with { OutputTemplate = "%(playlist_index)s-%(id)s.%(ext)s", PlaylistItems = "3", SkipDownload = true, WriteInfoJson = true };
            var info = await engine.InspectAsync(request, default); Assert(info.PlaylistSnapshot is not null, "Attachment inspection omitted its stable snapshot.");
            current = Post(Attachment("second"), Attachment("replacement"), Attachment());
            var result = await engine.DownloadAsync(request with { PlaylistSnapshot = info.PlaylistSnapshot }, null, null, default);
            using var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(result.SidecarPaths.Single()));
            Assert(result.Succeeded && calls.Count == 2 && json.RootElement.GetProperty("id").GetString() == Id + "-second" && json.RootElement.GetProperty("playlist_index").GetInt32() == 3,
                "Refreshed attachment selection became a nested post or changed source position.");
            Assert(!result.SidecarPaths.Any(path => path.Contains("replacement")), "A replacement attachment was selected by its new position.");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task Unavailable()
    {
        foreach (var visibility in new[] { "private", "direct", "unknown" }) { var post = Post(); post["visibility"] = visibility; await Expect<NotSupportedException>(() => Extract(post, [])); }
        var image = await Extract(Post(Attachment(type: "image")), []); Assert(image.Media.Count == 0 && image.Warnings.Count == 1, "Image-only status became playable video.");
        var suspended = Post(); suspended["account"]!["suspended"] = true; await Expect<NotSupportedException>(() => Extract(suspended, []));
        foreach (var url in new[] { "http://cdn.example/file.mp4", "https://secret@cdn.example/file.mp4", "https://cdn.example/file.mp4#fragment", "https://cdn.example/file.m3u8" })
        { var post = Post(); post["media_attachments"]![0]!["url"] = url; if (url.EndsWith("m3u8")) await Expect<NotSupportedException>(() => Extract(post, [])); else await Expect<InvalidDataException>(() => Extract(post, [])); }
        using var http = Http("bad JSON", []); await Expect<InvalidDataException>(() => new MastodonProvider().ExtractAsync(Request(Path.GetTempPath()), http, default));
        using var duplicate = Http(Post().ToJsonString().Replace("\"visibility\":\"public\"", "\"visibility\":\"private\",\"visibility\":\"public\""), []);
        await Expect<InvalidDataException>(() => new MastodonProvider().ExtractAsync(Request(Path.GetTempPath()), duplicate, default));
        var incomplete = Post(); incomplete["media_attachments"] = "missing inventory"; await Expect<InvalidDataException>(() => Extract(incomplete, []));
    }
    private static async Task Anonymous()
    {
        var options = new MediaHttpOptions(); options.Headers["Authorization"] = "Bearer fixture"; options.Headers["Cookie"] = "fixture=secret";
        using var http = Http(Post().ToJsonString(), [], options: options, observe: r => Assert(!r.Headers.Contains("Authorization") && !r.Headers.Contains("Cookie"), "Caller credentials reached public Mastodon metadata."));
        _ = await new MastodonProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
    }
    private static async Task Archives()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mastodon-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try
        {
            var archive = Path.Combine(directory, "archive.txt"); await File.WriteAllTextAsync(archive, "mastodon https://social.example/users/fixture/statuses/" + Id + "\n");
            var calls = new List<Uri>(); var engine = new NativeEngine([new MastodonProvider()], _ => Http(Post().ToJsonString(), calls));
            var result = await engine.DownloadAsync(Request(directory) with { DownloadArchiveFile = archive }, null, null, default);
            Assert(result.Succeeded && result.Skipped && calls.Count == 1, "Archived Mastodon post reached playback.");
            var other = Post(); other["uri"] = "https://other.example/users/fixture/statuses/" + Id; other["url"] = "https://other.example/@fixture/" + Id; other["account"]!["url"] = "https://other.example/@fixture";
            var page = await Extract(other, [], Request(directory) with { Url = new("https://other.example/@fixture/" + Id) }); Assert(page.ArchiveIdentity!.Id != "https://social.example/users/fixture/statuses/" + Id, "Equal federated IDs collided.");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task Cancellation()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel(); using var http = Http(Post().ToJsonString(), []);
        await Expect<OperationCanceledException>(() => new MastodonProvider().ExtractAsync(Request(Path.GetTempPath()), http, stop.Token));
        using var denied = Http(Post().ToJsonString(), [], status: HttpStatusCode.Forbidden); await Expect<HttpRequestException>(() => new MastodonProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
        using var redirected = Http(Post().ToJsonString(), [], redirect: true); await Expect<InvalidDataException>(() => new MastodonProvider().ExtractAsync(Request(Path.GetTempPath()), redirected, default));
    }
    private static async Task<YoutubeDl.Engine.Discovery.PageMetadata> Extract(JsonNode post, List<Uri> calls, NativeDownloadRequest? request = null)
    { using var http = Http(post.ToJsonString(), calls); return await new MastodonProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static MediaHttp Http(string json, List<Uri> calls, Func<HttpRequestMessage, HttpResponseMessage>? media = null, MediaHttpOptions? options = null,
        Action<HttpRequestMessage>? observe = null, HttpStatusCode status = HttpStatusCode.OK, bool redirect = false) => new(new Handler(r =>
        { calls.Add(r.RequestUri!); observe?.Invoke(r); if (r.RequestUri!.AbsolutePath.StartsWith("/api/")) return new(status) { Content = new StringContent(json), RequestMessage = redirect ? new HttpRequestMessage(HttpMethod.Get, "https://other.example/api/v1/statuses/42") : r };
            return media?.Invoke(r) ?? throw new InvalidOperationException("Unexpected Mastodon playback request."); }), options ?? new());
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
}
