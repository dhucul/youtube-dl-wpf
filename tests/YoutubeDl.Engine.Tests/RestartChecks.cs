using System.Net;
using System.Net.Http.Headers;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class RestartChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Restart policy ignores validated partial bytes and resumes again when re-enabled", Restart),
        ("Resume revalidates a completely written part by its final byte", CompletePart),
        ("Restart policy applies to in-job HTTP retries", Retry),
        ("Restart policy preserves saved and partial files on denial or pre-cancellation", Failure),
        ("Restart policy rejects unsolicited incomplete range responses", InvalidRange),
        ("Restart policy reaches native progressive and whole-resource DASH jobs", NativeRestart),
        ("Restart policy reaches HLS and segmented DASH fragment retries", FragmentRetry),
        ("Restart policy respects no-overwrite and simulation without deleting partial data", NoSideEffects),
        ("Restart and playlist-order extra arguments preserve explicit override order", Arguments)
    ];

    private static async Task Restart()
    {
        using var files = new Files();
        var requests = new List<(long? Range, string? Validator)>();
        using var http = Http(request =>
        {
            requests.Add((request.Headers.Range?.Ranges.Single().From, request.Headers.IfRange?.ToString()));
            return requests.Count switch
            {
                1 => Response("old", 8),
                2 => Response("new", 6),
                _ => Partial("est", 3, 5, 6)
            };
        });
        await Expect<IOException>(() => new FileDownloader(http).DownloadAsync(Url, files.Output, null, null, default, 0));
        await Expect<IOException>(() => new FileDownloader(http) { NoContinue = true }.DownloadAsync(Url, files.Output, null, null, default, 0));
        Assert(requests[1] == (null, null) && File.ReadAllText(files.Output + ".part") == "new", "Restart reused old bytes or validators.");
        await new FileDownloader(http).DownloadAsync(Url, files.Output, null, null, default, 0);
        Assert(requests[2] == (3, "\"fixture\"") && File.ReadAllText(files.Output) == "newest", "Re-enabled resume did not use the new partial entity.");
        Assert(!File.Exists(files.Output + ".part.json"), "A completed restart retained its resume receipt.");
    }

    private static async Task CompletePart()
    {
        using var files = new Files();
        var ranges = new List<long?>();
        using var http = Http(request =>
        {
            var from = request.Headers.Range?.Ranges.Single().From;
            ranges.Add(from);
            if (ranges.Count == 1) return Response("abcde", 6);
            if (from is >= 6) return new(HttpStatusCode.RequestedRangeNotSatisfiable);
            return from == 5 ? Partial("f", 5, 5, 6) : Response("abcdef");
        });
        await Expect<IOException>(() => new FileDownloader(http).DownloadAsync(Url, files.Output, null, null, default, 0));
        // Every byte reached the part, but the job stopped before publishing it.
        File.AppendAllText(files.Output + ".part", "f");
        await new FileDownloader(http).DownloadAsync(Url, files.Output, null, null, default, 0);
        Assert(ranges.Count == 2 && ranges[1] == 5 && File.ReadAllText(files.Output) == "abcdef",
            "A complete part was transferred again instead of revalidating its final byte.");
    }

    private static async Task Retry()
    {
        foreach (var restart in new[] { false, true })
        {
            using var files = new Files();
            var count = 0;
            using var http = Http(request =>
            {
                if (++count == 1) return Response("abc", 6);
                Assert((request.Headers.Range is null) == restart, "The retry ignored the requested resume policy.");
                return restart ? Response("abcdef") : Partial("def", 3, 5, 6);
            });
            await new FileDownloader(http) { NoContinue = restart }.DownloadAsync(Url, files.Output, null, null, default, 1);
            Assert(count == 2 && File.ReadAllText(files.Output) == "abcdef", "A retry duplicated or omitted media bytes.");
        }
    }

    private static async Task Failure()
    {
        using var files = new Files();
        File.WriteAllText(files.Output, "published");
        using var seed = Http(_ => Response("abc", 6));
        await Expect<IOException>(() => new FileDownloader(seed).DownloadAsync(Url, files.Output, null, null, default, 0));
        var receipt = File.ReadAllText(files.Output + ".part.json");
        using var denied = Http(request =>
        {
            Assert(request.Headers.Range is null && request.Headers.IfRange is null, "Restart supplied stale validators on an access failure.");
            return new(HttpStatusCode.Forbidden);
        });
        await Expect<IOException>(() => new FileDownloader(denied) { NoContinue = true }.DownloadAsync(Url, files.Output, null, null, default, 0));
        using var stop = new CancellationTokenSource(); stop.Cancel();
        using var canceled = Http(_ => throw new InvalidOperationException("A pre-canceled request reached HTTP."));
        await Expect<OperationCanceledException>(() => new FileDownloader(canceled) { NoContinue = true }.DownloadAsync(Url, files.Output, null, null, stop.Token, 0));
        Assert(File.ReadAllText(files.Output) == "published" && File.ReadAllText(files.Output + ".part") == "abc" &&
            File.ReadAllText(files.Output + ".part.json") == receipt, "An unstarted or denied restart destroyed recoverable media.");
    }

    private static async Task InvalidRange()
    {
        using var files = new Files(); var count = 0;
        using var http = Http(request =>
        {
            Assert(request.Headers.Range is null, "Restart unexpectedly requested a byte offset.");
            return ++count == 1 ? Partial("def", 3, 5, 6) : Response("abcdef");
        });
        await new FileDownloader(http) { NoContinue = true }.DownloadAsync(Url, files.Output, null, null, default, 0);
        Assert(count == 2 && File.ReadAllText(files.Output) == "abcdef", "Unsolicited partial content was published as a complete file.");
    }

    private static async Task NativeRestart()
    {
        foreach (var dash in new[] { false, true })
        {
            using var files = new Files(); var mediaRequests = 0;
            var engine = Engine(dash ? MediaKind.Dash : MediaKind.Progressive, request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith(".mpd")) return Response(WholeDash);
                Assert(request.Headers.Range is null && request.Headers.IfRange is null, "The native job resumed despite restart being selected.");
                return ++mediaRequests == 1 ? Response("old", 6) : Response("newest");
            });
            var request = Request(files.Directory) with { Url = dash ? new("https://media.example/manifest.mpd") : Url, NoContinue = true };
            File.WriteAllText(files.Output, "published");
            await Expect<IOException>(() => engine.DownloadAsync(request, null, null, default));
            Assert(File.ReadAllText(files.Output) == "published", "A partial native restart replaced published media.");
            var result = await engine.DownloadAsync(request, null, null, default);
            Assert(result.Succeeded && mediaRequests == 2 && File.ReadAllText(files.Output) == "newest", "The native progressive/DASH restart did not publish fresh complete data.");
        }
    }

    private static async Task FragmentRetry()
    {
        foreach (var dash in new[] { false, true })
        {
            using var files = new Files(); var mediaRequests = 0;
            var engine = Engine(dash ? MediaKind.Dash : MediaKind.Hls, request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith(".mpd")) return Response(SegmentedDash);
                if (path.EndsWith(".m3u8")) return Response("#EXTM3U\n#EXTINF:1,\nfragment.ts\n#EXT-X-ENDLIST\n");
                if (path.EndsWith("init.mp4")) return Response("INIT");
                Assert(request.Headers.Range is null && request.Headers.IfRange is null, "A fragment retry ignored restart policy.");
                return ++mediaRequests == 1 ? Response("old", 6) : Response("newest");
            });
            var result = await engine.DownloadAsync(Request(files.Directory) with
            { Url = new("https://media.example/manifest." + (dash ? "mpd" : "m3u8")), NoContinue = true, FragmentRetries = 1 }, null, null, default);
            Assert(result.Succeeded && mediaRequests == 2 && File.ReadAllText(result.OutputPath!) == (dash ? "INITnewest" : "newest"), "A fragmented restart lost or combined media bytes.");
        }
    }

    private static async Task NoSideEffects()
    {
        using var files = new Files();
        var engine = Engine(MediaKind.Progressive, _ => Response("abc", 6));
        var request = Request(files.Directory);
        await Expect<IOException>(() => engine.DownloadAsync(request, null, null, default));
        var partial = Directory.EnumerateFiles(files.Directory, "*.part", SearchOption.AllDirectories).Single();
        File.WriteAllText(files.Output, "published");
        engine = Engine(MediaKind.Progressive, _ => throw new InvalidOperationException("A skipped/simulated job fetched media."));
        var skipped = await engine.DownloadAsync(request with { NoContinue = true, NoOverwrites = true }, null, null, default);
        var simulated = await engine.DownloadAsync(request with { NoContinue = true, Simulate = true }, null, null, default);
        Assert(skipped.Skipped && simulated.Simulated && File.ReadAllText(files.Output) == "published" && File.ReadAllText(partial) == "abc",
            "Restart cleared partial or published files in a job that did not download media.");
    }

    private static Task Arguments()
    {
        var request = NativeExtraArguments.Apply(Request(Path.GetTempPath()), ["--no-continue", "--playlist-reverse", "--playlist-random"]);
        Assert(request.NoContinue && request.PlaylistReverse && request.PlaylistRandom, "Extra arguments lost the requested policies.");
        request = NativeExtraArguments.Apply(request, ["-c", "--no-playlist-reverse", "--no-playlist-random"]);
        Assert(!request.NoContinue && !request.PlaylistReverse && !request.PlaylistRandom, "Later job arguments could not turn off global policies.");
        return Task.CompletedTask;
    }

    private static readonly Uri Url = new("https://media.example/source.mp4");
    private const string WholeDash = "<MPD><Period><AdaptationSet mimeType='video/mp4'><Representation id='v'><BaseURL>source.mp4</BaseURL></Representation></AdaptationSet></Period></MPD>";
    private const string SegmentedDash = "<MPD mediaPresentationDuration='PT1S'><Period><AdaptationSet mimeType='video/mp4'><Representation id='v'><SegmentList duration='1'><Initialization sourceURL='init.mp4'/><SegmentURL media='fragment.m4s'/></SegmentList></Representation></AdaptationSet></Period></MPD>";
    private static NativeDownloadRequest Request(string directory) => new() { Url = Url, OutputDirectory = directory, FileName = "result", Retries = 0, VideoOnly = true };
    private static NativeEngine Engine(MediaKind kind, Func<HttpRequestMessage, HttpResponseMessage> respond) => new([new Provider(kind)], _ => Http(respond));
    private static MediaHttp Http(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new Handler(respond));
    private static HttpResponseMessage Response(string body, long? length = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) };
        response.Headers.ETag = new("\"fixture\"");
        if (length is not null) response.Content.Headers.ContentLength = length;
        return response;
    }
    private static HttpResponseMessage Partial(string body, long start, long end, long total)
    {
        var response = Response(body); response.StatusCode = HttpStatusCode.PartialContent;
        response.Content.Headers.ContentRange = new(start, end, total); return response;
    }
    private sealed class Provider(MediaKind kind) : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("restart-fixture", "Restart fixture", 300);
        public bool CanHandle(Uri url) => url.Host == "media.example";
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(new PageMetadata
        { Id = "fixture", Title = "Fixture", Media = [new() { Url = request.Url, Kind = kind, Extension = "mp4", HasVideo = true, HasAudio = false }] });
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request)); }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, "restart-" + Guid.NewGuid().ToString("N"));
        internal string Output => Path.Combine(Directory, "result.mp4");
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose()
        {
            var path = Path.GetFullPath(Directory);
            if (!path.StartsWith(Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Test folder escaped its root.");
            System.IO.Directory.Delete(path, true);
        }
    }
}
