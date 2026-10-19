using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.YouTube;

internal static class YouTubePlaybackSourceChecks
{
    private static NativeDownloadRequest Request() => new() { Url = new("https://www.youtube.com/watch?v=" + SabrFixture.VideoId), OutputDirectory = Path.GetTempPath(), Retries = 0 };
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("YouTube playback source automatically supplies ready sessions without the mobile request", Automatic),
        ("YouTube playback source mismatched empty and playlist results are rejected", Identity),
        ("YouTube playback source failure is explicit rather than falling through to another session", Failure),
        ("YouTube direct formats and known archive skips do not launch session capture", Unneeded),
        ("YouTube playback source cancellation prevents returning captured media", Cancellation),
        ("YouTube rejected simulated and metadata-only jobs do not initialize browser playback", MetadataOnly),
        ("YouTube playback sources receive an owned scoped discovery-cookie snapshot", CookieContext)
    ];
    private static async Task Automatic()
    {
        var calls = new List<string>(); var source = new Source((_, _, _) => Task.FromResult(ObservedYouTubeChecks.Parse()));
        var info = await Engine(source, calls).InspectAsync(Request(), default);
        Assert(source.Calls == 1 && calls.SequenceEqual(["/watch"]) && info.Formats.Count == 2 && info.Formats.All(format => format.Availability == FormatAvailability.Ready), "Native discovery bypassed or repeated session capture.");
    }
    private static async Task Identity()
    {
        foreach (var page in new[] { ObservedYouTubeChecks.Parse() with { Id = "other" }, ObservedYouTubeChecks.Parse() with { IsPlaylist = true }, ObservedYouTubeChecks.Parse() with { Media = [] } })
            await Expect<InvalidDataException>(() => Engine(new Source((_, _, _) => Task.FromResult(page)), []).InspectAsync(Request(), default));
    }
    private static async Task Failure()
    {
        var calls = new List<string>();
        await Expect<NotSupportedException>(() => Engine(new Source((_, _, _) => throw new NotSupportedException("Fixture unavailable.")), calls).InspectAsync(Request(), default));
        Assert(calls.SequenceEqual(["/watch"]), "A failed session capture triggered another playback path.");
    }
    private static async Task Unneeded()
    {
        var source = new Source((_, _, _) => throw new InvalidOperationException("Capture must not run.")); var calls = new List<string>();
        var info = await Engine(source, calls, direct: true).InspectAsync(Request(), default);
        Assert(info.Formats.Any(format => format.Availability == FormatAvailability.Ready) && source.Calls == 0, "Exposed ready media unnecessarily launched a browser.");
        using var files = new SabrFixture.Files(); var path = Path.Combine(files.Directory, "archive.txt"); await File.WriteAllTextAsync(path, "youtube " + SabrFixture.VideoId + "\n"); calls.Clear();
        var result = await Engine(source, calls).DownloadAsync(Request() with { DownloadArchiveFile = path }, null, null, default);
        Assert(result.Succeeded && result.Skipped && calls.Count == 0 && source.Calls == 0, "An archived video launched discovery or session capture.");
    }
    private static async Task Cancellation()
    {
        using var stop = new CancellationTokenSource(); var source = new Source((_, _, _) => { stop.Cancel(); return Task.FromResult(ObservedYouTubeChecks.Parse()); });
        await Expect<OperationCanceledException>(() => Engine(source, []).InspectAsync(Request(), stop.Token));
    }
    private static async Task MetadataOnly()
    {
        using var files = new SabrFixture.Files(); var source = new Source((_, _, _) => throw new InvalidOperationException("Browser capture must not run.")); var calls = new List<string>();
        var filtered = await Engine(source, calls).DownloadAsync(Request() with { MatchTitle = "never matches this title", OutputDirectory = files.Directory }, null, null, default);
        Assert(filtered.Filtered && calls.SequenceEqual(["/watch"]), "A title-rejected video initialized playback.");
        foreach (var simulate in new[] { true, false })
        {
            calls.Clear(); var result = await Engine(source, calls, mobile: true).DownloadAsync(Request() with { OutputDirectory = files.Directory, Simulate = simulate, SkipDownload = !simulate, WriteInfoJson = !simulate }, null, null, default);
            Assert(result.Succeeded && source.Calls == 0 && calls.SequenceEqual(["/watch", "/youtubei/v1/player"]), "A metadata-only job initialized browser playback or fetched media.");
        }
    }
    private static async Task CookieContext()
    {
        var jar = YouTubeCookieChecks.Jar(); var source = new ContextSource();
        var engine = new NativeEngine(youTubePlaybackSource: source, httpFactory: _ => new MediaHttp(new SabrFixture.Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SabrFixture.Watch) })), new MediaHttpOptions { CookieContainer = jar }));
        var info = await engine.InspectAsync(Request() with { CookieFile = "selected-fixture-already-loaded" }, default);
        Assert(info.Id == SabrFixture.VideoId && source.Cookies?.Count == 3, "The source lost the discovery session's selected cookie state.");
        jar.Add(new("https://www.youtube.com/"), new Cookie("host-fixture", "changed-after-capture", "/"));
        Assert(source.Cookies!.Items.Single(cookie => cookie.Name == "host-fixture").Value == "host-value", "The source retained mutable caller cookies.");
        await Expect<NotSupportedException>(() => Engine(new Source((_, _, _) => Task.FromResult(ObservedYouTubeChecks.Parse())), []).InspectAsync(Request() with { CookieFile = "selected-fixture" }, default));
    }
    private sealed class ContextSource : IYouTubePlaybackSource
    {
        internal YouTubePlaybackCookies? Cookies;
        public Task<PageMetadata> CaptureAsync(string id, NativeDownloadRequest request, CancellationToken ct) => throw new InvalidOperationException("The scoped overload was not used.");
        public Task<PageMetadata> CaptureAsync(string id, NativeDownloadRequest request, YouTubePlaybackCookies cookies, CancellationToken ct) { Cookies = cookies; return Task.FromResult(ObservedYouTubeChecks.Parse()); }
    }
    private static NativeEngine Engine(IYouTubePlaybackSource source, List<string> calls, bool direct = false, bool mobile = false) => new(youTubePlaybackSource: source,
        httpFactory: _ => new MediaHttp(new SabrFixture.Handler(message =>
        {
            calls.Add(message.RequestUri!.AbsolutePath);
            if (mobile && message.RequestUri.AbsolutePath == "/youtubei/v1/player") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SabrFixture.Player().ToJsonString()) });
            if (message.RequestUri.AbsolutePath != "/watch") throw new InvalidOperationException("Unexpected YouTube request.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(direct ? "var ytInitialPlayerResponse=" + YouTubeChecks.FixtureJson + ";" : SabrFixture.Watch) });
        })));
    private sealed class Source(Func<string, NativeDownloadRequest, CancellationToken, Task<PageMetadata>> capture) : IYouTubePlaybackSource
    { internal int Calls; public Task<PageMetadata> CaptureAsync(string id, NativeDownloadRequest request, CancellationToken ct) { Calls++; return capture(id, request, ct); } }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
