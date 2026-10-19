using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;

internal static class RedditChecks
{
    internal const string Asset = "nativefixture1";
    internal const string Id = "abc123";
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Reddit metadata verifies post identity and prefers complete manifests over silent fallback", Metadata),
        ("Reddit native downloads choose offered HLS while preserving literal access parameters", Transfer),
        ("Reddit complete silent GIF-like resources do not require fabricated audio", Silent),
        ("Reddit video-only fallback cannot silently replace a complete video request", VideoOnly),
        ("Reddit aliases and supplied manifests retain distinct post and asset identities", Aliases),
        ("Reddit crossposts retain requested metadata and validate their source identity", Crossposts),
        ("Reddit external video posts delegate to the native destination provider", External),
        ("Reddit ambiguous, removed, processing and mismatched media fail before transfer", Invalid),
        ("Reddit access denial stops without alternate clients or endpoints", Denied),
        ("Reddit archives follow the actual post or direct asset and cancellation publishes nothing", ArchiveAndCancellation),
        ("Reddit constrained DASH cannot silently downgrade a video request to audio-only", ConstrainedDash)
    ];

    internal static JsonObject Post(bool gif = false, bool manifests = true) => new()
    {
        ["id"] = Id, ["name"] = "t3_" + Id, ["permalink"] = "/r/fixture/comments/" + Id + "/native/", ["title"] = "Native Reddit clip", ["author"] = "fixture-author", ["selftext"] = "Description",
        ["url_overridden_by_dest"] = "https://v.redd.it/" + Asset,
        ["secure_media"] = new JsonObject { ["reddit_video"] = new JsonObject {
            ["fallback_url"] = "https://v.redd.it/" + Asset + "/DASH_720.mp4?token=a%2Bb",
            ["hls_url"] = manifests ? "https://v.redd.it/" + Asset + "/HLSPlaylist.m3u8?token=a%2Bb" : null,
            ["dash_url"] = manifests ? "https://v.redd.it/" + Asset + "/DASHPlaylist.mpd?token=a%2Bb" : null,
            ["width"] = 1280, ["height"] = 720, ["duration"] = 12, ["is_gif"] = gif, ["transcoding_status"] = "completed" } },
        ["preview"] = new JsonObject { ["images"] = new JsonArray(new JsonObject {
            ["source"] = new JsonObject { ["url"] = "https://preview.redd.it/full.jpg?width=1280&amp;height=720", ["width"] = 1280, ["height"] = 720 },
            ["resolutions"] = new JsonArray(new JsonObject { ["url"] = "https://preview.redd.it/small.jpg", ["width"] = 320, ["height"] = 180 }) }) }
    };
    internal static string Listing(JsonObject post) => new JsonArray(new JsonObject { ["kind"] = "Listing", ["data"] = new JsonObject {
        ["children"] = new JsonArray(new JsonObject { ["kind"] = "t3", ["data"] = post.DeepClone() }) } },
        new JsonObject { ["kind"] = "Listing", ["data"] = new JsonObject { ["children"] = new JsonArray() } }).ToJsonString();
    internal static NativeDownloadRequest Request(string output) => new() { Url = new("https://www.reddit.com/r/fixture/comments/" + Id + "/native/"), OutputDirectory = output, FileName = "reddit", Retries = 0 };
    internal static NativeEngine Engine(JsonObject post, List<Uri> calls, Func<HttpRequestMessage, HttpResponseMessage>? media = null) => new(httpFactory: request => new MediaHttp(new Handler(message =>
    {
        calls.Add(message.RequestUri!);
        if (message.RequestUri!.Host == "www.reddit.com")
        {
            Assert(message.Headers.UserAgent.ToString() == (request.UserAgent ?? "YoutubeDlNative/" + NativeEngine.Version), "Reddit metadata ignored the native/caller client identity.");
            return Response(Listing(post));
        }
        return media?.Invoke(message) ?? (message.RequestUri.AbsolutePath.EndsWith(".m3u8") ? Response("#EXTM3U\n#EXTINF:1,\nclip.ts\n#EXT-X-ENDLIST") : Response("complete media"));
    })));
    private static async Task Metadata()
    {
        var calls = new List<Uri>(); var info = await Engine(Post(), calls).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Id == Id && info.Title == "Native Reddit clip" && info.Uploader == "fixture-author" && info.DurationSeconds == 12 && info.Thumbnails.Count == 2 &&
            info.Formats.Count == 3 && info.Formats.Single(format => format.Id == "hls").Height is null && info.Formats.Single(format => format.Id == "fallback") is { Height: 720, HasAudio: false },
            "Reddit metadata inferred manifest measurements, lost artwork, or described the fallback as containing audio.");
        Assert(calls.Single().AbsoluteUri == "https://www.reddit.com/comments/abc123/.json?raw_json=1&limit=0" && info.ThumbnailUrl!.Contains("&height=720"), "Post metadata routing or escaped artwork changed.");
    }
    private static async Task Transfer()
    {
        using var files = new Files(); var calls = new List<Uri>();
        var result = await Engine(Post(), calls).DownloadAsync(Request(files.Directory), null, null, default);
        Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == "complete media" && calls.Count == 3 && calls[1].AbsolutePath.EndsWith("HLSPlaylist.m3u8") &&
            calls[1].Query == "?token=a%2Bb" && calls.All(url => !url.AbsolutePath.EndsWith("DASH_720.mp4")), "Reddit chose a silent fallback instead of its offered complete manifest.");
    }
    private static async Task Silent()
    {
        using var files = new Files(); var calls = new List<Uri>(); var engine = Engine(Post(true, false), calls);
        var result = await engine.DownloadAsync(Request(files.Directory), null, null, default);
        Assert(result.Succeeded && calls.Count == 2 && File.ReadAllText(result.OutputPath!) == "complete media", "Complete GIF-like media incorrectly required a separate audio track.");
        calls.Clear();
        await Expect<InvalidOperationException>(() => engine.DownloadAsync(Request(files.Directory) with { AudioOnly = true }, null, null, default));
        Assert(calls.Count == 1, "Silent media was presented as an audio source.");
    }
    private static async Task VideoOnly()
    {
        using var files = new Files(); var calls = new List<Uri>(); var engine = Engine(Post(false, false), calls);
        await Expect<NotSupportedException>(() => engine.DownloadAsync(Request(files.Directory), null, null, default));
        Assert(calls.Count == 1, "Missing complete audio caused a silent default download.");
        var result = await engine.DownloadAsync(Request(files.Directory) with { VideoOnly = true }, null, null, default);
        Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == "complete media", "An explicit video-only fallback request was lost.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "https://redd.it/abc123", "https://old.reddit.com/r/fixture/comments/abc123/title/.json", "https://m.reddit.com/comments/abc123", "https://www.redditmedia.com/r/fixture/comments/abc123/title/" })
        {
            var info = await Engine(Post(), []).InspectAsync(Request(Path.GetTempPath()) with { Url = new(url), UserAgent = "Caller/1.0" }, default);
            Assert(info.Id == Id, "A Reddit post alias lost its identity.");
        }
        using var files = new Files(); var calls = new List<Uri>();
        var supplied = new Uri("https://v.redd.it/" + Asset + "/HLSPlaylist.m3u8?token=a%2Bb");
        var result = await Engine(Post(), calls).DownloadAsync(Request(files.Directory) with { Url = supplied }, null, null, default);
        Assert(result.Succeeded && calls.Count == 2 && calls[0] == supplied, "A supplied manifest unnecessarily fetched Reddit post metadata or changed its access parameters.");
        foreach (var invalid in new[] { "https://www.reddit.com/r/fixture/", "https://www.reddit.com/r/fixture/s/sharetoken", "https://www.reddit.com/r/fixture/comments/abc123/title/commentid", "https://www.reddit.com/comments/abc123/?t=3", "https://v.redd.it/" + Asset })
            await Expect<NotSupportedException>(() => Engine(Post(), []).InspectAsync(Request(files.Directory) with { Url = new(invalid) }, default));
    }
    private static async Task Crossposts()
    {
        var post = Post(); var parent = Post(); parent["id"] = "def456"; parent["name"] = "t3_def456"; parent["permalink"] = "/comments/def456/";
        post.Remove("secure_media"); post["title"] = "Shared title"; post["crosspost_parent"] = "t3_def456"; post["crosspost_parent_list"] = new JsonArray(parent);
        var info = await Engine(post, []).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Id == Id && info.Title == "Shared title" && info.Formats.Count == 3, "Crosspost extraction lost requested identity or source media.");
        post["crosspost_parent"] = "t3_wrong";
        await Expect<InvalidDataException>(() => Engine(post, []).InspectAsync(Request(Path.GetTempPath()), default));
    }
    private static async Task External()
    {
        using var files = new Files(); var post = Post(); post.Remove("secure_media"); post["url_overridden_by_dest"] = "https://www.youtube.com/watch?v=abcdefghijk";
        var result = await Engine(post, [], request => Response(request.RequestUri!.Host.Contains("youtube.com") ? YouTubeChecks.FixtureHtml : "external media"))
            .DownloadAsync(Request(files.Directory) with { FormatId = "18" }, null, null, default);
        Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == "external media", "An external video post did not delegate to its native provider.");
    }
    private static async Task Invalid()
    {
        foreach (var change in new Action<JsonObject>[] {
            post => post["id"] = "wrong", post => post["name"] = "t3_wrong", post => post["permalink"] = "/comments/wrong/",
            post => post["secure_media"]!["reddit_video"]!["dash_url"] = "https://v.redd.it/anotherasset/DASHPlaylist.mpd",
            post => post["secure_media"]!["reddit_video"]!["fallback_url"] = "https://v.redd.it.evil.example/asset/video.mp4" })
        {
            var post = Post(); change(post); var calls = new List<Uri>();
            await Expect<InvalidDataException>(() => Engine(post, calls).DownloadAsync(Request(Path.GetTempPath()), null, null, default));
            Assert(calls.Count == 1, "Invalid Reddit metadata reached media transfer.");
        }
        foreach (var field in new[] { "processing", "removed", "gallery", "private" })
        {
            var post = Post();
            if (field == "processing") post["secure_media"]!["reddit_video"]!["transcoding_status"] = "processing";
            else if (field == "removed") post["removed_by_category"] = "deleted";
            else if (field == "private") post["subreddit_type"] = "private";
            else post["is_gallery"] = true;
            await Expect<NotSupportedException>(() => Engine(post, []).InspectAsync(Request(Path.GetTempPath()), default));
        }
    }
    private static async Task Denied()
    {
        var calls = 0;
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(_ => { calls++; return new(HttpStatusCode.Forbidden); })));
        await Expect<HttpRequestException>(() => engine.InspectAsync(Request(Path.GetTempPath()), default));
        Assert(calls == 1, "Reddit denial triggered an alternate origin or client request.");
    }
    private static async Task ArchiveAndCancellation()
    {
        using var files = new Files(); var archive = Path.Combine(files.Directory, "archive.txt"); var calls = new List<Uri>();
        var engine = Engine(Post(true, false), calls); var request = Request(files.Directory) with { DownloadArchiveFile = archive };
        await engine.DownloadAsync(request, null, null, default); calls.Clear();
        var repeat = await engine.DownloadAsync(request, null, null, default);
        Assert(repeat.Skipped && calls.Count == 1 && File.ReadAllText(archive).Trim() == "reddit abc123", "Reddit archives used an unverified early identity or fetched recorded media.");
        File.WriteAllText(archive, "reddit:video " + Asset + "\n"); calls.Clear();
        repeat = await engine.DownloadAsync(request with { Url = new("https://v.redd.it/" + Asset + "/HLSPlaylist.m3u8") }, null, null, default);
        Assert(repeat.Skipped && calls.Count == 0, "A recorded direct media asset did not skip before HTTP.");
        using var stop = new CancellationTokenSource();
        engine = Engine(Post(), [], _ => { stop.Cancel(); return Response("fragment"); });
        await Expect<OperationCanceledException>(() => engine.DownloadAsync(Request(files.Directory) with { FileName = "canceled" }, null, null, stop.Token));
        Assert(!Directory.EnumerateFiles(files.Directory, "canceled.*").Any(), "Canceled Reddit downloading published media.");
    }
    private static async Task ConstrainedDash()
    {
        using var files = new Files(); var calls = new List<Uri>();
        const string mpd = "<MPD><Period><AdaptationSet mimeType='video/mp4'><Representation id='video' height='640' width='360' codecs='avc1.4d401e'><BaseURL>video.mp4</BaseURL></Representation></AdaptationSet><AdaptationSet mimeType='audio/mp4'><Representation id='audio' codecs='mp4a.40.2'><BaseURL>audio.m4a</BaseURL></Representation></AdaptationSet></Period></MPD>";
        var engine = Engine(Post(), calls, request => request.RequestUri!.AbsolutePath.EndsWith(".mpd") ? Response(mpd) : throw new InvalidOperationException("A rejected video request fetched an audio resource."));
        foreach (var limits in new[] { Request(files.Directory) with { MaximumHeight = 360 }, Request(files.Directory) with { VideoCodec = "vp9" } })
        {
            calls.Clear();
            await Expect<InvalidOperationException>(() => engine.DownloadAsync(limits with { FormatId = "dash" }, null, null, default));
            Assert(calls.Count == 2 && !Directory.EnumerateFiles(files.Directory, "reddit.*").Any(), "A video constraint published audio as the requested complete video.");
        }
        var page = new YoutubeDl.Engine.Discovery.PageMetadata { Media = [
            new() { Url = new("https://media.example/video.mp4"), Extension = "mp4", HasVideo = true, HasAudio = false, Height = 640 },
            new() { Url = new("https://media.example/audio.m4a"), Extension = "m4a", HasAudio = true }] };
        await Expect<InvalidOperationException>(() => Task.FromResult(YoutubeDl.Engine.Download.MediaDownloadPlan.Create(page, Request(files.Directory) with { MaximumHeight = 360 })));
        Assert(YoutubeDl.Engine.Download.MediaDownloadPlan.Create(page, Request(files.Directory) with { MaximumHeight = 360, AudioOnly = true }).Primary.IsAudioOnly,
            "Rejecting an implicit audio downgrade broke explicit audio selection.");
    }
    private static HttpResponseMessage Response(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request)); }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, "reddit-" + Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() { var path = Path.GetFullPath(Directory); if (!path.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Test folder escaped its root."); System.IO.Directory.Delete(path, true); }
    }
}
