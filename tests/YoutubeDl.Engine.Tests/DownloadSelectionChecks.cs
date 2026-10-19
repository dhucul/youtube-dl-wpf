using System.Globalization;
using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class DownloadSelectionChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Native selection title patterns use resolved titles case-insensitively before filename changes", Titles),
        ("Native selection rejection prevents media sidecars archive changes and callbacks", Rejected),
        ("Native selection limits ignore filtered archived and failed entries and stop before later discovery", Limits),
        ("Native selection existing outputs do not consume the new-download limit", Existing),
        ("Native selection simulation limits accepted items without counting archived records", Simulation),
        ("Native selection metadata-only jobs count completed item sidecars once", Metadata),
        ("Native selection snapshots preserve source indexes and selected-order autonumbers", Snapshots),
        ("Native selection each top-level job receives an independent download limit", Independent),
        ("Native selection invalid expressions and limits fail before any discovery", Invalid),
        ("Native selection regex timeout stops an ignore-errors playlist without repeated failures", Timeout),
        ("Native selection arguments preserve title limit and abort precedence", Arguments)
    ];
    private static NativeDownloadRequest Request(string directory) => new() { Url = new("https://selection.example/list"), OutputDirectory = directory, OutputTemplate = "%(id)s.%(ext)s", Retries = 0 };
    private static async Task Titles()
    {
        using var files = new Files(); var fixture = new Fixture(["Public 21", "public 22 BANNED", "private 23"]);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var result = await fixture.Engine.DownloadAsync(Request(files.Path) with { MatchTitle = @"PUBLIC \d+", RejectTitle = "banned", FileName = "unrelated filename" }, null, null, default);
            Assert(result.Succeeded && result.OutputPaths.Count == 1 && fixture.Media.SequenceEqual(["1"]), "Matching used a listing title, filename, current culture, or ignored rejection.");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    private static async Task Rejected()
    {
        using var files = new Files(); var fixture = new Fixture(["Excluded"]); var archive = System.IO.Path.Combine(files.Path, "archive.txt"); var callbacks = 0;
        var result = await fixture.Engine.DownloadAsync(Request(files.Path) with { Url = new("https://selection.example/item/1"), MatchTitle = "keep", ForceWriteArchive = true,
            SkipDownload = true, WriteInfoJson = true, WriteDescription = true, WriteThumbnail = true, DownloadArchiveFile = archive }, null, null, default, _ => callbacks++);
        Assert(result.Succeeded && result.Skipped && result.Filtered && callbacks == 0 && fixture.Media.Count == 0 && !Directory.EnumerateFileSystemEntries(files.Path).Any(), "A title rejection caused output or archive side effects.");
    }
    private static async Task Limits()
    {
        using var files = new Files(); var fixture = new Fixture(["drop", "keep archived", "keep failure", "keep complete", "keep later"], fail: 3);
        var archive = System.IO.Path.Combine(files.Path, "archive.txt"); await File.WriteAllTextAsync(archive, "selection 2\n"); var completed = new List<string>();
        var result = await fixture.Engine.DownloadAsync(Request(files.Path) with { MatchTitle = "keep", MaxDownloads = 1, DownloadArchiveFile = archive }, null, null, default, completed.Add);
        Assert(!result.Succeeded && result.DownloadLimitReached && result.Failures.Count == 1 && result.Failures[0].Position == 3 && completed.Count == 1 && fixture.Media.SequenceEqual(["4"]), "Skips or failures consumed the limit, or completed output was lost.");
        Assert(fixture.Discovery.SequenceEqual(["list", "1", "3", "4"]) && (await File.ReadAllLinesAsync(archive)).SequenceEqual(["selection 2", "selection 4"]), "Archived/later entries were discovered or rejected entries were archived.");
    }
    private static async Task Existing()
    {
        using var files = new Files(); await File.WriteAllTextAsync(System.IO.Path.Combine(files.Path, "1.mp4"), "existing complete file");
        var fixture = new Fixture(["one", "two", "three"]); var completed = new List<string>();
        var result = await fixture.Engine.DownloadAsync(Request(files.Path) with { NoOverwrites = true, MaxDownloads = 1 }, null, null, default, completed.Add);
        Assert(result.Succeeded && result.DownloadLimitReached && fixture.Media.SequenceEqual(["2"]) && completed.Count == 1 && await File.ReadAllTextAsync(System.IO.Path.Combine(files.Path, "1.mp4")) == "existing complete file", "An existing output consumed the limit or was overwritten.");
    }
    private static async Task Simulation()
    {
        using var files = new Files(); var fixture = new Fixture(["drop", "keep archived", "keep simulation", "keep later"]); var logs = new List<string>();
        var archive = System.IO.Path.Combine(files.Path, "archive.txt"); await File.WriteAllTextAsync(archive, "selection 2\n");
        var result = await fixture.Engine.DownloadAsync(Request(files.Path) with { MatchTitle = "keep", MaxDownloads = 1, Simulate = true, DownloadArchiveFile = archive }, null, logs.Add, default);
        Assert(result.Succeeded && result.Simulated && result.DownloadLimitReached && fixture.Media.Count == 0 && logs.Count(line => line.StartsWith("[native] simulated ")) == 1 && fixture.Discovery.SequenceEqual(["list", "1", "3"]), "Simulation counted archived/filtered items or ignored its limit.");
    }
    private static async Task Metadata()
    {
        using var files = new Files(); var fixture = new Fixture(["one", "two"]);
        var result = await fixture.Engine.DownloadAsync(Request(files.Path) with { SkipDownload = true, WriteInfoJson = true, WriteDescription = true, MaxDownloads = 1 }, null, null, default);
        Assert(result.Succeeded && result.DownloadLimitReached && result.SidecarPaths.Count == 2 && fixture.Media.Count == 0 && fixture.Discovery.SequenceEqual(["list", "1"]), "Multiple sidecars consumed multiple items or the metadata limit was ignored.");
    }
    private static async Task Snapshots()
    {
        using var files = new Files(); var fixture = new Fixture(["drop", "keep two", "keep three"], snapshots: true);
        var result = await fixture.Engine.DownloadAsync(Request(files.Path) with { MatchTitle = "keep", MaxDownloads = 1, OutputTemplate = "%(playlist_index)d-%(playlist_autonumber)d.%(ext)s" }, null, null, default);
        Assert(result.Succeeded && result.DownloadLimitReached && System.IO.Path.GetFileName(result.OutputPath) == "2-2.mp4" && fixture.Discovery.SequenceEqual(["list"]) && fixture.Media.SequenceEqual(["2"]), "Snapshot filtering changed source/selected indexes or resolved another item.");
    }
    private static async Task Independent()
    {
        using var files = new Files(); var fixture = new Fixture(["one", "two"]); var request = Request(files.Path) with { MaxDownloads = 1 };
        var first = await fixture.Engine.DownloadAsync(request, null, null, default);
        var second = await fixture.Engine.DownloadAsync(request, null, null, default);
        Assert(first.Succeeded && second.Succeeded && first.DownloadLimitReached && second.DownloadLimitReached && fixture.Media.SequenceEqual(["1", "1"]), "A reused request retained the previous job's counter.");
    }
    private static async Task Invalid()
    {
        using var files = new Files(); var fixture = new Fixture(["one"]);
        foreach (var request in new[] { Request(files.Path) with { MatchTitle = "[" }, Request(files.Path) with { RejectTitle = new string('x', 4097) }, Request(files.Path) with { MaxDownloads = -1 } })
            await Expect<ArgumentException>(() => fixture.Engine.DownloadAsync(request, null, null, default));
        Assert(fixture.Discovery.Count == 0 && fixture.Media.Count == 0, "Invalid selection reached discovery.");
    }
    private static async Task Timeout()
    {
        using var files = new Files(); var fixture = new Fixture([new string('a', 100_000) + "!", "second"]);
        await Expect<NativeSelectionException>(() => fixture.Engine.DownloadAsync(Request(files.Path) with { MatchTitle = "(a|aa)+$", IgnoreErrors = true }, null, null, default));
        Assert(fixture.Discovery.SequenceEqual(["list", "1"]) && fixture.Media.Count == 0, "A timed-out title pattern continued through an ignore-errors playlist.");
    }
    private static Task Arguments()
    {
        var result = NativeExtraArguments.Apply(Request(System.IO.Path.GetTempPath()) with { MatchTitle = "saved", MaxDownloads = 5, IgnoreErrors = false },
            ["--match-title", "job", "--reject-title=exclude", "--max-downloads", "2", "--ignore-errors", "--abort-on-error"]);
        Assert(result.MatchTitle == "job" && result.RejectTitle == "exclude" && result.MaxDownloads == 2 && !result.IgnoreErrors, "Selection or abort precedence changed.");
        return Task.CompletedTask;
    }
    private sealed class Fixture : IMediaProvider
    {
        private readonly string[] _titles; private readonly int _fail; private readonly bool _snapshots;
        internal List<string> Discovery { get; } = []; internal List<string> Media { get; } = [];
        internal NativeEngine Engine { get; }
        internal Fixture(string[] titles, int fail = 0, bool snapshots = false)
        {
            _titles = titles; _fail = fail; _snapshots = snapshots;
            Engine = new([this], _ => new MediaHttp(new Handler(message => { Media.Add(System.IO.Path.GetFileNameWithoutExtension(message.RequestUri!.AbsolutePath)); return new(HttpStatusCode.OK) { Content = new StringContent("selected media bytes") }; })));
        }
        public MediaProviderInfo Info { get; } = new("selection", "Selection fixture", 1);
        public bool CanHandle(Uri url) => true;
        public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => request.Url.AbsolutePath == "/list" ? null : new("selection", request.Url.Segments[^1]);
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
        {
            var id = request.Url.Segments[^1]; Discovery.Add(id);
            if (id == "list") return Task.FromResult(new PageMetadata { Id = "list", Title = "Unrelated playlist title", IsPlaylist = true,
                Entries = Enumerable.Range(1, _titles.Length).Select(index => new NativePlaylistEntry(index, index.ToString(), "Stale listing title", new("https://selection.example/item/" + index), null, null, true, _snapshots ? Page(index) : null)).ToArray() });
            if (int.Parse(id) == _fail) throw new IOException("Fixture metadata failure.");
            return Task.FromResult(Page(int.Parse(id)));
        }
        private PageMetadata Page(int index) => new() { Id = index.ToString(), Extractor = "selection", Title = _titles[index - 1], Description = "Description", ArchiveIdentity = new("selection", index.ToString()),
            Media = [new() { Url = new("https://selection.example/media/" + index + ".mp4"), Kind = MediaKind.Progressive, Extension = "mp4", HasVideo = true, HasAudio = true }] };
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private sealed class Files : IDisposable
    { internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "native-selection-" + Guid.NewGuid()); public Files() => Directory.CreateDirectory(Path); public void Dispose() => Directory.Delete(Path, true); }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
