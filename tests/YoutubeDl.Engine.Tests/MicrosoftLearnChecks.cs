using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.MicrosoftLearn;

internal static class MicrosoftLearnChecks
{
    internal const string Id = "99fd9a1d-1f74-4a1e-a2c1-fed80118ea77";
    internal const string SecondId = "e7b5263c-3c86-4482-abc0-7cb8e7adb928";
    internal const string Query = "?Key=A%2Bb&case=C%2Fd";
    internal const string Origin = "https://videoencodingpublic-fixture.b01.azurefd.net/public-" + Id + "/";
    internal static NativeDownloadRequest Request(string output) => new() { Url = new("https://learn.microsoft.com/en-us/shows/fixture/first"), OutputDirectory = output, OutputTemplate = "learn.%(ext)s", DownloadPlaylist = true };
    internal static string Html(string slug = "first", string id = Id, bool series = false, bool session = false)
    {
        var kind = session ? "events" : "shows"; var page = "https://learn.microsoft.com/en-us/" + kind + "/fixture/" + (slug.Length > 0 ? slug : "");
        return "<html><head><title>Fixture Microsoft Learn</title><meta property='og:url' content='" + page + "'><meta name='locale' content='en-us'><meta name='schema' content='" + (slug.Length == 0 ? "Show" : session ? "Session" : "Episode") + "'>" +
            "<meta name='uid' content='" + (slug.Length == 0 ? "fixture" : "fixture." + slug) + "'><meta name='showId' content='fixture'><meta name='type' content='" + (series ? "series" : "show") + "'>" +
            (slug.Length == 0 ? "" : "<meta name='entryId' content='" + id + "'>") + "<meta name='description' content='Native complete episode'></head><body>No player code.</body></html>";
    }
    internal static JsonNode Entry(string id = Id, double duration = 2) => new JsonObject { ["id"] = id, ["title"] = "Native Microsoft Learn", ["publicVideo"] = new JsonObject {
        ["durationInMillisecond"] = duration * 1000, ["highQualityVideoUrl"] = Origin + "high.mp4" + Query, ["mediumQualityVideoUrl"] = Origin + "medium.mp4" + Query,
        ["lowQualityVideoUrl"] = Origin + "low.mp4" + Query, ["adaptiveVideoHLSUrl"] = Origin + "master.m3u8" + Query, ["adaptiveVideoDashUrl"] = Origin + "source.mpd" + Query,
        ["adaptiveVideoUrl"] = "", ["audioUrl"] = Origin + "audio.mp3" + Query, ["thumbnailOtherSizes"] = new JsonObject { ["w1120Url"] = Origin + "poster.jpg", ["w800Url"] = Origin + "poster-small.jpg" },
        ["captions"] = new JsonArray(new JsonObject { ["language"] = "en-us", ["url"] = Origin + "captions.vtt" + Query }) } };
    internal static JsonNode Row(string slug = "first", string? id = Id, bool hidden = false) => new JsonObject { ["uid"] = slug, ["show_uid"] = "fixture", ["entry_id"] = id,
        ["title"] = "Episode " + slug, ["hidden"] = hidden, ["duration_in_milliseconds"] = 2000, ["locale"] = "en-us", ["url"] = "/shows/fixture/" + slug + "/" };
    internal static JsonNode Listing(int total, string? next, params JsonNode[] rows) => new JsonObject { ["count"] = total, ["@nextLink"] = next, ["results"] = new JsonArray(rows) };
    internal static IEnumerable<(string Name, Func<Task> Test)> All => [
        ("Microsoft Learn public episode preserves supplied formats captions and original video facts", Facts),
        ("Microsoft Learn episode session direct-entry and embedded aliases preserve identity", Aliases),
        ("Microsoft Learn address boundaries exclude profiles malformed IDs and conflicting players", Boundaries),
        ("Microsoft Learn canonical page authoring metadata and API identities must agree", Identities),
        ("Microsoft Learn public resources enforce exact asset origins and media roles", Resources),
        ("Microsoft Learn current encoding inventory pairs actual video-only and audio tracks", SplitEncoding),
        ("Microsoft Learn complete show pagination retains positions availability and expected archives", Collections),
        ("Microsoft Learn incomplete changed repeated or unrelated show pages cannot become complete", Incomplete),
        ("Microsoft Learn series order and configured inventory limits are preserved", Series),
        ("Microsoft Learn captions enforce complete bounded language and asset inventories", Captions),
        ("Microsoft Learn literal external player metadata delegates its original native provider", External),
        ("Microsoft Learn anonymous metadata isolates caller credentials and malformed responses", Anonymous),
        ("Microsoft Learn archived direct-entry aliases skip metadata and playback", Archives),
        ("Microsoft Learn canceled denied and redirected API discovery stops explicitly", Cancellation)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var page = await Extract(calls);
        Assert(page.Id == Id && page.Title == "Native Microsoft Learn" && page.DurationSeconds == 2 && page.Media.Count == 6 && page.Subtitles.Single().Language == "en-us", "Episode metadata changed.");
        Assert(page.Media.Where(m => m.Kind == MediaKind.Progressive).All(m => m.Width is null && m.Height is null) && page.Thumbnails.Count == 2, "Named quality/poster widths became invented video dimensions.");
        Assert(page.Media.All(m => m.Url.Query == Query) && page.Media.Single(m => m.Id == "audio").IsAudioOnly && calls.Count == 2 && calls[1].Query == "?isAMS=false", "Original resources, roles or API mode changed.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "https://docs.microsoft.com/en-us/shows/fixture/first", "https://learn-video.azurefd.net/vod/player?show=fixture&ep=first",
            "https://learn.microsoft.com/_themes/docs.theme/master/en-us/_themes/global/video-embed-one-stream.html?show=fixture&ep=first",
            "https://learn-video.azurefd.net/vod/player?id=" + Id, "https://learn.microsoft.com/_themes/docs.theme/master/en-us/_themes/global/video-embed.html?id=" + Id })
        { var page = await Extract([], request: Request(Path.GetTempPath()) with { Url = new(url) }); Assert(page.Id == Id, "A player alias changed its media identity."); }
        var session = await Extract([], html: Html(session: true), request: Request(Path.GetTempPath()) with { Url = new("https://learn.microsoft.com/en-us/events/fixture/first") }); Assert(session.Id == Id, "Event session failed.");
        var modern = Html().Replace("fixture.first", "first"); Assert((await Extract([], html: modern)).Id == Id, "Modern slug-only UID failed.");
    }
    private static Task Boundaries()
    {
        var provider = new MicrosoftLearnProvider();
        foreach (var url in new[] { "http://learn.microsoft.com/en-us/shows/fixture/first", "https://learn.microsoft.com/en-us/docs/fixture/first", "https://learn.microsoft.com/en-us/events/fixture",
            "https://learn.microsoft.com:444/en-us/shows/fixture/first", "https://secret@learn.microsoft.com/en-us/shows/fixture/first", "https://learn-video.azurefd.net/vod/player?id=bad",
            "https://learn-video.azurefd.net/vod/player?show=fixture&ep=first&id=" + Id, "https://learn-video.azurefd.net/vod/player?show=fixture&ep=first&ep=second" })
            Assert(!provider.CanHandle(new(url)), "An unrelated or conflicting Microsoft URL was claimed.");
        Assert(YoutubeDl.Engine.Providers.MediaProviderRegistry.CreateDefault().Resolve(Request(Path.GetTempPath()).Url).Info.Id == "microsoftlearn", "Default dispatch missed Microsoft Learn."); return Task.CompletedTask;
    }
    private static async Task Identities()
    {
        foreach (var html in new[] { Html().Replace("fixture.first", "../other"), Html().Replace("name='showId' content='fixture'", "name='showId' content='other'"),
            Html().Replace("property='og:url' content='https://learn.microsoft.com/en-us/shows/fixture/first'", "property='og:url' content='https://learn.microsoft.com/en-us/shows/fixture/other'"),
            Html().Replace("</head>", "<meta name='entryId' content='" + SecondId + "'></head>") }) await Expect<InvalidDataException>(() => Extract([], html: html));
        await Expect<InvalidDataException>(() => Extract([], entry: Entry(SecondId)));
        var privateVideo = Entry(); privateVideo["publicVideo"] = null; await Expect<NotSupportedException>(() => Extract([], entry: privateVideo));
    }
    private static async Task Resources()
    {
        foreach (var raw in new[] { "http://videoencodingpublic-fixture.b01.azurefd.net/public-" + Id + "/high.mp4", "https://secret@videoencodingpublic-fixture.b01.azurefd.net/public-" + Id + "/high.mp4", Origin + "high.m3u8" })
        { var entry = Entry(); entry["publicVideo"]!["highQualityVideoUrl"] = raw; await Expect<InvalidDataException>(() => Extract([], entry: entry)); }
        foreach (var raw in new[] { "https://unrelated.example/public-" + Id + "/high.mp4", Origin.Replace(Id, SecondId) + "high.mp4" })
        { var entry = Entry(); entry["publicVideo"]!["highQualityVideoUrl"] = raw; await Expect<NotSupportedException>(() => Extract([], entry: entry)); }
    }
    private static string Next(int skip, string order = "desc") => "/api/contentbrowser/search/shows/fixture/episodes?locale=en-us&$top=30&$skip=" + skip + "&$orderby=upload_date%20" + order;
    private static async Task Collections()
    {
        var calls = new List<Uri>(); var page = await Extract(calls, html: Html(""), request: Request(Path.GetTempPath()) with { Url = new("https://learn.microsoft.com/en-us/shows/fixture/") },
            first: Listing(3, Next(2), Row(), Row("hidden", SecondId, true)), second: Listing(3, null, Row("last", SecondId)));
        Assert(page.IsPlaylist && page.Entries.Count == 3 && !page.Entries[1].Available && page.Entries[2].Position == 3 && calls.Count == 3, "Paginated show source positions changed.");
        Assert(page.Entries[2].ExpectedArchiveIdentity!.Id == SecondId && page.Entries[2].Url!.AbsolutePath == "/en-us/shows/fixture/last", "Show episode/expected archive identity changed.");
    }
    private static async Task SplitEncoding()
    {
        var entry = Entry(); entry["publicVideo"]!["audioUrl"] = Origin + "fixture__AACAudio_2Ch_192kbps.mp4" + Query;
        entry["publicVideo"]!["highQualityVideoUrl"] = Origin + "fixture__1920x1080_AACAudio_1067_1.mp4" + Query;
        entry["publicVideo"]!["mediumQualityVideoUrl"] = Origin + "fixture__1280x720_AACAudio_659_2.mp4" + Query;
        entry["publicVideo"]!["lowQualityVideoUrl"] = Origin + "fixture__640x360_AACAudio_246_4.mp4" + Query;
        var page = await Extract([], entry: entry); var video = page.Media.Single(m => m.Id == "low");
        Assert(video.Width == 640 && video.Height == 360 && video.HasAudio == false && !video.IsCompleteMedia, "An encoded video-only file became a complete movie.");
        var plan = YoutubeDl.Engine.Download.MediaDownloadPlan.Create(page, Request(Path.GetTempPath()) with { FormatId = "low", MaximumHeight = 360 });
        Assert(plan.Audio?.Id == "audio" && plan.Audio.Extension == "m4a" && plan.Primary.TimelineId == plan.Audio.TimelineId, "Current progressive playback omitted its supplied audio.");
        var single = YoutubeDl.Engine.Download.MediaDownloadPlan.Create(page, Request(Path.GetTempPath()) with { FormatSelector = "low" }); Assert(single.Audio is null, "An explicit single-video selector gained audio.");
    }
    private static async Task Incomplete()
    {
        foreach (var first in new[] { Listing(2, null, Row()), Listing(2, Next(0), Row()), Listing(2, "https://other.example" + Next(1), Row()), Listing(2, Next(1).Replace("locale=en-us", "locale=fr-fr"), Row()),
            Listing(2, Next(1), Row("first", Id), Row("first", SecondId)) })
            await Expect<InvalidDataException>(() => Extract([], html: Html(""), request: ShowRequest(), first: first));
        await Expect<InvalidDataException>(() => Extract([], html: Html(""), request: ShowRequest(), first: Listing(2, Next(1), Row()), second: Listing(3, null, Row("last", SecondId))));
        var wrong = Row(); wrong["show_uid"] = "other"; await Expect<InvalidDataException>(() => Extract([], html: Html(""), request: ShowRequest(), first: Listing(1, null, wrong)));
    }
    private static NativeDownloadRequest ShowRequest() => Request(Path.GetTempPath()) with { Url = new("https://learn.microsoft.com/en-us/shows/fixture/") };
    private static async Task Series()
    {
        var calls = new List<Uri>(); _ = await Extract(calls, html: Html("", series: true), request: ShowRequest(), first: Listing(1, null, Row()));
        Assert(Uri.UnescapeDataString(calls[1].Query).Contains("upload_date asc"), "Series order changed.");
        await Expect<NotSupportedException>(() => Extract([], html: Html(""), request: ShowRequest() with { MaximumPlaylistEntries = 1 }, first: Listing(2, null, Row(), Row("last", SecondId))));
        await Expect<NotSupportedException>(() => Extract([], html: Html(""), request: ShowRequest() with { Url = new(ShowRequest().Url.AbsoluteUri + "?terms=filtered") }));
    }
    private static async Task Captions()
    {
        foreach (var change in new Action<JsonNode>[] { p => p["captions"]![0]!["language"] = "../en", p => ((JsonArray)p["captions"]!).Add(p["captions"]![0]!.DeepClone()) })
        { var entry = Entry(); change(entry["publicVideo"]!); await Expect<InvalidDataException>(() => Extract([], entry: entry)); }
        var wrongAsset = Entry(); wrongAsset["publicVideo"]!["captions"]![0]!["url"] = Origin.Replace(Id, SecondId) + "captions.vtt"; await Expect<NotSupportedException>(() => Extract([], entry: wrongAsset));
    }
    private static async Task External()
    {
        var html = Html().Replace("<meta name='entryId' content='" + Id + "'>", "<meta name='externalVideoUrl' content='https://www.youtube.com/embed/jNQXAC9IVRw'>");
        var calls = new List<Uri>(); var page = await Extract(calls, html: html); Assert(page.EmbeddedPages.Single().Host == "www.youtube.com" && page.Media.Count == 0 && calls.Count == 1, "External player fetched unrelated Microsoft media.");
    }
    private static async Task Anonymous()
    {
        var options = new MediaHttpOptions(); options.Headers["Authorization"] = "Bearer fixture"; options.Headers["Cookie"] = "fixture=secret";
        using var http = Http([], options: options, observe: r => Assert(!r.Headers.Contains("Authorization") && !r.Headers.Contains("Cookie"), "Caller credentials reached public Microsoft metadata."));
        _ = await new MicrosoftLearnProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
        using var malformed = Http([], rawJson: "bad JSON"); await Expect<InvalidDataException>(() => new MicrosoftLearnProvider().ExtractAsync(Request(Path.GetTempPath()), malformed, default));
        using var duplicate = Http([], rawJson: Entry().ToJsonString().Replace("\"title\":\"Native Microsoft Learn\"", "\"title\":\"Other\",\"title\":\"Native Microsoft Learn\""));
        await Expect<InvalidDataException>(() => new MicrosoftLearnProvider().ExtractAsync(Request(Path.GetTempPath()), duplicate, default));
    }
    private static async Task Archives()
    {
        var dir = Path.Combine(Path.GetTempPath(), "learn-archive-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
        try { var path = Path.Combine(dir, "archive.txt"); await File.WriteAllTextAsync(path, "microsoftlearn " + Id + "\n"); var calls = new List<Uri>(); var engine = new NativeEngine([new MicrosoftLearnProvider()], _ => Http(calls));
            var result = await engine.DownloadAsync(Request(dir) with { Url = new("https://learn-video.azurefd.net/vod/player?id=" + Id), DownloadArchiveFile = path }, null, null, default);
            Assert(result.Succeeded && result.Skipped && calls.Count == 0, "Archived direct entry reached metadata/playback."); }
        finally { Directory.Delete(dir, true); }
    }
    private static async Task Cancellation()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel(); using var http = Http([]);
        await Expect<OperationCanceledException>(() => new MicrosoftLearnProvider().ExtractAsync(Request(Path.GetTempPath()), http, stop.Token));
        using var denied = Http([], status: HttpStatusCode.Forbidden); await Expect<HttpRequestException>(() => new MicrosoftLearnProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
        using var redirected = Http([], redirectApi: true); await Expect<InvalidDataException>(() => new MicrosoftLearnProvider().ExtractAsync(Request(Path.GetTempPath()), redirected, default));
    }
    private static async Task<PageMetadata> Extract(List<Uri> calls, string? html = null, JsonNode? entry = null, NativeDownloadRequest? request = null, JsonNode? first = null, JsonNode? second = null)
    { using var http = Http(calls, html, entry, first, second); return await new MicrosoftLearnProvider().ExtractAsync(request ?? Request(Path.GetTempPath()), http, default); }
    internal static MediaHttp Http(List<Uri> calls, string? html = null, JsonNode? entry = null, JsonNode? first = null, JsonNode? second = null,
        Func<HttpRequestMessage, HttpResponseMessage>? media = null, MediaHttpOptions? options = null, Action<HttpRequestMessage>? observe = null, string? rawJson = null,
        HttpStatusCode status = HttpStatusCode.OK, bool redirectApi = false, Func<Uri, string>? pages = null) => new(new Handler(r =>
    {
        calls.Add(r.RequestUri!); observe?.Invoke(r); var url = r.RequestUri!;
        if (url.Host != "learn.microsoft.com") return media?.Invoke(r) ?? throw new InvalidOperationException("Unexpected Microsoft Learn media request.");
        if (!url.AbsolutePath.StartsWith("/api/")) return new(status) { Content = new StringContent(pages?.Invoke(url) ?? html ?? Html()), RequestMessage = r };
        var json = url.AbsolutePath.Contains("/entries/") ? entry ?? Entry() : Uri.UnescapeDataString(url.Query).Contains("$skip=0") ? first ?? Listing(1, null, Row()) : second ?? Listing(2, null, Row("last", SecondId));
        return new(status) { Content = new StringContent(rawJson ?? json.ToJsonString()), RequestMessage = redirectApi ? new(HttpMethod.Get, "https://other.example/api/entry") : r };
    }), options ?? new());
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
}
