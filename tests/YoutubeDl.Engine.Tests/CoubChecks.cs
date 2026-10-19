using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Coub;

internal static class CoubChecks
{
    internal const string Id = "2zrym";
    internal const string Signed = "?Key=A%2Bb&case=C%2Fd";
    internal const string Origin = "https://attachments-cdn-s.coub.com/coub_storage/coub/simple/fixture/";
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new("https://coub.com/view/" + Id), OutputDirectory = directory, OutputTemplate = "coub.%(ext)s" };
    internal static JsonNode Record(bool sound = true, double duration = 2) => new JsonObject { ["id"] = 5781888, ["type"] = "Coub::Simple", ["permalink"] = Id, ["title"] = "Native Coub loop",
        ["visibility_type"] = "public", ["published"] = true, ["is_done"] = true, ["banned"] = false, ["moderation_state"] = "initial", ["age_restricted"] = false, ["age_restricted_by_admin"] = false,
        ["has_sound"] = sound, ["duration"] = duration, ["channel"] = new JsonObject { ["title"] = "Fixture channel" }, ["picture"] = Origin + "poster.jpg",
        ["dimensions"] = new JsonObject { ["big"] = new JsonArray(160, 96), ["med"] = new JsonArray(80, 48) }, ["file_versions"] = new JsonObject { ["html5"] = new JsonObject {
            ["video"] = new JsonObject { ["high"] = Descriptor("video-high.mp4", 1000), ["med"] = Descriptor("video-med.mp4", 500) },
            ["audio"] = sound ? new JsonObject { ["high"] = Descriptor("audio-high.mp3", 2000), ["med"] = Descriptor("audio-med.mp3", 1500) } : new JsonObject() } } };
    internal static JsonNode Descriptor(string file, int size) => new JsonObject { ["url"] = Origin + file + Signed, ["size"] = size };
    internal static IEnumerable<(string Name, Func<Task> Test)> All => [
        ("Coub public metadata binds requested permalink and supplied complete video audio resources", Facts),
        ("Coub view embed HTTP and host aliases normalize to the exact public API", Aliases),
        ("Coub URL boundaries exclude collections unrelated hosts and malformed identities", Boundaries),
        ("Coub identity unavailable private unfinished and banned records fail before media", Restrictions),
        ("Coub age access flags malformed JSON and repeated metadata remain explicit", Invalid),
        ("Coub public resource descriptors enforce origins containers exact sizes and dimensions", Descriptors),
        ("Coub metadata and playable resource literals preserve supplied signed query values", Resources),
        ("Coub normal sound selections pair tracks and enable a bounded video loop", Pairing),
        ("Coub video-only explicit single-stream and audio-only selections retain their source roles", Roles),
        ("Coub silent clips complete without inventing an audio track or loop render", Silent),
        ("Coub public sessions isolate caller authorization and cookies", Anonymous),
        ("Coub archived permalink aliases skip metadata and playback discovery", Archives),
        ("Coub cancelled denied or redirected metadata cannot request later media", Cancellation)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var page = await Extract(calls); Assert(page.Id == Id && page.Media.Count == 4 && page.DurationSeconds == 2 && page.Uploader == "Fixture channel" && calls.Count == 1, "Coub metadata/source binding changed.");
        var video = page.Media.Single(item => item.Id == "video-high"); Assert(video.Width == 160 && video.Height == 96 && video.HasAudio == false && video.LoopVideoToAudio && video.VerificationFileSize == 1000, "Supplied dimensions/source size/loop role changed.");
        Assert(page.Media.Single(item => item.Id == "audio-high").IsAudioOnly && page.Media.Single(item => item.Id == "audio-high").VerificationDurationSeconds is null, "The full audio track was incorrectly limited to the video-cycle duration.");
    }
    private static async Task Aliases()
    {
        foreach (var prefix in new[] { "https://coub.com/view/", "https://coub.com/embed/", "http://www.coub.com/view/" })
        { var calls = new List<Uri>(); var page = await Extract(calls, request: Request(Path.GetTempPath()) with { Url = new(prefix + Id + "/?autoplay=true") }); Assert(page.Id == Id && calls.Single().AbsoluteUri == "https://coub.com/api/v2/coubs/" + Id, "Coub alias selected another API identity."); }
    }
    private static Task Boundaries()
    {
        var provider = new CoubProvider(); foreach (var url in new[] { "https://coub.com/channel/fixture", "https://evil.coub.com/view/" + Id, "https://coub.com:444/view/" + Id, "https://secret@coub.com/view/" + Id,
            "https://coub.com/view/invalid!", "https://coub.com/view/" + Id + "/another" }) Assert(!provider.CanHandle(new(url)), "An unrelated Coub URL was claimed.");
        Assert(YoutubeDl.Engine.Providers.MediaProviderRegistry.CreateDefault().Resolve(Request(Path.GetTempPath()).Url).Info.Id == "coub", "Default routing missed Coub."); return Task.CompletedTask;
    }
    private static async Task Restrictions()
    {
        var other = Record(); other["permalink"] = "other"; await Expect<InvalidDataException>(() => Extract([], other));
        foreach (var change in new Action<JsonNode>[] { r => r["visibility_type"] = "private", r => r["published"] = false, r => r["is_done"] = false, r => r["banned"] = true, r => r["moderation_state"] = "rejected" })
        { var record = Record(); change(record); await Expect<NotSupportedException>(() => Extract([], record)); }
    }
    private static async Task Invalid()
    {
        foreach (var change in new Action<JsonNode>[] { r => r["age_restricted"] = true, r => ((JsonObject)r).Remove("has_sound") })
        { var record = Record(); change(record); await Expect<NotSupportedException>(() => Extract([], record)); }
        using var malformed = Http([], raw: "bad JSON"); await Expect<InvalidDataException>(() => new CoubProvider().ExtractAsync(Request(Path.GetTempPath()), malformed, default));
        using var duplicate = Http([], raw: Record().ToJsonString().Replace("\"banned\":false", "\"banned\":true,\"banned\":false")); await Expect<InvalidDataException>(() => new CoubProvider().ExtractAsync(Request(Path.GetTempPath()), duplicate, default));
        var nullable = Record(); nullable["age_restricted_by_admin"] = null; Assert((await Extract([], nullable)).Id == Id, "An explicit absent admin restriction was treated as a malformed access state.");
    }
    private static async Task Descriptors()
    {
        foreach (var change in new Action<JsonNode>[] { r => r["file_versions"]!["html5"]!["video"]!["high"]!["size"] = 0, r => r["dimensions"]!["big"] = new JsonArray(160), r => r["dimensions"]!["big"] = new JsonArray(160, 0) })
        { var record = Record(); change(record); await Expect<InvalidDataException>(() => Extract([], record)); }
        var missing = Record(); missing["file_versions"]!["html5"]!["audio"] = new JsonObject(); await Expect<NotSupportedException>(() => Extract([], missing));
    }
    private static async Task Resources()
    {
        var page = await Extract([]); Assert(page.Media.All(item => item.Url.Query == Signed), "Coub signed URL literals changed.");
        foreach (var raw in new[] { "https://unrelated.example/video.mp4", Origin + "video.webm", "https://secret@attachments-cdn-s.coub.com/video.mp4" })
        { var record = Record(); record["file_versions"]!["html5"]!["video"]!["high"]!["url"] = raw; await Expect<NotSupportedException>(() => Extract([], record)); }
    }
    private static async Task Pairing()
    {
        var page = await Extract([]); var plan = MediaDownloadPlan.Create(page, Request(Path.GetTempPath())); Assert(plan.Primary.Id == "video-high" && plan.Audio?.Id == "audio-high" && plan.Primary.LoopVideoToAudio, "Default Coub playback omitted its full supplied audio/loop behavior.");
        var explicitPair = MediaDownloadPlan.Create(page, Request(Path.GetTempPath()) with { FormatSelector = "video-med+audio-med" }); Assert(explicitPair.Primary.Id == "video-med" && explicitPair.Audio?.Id == "audio-med", "Explicit loop track selection changed.");
    }
    private static async Task Roles()
    {
        var page = await Extract([]); Assert(MediaDownloadPlan.Create(page, Request(Path.GetTempPath()) with { VideoOnly = true }).Audio is null && MediaDownloadPlan.Create(page, Request(Path.GetTempPath()) with { FormatSelector = "video-high" }).Audio is null, "A single video selection gained an audio/render track.");
        Assert(MediaDownloadPlan.Create(page, Request(Path.GetTempPath()) with { AudioOnly = true }).Primary.IsAudioOnly, "Audio-only selection chose video.");
    }
    private static async Task Silent()
    {
        var page = await Extract([], Record(false)); Assert(page.Media.Count == 2 && page.Media.All(item => item.HasAudio == false && item.IsCompleteMedia && !item.LoopVideoToAudio), "A silent clip invented audio or an extended render.");
    }
    private static async Task Anonymous()
    {
        var options = new MediaHttpOptions(); options.Headers["Authorization"] = "Bearer fixture"; options.Headers["Cookie"] = "fixture=secret"; using var http = Http([], options: options,
            observe: r => Assert(!r.Headers.Contains("Authorization") && !r.Headers.Contains("Cookie"), "Caller credentials reached Coub public metadata.")); _ = await new CoubProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
    }
    private static async Task Archives()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coub-archive-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
        try { var archive = Path.Combine(dir, "archive.txt"); await File.WriteAllTextAsync(archive, "coub " + Id + "\n"); var calls = new List<Uri>(); var engine = new NativeEngine([new CoubProvider()], _ => Http(calls));
            var result = await engine.DownloadAsync(Request(dir) with { Url = new("https://coub.com/embed/" + Id), DownloadArchiveFile = archive }, null, null, default); Assert(result.Succeeded && result.Skipped && calls.Count == 0, "Archived Coub alias requested playback."); }
        finally { Directory.Delete(dir, true); }
    }
    private static async Task Cancellation()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel(); using var http = Http([]); await Expect<OperationCanceledException>(() => new CoubProvider().ExtractAsync(Request(Path.GetTempPath()), http, stop.Token));
        using var denied = Http([], status: HttpStatusCode.Forbidden); await Expect<HttpRequestException>(() => new CoubProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
        using var redirected = Http([], redirect: true); await Expect<InvalidDataException>(() => new CoubProvider().ExtractAsync(Request(Path.GetTempPath()), redirected, default));
    }
    private static async Task<PageMetadata> Extract(List<Uri> calls, JsonNode? record = null, NativeDownloadRequest? request = null)
    { using var http = Http(calls, record); return await new CoubProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static MediaHttp Http(List<Uri> calls, JsonNode? record = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null, MediaHttpOptions? options = null,
        Action<HttpRequestMessage>? observe = null, string? raw = null, HttpStatusCode status = HttpStatusCode.OK, bool redirect = false) => new(new Handler(r =>
    { calls.Add(r.RequestUri!); observe?.Invoke(r); if (r.RequestUri!.Host == "coub.com") return new(status) { Content = new StringContent(raw ?? (record ?? Record()).ToJsonString()), RequestMessage = redirect ? new(HttpMethod.Get, "https://coub.com/api/v2/coubs/other") : r };
        return media?.Invoke(r) ?? throw new InvalidOperationException("Unexpected Coub media request."); }), options ?? new());
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
}
