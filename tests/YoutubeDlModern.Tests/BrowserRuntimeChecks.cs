using System.IO;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.BrowserRuntime;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Protocols.Sabr;

internal static class BrowserRuntimeChecks
{
    private const string Id = "abcdefghijk";
    private static NativeDownloadRequest Request => new() { Url = new("https://www.youtube.com/watch?v=" + Id), OutputDirectory = Path.GetTempPath() };
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Browser playback rejects unsupported account and network preferences before starting", Policy),
        ("Browser playback serializes capture and cancellation does not release another job's slot", Queue),
        ("Browser playback timeout releases its slot for later captures", Timeout),
        ("Browser playback pairs only matching metadata and post-startup request candidates", Pairing),
        ("Browser playback top-level navigation remains bound to the requested watch page", Navigation),
        ("Browser network proxy arguments reject credentials paths and option injection", NetworkArguments),
        ("Browser network policy accepts scoped settings and uses one aggregate rate rule", NetworkPolicy),
        ("Browser playback refresh is bounded and only follows startup observations", Refresh)
        ,("Browser playback accepts the actual publisher referrer",Publisher)
        ,("Browser playback rejects malformed publisher referrers before capture",BadPublisher)
    ];
    private static async Task Policy()
    {
        var calls = 0; var source = new YouTubeBrowserPlaybackSource((_, _, _) => { calls++; return Task.FromResult(new PageMetadata()); });
        foreach (var request in new[] { Request with { Proxy = "http://user:fixture@proxy.example" }, Request with { IpVersion = 4 },
            Request with { SourceAddress = "127.0.0.1" }, Request with { TimeoutSeconds = 12 }, 
            Request with { HttpHeaders = new Dictionary<string, string> { ["Authorization"] = "private-fixture" } } })
            await Expect<NotSupportedException>(() => source.CaptureAsync(Id, request, default));
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Expect<OperationCanceledException>(() => source.CaptureAsync(Id, Request, stop.Token));
        Assert(calls == 0, "Unsupported/canceled requests started a browser capture.");
    }
    private static async Task Publisher()
    {
        var seen=false;var source=new YouTubeBrowserPlaybackSource((_,request,_)=>{seen=request.Referer=="https://www.nowness.com/story/fixture";return Task.FromResult(new PageMetadata{Id=Id});});
        Assert((await source.CaptureAsync(Id,Request with {Referer="https://www.nowness.com/story/fixture"},default)).Id==Id&&seen,"The publisher referrer was lost.");
    }
    private static async Task BadPublisher()
    {
        var source=new YouTubeBrowserPlaybackSource((_,_,_)=>throw new InvalidOperationException("Invalid referrer started capture."));
        foreach(var referer in new[]{"https://user:secret@publisher.example/","https://publisher.example/#fragment","https://publisher.example:8080/","file:///private","https://publisher.example/\r\nInjected: value"})
            await Expect<ArgumentException>(()=>source.CaptureAsync(Id,Request with {Referer=referer},default));
    }
    private static async Task Queue()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0; var maximum = 0; var calls = 0;
        var source = new YouTubeBrowserPlaybackSource(async (_, _, ct) => { calls++; active++; maximum = Math.Max(maximum, active); entered.TrySetResult();
            try { await release.Task.WaitAsync(ct); return new PageMetadata { Id = Id }; } finally { active--; } });
        var first = source.CaptureAsync(Id, Request, default); await entered.Task;
        using var stop = new CancellationTokenSource(); var canceled = source.CaptureAsync(Id, Request, stop.Token); stop.Cancel();
        await Expect<OperationCanceledException>(() => canceled);
        var third = source.CaptureAsync(Id, Request, default);
        Assert(calls == 1 && active == 1, "A queued cancellation released the active capture slot.");
        release.SetResult(); await Task.WhenAll(first, third);
        Assert(calls == 2 && maximum == 1 && active == 0, "Captures overlapped or lost their slot.");
    }
    private static async Task Timeout()
    {
        var calls = 0;
        var source = new YouTubeBrowserPlaybackSource(async (_, _, ct) => { if (++calls == 1) await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct); return new PageMetadata { Id = Id }; }, TimeSpan.FromSeconds(1));
        await Expect<TimeoutException>(() => source.CaptureAsync(Id, Request, default));
        Assert((await source.CaptureAsync(Id, Request, default)).Id == Id && calls == 2, "A timed-out capture blocked a later job.");
    }
    private static Task Pairing()
    {
        var buffer = new YouTubeCaptureBuffer(Id); var endpoint = new Uri("https://r1.googlevideo.com/videoplayback?literal=A%2Bb");
        var player = JsonNode.Parse("""{"playabilityStatus":{"status":"OK"},"videoDetails":{"videoId":"abcdefghijk","title":"Fixture","lengthSeconds":"2"},"streamingData":{"serverAbrStreamingUrl":"https://r1.googlevideo.com/videoplayback","adaptiveFormats":[{"itag":137,"mimeType":"video/mp4; codecs=\"avc1.640028\"","lastModified":"100","height":96,"width":128}]},"playerConfig":{"mediaCommonConfig":{"mediaUstreamerRequestConfig":{"videoPlaybackUstreamerConfig":"AQID"}}}}""")!;
        var request = new SabrRequest { ClientName = 1, ClientVersion = "browser.fixture", PlaybackConfiguration = [1, 2, 3], VideoFormats = [new(137, 100)], ProofToken = new byte[64] };
        var wrong = player.DeepClone(); wrong["videoDetails"]!["videoId"] = "other-video"; buffer.AddPlayer(wrong.ToJsonString());
        buffer.AddRequest(endpoint, request.Encode()); Assert(buffer.TryRead("Fixture/1", default) is null, "Another video's metadata was paired.");
        var mismatch = player.DeepClone(); mismatch["playerConfig"]!["mediaCommonConfig"]!["mediaUstreamerRequestConfig"]!["videoPlaybackUstreamerConfig"] = "BAUG";
        buffer.AddPlayer(mismatch.ToJsonString()); Assert(buffer.TryRead("Fixture/1", default) is null, "Another playback configuration was paired.");
        var startup = new YouTubeCaptureBuffer(Id); startup.AddPlayer(player.ToJsonString()); startup.AddRequest(endpoint, (request with { ProofToken = new byte[10] }).Encode());
        Assert(startup.TryRead("Fixture/1", default) is null, "A startup placeholder was treated as ready capture.");
        buffer.AddPlayer(player.ToJsonString()); var page = buffer.TryRead("Fixture/1", default);
        Assert(page?.Id == Id && page.Media.Single().Sabr!.Session.ProofToken.Length == 64 && page.PlaybackHeaders["User-Agent"] == "Fixture/1", "Matching capture data or its browser agent changed.");
        var denied = player.DeepClone(); denied["playabilityStatus"]!["status"] = "LOGIN_REQUIRED";
        try { buffer.AddPlayer(denied.ToJsonString()); throw new InvalidOperationException("A new playback denial reused old observations."); }
        catch (NotSupportedException) { }
        Assert(buffer.TryRead("Fixture/1", default) is null, "A denied observation retained a previously usable session.");
        return Task.CompletedTask;
    }
    private static Task Navigation()
    {
        Assert(YouTubeBrowserPlaybackSource.IsWatchAddress("https://www.youtube.com/watch?v=" + Id + "&autoplay=1", Id), "The requested watch page was rejected.");
        foreach (var url in new[] { "https://www.youtube.com/watch?v=other-video", "https://www.youtube.com/watch?v=" + Id + "&v=other", "https://user@www.youtube.com/watch?v=" + Id,
            "https://www.youtube.com:444/watch?v=" + Id, "https://accounts.google.com/", "https://www.youtube.com/signin", "https://www.youtube.com.evil.example/watch?v=" + Id })
            Assert(!YouTubeBrowserPlaybackSource.IsWatchAddress(url, Id), "A foreign or interactive navigation was allowed.");
        return Task.CompletedTask;
    }
    private static async Task NetworkArguments()
    {
        Assert(BrowserNetworkSettings.Arguments(Request with { Proxy = "http://127.0.0.1:12345" }) == "--proxy-server=\"http://127.0.0.1:12345\" --proxy-bypass-list=\"<-loopback>\"", "The proxy route or loopback policy changed.");
        Assert(BrowserNetworkSettings.Arguments(Request with { Proxy = "https://[::1]:8443" }).Contains("https://[::1]:8443"), "IPv6 proxy authority was not preserved.");
        Assert(!BrowserNetworkSettings.Arguments(Request with { Proxy = "https://proxy.example" }).Contains("ignore-certificate") &&
            BrowserNetworkSettings.Arguments(Request with { Proxy = "https://proxy.example", NoCheckCertificate = true }).EndsWith(" --ignore-certificate-errors", StringComparison.Ordinal), "Proxy certificate checks changed without explicit opt-in.");
        foreach (var address in new[] { "http://private-user:private-pass@proxy.example", "http://proxy.example/path", "http://proxy.example?secret=value", "http://proxy.example#fragment", "socks5://proxy.example", "http://127.0.0.1:0" })
            await Expect<NotSupportedException>(() => Task.FromResult(BrowserNetworkSettings.Arguments(Request with { Proxy = address })));
        await Expect<ArgumentException>(() => Task.FromResult(BrowserNetworkSettings.Arguments(Request with { Proxy = "http://proxy.example\r\n--no-sandbox" })));
    }
    private static async Task NetworkPolicy()
    {
        var calls = 0; var source = new YouTubeBrowserPlaybackSource((_, request, _) => { calls++; Assert(request.NoCheckCertificate, "Certificate preference was lost."); return Task.FromResult(new PageMetadata { Id = Id }); });
        await source.CaptureAsync(Id, Request with { Proxy = "http://proxy.example:8080", RateLimit = "128K", NoCheckCertificate = true }, default);
        Assert(calls == 1, "Supported network settings were rejected before capture.");
        using var json = System.Text.Json.JsonDocument.Parse(BrowserNetworkSettings.Conditions(128 * 1024));
        var rules = json.RootElement.GetProperty("matchedNetworkConditions"); var rule = rules[0];
        Assert(rules.GetArrayLength() == 1 && rule.GetProperty("urlPattern").GetString() == "" && rule.GetProperty("downloadThroughput").GetInt64() == 131072 && rule.GetProperty("uploadThroughput").GetInt64() == -1 && rule.GetProperty("latency").GetInt32() == 0, "Rate shaping was not global or changed unrelated network properties.");
        await Expect<FormatException>(() => source.CaptureAsync(Id, Request with { RateLimit = "-1" }, default));
        Assert(calls == 1, "An invalid rate reached browser initialization.");
    }
    private static Task Refresh()
    {
        var buffer = new YouTubeCaptureBuffer(Id);
        Assert(!buffer.CanRefresh(TimeSpan.FromSeconds(20)), "A session without observations requested a reload.");
        var player = JsonNode.Parse("""{"playabilityStatus":{"status":"OK"},"videoDetails":{"videoId":"abcdefghijk"}}""")!;
        buffer.AddPlayer(player.ToJsonString());
        var request = new SabrRequest { ClientName = 1, ClientVersion = "fixture", PlaybackConfiguration = [1], VideoFormats = [new(137, 100)], ProofToken = new byte[10] };
        buffer.AddRequest(new("https://r1.googlevideo.com/videoplayback"), request.Encode());
        Assert(!buffer.CanRefresh(TimeSpan.FromSeconds(5)) && buffer.CanRefresh(TimeSpan.FromSeconds(6)), "Refresh ignored the startup wait.");
        buffer.Refreshed(); Assert(!buffer.CanRefresh(TimeSpan.FromMinutes(1)), "The same capture could repeatedly reload playback.");
        var denied = new YouTubeCaptureBuffer(Id); denied.AddPlayer(player.ToJsonString()); denied.AddRequest(new("https://r1.googlevideo.com/videoplayback"), request.Encode());
        player["playabilityStatus"]!["status"] = "LOGIN_REQUIRED";
        try { denied.AddPlayer(player.ToJsonString()); throw new InvalidOperationException("A denied player was accepted."); } catch (NotSupportedException) { }
        Assert(!denied.CanRefresh(TimeSpan.FromMinutes(1)), "An explicit denial was used to trigger session cycling.");
        return Task.CompletedTask;
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}

