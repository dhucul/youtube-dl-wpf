using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class PlaylistSyntaxChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Playlist syntax matches documented signed, stepped and open-range behavior", () => Sync(Examples)),
        ("Playlist syntax preserves explicit order and deduplicates source positions", () => Sync(Order)),
        ("Playlist syntax bounds work to the inventory for arbitrarily large endpoints", () => Sync(Large)),
        ("Playlist syntax retains sparse positions and rejects invalid inventories", () => Sync(Sparse)),
        ("Playlist syntax validates malformed input before extraction and honors cancellation", Validation),
        ("Playlist short-option and legacy boundaries retain explicit zero and precedence", () => Sync(Arguments)),
        ("Ordered playlist filenames and info JSON distinguish original indexes from autonumbers", Numbering),
        ("Empty playlist selections skip without media, output folders or archive writes", Empty),
        ("Ordered playlist failures retain their positions and selected-order numbering", Failure),
        ("Playlist numbering includes archived selections without new callbacks for them", Archive),
        ("Ordered playlist cancellation preserves the completed selection and its metadata", Cancellation)
    ];
    private static NativePlaylistEntry[] Entries(int count) => Enumerable.Range(1, count).Select(index => new NativePlaylistEntry(index, index.ToString(), "Item " + index,
        new Uri("https://media.example/" + index + ".mp4"), 1, null, true)).ToArray();
    private static int[] Select(string expression, int count = 10) => PlaylistSelection.Select(Entries(count), Request(Path.GetTempPath()) with { PlaylistItems = expression }).Select(entry => entry.Position).ToArray();
    private static void Examples()
    {
        var cases = new (string Expression, int[] Expected)[] {
            ("1:3,7,-5::2", [1,2,3,7,11,13,15]), ("4,2", [4,2]), ("2-4,3-4,3", [2,3,4]),
            (":", [1,2,3,4,5,6,7,8,9,10]), ("::1", [1,2,3,4,5,6,7,8,9,10]), ("::-1", [10,9,8,7,6,5,4,3,2,1]),
            (":6", [1,2,3,4,5,6]), (":-6", [1,2,3,4,5]), ("-1:6:-2", [10,8,6]), ("9:-6:-2", [9,7,5]),
            ("1:inf:2", [1,3,5,7,9]), ("-2:infinite", [9,10]), (":inf:-1", []), ("0-2:2", [2]), ("1-:2", [1,3,5,7,9]),
            ("0--2:2", [2,4,6,8]), ("10::3", [10]), ("-1::3", [10]), ("11::3", []), ("-15::2", [2,4,6,8,10]), ("-15::15", []),
            ("0", []), ("20", []), ("3-1", []), ("+3:+1:-1", [3,2,1]), ("-3--1", [8,9,10]), ("--2", [1,2,3,4,5,6,7,8,9])
        };
        foreach (var (expression, expected) in cases)
            Assert(Select(expression, expression == "1:3,7,-5::2" ? 15 : 10).SequenceEqual(expected), "Unexpected playlist selection for " + expression);
    }
    private static void Order()
    {
        var entries = Entries(6); var request = Request(Path.GetTempPath()) with { PlaylistItems = "6,2,4:1:-1,-1", PlaylistStart = 5, PlaylistEnd = 5 };
        Assert(PlaylistSelection.Select(entries, request).Select(entry => entry.Position).SequenceEqual([6,2,4,3,1]), "Explicit order or first-occurrence deduplication changed.");
        Assert(PlaylistSelection.Select(entries, request with { PlaylistReverse = true }).Select(entry => entry.Position).SequenceEqual([1,3,4,2,6]), "Reverse did not apply after explicit selection.");
        var random = PlaylistSelection.Select(entries, request with { PlaylistRandom = true }, new Random(42));
        Assert(random.Select(entry => entry.Position).Order().SequenceEqual([1,2,3,4,6]) && entries.Select(entry => entry.Position).SequenceEqual([1,2,3,4,5,6]), "Random ordering changed the selected set or mutated the inventory.");
    }
    private static void Large()
    {
        var huge = "1" + new string('0', 1000);
        Assert(Select("1:" + huge + ":3").SequenceEqual([1,4,7,10]), "A huge positive endpoint expanded outside the bounded inventory.");
        Assert(Select(huge).Length == 0 && Select("-" + huge).Length == 0, "A huge standalone index overflowed into a real position.");
        Assert(Select("-" + huge + ":inf:2").SequenceEqual([1,3,5,7,9]), "Clipping a huge negative start lost its arithmetic phase.");
        Assert(Select("1:10:" + huge).SequenceEqual([1]), "A huge step overflowed or iterated beyond the work list.");
        Throws<ArgumentException>(() => Select(new string('1', 4097)));
        Throws<ArgumentException>(() => Select(new string(' ', 4097)));
    }
    private static void Sparse()
    {
        var entries = Entries(7).Where(entry => entry.Position is 2 or 7).ToArray();
        var request = Request(Path.GetTempPath()) with { PlaylistItems = "-1,2,1:7" };
        Assert(PlaylistSelection.Select(entries, request).Select(entry => entry.Position).SequenceEqual([7,2]), "Sparse source positions were renumbered or duplicated.");
        Throws<InvalidDataException>(() => PlaylistSelection.Select([entries[0], entries[0]], request));
        Throws<InvalidDataException>(() => PlaylistSelection.Select([entries[0] with { Position = 0 }], request));
        Throws<InvalidDataException>(() => PlaylistSelection.Select(Entries(3), request with { MaximumPlaylistEntries = 2 }));
    }
    private static async Task Validation()
    {
        foreach (var expression in new[] { "1,,2", "::0", "1:2:3:4", "1:3:", "a", "1:-inf", "--", "+", "1 : 2", "1-2:zero", "1:10,broken" })
        {
            var engine = new NativeEngine(httpFactory: _ => throw new InvalidOperationException("Malformed selection reached HTTP session creation."));
            await Expect<ArgumentException>(() => engine.InspectAsync(Request(Path.GetTempPath()) with { PlaylistItems = expression }, default));
        }
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Throws<OperationCanceledException>(() => PlaylistSelection.Select(Entries(10), Request(Path.GetTempPath()), Random.Shared, stop.Token));
    }
    private static void Arguments()
    {
        foreach (var tokens in new[] { new[] { "-I", "-3::2" }, new[] { "-I-3::2" }, new[] { "-I=-3::2" }, new[] { "--playlist-items=-3::2" } })
            Assert(PlaylistSelection.Select(Entries(10), NativeExtraArguments.Apply(Request(Path.GetTempPath()), tokens)).Select(entry => entry.Position).SequenceEqual([8,10]), "An -I spelling lost its signed selection.");
        Assert(PlaylistSelection.Select(Entries(10), NativeExtraArguments.Apply(Request(Path.GetTempPath()), ["--playlist-items", "--2"])).Count == 9, "An open legacy negative end was mistaken for another option.");
        Assert(PlaylistSelection.Select(Entries(10), NativeExtraArguments.Apply(Request(Path.GetTempPath()), ["--playlist-end", "0"])).Count == 0, "Explicit zero was confused with an unset app boundary.");
        Assert(PlaylistSelection.Select(Entries(10), NativeExtraArguments.Apply(Request(Path.GetTempPath()), ["--playlist-start", "-3", "--playlist-end", "-1"])).Select(entry => entry.Position).SequenceEqual([8,9,10]), "Legacy boundary handling lost negative starts or the whole-list end marker.");
        Assert(PlaylistSelection.Select(Entries(10), NativeExtraArguments.Apply(Request(Path.GetTempPath()), ["--playlist-end", "0", "-I", "4,2"])).Select(entry => entry.Position).SequenceEqual([4,2]), "Explicit item selection did not override start/end flags.");
    }
    private static async Task Numbering()
    {
        using var files = new Files(); var calls = new List<int>(); var callbacks = new List<string>();
        var result = await Engine(calls).DownloadAsync(Request(files.Output) with { PlaylistItems = "-1,2:1:-1,2", WriteInfoJson = true,
            OutputTemplate = "%(playlist_autonumber)02d-%(playlist_index)02d-%(id)s.%(ext)s" }, null, null, default, callbacks.Add);
        Assert(result.Succeeded && calls.SequenceEqual([5,2,1]) && result.OutputPaths.SequenceEqual(callbacks) &&
            callbacks.Select(Path.GetFileName).SequenceEqual(["01-05-5.mp4", "02-02-2.mp4", "03-01-1.mp4"]), "Sequential filenames confused selected order with original playlist indexes.");
        for (var index = 0; index < callbacks.Count; index++)
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(callbacks[index], "info.json")));
            var root = json.RootElement;
            Assert(root.GetProperty("playlist_index").GetInt32() == calls[index] && root.GetProperty("playlist_autonumber").GetInt32() == index + 1 && root.GetProperty("n_entries").GetInt32() == 5 &&
                root.GetProperty("playlist_id").GetString() == "list" && root.GetProperty("playlist_title").GetString() == "Selection fixture", "Info JSON lost playlist identity, original position or selected-order numbering.");
        }
    }
    private static async Task Empty()
    {
        using var files = new Files(); var calls = new List<int>(); var callbacks = 0; var archive = Path.Combine(files.Root, "new-archive", "history.txt");
        var result = await Engine(calls).DownloadAsync(Request(files.Output) with { PlaylistItems = "0,9000,4:2", DownloadArchiveFile = archive }, null, null, default, _ => callbacks++);
        Assert(result.Succeeded && result.Skipped && calls.Count == 0 && callbacks == 0 && !Directory.Exists(files.Output) && !Directory.Exists(Path.GetDirectoryName(archive)!),
            "An empty selection created output/archive files or reported a media completion.");
    }
    private static async Task Failure()
    {
        using var files = new Files(); var calls = new List<int>();
        var result = await Engine(calls, unavailable: 2).DownloadAsync(Request(files.Output) with { PlaylistItems = "5,2,1", WriteInfoJson = true,
            OutputTemplate = "%(playlist_autonumber)d-%(playlist_index)d.%(ext)s" }, null, null, default);
        Assert(!result.Succeeded && result.Failures.Single().Position == 2 && calls.SequenceEqual([5,1]) && result.OutputPaths.Select(Path.GetFileName).SequenceEqual(["1-5.mp4", "3-1.mp4"]),
            "A failed selection renumbered later work or lost its original failure position.");
        calls.Clear();
        var stopped = await Engine(calls, unavailable: 2).DownloadAsync(Request(files.Output) with { PlaylistItems = "5,2,1", IgnoreErrors = false }, null, null, default);
        Assert(!stopped.Succeeded && calls.SequenceEqual([5]), "Ordered stop-on-error continued past an unavailable selected item.");
    }
    private static async Task Archive()
    {
        using var files = new Files(); var calls = new List<int>(); var archive = Path.Combine(files.Root, "history.txt"); File.WriteAllText(archive, "selection-fixture 5\n");
        var result = await Engine(calls).DownloadAsync(Request(files.Output) with { PlaylistItems = "5,1", DownloadArchiveFile = archive,
            OutputTemplate = "%(playlist_autonumber)d-%(playlist_index)d.%(ext)s" }, null, null, default);
        Assert(result.Succeeded && calls.SequenceEqual([1]) && Path.GetFileName(result.OutputPaths.Single()) == "2-1.mp4" && File.ReadAllLines(archive).SequenceEqual(["selection-fixture 5", "selection-fixture 1"]),
            "Archived selections changed numbering, identity or media requests.");
    }
    private static async Task Cancellation()
    {
        using var files = new Files(); using var stop = new CancellationTokenSource(); var calls = new List<int>(); var completed = new List<string>();
        await Expect<OperationCanceledException>(() => Engine(calls).DownloadAsync(Request(files.Output) with { PlaylistItems = "-1::-1", WriteInfoJson = true }, null, null, stop.Token,
            path => { completed.Add(path); stop.Cancel(); }));
        Assert(calls.SequenceEqual([5]) && completed.Count == 1 && File.Exists(completed[0]) && File.Exists(Path.ChangeExtension(completed[0], "info.json")), "Cancellation removed a completed ordered result or started another item.");
    }
    private static NativeDownloadRequest Request(string directory) => new() { Url = new("https://media.example/list"), OutputDirectory = directory, Retries = 0 };
    private static NativeEngine Engine(List<int> calls, int? unavailable = null) => new([new Provider(unavailable)], _ => new MediaHttp(new Handler(request =>
    {
        var index = int.Parse(Path.GetFileNameWithoutExtension(request.RequestUri!.AbsolutePath)); calls.Add(index);
        return new(HttpStatusCode.OK) { Content = new StringContent("media " + index) };
    })));
    private sealed class Provider(int? unavailable) : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("selection-fixture", "Selection fixture", 300);
        public bool CanHandle(Uri url) => url.Host == "media.example";
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(new PageMetadata
        { Id = "list", PlaylistId = "list", Title = "Selection fixture", IsPlaylist = true, Entries = Entries(5).Select(entry => entry with {
            Available = entry.Position != unavailable, ResolvedMedia = new PageMetadata { Id = entry.Id, Extractor = Info.Id, Title = entry.Title,
                Media = [new() { Url = entry.Url!, Extension = "mp4", HasVideo = true, HasAudio = true }] } }).ToArray() });
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Files : IDisposable
    {
        private static readonly string Base = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Root { get; } = Path.Combine(Base, "playlist-syntax-" + Guid.NewGuid().ToString("N"));
        internal string Output => Path.Combine(Root, "output");
        internal Files() => Directory.CreateDirectory(Root);
        public void Dispose() { var path = Path.GetFullPath(Root); if (!path.StartsWith(Base, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Test folder escaped its root."); Directory.Delete(path, true); }
    }
}
