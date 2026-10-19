using System.Diagnostics;
using System.Net;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class DownloadArchiveChecks
{
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Existing archive records skip known videos before metadata or media requests", Known),
        ("Successful downloads atomically append archive records while preserving existing bytes", Record),
        ("Archive matching uses complete case-sensitive identities and accepts UTF-8 BOMs", Identity),
        ("Failed and canceled downloads never acquire successful archive records", Failure),
        ("Playlist archives record completed items and retain partial failures", Playlist),
        ("Concurrent jobs sharing an archive download a media identity once", Concurrent),
        ("Archive completion callbacks can reenter without retaining locks", Reentrant),
        ("Archive write failures retain published paths and completion notifications", RecordFailure),
        ("Output and sidecar collisions cannot overwrite the download archive", Collisions),
        ("Inspection never reads or creates a download archive", Inspection),
        ("No-overwrite successes can seed an archive without media transfer", NoOverwrite),
        ("Archive lock waits support cancellation and release the waiting job", Cancellation),
        ("Archive locks coordinate a separate native process", CrossProcess),
        ("Invalid archive encodings and identifiers fail without altering data", Invalid),
        ("Read-only archive matches skip safely and new entries fail before transfer", ReadOnly),
        ("Archive commits preserve entries added externally during media transfer", ExternalEdit),
        ("Archives inside a retained workspace survive a rejected retry", WorkspaceCollision),
        ("Direct media archive identities distinguish URLs on the same host", Direct)
    ];

    private static NativeDownloadRequest Request(Files files, string id = "item") => new()
    { Url = new Uri("https://fixture.example/" + id), OutputDirectory = files.Directory, FileName = id, DownloadArchiveFile = files.Archive, Retries = 0 };
    private static PageMetadata Page(string id) => new() { Id = id, Extractor = "fixture", Title = id, Media = [new() { Url = new Uri("https://cdn.example/" + Uri.EscapeDataString(id) + ".mp4"), Id = "file", Extension = "mp4", Kind = MediaKind.Progressive, HasAudio = true, HasVideo = true }] };
    private static NativeEngine Engine(Func<HttpRequestMessage, HttpResponseMessage> response, Func<NativeDownloadRequest, PageMetadata>? pages = null) =>
        new([new Provider(pages ?? (request => Page(request.Url.AbsolutePath.Trim('/'))))], _ => new MediaHttp(new Handler(response)));

    private static async Task Known()
    {
        using var files = new Files();
        await File.WriteAllTextAsync(files.Archive, "youtube abcdefghijk\nvimeo 123\nkaltura 1_fixture\n", new UTF8Encoding(false));
        var engine = new NativeEngine(httpFactory: _ => throw new InvalidOperationException("Archived metadata contacted the site."));
        foreach (var url in new[] { "https://youtu.be/abcdefghijk", "https://vimeo.com/123", "https://cdnapisec.kaltura.com/p/309/embedPlaykitJs?entry_id=1_fixture" })
        {
            var callbacks = 0;
            var result = await engine.DownloadAsync(Request(files) with { Url = new Uri(url) }, null, null, default, _ => callbacks++);
            Assert(result.Succeeded && result.Skipped && result.OutputPaths.Count == 0 && callbacks == 0, "Archived media was published or notified again.");
        }
        Assert(!File.Exists(files.Archive + ".native.lock"), "An already-recorded check created a lock artifact.");
    }

    private static async Task Record()
    {
        using var files = new Files();
        var before = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("# original comment\r\nother untouched\r\nfixture old")).ToArray();
        await File.WriteAllBytesAsync(files.Archive, before);
        var callbacks = 0;
        var result = await Engine(_ => Ok("complete media")).DownloadAsync(Request(files), null, null, default, path =>
        {
            Assert(File.ReadAllText(path) == "complete media" && File.ReadAllText(files.Archive).EndsWith("fixture item\n", StringComparison.Ordinal), "Completion preceded media or archive publication.");
            callbacks++;
        });
        var after = File.ReadAllBytes(files.Archive);
        Assert(result.Succeeded && callbacks == 1 && after.AsSpan(0, before.Length).SequenceEqual(before) &&
            Encoding.UTF8.GetString(after.AsSpan(before.Length)) == "\nfixture item\n", "Existing archive bytes were changed.");
        Assert(!Directory.EnumerateFiles(files.Directory, ".native-archive-*.tmp").Any(), "Temporary archive files were retained after success.");
    }

    private static async Task Identity()
    {
        using var files = new Files();
        await File.WriteAllTextAsync(files.Archive, "\ufefffixture Item\nFixture item\nfixture item-more\n", new UTF8Encoding(false));
        var transfers = 0;
        var engine = Engine(_ => { transfers++; return Ok("media"); });
        var first = await engine.DownloadAsync(Request(files), null, null, default);
        var second = await engine.DownloadAsync(Request(files), null, null, default);
        Assert(first.Succeeded && !first.Skipped && second.Skipped && transfers == 1, "Archive records were matched as substrings or without case distinctions.");
    }

    private static async Task Failure()
    {
        using var files = new Files();
        await Expect<IOException>(() => Engine(_ => new(HttpStatusCode.Forbidden)).DownloadAsync(Request(files), null, null, default));
        Assert(!File.Exists(files.Archive), "A failed transfer was archived.");
        using var cancel = new CancellationTokenSource();
        await Expect<OperationCanceledException>(() => Engine(_ => { cancel.Cancel(); return Ok("media"); }).DownloadAsync(Request(files), null, null, cancel.Token));
        Assert(!File.Exists(files.Archive), "A canceled transfer was archived.");
        var page = Page("item") with { Subtitles = [new() { Url = new Uri("https://cdn.example/sub.vtt"), Language = "en", Extension = "vtt" }] };
        await Expect<HttpRequestException>(() => Engine(request => request.RequestUri!.AbsolutePath.EndsWith(".vtt") ? new(HttpStatusCode.Forbidden) : Ok("media"), _ => page)
            .DownloadAsync(Request(files) with { WriteSubs = true }, null, null, default));
        Assert(!File.Exists(files.Archive), "Failed post-processing was archived.");
    }

    private static async Task Playlist()
    {
        using var files = new Files();
        var playlist = new PageMetadata { Id = "list", IsPlaylist = true, Title = "List", Extractor = "fixture", Entries = Enumerable.Range(1, 3)
            .Select(index => new NativePlaylistEntry(index, "item" + index, "Item " + index, new Uri("https://fixture.example/item" + index), null, null, true, Page("item" + index))).ToArray() };
        var transfers = new List<string>();
        var engine = Engine(request => { var path = request.RequestUri!.AbsolutePath; transfers.Add(path); return path.Contains("item2") ? new(HttpStatusCode.Forbidden) : Ok("media"); }, _ => playlist);
        var result = await engine.DownloadAsync(Request(files, "list"), null, null, default);
        Assert(!result.Succeeded && result.OutputPaths.Count == 2 && File.ReadAllLines(files.Archive).SequenceEqual(new[] { "fixture item1", "fixture item3" }), "Playlist failures or the playlist identity were archived.");
        transfers.Clear();
        var retry = await engine.DownloadAsync(Request(files, "list"), null, null, default);
        Assert(!retry.Succeeded && transfers.SequenceEqual(new[] { "/item2.mp4" }) && retry.OutputPaths.Count == 0, "A playlist retry redownloaded successful items.");
    }

    private static async Task Concurrent()
    {
        using var files = new Files();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transfers = 0;
        var engine = new NativeEngine([new Provider(_ => Page("same"))], _ => new MediaHttp(new AsyncHandler(async _ =>
        { Interlocked.Increment(ref transfers); started.TrySetResult(); await release.Task; return Ok("media"); })));
        var first = engine.DownloadAsync(Request(files, "first"), null, null, default);
        await started.Task;
        var second = engine.DownloadAsync(Request(files, "second"), null, null, default);
        release.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert(transfers == 1 && results.Count(result => result.Skipped) == 1 && File.ReadAllLines(files.Archive).Single() == "fixture same", "Concurrent archive jobs duplicated media or records.");
    }

    private static async Task Reentrant()
    {
        using var files = new Files(); var engine = Engine(_ => Ok("media"));
        await engine.DownloadAsync(Request(files), null, null, default, _ =>
            Assert(engine.DownloadAsync(Request(files), null, null, default).GetAwaiter().GetResult().Skipped, "Reentrant completion did not observe committed history."));
    }

    private static async Task RecordFailure()
    {
        foreach (var corrupt in new[] { false, true })
        {
            using var files = new Files(); var callbacks = 0;
            var result = await Engine(_ => { if (corrupt) File.WriteAllBytes(files.Archive, [0xff]); else Directory.CreateDirectory(files.Archive); return Ok("completed"); })
                .DownloadAsync(Request(files), null, null, default, _ => callbacks++);
            Assert(!result.Succeeded && result.OutputPaths.Count == 1 && File.ReadAllText(result.OutputPath!) == "completed" && callbacks == 1 && result.Error!.Contains("archive"), "Archive failure lost a published file or its notification.");
        }
    }

    private static async Task Collisions()
    {
        using var files = new Files();
        var ledger = "fixture previous\n";
        var media = Path.Combine(files.Directory, "item.mp4");
        await File.WriteAllTextAsync(media, ledger);
        await Expect<IOException>(() => Engine(_ => throw new InvalidOperationException("A collision reached transfer.")).DownloadAsync(Request(files) with { DownloadArchiveFile = media }, null, null, default));
        Assert(File.ReadAllText(media) == ledger, "Media publication overwrote the archive.");
        var sidecar = Path.Combine(files.Directory, "item.info.json");
        await File.WriteAllTextAsync(sidecar, ledger);
        await Expect<IOException>(() => Engine(_ => Ok("new media")).DownloadAsync(Request(files) with { DownloadArchiveFile = sidecar, WriteInfoJson = true }, null, null, default));
        Assert(File.ReadAllText(sidecar) == ledger && File.ReadAllText(media) == ledger, "Sidecar publication overwrote archive or previous media.");
    }

    private static async Task Inspection()
    {
        using var files = new Files();
        var result = await Engine(_ => throw new InvalidOperationException("Unexpected transfer.")).InspectAsync(Request(files) with { DownloadArchiveFile = files.Directory }, default);
        Assert(result.Id == "item" && Directory.GetFiles(files.Directory).Length == 0, "Inspection read or created an archive.");
    }

    private static async Task NoOverwrite()
    {
        using var files = new Files();
        var path = Path.Combine(files.Directory, "item.mp4"); await File.WriteAllTextAsync(path, "existing media");
        var result = await Engine(_ => throw new InvalidOperationException("Existing output was transferred.")).DownloadAsync(Request(files) with { NoOverwrites = true }, null, null, default);
        Assert(result.Succeeded && result.Skipped && File.ReadAllText(files.Archive) == "fixture item\n" && File.ReadAllText(path) == "existing media", "No-overwrite success did not seed the archive.");
    }

    private static async Task Cancellation()
    {
        using var files = new Files();
        using var held = new FileStream(files.Archive + ".native.lock", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var cancel = new CancellationTokenSource();
        var waiting = Engine(_ => throw new InvalidOperationException("A locked archive reached transfer.")).DownloadAsync(Request(files), null, null, cancel.Token);
        Assert(!waiting.IsCompleted, "The archive lock was ignored."); cancel.Cancel();
        await Expect<OperationCanceledException>(() => waiting);
        Assert(!File.Exists(files.Archive), "A canceled lock wait created history.");
    }

    private static async Task CrossProcess()
    {
        using var files = new Files();
        var ready = Path.Combine(files.Directory, "ready");
        using var held = new FileStream(files.Archive + ".native.lock", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(DownloadArchiveChecks).Assembly.Location);
        foreach (var argument in new[] { "--archive-worker", files.Directory, ready }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (!File.Exists(ready)) { if (process.HasExited) throw new InvalidOperationException(await stderr); await Task.Delay(20, timeout.Token); }
            Assert(!File.Exists(Path.Combine(files.Directory, "item.mp4")), "Another process bypassed the archive lock.");
            held.Dispose();
            await process.WaitForExitAsync(timeout.Token);
            Assert(process.ExitCode == 0 && File.ReadAllLines(files.Archive).Single() == "fixture item", "The archive worker failed: " + await stderr);
            await stdout;
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
    }
    public static async Task<int> Worker(string directory, string ready)
    {
        using var files = new Files(directory, owns: false);
        var engine = Engine(_ => Ok("worker media"), _ => { File.WriteAllText(ready, "ready"); return Page("item"); });
        var result = await engine.DownloadAsync(Request(files), null, null, default);
        return result.Succeeded ? 0 : 1;
    }

    private static async Task Invalid()
    {
        using var files = new Files();
        await File.WriteAllBytesAsync(files.Archive, new byte[] { 0xff, 0xfe, 1 });
        var engine = Engine(_ => throw new InvalidOperationException("Invalid archive reached transfer."));
        await Expect<InvalidDataException>(() => engine.DownloadAsync(Request(files), null, null, default));
        Assert(File.ReadAllBytes(files.Archive).AsSpan().SequenceEqual(new byte[] { 0xff, 0xfe, 1 }), "Invalid archive bytes were changed.");
        File.Delete(files.Archive);
        await Expect<InvalidDataException>(() => Engine(_ => Ok("media"), _ => Page("forged\nfixture another")).DownloadAsync(Request(files), null, null, default));
        Assert(!File.Exists(files.Archive), "An identifier injected an archive record.");
        if (OperatingSystem.IsWindows()) foreach (var name in new[] { "NUL", "history.txt:stream" })
            await Expect<ArgumentException>(() => engine.DownloadAsync(Request(files) with { DownloadArchiveFile = Path.Combine(files.Directory, name) }, null, null, default));
    }

    private static async Task ReadOnly()
    {
        using var files = new Files();
        await File.WriteAllTextAsync(files.Archive, "fixture item\n");
        File.SetAttributes(files.Archive, File.GetAttributes(files.Archive) | FileAttributes.ReadOnly);
        try
        {
            var engine = Engine(_ => throw new InvalidOperationException("Read-only archive reached transfer."));
            Assert((await engine.DownloadAsync(Request(files), null, null, default)).Skipped, "An existing read-only record did not skip.");
            await Expect<UnauthorizedAccessException>(() => engine.DownloadAsync(Request(files, "other"), null, null, default));
            Assert(File.ReadAllText(files.Archive) == "fixture item\n", "Read-only history was altered.");
        }
        finally { File.SetAttributes(files.Archive, FileAttributes.Normal); }
    }

    private static async Task ExternalEdit()
    {
        using var files = new Files();
        await File.WriteAllTextAsync(files.Archive, "fixture old\n");
        var result = await Engine(_ => { File.AppendAllText(files.Archive, "external added\n"); return Ok("media"); }).DownloadAsync(Request(files), null, null, default);
        Assert(result.Succeeded && File.ReadAllLines(files.Archive).SequenceEqual(new[] { "fixture old", "external added", "fixture item" }), "External history was overwritten by a stale snapshot.");
    }

    private static async Task WorkspaceCollision()
    {
        using var files = new Files();
        var partial = Engine(_ => { var response = Ok("partial"); response.Content.Headers.ContentLength = 100; return response; });
        await Expect<IOException>(() => partial.DownloadAsync(Request(files) with { DownloadArchiveFile = "" }, null, null, default));
        var workspace = Directory.GetDirectories(files.Directory, ".native-download-*").Single();
        var archive = Path.Combine(workspace, "processing", "history.txt");
        await File.WriteAllTextAsync(archive, "fixture previous\n");
        await Expect<IOException>(() => Engine(_ => throw new InvalidOperationException("Workspace collision reached transfer.")).DownloadAsync(Request(files) with { DownloadArchiveFile = archive }, null, null, default));
        Assert(File.ReadAllText(archive) == "fixture previous\n", "Workspace cleanup removed the archive.");
    }

    private static async Task Direct()
    {
        using var files = new Files(); var transfers = 0;
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(_ => { transfers++; return Ok("media"); })));
        var request = Request(files) with { Url = new Uri("https://cdn.example/first.mp4") };
        await engine.DownloadAsync(request, null, null, default);
        await engine.DownloadAsync(request with { Url = new Uri("https://cdn.example/second.mp4"), FileName = "second" }, null, null, default);
        var retry = await engine.DownloadAsync(request, null, null, default);
        Assert(transfers == 2 && retry.Skipped && File.ReadAllLines(files.Archive).Distinct().Count() == 2, "Direct URLs collided through a shared host identity.");
    }

    private sealed class Provider(Func<NativeDownloadRequest, PageMetadata> pages) : IMediaProvider
    { public MediaProviderInfo Info { get; } = new("fixture", "Fixture", 1); public bool CanHandle(Uri url) => true; public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(pages(request)); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(response(request)); } }
    private sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => response(request); }
    private sealed class Files : IDisposable
    {
        public string Directory { get; }
        public string Archive => Path.Combine(Directory, "download-archive.txt");
        private readonly bool _owns;
        public Files(string? directory = null, bool owns = true) { Directory = directory ?? Path.Combine(AppContext.BaseDirectory, "test-data", "archive-" + Guid.NewGuid().ToString("N")); _owns = owns; System.IO.Directory.CreateDirectory(Directory); }
        public void Dispose() { if (_owns) System.IO.Directory.Delete(Directory, recursive: true); }
    }
    private static HttpResponseMessage Ok(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
