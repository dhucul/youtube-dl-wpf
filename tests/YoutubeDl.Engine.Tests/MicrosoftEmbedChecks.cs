using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.MicrosoftEmbed;

internal static class MicrosoftEmbedChecks
{
    internal const string Id = "RWfixture1";
    internal const string Signed = "?Key=A%2Bb&case=C%2Fd";
    internal static Uri Url(JsonNode options) => new("https://www.microsoft.com/en-us/videoplayer/embed?options=" + Uri.EscapeDataString(options.ToJsonString()));
    internal static NativeDownloadRequest Request(string directory, JsonNode? options = null) => new() { Url = Url(options ?? Options()), OutputDirectory = directory, OutputTemplate = "embed.%(ext)s" };
    internal static JsonNode Options(string mode = "mp4", double duration = 2) => new JsonObject { ["title"] = "Native Microsoft Embed", ["duration"] = duration,
        ["poster"] = "https://media.example/poster.jpg", ["sources"] = new JsonArray(new JsonObject { ["type"] = mode.ToUpperInvariant(), ["quality"] = mode == "mp4" ? "HD" : null,
            ["src"] = "https://media.example/" + (mode == "hls" ? "master.m3u8" : mode == "dash" ? "source.mpd" : "high.mp4") + Signed }),
        ["ccFiles"] = new JsonArray(new JsonObject { ["locale"] = "en-us", ["ccType"] = "VTT", ["url"] = "https://media.example/captions.vtt" + Signed }) };
    internal static JsonNode Mapping() => new JsonObject { ["adobeCms"] = new JsonObject { ["operatingGroup"] = "fixture", ["videoId"] = "video-123" } };
    internal static JsonNode Playback() => new JsonObject { ["videoId"] = "video-123", ["operatingGroup"] = "fixture", ["videoTitle"] = "Mapped native video", ["posterUrl"] = "https://media.example/poster.jpg",
        ["duration"] = 2, ["minimumAge"] = 0, ["sources"] = new JsonArray(new JsonObject { ["type"] = "MP4", ["url"] = "https://media.example/high.mp4" + Signed }),
        ["closedCaptions"] = new JsonArray(new JsonObject { ["ccType"] = "VTT", ["locale"] = "en-us", ["url"] = "https://media.example/captions.vtt" + Signed }), ["downloadableFiles"] = new JsonArray() };
    internal static IEnumerable<(string Name, Func<Task> Test)> All => [
        ("Microsoft Embed literal JSON retains complete supplied source caption and duration facts", Facts),
        ("Microsoft Embed flat repeated media and locale query fields preserve literal resources", Flat),
        ("Microsoft Embed named qualities rank without inventing pixel or bandwidth facts", Quality),
        ("Microsoft Embed PowerCMS mapping binds the exact Cascade playback endpoint", MappingFlow),
        ("Microsoft Embed direct Cascade and JSON PowerCMS plugin aliases retain identity", Aliases),
        ("Microsoft Embed address and resource boundaries exclude unrelated unsafe or repeated identities", Boundaries),
        ("Microsoft Embed malformed repeated oversized and conflicting source settings fail", Invalid),
        ("Microsoft Embed conflicting CMS identities private age settings and redirected metadata fail", Restrictions),
        ("Microsoft Embed captions prefer VTT while retaining TTML-only language tracks", Captions),
        ("Microsoft Embed supplied source archives ignore display titles and query field order", ArchiveIdentity),
        ("Microsoft Embed known archived PowerCMS aliases skip all metadata and media", ArchiveSkip),
        ("Microsoft Embed public CMS sessions isolate caller authorization and cookies", Anonymous),
        ("Microsoft Embed missing mappings denied playback and cancellation have no alternate fallback", Cancellation),
        ("Microsoft Embed native generic iframe routing retains embedded identity", Embeds)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var page = await Extract(Request(Path.GetTempPath()), calls);
        Assert(page.Title == "Native Microsoft Embed" && page.DurationSeconds == 2 && page.Media.Single().Url.Query == Signed && page.Subtitles.Single().Language == "en-us" && calls.Count == 0, "Literal options unexpectedly fetched metadata or changed facts.");
        Assert(page.Media[0].Height is null && page.Media[0].Bandwidth is null && page.Media[0].VerificationDemuxer == "mov", "A named quality became invented metrics or skipped decoding.");
    }
    private static async Task Flat()
    {
        var url = "https://www.microsoft.com/videoplayer/embed?mp4=" + Uri.EscapeDataString("HD,https://media.example/high.mp4" + Signed) + "&mp4=" + Uri.EscapeDataString("LO,https://media.example/low.mp4" + Signed) +
            "&vtt=" + Uri.EscapeDataString("en-us,https://media.example/captions.vtt" + Signed) + "&title=Native+flat+title";
        var page = await Extract(Request(Path.GetTempPath()) with { Url = new(url) }, []);
        Assert(page.Title == "Native flat title" && page.Media.Count == 2 && page.Media.All(item => item.Url.Query == Signed) && page.Subtitles[0].Url.Query == Signed, "Form decoding changed embedded query escapes or repeated resources.");
    }
    private static async Task Quality()
    {
        var options = Options(); ((JsonArray)options["sources"]!).Add(new JsonObject { ["type"] = "MP4", ["quality"] = "LO", ["src"] = "https://media.example/low.mp4" });
        var page = await Extract(Request(Path.GetTempPath(), options), []);
        Assert(QualitySelector.Select(page.Media, QualityPreference.Best, null, false).Id == "HD" && QualitySelector.Select(page.Media, QualityPreference.Worst, null, false).Id == "LO", "Provider quality order did not select best/worst labels.");
        await Expect<InvalidOperationException>(() => Task.FromResult(QualitySelector.Select(page.Media, QualityPreference.MaximumHeight, 720, false)));
    }
    private static async Task MappingFlow()
    {
        var calls = new List<Uri>(); var page = await Extract(Request(Path.GetTempPath()) with { Url = new("https://www.microsoft.com/en-us/videoplayer/embed/" + Id) }, calls);
        Assert(page.Id == Id && page.Title == "Mapped native video" && calls.Count == 2 && calls[0].AbsolutePath == "/videoplayer/umpOptionsMappings/" + Id + ".json" &&
            calls[1].AbsoluteUri == "https://video.cascade.microsoft.com/api/og/fixture/videos/video-123/playback-info", "PowerCMS was not bound to its requested mapping/Cascade endpoint.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "https://www.microsoft.com/videoplayer/embed?powerCmsVideoId=" + Id, "https://microsoft.com/en-gb/videoplayer/embed/" + Id + "?partnerName=oneplayer" })
            Assert((await Extract(Request(Path.GetTempPath()) with { Url = new(url) }, [])).Id == Id, "PowerCMS alias changed its identity.");
        var direct = Request(Path.GetTempPath()) with { Url = new("https://www.microsoft.com/videoplayer/embed?adobeCmsOperatingGroup=fixture&adobeCmsVideoId=video-123") };
        var calls = new List<Uri>(); var page = await Extract(direct, calls); Assert(page.Id == "fixture/video-123" && calls.Count == 1, "Direct Cascade alias fetched an unrelated mapping.");
        var overridden = new JsonObject { ["adobeCms"] = Mapping()["adobeCms"]!.DeepClone(), ["title"] = "Configured title", ["ccFiles"] = new JsonArray(new JsonObject { ["locale"] = "en-us", ["ccType"] = "VTT", ["url"] = "https://media.example/override.vtt" }) };
        var overlay = await Extract(Request(Path.GetTempPath(), overridden), []); Assert(overlay.Title == "Configured title" && overlay.Subtitles.Single().Url.AbsolutePath == "/override.vtt", "Cascade discovery discarded explicitly supplied title/captions.");
        var plugin = new JsonObject { ["plugins"] = new JsonObject { ["powerCms"] = new JsonObject { ["videoId"] = Id } } };
        Assert((await Extract(Request(Path.GetTempPath(), plugin), [])).Id == Id, "Literal PowerCMS plugin options failed.");
    }
    private static Task Boundaries()
    {
        var provider = new MicrosoftEmbedProvider();
        foreach (var url in new[] { "http://www.microsoft.com/videoplayer/embed/" + Id, "https://www.microsoft.com:444/videoplayer/embed/" + Id, "https://secret@www.microsoft.com/videoplayer/embed/" + Id,
            "https://www.microsoft.com/office/" + Id, "https://evil.microsoft.com/videoplayer/embed/" + Id, "https://www.microsoft.com/videoplayer/embed/" + Id + "/other" }) Assert(!provider.CanHandle(new(url)), "An unrelated or unsafe player was claimed.");
        return Task.CompletedTask;
    }
    private static async Task Invalid()
    {
        foreach (var options in new[] { "null", "{}", "{\"sources\":{}}", "{\"sources\":[],\"sources\":[]}", "{\"_nativePowerId\":\"other\"}" })
        {
            var request = Request(Path.GetTempPath()) with { Url = new("https://www.microsoft.com/videoplayer/embed?options=" + Uri.EscapeDataString(options)) };
            if (options == "{}") await Expect<NotSupportedException>(() => Extract(request, [])); else await Expect<InvalidDataException>(() => Extract(request, []));
        }
        foreach (var resource in new[] { "file:///C:/secret.mp4", "https://secret@media.example/file.mp4", "https://media.example/file.mp4#fragment" })
        { var options = Options(); options["sources"]![0]!["src"] = resource; await Expect<InvalidDataException>(() => Extract(Request(Path.GetTempPath(), options), [])); }
        var conflict = Options(); ((JsonArray)conflict["sources"]!).Add(new JsonObject { ["type"] = "MP4", ["quality"] = "HD", ["src"] = "https://media.example/other.mp4" });
        await Expect<InvalidDataException>(() => Extract(Request(Path.GetTempPath(), conflict), []));
        await Expect<ArgumentException>(() => Extract(Request(Path.GetTempPath()) with { Url = new("https://www.microsoft.com/videoplayer/embed?powerCmsVideoId=" + Id + "&powerCmsVideoId=other") }, []));
    }
    private static async Task Restrictions()
    {
        var power = Request(Path.GetTempPath()) with { Url = new("https://www.microsoft.com/videoplayer/embed/" + Id) };
        var wrong = Playback(); wrong["videoId"] = "other"; await Expect<InvalidDataException>(() => Extract(power, [], playback: wrong));
        var gated = Playback(); gated["minimumAge"] = 18; await Expect<NotSupportedException>(() => Extract(power, [], playback: gated));
        await Expect<InvalidDataException>(() => Extract(power, [], redirect: true));
        var protectedOptions = Options(); protectedOptions["drm"] = new JsonObject(); await Expect<NotSupportedException>(() => Extract(Request(Path.GetTempPath(), protectedOptions), []));
        var configuredAge = Options(); configuredAge["ageGate"] = new JsonObject { ["minimumAge"] = "18" }; await Expect<NotSupportedException>(() => Extract(Request(Path.GetTempPath(), configuredAge), []));
        var mixed = Options(); mixed["adobeCms"] = Mapping()["adobeCms"]!.DeepClone(); await Expect<NotSupportedException>(() => Extract(Request(Path.GetTempPath(), mixed), []));
    }
    private static async Task Captions()
    {
        var options = Options(); ((JsonArray)options["ccFiles"]!).Add(new JsonObject { ["ccType"] = "TTML", ["locale"] = "en-us", ["url"] = "https://media.example/english.ttml" });
        ((JsonArray)options["ccFiles"]!).Add(new JsonObject { ["ccType"] = "TTML", ["locale"] = "fr-fr", ["url"] = "https://media.example/french.ttml" });
        var page = await Extract(Request(Path.GetTempPath(), options), []); Assert(page.Subtitles.Count == 2 && page.Subtitles[0].Extension == "vtt" && page.Subtitles[1].Extension == "ttml", "Caption type preference lost native TTML-only tracks.");
        options["ccFiles"]![0]!["locale"] = "../en"; await Expect<InvalidDataException>(() => Extract(Request(Path.GetTempPath(), options), []));
    }
    private static async Task ArchiveIdentity()
    {
        var one = await Extract(Request(Path.GetTempPath()), []); var options = Options(); options["title"] = "Renamed display";
        var two = await Extract(Request(Path.GetTempPath(), options), []); Assert(one.ArchiveIdentity == two.ArchiveIdentity, "Display metadata changed the supplied-source archive.");
        options["sources"]![0]!["src"] = "https://media.example/different.mp4"; Assert((await Extract(Request(Path.GetTempPath(), options), [])).ArchiveIdentity != one.ArchiveIdentity, "Different supplied media shared an archive identity.");
    }
    private static async Task ArchiveSkip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ms-embed-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
        try { var path = Path.Combine(dir, "archive.txt"); await File.WriteAllTextAsync(path, "microsoftembed " + Id + "\n"); var calls = new List<Uri>(); var engine = new NativeEngine([new MicrosoftEmbedProvider()], _ => Http(calls));
            var result = await engine.DownloadAsync(Request(dir) with { Url = new("https://www.microsoft.com/videoplayer/embed/" + Id), DownloadArchiveFile = path }, null, null, default); Assert(result.Succeeded && result.Skipped && calls.Count == 0, "Archived PowerCMS alias requested metadata."); }
        finally { Directory.Delete(dir, true); }
    }
    private static async Task Anonymous()
    {
        var options = new MediaHttpOptions(); options.Headers["Authorization"] = "Bearer fixture"; options.Headers["Cookie"] = "fixture=secret"; var calls = new List<Uri>();
        using var http = Http(calls, options: options, observe: r => Assert(!r.Headers.Contains("Authorization") && !r.Headers.Contains("Cookie"), "Caller credentials reached public CMS origins."));
        _ = await new MicrosoftEmbedProvider().ExtractAsync(Request(Path.GetTempPath()) with { Url = new("https://www.microsoft.com/videoplayer/embed/" + Id) }, http, default); Assert(calls.Count == 2, "The anonymous discovery flow did not finish.");
    }
    private static async Task Cancellation()
    {
        var request = Request(Path.GetTempPath()) with { Url = new("https://www.microsoft.com/videoplayer/embed/" + Id) };
        var calls = new List<Uri>(); using var missing = Http(calls, status: HttpStatusCode.NotFound); await Expect<HttpRequestException>(() => new MicrosoftEmbedProvider().ExtractAsync(request, missing, default)); Assert(calls.Count == 1, "A missing mapping tried alternate playback.");
        using var stop = new CancellationTokenSource(); stop.Cancel(); using var http = Http([]); await Expect<OperationCanceledException>(() => new MicrosoftEmbedProvider().ExtractAsync(request, http, stop.Token));
    }
    private static async Task Embeds()
    {
        var url = Url(Options()); var embedded = EmbeddedMediaReader.Read("<iframe src='" + WebUtility.HtmlEncode(url.AbsoluteUri) + "'></iframe>", new("https://page.example/post"));
        Assert(embedded.Single() == url && YoutubeDl.Engine.Providers.MediaProviderRegistry.CreateDefault().Resolve(embedded[0]).Info.Id == "microsoftembed", "Generic iframe discovery missed native Microsoft dispatch.");
        Assert((await Extract(Request(Path.GetTempPath()) with { Url = embedded[0] }, [])).Media.Count == 1, "The discovered iframe did not expose its supplied source.");
    }
    private static async Task<PageMetadata> Extract(NativeDownloadRequest request, List<Uri> calls, JsonNode? playback = null, bool redirect = false)
    { using var http = Http(calls, playback: playback, redirect: redirect); return await new MicrosoftEmbedProvider().ExtractAsync(request, http, default); }
    internal static MediaHttp Http(List<Uri> calls, JsonNode? playback = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null, MediaHttpOptions? options = null,
        Action<HttpRequestMessage>? observe = null, HttpStatusCode status = HttpStatusCode.OK, bool redirect = false) => new(new Handler(r =>
    { calls.Add(r.RequestUri!); observe?.Invoke(r); var uri = r.RequestUri!;
        if (uri.Host is "www.microsoft.com" or "video.cascade.microsoft.com") return new(status) { Content = new StringContent((uri.Host == "www.microsoft.com" ? Mapping() : playback ?? Playback()).ToJsonString()), RequestMessage = redirect ? new(HttpMethod.Get, "https://other.example/metadata") : r };
        return media?.Invoke(r) ?? throw new InvalidOperationException("Unexpected Microsoft Embed media request."); }), options ?? new());
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
}
