using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class PlaylistSnapshotChecks
{
    internal static Uri Root => new("https://selection.example/list");
    internal static NativeDownloadRequest Request(string directory) => new() { Url = Root, OutputDirectory = directory, Retries = 0, WriteInfoJson = true };
    internal static PageMetadata Video(string id, string stamp = "fresh") => new() { Id = id, Extractor = "selection", Title = "Clip " + id,
        Media = [new() { Url = new("https://cdn.example/" + id + "-" + stamp + ".mp4"), Extension = "mp4", Kind = MediaKind.Progressive, HasAudio = true, HasVideo = true, IsCompleteMedia = true }] };
    internal static PageMetadata Playlist(bool parent, params string[] ids) => new() { Id = "list", PlaylistId = "list", IsPlaylist = true, Extractor = "selection", Title = "Inspected list",
        Entries = ids.Select((id, index) => new NativePlaylistEntry(index + 1, id, "Old " + id,
            parent ? Video(id, "expired-token").Media[0].Url : new("https://selection.example/item/" + id), 2, null, true, parent ? Video(id, "expired-token") : null)).ToList() };
    internal sealed class Provider : IMediaProvider
    {
        internal PageMetadata Page;
        internal bool RefreshParent;
        internal int ParentCalls;
        internal List<string> Children = [];
        internal string? WrongChild;
        internal string? LastReferer;
        internal Provider(bool parent = false) { RefreshParent = parent; Page = Playlist(parent, "a", "b"); }
        public MediaProviderInfo Info { get; } = new("selection", "Snapshot fixture", 1000);
        public bool CanHandle(Uri url) => url.Host == "selection.example";
        public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => request.Url.AbsolutePath.StartsWith("/item/", StringComparison.Ordinal) ? new("selection", request.Url.Segments.Last()) : null;
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
        {
            if (request.Url.AbsolutePath == "/list") { ParentCalls++; return Task.FromResult(Page); }
            var id = request.Url.Segments.Last(); Children.Add(id); LastReferer = request.Referer; return Task.FromResult(Video(WrongChild ?? id));
        }
    }
    internal static NativeEngine Engine(Provider provider, List<Uri>? calls = null) => new([provider, new DirectMediaProvider()], _ => Http(calls));
    internal static MediaHttp Http(List<Uri>? calls = null) => new(new Handler(request =>
    {
        calls?.Add(request.RequestUri!);
        if (request.RequestUri!.AbsoluteUri.Contains("expired-token", StringComparison.Ordinal)) throw new InvalidOperationException("An expired inspected playback link was used.");
        return new(HttpStatusCode.OK) { Content = new ByteArrayContent("fresh complete media"u8.ToArray()) };
    }));
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Playlist snapshot keeps selected child identities when the parent order changes", Linked),
        ("Playlist snapshot refreshes direct playback by identity without using inspected URLs", Parent),
        ("Playlist snapshot refreshed parent IDs also support raw direct-media rows", ParentLinks),
        ("Playlist snapshot child discovery retains inspected embed referrers and explicit overrides", Referers),
        ("Playlist snapshot missing refreshed members cannot download replacement positions", Missing),
        ("Playlist snapshot refuses changed parent identities before publication", ParentIdentity),
        ("Playlist snapshot refuses changed child identities before media transfer", ChildIdentity),
        ("Playlist snapshot URL mode and count bindings fail before HTTP access", Binding),
        ("Playlist snapshot unavailable positions and output source indexes remain unchanged", Unavailable),
        ("Playlist snapshot cancellation retains complete files without fetching later items", Cancellation),
        ("Playlist snapshot archive retries skip linked children before playback lookup", Archive),
        ("Playlist snapshot direct-source refresh rejects ambiguous repeated identities", Ambiguous),
        ("Playlist snapshot empty selection does not refresh unrelated source metadata", Empty),
        ("Playlist snapshot copies inspection lists and omits playback session data from JSON", Isolation)
    ];
    private static async Task Linked()
    {
        using var files = new Files(); var provider = new Provider(); var calls = new List<Uri>(); var engine = Engine(provider, calls);
        var info = await engine.InspectAsync(Request(files.Directory), default); provider.Page = Playlist(false, "b", "a", "new");
        var result = await engine.DownloadAsync(Request(files.Directory) with { PlaylistSnapshot = info.PlaylistSnapshot, PlaylistItems = "2" }, null, null, default);
        Assert(result.Succeeded && provider.ParentCalls == 1 && provider.Children.SequenceEqual(new[] { "b" }) && calls.Single().AbsolutePath == "/b-fresh.mp4", "Reordered listing replaced the selected inspected child.");
        await Metadata(result, 2, 1, 2);
    }
    private static async Task Parent()
    {
        using var files = new Files(); var provider = new Provider(true); var calls = new List<Uri>(); var engine = Engine(provider, calls); var info = await engine.InspectAsync(Request(files.Directory), default);
        provider.Page = Playlist(true, "b", "a") with { Entries = new[] { new NativePlaylistEntry(1, "b", "Fresh B", Video("b").Media[0].Url, 2, null, true, Video("b")),
            new NativePlaylistEntry(2, "a", "Fresh A", Video("a").Media[0].Url, 2, null, true, Video("a")) } };
        var result = await engine.DownloadAsync(Request(files.Directory) with { PlaylistSnapshot = info.PlaylistSnapshot, PlaylistItems = "2" }, null, null, default);
        Assert(result.Succeeded && provider.ParentCalls == 2 && provider.Children.Count == 0 && calls.Single().AbsolutePath == "/b-fresh.mp4", "Parent refresh used stale URLs or a new position."); await Metadata(result, 2, 1, 2);
    }
    private static async Task Missing()
    {
        using var files = new Files(); var provider = new Provider(true); var calls = new List<Uri>(); var engine = Engine(provider, calls); var info = await engine.InspectAsync(Request(files.Directory), default);
        provider.Page = Playlist(true, "a", "replacement"); var callbacks = new List<string>();
        var result = await engine.DownloadAsync(Request(files.Directory) with { PlaylistSnapshot = info.PlaylistSnapshot, PlaylistItems = "2" }, null, null, default, callbacks.Add);
        Assert(!result.Succeeded && result.Failures.Single().Position == 2 && calls.Count == 0 && callbacks.Count == 0, "A missing member downloaded the new item at its old position.");
    }
    private static async Task Referers()
    {
        using var files = new Files(); var provider = new Provider(); var media = Video("b") with { PlaybackHeaders = new Dictionary<string, string> { ["Referer"] = "https://article.example/actual-player-page" } };
        provider.Page = provider.Page with { Entries = new[] { provider.Page.Entries[0], provider.Page.Entries[1] with { ResolvedMedia = media } } };
        var engine = Engine(provider); var info = await engine.InspectAsync(Request(files.Directory), default);
        await engine.DownloadAsync(Request(files.Directory) with { PlaylistSnapshot = info.PlaylistSnapshot, PlaylistItems = "2", SkipDownload = true }, null, null, default);
        Assert(provider.LastReferer == "https://article.example/actual-player-page", "Snapshot refresh lost the actual embedding page's access context.");
        await engine.DownloadAsync(Request(files.Directory) with { PlaylistSnapshot = info.PlaylistSnapshot, PlaylistItems = "2", SkipDownload = true, Referer = "https://explicit.example/override" }, null, null, default);
        Assert(provider.LastReferer == "https://explicit.example/override", "Inspected referrer replaced the explicit current-job override.");
    }
    private static async Task ParentLinks()
    {
        using var files = new Files(); var provider = new Provider(true); var calls = new List<Uri>(); var engine = Engine(provider, calls);
        provider.Page = provider.Page with { Entries = provider.Page.Entries.Select(entry => entry with { ResolvedMedia = null }).ToArray() };
        var info = await engine.InspectAsync(Request(files.Directory), default);
        provider.Page = Playlist(true, "b", "a") with { Entries = new[] { new NativePlaylistEntry(1, "b", "B", Video("b").Media[0].Url, 2, null, true),
            new NativePlaylistEntry(2, "a", "A", Video("a").Media[0].Url, 2, null, true) } };
        var result = await engine.DownloadAsync(Request(files.Directory) with { PlaylistSnapshot = info.PlaylistSnapshot, PlaylistItems = "2" }, null, null, default);
        Assert(result.Succeeded && calls.Single().AbsolutePath == "/b-fresh.mp4", "A parent-bound direct URL was rejected for lacking a separate child-page ID.");
    }
    private static async Task ParentIdentity()
    {
        using var files = new Files(); var provider = new Provider(true); var calls = new List<Uri>(); var engine = Engine(provider, calls); var info = await engine.InspectAsync(Request(files.Directory), default);
        provider.Page = Playlist(true, "a", "b") with { Id = "other" }; await Reject<InvalidDataException>(() => engine.DownloadAsync(Request(files.Directory) with { PlaylistSnapshot = info.PlaylistSnapshot }, null, null, default));
        Assert(calls.Count == 0, "Changed parent transferred media.");
    }
    private static async Task ChildIdentity()
    {
        using var files = new Files(); var provider = new Provider(); var calls = new List<Uri>(); var engine = Engine(provider, calls); var info = await engine.InspectAsync(Request(files.Directory), default); provider.WrongChild = "replacement";
        var result = await engine.DownloadAsync(Request(files.Directory) with { PlaylistSnapshot = info.PlaylistSnapshot, PlaylistItems = "2" }, null, null, default);
        Assert(!result.Succeeded && calls.Count == 0 && result.OutputPaths.Count == 0, "Changed linked video was published.");
    }
    private static async Task Binding()
    {
        using var files = new Files(); var provider = new Provider(); var calls = new List<Uri>(); var engine = Engine(provider, calls); var info = await engine.InspectAsync(Request(files.Directory), default);
        foreach (var bad in new[] { Request(files.Directory) with { Url = new("https://selection.example/other") }, Request(files.Directory) with { DownloadPlaylist = false } })
            await Reject<ArgumentException>(() => engine.DownloadAsync(bad with { PlaylistSnapshot = info.PlaylistSnapshot }, null, null, default));
        await Reject<InvalidOperationException>(() => engine.DownloadAsync(Request(files.Directory) with { PlaylistSnapshot = info.PlaylistSnapshot, MaximumPlaylistEntries = 1 }, null, null, default));
        Assert(provider.ParentCalls == 1 && calls.Count == 0, "Invalid snapshot bindings accessed network or rediscovered a parent.");
    }
    private static async Task Unavailable()
    {
        using var files = new Files(); var provider = new Provider(); provider.Page = provider.Page with { Entries = new[] { provider.Page.Entries[0] with { Available = false }, provider.Page.Entries[1] } };
        var engine = Engine(provider); var info = await engine.InspectAsync(Request(files.Directory), default);
        var result = await engine.DownloadAsync(Request(files.Directory) with { PlaylistSnapshot = info.PlaylistSnapshot }, null, null, default);
        Assert(!result.Succeeded && result.Failures.Single().Position == 1 && provider.Children.SequenceEqual(new[] { "b" }), "Unavailable source positions were renumbered or requested."); await Metadata(result, 2, 2, 2);
    }
    private static async Task Cancellation()
    {
        using var files = new Files(); var provider = new Provider(); var engine = Engine(provider); var info = await engine.InspectAsync(Request(files.Directory), default); var callbacks = new List<string>(); using var stop = new CancellationTokenSource();
        await Reject<OperationCanceledException>(() => engine.DownloadAsync(Request(files.Directory) with { PlaylistSnapshot = info.PlaylistSnapshot }, null, null, stop.Token, path => { callbacks.Add(path); stop.Cancel(); }));
        Assert(callbacks.Count == 1 && File.Exists(callbacks[0]) && provider.Children.SequenceEqual(new[] { "a" }), "Cancellation discarded completion or requested later selected items.");
    }
    private static async Task Archive()
    {
        using var files = new Files(); var provider = new Provider(); var calls = new List<Uri>(); var engine = Engine(provider, calls); var info = await engine.InspectAsync(Request(files.Directory), default);
        var archive = Path.Combine(files.Directory, "archive.txt"); await File.WriteAllTextAsync(archive, "selection a\nselection b\n");
        var result = await engine.DownloadAsync(Request(files.Directory) with { PlaylistSnapshot = info.PlaylistSnapshot, DownloadArchiveFile = archive }, null, null, default);
        Assert(result.Succeeded && result.Skipped && provider.ParentCalls == 1 && provider.Children.Count == 0 && calls.Count == 0, "Recorded snapshot children requested metadata or bytes.");
    }
    private static async Task Ambiguous()
    {
        using var files = new Files(); var provider = new Provider(true); var engine = Engine(provider); var info = await engine.InspectAsync(Request(files.Directory), default);
        provider.Page = Playlist(true, "b", "b") with { Entries = new[] { new NativePlaylistEntry(1, "b", "B", Video("b", "fresh-one").Media[0].Url, 2, null, true, Video("b", "fresh-one")),
            new NativePlaylistEntry(2, "b", "B", Video("b", "fresh-two").Media[0].Url, 2, null, true, Video("b", "fresh-two")) } };
        await Reject<InvalidDataException>(() => engine.DownloadAsync(Request(files.Directory) with { PlaylistSnapshot = info.PlaylistSnapshot, PlaylistItems = "2" }, null, null, default));
    }
    private static async Task Empty()
    {
        using var files = new Files(); var provider = new Provider(true); var engine = Engine(provider); var info = await engine.InspectAsync(Request(files.Directory), default);
        var result = await engine.DownloadAsync(Request(files.Directory) with { PlaylistSnapshot = info.PlaylistSnapshot, PlaylistItems = "9" }, null, null, default);
        Assert(result.Succeeded && result.OutputPaths.Count == 0 && provider.ParentCalls == 1, "Empty selection refreshed unrelated source metadata.");
    }
    private static async Task Isolation()
    {
        using var files = new Files(); var provider = new Provider(); var engine = Engine(provider); var info = await engine.InspectAsync(Request(files.Directory), default); var snapshot = info.PlaylistSnapshot!;
        ((List<NativePlaylistEntry>)provider.Page.Entries).Clear();
        Assert(snapshot.Count == 2 && !JsonSerializer.Serialize(info).Contains("PlaylistSnapshot", StringComparison.Ordinal) && JsonSerializer.Serialize(snapshot) == "{\"Count\":2}", "Snapshot retained mutable inspection storage or serialized runtime data.");
        var result = await engine.DownloadAsync(Request(files.Directory) with { PlaylistSnapshot = snapshot, PlaylistItems = "2" }, null, null, default);
        Assert(result.Succeeded && provider.Children.Single() == "b", "Mutating public inspection metadata changed a captured selection.");
    }
    private static async Task Metadata(NativeDownloadResult result, int index, int number, int count)
    { using var json = JsonDocument.Parse(await File.ReadAllTextAsync(result.SidecarPaths.Single(path => path.EndsWith(".info.json")))); Assert(json.RootElement.GetProperty("playlist_index").GetInt32() == index &&
        json.RootElement.GetProperty("playlist_autonumber").GetInt32() == number && json.RootElement.GetProperty("n_entries").GetInt32() == count, "Snapshot changed source position, job number or inspected count."); }
    private sealed class Files : IDisposable
    { internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "native-selection-" + Guid.NewGuid()); public Files() => System.IO.Directory.CreateDirectory(Directory); public void Dispose() => System.IO.Directory.Delete(Directory, true); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(run(request)); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Reject<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
