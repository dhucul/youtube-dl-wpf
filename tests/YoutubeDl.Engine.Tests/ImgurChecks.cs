using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Imgur;

internal static class ImgurChecks
{
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new("https://imgur.com/a/AbC1234"), OutputDirectory = directory, FileName = "imgur", Retries = 0, DownloadPlaylist = true };
    internal static JsonNode Data(bool sound = true) => new JsonObject { ["id"] = "AbC1234", ["image_count"] = 3, ["is_album"] = true, ["privacy"] = "private", ["title"] = "Album title",
        ["account"] = new JsonObject { ["username"] = "Fixture author" }, ["media"] = new JsonArray(
            new JsonObject { ["id"] = "Still01", ["type"] = "image", ["mime_type"] = "image/jpeg", ["url"] = "https://i.imgur.com/Still01.jpg" },
            Video("Video02", sound), Video("Video03", true)) };
    private static JsonNode Video(string id, bool sound) => new JsonObject { ["id"] = id, ["type"] = "video", ["mime_type"] = "video/mp4", ["url"] = "https://i.imgur.com/" + id + ".mp4?token=A%2Bb&Case=C%2Fd", ["width"] = 160, ["height"] = 96,
        ["metadata"] = new JsonObject { ["title"] = "Selected video", ["description"] = "Description & text", ["has_sound"] = sound, ["duration"] = 2 } };
    internal static string Html(JsonNode? node = null) => "<script>\nwindow.postDataJSON=" + JsonSerializer.Serialize((node ?? Data()).ToJsonString()) + ";</script>";
    internal static string Single(string id = "Video02") => "<meta content='https://i.imgur.com/" + id + ".mp4?token=A%2Bb&amp;Case=C%2Fd' property='og:video'><meta property='og:video:type' content='video/mp4'><meta property='og:video:width' content='160'><meta property='og:video:height' content='96'>";
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Imgur album media retain order silence metadata and literal resource queries", Facts),
        ("Imgur single and gifv aliases share identities without guessing media URLs", Aliases),
        ("Imgur host path and case-sensitive resource boundaries are enforced", Boundaries),
        ("Imgur incomplete duplicate mismatched and malformed inventories fail", Invalid),
        ("Imgur pending posts and missing declarations cannot use loose fallback media", Unavailable),
        ("Imgur selected album simulation retains unavailable source positions", Selection),
        ("Imgur individual archive records skip aliases before network requests", Archive),
        ("Imgur cancellation and HTTP denial do not request media", Cancellation)
    ];
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var info = await Engine(calls, Data(false)).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.IsPlaylist && info.Entries.Count == 3 && !info.Entries[0].Available && info.Entries[1].Position == 2 && info.Uploader == "Fixture author", "The album inventory or author changed.");
        var media = info.Entries[1].ResolvedMedia!;
        Assert(media.Id == "Video02" && media.DurationSeconds == 2 && media.Media[0].HasAudio == false && media.Media[0].IsCompleteMedia && media.Media[0].Url.Query == "?token=A%2Bb&Case=C%2Fd", "Video facts or literal query changed.");
        Assert(calls.Count == 1, "Enumeration fetched individual media.");
        var escaped = Data(); escaped["title"] = "Publisher's \\n title";
        var literalOptions = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var literal = JsonSerializer.Serialize(escaped.ToJsonString(literalOptions), literalOptions).Replace("'", "\\'");
        Assert(literal.Contains("\\'"), "The fixture did not contain Imgur's observed JavaScript apostrophe escape.");
        var quoted = await Engine([], html: "<script>window.postDataJSON=" + literal + ";</script>").InspectAsync(Request(Path.GetTempPath()), default);
        Assert(quoted.Title == "Publisher's \\n title", "JavaScript apostrophe escaping changed literal backslashes or text.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "https://imgur.com/Video02", "https://m.imgur.com/Video02", "https://i.imgur.com/Video02.gifv", "https://imgur.com/Video02.gifv" })
        {
            var calls = new List<Uri>(); var info = await Engine(calls, html: Single()).InspectAsync(Request(Path.GetTempPath()) with { Url = new(url) }, default);
            Assert(info.Id == "Video02" && info.Formats.Count == 1 && info.Formats[0].Width == 160 && calls.Single().AbsoluteUri == "https://imgur.com/Video02", "A player alias changed media identity or guessed a resource.");
        }
        var gallery = await Engine([]).InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://imgur.com/gallery/fixture-title-AbC1234") }, default);
        Assert(gallery.Entries.Count == 3, "Gallery alias lost album entries.");
    }
    private static Task Boundaries()
    {
        var provider = new ImgurProvider();
        foreach (var url in new[] { "https://imgur.com.evil.example/Video02", "https://user@imgur.com/Video02", "https://imgur.com:444/Video02", "https://i.imgur.com/Video02.mp4", "https://imgur.com/a/AbC1234/other", "https://imgur.com/a/AbC1234#Video02" })
            Assert(!provider.CanHandle(new(url)), "Unsupported address claimed by Imgur.");
        return Task.CompletedTask;
    }
    private static async Task Invalid()
    {
        foreach (var mutate in new Action<JsonNode>[] { d => d["id"] = "abC1234", d => d["image_count"] = 4, d => d["media"]![2]!["id"] = "Video02",
            d => d["media"]![1]!["url"] = "https://i.imgur.com/video02.mp4", d => d["media"]![1]!["url"] = "https://evil.example/Video02.mp4",
            d => d["media"]![1]!["metadata"]!["has_sound"] = null, d => d["media"]![1]!["metadata"]!["duration"] = 0 })
        { var data = Data(); mutate(data); await Expect<InvalidDataException>(() => Engine([], data).InspectAsync(Request(Path.GetTempPath()), default)); }
        await Expect<InvalidDataException>(() => Engine([]).InspectAsync(Request(Path.GetTempPath()) with { MaximumPlaylistEntries = 2 }, default));
        await Expect<InvalidDataException>(() => Engine([], html: Html() + Html()).InspectAsync(Request(Path.GetTempPath()), default));
        await Expect<InvalidDataException>(() => Engine([], html: "<script>window.postDataJSON=\"{bad}\";</script>").InspectAsync(Request(Path.GetTempPath()), default));
        await Expect<InvalidDataException>(() => Engine([], html: Single("Video03")).InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://imgur.com/Video02") }, default));
    }
    private static async Task Unavailable()
    {
        var pending = Data(); pending["is_pending"] = true;
        await Expect<NotSupportedException>(() => Engine([], pending).InspectAsync(Request(Path.GetTempPath()), default));
        foreach (var html in new[] { Single(), "<!--" + Html() + "-->", "<template>" + Html() + "</template>", "<script>const sample=" + JsonSerializer.Serialize(Html()) + ";</script>" })
            await Expect<NotSupportedException>(() => Engine([], html: html).InspectAsync(Request(Path.GetTempPath()), default));
    }
    private static async Task Selection()
    {
        var calls = new List<Uri>(); var result = await Engine(calls, Data(false)).DownloadAsync(Request(Path.GetTempPath()) with { PlaylistItems = "2", Simulate = true }, null, null, default);
        Assert(result.Succeeded && calls.Count == 1, "Simulation fetched media or lost the selected silent video.");
    }
    private static async Task Archive()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try { await File.WriteAllTextAsync(file, "imgur Video02\n"); var calls = new List<Uri>();
            var result = await Engine(calls).DownloadAsync(Request(Path.GetTempPath()) with { Url = new("https://i.imgur.com/Video02.gifv"), DownloadArchiveFile = file }, null, null, default);
            Assert(result.Succeeded && result.Skipped && calls.Count == 0, "An archived alias reached the network."); }
        finally { File.Delete(file); }
    }
    private static async Task Cancellation()
    {
        using var stop = new CancellationTokenSource(); var calls = new List<Uri>();
        await Expect<OperationCanceledException>(() => Engine(calls, observe: _ => stop.Cancel()).InspectAsync(Request(Path.GetTempPath()), stop.Token));
        Assert(calls.Count == 1, "Canceled discovery continued.");
        await Expect<HttpRequestException>(() => Engine([], status: HttpStatusCode.Forbidden).InspectAsync(Request(Path.GetTempPath()), default));
    }
    internal static NativeEngine Engine(List<Uri> calls, JsonNode? data = null, string? html = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null,
        Action<HttpRequestMessage>? observe = null, HttpStatusCode status = HttpStatusCode.OK) => new(httpFactory: _ => new MediaHttp(new Handler(message =>
    {
        var url = message.RequestUri!; calls.Add(url); observe?.Invoke(message);
        if (url.Host is "imgur.com" or "m.imgur.com") return new(status) { Content = new StringContent(html ?? Html(data)) };
        return media?.Invoke(message) ?? throw new InvalidOperationException("Unexpected Imgur media request.");
    })));
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
}
