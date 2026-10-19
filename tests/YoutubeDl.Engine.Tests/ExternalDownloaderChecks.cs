using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class ExternalDownloaderChecks
{
    internal sealed class Files : IDisposable
    {
        internal string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", "curl-" + Guid.NewGuid().ToString("N"));
        internal string Output => Path.Combine(Directory, "file.bin");
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
    internal sealed record Request(string Target, IReadOnlyDictionary<string, string> Headers);
    internal sealed class Server : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(30));
        private readonly ConcurrentBag<Task> _clients = [];
        private readonly Task _loop;
        internal Uri Url { get; }
        internal ConcurrentQueue<Request> Requests { get; } = [];
        internal Server(Func<Request, NetworkStream, CancellationToken, Task> serve)
        {
            _listener.Start(); Url = new("http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/media");
            _loop = Task.Run(async () =>
            {
                try { while (!_stop.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_stop.Token); _clients.Add(Handle(client)); } }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            });
            async Task Handle(TcpClient client)
            {
                using (client)
                {
                    try
                    {
                        var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                        var first = await reader.ReadLineAsync(_stop.Token) ?? throw new IOException("Missing request.");
                        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        for (var n = 0; n < 100; n++) { var line = await reader.ReadLineAsync(_stop.Token); if (string.IsNullOrEmpty(line)) break; var colon = line.IndexOf(':'); headers[line[..colon]] = line[(colon + 1)..].Trim(); }
                        var request = new Request(first.Split(' ')[1], headers); Requests.Enqueue(request);
                        await serve(request, stream, _stop.Token);
                    }
                    catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException) { }
                }
            }
        }
        public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); await _loop; await Task.WhenAll(_clients.ToArray()); _stop.Dispose(); }
    }
    internal static async Task Reply(NetworkStream stream, byte[] bytes, CancellationToken ct, int status = 200, string headers = "", long? length = null)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " Fixture\r\nConnection: close\r\nContent-Length: " + (length ?? bytes.Length) + "\r\n" + headers + "\r\n"), ct);
        await stream.WriteAsync(bytes, ct); await stream.FlushAsync(ct);
    }
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
        new[] { "bytes", "headers", "chunked", "interim", "redirect", "foreign-redirect", "renewal", "resume", "changed", "bad-range", "416", "chunks", "chunk-resume", "chunk-fallback", "no-part", "no-continue", "maximum", "minimum", "cancel", "deadline", "encoded", "oversized-headers", "proxy" }
        .Select(mode => ("External downloader transport " + mode, (Func<Task>)(() => Transfer(mode))))
        .Concat(new[] { "ffmpeg", "aria2c", "http:curl", "curl --silent", "./curl.exe", "C:\\missing\\other.exe" }.Select((choice, i) => ("External downloader backend refusal " + i, (Func<Task>)(() => { Reject(() => NativeExternalDownloader.Validate(choice, null)); return Task.CompletedTask; }))))
        .Concat(new[] { "--output target", "--config file", "--header secret", "--url https://other.example", "--location-trusted", "--retry 3", "--max-time 0", "--connect-timeout NaN", "--speed-time 0.5", "--limit-rate 0", "--http1.1=1", "curl:", "--max-time 'unterminated" }
            .Select((args, i) => ("External downloader argument refusal " + i, (Func<Task>)(() => { Reject(() => NativeExternalDownloader.Validate("curl", [args])); return Task.CompletedTask; }))))
        .Concat(new[] { "hls", "dash", "sabr", "injected", "simulate", "no-overwrite", "archive", "missing" }.Select(mode => ("External downloader engine " + mode, (Func<Task>)(() => EngineCase(mode)))))
        .Concat(new[] { ("External downloader typed aliases and analysis", (Func<Task>)Aliases), ("External downloader default and recovery recipes", (Func<Task>)Defaults) });

    private static async Task Transfer(string mode)
    {
        using var files = new Files(); var data = Enumerable.Range(0, mode == "cancel" ? 262144 : 12345).Select(i => (byte)(i * 23)).ToArray(); var calls = 0;
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var foreign = new Server((_, stream, ct) => Reply(stream, data, ct));
        await using var server = new Server(async (request, stream, ct) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (mode == "foreign-redirect") { await Reply(stream, [], ct, 302, "Location: " + foreign.Url + "\r\n"); return; }
            if (mode == "redirect" && request.Target == "/media") { await Reply(stream, [], ct, 302, "Location: /final\r\nSet-Cookie: issued=yes; Path=/\r\n"); return; }
            if (mode == "renewal" && request.Target.Contains("old", StringComparison.Ordinal)) { await Reply(stream, [], ct, 403); return; }
            if (mode == "deadline") { reached.TrySetResult(); await Task.Delay(TimeSpan.FromSeconds(10), ct); return; }
            if (mode == "chunked") { await stream.WriteAsync("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n3039\r\n"u8.ToArray(), ct); await stream.WriteAsync(data, ct); await stream.WriteAsync("\r\n0\r\n\r\n"u8.ToArray(), ct); return; }
            if (mode == "interim") await stream.WriteAsync("HTTP/1.1 103 Early Hints\r\nLink: </hint>\r\n\r\n"u8.ToArray(), ct);
            if (mode == "oversized-headers") { await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nX-Large: " + new string('a', 65536) + "\r\n\r\n"), ct); return; }
            if (mode is "resume" or "changed" or "bad-range" or "416" or "no-part" or "no-continue" or "chunk-resume" && call == 1)
            {
                await Reply(stream, data[..4000], ct, 200, "ETag: \"first\"\r\n", data.Length); return;
            }
            if (mode == "cancel")
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: " + data.Length + "\r\nETag: \"first\"\r\n\r\n"), ct);
                await stream.WriteAsync(data.AsMemory(0, 65536), ct); await stream.FlushAsync(ct); reached.TrySetResult(); await Task.Delay(TimeSpan.FromSeconds(10), ct); return;
            }
            if (request.Headers.TryGetValue("Range", out var range))
            {
                if (mode == "416") { await Reply(stream, [], ct, 416, "Content-Range: bytes */12345\r\n"); return; }
                if (mode is "changed" or "no-part") { await Reply(stream, data, ct, 200, "ETag: \"changed\"\r\n"); return; }
                var parts = range[6..].Split('-'); var offset = int.Parse(parts[0]); var last = parts[1].Length == 0 ? data.Length - 1 : Math.Min(int.Parse(parts[1]), data.Length - 1);
                if (mode == "bad-range") offset = 0;
                await Reply(stream, data[offset..(last + 1)], ct, 206, "Content-Range: bytes " + offset + "-" + last + "/" + data.Length + "\r\n" + (mode == "chunk-fallback" ? "" : "ETag: \"first\"\r\n")); return;
            }
            await Reply(stream, data, ct, 200, "ETag: \"first\"\r\nLast-Modified: Wed, 01 Jan 2020 00:00:00 GMT\r\n" + (mode == "encoded" ? "Content-Encoding: gzip\r\n" : ""));
        });
        var jar = new CookieContainer(); jar.Add(server.Url, new Cookie("test", "owned", "/"));
        var options = new MediaHttpOptions { CookieContainer = jar, Proxy = mode == "proxy" ? server.Url.GetLeftPart(UriPartial.Authority) : "" };
        if (mode is "headers" or "foreign-redirect") { options.Headers["X-Fixture-Secret"] = "owned-secret"; options.Headers["Authorization"] = "Bearer owned"; options.Referer = "https://fixture.example/private?token=owned"; }
        using var http = new MediaHttp(options); var authorization = new Lease(server.Url);
        using var authorized = mode == "renewal" ? http.WithAuthorization(authorization) : null;
        var downloader = new FileDownloader(authorized ?? http)
        {
            ExternalDownloader = "curl", ExternalDownloaderArguments = mode == "deadline" ? ["curl:--max-time 0.1"] : ["--http1.1 --tcp-nodelay --connect-timeout=3"],
            NoPart = mode == "no-part", NoContinue = mode == "no-continue", HttpChunkSize = mode.StartsWith("chunk", StringComparison.Ordinal) && mode != "chunked" ? "4096" : "",
            MaximumBytes = mode == "maximum" ? 100 : null, MinimumBytes = mode == "minimum" ? 99999 : null, NoMtime = true
        };
        var url = mode == "proxy" ? new Uri("http://native-curl-fixture.invalid/selected") : server.Url;
        if (mode == "no-part") await File.WriteAllTextAsync(files.Output, "prior output");
        if (mode is "resume" or "changed" or "bad-range" or "416" or "no-part" or "no-continue" or "chunk-resume")
        {
            await Expect<IOException>(() => downloader.DownloadAsync(url, files.Output, null, null, default, 0));
            Assert(mode == "no-part" ? File.ReadAllText(files.Output) == "prior output" : !File.Exists(files.Output), "An incomplete curl transfer replaced the prior result.");
        }
        if (mode == "cancel")
        {
            using var stop = new CancellationTokenSource(); var operation = downloader.DownloadAsync(url, files.Output, null, null, stop.Token, 0);
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(5)); stop.Cancel(); await Expect<OperationCanceledException>(() => operation);
            Assert(!File.Exists(files.Output), "Cancellation published media."); return;
        }
        if (mode is "maximum" or "minimum") { await Expect<NativeFileSizeException>(() => downloader.DownloadAsync(url, files.Output, null, null, default, 0)); Assert(!File.Exists(files.Output), "Invalid file size was published."); return; }
        if (mode is "deadline" or "encoded" or "oversized-headers") { await Expect<Exception>(() => downloader.DownloadAsync(url, files.Output, null, null, default, 0)); Assert(!File.Exists(files.Output), "Invalid external response was published."); return; }
        await downloader.DownloadAsync(url, files.Output, new ByteRateLimiter(null), null, default, 0);
        Assert(SHA256.HashData(await File.ReadAllBytesAsync(files.Output)).SequenceEqual(SHA256.HashData(data)), "curl changed or lost media bytes.");
        var requests = server.Requests.ToArray();
        if (mode == "headers") Assert(requests.Single().Headers["X-Fixture-Secret"] == "owned-secret" && requests[0].Headers["Authorization"] == "Bearer owned" && requests[0].Headers["Cookie"].Contains("test=owned", StringComparison.Ordinal), "Configured request headers/cookies were not transferred.");
        if (mode == "redirect") Assert(requests.Length == 2 && requests[1].Headers["Cookie"].Contains("issued=yes", StringComparison.Ordinal), "Redirect cookies did not enter the native jar.");
        if (mode == "foreign-redirect") Assert(!foreign.Requests.Single().Headers.ContainsKey("X-Fixture-Secret") && !foreign.Requests.Single().Headers.ContainsKey("Authorization") && !foreign.Requests.Single().Headers.ContainsKey("Referer"), "Credentials crossed a redirect authority.");
        if (mode is "resume" or "bad-range" or "416") Assert(requests[1].Headers["Range"] == "bytes=4000-" && requests[1].Headers["If-Range"] == "\"first\"", "curl lost the native resume validator.");
        if (mode is "bad-range" or "416" or "chunk-fallback") Assert(!requests[^1].Headers.ContainsKey("Range"), "Invalid ranges did not fall back to a complete transfer.");
        if (mode == "no-continue") Assert(!requests[1].Headers.ContainsKey("Range"), "No-continue reused an interrupted file.");
        if (mode == "chunks") Assert(requests.Length == 4 && requests[1].Headers["Range"] == "bytes=4096-8191", "Native bounded ranges were not delegated.");
        if (mode == "renewal") Assert(authorization.Renewals == 1 && requests.Length == 2 && requests[1].Target == "/new", "curl bypassed native signed-link renewal.");
        if (mode == "proxy") Assert(requests.Single().Target == url.AbsoluteUri, "The selected media did not use its configured HTTP proxy.");
    }

    private sealed class Lease(Uri url) : IPlaybackAuthorization
    {
        internal int Renewals;
        public bool AppliesTo(Uri value) => value.Host == url.Host;
        public Task<PlaybackLease> AuthorizeAsync(Uri value, CancellationToken ct) => Task.FromResult(new PlaybackLease(new Uri(url, Renewals == 0 ? "/old" : "/new"), Renewals));
        public Task<PlaybackLease> RenewAsync(Uri value, long generation, CancellationToken ct) { Renewals++; return AuthorizeAsync(value, ct); }
        public Uri Canonicalize(Uri value) => url;
    }
    internal sealed class Provider(Uri media, MediaKind kind = MediaKind.Progressive, string? demuxer = null, IPlaybackAuthorization? authorization = null) : IMediaProvider
    {
        public MediaProviderInfo Info => new("curl-fixture", "Owned curl transport fixture", 999);
        public bool CanHandle(Uri url) => true;
        public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => new("curl-fixture", "selected");
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(new PageMetadata
        { Id = "selected", Extractor = "curl-fixture", Title = "selected", PlaybackAuthorization = authorization, DurationSeconds = 3,
            Media = [new() { Url = media, Kind = kind, Extension = "mp4", HasAudio = true, HasVideo = true, VerificationDemuxer = demuxer, VerificationDurationSeconds = demuxer is null ? null : 3 }] });
    }
    private static async Task EngineCase(string mode)
    {
        using var files = new Files(); await using var server = new Server((_, stream, ct) => Reply(stream, "complete native fixture"u8.ToArray(), ct));
        var kind = mode switch { "hls" => MediaKind.Hls, "dash" => MediaKind.Dash, "sabr" => MediaKind.Sabr, _ => MediaKind.Progressive };
        var engine = mode == "injected" ? new NativeEngine([new Provider(server.Url, kind)], _ => AdaptiveAccountChecks.Http((_, _) => throw new Exception("Injected handler was used."))) : new NativeEngine([new Provider(server.Url, kind)]);
        var request = new NativeDownloadRequest { Url = new("https://fixture.example/watch"), OutputDirectory = files.Directory, FileName = "output", ExternalDownloader = "curl", Retries = 0, NoMtime = true, Simulate = mode == "simulate", NoOverwrites = mode == "no-overwrite", DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") };
        var output = Path.Combine(files.Directory, "output.mp4");
        if (mode == "no-overwrite") await File.WriteAllTextAsync(output, "saved");
        if (mode == "archive") { await File.WriteAllTextAsync(output, "saved"); await File.WriteAllTextAsync(request.DownloadArchiveFile, "curl-fixture selected\n"); }
        if (mode == "missing") request = request with { ExternalDownloader = Path.Combine(files.Directory, OperatingSystem.IsWindows() ? "curl.exe" : "curl") };
        if (mode is "hls" or "dash" or "sabr" or "injected" or "missing")
        {
            try { var failed = await engine.DownloadAsync(request, null, null, default); Assert(!failed.Succeeded && failed.Error?.Contains("curl", StringComparison.OrdinalIgnoreCase) == true, "The selected backend was not explicitly refused."); }
            catch (Exception ex) when (ex is NotSupportedException or IOException) { Assert(ex.Message.Contains("curl", StringComparison.OrdinalIgnoreCase), "The rejection did not identify the selected backend."); }
        }
        else { var result = await engine.DownloadAsync(request, null, null, default); Assert(result.Succeeded && (mode == "simulate" ? result.Simulated : result.Skipped), "A protected/metadata-only job did not skip its transfer."); }
        Assert(server.Requests.IsEmpty, "A refused or protected job transferred media.");
    }
    private static Task Aliases()
    {
        var request = new NativeDownloadRequest { Url = new("https://fixture.example/watch"), OutputDirectory = "." };
        foreach (var prefix in new[] { "--downloader", "--external-downloader" })
        {
            var selected = NativeExtraArguments.Apply(request, [prefix, "curl", prefix + "-args", "curl:--max-time 12 --limit-rate 1K"]);
            Assert(selected.ExternalDownloader == "curl" && selected.ExternalDownloaderArguments!.Count == 1, "A downloader alias did not become a typed setting.");
            Assert(NativeAnalysisArguments.Select([prefix, "ignored-backend", prefix + "-args", "ignored-arguments"]).Count == 0, "Read-only analysis selected an external downloader.");
        }
        NativeExternalDownloader.Validate("native", null); NativeExternalDownloader.Validate("curl", ["--no-buffer --tcp-nodelay --no-keepalive --http1.1 --speed-time 3 --speed-limit 10"]);
        Reject(() => NativeExternalDownloader.Validate("native", ["--max-time 2"])); return Task.CompletedTask;
    }
    private static Task Defaults()
    {
        var request = new NativeDownloadRequest { Url = new("https://fixture.example/watch"), OutputDirectory = "." };
        Assert(!JsonSerializer.Serialize(request).Contains("ExternalDownloader", StringComparison.Ordinal), "Default request JSON changed.");
        Assert(DownloadWorkspace.Recipe(request) == DownloadWorkspace.Recipe(request with { ExternalDownloader = "curl", ExternalDownloaderArguments = ["--max-time 12"] }), "An operational backend choice invalidated media recovery.");
        return Task.CompletedTask;
    }
    internal static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected failure was not raised."); }
    private static void Reject(Action action) { try { action(); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return; } throw new Exception("An unsupported downloader setting was accepted."); }
}
