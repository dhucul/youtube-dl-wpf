using System.Net;
using System.Net.Sockets;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class PlaylistSessionChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Playlist HTTP sessions retain listing and entry cookies with scoped playback headers", () => Run("normal")),
        ("Playlist HTTP sessions are shared across resolved and freshly discovered entries", () => Run("snapshot")),
        ("Separate playlist jobs keep independent cookie jars", () => Run("isolation")),
        ("Playlist archive skips preserve the session for remaining selected entries", () => Run("archive")),
        ("Playlist item failures preserve cookies for subsequent selected entries", () => Run("failure")),
        ("Playlist cancellation preserves completed media and a later job starts cleanly", () => Run("cancel")),
        ("Playlist resolved snapshots must match advertised archive identities", () => Run("binding"))
    ];
    private static async Task Run(string mode)
    {
        using var files = new Files(); using var cancel = new CancellationTokenSource();
        await using var server = new Server(mode, mode == "cancel" ? cancel.Cancel : null);
        var provider = new Provider(server.Root, mode is "snapshot" or "binding", mode == "binding"); var engine = new NativeEngine([provider]);
        var archive = Path.Combine(files.Directory, "archive.txt");
        if (mode == "archive") await File.WriteAllTextAsync(archive, "session-fixture 1\n");
        var request = new NativeDownloadRequest { Url = new(server.Root, "list"), OutputDirectory = files.Directory, FileName = "entry", Retries = 0,
            DownloadArchiveFile = archive, UserAgent = "PlaylistFixture/1.0", Referer = "https://caller.example/", IgnoreErrors = true };
        var completed = new List<string>(); NativeDownloadResult? result = null;
        if (mode == "cancel")
        {
            try { await engine.DownloadAsync(request, null, null, cancel.Token, completed.Add); throw new InvalidOperationException("Playlist cancellation was swallowed."); }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            Assert(completed.Count == 1 && File.ReadAllText(completed[0]) == "media-1" && (await File.ReadAllLinesAsync(archive)).SequenceEqual(new[] { "session-fixture 1" }), "Cancellation lost or falsely reported a completed earlier entry.");
            var recorded = server.Requests;
            Assert(!recorded.Any(item => item.Path == "/media/2"), "Canceled discovery continued downloading media.");
            completed.Clear(); result = await engine.DownloadAsync(request, null, null, default, completed.Add);
            Assert(result.Succeeded && completed.Count == 1 && File.ReadAllText(completed[0]) == "media-2", "A later isolated job could not finish the remaining entry.");
        }
        else
        {
            result = await engine.DownloadAsync(request, null, null, default, completed.Add);
            var expected = mode is "archive" or "failure" or "binding" ? 1 : 2;
            Assert(result.Succeeded == (mode is not ("failure" or "binding")) && result.OutputPaths.Count == expected && completed.Count == expected && result.Failures.Count == (mode is "failure" or "binding" ? 1 : 0),
                "Listing cookies or entry cookie updates were lost before selected playback: " + result.Error);
            Assert(completed.All(path => File.ReadAllText(path) is "media-1" or "media-2"), "A selected entry saved another resource.");
            if (mode == "archive") Assert(server.Requests.All(item => item.Path is not ("/item/1" or "/media/1")), "An archived entry queried discovery or media.");
            if (mode == "snapshot") Assert(server.Requests.All(item => item.Path != "/item/1"), "A resolved item repeated discovery.");
            if (mode == "binding") Assert(server.Requests.All(item => item.Path is not ("/item/1" or "/media/wrong")), "A mismatched resolved snapshot fetched or published media.");
            if (mode == "isolation")
            {
                completed.Clear(); result = await engine.DownloadAsync(request with { FileName = "another", DownloadArchiveFile = "" }, null, null, default, completed.Add);
                Assert(result.Succeeded && completed.Count == 2, "A second job could not create its own session.");
            }
        }
        var requests = server.Requests;
        Assert(requests.Where(item => item.Path == "/list").All(item => !item.Headers.Contains("seed=", StringComparison.Ordinal)), "A separate job reused another job's cookie jar.");
        Assert(requests.Where(item => item.Path.StartsWith("/item/") || item.Path.StartsWith("/media/")).All(item => item.Headers.Contains("seed=fixture", StringComparison.Ordinal)), "A selected entry lost the listing's cookie.");
        Assert(requests.All(item => item.Headers.Contains("PlaylistFixture/1.0") && item.Headers.Contains("Referer: https://caller.example/", StringComparison.OrdinalIgnoreCase)), "The shared job session changed caller headers.");
        Assert(requests.Where(item => item.Path.StartsWith("/item/") || item.Path == "/list").All(item => !item.Headers.Contains("X-Entry", StringComparison.OrdinalIgnoreCase)), "One entry's playback-only header leaked into another discovery request.");
        foreach (var item in requests.Where(item => item.Path.StartsWith("/media/")))
            Assert(item.Headers.Contains("X-Entry: " + item.Path[^1], StringComparison.OrdinalIgnoreCase), "The selected media request lost its scoped playback header.");
        if (mode is "normal" or "snapshot" or "failure")
            Assert(requests.Single(item => item.Path == "/item/2").Headers.Contains("stage=" + (mode == "failure" ? "failed" : "media"), StringComparison.Ordinal), "Cookies returned by an earlier media request were lost.");
    }
    private sealed class Provider(Uri root, bool snapshot, bool binding = false) : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("session-fixture", "Session fixture", 0);
        public bool CanHandle(Uri url) => url.Host == root.Host && url.Port == root.Port;
        public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => request.Url.AbsolutePath.StartsWith("/item/") ? new("session-fixture", request.Url.AbsolutePath[^1..]) : null;
        public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
        {
            await http.GetStringAsync(request.Url, 128, ct);
            if (request.Url.AbsolutePath != "/list") return Video(request.Url.AbsolutePath[^1..]);
            return new() { Id = "collection", Extractor = Info.Id, Title = "Collection", IsPlaylist = true, Entries =
                [new(1, "1", "First", new(root, "item/1"), null, null, true, snapshot ? Video(binding ? "wrong" : "1") : null, binding ? new("session-fixture", "1") : null),
                 new(2, "2", "Second", new(root, "item/2"), null, null, true, ExpectedArchiveIdentity: binding ? new("session-fixture", "2") : null)] };
        }
        private PageMetadata Video(string id) => new() { Id = id, Extractor = Info.Id, Title = "Video " + id,
            Media = [new() { Id = "file", Url = new(root, "media/" + id), Kind = MediaKind.Progressive, Extension = "mp4", HasVideo = true, HasAudio = true }],
            PlaybackHeaders = new Dictionary<string, string> { ["X-Entry"] = id } };
    }
    private sealed record Seen(string Path, string Headers);
    private sealed class Server : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(10));
        private readonly List<Seen> _requests = [];
        private readonly Task _run;
        private readonly string _mode; private readonly Action? _cancel;
        private bool _canceled;
        internal Uri Root { get; }
        internal Seen[] Requests { get { lock (_requests) return _requests.ToArray(); } }
        internal Server(string mode, Action? cancel)
        {
            _mode = mode; _cancel = cancel; _listener.Start(); Root = new("http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/"); _run = Serve();
        }
        private async Task Serve()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token); await using var stream = client.GetStream();
                    var bytes = new List<byte>(); var buffer = new byte[1];
                    while (bytes.Count < 16384)
                    {
                        if (await stream.ReadAsync(buffer, _stop.Token) == 0) throw new IOException("Incomplete fixture request.");
                        bytes.Add(buffer[0]); if (bytes.Count >= 4 && bytes[^4] == 13 && bytes[^3] == 10 && bytes[^2] == 13 && bytes[^1] == 10) break;
                    }
                    var headers = Encoding.ASCII.GetString(bytes.ToArray()); var path = headers.Split(' ')[1]; lock (_requests) _requests.Add(new(path, headers));
                    if (_mode == "cancel" && path == "/item/2" && !_canceled) { _canceled = true; _cancel!(); continue; }
                    var status = path != "/list" && !headers.Contains("seed=fixture", StringComparison.Ordinal) ? "403 Forbidden" : "200 OK";
                    var cookie = path switch { "/list" => "seed=fixture", "/item/1" => "stage=one", "/media/1" => _mode == "failure" ? "stage=failed" : "stage=media", "/item/2" => "stage=two", _ => null };
                    if (_mode == "failure" && path == "/media/1") status = "403 Forbidden";
                    var body = path.StartsWith("/media/") ? "media-" + path[^1] : "OK";
                    var response = "HTTP/1.1 " + status + "\r\nContent-Length: " + Encoding.ASCII.GetByteCount(body) + "\r\nConnection: close\r\n" +
                        (cookie is null ? "" : "Set-Cookie: " + cookie + "; Path=/; HttpOnly\r\n") + "\r\n" + body;
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
        }
        public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); try { await _run; } finally { _stop.Dispose(); } }
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() { var path = Path.GetFullPath(Directory); if (!path.StartsWith(Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Fixture escaped its root."); System.IO.Directory.Delete(path, true); }
    }
}
