using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Rtve;

internal static partial class RtveChecks
{
    private sealed record ProxyRequest(string Authority, string Target, IReadOnlyDictionary<string, string> Headers);
    private sealed class TlsProxy : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0); private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(30));
        private readonly ConcurrentBag<Task> _clients = []; private readonly X509Certificate2 _certificate; private readonly Task _loop;
        internal ConcurrentQueue<ProxyRequest> Requests { get; } = []; internal string Address { get; }
        internal TlsProxy(Func<ProxyRequest, (int Status, byte[] Body)> serve)
        {
            using var key = RSA.Create(2048); var cert = new CertificateRequest("CN=api.rtve.es", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var generated = cert.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            _certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.DefaultKeySet);
            _listener.Start(); Address = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
            _loop = Task.Run(async () => { try { while (!_stop.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_stop.Token); _clients.Add(Handle(client)); } } catch (OperationCanceledException) when (_stop.IsCancellationRequested) { } });
            async Task Handle(TcpClient client)
            {
                using (client)
                {
                    try
                    {
                        var network = client.GetStream(); string connect;
                        using (var reader = new StreamReader(network, Encoding.ASCII, false, 1024, true)) { connect = await reader.ReadLineAsync(_stop.Token) ?? ""; while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 }) { } }
                        Assert(connect.StartsWith("CONNECT ", StringComparison.Ordinal), "Verification did not use an HTTPS proxy tunnel.");
                        await network.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), _stop.Token);
                        await using var tls = new SslStream(network, true); await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, _stop.Token);
                        using var input = new StreamReader(tls, Encoding.UTF8, false, 1024, true); var first = await input.ReadLineAsync(_stop.Token) ?? ""; var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        while (await input.ReadLineAsync(_stop.Token) is { Length: > 0 } line) { var colon = line.IndexOf(':'); headers[line[..colon]] = line[(colon + 1)..].Trim(); }
                        var request = new ProxyRequest(connect.Split(' ')[1], first.Split(' ')[1], headers); Requests.Enqueue(request); var response = serve(request);
                        await tls.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 " + response.Status + " Fixture\r\nContent-Length: " + response.Body.Length + "\r\nConnection: close\r\n\r\n"), _stop.Token); await tls.WriteAsync(response.Body, _stop.Token);
                    }
                    catch (Exception ex) when (ex is AuthenticationException or IOException || ex is OperationCanceledException && _stop.IsCancellationRequested) { }
                }
            }
        }
        public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); await _loop; await Task.WhenAll(_clients.ToArray()); _certificate.Dispose(); _stop.Dispose(); }
    }
    private static async Task Proxy(bool strict, bool curl = false, bool denied = false)
    {
        using var files = new ExecSponsorChecks.Files(); var data = "complete owned proxy media"u8.ToArray(); var grants = 0; var fixture = new Fixture { Media = data };
        await using var verification = new TlsProxy(_ =>
        {
            var generation = Interlocked.Increment(ref grants); var row = fixture.Audio();
            row["qualities"] = new[] { new { identifier = 9, filePath = "https://ztnr.rtve.es/ztnr/41.mp3?generation=" + generation, filesize = data.Length, duration = 3000, type = "audio/mpeg", bitRate = 128000 } };
            return (200, JsonSerializer.SerializeToUtf8Bytes(Fixture.Envelope([row])));
        });
        await using var media = new TlsProxy(request => denied || request.Target.Contains("generation=1", StringComparison.Ordinal) ? (403, System.Array.Empty<byte>()) : (200, data));
        var cookies = new CookieContainer(); cookies.Add(new Uri("https://api.rtve.es"), new Cookie("private", "owned"));
        var options = new MediaHttpOptions { Proxy = media.Address, CookieContainer = cookies, NoCheckCertificate = !strict }; options.Headers["Authorization"] = "Bearer owned"; options.Headers["X-Fixture-Secret"] = "owned"; options.Referer = "https://source.example/private";
        using var http = new MediaHttp(options); var request = Request(files.Directory) with { GeoVerification = new() { Proxy = verification.Address } };
        if (strict) { await Expect<HttpRequestException>(() => new RtveProvider().ExtractAsync(request, http, default)); Assert(verification.Requests.IsEmpty && media.Requests.IsEmpty, "A strict session accepted the owned untrusted certificate."); return; }
        var page = await new RtveProvider().ExtractAsync(request, http, default);
        using var anonymous = http.CreateAnonymousSession(); using var playback = anonymous.WithAuthorization(page.PlaybackAuthorization!);
        var output = Path.Combine(files.Directory, "media.mp3"); var transfer = new FileDownloader(playback) { ExternalDownloader = curl ? "curl" : null };
        if (denied) { await Expect<IOException>(() => transfer.DownloadAsync(page.Media.Single().Url, output, null, null, default, 0)); Assert(!File.Exists(output), "A verification grant hid the media server's actual denial."); }
        else { await transfer.DownloadAsync(page.Media.Single().Url, output, null, null, default, 0); Assert(File.ReadAllBytes(output).SequenceEqual(data), "The actual verification proxy changed media bytes."); }
        Assert(grants == 2, "Playback token renewal was not bounded.");
        Assert(verification.Requests.Count == 2 && verification.Requests.All(item => item.Authority == "api.rtve.es:443" && !item.Headers.ContainsKey("Authorization") && !item.Headers.ContainsKey("Cookie") && !item.Headers.ContainsKey("X-Fixture-Secret") && !item.Headers.ContainsKey("Referer")), "Source credentials or media escaped into verification requests.");
        Assert(media.Requests.Count == 2 && media.Requests.All(item => item.Authority == "ztnr.rtve.es:443"), "Verification proxy replaced the normal media connection.");
    }
}
