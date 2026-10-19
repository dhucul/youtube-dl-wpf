using System.Net;
using System.Net.Http.Headers;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class PublicationChecks
{
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Failed caption processing preserves the previous media and complete sidecar set", CaptionFailure),
        ("Caption cancellation leaves published files unchanged", CaptionCancellation),
        ("Native engine retries resume staged partial media before publication", Resume),
        ("A locked output rolls back previously moved media sidecars", LockedOutput),
        ("Output folder conflicts fail without moving previously published files", FolderConflict),
        ("Completion callbacks observe the complete published output set", Completion)
    ];

    private static async Task CaptionFailure()
    {
        using var files = new Files();
        await Previous(files);
        var completed = 0;
        var engine = Engine(request => request.RequestUri!.AbsolutePath == "/fr.vtt" ? new(HttpStatusCode.Forbidden) : Response(request.RequestUri.AbsolutePath.EndsWith(".vtt") ? CaptionChecks.Caption : "new media"));
        await Expect<HttpRequestException>(() => engine.DownloadAsync(Request(files.Directory), null, null, default, _ => completed++));
        AssertPrevious(files);
        Assert(completed == 0 && !Directory.EnumerateFiles(files.Directory, ".sidecar-*").Any(), "An unfinished output was reported as complete.");
    }
    private static async Task CaptionCancellation()
    {
        using var files = new Files();
        await Previous(files);
        using var cancellation = new CancellationTokenSource();
        var engine = Engine(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/fr.vtt") cancellation.Cancel();
            return Response(request.RequestUri.AbsolutePath.EndsWith(".vtt") ? CaptionChecks.Caption : "new media");
        });
        await Expect<OperationCanceledException>(() => engine.DownloadAsync(Request(files.Directory), null, null, cancellation.Token));
        AssertPrevious(files);
    }
    private static async Task Resume()
    {
        using var files = new Files();
        await File.WriteAllTextAsync(files.Media, "previous media");
        var requests = 0;
        long? range = null;
        var engine = Engine(request =>
        {
            requests++;
            if (requests == 1)
            {
                var incomplete = Response("abc");
                incomplete.Content.Headers.ContentLength = 6;
                return incomplete;
            }
            range = request.Headers.Range?.Ranges.Single().From;
            var response = Response("def");
            response.StatusCode = HttpStatusCode.PartialContent;
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(3, 5, 6);
            return response;
        });
        var request = Request(files.Directory) with { WriteSubs = false, WriteInfoJson = false };
        await Expect<IOException>(() => engine.DownloadAsync(request, null, null, default));
        Assert(File.ReadAllText(files.Media) == "previous media", "A partial transfer replaced the previous result.");
        var result = await engine.DownloadAsync(request, null, null, default);
        Assert(result.Succeeded && range == 3 && File.ReadAllText(files.Media) == "abcdef", "The staged partial file was not safely resumed.");
        Assert(!Directory.EnumerateDirectories(files.Directory, ".native-download-*").Any(), "A completed workspace was retained.");
    }
    private static async Task LockedOutput()
    {
        using var files = new Files();
        await Previous(files);
        using var locked = new FileStream(files.Media, FileMode.Open, FileAccess.Read, FileShare.Read);
        await Expect<IOException>(() => Engine(_ => Response(CaptionChecks.Caption)).DownloadAsync(Request(files.Directory), null, null, default));
        AssertPrevious(files);
        Assert(!Directory.EnumerateFiles(files.Directory, "*.backup").Any(), "Recovery files escaped into the published output folder.");
    }
    private static async Task FolderConflict()
    {
        using var files = new Files();
        await Previous(files);
        File.Delete(files.FrCaption);
        Directory.CreateDirectory(files.FrCaption);
        await Expect<IOException>(() => Engine(_ => Response(CaptionChecks.Caption)).DownloadAsync(Request(files.Directory), null, null, default));
        Assert(File.ReadAllText(files.Media) == "previous media" && File.ReadAllText(files.Info) == "previous info" && File.ReadAllText(files.EnCaption) == "previous en", "Preflight conflict changed previous files.");
        Assert(Directory.Exists(files.FrCaption), "The existing folder was removed.");
    }
    private static async Task Completion()
    {
        using var files = new Files();
        await Previous(files);
        var completed = 0;
        var result = await Engine(request => Response(request.RequestUri!.AbsolutePath.EndsWith(".vtt") ? CaptionChecks.Caption : "new media"))
            .DownloadAsync(Request(files.Directory), null, null, default, path =>
            {
                completed++;
                Assert(path == files.Media && File.ReadAllText(path) == "new media" && File.ReadAllText(files.EnCaption) == CaptionChecks.Caption && File.ReadAllText(files.FrCaption) == CaptionChecks.Caption,
                    "The callback ran before the output set was complete.");
            });
        Assert(result.Succeeded && completed == 1 && !Directory.EnumerateDirectories(files.Directory, ".native-download-*").Any(), "Successful publication did not clean staging or report exactly once.");
    }
    internal static async Task ProcessingFailure(string ffmpeg)
    {
        using var files = new Files();
        await Previous(files);
        await Expect<InvalidDataException>(() => Engine(_ => Response("invalid media")).DownloadAsync(Request(files.Directory) with
        { WriteSubs = false, AddMetadata = true, FfmpegPath = ffmpeg }, null, null, default));
        AssertPrevious(files);
    }
    private static NativeDownloadRequest Request(string directory) => new() { Url = new Uri("https://media.example/source.mp4"), OutputDirectory = directory, FileName = "result", WriteSubs = true, SubLanguages = "en,fr", WriteInfoJson = true, Retries = 0 };
    private static NativeEngine Engine(Func<HttpRequestMessage, HttpResponseMessage> respond) => new([new Provider()], _ => new MediaHttp(new Handler(respond)));
    private static HttpResponseMessage Response(string body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) };
        response.Headers.ETag = new EntityTagHeaderValue("\"fixture-v1\"");
        return response;
    }
    private sealed class Provider : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("fixture-publication", "Publication fixture", 200);
        public bool CanHandle(Uri url) => url.Host == "media.example";
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(new PageMetadata
        {
            Id = "fixture", Title = "Result", Description = "Description", Media = [new() { Url = request.Url, Extension = "mp4", HasAudio = true, HasVideo = true }],
            Subtitles = [new() { Url = new Uri("https://media.example/en.vtt"), Language = "en" }, new() { Url = new Uri("https://media.example/fr.vtt"), Language = "fr" }]
        });
    }
    private static async Task Previous(Files files)
    { await File.WriteAllTextAsync(files.Media, "previous media"); await File.WriteAllTextAsync(files.Info, "previous info"); await File.WriteAllTextAsync(files.EnCaption, "previous en"); await File.WriteAllTextAsync(files.FrCaption, "previous fr"); }
    private static void AssertPrevious(Files files) => Assert(File.ReadAllText(files.Media) == "previous media" && File.ReadAllText(files.Info) == "previous info" &&
        File.ReadAllText(files.EnCaption) == "previous en" && File.ReadAllText(files.FrCaption) == "previous fr", "A failed or canceled job changed a previously published output.");
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private sealed class Files : IDisposable
    {
        public string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        public string Media => Path.Combine(Directory, "result.mp4");
        public string Info => Path.Combine(Directory, "result.info.json");
        public string EnCaption => Path.Combine(Directory, "result.en.vtt");
        public string FrCaption => Path.Combine(Directory, "result.fr.vtt");
        public Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
