using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.YouTube;

internal static class QualitySiteChecks
{
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
        new[] { "mobile-inventory", "browser-webm", "direct-webm", "h264", "hls", "encoded", "metadata", "restricted", "live", "clip", "unresolved", "declared-source", "preview" }
            .Select(mode => ("Quality site " + mode, (Func<Task>)(() => Contract(mode))));
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Contract(string mode)
    {
        if (mode is "mobile-inventory" or "browser-webm" or "direct-webm")
        {
            var node = SabrFixture.Player(); var formats = node["streamingData"]!["adaptiveFormats"]!.AsArray();
            var video = formats[0]!.DeepClone(); video["itag"] = 278; video["mimeType"] = "video/webm; codecs=\"vp9\""; formats.Add(video);
            var audio = formats[1]!.DeepClone(); audio["itag"] = 251; audio["mimeType"] = "audio/webm; codecs=\"opus\""; formats.Add(audio);
            if (mode == "direct-webm") { video["url"] = "https://media.googlevideo.com/selected.webm"; audio["url"] = "https://media.googlevideo.com/selected-audio.webm"; }
            PageMetadata page;
            if (mode == "browser-webm") page = YouTubePlayerParser.ParseResponse(node.ToJsonString(), SabrFixture.VideoId, sabrSession: new(SabrFixture.VideoId, SabrFixture.Endpoint, 1, "fixture", [1]));
            else
            {
                using var http = new MediaHttp(new Handler(_ => node.ToJsonString()), new() { AllowAutoRedirect = false });
                page = await YouTubeMobilePlayback.ReadAsync(SabrFixture.VideoId, new() { Url = new("https://youtu.be/" + SabrFixture.VideoId), OutputDirectory = Path.GetTempPath() }, http, default);
            }
            if (mode == "mobile-inventory")
            {
                Assert(page.Media.Count == 2 && page.Media.All(m => m.Extension != "webm"), "Mobile SABR exposed rejected WebM tracks as ready.");
                Assert(page.ReportedFormats.Where(f => f.Extension == "webm").All(f => f.Availability != FormatAvailability.Ready && f.Url is null && f.UnavailableReason!.Contains("MP4", StringComparison.Ordinal)), "Unavailable mobile renditions lost their actionable explanation.");
            }
            else Assert((mode == "direct-webm" || page.Media.Count == 4) && page.Media.Count(m => m.Extension == "webm") == 2, "A browser session or an exposed direct WebM URL was filtered.");
            return;
        }
        var source = "https://cdn.owned.example/selected/stream";
        var player = "window.initials={xplayerSettings:{sources:{standard:{h264:[{url:'" + (mode == "encoded" ? new string('a', 96) : source) + "',quality:'240p'}]}}}};";
        if (mode == "hls") player = "window.initials={xplayerSettings:{sources:{hls:{h264:{url:'" + source + "'}}}}};";
        if (mode == "preview") player = "jwplayer('selected').setup({isPreview:true,file:'https://cdn.owned.example/selected.mp4'});";
        var declaration = new JsonObject { ["@context"] = "https://schema.org", ["@type"] = "VideoObject", ["name"] = "Owned complete sample", ["duration"] = "PT22S" };
        if (mode == "restricted") declaration["isAccessibleForFree"] = false;
        if (mode == "live") declaration["isLiveBroadcast"] = true;
        if (mode == "clip") declaration["startTime"] = 3;
        if (mode == "declared-source") declaration["contentUrl"] = "https://cdn.owned.example/declared.mp4";
        var structured = mode is "h264" or "hls" or "encoded" ? "" : "<script type='application/ld+json'>" + declaration.ToJsonString() + "</script>";
        if (mode == "unresolved") structured = "<script type='application/ld+json'>{\"@context\":\"https://schema.org\",\"@graph\":[{\"@type\":\"WebPage\",\"mainEntity\":{\"@id\":\"#missing\"}}," + declaration.ToJsonString() + "]}</script>";
        var html = structured + "<script>" + player + ";var advertising='https://ads.owned.example/advert.mp4';</script>";
        var transfers = 0; using var session = new MediaHttp(new Handler(request => { Assert(request.RequestUri!.Host == "owned.example", "Extraction transferred media or advertising."); transfers++; return html; }), new() { AllowAutoRedirect = false });
        var request = new NativeDownloadRequest { Url = new("https://owned.example/watch/selected"), OutputDirectory = Path.GetTempPath() };
        if (mode == "encoded") { await ProviderFamiliesChecks.Expect<NotSupportedException>(() => new GenericPageProvider().ExtractAsync(request, session, default)); Assert(transfers == 1, "Encoded source bytes were fetched as a relative page URL."); return; }
        var result = await new GenericPageProvider().ExtractAsync(request, session, default);
        if (mode is "restricted" or "live" or "clip" or "unresolved" or "preview") { Assert(result.Media.Count == 0 && result.Warnings.Count > 0, "Selected-player fallback overrode an unavailable/restricted/preview declaration."); return; }
        Assert(result.Media.Count == 1 && result.Media[0].Url.Host == "cdn.owned.example", "A selected source was lost or advertising was chosen.");
        if (mode == "declared-source") Assert(result.Media[0].Url.AbsolutePath == "/declared.mp4", "Existing structured content was replaced.");
        else if (mode == "hls") Assert(result.Media[0].Kind == MediaKind.Hls, "The explicit HLS group lost its MIME classification.");
        else Assert(result.Media[0].Extension == "mp4" && result.Media[0].Height == 240, "The H.264 source lost its explicit container or height.");
        if (mode == "metadata") Assert(result.DurationSeconds == 22 && result.Title == "Owned complete sample", "The selected player lost its metadata-only declaration.");
    }
    private sealed class Handler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request)) }); }
    }
}
