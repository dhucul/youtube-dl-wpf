using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Protocols;
using YoutubeDl.Engine.Protocols.Sabr;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.YouTube;

internal static class ObservedYouTubeChecks
{
    internal static readonly byte[] Proof = [9, 8, 7, 6, 5, 4];
    internal static byte[] ClientInfo(uint name = 1, string version = "browser.fixture", bool duplicate = false)
    { var w = new ProtobufWriter(); w.String(1, "browser-context"); w.UInt64(16, name); w.String(17, version); w.UInt64(18, 2); if (duplicate) w.UInt64(16, name); return w.ToArray(); }
    internal static SabrRequest Request() => new() { ClientName = 1, ClientVersion = "browser.fixture", ClientInfo = ClientInfo(), ProofToken = Proof,
        PlaybackConfiguration = [1, 2, 3], VideoFormats = [SabrFixture.Video], AudioFormats = [SabrFixture.Audio] };
    internal static PageMetadata Parse(byte[]? body = null, string? json = null, Uri? endpoint = null, string? id = null, CancellationToken ct = default) =>
        YouTubeObservedPlayback.Parse(json ?? SabrFixture.Player().ToJsonString(), id ?? SabrFixture.VideoId, endpoint ?? SabrFixture.Endpoint, body ?? Request().Encode(), "BrowserFixture/1", ct);
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Observed YouTube sessions retain the exact client context proof and offered media", Facts),
        ("Observed YouTube player configuration video and endpoint identities must agree", Identities),
        ("Observed YouTube initialized and buffered formats cannot belong to another video revision", Formats),
        ("Observed YouTube session secrets are copied and excluded from JSON serialization", Secrets),
        ("Observed YouTube request encoding rejects changed repeated and excessive client identity fields", Encoding),
        ("Observed YouTube playback proof does not override server denial or publish partial files", Denial),
        ("Observed YouTube cancellation and missing proof are explicit", Cancellation),
        ("Observed YouTube control-only refresh requires validated unchanged format history", ControlRefresh),
        ("Observed YouTube response evidence binds video revisions and respects denial", ResponseEvidence)
    ];
    private static Task Facts()
    {
        var page = Parse(); var session = page.Media[0].Sabr!.Session;
        Assert(page.Id == SabrFixture.VideoId && page.Media.Count == 2 && page.AnonymousPlayback && session.ClientName == 1 && session.ClientVersion == "browser.fixture", "Observed media lost its identity or anonymous scope.");
        Assert(session.ProofToken.SequenceEqual(Proof) && session.ClientInfo.SequenceEqual(ClientInfo()) && page.Media.All(media => ReferenceEquals(media.Sabr!.Session, session)), "Observed client context or shared session changed.");
        return Task.CompletedTask;
    }
    private static async Task Identities()
    {
        await Expect<InvalidDataException>(() => Task.FromResult(Parse(id: "other-video")));
        await Expect<InvalidDataException>(() => Task.FromResult(Parse((Request() with { PlaybackConfiguration = [4] }).Encode())));
        foreach (var endpoint in new[] { "https://googlevideo.com.evil.example/videoplayback", "https://user@r1.googlevideo.com/videoplayback", "https://r1.googlevideo.com:444/videoplayback", "https://r1.googlevideo.com/other" })
            await Expect<InvalidDataException>(() => Task.FromResult(Parse(endpoint: new(endpoint))));
        await Expect<NotSupportedException>(() => Task.FromResult(Parse((Request() with { ClientName = 5, ClientInfo = ClientInfo(5) }).Encode())));
        var unavailable = SabrFixture.Player(); unavailable["playabilityStatus"]!["status"] = "LOGIN_REQUIRED";
        await Expect<InvalidOperationException>(() => Task.FromResult(Parse(json: unavailable.ToJsonString())));
    }
    private static async Task Formats()
    {
        await Expect<InvalidDataException>(() => Task.FromResult(Parse((Request() with { VideoFormats = [SabrFixture.Video with { LastModified = 101 }] }).Encode())));
        await Expect<InvalidDataException>(() => Task.FromResult(Parse((Request() with { AudioFormats = [SabrFixture.Audio with { ExtraTags = "other" }] }).Encode())));
        foreach (var buffered in new[] { false, true })
        {
            var extra = new ProtobufWriter();
            void Format(ProtobufWriter format) { format.UInt64(1, 999); format.UInt64(2, 100); }
            if (buffered) extra.Message(3, range => range.Message(1, Format)); else extra.Message(2, Format);
            await Expect<InvalidDataException>(() => Task.FromResult(Parse(Request().Encode().Concat(extra.ToArray()).ToArray())));
        }
    }
    private static Task Secrets()
    {
        var body = Request().Encode(); var page = Parse(body); var session = page.Media[0].Sabr!.Session; Array.Fill(body, (byte)0);
        Assert(session.ProofToken.SequenceEqual(Proof) && session.ClientInfo.SequenceEqual(ClientInfo()), "A caller buffer changed the active session.");
        using var sessionJson = JsonDocument.Parse(JsonSerializer.Serialize(session)); using var requestJson = JsonDocument.Parse(JsonSerializer.Serialize(Request()));
        foreach (var doc in new[] { sessionJson, requestJson }) Assert(!doc.RootElement.TryGetProperty("ProofToken", out _) && !doc.RootElement.TryGetProperty("ClientInfo", out _), "Observed credentials were serialized into diagnostic JSON.");
        return Task.CompletedTask;
    }
    private static async Task Encoding()
    {
        foreach (var request in new[] { Request() with { ClientInfo = ClientInfo(5) }, Request() with { ClientInfo = ClientInfo(version: "different") }, Request() with { ClientInfo = ClientInfo(duplicate: true) }, Request() with { ProofToken = new byte[16385] } })
            await Expect<ArgumentException>(() => Task.FromResult(request.Encode()));
        var fields = ProtobufReader.Read(Request().Encode()); var context = ProtobufReader.Read(fields.Single(field => field.Number == 19).Bytes);
        Assert(context.Single(field => field.Number == 1).Bytes.Span.SequenceEqual(ClientInfo()) && context.Single(field => field.Number == 2).Bytes.Span.SequenceEqual(Proof), "Request encoding discarded observed client fields or proof bytes.");
    }
    private static async Task Denial()
    {
        using var files = new SabrFixture.Files(); var completed = 0; var calls = 0;
        var engine = new NativeEngine([new Provider()], _ => new MediaHttp(new SabrFixture.Handler(_ =>
        { calls++; using var response = new MemoryStream(); SabrFixture.Status(response, 3); return Task.FromResult(SabrFixture.Response(response.ToArray())); }), new MediaHttpOptions { AllowAutoRedirect = false }));
        await Expect<NotSupportedException>(() => engine.DownloadAsync(new NativeDownloadRequest { Url = new("https://www.youtube.com/watch?v=" + SabrFixture.VideoId), OutputDirectory = files.Directory,
            VideoOnly = true, FormatSelector = "137", DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt"), Retries = 0 }, null, null, default, _ => completed++));
        Assert(calls == 1 && completed == 0 && !Directory.EnumerateFileSystemEntries(files.Directory).Any(path => !path.EndsWith(".lock")), "A supplied proof bypassed denial or published output.");
    }
    private static async Task Cancellation()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Expect<OperationCanceledException>(() => Task.FromResult(Parse(ct: stop.Token)));
        await Expect<InvalidDataException>(() => Task.FromResult(Parse((Request() with { ProofToken = [] }).Encode())));
    }
    private static async Task ControlRefresh()
    {
        var original = Request().Encode(); var fields = ProtobufReader.Read(original); var control = new ProtobufWriter();
        foreach (var field in fields.Where(field => field.Number is 5 or 19)) control.Bytes(field.Number, field.Bytes.Span);
        var body = control.ToArray(); var player = SabrFixture.Player().ToJsonString();
        await Expect<InvalidDataException>(() => Task.FromResult(YouTubeObservedPlayback.Parse(player, SabrFixture.VideoId, SabrFixture.Endpoint, body)));
        var evidence = YouTubeObservedPlayback.Observe(player, SabrFixture.VideoId, SabrFixture.Endpoint, original);
        var page = YouTubeObservedPlayback.Parse(player, SabrFixture.VideoId, SabrFixture.Endpoint, body, previous: evidence);
        Assert(page.Id == SabrFixture.VideoId && page.Media.Count == 2 && evidence.Formats.Count == 2, "Validated control refresh lost its format/identity binding.");
        var changed = SabrFixture.Player(); changed["streamingData"]!["adaptiveFormats"]![0]!["lastModified"] = "101";
        await Expect<InvalidDataException>(() => Task.FromResult(YouTubeObservedPlayback.Parse(changed.ToJsonString(), SabrFixture.VideoId, SabrFixture.Endpoint, body, previous: evidence)));
        var other = SabrFixture.Player(); other["videoDetails"]!["videoId"] = "other-video";
        await Expect<InvalidDataException>(() => Task.FromResult(YouTubeObservedPlayback.Parse(other.ToJsonString(), "other-video", SabrFixture.Endpoint, body, previous: evidence)));
    }
    private static async Task ResponseEvidence()
    {
        using var stream = new MemoryStream(); SabrFixture.Status(stream, 2); SabrFixture.Metadata(stream, SabrFixture.Video, 1, 1000); SabrFixture.Metadata(stream, SabrFixture.Audio, 1, 1000);
        var bytes = stream.ToArray(); var evidence = await YouTubeObservedPlayback.ObserveResponseAsync(SabrFixture.VideoId, bytes);
        Assert(evidence.Formats.Count == 2 && evidence.VideoId == SabrFixture.VideoId, "Response evidence lost its declared tracks.");
        await Expect<InvalidDataException>(() => YouTubeObservedPlayback.ObserveResponseAsync("other-video", bytes));
        using var denied = new MemoryStream(); SabrFixture.Status(denied, 3); SabrFixture.Metadata(denied, SabrFixture.Video, 1, 1000);
        await Expect<NotSupportedException>(() => YouTubeObservedPlayback.ObserveResponseAsync(SabrFixture.VideoId, denied.ToArray()));
        using var empty = new MemoryStream(); SabrFixture.Status(empty, 1);
        await Expect<InvalidDataException>(() => YouTubeObservedPlayback.ObserveResponseAsync(SabrFixture.VideoId, empty.ToArray()));
    }
    internal sealed class Provider : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("observed-youtube-fixture", "Observed YouTube fixture", 1);
        public bool CanHandle(Uri url) => true;
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(Parse(ct: ct));
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
