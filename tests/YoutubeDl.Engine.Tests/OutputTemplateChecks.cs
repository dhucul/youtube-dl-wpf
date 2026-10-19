using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class OutputTemplateChecks
{
    private static readonly Uri Url = new("https://media.example/source.mp4");
    private static readonly MediaCandidate Format = new() { Id = "137", Url = Url, Extension = "mp4", Height = 1080, Width = 1920, HasAudio = true, HasVideo = true };
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Native output templates expand metadata, folders, numeric formats, and percent literals", () => Sync(Fields)),
        ("Output precision counts Unicode characters or UTF-8 bytes without splitting them", () => Sync(Unicode)),
        ("Missing output values support alternatives and conditional replacements", () => Sync(Alternatives)),
        ("Output values cannot inject directories and reserved Windows names are usable", () => Sync(Filenames)),
        ("Native output-template downloads publish matching media and sidecar names", Download),
        ("Playlist templates retain the original indexes and playlist context", Playlist),
        ("No-overwrite skips existing output before transfer or processing", NoOverwrite),
        ("Concurrent no-overwrite downloads retain the first complete result", Concurrent),
        ("Completion callbacks can start another download without retaining save locks", ReentrantCompletion),
        ("Unsupported output syntax fails before media transfer", Invalid)
    ];
    private static PageMetadata Page => new() { Id = "video-id", Title = "Template fixture", Uploader = "Creator", DurationSeconds = 9.4, Media = [Format],
        ReportedFormats = [new() { Id = "137", FramesPerSecond = 29.97, FileSize = 1000 }] };
    private static NativeDownloadRequest Request(string root) => new() { Url = Url, OutputDirectory = root, Retries = 0 };
    private static void Fields()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "template-preview"));
        var result = NativeOutputTemplate.Render(Request(root) with { OutputTemplate = "%(uploader)s/%(title)s [%(id)s].%(ext)s" }, Page, Format, "mp4");
        Assert(result == Path.Combine(root, "Creator", "Template fixture [video-id].mp4"), "Basic fields or folder paths changed.");
        var numeric = NativeOutputTemplate.Render(Request(root) with { OutputTemplate = "%(height)05d-%(duration).2f-%(fps).1f-100%%.%(ext)s" }, Page, Format, "mp4");
        Assert(Path.GetFileName(numeric) == "01080-9.40-30.0-100%.mp4", "Numeric or percent formatting changed.");
        var absolute = NativeOutputTemplate.Render(Request(root) with { OutputTemplate = Path.Combine(root, "fixed", "%(id)s.%(ext)s") }, Page, Format, "mp4");
        Assert(absolute == Path.Combine(root, "fixed", "video-id.mp4"), "An explicit absolute template was changed.");
    }
    private static void Unicode()
    {
        var page = Page with { Title = "é😀tail" };
        var root = Path.GetTempPath();
        Assert(Path.GetFileName(NativeOutputTemplate.Render(Request(root) with { OutputTemplate = "%(title).4B.%(ext)s" }, page, Format, "mp4")) == "é.mp4", "Byte precision split a UTF-8 character.");
        Assert(Path.GetFileName(NativeOutputTemplate.Render(Request(root) with { OutputTemplate = "%(title).3s.%(ext)s" }, page, Format, "mp4")) == "é😀t.mp4", "Character precision split a surrogate pair.");
    }
    private static void Alternatives()
    {
        var request = Request(Path.GetTempPath()) with { OutputTemplate = "%(uploader,creator|Anonymous)s-%(playlist_index&{} - |)s%(title)s.%(ext)s" };
        var page = Page with { Uploader = null };
        Assert(Path.GetFileName(NativeOutputTemplate.Render(request, page, Format, "mp4")) == "Anonymous-Template fixture.mp4", "Missing field defaults changed.");
        Assert(Path.GetFileName(NativeOutputTemplate.Render(request with { PlaylistIndex = 3 }, page, Format, "mp4")) == "Anonymous-3 - Template fixture.mp4", "Conditional replacement changed.");
    }
    private static void Filenames()
    {
        var root = Path.Combine(Path.GetTempPath(), "output-fixture");
        var request = Request(root) with { OutputTemplate = "%(uploader)s/%(title)s.%(ext)s" };
        var result = NativeOutputTemplate.Render(request, Page with { Uploader = "../escape", Title = "CON" }, Format, "mp4");
        Assert(Path.GetDirectoryName(result) == Path.Combine(root, ".._escape") && Path.GetFileName(result) == "_CON.mp4", "Metadata escaped the configured directory or reserved names were retained.");
        var restricted = NativeOutputTemplate.Render(request with { RestrictFilenames = true }, Page with { Title = "résumé 🎬" }, Format, "mp4");
        Assert(Path.GetFileName(restricted) == "resume__.mp4", "Restricted filename conversion failed.");
        var longName = NativeOutputTemplate.Render(request, Page with { Title = new string('x', 400) }, Format, "mp4");
        Assert(Path.GetFileName(longName).Length <= 240 && longName.EndsWith(".mp4"), "Filename limits discarded the media extension.");
    }
    private static async Task Download()
    {
        using var files = new Files();
        var result = await Engine(Page).DownloadAsync(Request(files.Directory) with
        { OutputTemplate = "%(uploader)s/%(title)s [%(id)s].%(ext)s", WriteInfoJson = true }, null, null, default);
        var expected = Path.Combine(files.Directory, "Creator", "Template fixture [video-id].mp4");
        Assert(result.Succeeded && result.OutputPath == expected && File.ReadAllText(expected) == "media" && File.Exists(Path.ChangeExtension(expected, "info.json")), "Media and sidecar output names diverged.");
    }
    private static async Task Playlist()
    {
        using var files = new Files();
        var page = new PageMetadata { Id = "list-id", PlaylistId = "list-id", Title = "List", IsPlaylist = true, Entries =
        [new(2, "two", "Second", Url, 1, null, true, Page with { Id = "two", Title = "Second" }), new(7, "seven", "Seventh", Url, 1, null, true, Page with { Id = "seven", Title = "Seventh" })] };
        var result = await Engine(page).DownloadAsync(Request(files.Directory) with { OutputTemplate = "%(playlist)s/%(playlist_index)03d - %(title)s.%(ext)s" }, null, null, default);
        Assert(result.Succeeded && result.OutputPaths.Select(Path.GetFileName).SequenceEqual(["002 - Second.mp4", "007 - Seventh.mp4"]) &&
            result.OutputPaths.All(path => Path.GetDirectoryName(path) == Path.Combine(files.Directory, "List")), "Playlist output context was lost or indexes were renumbered.");
    }
    private static async Task NoOverwrite()
    {
        using var files = new Files();
        var path = Path.Combine(files.Directory, "Template fixture [video-id].mp4");
        await File.WriteAllTextAsync(path, "previous");
        var requests = 0;
        var completed = 0;
        var result = await Engine(Page, () => requests++).DownloadAsync(Request(files.Directory) with
        { OutputTemplate = "%(title)s [%(id)s].%(ext)s", NoOverwrites = true, AddMetadata = true }, null, null, default, _ => completed++);
        Assert(result.Succeeded && result.Skipped && result.OutputPath == path && requests == 0 && completed == 0 && File.ReadAllText(path) == "previous", "No-overwrite downloaded or processed an existing output.");
    }
    private static async Task Concurrent()
    {
        using var files = new Files();
        var requests = 0;
        var engine = Engine(Page, () => Interlocked.Increment(ref requests));
        var request = Request(files.Directory) with { OutputTemplate = "%(title)s.%(ext)s", NoOverwrites = true };
        var results = await Task.WhenAll(engine.DownloadAsync(request, null, null, default), engine.DownloadAsync(request, null, null, default));
        Assert(results.Count(result => result.Skipped) == 1 && requests == 1 && results.All(result => result.Succeeded), "Concurrent no-overwrite replaced or downloaded a completed result twice.");
    }
    private static async Task Invalid()
    {
        using var files = new Files();
        var requests = 0;
        foreach (var template in new[] { "%(title)q", "%title", "%(title", "%()s", "%(title)99999s", "%(upload_date>%Y)s", ".." })
        {
            try { await Engine(Page, () => requests++).DownloadAsync(Request(files.Directory) with { OutputTemplate = template }, null, null, default); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { continue; }
            throw new InvalidOperationException("Unsupported template did not fail: " + template);
        }
        Assert(requests == 0, "Unsupported output syntax reached media transfer.");
    }
    private static async Task ReentrantCompletion()
    {
        using var files = new Files();
        var engine = Engine(Page);
        var request = Request(files.Directory) with { OutputTemplate = "%(title)s.%(ext)s" };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var result = await engine.DownloadAsync(request, null, null, cancellation.Token, _ =>
        {
            var skipped = engine.DownloadAsync(request with { NoOverwrites = true }, null, null, cancellation.Token).GetAwaiter().GetResult();
            Assert(skipped.Skipped, "The follow-up did not observe the completed file.");
        });
        Assert(result.Succeeded, "Reentrant completion did not finish.");
    }
    internal static NativeEngine AudioEngine(byte[] bytes) => Engine(Page with { Media = [new() { Id = "audio", Url = Url, Extension = "m4a", HasAudio = true, HasVideo = false }] }, content: bytes);
    private static NativeEngine Engine(PageMetadata page, Action? onRequest = null, byte[]? content = null) => new([new Provider(page)], _ => new MediaHttp(new Handler(_ =>
    { onRequest?.Invoke(); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content ?? "media"u8.ToArray()) }; })));
    private sealed class Provider(PageMetadata page) : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("fixture-template", "Template fixture", 200);
        public bool CanHandle(Uri url) => url.Host == "media.example";
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(page);
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Files : IDisposable
    {
        public string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        public Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
