using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class NestedPlaylistChecks
{
    internal static Uri Url(string id) => new("https://nested.example/" + id);
    internal static NativeDownloadRequest Request(string directory) => new() { Url = Url("root"), OutputDirectory = directory, WriteInfoJson = true, Retries = 0 };
    internal static PageMetadata Video(string id) => new() { Id = id, Extractor = "nested", Title = id, ArchiveIdentity = new("nested", id),
        Media = [new() { Url = new("https://cdn.example/" + id + ".mp4"), Extension = "mp4", Kind = MediaKind.Progressive, HasAudio = true, HasVideo = true, IsCompleteMedia = true }] };
    internal static PageMetadata List(string id, params NativePlaylistEntry[] entries) => new() { Id = id, PlaylistId = id, Extractor = "nested", ArchiveIdentity = new("nested", id), Title = id,
        IsPlaylist = true, Entries = entries, SourcePageUrl = Url(id) };
    internal static NativePlaylistEntry Entry(int position, string id, bool available = true, PageMetadata? resolved = null) => new(position, id, id, Url(id), 2, null, available, resolved, new("nested", id));
    internal sealed class Provider : IMediaProvider
    {
        internal Dictionary<string, PageMetadata> Nodes = new() { ["root"] = List("root", Entry(1, "inner"), Entry(2, "c")), ["inner"] = List("inner", Entry(1, "a"), Entry(2, "b")),
            ["a"] = Video("a"), ["b"] = Video("b"), ["c"] = Video("c") };
        internal List<string> Calls = [];
        public MediaProviderInfo Info { get; } = new("nested", "Nested fixture", 1000);
        public bool CanHandle(Uri url) => url.Host == "nested.example";
        public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => Nodes.TryGetValue(request.Url.Segments.Last(), out var page) && !page.IsPlaylist ? page.ArchiveIdentity : null;
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
        { var id = request.Url.Segments.Last(); Calls.Add(id); return Task.FromResult(Nodes[id]); }
    }
    internal static NativeEngine Engine(Provider provider, List<Uri>? bytes = null) => new([provider], _ => new MediaHttp(new Handler(request =>
    { bytes?.Add(request.RequestUri!); return new(HttpStatusCode.OK) { Content = new ByteArrayContent("nested complete media"u8.ToArray()) }; })));
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Nested playlist linked collections preserve all outputs with one callback per file", Linked),
        ("Nested playlist resolved collections preserve parent and child source metadata", Resolved),
        ("Nested playlist generic multi-embed pages retain collection children", Embedded),
        ("Nested playlist partial failures retain outputs and original parent paths", Partial),
        ("Nested playlist abort-on-error stops inner and outer later entries", Abort),
        ("Nested playlist cancellation retains notified media without duplicate callbacks", Cancellation),
        ("Nested playlist shared download limits apply across all collection levels", Limit),
        ("Nested playlist repeated ancestors fail without deleting completed media", Cycle),
        ("Nested playlist depth and aggregate traversal bounds fail explicitly", Bounds),
        ("Nested playlist archive repeats do not retransfer or duplicate completed files", Archive),
        ("Nested playlist changed container identities cannot publish replacement media", Identity)
    ];
    private static async Task Linked()
    {
        using var files = new Files(); var provider = new Provider(); var callbacks = new List<string>();
        var result = await Engine(provider).DownloadAsync(Request(files.Directory), null, null, default, callbacks.Add);
        Assert(result.Succeeded && result.OutputPaths.Count == 3 && callbacks.Count == 3 && callbacks.Distinct().Count() == 3 && result.OutputPaths.SequenceEqual(callbacks), "Nested callbacks or final outputs were missing/duplicated.");
    }
    private static async Task Resolved()
    {
        using var files = new Files(); var provider = new Provider(); provider.Nodes["root"] = List("root", Entry(1, "unused", false), Entry(2, "inner", resolved: provider.Nodes["inner"]));
        var result = await Engine(provider).DownloadAsync(Request(files.Directory) with { PlaylistItems = "2" }, null, null, default);
        Assert(result.Succeeded && result.OutputPaths.Count == 2 && !provider.Calls.Contains("inner"), "Resolved nested collection was discarded or refetched.");
        using var info = JsonDocument.Parse(await File.ReadAllTextAsync(result.SidecarPaths.First(path => path.EndsWith(".info.json"))));
        var parent = info.RootElement.GetProperty("playlist_parents")[0];
        Assert(parent.GetProperty("id").GetString() == "root" && parent.GetProperty("playlist_index").GetInt32() == 2 && parent.GetProperty("playlist_autonumber").GetInt32() == 1 &&
            info.RootElement.GetProperty("playlist_id").GetString() == "inner" && info.RootElement.GetProperty("playlist_index").GetInt32() == 1, "Parent/child original source metadata collapsed.");
    }
    private static async Task Partial()
    {
        using var files = new Files(); var provider = new Provider(); provider.Nodes["inner"] = List("inner", Entry(1, "a"), Entry(2, "private", false)); var callbacks = new List<string>();
        var result = await Engine(provider).DownloadAsync(Request(files.Directory), null, null, default, callbacks.Add);
        Assert(!result.Succeeded && result.OutputPaths.Count == 2 && callbacks.Count == 2 && result.Failures.Single().Position == 2 && result.Failures[0].Parents?.Single().Index == 1 &&
            result.OutputPaths.All(File.Exists), "Partial nested failures lost completed media or original failure path.");
    }
    private static async Task Embedded()
    {
        using var files = new Files(); var provider = new Provider(); var callbacks = new List<string>();
        var engine = new NativeEngine([provider, new GenericPageProvider()], _ => new MediaHttp(new Handler(request => new(HttpStatusCode.OK) { Content = request.RequestUri!.Host == "article.example" ?
            new StringContent("<title>Embedded collections</title><iframe src='https://nested.example/inner'></iframe><iframe src='https://nested.example/c'></iframe>") : new ByteArrayContent("nested media"u8.ToArray()) })));
        var result = await engine.DownloadAsync(Request(files.Directory) with { Url = new("https://article.example/tree") }, null, null, default, callbacks.Add);
        Assert(result.Succeeded && result.OutputPaths.Count == 3 && callbacks.Count == 3, "Generic embed discovery replaced a collection with an unavailable placeholder.");
    }
    private static async Task Abort()
    {
        using var files = new Files(); var provider = new Provider(); provider.Nodes["inner"] = List("inner", Entry(1, "private", false), Entry(2, "b"));
        var result = await Engine(provider).DownloadAsync(Request(files.Directory) with { IgnoreErrors = false }, null, null, default);
        Assert(!result.Succeeded && result.OutputPaths.Count == 0 && !provider.Calls.Contains("b") && !provider.Calls.Contains("c"), "Nested abort still requested later child/outer entries.");
    }
    private static async Task Cancellation()
    {
        using var files = new Files(); var provider = new Provider(); var callbacks = new List<string>(); using var stop = new CancellationTokenSource();
        await Reject<OperationCanceledException>(() => Engine(provider).DownloadAsync(Request(files.Directory), null, null, stop.Token, path => { callbacks.Add(path); stop.Cancel(); }));
        Assert(callbacks.Count == 1 && File.Exists(callbacks[0]) && !provider.Calls.Contains("b") && !provider.Calls.Contains("c"), "Nested cancellation requested later items or erased completion.");
    }
    private static async Task Limit()
    {
        using var files = new Files(); var provider = new Provider(); var callbacks = new List<string>();
        var result = await Engine(provider).DownloadAsync(Request(files.Directory) with { MaxDownloads = 1 }, null, null, default, callbacks.Add);
        Assert(result.Succeeded && result.DownloadLimitReached && callbacks.Count == 1 && !provider.Calls.Contains("b") && !provider.Calls.Contains("c"), "Nested collections reset the shared completion limit.");
    }
    private static async Task Cycle()
    {
        using var files = new Files(); var provider = new Provider(); provider.Nodes["inner"] = List("inner", Entry(1, "a"), Entry(2, "root")); var callbacks = new List<string>();
        await Reject<NativePlaylistTraversalException>(() => Engine(provider).DownloadAsync(Request(files.Directory), null, null, default, callbacks.Add));
        Assert(callbacks.Count == 1 && File.Exists(callbacks[0]), "Ancestor refusal deleted complete notified output.");
    }
    private static async Task Bounds()
    {
        using var files = new Files(); var provider = new Provider(); provider.Nodes["root"] = List("root", Entry(1, "level1"));
        for (var level = 1; level <= 6; level++) provider.Nodes["level" + level] = List("level" + level, Entry(1, level == 6 ? "a" : "level" + (level + 1)));
        await Reject<NativePlaylistTraversalException>(() => Engine(provider).DownloadAsync(Request(files.Directory), null, null, default));
        provider = new Provider(); provider.Nodes["root"] = List("root", Enumerable.Range(1, 6000).Select(index => Entry(index, index == 1 ? "inner" : "a")).ToArray());
        provider.Nodes["inner"] = List("inner", Enumerable.Range(1, 5000).Select(index => Entry(index, "a")).ToArray());
        await Reject<NativePlaylistTraversalException>(() => Engine(provider).DownloadAsync(Request(files.Directory) with { MaximumPlaylistEntries = 10000, PlaylistItems = "1" }, null, null, default));
    }
    private static async Task Archive()
    {
        using var files = new Files(); var provider = new Provider(); var bytes = new List<Uri>(); var engine = Engine(provider, bytes); var archive = Path.Combine(files.Directory, "archive.txt");
        var request = Request(files.Directory) with { DownloadArchiveFile = archive }; await engine.DownloadAsync(request, null, null, default); bytes.Clear(); var callbacks = new List<string>();
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        Assert(result.Succeeded && callbacks.Count == 0 && bytes.Count == 0 && (await File.ReadAllLinesAsync(archive)).Length == 3, "Nested archive retries re-downloaded or recorded collection wrappers as media.");
    }
    private static async Task Identity()
    {
        using var files = new Files(); var provider = new Provider(); provider.Nodes["inner"] = List("replacement", Entry(1, "a")); var bytes = new List<Uri>();
        var result = await Engine(provider, bytes).DownloadAsync(Request(files.Directory) with { PlaylistItems = "1" }, null, null, default);
        Assert(!result.Succeeded && bytes.Count == 0 && result.OutputPaths.Count == 0, "Changed collection identity published another container's media.");
    }
    private sealed class Files : IDisposable
    { internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "native-nested-" + Guid.NewGuid()); public Files() => System.IO.Directory.CreateDirectory(Directory); public void Dispose() => System.IO.Directory.Delete(Directory, true); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(run(request)); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Reject<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
