using System.Net;
using System.Net.Sockets;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class PlaybackHeaderChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
        [("Scoped playback headers preserve shared cookies without leaking headers to another item", Isolation)];
    private static async Task Isolation()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            var origin = new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/");
            var received = new List<string>(); var server = Serve();
            var engine = new NativeEngine([new Provider(origin)], _ => new MediaHttp(new MediaHttpOptions { UserAgent = "Parent/1.0", Referer = "https://parent.example/" }));
            var result = await engine.DownloadAsync(new NativeDownloadRequest { Url = new(origin, "list"), OutputDirectory = directory, Retries = 0 }, null, null, timeout.Token);
            await server;
            if (!result.Succeeded || result.OutputPaths.Count != 2 || received.Count != 3 || !received[1].Contains("User-Agent: Playback/1.0") ||
                !received[1].Contains("Referer: https://playback.example/") || !received[1].Contains("session=first") ||
                !received[2].Contains("User-Agent: Parent/1.0") || !received[2].Contains("Referer: https://parent.example/") || !received[2].Contains("session=second"))
                throw new InvalidOperationException("Header scopes leaked, lost cookies, or disposed the shared HTTP handler.");

            async Task Serve()
            {
                for (var index = 0; index < 3; index++)
                {
                    using var client = await listener.AcceptTcpClientAsync(timeout.Token); await using var stream = client.GetStream();
                    var request = new StringBuilder(); var buffer = new byte[1];
                    while (!request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                    { if (request.Length > 8192 || await stream.ReadAsync(buffer, timeout.Token) != 1) throw new IOException("Invalid fixture request."); request.Append((char)buffer[0]); }
                    received.Add(request.ToString());
                    var cookie = index == 0 ? "Set-Cookie: session=first; Path=/\r\n" : index == 1 ? "Set-Cookie: session=second; Path=/\r\n" : "";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 2\r\nContent-Type: audio/mpeg\r\n" + cookie + "\r\nOK"), timeout.Token);
                }
            }
        }
        finally
        {
            listener.Stop();
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(directory).StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Test folder escaped its root.");
            Directory.Delete(directory, true);
        }
    }
    private sealed class Provider(Uri origin) : IMediaProvider
    {
        public MediaProviderInfo Info => new("header-fixture", "Header fixture", 1000);
        public bool CanHandle(Uri url) => true;
        public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
        {
            _ = await http.GetStringAsync(request.Url, 32, ct);
            var entries = Enumerable.Range(1, 2).Select(index =>
            {
                var url = new Uri(origin, index + ".mp3");
                var page = new PageMetadata { Id = index.ToString(), Title = "Item " + index,
                    PlaybackHeaders = index == 1 ? new Dictionary<string, string> { ["User-Agent"] = "Playback/1.0", ["Referer"] = "https://playback.example/" } : new Dictionary<string, string>(),
                    Media = [new() { Url = url, Kind = MediaKind.Progressive, Extension = "mp3", MimeType = "audio/mpeg", HasAudio = true, HasVideo = false }] };
                return new NativePlaylistEntry(index, page.Id, page.Title, url, null, null, true, page);
            }).ToArray();
            return new PageMetadata { Id = "headers", Title = "Headers", IsPlaylist = true, Entries = entries };
        }
    }
}
