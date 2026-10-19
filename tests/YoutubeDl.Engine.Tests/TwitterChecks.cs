using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Twitter;

internal static class TwitterChecks
{
    internal const string Id = "2039908597748940872";
    internal const string Asset = "2039908545085198336";
    internal const string Other = "2040231537371725888";
    internal const string Query = "?tag=12&token=A%2Bb&case=C%2Fd";
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new("https://x.com/NASA/status/" + Id), OutputDirectory = directory, FileName = "twitter", Retries = 0 };
    internal static JsonNode Data() => new JsonObject { ["__typename"] = "Tweet", ["id_str"] = Id, ["text"] = "Fixture post text", ["user"] = new JsonObject { ["name"] = "Fixture author", ["screen_name"] = "FixtureAuthor" },
        ["mediaDetails"] = new JsonArray(Video()) };
    internal static JsonNode Video(string asset = Asset, double duration = 2, bool gif = false) => new JsonObject {
        ["type"] = gif ? "animated_gif" : "video", ["media_url_https"] = "https://pbs.twimg.com/media/fixture.jpg", ["original_info"] = new JsonObject { ["width"] = 160, ["height"] = 96 },
        ["ext_media_availability"] = new JsonObject { ["status"] = "Available" }, ["video_info"] = new JsonObject { ["duration_millis"] = duration * 1000,
            ["variants"] = gif ? new JsonArray(new JsonObject { ["content_type"] = "video/mp4", ["url"] = "https://video.twimg.com/tweet_video/fixture.mp4" + Query }) :
                new JsonArray(new JsonObject { ["content_type"] = "application/x-mpegURL", ["url"] = "https://video.twimg.com/amplify_video/" + asset + "/pl/master.m3u8" + Query },
                    new JsonObject { ["content_type"] = "video/mp4", ["bitrate"] = 832000, ["url"] = "https://video.twimg.com/amplify_video/" + asset + "/vid/avc1/160x96/video.mp4" + Query }) } };
    internal static MediaHttp Http(JsonNode data, List<Uri>? calls = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null) => new(new Handler(request =>
    {
        calls?.Add(request.RequestUri!);
        return request.RequestUri!.Host == "cdn.syndication.twimg.com" ? new(HttpStatusCode.OK) { Content = new StringContent(data.ToJsonString()) } :
            media?.Invoke(request) ?? new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
    }), new() { AllowAutoRedirect = false });
    private static async Task<PageMetadata> Read(JsonNode? data = null, NativeDownloadRequest? request = null)
    { using var http = Http(data ?? Data()); return await new TwitterProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Twitter public embed checksum matches primary JavaScript numeric vectors", () => Sync(Tokens)),
        ("Twitter public metadata preserves post media queries dimensions duration and archive identity", Facts),
        ("Twitter X mobile embed and selected-media aliases retain canonical identity", Aliases),
        ("Twitter public metadata rejects tombstones mismatched posts and mixed video assets", Boundaries),
        ("Twitter photo positions and multiple videos retain original indexes and media archives", Multiple),
        ("Twitter unavailable video positions preserve other offered videos", Unavailable),
        ("Twitter oversized media inventories fail without truncating positions", Limits),
        ("Twitter animated GIF video resources stay silent and retain declared dimensions", Gifs),
        ("Twitter quoted video posts delegate to their original native post identity", Quotes),
        ("Twitter archived public videos skip media and related-file transfers", Archive),
        ("Twitter embedded post HTML delegates through the native provider", Embedded)
    ];
    private static void Tokens()
    {
        foreach (var vector in new[] { ("1", "bhi2ay3f28n"), ("999999999999999", "353i5ab8p54"), ("1362506126839848961", "3awft943jfu"),
            (Id, "4yk86cwsl4"), (Other, "4y1kryhzu6"), ("18446744073709551615", "18ps5lqos4lc") })
            Assert(TwitterEmbedToken.Create(vector.Item1) == vector.Item2, "The C# public embed checksum differs from its JavaScript reference for " + vector.Item1 + ".");
    }
    private static async Task Facts()
    {
        var page = await Read();
        Assert(page.Id == Id && page.Media.Count == 2 && page.Media.Single(item => item.Kind == MediaKind.Progressive).Height == 96 && page.Media.All(item => item.Url.Query == Query),
            "Post identity, offered formats, dimensions or literal query values changed.");
        Assert(page.DurationSeconds == 2 && page.ArchiveIdentity?.Extractor == "twitter" && page.ArchiveIdentity.Id == Id && page.AnonymousPlayback && page.ThumbnailUrl == "https://pbs.twimg.com/media/fixture.jpg", "Metadata/media availability or recording archive changed.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "https://twitter.com/NASA/status/" + Id, "https://mobile.twitter.com/NASA/status/" + Id, "https://x.com/i/web/status/" + Id,
            "https://platform.twitter.com/embed/Tweet.html?id=" + Id, "https://x.com/NASA/status/" + Id + "/video/1" })
            Assert((await Read(request: Request(Path.GetTempPath()) with { Url = new(url) })).Id == Id, "Alias changed post identity.");
        await Reject<NotSupportedException>(() => Read(request: Request(Path.GetTempPath()) with { Url = new("https://x.com/NASA") }));
    }
    private static async Task Boundaries()
    {
        var data = Data(); data["id_str"] = Other; await Reject<InvalidDataException>(() => Read(data));
        data = new JsonObject { ["__typename"] = "TweetTombstone", ["id_str"] = Id }; await Reject<NotSupportedException>(() => Read(data));
        data = Data(); data["mediaDetails"]![0]!["video_info"]!["variants"]![1]!["url"] = "https://video.twimg.com/amplify_video/" + Other + "/vid/avc1/160x96/other.mp4";
        await Reject<InvalidDataException>(() => Read(data));
        data = Data(); data["mediaDetails"]![0]!["ext_media_availability"]!["status"] = "Unavailable"; await Reject<NotSupportedException>(() => Read(data));
    }
    private static async Task Multiple()
    {
        var data = Data(); data["mediaDetails"] = new JsonArray(new JsonObject { ["type"] = "photo" }, Video(), Video(Other));
        var page = await Read(data); Assert(page.IsPlaylist && page.Entries.Count == 3 && !page.Entries[0].Available && page.Entries[1].Position == 2 && page.Entries[2].ExpectedArchiveIdentity?.Id == Id + "-3", "Media inventory renumbered videos or shared archive keys.");
        page = await Read(data, Request(Path.GetTempPath()) with { Url = new("https://x.com/NASA/status/" + Id + "/video/3") });
        Assert(!page.IsPlaylist && page.Id == Id + "-3" && page.Media.All(item => item.Url.AbsolutePath.Contains(Other)), "Selected media index downloaded another video.");
        await Reject<NotSupportedException>(() => Read(data, Request(Path.GetTempPath()) with { Url = new("https://x.com/NASA/status/" + Id + "/photo/1") }));
    }
    private static async Task Gifs()
    {
        var data = Data(); data["mediaDetails"] = new JsonArray(Video(gif: true)); var page = await Read(data);
        Assert(page.Media.Count == 1 && page.Media[0].HasAudio == false && page.Media[0].IsCompleteMedia && page.Media[0].Height == 96, "GIF video resources invented audio or lost dimensions.");
    }
    private static async Task Unavailable()
    {
        var denied = Video(); denied["ext_media_availability"]!["status"] = "Unavailable";
        var data = Data(); data["mediaDetails"] = new JsonArray(denied, Video(Other));
        var page = await Read(data);
        Assert(page.IsPlaylist && page.Entries.Count == 2 && !page.Entries[0].Available && page.Entries[0].ResolvedMedia is null &&
            page.Entries[1].Available && page.Entries[1].Position == 2 && page.Entries[1].ResolvedMedia?.Id == Id + "-2" && page.Warnings.Count > 0,
            "An unavailable video prevented usable media or changed its original position.");
        await Reject<NotSupportedException>(() => Read(data, Request(Path.GetTempPath()) with { Url = new("https://x.com/NASA/status/" + Id + "/video/1") }));
        data["mediaDetails"]![0]!["ext_media_availability"]!["status"] = "Available";
        data["mediaDetails"]![0]!["video_info"]!["variants"]![1]!["url"] = "https://video.twimg.com/amplify_video/" + Other + "/vid/160x96/other.mp4";
        await Reject<InvalidDataException>(() => Read(data));
    }
    private static async Task Limits()
    {
        var data = Data(); data["mediaDetails"] = new JsonArray(new JsonObject { ["type"] = "photo" }, Video(), Video(Other));
        await Reject<InvalidDataException>(() => Read(data, Request(Path.GetTempPath()) with { MaximumPlaylistEntries = 2 }));
    }
    private static async Task Quotes()
    {
        var data = Data(); data["mediaDetails"] = new JsonArray(); var quoted = Data(); quoted["id_str"] = Other; data["quoted_tweet"] = quoted;
        var page = await Read(data); Assert(page.Media.Count == 0 && page.EmbeddedPages.Single().AbsolutePath.EndsWith(Other), "Quoted video ownership was attributed to the wrapper post.");
    }
    private static async Task Archive()
    {
        var directory = Path.Combine(Path.GetTempPath(), "native-twitter-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try { var archive = Path.Combine(directory, "archive.txt"); await File.WriteAllTextAsync(archive, "twitter " + Id + "\n"); var calls = new List<Uri>();
            var engine = new NativeEngine([new TwitterProvider()], _ => Http(Data(), calls)); var result = await engine.DownloadAsync(Request(directory) with { DownloadArchiveFile = archive }, null, null, default);
            Assert(result.Succeeded && result.Skipped && calls.All(url => url.Host == "cdn.syndication.twimg.com"), "Archive retry requested media or thumbnails."); }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task Embedded()
    {
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request => new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.Host == "article.example" ?
            "<iframe src='https://platform.twitter.com/embed/Tweet.html?id=" + Id + "'></iframe>" : Data().ToJsonString()) })));
        var info = await engine.InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://article.example/post") }, default);
        Assert(info.Extractor == "twitter" && info.Id == Id, "Known embedded posts did not use native extraction.");
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(run(request)); }
    private static Task Sync(Action test) { test(); return Task.CompletedTask; }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Reject<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
