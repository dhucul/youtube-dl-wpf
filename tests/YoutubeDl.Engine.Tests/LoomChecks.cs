using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Loom;

internal static class LoomChecks
{
    internal const string Id = "1234567890abcdef1234567890abcdef";
    internal const string Other = "abcdef1234567890abcdef1234567890";
    internal static string Prefix => "https://luna.loom.com/id/" + Id + "/rev/" + new string('a', 65) + "/resource/";
    internal static string Query => "?" + string.Join('&', ScopedMediaQueryChecks.Parameters);
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new("https://www.loom.com/share/" + Id), OutputDirectory = directory, FileName = "loom", Retries = 0 };
    internal static JsonNode State(bool adaptive = true, double duration = 2) => new JsonObject {
        ["ROOT_QUERY"] = new JsonObject { ["getVideo({\"id\":\"" + Id + "\",\"password\":null})"] = new JsonObject { ["__ref"] = "RegularUserVideo:" + Id },
            ["fetchVideoTranscript({\"videoId\":\"" + Id + "\"})"] = new JsonObject { ["__ref"] = "transcript" } },
        ["RegularUserVideo:" + Id] = new JsonObject { ["__typename"] = "RegularUserVideo", ["id"] = Id, ["complete"] = true, ["privacy"] = "public", ["name"] = "Fixture Loom",
            ["playable_duration"] = duration, ["source_duration"] = duration, ["hasTranscriptRecord"] = true, ["owner"] = new JsonObject { ["__ref"] = "owner" },
            ["video_properties"] = new JsonObject { ["duration"] = duration, ["height"] = 96, ["width"] = 160 },
            ["thumbnails"] = new JsonObject { ["default"] = "https://cdn.loom.com/sessions/thumbnails/" + Id + "-00001.jpg" },
            ["nullableRawCdnUrl({\"acceptableMimes\":[\"M3U8\"],\"password\":null})"] = adaptive ? new JsonObject { ["url"] = Prefix + "master.m3u8" + Query } : null,
            ["chapters"] = new JsonArray() },
        ["owner"] = new JsonObject { ["display_name"] = "Fixture speaker" },
        ["transcript"] = new JsonObject { ["video_id"] = Id, ["transcription_status"] = "success", ["processing_service"] = "rev_ai_async", ["language"] = "en",
            ["captions_source_url"] = "https://cdn.loom.com/mediametadata/captions/" + Id + "-1.vtt" + Query } };
    internal static JsonNode Video(JsonNode state) => state["RegularUserVideo:" + Id]!;
    internal static JsonNode Api() => new JsonObject { ["data"] = new JsonObject { ["getVideo"] = new JsonObject { ["__typename"] = "RegularUserVideo", ["id"] = Id,
        ["nullableRawCdnUrl"] = new JsonObject { ["__typename"] = "CloudfrontSignedUrlPayload", ["url"] = "https://cdn.loom.com/sessions/transcoded/" + Id + ".mp4" + Query } } } };
    internal static MediaHttp Http(JsonNode state, List<Uri>? calls = null, JsonNode? api = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null) =>
        new(new Handler(async request =>
        {
            calls?.Add(request.RequestUri!);
            if (request.RequestUri!.Host == "www.loom.com")
            {
                if (request.Method == HttpMethod.Post) { var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                    if (body["operationName"]!.GetValue<string>() != "NativeLoomMedia" || body["variables"]!["id"]!.GetValue<string>() != Id) throw new InvalidOperationException("Unexpected Loom media lookup.");
                    return new(HttpStatusCode.OK) { Content = new StringContent((api ?? Api()).ToJsonString()) }; }
                return new(HttpStatusCode.OK) { Content = new StringContent("<script>window.__APOLLO_STATE__ = " + state.ToJsonString() + ";</script>") };
            }
            return media?.Invoke(request) ?? new(HttpStatusCode.OK) { Content = new StringContent("WEBVTT\n\n00:00:00.100 --> 00:00:01.900\nFixture caption.\n") };
        }), new() { AllowAutoRedirect = false });
    private static async Task<PageMetadata> Read(JsonNode? state = null, NativeDownloadRequest? request = null, JsonNode? api = null)
    { using var http = Http(state ?? State(), api: api); return await new LoomProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Loom metadata preserves video identity progressive adaptive formats captions and scoped access", Facts),
        ("Loom public embed and community aliases use the authoritative share identity", Aliases),
        ("Loom private password incomplete and edited timelines fail before media transfer", Boundaries),
        ("Loom normalized metadata and API resource identities remain bound to the requested video", Identities),
        ("Loom legacy recordings retain MP4 playback when adaptive preload is absent", Legacy),
        ("Loom transcript resources remain video-bound and undeclared languages stay unknown", Captions),
        ("Loom JSON metadata parsing never evaluates script suffixes and rejects duplicate state", Parsing),
        ("Loom archive repeats skip media and related-file transfers", Archive),
        ("Loom native embedded-page routing preserves the requested video", Embedded)
    ];
    private static async Task Facts()
    {
        var page = await Read();
        Assert(page.Id == Id && page.Media.Count == 2 && page.Media.Single(item => item.Id == "hls").Height is null && page.Media.Single(item => item.Id == "mp4").Height == 96,
            "Identity or adaptive/progressive format facts changed.");
        Assert(page.PlaybackQueries.Count == 1 && page.AnonymousPlayback && page.Subtitles.Single().Automatic && page.Uploader == "Fixture speaker" && page.ArchiveIdentity?.Id == Id, "Scoped playback, caption provenance or metadata changed.");
    }
    private static async Task Aliases()
    {
        foreach (var path in new[] { "/embed/" + Id, "/community/" + Id + "-pg" })
            Assert((await Read(request: Request(Path.GetTempPath()) with { Url = new("https://www.loom.com" + path) })).Id == Id, "Alias changed video identity.");
        await Reject<NotSupportedException>(() => Read(request: Request(Path.GetTempPath()) with { Url = new("https://www.loom.com/share/folder/" + Id) }));
    }
    private static async Task Boundaries()
    {
        foreach (var field in new[] { "needs_password", "is_protected", "viewerNeedsPermission", "archived" }) { var state = State(); Video(state)[field] = true; await Reject<NotSupportedException>(() => Read(state)); }
        var data = State(); Video(data)["privacy"] = "private"; await Reject<NotSupportedException>(() => Read(data));
        data = State(); Video(data)["complete"] = false; await Reject<NotSupportedException>(() => Read(data));
        data = State(); Video(data)["source_duration"] = 4; await Reject<NotSupportedException>(() => Read(data));
    }
    private static async Task Identities()
    {
        var state = State(); Video(state)["id"] = Other; await Reject<InvalidDataException>(() => Read(state));
        var api = Api(); api["data"]!["getVideo"]!["id"] = Other; await Reject<InvalidDataException>(() => Read(api: api));
        api = Api(); api["data"]!["getVideo"]!["nullableRawCdnUrl"]!["url"] = "https://cdn.loom.com/sessions/transcoded/" + Other + ".mp4"; await Reject<InvalidDataException>(() => Read(api: api));
    }
    private static async Task Legacy()
    {
        var state = State(false); Video(state)["hasTranscriptRecord"] = false;
        var page = await Read(state); Assert(page.Media.Count == 1 && page.Media[0].Id == "mp4" && page.Subtitles.Count == 0, "Legacy preload absence prevented complete MP4 playback.");
    }
    private static async Task Captions()
    {
        var state = State(); state["transcript"]!["language"] = null; var page = await Read(state); Assert(page.Subtitles.Single().Language == "und", "Unknown caption language was invented.");
        state["transcript"]!["video_id"] = Other; await Reject<InvalidDataException>(() => Read(state));
        state = State(); state["transcript"]!["captions_source_url"] = "https://cdn.loom.com/mediametadata/captions/" + Other + "-1.vtt"; await Reject<InvalidDataException>(() => Read(state));
    }
    private static Task Parsing()
    {
        using var json = LoomProvider.ReadState("window.__APOLLO_STATE__=" + State().ToJsonString() + ";throw new Error('not executed');");
        Assert(json.RootElement.TryGetProperty("ROOT_QUERY", out _), "JSON metadata was not isolated from script suffixes.");
        try { LoomProvider.ReadState("window.__APOLLO_STATE__={};window.__APOLLO_STATE__={};"); throw new InvalidOperationException("Duplicate state was accepted."); } catch (NotSupportedException) { }
        return Task.CompletedTask;
    }
    private static async Task Archive()
    {
        var directory = Path.Combine(Path.GetTempPath(), "native-loom-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try { var archive = Path.Combine(directory, "archive.txt"); await File.WriteAllTextAsync(archive, "loom " + Id + "\n"); var calls = new List<Uri>();
            var engine = new NativeEngine([new LoomProvider()], _ => Http(State(), calls)); var result = await engine.DownloadAsync(Request(directory) with { DownloadArchiveFile = archive }, null, null, default);
            Assert(result.Succeeded && result.Skipped && calls.All(url => url.Host == "www.loom.com"), "An archived video fetched media/captions."); }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task Embedded()
    {
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content =
            new StringContent(request.RequestUri!.Host == "article.example" ? "<iframe src='https://www.loom.com/embed/" + Id + "'></iframe>" : request.Method == HttpMethod.Post ? Api().ToJsonString() : "window.__APOLLO_STATE__=" + State().ToJsonString() + ";") }))));
        var info = await engine.InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://article.example/guide") }, default);
        Assert(info.Extractor == "loom" && info.Id == Id, "Embedded pages did not use native Loom extraction.");
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => run(request); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Reject<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
