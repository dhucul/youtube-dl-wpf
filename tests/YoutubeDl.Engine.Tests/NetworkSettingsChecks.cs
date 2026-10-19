using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;

internal static class NetworkSettingsChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Native network IPv4 connections use the requested local source address", Source),
        ("Native network IPv6 selection uses IPv6 and never falls back to IPv4", IPv6),
        ("Native network DNS filtering honors forced IPv4 for localhost", DnsFamily),
        ("Native network HTTP proxy sockets retain source binding and target host", Proxy),
        ("Native network HTTPS proxy tunnels retain source binding and TLS preferences", Tunnel),
        ("Native network certificate override is explicit and does not change other sessions", Certificates),
        ("Native network copied scoped and anonymous sessions preserve connection preferences", Copies),
        ("Native network custom headers preserve values body types and case-insensitive replacement", Headers),
        ("Native network invalid settings and injected header lines fail before discovery", Invalid),
        ("Native network extra arguments retain ordered overrides and typed header precedence", Extras),
        ("Native network end-to-end download applies source binding and custom headers", Download),
        ("Native network canceled requests do not connect", Cancellation)
    ];
    private static async Task Source()
    {
        await using var server = new Server(IPAddress.Loopback);
        using var http = new MediaHttp(new MediaHttpOptions { SourceAddress = "127.0.0.2", IpVersion = 4 });
        Assert(await http.GetStringAsync(server.Url, 1024, default) == "native-network", "The response changed.");
        Assert(server.Requests.Single().Peer == "127.0.0.2", "The requested source address was ignored.");
    }
    private static async Task IPv6()
    {
        if (!Socket.OSSupportsIPv6) throw new InvalidOperationException("IPv6 verification requires an IPv6-capable test host.");
        await using var v6 = new Server(IPAddress.IPv6Loopback);
        using var http = new MediaHttp(new MediaHttpOptions { SourceAddress = "::1", IpVersion = 6 });
        await http.GetStringAsync(v6.Url, 1024, default);
        Assert(v6.Requests.Single().Peer == "::1", "The IPv6 source was changed.");
        await using var v4 = new Server(IPAddress.Loopback);
        await Expect<HttpRequestException>(() => http.GetStringAsync(v4.Url, 1024, default));
        Assert(v4.Requests.Count == 0, "Forced IPv6 silently fell back to IPv4.");
    }
    private static async Task DnsFamily()
    {
        await using var server = new Server(IPAddress.Loopback);
        using var http = new MediaHttp(new MediaHttpOptions { IpVersion = 4 });
        await http.GetStringAsync(new UriBuilder(server.Url) { Host = "localhost" }.Uri, 1024, default);
        Assert(server.Requests.Single().Peer == "127.0.0.1", "DNS family selection changed.");
    }
    private static async Task Proxy()
    {
        await using var proxy = new Server(IPAddress.Loopback);
        using var http = new MediaHttp(new MediaHttpOptions { Proxy = proxy.Url.GetLeftPart(UriPartial.Authority), SourceAddress = "127.0.0.2", IpVersion = 4 });
        await http.GetStringAsync(new("http://native-proxy-target.invalid/media"), 1024, default);
        var request = proxy.Requests.Single();
        Assert(request.Peer == "127.0.0.2" && request.Line == "GET http://native-proxy-target.invalid/media HTTP/1.1" && request.Headers["Host"] == "native-proxy-target.invalid", "Source binding or proxy routing lost the requested destination.");
    }
    private static async Task Certificates()
    {
        await using var server = new Server(IPAddress.Loopback, tls: true);
        using (var strict = new MediaHttp()) await Expect<HttpRequestException>(() => strict.GetStringAsync(server.Url, 1024, default));
        using (var allowed = new MediaHttp(new MediaHttpOptions { NoCheckCertificate = true, SourceAddress = "127.0.0.2", IpVersion = 4 }))
        {
            try { Assert(await allowed.GetStringAsync(server.Url, 1024, default) == "native-network", "Explicit certificate override failed."); }
            catch (HttpRequestException) { throw new InvalidOperationException("Local TLS server failed: " + string.Join("; ", server.Errors)); }
        }
        using (var strictAgain = new MediaHttp()) await Expect<HttpRequestException>(() => strictAgain.GetStringAsync(server.Url, 1024, default));
        Assert(server.Requests.Count == 1 && server.Requests.Single().Peer == "127.0.0.2", "Certificate override leaked into other sessions.");
    }
    private static async Task Tunnel()
    {
        await using var proxy = new Server(IPAddress.Loopback, tls: true, tunnel: true);
        using var http = new MediaHttp(new MediaHttpOptions { Proxy = proxy.Url.GetLeftPart(UriPartial.Authority), SourceAddress = "127.0.0.2", IpVersion = 4, NoCheckCertificate = true });
        Assert(await http.GetStringAsync(new("https://native-tunnel.invalid/media"), 1024, default) == "native-network", "The HTTPS proxy response changed.");
        var requests = proxy.Requests.ToArray();
        Assert(requests.Length == 2 && requests.All(request => request.Peer == "127.0.0.2") && requests[0].Line == "CONNECT native-tunnel.invalid:443 HTTP/1.1" &&
            requests[1].Line == "GET /media HTTP/1.1" && requests[1].Headers["Host"] == "native-tunnel.invalid", "Proxy TLS tunnel routing or source binding changed.");
    }
    private static async Task Copies()
    {
        await using var server = new Server(IPAddress.Loopback, tls: true);
        var options = new MediaHttpOptions { NoCheckCertificate = true, SourceAddress = "127.0.0.2", IpVersion = 4 };
        options.Headers["Authorization"] = "Bearer fixture"; options.Headers["Cookie"] = "fixture=secret";
        using var http = new MediaHttp(options);
        using (var scoped = http.WithHeaders(new Dictionary<string, string> { ["X-Scoped"] = "yes" })) await scoped.GetStringAsync(server.Url, 1024, default);
        using (var anonymous = http.CreateAnonymousSession()) await anonymous.GetStringAsync(server.Url, 1024, default);
        using (var redirects = http.CreateWithoutRedirects()) await redirects.GetStringAsync(server.Url, 1024, default);
        var requests = server.Requests.ToArray();
        Assert(requests.Length == 3 && requests.All(request => request.Peer == "127.0.0.2"), "Copied sessions lost connection or TLS preferences.");
        Assert(requests[0].Headers["X-Scoped"] == "yes" && !requests[1].Headers.ContainsKey("Authorization") && !requests[1].Headers.ContainsKey("Cookie") && !requests[2].Headers.ContainsKey("X-Scoped"), "Scoped headers or anonymous isolation leaked.");
    }
    private static async Task Headers()
    {
        await using var server = new Server(IPAddress.Loopback);
        var options = new MediaHttpOptions { UserAgent = "DefaultFixture/1" };
        foreach (var pair in NativeHttpHeaders.ParseLines("X-Value: original\r\nx-value: A:B %2F\r\nUser-Agent: CustomFixture/2\r\nContent-Type: application/x-native\r\nX-Empty:")) options.Headers[pair.Key] = pair.Value;
        using var http = new MediaHttp(options);
        using var request = new HttpRequestMessage(HttpMethod.Post, server.Url) { Content = new StringContent("payload") };
        using var response = await http.SendAsync(request, default); response.EnsureSuccessStatusCode();
        var captured = server.Requests.Single();
        Assert(captured.Headers["X-Value"] == "A:B %2F" && captured.Headers["User-Agent"] == "CustomFixture/2" && captured.Headers["Content-Type"] == "application/x-native" && captured.Body == "payload", "Custom headers appended, decoded or changed the body.");
        Assert(captured.Headers.ContainsKey("X-Empty"), "An explicitly empty header disappeared.");
    }
    private static async Task Invalid()
    {
        var calls = 0; var engine = new NativeEngine(httpFactory: _ => { calls++; throw new InvalidOperationException("Unexpected discovery."); });
        var request = new NativeDownloadRequest { Url = new("https://example.test/video.mp4"), OutputDirectory = Path.GetTempPath() };
        foreach (var invalid in new[] { request with { SourceAddress = "example.test" }, request with { IpVersion = 5 }, request with { SourceAddress = "127.0.0.1", IpVersion = 6 },
            request with { HttpHeaders = new Dictionary<string, string> { ["X-Value"] = "secret\r\nInjected: true" } } })
            await Expect<ArgumentException>(() => engine.InspectAsync(invalid, default));
        foreach (var header in new[] { "not-a-pair", "Bad Name: secret", "X: secret\0value" })
            await Expect<ArgumentException>(() => Task.FromResult(NativeHttpHeaders.ParseLines(header)));
        await Expect<NotSupportedException>(() => Task.FromResult(NativeHttpHeaders.ParseLines("Content-Length: 999")));
        Assert(calls == 0, "Invalid network preferences reached discovery.");
    }
    private static Task Extras()
    {
        var request = new NativeDownloadRequest { Url = new("https://example.test/video.mp4"), OutputDirectory = Path.GetTempPath(),
            HttpHeaders = NativeHttpHeaders.ParseLines("User-Agent: Saved/1\nX-Job: saved") };
        var result = NativeExtraArguments.Apply(request, ["--source-address", "::1", "-4", "--force-ipv6", "--no-check-certificates", "--check-certificates", "--add-headers", "X-Job: override: value", "--user-agent", "Job/2", "--add-header", "Referer: https://caller.example/", "--socket-timeout", "12"]);
        Assert(result.SourceAddress == "::1" && result.IpVersion == 6 && !result.NoCheckCertificate && result.UserAgent == "Job/2" && result.Referer == "https://caller.example/" && result.HttpHeaders["X-Job"] == "override: value" && result.TimeoutSeconds == 12, "Network argument precedence changed.");
        Assert(request.HttpHeaders["X-Job"] == "saved", "Header overrides mutated the saved request.");
        return Task.CompletedTask;
    }
    private static async Task Download()
    {
        await using var server = new Server(IPAddress.Loopback);
        var directory = Path.Combine(Path.GetTempPath(), "native-network-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try
        {
            var done = new List<string>();
            var result = await new NativeEngine().DownloadAsync(new NativeDownloadRequest { Url = new(server.Url, "/video.mp4"), OutputDirectory = directory, FileName = "network",
                SourceAddress = "127.0.0.2", IpVersion = 4, HttpHeaders = NativeHttpHeaders.ParseLines("X-Native: request-value\nUser-Agent: NativeTransport/1"), Retries = 0 }, null, null, default, done.Add);
            Assert(result.Succeeded && done.Count == 1 && await File.ReadAllTextAsync(result.OutputPath!) == "native-network", "End-to-end download did not preserve transferred bytes.");
            Assert(server.Requests.All(request => request.Peer == "127.0.0.2" && request.Headers["X-Native"] == "request-value" && request.Headers["User-Agent"] == "NativeTransport/1"), "Typed network settings did not reach the real transfer.");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task Cancellation()
    {
        await using var server = new Server(IPAddress.Loopback); using var stop = new CancellationTokenSource(); stop.Cancel();
        using var http = new MediaHttp(new MediaHttpOptions { IpVersion = 4, SourceAddress = "127.0.0.2" });
        await Expect<OperationCanceledException>(() => http.GetStringAsync(server.Url, 1024, stop.Token));
        Assert(server.Requests.Count == 0, "A canceled request reached the server.");
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed record Captured(string Peer, string Line, Dictionary<string, string> Headers, string Body);
    private sealed class Server : IAsyncDisposable
    {
        private readonly TcpListener _listener; private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(20)); private readonly Task _loop;
        private readonly ConcurrentBag<Task> _clients = []; private readonly X509Certificate2? _certificate; private readonly bool _tunnel;
        internal ConcurrentQueue<Captured> Requests { get; } = new();
        internal ConcurrentQueue<string> Errors { get; } = new();
        internal Uri Url { get; }
        internal Server(IPAddress address, bool tls = false, bool tunnel = false)
        {
            _tunnel = tunnel;
            if (tls)
            {
                using var rsa = RSA.Create(2048); var certificate = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback); certificate.CertificateExtensions.Add(names.Build());
                using var generated = certificate.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
                // Schannel needs an imported key handle. No certificate is added to a trust store.
                _certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.DefaultKeySet);
            }
            _listener = new(address, 0); if (address.AddressFamily == AddressFamily.InterNetworkV6) _listener.Server.DualMode = false; _listener.Start();
            Url = new UriBuilder(tls && !tunnel ? "https" : "http", address.ToString(), ((IPEndPoint)_listener.LocalEndpoint).Port).Uri;
            _loop = Run();
        }
        private async Task Run()
        {
            try { while (!_stop.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_stop.Token); _clients.Add(Serve(client)); } }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
        }
        private async Task Serve(TcpClient client)
        {
            using (client)
            {
                var peer = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
                await using var network = client.GetStream();
                await using var tls = _certificate is null ? null : new SslStream(network, leaveInnerStreamOpen: true);
                try
                {
                    if (_tunnel)
                    {
                        using var tunnelReader = new StreamReader(network, Encoding.ASCII, false, 1024, true);
                        var connect = await tunnelReader.ReadLineAsync(_stop.Token) ?? "";
                        while (await tunnelReader.ReadLineAsync(_stop.Token) is { Length: > 0 }) { }
                        Requests.Enqueue(new(peer, connect, new(StringComparer.OrdinalIgnoreCase), ""));
                        await network.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n"), _stop.Token);
                    }
                    if (tls is not null) await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, _stop.Token);
                    var stream = (Stream?)tls ?? network; using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                    var line = await reader.ReadLineAsync(_stop.Token); if (line is null) return;
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 } header)
                    { var colon = header.IndexOf(':'); if (colon <= 0) throw new InvalidDataException("Invalid test HTTP header."); headers[header[..colon]] = header[(colon + 1)..].Trim(); }
                    var length = headers.TryGetValue("Content-Length", out var raw) ? int.Parse(raw) : 0; var body = new char[length]; var read = 0;
                    while (read < length) { var count = await reader.ReadAsync(body.AsMemory(read), _stop.Token); if (count == 0) break; read += count; }
                    Requests.Enqueue(new(peer, line, headers, new string(body)));
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 14\r\nConnection: close\r\n\r\nnative-network"), _stop.Token);
                }
                catch (Exception ex) when (ex is AuthenticationException or IOException || ex is OperationCanceledException && _stop.IsCancellationRequested)
                { Errors.Enqueue(ex.GetBaseException().Message); }
            }
        }
        public async ValueTask DisposeAsync()
        { _stop.Cancel(); _listener.Stop(); await _loop; await Task.WhenAll(_clients); _certificate?.Dispose(); _stop.Dispose(); }
    }
}
