using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using YoutubeDl.Engine;
using YoutubeDl.Engine.BrowserRuntime;

// Optional integration checks: actual WebView2, local servers, no public websites or user profiles.
internal static class BrowserTransportChecks
{
    private static NativeDownloadRequest Request => new() { Url = new("https://www.youtube.com/watch?v=abcdefghijk"), OutputDirectory = Path.GetTempPath() };
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Browser tools route local navigation through configured HTTP and HTTPS proxies", Proxy),
        ("Browser tools enforce a shared rate limit across simultaneous downloads", Rate),
        ("Browser tools certificate override remains isolated from strict sessions", Certificates),
        ("Browser tools cookie import preserves selected fields and isolates later profiles", Cookies)
    ];
    private static async Task Proxy()
    {
        foreach (var secure in new[] { false, true })
        {
            await using var server = new Server(tls: secure);
            await using var browser = await Browser.Create(Request with { Proxy = server.Url.GetLeftPart(UriPartial.Authority), NoCheckCertificate = secure });
            var unused = new TcpListener(IPAddress.Loopback, 0); unused.Start(); var port = ((IPEndPoint)unused.LocalEndpoint).Port; unused.Stop();
            var target = new Uri("http://127.0.0.1:" + port + "/proxy-target");
            Assert(await browser.Navigate(target), secure ? "HTTPS proxy navigation failed." : "HTTP proxy navigation failed.");
            Assert(server.Lines.Any(line => line == "GET " + target.AbsoluteUri + " HTTP/1.1"), "The browser bypassed the configured proxy for the destination.");
            if (secure)
            {
                await using var strict = await Browser.Create(Request with { Proxy = server.Url.GetLeftPart(UriPartial.Authority) });
                Assert(!await strict.Navigate(target), "An opted-in proxy session disabled certificate checks in another browser.");
            }
        }
    }
    private static async Task Rate()
    {
        await using var server = new Server();
        double baseline;
        await using (var browser = await Browser.Create(Request))
        { Assert(await browser.Navigate(server.Url), "The local page did not load."); baseline = await Transfer(browser); }
        await using (var browser = await Browser.Create(Request with { RateLimit = "128K" }))
        {
            Assert(await browser.Navigate(server.Url), "The throttled local page did not load."); var seconds = await Transfer(browser);
            Assert(seconds >= 3 && seconds >= baseline * 2, "Two simultaneous downloads did not share the requested 128 KiB/s cap.");
            Console.WriteLine($"Browser rate evidence: 524288 bytes, uncapped {baseline:F3}s, capped {seconds:F3}s.");
        }
        static async Task<double> Transfer(Browser browser)
        {
            var watch = Stopwatch.StartNew();
            await browser.Core.ExecuteScriptAsync("window.__nativeTransfer=null;Promise.all(['/one','/two'].map(p=>fetch(p+'?n='+Math.random(),{cache:'no-store'}).then(r=>r.arrayBuffer()))).then(a=>window.__nativeTransfer=a.map(x=>x.byteLength)).catch(()=>window.__nativeTransfer=[]);");
            while (watch.Elapsed < TimeSpan.FromSeconds(15))
            {
                using var json = JsonDocument.Parse(await browser.Core.ExecuteScriptAsync("window.__nativeTransfer"));
                if (json.RootElement.ValueKind == JsonValueKind.Array)
                {
                    Assert(json.RootElement.GetArrayLength() == 2 && json.RootElement.EnumerateArray().All(item => item.GetInt32() == 262144), "Browser transfer lost payload bytes.");
                    return watch.Elapsed.TotalSeconds;
                }
                await Task.Delay(50);
            }
            throw new TimeoutException("The browser rate fixture did not finish.");
        }
    }
    private static async Task Certificates()
    {
        await using var server = new Server(tls: true);
        await using (var strict = await Browser.Create(Request)) Assert(!await strict.Navigate(server.Url), "A strict browser trusted the self-signed fixture.");
        await using (var allowed = await Browser.Create(Request with { NoCheckCertificate = true })) Assert(await allowed.Navigate(server.Url), "The explicit per-session certificate override was ignored.");
        await using (var strict = await Browser.Create(Request)) Assert(!await strict.Navigate(server.Url), "The override leaked into another browser session.");
    }
    private static async Task Cookies()
    {
        var jar = new CookieContainer();
        jar.Add(new("https://www.youtube.com/"), new Cookie("host-fixture", "dummy-host", "/") { Secure = true, HttpOnly = true });
        jar.Add(new Cookie("domain-fixture", "dummy-domain", "/youtubei", ".youtube.com") { Secure = true, Expires = DateTime.UtcNow.AddHours(1) });
        jar.Add(new("https://www.youtube.com/"), new Cookie("empty-fixture", "", "/"));
        jar.Add(new("https://unrelated.example/"), new Cookie("unrelated", "dummy-unrelated", "/"));
        var snapshot = YoutubeDl.Engine.Providers.YouTube.YouTubePlaybackCookies.FromContainer(jar);
        await using (var browser = await Browser.Create(Request))
        {
            await BrowserCookieSettings.ImportAsync(browser.Core, snapshot, default);
            var read = await BrowserCookieSettings.ExportAsync(browser.Core, default);
            Assert(read.Count == 3 && read.Items.Single(cookie => cookie.Name == "host-fixture") is { IncludeSubdomains: false, Secure: true, HttpOnly: true, Expires: null } &&
                read.Items.Single(cookie => cookie.Name == "domain-fixture") is { IncludeSubdomains: true, Expires: not null } && read.Items.Single(cookie => cookie.Name == "empty-fixture").Value == "", "The real browser changed cookie scope or values.");
            var updated = browser.Core.CookieManager.CreateCookie("host-fixture", "dummy-updated", "www.youtube.com", "/"); updated.IsSecure = true; updated.IsHttpOnly = true; browser.Core.CookieManager.AddOrUpdateCookie(updated);
            var exported = await BrowserCookieSettings.ExportAsync(browser.Core, default);
            Assert(exported.Items.Single(cookie => cookie.Name == "host-fixture").Value == "dummy-updated", "Browser response changes were not available to the native transport.");
        }
        await using (var other = await Browser.Create(Request))
            Assert((await BrowserCookieSettings.ExportAsync(other.Core, default)).Count == 0, "Selected cookies leaked into another temporary profile.");
    }
    private sealed class Browser : IAsyncDisposable
    {
        private readonly string _profile = Path.Combine(Path.GetTempPath(), "browser-network-test-" + Guid.NewGuid().ToString("N"));
        private readonly WebView2 _view = new(); private readonly Window _window;
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CoreWebView2 Core => _view.CoreWebView2;
        private Browser() { _window = new Window { Width = 640, Height = 480, Opacity = 0, ShowInTaskbar = false, ShowActivated = false, Content = _view }; }
        internal static async Task<Browser> Create(NativeDownloadRequest request)
        {
            var browser = new Browser();
            try
            {
                Directory.CreateDirectory(browser._profile); browser._window.Show();
                var environment = await CoreWebView2Environment.CreateAsync(null, browser._profile, new CoreWebView2EnvironmentOptions(BrowserNetworkSettings.Arguments(request)));
                environment.BrowserProcessExited += (_, _) => browser._exited.TrySetResult();
                await browser._view.EnsureCoreWebView2Async(environment);
                browser.Core.IsMuted = true; browser.Core.Settings.AreDefaultScriptDialogsEnabled = false;
                await BrowserNetworkSettings.ApplyAsync(browser.Core, request, default);
                return browser;
            }
            catch { await browser.DisposeAsync(); throw; }
        }
        internal async Task<bool> Navigate(Uri url)
        {
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Handler(object? _, CoreWebView2NavigationCompletedEventArgs args) { if (Core.Source == url.AbsoluteUri) finished.TrySetResult(args.IsSuccess); }
            Core.NavigationCompleted += Handler;
            try { Core.Navigate(url.AbsoluteUri); return await finished.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { Core.NavigationCompleted -= Handler; }
        }
        public async ValueTask DisposeAsync()
        {
            _view.Dispose(); _window.Content = null; _window.Close();
            try { await _exited.Task.WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { }
            for (var attempt = 0; attempt < 8; attempt++)
            {
                try { Directory.Delete(_profile, true); return; }
                catch (DirectoryNotFoundException) { return; }
                catch (IOException) when (attempt < 7) { await Task.Delay(250); }
            }
        }
    }
    private sealed class Server : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0); private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(40));
        private readonly ConcurrentBag<Task> _clients = []; private readonly Task _loop; private readonly X509Certificate2? _certificate;
        internal ConcurrentQueue<string> Lines { get; } = new(); internal Uri Url { get; }
        internal Server(bool tls = false)
        {
            if (tls)
            {
                using var rsa = RSA.Create(2048); var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var names = new SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback); request.CertificateExtensions.Add(names.Build());
                using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
                _certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.DefaultKeySet);
            }
            _listener.Start(); Url = new UriBuilder(tls ? "https" : "http", "127.0.0.1", ((IPEndPoint)_listener.LocalEndpoint).Port).Uri; _loop = Run();
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
            await using (var network = client.GetStream())
            await using (var tls = _certificate is null ? null : new SslStream(network, true))
            {
                try
                {
                    if (tls is not null) await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, _stop.Token);
                    var stream = (Stream?)tls ?? network; using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                    var line = await reader.ReadLineAsync(_stop.Token); if (line is null) return; Lines.Enqueue(line);
                    while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 }) { }
                    var body = line.Contains("/one?") || line.Contains("/two?") ? new byte[262144] : Encoding.UTF8.GetBytes("<html><body>Local browser transport fixture</body></html>");
                    var type = body.Length == 262144 ? "application/octet-stream" : "text/html";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n"), _stop.Token);
                    await stream.WriteAsync(body, _stop.Token);
                }
                catch (Exception ex) when (ex is IOException or AuthenticationException || ex is OperationCanceledException && _stop.IsCancellationRequested) { }
            }
        }
        public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); await _loop; await Task.WhenAll(_clients); _certificate?.Dispose(); _stop.Dispose(); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
