using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Nfb;

internal static class NfbChecks
{
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new("https://www.nfb.ca/film/fixture/"), OutputDirectory = directory, FileName = "nfb", Retries = 0 };
    internal static JsonNode Options(string slug = "fixture", bool french = false, int id = 213822) => new JsonObject {
        ["poster"] = "https://assets.nfbcdn.ca/poster.jpg?width=640", ["language"] = french ? "fr" : "en",
        ["overlay"] = new JsonObject { ["title"] = "Film &amp; title", ["description"] = "Film description", ["url"] = "https://www." + (french ? "onf" : "nfb") + ".ca/film/" + slug + "/" },
        ["gtm"] = new JsonObject { ["registryId"] = id, ["versionType"] = "film", ["versionLang"] = french ? "fr" : "en" },
        ["unstats"] = new JsonObject { ["registryId"] = id, ["type"] = "film", ["slug"] = slug },
        ["source"] = "https://d3acx5b8mnvbua.cloudfront.net/hls/411214_" + id + "_DM-9111/411214_" + id + "_DM-9111.m3u8?Policy=A%2Bb&Signature=C%2Fd&Key-Pair-Id=fixture#expires=4102444800",
        ["dvSource"] = "https://d3acx5b8mnvbua.cloudfront.net/hls/411214_" + id + "_DM-9111_aen/411214_" + id + "_DM-9111_aen.m3u8?Policy=A%2Bb&Signature=C%2Fd&Key-Pair-Id=fixture#expires=4102444800" };
    internal static string Film(int id = 213822) => "<title>Film title</title><div>Rent Buy</div><script>window.DISPLAYED_WORK_ID = " + id + ";</script>" +
        "<script type='application/ld+json'>{\"@type\":[\"Movie\",\"VideoObject\"],\"duration\":\"PT7M\",\"director\":[{\"name\":\"Director\"}]}</script>";
    internal static string Player(JsonNode? data = null, int id = 213822, int duration = 2) => "<video-js id='nfb-player'></video-js><script>window.dataLayer = [{'nfb_version_id':'" + id + "','nfb_version_duration':'" + duration + "'}]; window.IFRAME_OPTIONS = " + (data ?? Options()).ToJsonString() + ";</script>";
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("NFB native player facts bind registry metadata and preserve signed URLs", Facts),
        ("NFB film embed and French ONF aliases retain version identities", Aliases),
        ("NFB URL boundaries reject unrelated hosts and unsupported page types", Addresses),
        ("NFB registry slug analytics and canonical mismatches fail before media", Identities),
        ("NFB unavailable trailer protected and malformed player states cannot download", Unavailable),
        ("NFB described-audio alternatives require explicit selection", Selection),
        ("NFB generic iframe discovery delegates to native film playback", Embed),
        ("NFB native registry archives avoid slug collisions and skip media", Archive),
        ("NFB caller headers survive discovery and HTTP denials remain explicit", Headers),
        ("NFB cancellation prevents playback and publication", Cancellation)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var info = await Engine(calls).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Id == "213822" && info.Title == "Film & title" && info.Uploader == "Director" && info.DurationSeconds == 2 && info.Formats.Count == 2, "Player metadata lost its exact identity or used rounded page duration.");
        Assert(info.Formats[0].Id == "hls" && info.Formats[1].Id == "hls-described" && info.Formats[0].Url!.Query == "?Policy=A%2Bb&Signature=C%2Fd&Key-Pair-Id=fixture" && info.Formats[0].Url!.Fragment == "#expires=4102444800", "Signed resource data or distinct audio variants changed.");
        Assert(info.Formats[0].Note == "Original audio" && info.Formats[1].Note == "Described audio", "Available audio variants lost their user-facing labels.");
        Assert(calls.Count == 2 && calls[1].AbsolutePath == "/film/fixture/embed/player/", "An unexpected endpoint was requested.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "https://nfb.ca/film/fixture", "https://www.nfb.ca/film/fixture/embed/", "https://www.nfb.ca/film/fixture/embed/player/?autoplay=0" })
        {
            var calls = new List<Uri>(); var info = await Engine(calls).InspectAsync(Request(Path.GetTempPath()) with { Url = new(url) }, default);
            Assert(info.Id == "213822" && calls.Count == (url.Contains("/embed") ? 1 : 2), "A film/player alias changed its registry identity.");
        }
        var french = await Engine([], Options("fixture", true, 213823), film: Film(213823), playerId: 213823).InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://www.onf.ca/film/fixture/") }, default);
        Assert(french.Id == "213823" && french.Formats.All(format => format.AudioLanguage == "fr"), "The French language version lost its identity or language facts.");
    }
    private static Task Addresses()
    {
        var provider = new NfbProvider();
        foreach (var url in new[] { "https://www.nfb.ca.evil.example/film/fixture/", "https://user@www.nfb.ca/film/fixture/", "https://www.nfb.ca:444/film/fixture/", "https://www.nfb.ca/film/fixture/other/", "https://www.nfb.ca/series/fixture/season0/" })
            Assert(!provider.CanHandle(new(url)), "An unsupported NFB address was claimed.");
        return Task.CompletedTask;
    }
    private static async Task Identities()
    {
        foreach (var mutate in new Action<JsonNode>[] { data => data["gtm"]!["registryId"] = 1, data => data["unstats"]!["registryId"] = 1,
            data => data["unstats"]!["slug"] = "different", data => data["overlay"]!["url"] = "https://www.nfb.ca/film/other/",
            data => data["source"] = "https://d3acx5b8mnvbua.cloudfront.net/hls/411214_1_DM-9111/411214_1_DM-9111.m3u8" })
        { var data = Options(); mutate(data); await Expect<InvalidDataException>(() => Engine([], data).InspectAsync(Request(Path.GetTempPath()), default)); }
        await Expect<InvalidDataException>(() => Engine([], playerId: 1).InspectAsync(Request(Path.GetTempPath()), default));
        await Expect<InvalidDataException>(() => Engine([], film: Film(1)).InspectAsync(Request(Path.GetTempPath()), default));
        var wrong = Options(); wrong["source"] = "https://other.cloudfront.net/hls/411214_213822_DM-9111/411214_213822_DM-9111.m3u8";
        await Expect<NotSupportedException>(() => Engine([], wrong).InspectAsync(Request(Path.GetTempPath()), default));
    }
    private static async Task Unavailable()
    {
        foreach (var mutate in new Action<JsonNode>[] { data => data["gtm"]!["versionType"] = "trailer", data => data["geoblocked"] = true, data => data["drm"] = true,
            data => data["source"] = null, data => data["source"] = data["source"]!.GetValue<string>().Replace("#expires=4102444800", "#t=10,20") })
        {
            var data = Options(); mutate(data); var calls = new List<Uri>();
            await Expect<NotSupportedException>(() => Engine(calls, data).InspectAsync(Request(Path.GetTempPath()), default));
            Assert(calls.Count == 2, "An unavailable player reached a media request.");
        }
        await Expect<NotSupportedException>(() => Engine([], playerHtml: "<div>Film unavailable</div>").InspectAsync(Request(Path.GetTempPath()), default));
        await Expect<InvalidDataException>(() => Engine([], playerHtml: "<script>window.IFRAME_OPTIONS={bad};</script>").InspectAsync(Request(Path.GetTempPath()), default));
    }
    private static async Task Selection()
    {
        foreach (var selector in new[] { "", "hls-described" })
        {
            var calls = new List<Uri>(); var result = await Engine(calls).DownloadAsync(Request(Path.GetTempPath()) with { Simulate = true, FormatSelector = selector }, null, null, default);
            Assert(result.Succeeded && result.Simulated && calls.Count == 2, "Format selection requested media during simulation.");
        }
    }
    private static async Task Embed()
    {
        var calls = new List<Uri>(); var info = await Engine(calls).InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://article.example/post") }, default);
        Assert(info.Extractor == "nfb" && info.Id == "213822" && calls.Count == 2, "A public iframe bypassed native NFB extraction.");
    }
    private static async Task Archive()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); var path = Path.Combine(directory, "archive.txt");
        try
        {
            await File.WriteAllTextAsync(path, "nfb:registry 213822\n"); var calls = new List<Uri>();
            var result = await Engine(calls).DownloadAsync(Request(directory) with { DownloadArchiveFile = path }, null, null, default);
            Assert(result.Succeeded && result.Skipped && calls.Count == 2, "An archived registry fetched playback resources.");
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }
    private static async Task Headers()
    {
        var count = 0; await Engine([], observe: request => { count++; Assert(request.Headers.UserAgent.ToString() == "NativeFixture/1" && request.Headers.Referrer?.AbsoluteUri == "https://caller.example/", "Caller headers were overwritten."); })
            .InspectAsync(Request(Path.GetTempPath()) with { UserAgent = "NativeFixture/1", Referer = "https://caller.example/" }, default);
        Assert(count == 2, "Discovery requests were missing.");
        var calls = new List<Uri>(); await Expect<HttpRequestException>(() => Engine(calls, status: HttpStatusCode.Forbidden).InspectAsync(Request(Path.GetTempPath()), default));
        Assert(calls.Count == 1, "An HTTP denial triggered an alternate endpoint.");
    }
    private static async Task Cancellation()
    {
        using var stop = new CancellationTokenSource(); var calls = new List<Uri>();
        await Expect<OperationCanceledException>(() => Engine(calls, observe: _ => stop.Cancel()).InspectAsync(Request(Path.GetTempPath()), stop.Token));
        Assert(calls.Count == 1, "Canceled discovery continued to playback metadata.");
    }
    internal static NativeEngine Engine(List<Uri> calls, JsonNode? options = null, string? film = null, int playerId = 213822, string? playerHtml = null,
        Func<HttpRequestMessage, HttpResponseMessage>? media = null, Action<HttpRequestMessage>? observe = null, HttpStatusCode status = HttpStatusCode.OK) => new(httpFactory: request => new MediaHttp(new Handler(message =>
    {
        var url = message.RequestUri!; lock (calls) calls.Add(url); observe?.Invoke(message);
        if (url.Host == "article.example") return new(HttpStatusCode.OK) { Content = new StringContent("<iframe src='https://www.nfb.ca/film/fixture/embed/player/'></iframe>") };
        if (url.Host.EndsWith("nfb.ca") || url.Host.EndsWith("onf.ca")) return new(status) { Content = new StringContent(url.AbsolutePath.Contains("/embed/") ? playerHtml ?? Player(options, playerId) : film ?? Film()) };
        return media?.Invoke(message) ?? throw new InvalidOperationException("Unexpected NFB media request.");
    }), new MediaHttpOptions { UserAgent = request.UserAgent ?? "", Referer = request.Referer ?? "" }));
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
}
