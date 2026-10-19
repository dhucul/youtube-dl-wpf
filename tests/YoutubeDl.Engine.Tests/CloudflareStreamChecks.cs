using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.CloudflareStream;

internal static class CloudflareStreamChecks
{
    private const string Id = "6b9e68b07dfee8cc2d116e4c51d6a957";
    private const string Host = "customer-example.cloudflarestream.com";
    private static string Player(string key) => "<stream src=\"" + key + "\" customer-domain-prefix=\"customer-example\"></stream>";
    private static string Lifecycle(string id = Id, bool live = false, bool input = false) => JsonSerializer.Serialize(new { videoUID = id, live, isInput = input, status = live ? "connected" : "disconnected", chunked = true });
    private const string Metadata = """{"dimensions":{"width":1920,"height":1080},"tt":[],"audio":[],"isPreview":false,"publicDetails":{"title":"Stream fixture"}}""";
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Cloudflare Stream verifies player/customer/video identity and exposes native manifests", MetadataInfo),
        ("Cloudflare Stream preserves supplied tokens and actual embedding origin", Tokens),
        ("Cloudflare Stream provided manifests retain literal playback parameters", DirectManifest),
        ("Cloudflare Stream stream-tag, iframe and SDK embeds use native discovery", Embeds),
        ("Cloudflare Stream missing optional enhancements remain valid metadata", EmptyMetadata),
        ("Cloudflare Stream previews, live inputs and identity mismatches fail explicitly", Invalid),
        ("Cloudflare Stream access denial does not probe alternate domains", Denied),
        ("Cloudflare Stream archive identity follows validated lifecycle metadata", Archive),
        ("Canceled Cloudflare Stream extraction cannot publish media", Cancellation)
    ];
    private static async Task MetadataInfo()
    {
        var calls = new List<Uri>(); var info = await Engine(calls).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Id == Id && info.Title == "Stream fixture" && info.Formats.Count == 2 && info.Formats.Select(format => format.Id).SequenceEqual(new[] { "hls", "dash" }) &&
            info.Formats.All(format => format.Height is null) && info.ThumbnailUrl == "https://" + Host + "/" + Id + "/thumbnails/thumbnail.jpg", "Native Stream identity, formats or truthful size facts changed.");
        Assert(calls.Count == 3 && calls[1].AbsolutePath.EndsWith("/lifecycle") && calls[2].AbsolutePath.EndsWith("/metadata/playerEnhancementInfo.json"), "Stream discovery fetched media instead of bounded metadata.");
    }
    private static async Task Tokens()
    {
        var key = Token(Id); var calls = new List<Uri>();
        var request = Request(Path.GetTempPath()) with { Url = new("https://" + Host + "/" + key + "/iframe?auth=a%2Bb%2Fc"), Referer = "https://article.example/story" };
        var info = await Engine(calls, key: key).InspectAsync(request, default);
        Assert(info.Id == Id && calls.All(url => url.AbsolutePath.StartsWith("/" + key + "/", StringComparison.Ordinal)) && calls.All(url => url.Query == "?auth=a%2Bb%2Fc"), "A signed token or literal access query was changed during discovery.");
        Assert(info.Formats.All(format => format.Url!.AbsolutePath.StartsWith("/" + key + "/") && format.Url.Query == "?auth=a%2Bb%2Fc&parentOrigin=https%3A%2F%2Farticle.example"), "Playback lost the supplied token or actual parent origin.");
    }
    private static async Task DirectManifest()
    {
        using var files = new Files(); var calls = new List<Uri>();
        var url = new Uri("https://" + Host + "/" + Id + "/manifest/video.m3u8?clientBandwidthHint=1.8&signature=a%2Bb");
        var result = await Engine(calls).DownloadAsync(Request(files.Directory) with { Url = url }, null, null, default);
        Assert(result.Succeeded && calls[0] == url && calls.Count == 2 && File.ReadAllText(result.OutputPath!) == "media bytes", "A supplied Stream manifest was rewritten or caused unnecessary player lookup.");
    }
    private static async Task Embeds()
    {
        using var files = new Files();
        foreach (var style in new[] { "stream", "iframe", "script" })
        {
            var calls = new List<Uri>();
            var result = await Engine(calls).DownloadAsync(Request(files.Directory) with { Url = new("https://article.example/" + style), FileName = style }, null, null, default);
            Assert(result.Succeeded && calls.Count == 6 && calls[4].Query == "?parentOrigin=https%3A%2F%2Farticle.example", "A Stream embed lost its native dispatch or actual article origin.");
        }
        var legacy = await Engine([]).InspectAsync(Request(files.Directory) with { Url = new("https://iframe.videodelivery.net/" + Id) }, default);
        Assert(legacy.Id == Id, "A legacy iframe did not follow its declared customer domain.");
        var sdk = await Engine([]).InspectAsync(Request(files.Directory) with { Url = new("https://embed.cloudflarestream.com/embed/sdk.latest.js?video=" + Id) }, default);
        Assert(sdk.Id == Id, "A video-specific SDK address was treated as executable JavaScript.");
        var sdkCalls = new List<Uri>();
        var signedSdk = await Engine(sdkCalls, key: Token(Id)).InspectAsync(Request(files.Directory) with
        { Url = new("https://embed.videodelivery.net/embed/r4xu.fla9.latest.js?video=" + Token(Id) + "&auth=a%2Bb%2Fc") }, default);
        Assert(signedSdk.Id == Id && sdkCalls.Count == 3 && sdkCalls.All(url => url.Query == "?auth=a%2Bb%2Fc"),
            "A legacy SDK alias lost literal supplied access parameters or fetched executable code.");
    }
    private static async Task EmptyMetadata()
    {
        var info = await Engine([], emptyMetadata: true).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Id == Id && info.Title == Id && info.Formats.Count == 2, "An explicit no-content enhancement response hid playable Stream media.");
    }
    private static async Task Invalid()
    {
        await Expect<InvalidDataException>(() => Engine([], lifecycle: Lifecycle(new string('b', 32))).InspectAsync(Request(Path.GetTempPath()), default));
        await Expect<NotSupportedException>(() => Engine([], lifecycle: Lifecycle(input: true)).InspectAsync(Request(Path.GetTempPath()), default));
        await Expect<NotSupportedException>(() => Engine([], metadata: Metadata.Replace("\"isPreview\":false", "\"isPreview\":true")).InspectAsync(Request(Path.GetTempPath()), default));
        await Expect<InvalidDataException>(() => Engine([], player: Player(Id).Replace("customer-example", "customer-another")).InspectAsync(Request(Path.GetTempPath()), default));
        var live = await Engine([], lifecycle: Lifecycle(live: true)).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(live.IsLive && live.Formats.Count == 0 && live.Warnings.Count > 0, "Live input appeared to be a completed native download.");
        var provider = new CloudflareStreamProvider();
        Assert(!provider.CanHandle(new("https://customer-example.cloudflarestream.com.evil.example/" + Id)) && !provider.CanHandle(new("https://user@" + Host + "/" + Id)), "An invalid Stream origin was accepted.");
        await Expect<ArgumentException>(() => Engine([]).InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://" + Host + "/not-an-id/iframe") }, default));
        foreach (var video in new[] { Id + "%2Fmanifest%2Fvideo.m3u8", Id + "%3Fauth%3Dinjected", Id + "&video=" + new string('b', 32) })
        {
            var calls = new List<Uri>();
            await Expect<ArgumentException>(() => Engine(calls).InspectAsync(Request(Path.GetTempPath()) with
            { Url = new("https://embed.cloudflarestream.com/embed/sdk.latest.js?video=" + video) }, default));
            Assert(calls.Count == 0, "An invalid SDK video key reached the network.");
        }
    }
    private static async Task Denied()
    {
        var calls = new List<Uri>();
        await Expect<HttpRequestException>(() => Engine(calls, deny: true).InspectAsync(Request(Path.GetTempPath()), default));
        Assert(calls.Count == 1, "A denied Stream address was retried through another origin.");
    }
    private static async Task Archive()
    {
        using var files = new Files(); var archive = Path.Combine(files.Directory, "history.txt"); File.WriteAllText(archive, "cloudflarestream " + Id + "\n");
        var calls = new List<Uri>();
        var result = await Engine(calls).DownloadAsync(Request(files.Directory) with { DownloadArchiveFile = archive }, null, null, default);
        Assert(result.Succeeded && result.Skipped && calls.Count == 3 && calls.All(url => !url.AbsolutePath.Contains("/manifest/")), "Validated Stream archive identity was lost or media was fetched for a recorded video.");
    }
    private static async Task Cancellation()
    {
        using var files = new Files(); using var stop = new CancellationTokenSource();
        await Expect<OperationCanceledException>(() => Engine([], before: _ => stop.Cancel()).DownloadAsync(Request(files.Directory), null, null, stop.Token));
        Assert(!Directory.EnumerateFiles(files.Directory).Any(), "Canceled Stream discovery published a file.");
    }
    private static string Token(string id)
    {
        string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return Encode("{\"alg\":\"RS256\"}") + "." + Encode("{\"sub\":\"" + id + "\",\"exp\":4102444800}") + ".Zml4dHVyZS1zaWduYXR1cmU";
    }
    private static NativeEngine Engine(List<Uri> calls, string key = Id, string? lifecycle = null, string? metadata = null, string? player = null, bool emptyMetadata = false, bool deny = false, Action<Uri>? before = null) => new(httpFactory: _ => new MediaHttp(new Handler(request =>
    {
        var url = request.RequestUri!; calls.Add(url); before?.Invoke(url);
        if (deny) return new(HttpStatusCode.Forbidden);
        if (url.AbsolutePath.EndsWith("playerEnhancementInfo.json") && emptyMetadata) return new(HttpStatusCode.NoContent) { Content = new StringContent("") };
        var text = url.Host == "article.example" ? url.AbsolutePath == "/stream" ? Player(Id) : url.AbsolutePath == "/script" ? "<script src=\"https://embed.cloudflarestream.com/embed/sdk.latest.js?video=" + Id + "\"></script>" : "<iframe src=\"https://" + Host + "/" + Id + "/iframe\"></iframe>" :
            url.AbsolutePath.EndsWith("/lifecycle") ? lifecycle ?? Lifecycle() : url.AbsolutePath.EndsWith("playerEnhancementInfo.json") ? metadata ?? Metadata :
            url.AbsolutePath.EndsWith("video.m3u8") ? "#EXTM3U\n#EXTINF:1,\nsegment.ts\n#EXT-X-ENDLIST" : url.AbsolutePath.EndsWith("segment.ts") ? "media bytes" : player ?? Player(key);
        return new(HttpStatusCode.OK) { Content = new StringContent(text) };
    })));
    private static NativeDownloadRequest Request(string directory) => new() { Url = new("https://" + Host + "/" + Id + "/iframe"), OutputDirectory = directory, FileName = "stream", Retries = 0 };
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, "cloudflare-" + Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() { var path = Path.GetFullPath(Directory); if (!path.StartsWith(Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Test folder escaped its root."); System.IO.Directory.Delete(path, true); }
    }
}
