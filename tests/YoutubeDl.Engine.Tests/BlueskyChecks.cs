using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Bluesky;

internal static class BlueskyChecks
{
    internal const string Did = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string Cid = "bafkreih7qpo7ef7hhtm7ehi7paimlieddrn5argpq4eayz6teez2qbsr7m";
    internal const string CaptionCid = "bafkreiaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string RecordCid = "bafyreiaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new("https://bsky.app/profile/fixture.example/post/first"), OutputDirectory = directory, FileName = "bluesky", Retries = 0 };
    internal static string Record(string key = "first", string did = Did) => "at://" + did + "/app.bsky.feed.post/" + key;
    internal static JsonNode Post(string key = "first", string did = Did) => new JsonObject { ["thread"] = new JsonObject { ["$type"] = "app.bsky.feed.defs#threadViewPost", ["post"] = new JsonObject {
        ["uri"] = Record(key, did), ["cid"] = RecordCid, ["author"] = new JsonObject { ["did"] = did, ["displayName"] = "Fixture author", ["handle"] = "fixture.example" },
        ["record"] = new JsonObject { ["$type"] = "app.bsky.feed.post", ["text"] = "Native post title\nFull description", ["embed"] = new JsonObject { ["$type"] = "app.bsky.embed.video", ["video"] = Blob(Cid, "video/mp4", 1000),
            ["aspectRatio"] = new JsonObject { ["width"] = 16, ["height"] = 9 }, ["captions"] = new JsonArray(new JsonObject { ["lang"] = "en", ["file"] = Blob(CaptionCid, "text/vtt", 100) }) } },
        ["embed"] = new JsonObject { ["$type"] = "app.bsky.embed.video#view", ["cid"] = Cid,
            ["playlist"] = "https://video.bsky.app/watch/" + Uri.EscapeDataString(did) + "/" + Cid + "/playlist.m3u8?Key=A%2Bb&case=C%2Fd",
            ["thumbnail"] = "https://video.bsky.app/watch/" + Uri.EscapeDataString(did) + "/" + Cid + "/thumbnail.jpg" } } } };
    private static JsonNode Blob(string cid, string mime, int size) => new JsonObject { ["$type"] = "blob", ["mimeType"] = mime, ["size"] = size, ["ref"] = new JsonObject { ["$link"] = cid } };
    internal static JsonNode Document(string did = Did) => new JsonObject { ["id"] = did, ["alsoKnownAs"] = new JsonArray("at://fixture.example"), ["service"] = new JsonArray(new JsonObject {
        ["id"] = "#atproto_pds", ["type"] = "AtprotoPersonalDataServer", ["serviceEndpoint"] = "https://pds.example.test" }) };
    internal static JsonNode Quote()
    {
        var root = Post(); var p = root["thread"]!["post"]!;
        p["record"]!["embed"] = new JsonObject { ["$type"] = "app.bsky.embed.record", ["record"] = new JsonObject { ["uri"] = Record("second"), ["cid"] = RecordCid } };
        p["embed"] = new JsonObject { ["$type"] = "app.bsky.embed.record#view", ["record"] = new JsonObject { ["$type"] = "app.bsky.embed.record#viewRecord", ["uri"] = Record("second"), ["cid"] = RecordCid } };
        return root;
    }
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Bluesky native video retains supplied HLS original captions and stable author identity", Facts),
        ("Bluesky DID PLC and DID web aliases resolve without handle lookups", Aliases),
        ("Bluesky post address boundaries exclude unrelated hosts feeds and invalid identities", Boundaries),
        ("Bluesky account post blob and resource mismatches fail before media", Mismatches),
        ("Bluesky unavailable authenticated and malformed records remain explicit failures", Unavailable),
        ("Bluesky quoted records bind revisions while own attached video takes precedence", Quotes),
        ("Bluesky quote restrictions cycles and mismatched revisions cannot be bypassed", QuoteFailures),
        ("Bluesky original and caption descriptors enforce types sizes and language uniqueness", Descriptors),
        ("Bluesky public discovery strips caller credentials across all origins", Anonymous),
        ("Bluesky post archives remain distinct across authors and aliases", Archive),
        ("Bluesky cancellation stops follow-up metadata and HTTP denial remains explicit", Cancellation)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var info = await Engine(calls).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Id == "first" && info.Title == "Native post title" && info.Uploader == "Fixture author" && info.Formats.Count == 2 && info.Subtitles.Single().Language == "en", "Post metadata or resources changed.");
        Assert(info.Formats[0].Url!.Query == "?Key=A%2Bb&case=C%2Fd" && info.Formats[1].Id == "original" && info.Formats[1].Height is null && calls.Count == 3, "Signed URL changed, aspect ratio became pixel dimensions, or unexpected requests occurred.");
    }
    private static async Task Aliases()
    {
        foreach (var did in new[] { Did, "did:web:fixture.example" })
        { var calls = new List<Uri>(); var info = await Engine(calls, Post(did: did), Document(did)).InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://bsky.app/profile/" + did + "/post/first") }, default);
            Assert(info.Formats.Count == 2 && calls.Count == 2 && !calls.Any(url => url.AbsolutePath.Contains("resolveHandle")), "A DID address was resolved as a handle."); }
    }
    private static Task Boundaries()
    {
        var provider = new BlueskyProvider();
        foreach (var url in new[] { "https://bsky.app.evil.example/profile/fixture.example/post/first", "https://user@bsky.app/profile/fixture.example/post/first", "https://bsky.app:444/profile/fixture.example/post/first", "https://bsky.app/profile/fixture.example", "https://bsky.app/profile/did:plc:bad/post/first", "https://bsky.app/profile/did:web:fixture.example:other/post/first" })
            Assert(!provider.CanHandle(new(url)), "Unsupported Bluesky address claimed.");
        return Task.CompletedTask;
    }
    private static async Task Mismatches()
    {
        foreach (var change in new Action<JsonNode>[] { p => p["uri"] = Record("other"), p => p["author"]!["did"] = "did:web:other.example", p => p["embed"]!["cid"] = CaptionCid,
            p => p["embed"]!["playlist"] = "https://video.bsky.app/watch/other/" + Cid + "/playlist.m3u8" })
        { var root = Post(); change(root["thread"]!["post"]!); await Expect<InvalidDataException>(() => Engine([], root).InspectAsync(Request(Path.GetTempPath()), default)); }
        foreach (var change in new Action<JsonNode>[] { d => d["id"] = "did:web:other.example", d => d["alsoKnownAs"] = new JsonArray("at://other.example") })
        { var d = Document(); change(d); await Expect<InvalidDataException>(() => Engine([], document: d).InspectAsync(Request(Path.GetTempPath()), default)); }
        var bad = Document(); bad["service"]![0]!["serviceEndpoint"] = "https://user:secret@pds.example.test/path";
        await Expect<NotSupportedException>(() => Engine([], document: bad).InspectAsync(Request(Path.GetTempPath()), default));
    }
    private static async Task Unavailable()
    {
        foreach (var change in new Action<JsonNode>[] { d => d["thread"]!["$type"] = "app.bsky.feed.defs#blockedPost", d => d["thread"]!["post"]!["labels"] = new JsonArray(new JsonObject { ["val"] = "!no-unauthenticated" }),
            d => d["thread"]!["post"]!["embed"]!["$type"] = "app.bsky.embed.images#view" })
        { var d = Post(); change(d); await Expect<NotSupportedException>(() => Engine([], d).InspectAsync(Request(Path.GetTempPath()), default)); }
        await Expect<InvalidDataException>(() => Engine([], responseText: "bad JSON").InspectAsync(Request(Path.GetTempPath()), default));
    }
    private static async Task Quotes()
    {
        var calls = new List<Uri>(); var info = await Engine(calls, Quote()).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Id == "second" && calls.Count == 4, "A quote did not resolve its actual video post.");
        var own = Post(); var p = own["thread"]!["post"]!; var originalRecord = p["record"]!["embed"]!.DeepClone(); var originalView = p["embed"]!.DeepClone();
        p["record"]!["embed"] = new JsonObject { ["$type"] = "app.bsky.embed.recordWithMedia", ["media"] = originalRecord };
        p["embed"] = new JsonObject { ["$type"] = "app.bsky.embed.recordWithMedia#view", ["media"] = originalView, ["record"] = new JsonObject { ["$type"] = "app.bsky.embed.record#viewBlocked" } };
        calls.Clear(); var direct = await Engine(calls, own).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(direct.Id == "first" && calls.Count == 3, "Own video was replaced by a quoted record.");
    }
    private static async Task QuoteFailures()
    {
        var blocked = Quote(); blocked["thread"]!["post"]!["embed"]!["record"]!["$type"] = "app.bsky.embed.record#viewBlocked";
        var calls = new List<Uri>(); await Expect<NotSupportedException>(() => Engine(calls, blocked).InspectAsync(Request(Path.GetTempPath()), default));
        Assert(calls.Count == 2, "A blocked quote triggered another lookup.");
        var changed = Post("second"); changed["thread"]!["post"]!["cid"] = Cid;
        await Expect<InvalidDataException>(() => Engine([], Quote(), second: changed).InspectAsync(Request(Path.GetTempPath()), default));
        var cycle = Quote(); var p = cycle["thread"]!["post"]!; p["record"]!["embed"]!["record"]!["uri"] = Record(); p["embed"]!["record"]!["uri"] = Record();
        await Expect<InvalidDataException>(() => Engine([], cycle).InspectAsync(Request(Path.GetTempPath()), default));
        var unconfirmed = Document(); unconfirmed["alsoKnownAs"] = new JsonArray("at://other.example");
        await Expect<InvalidDataException>(() => Engine([], Quote(), unconfirmed).InspectAsync(Request(Path.GetTempPath()), default));
    }
    private static async Task Descriptors()
    {
        foreach (var change in new Action<JsonNode>[] { e => e["video"]!["mimeType"] = "text/html", e => e["video"]!["size"] = 0,
            e => e["captions"]![0]!["file"]!["size"] = 20001, e => e["captions"]![0]!["lang"] = "../en", e => ((JsonArray)e["captions"]!).Add(e["captions"]![0]!.DeepClone()) })
        { var p = Post(); change(p["thread"]!["post"]!["record"]!["embed"]!); await Expect<InvalidDataException>(() => Engine([], p).InspectAsync(Request(Path.GetTempPath()), default)); }
    }
    private static async Task Anonymous()
    {
        var count = 0; await Engine([], observe: message => { count++; Assert(!message.Headers.Contains("Authorization") && !message.Headers.Contains("Cookie"), "Caller credentials reached public federated origins."); }, credentials: true).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(count == 3, "The public discovery flow did not reach all expected origins.");
    }
    private static async Task Archive()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bluesky-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "archive.txt");
        try { await File.WriteAllTextAsync(path, "bluesky:post " + Did + "/first\n"); var calls = new List<Uri>();
            var result = await Engine(calls).DownloadAsync(Request(Path.GetTempPath()) with { DownloadArchiveFile = path }, null, null, default);
            Assert(result.Succeeded && result.Skipped && calls.Count == 3, "The archived post requested playback.");
            var other = "did:web:other.example"; calls.Clear();
            var different = await Engine(calls, Post(did: other), Document(other)).DownloadAsync(Request(directory) with { Url = new("https://bsky.app/profile/" + other + "/post/first"), DownloadArchiveFile = path, SkipDownload = true, WriteInfoJson = true }, null, null, default);
            Assert(different.Succeeded && different.SidecarPaths.Any(File.Exists), "Equal post keys on different accounts collided."); }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task Cancellation()
    {
        using var stop = new CancellationTokenSource(); var calls = new List<Uri>();
        await Expect<OperationCanceledException>(() => Engine(calls, observe: _ => stop.Cancel()).InspectAsync(Request(Path.GetTempPath()), stop.Token));
        Assert(calls.Count == 1, "Canceled discovery continued.");
        await Expect<HttpRequestException>(() => Engine([], status: HttpStatusCode.Forbidden).InspectAsync(Request(Path.GetTempPath()), default));
    }
    internal static NativeEngine Engine(List<Uri> calls, JsonNode? post = null, JsonNode? document = null, JsonNode? second = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null,
        Action<HttpRequestMessage>? observe = null, bool credentials = false, HttpStatusCode status = HttpStatusCode.OK, string? responseText = null) => new(httpFactory: _ =>
    {
        var options = new MediaHttpOptions(); if (credentials) { options.Headers["Authorization"] = "Bearer fixture"; options.Headers["Cookie"] = "fixture=secret"; }
        return new MediaHttp(new Handler(message =>
        {
            var url = message.RequestUri!; calls.Add(url); observe?.Invoke(message);
            JsonNode? data = url.Host == "public.api.bsky.app" ? url.AbsolutePath.Contains("resolveHandle") ? new JsonObject { ["did"] = Did } : Uri.UnescapeDataString(url.Query).Contains("/second") ? second ?? Post("second") : post ?? Post()
                : url.Host is "plc.directory" or "fixture.example" or "other.example" ? document ?? Document() : null;
            if (data is not null) return new(status) { Content = new StringContent(responseText ?? data.ToJsonString()) };
            return media?.Invoke(message) ?? throw new InvalidOperationException("Unexpected Bluesky media request.");
        }), options);
    });
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
}
