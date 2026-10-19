using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

internal static partial class ProviderFamiliesChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> NetworkChecks => new[] { "tvp", "arte", "orf" }.SelectMany(family => new[] { false, true }.Select(denied => ("Provider families proxy " + family + (denied ? " denied" : " renewed"), (Func<Task>)(() => Proxy(family, denied)))));
    private sealed record ProxyRequest(string Authority, string Target, IReadOnlyDictionary<string, string> Headers);
    private sealed class OwnedProxy : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0); private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(30));
        private readonly ConcurrentBag<Task> _clients = []; private readonly X509Certificate2 _certificate; private readonly Task _loop;
        internal ConcurrentQueue<ProxyRequest> Requests { get; } = []; internal string Address { get; }
        internal OwnedProxy(Func<ProxyRequest, (int Status, byte[] Body)> respond)
        {
            using var key = RSA.Create(2048); var request = new CertificateRequest("CN=owned-provider-test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1)); _certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.DefaultKeySet);
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
                        Assert(connect.StartsWith("CONNECT ", StringComparison.Ordinal), "A public provider did not use an HTTPS proxy tunnel."); await network.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), _stop.Token);
                        await using var tls = new SslStream(network, true); await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, _stop.Token);
                        using var input = new StreamReader(tls, Encoding.UTF8, false, 1024, true); var first = await input.ReadLineAsync(_stop.Token) ?? ""; var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        while (await input.ReadLineAsync(_stop.Token) is { Length: > 0 } line) { var colon = line.IndexOf(':'); headers[line[..colon]] = line[(colon + 1)..].Trim(); }
                        var item = new ProxyRequest(connect.Split(' ')[1], first.Split(' ')[1], headers); Requests.Enqueue(item); var response = respond(item);
                        await tls.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 " + response.Status + " Fixture\r\nContent-Length: " + response.Body.Length + "\r\nConnection: close\r\n\r\n"), _stop.Token); await tls.WriteAsync(response.Body, _stop.Token);
                    }
                    catch (Exception ex) when (ex is IOException or AuthenticationException || ex is OperationCanceledException && _stop.IsCancellationRequested) { }
                }
            }
        }
        public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); await _loop; await Task.WhenAll(_clients.ToArray()); _certificate.Dispose(); _stop.Dispose(); }
    }
    private static async Task Proxy(string family, bool denied)
    {
        using var files = new ExecSponsorChecks.Files(); var fixture = new Fixture(family);
        await using var verification = new OwnedProxy(request => (200, Encoding.UTF8.GetBytes(fixture.Response(new Uri("https://" + request.Authority + request.Target)).ToJsonString())));
        await using var media = new OwnedProxy(request => denied || request.Target.Contains("generation=1", StringComparison.Ordinal) ? (403, Array.Empty<byte>()) : (200, fixture.Data));
        var cookies = new CookieContainer(); cookies.Add(new Uri("https://" + fixture.ApiHost), new Cookie("private", "owned"));
        var options = new MediaHttpOptions { Proxy = media.Address, CookieContainer = cookies, NoCheckCertificate = true }; options.Headers["Authorization"] = "Bearer owned"; options.Headers["X-Fixture-Secret"] = "owned"; options.Referer = "https://source.example/private";
        using var http = new MediaHttp(options); var request = Request(fixture, files.Directory) with { GeoVerification = new() { Proxy = verification.Address } }; var page = await fixture.Provider.ExtractAsync(request, http, default);
        using var anonymous = http.CreateAnonymousSession(); using var playback = anonymous.WithAuthorization(page.PlaybackAuthorization!); var output = Path.Combine(files.Directory, "body.bin");
        if (denied) { await Expect<IOException>(() => new FileDownloader(playback).DownloadAsync(page.Media[0].Url, output, null, null, default, 0)); Assert(!File.Exists(output), "A verification response hid an actual media denial."); }
        else { await new FileDownloader(playback).DownloadAsync(page.Media[0].Url, output, null, null, default, 0); Assert(File.ReadAllBytes(output).SequenceEqual(fixture.Data), "A public playback renewal changed media bytes."); }
        Assert(fixture.Generation == 2 && media.Requests.Count == 2 && media.Requests.All(item => item.Authority == fixture.MediaHost + ":443"), "Public media renewal or ordinary proxy routing changed.");
        Assert(verification.Requests.All(item => item.Authority == fixture.ApiHost + ":443" && !item.Headers.Keys.Any(name => name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) || name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) || name.Equals("Referer", StringComparison.OrdinalIgnoreCase) || name.Equals("X-Fixture-Secret", StringComparison.OrdinalIgnoreCase))), "Source credentials or media traffic entered a verification API route.");
    }
}
