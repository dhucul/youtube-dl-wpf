using System.Net;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class MetadataOnlyChecks
{
    private static readonly Uri Url = new("https://fixture.example/watch");
    private static PageMetadata Page(string id = "item") => new() { Id = id, Title = "Fixture title", Extractor = "fixture", Description = "Fixture description", ThumbnailUrl = "https://assets.example/image",
        Media = [new() { Id = "video", Url = new("https://assets.example/media.mp4"), Kind = MediaKind.Progressive, Extension = "mp4", HasVideo = true, HasAudio = true }],
        Subtitles = [new() { Url = new("https://assets.example/en.vtt"), Language = "en", Extension = "vtt" }, new() { Url = new("https://assets.example/fr.vtt"), Language = "fr", Extension = "vtt", Automatic = true }] };
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Metadata-only jobs save standalone outputs without media, ffmpeg or media callbacks", Save),
        ("Simulation suppresses all requested files and non-forced archive writes", Simulation),
        ("Metadata-only unavailable-format handling requires an explicit override", NoFormats),
        ("Explicit force-archive records successful simulated metadata once", ForceArchive),
        ("Metadata-only no-overwrite preserves each existing standalone file", NoOverwrite),
        ("Metadata-only output cannot overwrite the download archive", ProtectArchive),
        ("Metadata-only publication rolls back when an existing caption is locked", PublicationFailure),
        ("Metadata-only forced-archive failures retain and report saved standalone files", ArchiveFailure),
        ("Failed metadata-only captions leave prior outputs and archives untouched", () => Failure(false)),
        ("Canceled metadata-only jobs cannot publish a partial output set", () => Failure(true)),
        ("Metadata-only staging does not delete a resumable media workspace", PreserveResume),
        ("Metadata-only playlist failures retain successful standalone files", Playlist),
        ("Metadata-only DASH downloads requested text without any video resource", Dash),
        ("Metadata-only HLS timing boundaries never fetch media to satisfy captions", HlsBoundary),
        ("Native extra arguments preserve ordering and reject unsupported requests", Arguments)
    ];
    private static async Task Save()
    {
        using var files = new Files(); var calls = new List<string>(); var callbacks = new List<string>();
        var archive = Path.Combine(files.Directory, "archive-folder", "history.txt");
        var result = await Engine(Page(), calls).DownloadAsync(Request(files.Directory) with { DownloadArchiveFile = archive, WriteInfoJson = true, WriteDescription = true, WriteThumbnail = true,
            WriteSubs = true, WriteAutoSubs = true, SubLanguages = "all", AddMetadata = true, EmbedSubs = true, EmbedThumbnail = true }, null, null, default, callbacks.Add);
        Assert(result.Succeeded && result.OutputPath is null && result.OutputPaths.Count == 0 && result.SidecarPaths.Count == 5 && result.NewSidecarPaths.Count == 5 && callbacks.Count == 0,
            "Standalone files were missing or were reported as downloaded media.");
        Assert(calls.Order().SequenceEqual(new[] { "/en.vtt", "/fr.vtt", "/image" }) && !Directory.Exists(Path.GetDirectoryName(archive)), "Skip-download fetched media or created a non-forced archive lock.");
        using var info = JsonDocument.Parse(File.ReadAllText(result.SidecarPaths.Single(path => path.EndsWith(".info.json"))));
        Assert(info.RootElement.GetProperty("id").GetString() == "item" && result.SidecarPaths.All(File.Exists), "Standalone publication or metadata identity changed.");
    }
    private static async Task Simulation()
    {
        using var files = new Files(); var calls = new List<string>();
        var result = await Engine(Page(), calls).DownloadAsync(Request(Path.Combine(files.Directory, "output")) with { Simulate = true, WriteInfoJson = true, WriteThumbnail = true, WriteSubs = true,
            DownloadArchiveFile = Path.Combine(files.Directory, "history", "archive.txt") }, null, null, default);
        Assert(result.Succeeded && result.Simulated && result.SidecarPaths.Count == 0 && calls.Count == 0 && !Directory.EnumerateFileSystemEntries(files.Directory).Any(), "Simulation wrote files, an archive lock or fetched sidecars.");
    }
    private static async Task NoFormats()
    {
        using var files = new Files(); var page = Page() with { Media = [], DrmRefused = true, RefusalReason = "Protected fixture" }; var calls = new List<string>();
        var engine = Engine(page, calls); var request = Request(files.Directory) with { WriteSubs = true, SubLanguages = "en" };
        Assert(!(await engine.DownloadAsync(request, null, null, default)).Succeeded && calls.Count == 0, "Missing formats were ignored without authorization.");
        var result = await engine.DownloadAsync(request with { IgnoreNoFormatsError = true }, null, null, default);
        Assert(result.Succeeded && result.SidecarPaths.Count == 1 && calls.SequenceEqual(new[] { "/en.vtt" }) && File.ReadAllText(result.SidecarPaths[0]) == CaptionChecks.Caption, "The explicit metadata-only override failed to save an exposed caption.");
        var withoutSkip = await engine.DownloadAsync(request with { SkipDownload = false, IgnoreNoFormatsError = true, WriteInfoJson = true }, null, null, default);
        Assert(withoutSkip.Succeeded && withoutSkip.SidecarPaths.Count == 2 && withoutSkip.OutputPaths.Count == 0, "Ignoring a no-formats error still required a media download.");
    }
    private static async Task ForceArchive()
    {
        using var files = new Files(); var archive = Path.Combine(files.Directory, "history", "archive.txt"); var calls = new List<string>();
        var engine = Engine(Page(), calls); var request = Request(Path.Combine(files.Directory, "output")) with { Simulate = true, ForceWriteArchive = true, DownloadArchiveFile = archive, WriteInfoJson = true };
        Assert((await engine.DownloadAsync(request, null, null, default)).Succeeded, "Forced simulation archive failed.");
        Assert((await engine.DownloadAsync(request, null, null, default)).Skipped && File.ReadAllText(archive) == "fixture item\n" && !Directory.Exists(request.OutputDirectory) && calls.Count == 0,
            "Forced metadata archive duplicated records or saved other files.");
    }
    private static async Task NoOverwrite()
    {
        using var files = new Files(); var existing = Path.Combine(files.Directory, "standalone.info.json"); File.WriteAllText(existing, "previous info");
        var result = await Engine(Page(), []).DownloadAsync(Request(files.Directory) with { WriteInfoJson = true, WriteDescription = true, NoOverwrites = true }, null, null, default);
        Assert(result.Succeeded && result.SidecarPaths.Count == 2 && result.NewSidecarPaths.Count == 1 && File.ReadAllText(existing) == "previous info", "No-overwrite did not apply per standalone file.");
    }
    private static async Task ProtectArchive()
    {
        using var files = new Files(); var archive = Path.Combine(files.Directory, "standalone.info.json"); File.WriteAllText(archive, "other completed\n");
        await Expect<IOException>(() => Engine(Page(), []).DownloadAsync(Request(files.Directory) with { WriteInfoJson = true, DownloadArchiveFile = archive }, null, null, default));
        Assert(File.ReadAllText(archive) == "other completed\n", "Metadata publication overwrote its protected archive path.");
    }
    private static async Task PublicationFailure()
    {
        using var files = new Files(); var info = Path.Combine(files.Directory, "standalone.info.json"); var caption = Path.Combine(files.Directory, "standalone.en.vtt");
        File.WriteAllText(info, "previous info"); File.WriteAllText(caption, "previous caption");
        using (var held = new FileStream(caption, FileMode.Open, FileAccess.Read, FileShare.None))
            await Expect<IOException>(() => Engine(Page(), []).DownloadAsync(Request(files.Directory) with { WriteInfoJson = true, WriteSubs = true, SubLanguages = "en" }, null, null, default));
        Assert(File.ReadAllText(info) == "previous info" && File.ReadAllText(caption) == "previous caption", "Failed standalone publication did not restore prior files.");
    }
    private static async Task ArchiveFailure()
    {
        using var files = new Files(); var archive = Path.Combine(files.Directory, "archive.txt"); File.WriteAllText(archive, "other completed\n");
        FileStream? held = null;
        try
        {
            var result = await Engine(Page(), [], path => { if (path == "/en.vtt") held = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.None); }).DownloadAsync(
                Request(files.Directory) with { WriteInfoJson = true, WriteSubs = true, SubLanguages = "en", DownloadArchiveFile = archive, ForceWriteArchive = true }, null, null, default);
            Assert(!result.Succeeded && result.SidecarPaths.Count == 2 && result.NewSidecarPaths.Count == 2 && result.SidecarPaths.All(File.Exists) && result.Error!.Contains("archive"),
                "A forced archive failure erased successful standalone outputs or their result paths.");
        }
        finally { held?.Dispose(); }
        Assert(File.ReadAllText(archive) == "other completed\n", "A failed archive commit changed prior records.");
    }
    private static async Task Failure(bool cancel)
    {
        using var files = new Files(); using var stop = new CancellationTokenSource();
        var existing = Path.Combine(files.Directory, "standalone.info.json"); File.WriteAllText(existing, "previous info");
        var archive = Path.Combine(files.Directory, "history.txt"); File.WriteAllText(archive, "other completed\n");
        var engine = Engine(Page(), [], path => { if (path == "/en.vtt" && cancel) stop.Cancel(); }, badCaptions: !cancel);
        var action = () => engine.DownloadAsync(Request(files.Directory) with { WriteInfoJson = true, WriteSubs = true, SubLanguages = "en", DownloadArchiveFile = archive }, null, null, stop.Token);
        if (cancel) await Expect<OperationCanceledException>(action); else await Expect<InvalidDataException>(action);
        Assert(File.ReadAllText(existing) == "previous info" && File.ReadAllText(archive) == "other completed\n" && !File.Exists(Path.Combine(files.Directory, "standalone.en.vtt")), "A failed standalone job replaced prior files or recorded a media download.");
    }
    private static async Task PreserveResume()
    {
        using var files = new Files(); string partial;
        using (var workspace = new DownloadWorkspace(Path.Combine(files.Directory, "standalone.mp4"), Url))
        { partial = workspace.RawPath("source.mp4.part"); File.WriteAllText(partial, "resumable bytes"); }
        await Engine(Page(), []).DownloadAsync(Request(files.Directory) with { WriteInfoJson = true }, null, null, default);
        Assert(File.ReadAllText(partial) == "resumable bytes", "Standalone completion deleted an existing media resume workspace.");
    }
    private static async Task Playlist()
    {
        using var files = new Files(); var first = Page("one"); var bad = Page("two") with { Subtitles = [new() { Url = new("https://assets.example/bad.vtt"), Language = "en", Extension = "vtt" }] };
        var playlist = new PageMetadata { Id = "list", Title = "Collection", Extractor = "fixture", IsPlaylist = true, Entries = [
            new(1, "one", "First", Url, null, null, true, first), new(2, "two", "Second", Url, null, null, true, bad), new(3, "three", "Third", Url, null, null, true, Page("three"))] };
        var callbacks = new List<string>();
        var result = await Engine(playlist, []).DownloadAsync(Request(files.Directory) with { WriteInfoJson = true, WriteSubs = true, SubLanguages = "en" }, null, null, default, callbacks.Add);
        Assert(!result.Succeeded && result.Failures.Count == 1 && result.SidecarPaths.Count == 4 && result.SidecarPaths.All(File.Exists) && callbacks.Count == 0 && result.OutputPaths.Count == 0,
            "A standalone playlist lost completed artifacts or reported fictitious media.");
    }
    private static async Task Dash()
    {
        using var files = new Files(); var bytes = DashCaptionChecks.Movie([DashCaptionChecks.Sample("Standalone cue")], out var index);
        var manifest = "<MPD mediaPresentationDuration='PT1S'><Period><AdaptationSet mimeType='video/mp4'><Representation id='video'><BaseURL>video.mp4</BaseURL></Representation></AdaptationSet>" +
            "<AdaptationSet contentType='text' lang='en'><Representation id='text' mimeType='application/mp4' codecs='wvtt'><BaseURL>captions.mp4</BaseURL><SegmentBase indexRange='" + index + "-" + (index + 43) + "'><Initialization range='0-" + (index - 1) + "'/></SegmentBase></Representation></AdaptationSet></Period></MPD>";
        var page = Page() with { Subtitles = [], Media = [new() { Url = new("https://assets.example/source.mpd"), Kind = MediaKind.Dash, Extension = "mpd" }] }; var calls = new List<string>();
        var engine = new NativeEngine([new Provider(page)], _ => new MediaHttp(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath; calls.Add(path);
            return path switch { "/source.mpd" => Text(manifest), "/captions.mp4" => DashIndexChecks.Range(request, bytes), _ => throw new InvalidOperationException("A metadata-only DASH job requested video.") };
        })));
        var result = await engine.DownloadAsync(Request(files.Directory) with { WriteSubs = true, SubLanguages = "en", ConcurrentFragments = 3 }, null, null, default);
        Assert(result.Succeeded && result.SidecarPaths.Count == 1 && File.ReadAllText(result.SidecarPaths[0]).Contains("Standalone cue") && calls.All(path => path != "/video.mp4"), "Standalone DASH captions required or fetched video.");
    }
    private static async Task HlsBoundary()
    {
        using var files = new Files(); var calls = 0;
        var page = Page() with { Subtitles = [], Media = [new() { Url = new("https://assets.example/master.m3u8"), Kind = MediaKind.Hls, Extension = "m3u8" }] };
        var engine = new NativeEngine([new Provider(page)], _ => new MediaHttp(new Handler(_ => { calls++; return Text("#EXTM3U\n#EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID=\"sub\",NAME=\"English\",LANGUAGE=\"en\",URI=\"en.m3u8\"\n#EXT-X-STREAM-INF:BANDWIDTH=100000,SUBTITLES=\"sub\"\nvideo.m3u8"); })));
        await Expect<NotSupportedException>(() => engine.DownloadAsync(Request(files.Directory) with { WriteSubs = true, SubLanguages = "en" }, null, null, default));
        Assert(calls == 1 && !Directory.EnumerateFiles(files.Directory).Any(), "Unsupported standalone HLS timing fetched media or published files.");
        Assert((await engine.DownloadAsync(Request(files.Directory) with { WriteAutoSubs = true }, null, null, default)).Succeeded, "An unrequested authored HLS track blocked automatic-only metadata.");
    }
    private static Task Arguments()
    {
        var request = NativeExtraArguments.Apply(Request(Path.GetTempPath()), ["--simulate", "--write-subs", "--sub-langs=en,fr", "--no-simulate", "--no-write-subs", "--write-auto-subs", "-N", "3", "--fragment-retries", "0", "--ignore-no-formats-error", "--force-download-archive"]);
        Assert(!request.Simulate && request.SkipDownload && !request.WriteSubs && request.WriteAutoSubs && request.SubLanguages == "en,fr" && request.ConcurrentFragments == 3 && request.FragmentRetries == 0 && request.IgnoreNoFormatsError && request.ForceWriteArchive,
            "Extra argument precedence, aliases or values changed.");
        Throws<NotSupportedException>(() => NativeExtraArguments.Apply(request, ["--exec", "before_dl:unwanted command"]));
        Throws<ArgumentException>(() => NativeExtraArguments.Apply(request, ["--sub-langs"]));
        Throws<ArgumentException>(() => NativeExtraArguments.Apply(request, ["--simulate=true"]));
        Throws<NotSupportedException>(() => NativeExtraArguments.Apply(request, ["--paths", "subtitle:elsewhere"]));
        return Task.CompletedTask;
    }
    private static NativeEngine Engine(PageMetadata page, List<string> calls, Action<string>? before = null, bool badCaptions = false) => new([new Provider(page)], _ => new MediaHttp(new Handler(request =>
    {
        var path = request.RequestUri!.AbsolutePath; calls.Add(path); before?.Invoke(path);
        if (path == "/image") return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=")) };
        if (path.EndsWith(".vtt")) return Text(badCaptions || path == "/bad.vtt" ? "<html>unavailable</html>" : CaptionChecks.Caption);
        throw new InvalidOperationException("A metadata-only job attempted media transfer.");
    })));
    private static NativeDownloadRequest Request(string directory) => new() { Url = Url, OutputDirectory = directory, FileName = "standalone", SkipDownload = true, Retries = 0 };
    private static HttpResponseMessage Text(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Provider(PageMetadata page) : IMediaProvider
    { public MediaProviderInfo Info { get; } = new("fixture", "Fixture", 1000); public bool CanHandle(Uri url) => true; public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(page); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, "metadata-" + Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() { var path = Path.GetFullPath(Directory); if (!path.StartsWith(Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Test folder escaped its root."); System.IO.Directory.Delete(path, true); }
    }
}
