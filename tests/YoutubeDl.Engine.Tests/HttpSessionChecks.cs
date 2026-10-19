using System.Net;
using System.Net.Sockets;
using System.Text;
using YoutubeDl.Engine.Http;

internal static class HttpSessionChecks
{
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Native HTTP sessions retain website-set cookies without a supplied cookies file", () => Cookies(false)),
        ("Controlled playback HTTP shares cookies and headers while suppressing redirects", () => Cookies(true))
    ];
    private static async Task Cookies(bool controlled)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var uri = new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/");
            var received = new List<string>();
            var server = Serve();
            var options = new MediaHttpOptions { UserAgent = "NativeFixture/1.0", Referer = "https://www.youtube.com/", TimeoutSeconds = 5 };
            options.Headers.Add("X-Native-Fixture", "shared");
            using var http = new MediaHttp(options);
            await http.GetStringAsync(new Uri(uri, "set"), 128, timeout.Token);
            if (controlled)
            {
                using (var playback = http.CreateWithoutRedirects())
                using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri(uri, "check")))
                using (var response = await playback.SendAsync(request, timeout.Token))
                {
                    if (playback.AllowAutoRedirect || response.StatusCode != HttpStatusCode.Found) throw new InvalidOperationException("Playback automatically followed the redirect.");
                }
                await http.GetStringAsync(new Uri(uri, "after"), 128, timeout.Token);
            }
            else await http.GetStringAsync(new Uri(uri, "check"), 128, timeout.Token);
            await server;
            if (received[0].Contains("Cookie:", StringComparison.OrdinalIgnoreCase) || !received[1].Contains("native_session=fixture", StringComparison.Ordinal))
                throw new InvalidOperationException("The native HTTP session did not retain its own site cookie.");
            if (controlled && (!received[2].Contains("playback_session=second") || received.Any(request => !request.Contains("NativeFixture/1.0") ||
                !request.Contains("Referer: https://www.youtube.com/") || !request.Contains("X-Native-Fixture: shared"))))
                throw new InvalidOperationException("Playback cookies were not shared back or configured headers were lost.");

            async Task Serve()
            {
                for (var index = 0; index < (controlled ? 3 : 2); index++)
                {
                    using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                    await using var stream = client.GetStream();
                    var request = new StringBuilder();
                    var buffer = new byte[1];
                    while (!request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                    {
                        if (request.Length > 8192 || await stream.ReadAsync(buffer, timeout.Token) != 1) throw new IOException("Incomplete test request.");
                        request.Append((char)buffer[0]);
                    }
                    received.Add(request.ToString());
                    var response = (controlled && index == 1 ? "HTTP/1.1 302 Found\r\nLocation: /unexpected\r\nSet-Cookie: playback_session=second; Path=/\r\n" : "HTTP/1.1 200 OK\r\n") + "Content-Length: 2\r\nConnection: close\r\n" +
                        (index == 0 ? "Set-Cookie: native_session=fixture; Path=/; HttpOnly\r\n" : "") + "\r\nOK";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response), timeout.Token);
                }
            }
        }
        finally { listener.Stop(); }
    }
}
