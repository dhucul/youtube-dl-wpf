using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;
using YoutubeDl.Engine.Providers.Panopto;

internal static class PanoptoChecks
{
    internal const string DeliveryId = "11111111-1111-4111-8111-111111111111";
    internal const string SessionId = "22222222-2222-4222-8222-222222222222";
    internal const string PodcastId = "33333333-3333-4333-8333-333333333333";
    internal const string SourceId = "44444444-4444-4444-8444-444444444444";
    internal const string Query = "?Policy=A%2Bb&Signature=C%2Fd&Key-Pair-Id=fixture";
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new("https://campus.example/Panopto/Pages/Viewer.aspx?id=" + DeliveryId), OutputDirectory = directory, FileName = "panopto", Retries = 0 };
    internal static JsonNode Data(double duration = 2) => JsonNode.Parse($$$"""
    {"SessionId":"{{{SessionId}}}","PodcastCompleted":true,"Delivery":{"PublicID":"{{{DeliveryId}}}","SessionPublicID":"{{{SessionId}}}","SessionFileId":"{{{SessionId}}}",
     "SessionName":"Recording & title","SessionAbstract":"Fixture description","OwnerDisplayName":"Fixture speaker","Duration":{{{duration.ToString(System.Globalization.CultureInfo.InvariantCulture)}}},
     "IsOpen":false,"IsBroadcast":false,"IsActiveBroadcast":false,"HasSplices":false,"IsPodcastEncodeComplete":true,"IsAudioPodcastEncodeComplete":false,"IsPrimaryAudioOnly":false,
     "PodcastStreams":[{"PublicID":"{{{PodcastId}}}","ViewerMediaFileTypeName":"mp4","RelativeStart":0,"RelativeEnd":{{{duration.ToString(System.Globalization.CultureInfo.InvariantCulture)}}},"StreamUrl":"https://cdn.example/sessions/{{{SessionId}}}/{{{DeliveryId}}}-{{{PodcastId}}}.mp4{{{Query}}}"}],
     "Streams":[{"PublicID":"{{{SourceId}}}","ViewerMediaFileTypeName":"hls","RelativeStart":0,"RelativeEnd":{{{duration.ToString(System.Globalization.CultureInfo.InvariantCulture)}}},"StreamUrl":"https://cdn.example/sessions/{{{SessionId}}}/{{{DeliveryId}}}-{{{SourceId}}}.hls/master.m3u8{{{Query}}}"}],
     "AvailableCaptions":[{"Language":0,"ShowDisclaimer":true},{"Language":5,"ShowDisclaimer":false}],
     "Timestamps":[{"Time":0,"Caption":"First","ShowInTableOfContents":true,"EventTargetType":"PowerPoint"},{"Time":1,"Caption":"Second","ShowInTableOfContents":true,"EventTargetType":"Primary"}]}}
    """)!;
    internal const string Captions = """
    [{"Time":0.1,"CaptionDuration":0.8,"Caption":"Literal <b> & caption --> text\n\nNext line","SessionID":"00000000-0000-0000-0000-000000000000"},
     {"Time":1,"CaptionDuration":0.9,"Caption":"Last cue","SessionID":"22222222-2222-4222-8222-222222222222"}]
    """;
    internal static string Html => "<meta property='og:image' content='https://cdn.example/sessions/" + SessionId + "/thumbs/slide.jpg'>";
    internal static MediaHttp Http(JsonNode data, List<(HttpRequestMessage Request, string Body)>? observed = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null, string captions = Captions, MediaHttpOptions? options = null) =>
        new(new Handler(async request =>
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(); observed?.Add((request, body));
            if (request.RequestUri!.Host == "campus.example") return new(HttpStatusCode.OK)
            { Content = new StringContent(request.Method == HttpMethod.Get ? Html : body.Contains("getCaptions=true") ? captions : data.ToJsonString()) };
            return media?.Invoke(request) ?? throw new InvalidOperationException("Unexpected media request.");
        }), options);
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Panopto recording metadata preserves signed media chapters and scoped archive identity", Facts),
        ("Panopto captions fetch only selected language using bounded playback form requests", CaptionsSelected),
        ("Panopto viewer embed and custom server routes retain one recording identity", Addresses),
        ("Panopto mismatched delivery session and media identities fail before transfer", Identities),
        ("Panopto live purged DRM and incomplete timelines are refused", Boundaries),
        ("Panopto multi-view and edited sources require a complete podcast encode", Composition),
        ("Panopto failed playback and login redirects never become media", Denials),
        ("Panopto caption timing text and session binding remain valid", CaptionBounds),
        ("Panopto archive lookup skips media after recording identity resolution", Archive),
        ("Panopto standalone captions publish without ffmpeg or media requests", Standalone),
        ("Panopto recordings embedded in generic pages route through their native provider", Embedded)
    ];
    private static async Task<PageMetadata> Read(JsonNode? data = null, NativeDownloadRequest? request = null)
    { using var http = Http(data ?? Data()); return await new PanoptoProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    private static async Task Facts()
    {
        var page = await Read();
        Assert(page.Id == DeliveryId && page.Title == "Recording & title" && page.Media.Count == 2 && page.Media[0].Url.Query == Query && page.Media[0].IsCompleteMedia,
            "Metadata lost identity, media roles or literal access parameters.");
        Assert(page.Media[0].VerificationDurationSeconds == 2 && page.Media[1].IsFallback && page.Media[1].Kind == MediaKind.Hls && page.Chapters.Count == 2 && page.Chapters[0].EndSeconds == 1,
            "Playback selection or chapter ranges changed.");
        Assert(page.ArchiveIdentity?.Extractor == "panopto:delivery" && page.ArchiveIdentity.Id == "campus.example/" + DeliveryId && page.AnonymousPlayback && page.Subtitles.All(track => track.Content is null), "Session scoping or lazy captions changed.");
    }
    private static async Task CaptionsSelected()
    {
        var calls = new List<(HttpRequestMessage Request, string Body)>(); using var http = Http(Data(), calls);
        var page = await new PanoptoProvider().ExtractAsync(Request(Path.GetTempPath()) with { WriteSubs = true, SubLanguages = "fr" }, http, default);
        Assert(calls.Count == 3 && calls[1].Request.Method == HttpMethod.Post && calls[1].Body.Contains("deliveryId=" + DeliveryId) && calls[2].Body.Contains("language=5"), "Caption retrieval used wrong request or language.");
        Assert(page.Subtitles.Single(track => track.Language == "fr").Content?.Contains("&lt;b&gt;") == true && page.Subtitles.Single(track => track.Language == "en-US").Content is null,
            "Unselected captions were fetched or literal text became markup.");
    }
    private static async Task Addresses()
    {
        var request = Request(Path.GetTempPath()) with { Url = new("https://campus.example/Panopto/Pages/Embed.aspx?id=" + DeliveryId.ToUpperInvariant() + "&autoplay=false") };
        Assert((await Read(request: request)).Id == DeliveryId, "Embedded recording changed identity.");
        var provider = new PanoptoProvider();
        Assert(!provider.CanHandle(new("https://campus.example/unrelated/Viewer.aspx?id=" + DeliveryId)), "Unrelated pages were claimed.");
        foreach (var url in new[] { "https://campus.example/Panopto/Pages/Viewer.aspx?pid=" + DeliveryId,
            "https://campus.example/Panopto/Pages/Viewer.aspx?id=" + DeliveryId + "&pid=" + SourceId,
            "https://campus.example/Panopto/Pages/Viewer.aspx?id=" + DeliveryId + "&id=" + SourceId, "https://user:secret@campus.example/Panopto/Pages/Viewer.aspx?id=" + DeliveryId })
            await Expect<NotSupportedException>(() => Read(request: request with { Url = new(url) }));
    }
    private static async Task Identities()
    {
        foreach (var name in new[] { "PublicID", "SessionPublicID", "SessionFileId" })
        { var data = Data(); data["Delivery"]![name] = SourceId; await Expect<InvalidDataException>(() => Read(data)); }
        var other = Data(); other["Delivery"]!["PodcastStreams"]![0]!["StreamUrl"] = "https://cdn.example/sessions/" + SourceId + "/" + DeliveryId + "-" + PodcastId + ".mp4";
        await Expect<InvalidDataException>(() => Read(other));
        other = Data(); other["Delivery"]!["PodcastStreams"]![0]!["StreamUrl"] = "https://user:secret@cdn.example/sessions/" + SessionId + "/" + DeliveryId + "-" + PodcastId + ".mp4";
        await Expect<InvalidDataException>(() => Read(other));
    }
    private static async Task Boundaries()
    {
        foreach (var name in new[] { "IsActiveBroadcast", "IsPurgedEncode", "IsPurgedLegacyEncode", "IsReferenceCopy", "DrmProtected" })
        { var data = Data(); data["Delivery"]![name] = true; await Expect<NotSupportedException>(() => Read(data)); }
        foreach (var name in new[] { "RelativeStart", "RelativeEnd" })
        { var data = Data(); data["Delivery"]!["PodcastStreams"]![0]![name] = 1; await Expect<NotSupportedException>(() => Read(data)); }
    }
    private static async Task Composition()
    {
        var data = Data(); data["Delivery"]!["HasSplices"] = true;
        var page = await Read(data); Assert(page.Media.Count == 1 && page.ReportedFormats.Count == 1, "Edited source was exposed as complete playback.");
        data["Delivery"]!["IsPodcastEncodeComplete"] = false; await Expect<NotSupportedException>(() => Read(data));
        data = Data(); var streams = data["Delivery"]!["Streams"]!.AsArray(); streams.Add(streams[0]!.DeepClone());
        page = await Read(data); Assert(page.Media.Count == 1 && page.ReportedFormats.Count == 2, "Multiple views were silently discarded into a single source download.");
    }
    private static async Task Denials()
    {
        using var http = new MediaHttp(new Handler(request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(request.Method == HttpMethod.Get ? Html : "{\"ErrorCode\":403,\"ErrorMessage\":\"fixture-secret\"}") })));
        try { await new PanoptoProvider().ExtractAsync(Request(Path.GetTempPath()), http, default); throw new InvalidOperationException("Denial was accepted."); }
        catch (NotSupportedException ex) { Assert(!ex.Message.Contains("fixture-secret"), "Server detail leaked through a playback refusal."); }
        using var redirected = new MediaHttp(new Handler(request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            RequestMessage = new(HttpMethod.Get, new Uri("https://login.example/Panopto/Pages/Viewer.aspx?id=" + DeliveryId)), Content = new StringContent(Html) })));
        await Expect<InvalidDataException>(() => new PanoptoProvider().ExtractAsync(Request(Path.GetTempPath()), redirected, default));
    }
    private static Task CaptionBounds()
    {
        using var json = JsonDocument.Parse(Captions); var text = PanoptoProvider.RenderCaptions(json.RootElement, 2, SessionId);
        Assert(text.Contains("00:00:00.100 --> 00:00:00.900") && text.Contains("&amp;") && !text.Contains("\n\nNext line"), "Caption times or safe text changed.");
        foreach (var altered in new[] { Captions.Replace("0.8", "20"), Captions.Replace("0.1", "-1"), Captions.Replace(SessionId, SourceId), "{}" })
        { using var document = JsonDocument.Parse(altered); try { PanoptoProvider.RenderCaptions(document.RootElement, 2, SessionId); throw new InvalidOperationException("Invalid captions were accepted."); } catch (InvalidDataException) { } }
        return Task.CompletedTask;
    }
    private static async Task Archive()
    {
        using var files = new Files(); var archive = Path.Combine(files.Directory, "archive.txt"); await File.WriteAllTextAsync(archive, "panopto:delivery campus.example/" + DeliveryId + "\n");
        var transfers = 0; var engine = new NativeEngine([new PanoptoProvider()], _ => Http(Data(), media: _ => { transfers++; throw new InvalidOperationException("An archived item requested media."); }));
        var result = await engine.DownloadAsync(Request(files.Directory) with { DownloadArchiveFile = archive }, null, null, default);
        Assert(result.Succeeded && result.Skipped && transfers == 0, "Archive identity did not suppress transfer.");
    }
    private static async Task Standalone()
    {
        using var files = new Files(); var calls = new List<(HttpRequestMessage Request, string Body)>();
        var engine = new NativeEngine([new PanoptoProvider()], _ => Http(Data(), calls));
        var result = await engine.DownloadAsync(Request(files.Directory) with { SkipDownload = true, WriteSubs = true, SubLanguages = "en.*" }, null, null, default);
        Assert(result.Succeeded && result.OutputPaths.Count == 0 && result.SidecarPaths.Count == 1 && calls.Count == 3 && await File.ReadAllTextAsync(result.SidecarPaths[0]) is { } text && text.Contains("00:00:01.000 --> 00:00:01.900"), "Standalone captions needed media or did not preserve timing.");
    }
    private static async Task Embedded()
    {
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(async request =>
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync();
            return new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.Host == "article.example" ?
                "<iframe src='https://campus.example/Panopto/Pages/Embed.aspx?id=" + DeliveryId + "'></iframe>" : request.Method == HttpMethod.Post ? Data().ToJsonString() : Html) };
        })));
        var info = await engine.InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://article.example/lesson") }, default);
        Assert(info.Extractor == "panopto" && info.Id == DeliveryId, "Generic pages did not delegate Panopto embeds.");
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request); }
    private sealed class Files : IDisposable
    { internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "native-panopto-" + Guid.NewGuid()); public Files() => System.IO.Directory.CreateDirectory(Directory); public void Dispose() => System.IO.Directory.Delete(Directory, true); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
