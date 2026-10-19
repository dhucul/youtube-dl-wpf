using System.Net;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;
using YoutubeDl.Engine.Providers;

internal static class CaptionChecks
{
    internal const string Caption = "WEBVTT\n\n00:00:00.000 --> 00:00:00.200\nNative caption & punctuation.\n";
    private static readonly Uri MediaUrl = new("https://media.example/clip.mp4");
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Caption language patterns select whole identifiers and prefer authored tracks", () => Sync(Selection)),
        ("Native caption sidecars save multiple languages without requiring ffmpeg", Saving),
        ("Automatic caption saving uses exposed tracks and the same HTTP session", Automatic),
        ("Invalid caption documents and oversized responses retain existing sidecars", Failures),
        ("Caption cancellation preserves existing sidecars", Cancellation),
        ("Embedding-only caption files are cleaned after completion and failure", TemporaryFiles),
        ("Sidecar metadata uses the real ID and thumbnails use the returned image format", Sidecars)
    ];

    private static void Selection()
    {
        var tracks = new[] { Track("en", false), Track("en", true), Track("en-US", true), Track("fr", false), Track("es", true), Track("live_chat", false) };
        var request = Request(Path.GetTempPath()) with { WriteSubs = true, WriteAutoSubs = true };
        Assert(SubtitleSelection.Select(tracks, request).Single() is { Language: "en", Automatic: false }, "Default or authored-track priority lost.");
        Assert(SubtitleSelection.Select(tracks, request with { SubLanguages = "en.*,-en-US" }).Single().Language == "en", "Include/exclude matching failed.");
        Assert(SubtitleSelection.Select(tracks, request with { SubLanguages = "all,-live_chat,-en.*" }).Select(track => track.Language).SequenceEqual(["fr", "es"]), "All-language exclusions failed.");
        Assert(SubtitleSelection.Select(tracks, request with { SubLanguages = "e" }).Count == 0, "Substring matching returned the wrong language.");
        Assert(SubtitleSelection.Select(tracks, request with { WriteSubs = false, SubLanguages = "all" }).All(track => track.Automatic), "Authored captions were selected when only automatic captions were requested.");
        Throws<ArgumentException>(() => SubtitleSelection.Validate("en,["));
        Throws<ArgumentException>(() => SubtitleSelection.Validate("en,,fr"));
    }

    private static async Task Saving()
    {
        using var files = new Files();
        var requests = new List<string>();
        var result = await CreateEngine(Encoding.UTF8.GetBytes("media"), requests).DownloadAsync(Request(files.Directory) with
        { WriteSubs = true, SubLanguages = "en,fr" }, null, null, default);
        Assert(result.Succeeded && File.ReadAllText(Path.Combine(files.Directory, "result.en.vtt")) == Caption &&
            File.ReadAllText(Path.Combine(files.Directory, "result.fr.vtt")) == Caption, "Language sidecars missing.");
        Assert(requests.Count == 3 && !requests.Contains("auto-es.vtt"), "Unexpected caption requests made.");
    }

    private static async Task Automatic()
    {
        using var files = new Files();
        var sessions = 0;
        var requests = new List<string>();
        var engine = CreateEngine(Encoding.UTF8.GetBytes("media"), requests, onSession: () => sessions++);
        var result = await engine.DownloadAsync(Request(files.Directory) with { WriteAutoSubs = true, SubLanguages = "all" }, null, null, default);
        Assert(result.Succeeded && sessions == 1 && requests.SequenceEqual(["clip.mp4", "auto-es.vtt"]), "Captions did not reuse the media HTTP session.");
        Assert(File.ReadAllText(Path.Combine(files.Directory, "result.es.vtt")) == Caption && !File.Exists(Path.Combine(files.Directory, "result.en.vtt")), "Caption-kind selection was ignored.");
    }

    private static async Task Failures()
    {
        using var files = new Files();
        var path = Path.Combine(files.Directory, "result.en.vtt");
        foreach (var bytes in new[] { Encoding.UTF8.GetBytes("<html>Access required</html>"), new byte[2 * 1024 * 1024 + 1] })
        {
            await File.WriteAllTextAsync(path, "previous");
            await Expect<InvalidDataException>(() => CreateEngine(Encoding.UTF8.GetBytes("media"), [], captionResponse: _ => new ByteArrayContent(bytes))
                .DownloadAsync(Request(files.Directory) with { WriteSubs = true }, null, null, default));
            Assert(File.ReadAllText(path) == "previous" && !Directory.EnumerateFiles(files.Directory).Any(file => Path.GetFileName(file).StartsWith(".sidecar-")), "A failed caption response replaced a sidecar.");
        }
        await Expect<InvalidDataException>(() => CreateEngine(Encoding.UTF8.GetBytes("media"), [], tracks: [Track("../escape", false)])
            .DownloadAsync(Request(files.Directory) with { WriteSubs = true }, null, null, default));
    }

    private static async Task Cancellation()
    {
        using var files = new Files();
        using var cancellation = new CancellationTokenSource();
        var path = Path.Combine(files.Directory, "result.en.vtt");
        await File.WriteAllTextAsync(path, "previous");
        var engine = CreateEngine(Encoding.UTF8.GetBytes("media"), [], captionResponse: _ =>
        { cancellation.Cancel(); return new StringContent(Caption); });
        await Expect<OperationCanceledException>(() => engine.DownloadAsync(Request(files.Directory) with { WriteSubs = true }, null, null, cancellation.Token));
        Assert(File.ReadAllText(path) == "previous", "Cancellation replaced an existing caption.");
    }

    private static async Task TemporaryFiles()
    {
        using var files = new Files();
        using var http = new MediaHttp(new Handler(_ => new StringContent(Caption)));
        var page = new Provider().Page;
        using (var captions = await SubtitleWriter.WriteAsync(Request(files.Directory) with { EmbedSubs = true, SubLanguages = "en,fr" }, page,
            Path.Combine(files.Directory, "result.mp4"), http, default))
        {
            Assert(captions.Tracks.Count == 2 && captions.Tracks.All(track => File.Exists(track.Path)), "Temporary captions missing before embedding.");
        }
        Assert(!Directory.EnumerateFiles(files.Directory).Any(), "Temporary captions remained after disposal.");
        using var failureHttp = new MediaHttp(new Handler(request => new StringContent(request.RequestUri!.AbsolutePath.Contains("fr") ? "<html>error</html>" : Caption)));
        await Expect<InvalidDataException>(() => SubtitleWriter.WriteAsync(Request(files.Directory) with { EmbedSubs = true, SubLanguages = "en,fr" }, page,
            Path.Combine(files.Directory, "result.mp4"), failureHttp, default));
        Assert(!Directory.EnumerateFiles(files.Directory).Any(), "A later caption failure leaked temporary files.");
    }

    private static async Task Sidecars()
    {
        using var files = new Files();
        using var http = new MediaHttp(new Handler(_ => new ByteArrayContent([137, 80, 78, 71, 13, 10, 26, 10, 0])));
        var page = new PageMetadata { Id = "actual-id", Title = "Actual title", Description = "Description", ThumbnailUrl = "https://media.example/image.jpg?token=fixture", Extractor = "fixture", DurationSeconds = 0.3 };
        var path = await SidecarWriter.WriteAsync(Request(files.Directory) with { WriteInfoJson = true, WriteDescription = true, WriteThumbnail = true },
            page, Path.Combine(files.Directory, "result.mp4"), http, default);
        using var info = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(files.Directory, "result.info.json")));
        Assert(info.RootElement.GetProperty("id").GetString() == "actual-id" && info.RootElement.GetProperty("duration").GetDouble() == 0.3, "Info JSON contains the title in place of the ID.");
        Assert(path.ThumbnailPath == Path.Combine(files.Directory, "result.png") && path.Files.Count == 3 && File.ReadAllText(Path.Combine(files.Directory, "result.description")) == "Description", "Sidecar paths or descriptions are wrong.");
    }

    internal static NativeEngine CreateEngine(byte[] media, List<string> requests, Func<HttpRequestMessage, HttpContent>? captionResponse = null,
        IReadOnlyList<SubtitleTrack>? tracks = null, Action? onSession = null) => new([new Provider(tracks)], _ =>
    {
        onSession?.Invoke();
        return new MediaHttp(new Handler(request =>
        {
            var name = Path.GetFileName(request.RequestUri!.AbsolutePath);
            requests.Add(name);
            return name == "clip.mp4" ? new ByteArrayContent(media) : captionResponse?.Invoke(request) ?? new StringContent(Caption);
        }));
    });
    internal static NativeDownloadRequest Request(string directory) => new() { Url = MediaUrl, OutputDirectory = directory, FileName = "result", Retries = 0, VideoOnly = true };
    private static SubtitleTrack Track(string language, bool automatic) => new()
    { Language = language, Automatic = automatic, Url = new Uri("https://media.example/" + (automatic ? "auto-" : "") + Uri.EscapeDataString(language) + ".vtt") };
    private sealed class Provider(IReadOnlyList<SubtitleTrack>? tracks = null) : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("fixture-captions", "Captions fixture", 200);
        public bool CanHandle(Uri url) => url.Host == "media.example";
        public PageMetadata Page => new() { Id = "fixture-id", Title = "Caption fixture", Media = [new MediaCandidate
        { Url = MediaUrl, Extension = "mp4", Kind = MediaKind.Progressive, HasAudio = false, HasVideo = true }],
            Subtitles = tracks ?? [Track("en", false), Track("fr", false), Track("es", true)] };
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(Page);
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpContent> content) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content(request) }); }
    private static Task Sync(Action test) { test(); return Task.CompletedTask; }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action test) where T : Exception
    { try { test(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> test) where T : Exception
    { try { await test(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Files : IDisposable
    {
        public string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        public Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
