using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Wikimedia;

internal static class WikimediaChecks
{
    internal const string Original = "https://upload.wikimedia.org/wikipedia/commons/a/ab/Fixture.webm?Token=A%2Bb&case=C%2Fd";
    internal const string Derived = "https://upload.wikimedia.org/wikipedia/commons/transcoded/a/ab/Fixture.webm/Fixture.webm.240p.vp9.webm?Token=A%2Bb&case=C%2Fd";
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new("https://commons.wikimedia.org/wiki/File:Fixture.webm"), OutputDirectory = directory, FileName = "wikimedia", Retries = 0 };
    internal static JsonNode Data(bool silent = false) => JsonNode.Parse("""
    {"batchcomplete":true,"query":{"pages":[{"pageid":42,"ns":6,"title":"File:Fixture.webm","imagerepository":"local","videoinfo":[{
    "canonicaltitle":"File:Fixture.webm","mediatype":"VIDEO","mime":"video/webm","size":1000,"duration":2,"width":1280,"height":720,"user":"Uploader",
    "url":"https://upload.wikimedia.org/wikipedia/commons/a/ab/Fixture.webm?Token=A%2Bb&case=C%2Fd","descriptionurl":"https://commons.wikimedia.org/wiki/File:Fixture.webm",
    "thumburl":"https://upload.wikimedia.org/wikipedia/commons/thumb/a/ab/Fixture.webm/640px--Fixture.webm.jpg","thumbwidth":640,"thumbheight":360,
    "responsiveUrls":{"1.5":"https://upload.wikimedia.org/wikipedia/commons/thumb/a/ab/Fixture.webm/960px--Fixture.webm.jpg"},
    "metadata":[{"name":"video","value":[{"name":"frame_rate","value":24}]}],
    "extmetadata":{"ObjectName":{"value":"<b>File &amp; title</b>"},"Artist":{"value":"<a href='/wiki/User:Creator'>Creator</a>"}},
    "derivatives":[{"src":"https://upload.wikimedia.org/wikipedia/commons/a/ab/Fixture.webm?utm_source=api","type":"video/webm; codecs=\"vp9, opus\"","width":1280,"height":720},
    {"src":"https://upload.wikimedia.org/wikipedia/commons/transcoded/a/ab/Fixture.webm/Fixture.webm.240p.vp9.webm?Token=A%2Bb&case=C%2Fd","type":"video/webm; codecs=\"vp9, opus\"","transcodekey":"240p.vp9.webm","width":426,"height":240},
    {"src":"https://upload.wikimedia.org/wikipedia/commons/transcoded/a/ab/Fixture.webm/Fixture.webm.360p.mpeg4.mov","type":"video/quicktime; codecs=\"mp4v\"","transcodekey":"360p.mpeg4.mov","width":640,"height":360}],
    "timedtext":[{"src":"https://commons.wikimedia.org/w/api.php?action=timedtext&title=File%3AFixture.webm&lang=en&trackformat=vtt&origin=%2A","kind":"subtitles","type":"text/vtt","srclang":"en"}]
    }]}]}}
    """.Replace("vp9, opus", silent ? "vp9" : "vp9, opus"))!;
    internal static JsonNode Info(JsonNode data) => data["query"]!["pages"]![0]!["videoinfo"]![0]!;
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Wikimedia file metadata preserves resource queries roles captions and image variants", Facts),
        ("Wikimedia file URL aliases share identity and direct-file aliases stay original-only", Aliases),
        ("Wikimedia shared repository lookup resolves the authoritative file identity", Shared),
        ("Wikimedia normalization and redirects remain tied to the requested title", Redirects),
        ("Wikimedia unknown article and credential boundaries are not claimed", Addresses),
        ("Wikimedia file and repository resource mismatches fail before transfer", Identities),
        ("Wikimedia hidden missing and historical file requests cannot return current media", Unavailable),
        ("Wikimedia caption identity language and origin checks reject unrelated tracks", Captions),
        ("Wikimedia default selection avoids incomplete video-only alternatives", Selection),
        ("Wikimedia page archives skip transfer after canonical metadata lookup", Archive),
        ("Wikimedia cancellation and API denials stop before media", Cancellation)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var info = await Engine(calls).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Id == "42" && info.Title == "File & title" && info.Uploader == "Creator" && info.Formats.Count == 3 && info.Thumbnails.Count == 2 && info.Subtitles.Single().Language == "en", "Wikimedia metadata or alternatives were lost.");
        Assert(info.Formats[0].Url!.AbsoluteUri == Original && info.Formats[1].Url!.AbsoluteUri == Derived && info.Formats[2].HasAudio == false && info.Formats[0].FramesPerSecond == 24 && info.Formats[1].FramesPerSecond is null && calls.Count == 1, "Literal URLs, offered track roles or actual frame-rate facts changed.");
        var bucket = Data(); Info(bucket)["thumburl"] = Info(bucket)["thumburl"]!.GetValue<string>().Replace("upload.wikimedia.org", "thumb.wikimedia.org");
        var imageInfo = await Engine([], bucket).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(imageInfo.Thumbnails.Count == 2 && imageInfo.Thumbnails.All(image => image.Width is null && image.Height is null), "Logical display sizes became unverified image pixel dimensions.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "https://commons.wikimedia.org/wiki/Image:Fixture.webm", "https://commons.m.wikimedia.org/wiki/File%3AFixture.webm", "https://commons.wikimedia.org/w/index.php?title=File%3AFixture.webm" })
            Assert((await Engine([]).InspectAsync(Request(Path.GetTempPath()) with { Url = new(url) }, default)).Id == "42", "An alias changed its file identity.");
        var direct = await Engine([]).InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://commons.wikimedia.org/wiki/Special:FilePath/Fixture.webm") }, default);
        Assert(direct.Formats.Count == 1 && direct.Formats[0].Id == "original", "A direct-file alias selected another rendition.");
    }
    private static async Task Shared()
    {
        var calls = new List<Uri>(); var info = await Engine(calls, shared: true).InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://en.wikipedia.org/wiki/File:Fixture.webm") }, default);
        Assert(info.Id == "42" && calls.Select(url => url.Host).SequenceEqual(["en.wikipedia.org", "commons.wikimedia.org"]), "A shared file used the referring page's identity.");
    }
    private static async Task Redirects()
    {
        var data = Data(); data["query"]!["redirects"] = new JsonArray(new JsonObject { ["from"] = "File:Old.webm", ["to"] = "File:Fixture.webm" });
        var info = await Engine([], data).InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://commons.wikimedia.org/wiki/File:Old.webm") }, default);
        Assert(info.Id == "42", "A bound file redirect failed.");
        data["query"]!["redirects"]![0]!["from"] = "File:Unrelated.webm";
        await Expect<InvalidDataException>(() => Engine([], data).InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://commons.wikimedia.org/wiki/File:Old.webm") }, default));
    }
    private static Task Addresses()
    {
        var provider = new WikimediaProvider();
        foreach (var url in new[] { "https://commons.wikimedia.org.evil.example/wiki/File:Fixture.webm", "https://user@commons.wikimedia.org/wiki/File:Fixture.webm", "https://commons.wikimedia.org:444/wiki/File:Fixture.webm", "https://en.wikipedia.org/wiki/Article" }) Assert(!provider.CanHandle(new(url)), "An unrelated address was claimed.");
        return Task.CompletedTask;
    }
    private static async Task Identities()
    {
        foreach (var change in new Action<JsonNode>[] { d => d["canonicaltitle"] = "File:Other.webm", d => d["mediatype"] = "AUDIO", d => d["url"] = Original.Replace("Fixture", "Other"), d => d["url"] = Original.Replace("/commons/", "/en/"),
            d => d["derivatives"]![1]!["src"] = Derived.Replace("Fixture.webm.240", "Other.webm.240"), d => d["descriptionurl"] = "https://commons.wikimedia.org/wiki/User:Fixture.webm", d => d["thumburl"] = "https://upload.wikimedia.org/wikipedia/commons/thumb/a/ab/Other.webm/640px.jpg" })
        { var data = Data(); change(Info(data)); await Expect<InvalidDataException>(() => Engine([], data).InspectAsync(Request(Path.GetTempPath()), default)); }
    }
    private static async Task Unavailable()
    {
        var hidden = Data(); Info(hidden)["filehidden"] = true;
        await Expect<NotSupportedException>(() => Engine([], hidden).InspectAsync(Request(Path.GetTempPath()), default));
        var missing = Data(); missing["query"]!["pages"]![0]!["missing"] = true;
        await Expect<NotSupportedException>(() => Engine([], missing).InspectAsync(Request(Path.GetTempPath()), default));
        var calls = new List<Uri>(); await Expect<NotSupportedException>(() => Engine(calls).InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://commons.wikimedia.org/wiki/File:Fixture.webm?oldid=123") }, default));
        Assert(calls.Count == 0, "A historical URL was silently replaced with the current version.");
    }
    private static async Task Captions()
    {
        foreach (var (api, label) in new[] { ("zh-cn", "zh-Hans-CN"), ("zh-tw", "zh-Hant-TW") })
        {
            var mapped = Data(); var item = Info(mapped)["timedtext"]![0]!; item["srclang"] = label; item["src"] = item["src"]!.GetValue<string>().Replace("lang=en", "lang=" + api);
            var info = await Engine([], mapped).InspectAsync(Request(Path.GetTempPath()), default);
            Assert(info.Subtitles.Single().Language == label && info.Subtitles.Single().Url.Query.Contains("lang=" + api), "An observed IETF language alias changed its supplied caption URL.");
        }
        foreach (var change in new Func<string, string>[] { value => value.Replace("Fixture.webm", "Other.webm"), value => value.Replace("commons.wikimedia.org", "evil.example"), value => value.Replace("lang=en", "lang=fr"), value => value.Replace("File%3A", "User%3A"), value => value + "&action=edit" })
        { var data = Data(); var item = Info(data)["timedtext"]![0]!; item["src"] = change(item["src"]!.GetValue<string>()); await Expect<InvalidDataException>(() => Engine([], data).InspectAsync(Request(Path.GetTempPath()), default)); }
    }
    private static async Task Selection()
    {
        var provider = new WikimediaProvider(); using var http = Session([], Data());
        var request = Request(Path.GetTempPath()) with { MaximumHeight = 360 };
        var page = await provider.ExtractAsync(request, http, default); var selected = YoutubeDl.Engine.Download.MediaDownloadPlan.Create(page, request);
        Assert(selected.Primary.Id == "240p.vp9.webm" && selected.Audio is null, "An incomplete video-only rendition displaced complete media.");
        using var silentHttp = Session([], Data(true)); var silent = await provider.ExtractAsync(request, silentHttp, default);
        Assert(silent.Media.All(item => item.IsCompleteMedia), "A silent file incorrectly required a missing audio track.");
    }
    private static async Task Archive()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try { await File.WriteAllTextAsync(path, "wikimedia:page commons.wikimedia.org/42\n"); var calls = new List<Uri>();
            var result = await Engine(calls).DownloadAsync(Request(Path.GetTempPath()) with { DownloadArchiveFile = path }, null, null, default);
            Assert(result.Succeeded && result.Skipped && calls.Count == 1, "An archived Wikimedia file transferred media."); }
        finally { File.Delete(path); }
    }
    private static async Task Cancellation()
    {
        using var stop = new CancellationTokenSource(); var calls = new List<Uri>();
        await Expect<OperationCanceledException>(() => Engine(calls, observe: _ => stop.Cancel()).InspectAsync(Request(Path.GetTempPath()), stop.Token));
        Assert(calls.Count == 1, "Canceled Wikimedia discovery continued.");
        await Expect<HttpRequestException>(() => Engine([], status: HttpStatusCode.Forbidden).InspectAsync(Request(Path.GetTempPath()), default));
    }
    internal static NativeEngine Engine(List<Uri> calls, JsonNode? data = null, bool shared = false, Func<HttpRequestMessage, HttpResponseMessage>? media = null,
        Action<HttpRequestMessage>? observe = null, HttpStatusCode status = HttpStatusCode.OK) => new(httpFactory: _ => Session(calls, data ?? Data(), shared, media, observe, status));
    private static MediaHttp Session(List<Uri> calls, JsonNode data, bool shared = false, Func<HttpRequestMessage, HttpResponseMessage>? media = null,
        Action<HttpRequestMessage>? observe = null, HttpStatusCode status = HttpStatusCode.OK) => new(new Handler(message =>
    {
        var url = message.RequestUri!; calls.Add(url); observe?.Invoke(message);
        if (url.AbsolutePath == "/w/api.php" && url.Query.Contains("action=query"))
        {
            var response = data.DeepClone(); if (shared && url.Host == "en.wikipedia.org") { var p = response["query"]!["pages"]![0]!; p["imagerepository"] = "shared"; p["missing"] = true; p.AsObject().Remove("pageid"); }
            return new(status) { Content = new StringContent(response.ToJsonString()) };
        }
        return media?.Invoke(message) ?? throw new InvalidOperationException("Unexpected Wikimedia media request.");
    }));
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
