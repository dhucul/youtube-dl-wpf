using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.YouTube;

internal static class YouTubeCookieChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("YouTube cookie snapshots preserve scope flags empty values and expiry", Scope),
        ("YouTube cookie snapshots exclude unrelated domains and own their input state", Isolation),
        ("YouTube cookie snapshots omit secrets from JSON and diagnostic text", Secrets),
        ("YouTube selected cookie files take precedence over browser discovery", Selection),
        ("YouTube captured cookies replace caller cookies without forwarding header credentials", Transport)
    ];
    internal static CookieContainer Jar()
    {
        var jar = new CookieContainer();
        jar.Add(new("https://www.youtube.com/"), new Cookie("host-fixture", "host-value", "/") { Secure = true, HttpOnly = true });
        jar.Add(new Cookie("domain-fixture", "domain-value", "/youtubei", ".youtube.com") { Secure = true, Expires = DateTime.UtcNow.AddHours(1) });
        jar.Add(new("https://www.youtube.com/"), new Cookie("empty-fixture", "", "/"));
        jar.Add(new("https://bank.example/"), new Cookie("unrelated", "private-unrelated", "/"));
        jar.Add(new("https://mail.google.com/"), new Cookie("mail-only", "private-mail", "/"));
        return jar;
    }
    private static Task Scope()
    {
        var snapshot = YouTubePlaybackCookies.FromContainer(Jar()); var jar = snapshot.ToContainer();
        Assert(snapshot.Count == 3 && snapshot.Items.Single(cookie => cookie.Name == "host-fixture") is { IncludeSubdomains: false, Secure: true, HttpOnly: true, Expires: null }, "Host-only/session flags changed.");
        Assert(snapshot.Items.Single(cookie => cookie.Name == "domain-fixture") is { IncludeSubdomains: true, Expires: not null }, "Domain/expiry flags changed.");
        Assert(jar.GetCookies(new("https://sub.www.youtube.com/")).Cast<Cookie>().All(cookie => cookie.Name != "host-fixture"), "A host cookie widened to subdomains.");
        Assert(jar.GetCookies(new("https://m.youtube.com/youtubei/player")).Cast<Cookie>().Any(cookie => cookie.Name == "domain-fixture") && jar.GetCookies(new("https://m.youtube.com/")).Count == 0, "Domain/path matching changed.");
        Assert(jar.GetCookies(new("https://www.youtube.com/")).Cast<Cookie>().Single(cookie => cookie.Name == "empty-fixture").Value == "", "An empty cookie disappeared.");
        return Task.CompletedTask;
    }
    private static Task Isolation()
    {
        var original = Jar(); var snapshot = YouTubePlaybackCookies.FromContainer(original);
        original.Add(new("https://www.youtube.com/"), new Cookie("host-fixture", "modified", "/"));
        var first = snapshot.ToContainer(); first.Add(new("https://www.youtube.com/"), new Cookie("host-fixture", "modified-again", "/"));
        Assert(snapshot.Count == 3 && snapshot.ToContainer().GetCookies(new("https://www.youtube.com/")).Cast<Cookie>().Single(cookie => cookie.Name == "host-fixture").Value == "host-value", "Cookie ownership was shared with caller state.");
        Assert(!snapshot.Items.Any(cookie => cookie.Domain.Contains("bank") || cookie.Domain.Contains("mail")), "Unrelated account domains entered playback scope.");
        return Task.CompletedTask;
    }
    private static Task Secrets()
    {
        var snapshot = YouTubePlaybackCookies.FromContainer(Jar()); var text = JsonSerializer.Serialize(snapshot) + snapshot + JsonSerializer.Serialize(snapshot.Items) + string.Join(';', snapshot.Items);
        Assert(!text.Contains("host-value") && !text.Contains("domain-value") && !text.Contains("private-unrelated"), "Cookie values entered diagnostics.");
        return Task.CompletedTask;
    }
    private static async Task Selection()
    {
        using var files = new SabrFixture.Files(); var path = Path.Combine(files.Directory, "cookies.txt");
        await File.WriteAllTextAsync(path, "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t0\tfile-fixture\tfile-value\n.example.com\tTRUE\t/\tFALSE\t0\tunrelated\tother\n");
        var snapshot = YouTubePlaybackCookies.LoadRequested(new NativeDownloadRequest { Url = new("https://www.youtube.com/watch?v=abcdefghijk"), OutputDirectory = files.Directory, CookieFile = path, BrowserCookies = "nonexistent-browser" });
        Assert(snapshot.Count == 1 && snapshot.Items[0].Name == "file-fixture", "A selected cookie file did not take precedence or was not scoped.");
    }
    private static async Task Transport()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var receive = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(stop.Token); await using var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); _ = await reader.ReadLineAsync(stop.Token);
            while (await reader.ReadLineAsync(stop.Token) is { Length: > 0 } line) { var colon = line.IndexOf(':'); headers[line[..colon]] = line[(colon + 1)..].Trim(); }
            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray(), stop.Token); return headers;
        });
        try
        {
            var jar = new CookieContainer(); jar.Add(new("http://r1.googlevideo.com/media/"), new Cookie("playback-fixture", "fresh-value", "/media"));
            var options = new MediaHttpOptions { Proxy = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port }; options.Headers["Authorization"] = "Bearer unrelated"; options.Headers["Cookie"] = "unrelated-header=secret";
            using var original = new MediaHttp(options); using var scoped = original.CreateCookieSession(YouTubePlaybackCookies.FromContainer(jar));
            Assert(await scoped.GetStringAsync(new("http://r1.googlevideo.com/media/file"), 16, stop.Token) == "ok", "The scoped transport failed.");
            var headers = await receive;
            Assert(headers.GetValueOrDefault("Cookie") == "playback-fixture=fresh-value" && !headers.ContainsKey("Authorization"), "Captured cookies were replaced by caller header credentials.");
        }
        finally { listener.Stop(); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
