using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Feeds;

internal static class FeedChecks
{
    internal static readonly Uri Url = new("https://feeds.example/show.xml");
    internal const string Rss = """
    <?xml version="1.0" encoding="utf-8"?>
    <rss version="2.0" xmlns:itunes="http://www.itunes.com/dtds/podcast-1.0.dtd" xmlns:atom="http://www.w3.org/2005/Atom"
      xmlns:podcast="https://podcastindex.org/namespace/1.0" xmlns:psc="http://podlove.org/simple-chapters">
      <channel><title>Fixture show</title><atom:link rel="self" href="https://feeds.example/show.xml"/><itunes:author>Creator</itunes:author>
        <image><url>/cover.png</url></image>
        <item><guid isPermaLink="false">episode-one</guid><title>First &amp; literal</title><description><![CDATA[Text with <b>markup</b>.]]></description>
          <link>https://article.example/first</link><itunes:duration>00:00:00.300</itunes:duration>
          <enclosure url="https://media.example/first.mp3?token=a%2Bb&amp;other=c" type="audio/mpeg" length="0"/>
          <podcast:transcript url="https://media.example/en.vtt" type="text/vtt" language="en"/>
          <psc:chapters><psc:chapter start="0" title="Opening"/><psc:chapter start="0.1" title="Ending"/></psc:chapters>
        </item>
        <item><guid>missing</guid><title>Missing media</title><description>No enclosure.</description></item>
        <item><guid>episode-three</guid><title>Third</title><enclosure url="https://media.example/third.mp3" type="audio/mpeg" length="5"/></item>
      </channel>
    </rss>
    """;

    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("RSS media feeds retain episode metadata, unavailable positions, artwork, transcripts and chapters", Metadata),
        ("Atom and Media RSS resolve inherited XML bases and alternative media facts", Atom),
        ("Native media-feed selection transfers only chosen enclosures without fetching article pages", Selection),
        ("Feed pagination preserves order and removes unchanged overlap", Pagination),
        ("Feed page loops, changed identities and limits fail without silent truncation", InvalidPages),
        ("XML external entities, unsafe addresses, deep trees and excessive resources are refused", InvalidXml),
        ("Media-feed download archives distinguish identical episode IDs in different feeds", Archive),
        ("Feed XML declaration encoding survives HTTP decoding", EncodingCheck),
        ("Canceled feed pagination never starts media publication", Cancellation),
        ("Feed discovery cookies remain available to resolved episode downloads", Cookies)
    ];

    private static Task Metadata()
    {
        var parsed = MediaFeedParser.Parse(Rss, Url).Metadata;
        var first = parsed.Entries[0].ResolvedMedia!;
        Assert(parsed.IsPlaylist && parsed.Title == "Fixture show" && parsed.Entries.Count == 3 && !parsed.Entries[1].Available, "Feed entries or missing media positions changed.");
        Assert(first.Id == "episode-one" && first.Title == "First & literal" && first.Uploader == "Creator" && first.DurationSeconds == 0.3 && first.ThumbnailUrl == "https://feeds.example/cover.png", "Episode metadata was lost.");
        Assert(first.Media.Single().FileSize is null && first.Media[0].IsAudioOnly && first.Media[0].Url.Query == "?token=a%2Bb&other=c", "Unknown size or literal enclosure URL changed.");
        Assert(first.Chapters.Count == 2 && first.Chapters[1].EndSeconds == 0.3 && first.Subtitles.Single().Language == "en", "Feed captions or chapters were lost.");
        return Task.CompletedTask;
    }
    private static Task Atom()
    {
        const string xml = """
        <a:feed xmlns:a="http://www.w3.org/2005/Atom" xmlns:m="http://search.yahoo.com/mrss/" xml:base="https://cdn.example/root/">
          <a:id>urn:fixture:feed</a:id><a:title>Atom</a:title><a:author><a:name>Author</a:name></a:author>
          <a:entry xml:base="episodes/"><a:id>tag:fixture,2026:one</a:id><a:title>Entry</a:title><a:link href="one"/>
            <a:link rel="enclosure" href="audio" type="audio/mp4" length="123"/>
            <m:group><m:thumbnail url="../image.jpg"/>
              <m:content url="low.mp4" type="video/mp4" width="640" height="360" bitrate="800" framerate="25" duration="1.5"/>
              <m:content url="high.mp4" type="video/mp4" width="1280" height="720" bitrate="1600" framerate="50"/>
              <m:content url="sample.mp4" type="video/mp4" expression="sample"/>
              <m:content url="image.jpg" type="image/jpeg"/>
            </m:group>
          </a:entry>
        </a:feed>
        """;
        var parsed = MediaFeedParser.Parse(xml, Url); var entry = parsed.Metadata.Entries.Single(); var media = entry.ResolvedMedia!;
        Assert(parsed.ExplicitId == "urn:fixture:feed" && entry.Url!.AbsoluteUri == "https://cdn.example/root/episodes/one" && media.ThumbnailUrl == "https://cdn.example/root/image.jpg", "Inherited XML bases were not honored.");
        Assert(media.Media.Count == 3 && media.Media[0].Extension == "m4a" && media.Media[0].FileSize == 123 && media.Media[1].Height == 360 && media.Media[1].Bandwidth == 800000 && media.DurationSeconds == 1.5,
            "Atom enclosure or Media RSS facts were changed.");
        Assert(media.Warnings.Single().Contains("sample"), "Preview media was silently exposed as complete.");
        return Task.CompletedTask;
    }
    private static async Task Selection()
    {
        using var files = new Files(); var calls = new List<Uri>(); var sessions = 0;
        var engine = Engine(uri => uri == Url ? Response(Rss) : new(HttpStatusCode.OK) { Content = new StringContent(CaptionChecks.Caption) }, calls, () => sessions++);
        var result = await engine.DownloadAsync(Request(files.Directory) with { PlaylistItems = "1", WriteSubs = true }, null, null, default);
        Assert(result.Succeeded && result.OutputPaths.Count == 1 && result.OutputPath!.EndsWith(".mp3") && sessions == 1 &&
            calls.Select(uri => uri.Host).SequenceEqual(new[] { "feeds.example", "media.example", "media.example" }), "Selection fetched an article, an unselected episode, or a separate playback session.");
        Assert(File.ReadAllText(Path.ChangeExtension(result.OutputPath!, ".en.vtt")) == CaptionChecks.Caption, "Feed transcript sidecar was not saved.");
    }
    private static string Page(string id, string? next = null, string title = "Paged") =>
        "<rss xmlns:atom=\"http://www.w3.org/2005/Atom\"><channel><title>" + title + "</title>" +
        (next is null ? "" : "<atom:link rel=\"next\" href=\"" + next + "\"/>") +
        "<item><guid>" + id + "</guid><title>" + id + "</title><enclosure url=\"https://media.example/" + id + ".mp3\" type=\"audio/mpeg\"/></item></channel></rss>";
    private static async Task Pagination()
    {
        const string overlap = "<item><guid>one</guid><title>one</title><enclosure url=\"https://media.example/one.mp3\" type=\"audio/mpeg\"/></item>";
        var page1 = Page("one", "next.xml"); var page2 = Page("two").Replace("<item>", overlap + "<item>");
        var calls = new List<Uri>();
        var info = await Engine(uri => Response(uri == Url ? page1 : page2), calls).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Entries.Select(entry => entry.Id).SequenceEqual(new[] { "one", "two" }) && info.Entries.Select(entry => entry.Position).SequenceEqual(new[] { 1, 2 }) && calls.Count == 2,
            "Feed pagination changed order or repeated an overlap.");
    }
    private static async Task InvalidPages()
    {
        await Expect<InvalidDataException>(() => Engine(_ => Response(Page("one", Url.AbsoluteUri)), []).InspectAsync(Request(Path.GetTempPath()), default));
        await Expect<InvalidDataException>(() => Engine(uri => Response(uri == Url ? Page("one", "next.xml") : Page("two", title: "Changed")), []).InspectAsync(Request(Path.GetTempPath()), default));
        await Expect<InvalidDataException>(() => Engine(uri => Response(uri == Url ? Page("one", "next.xml") : Page("one").Replace("one.mp3", "changed.mp3")), []).InspectAsync(Request(Path.GetTempPath()), default));
        await Expect<InvalidDataException>(() => Engine(uri => Response(uri == Url ? Page("one", "next.xml") : Page("two")), []).InspectAsync(Request(Path.GetTempPath()) with { MaximumPlaylistEntries = 1 }, default));
    }
    private static Task InvalidXml()
    {
        foreach (var xml in new[] { "<!DOCTYPE rss [<!ENTITY external SYSTEM 'file:///private'>]><rss><channel><title>&external;</title></channel></rss>",
            Rss.Replace("https://media.example/first.mp3", "file:///private.mp3"), Rss.Replace("https://media.example/first.mp3", "https://user@media.example/first.mp3"),
            "<rss><channel><title>Too deep</title>" + string.Concat(Enumerable.Repeat("<x>", 70)) + string.Concat(Enumerable.Repeat("</x>", 70)) + "</channel></rss>" })
            Throws<InvalidDataException>(() => MediaFeedParser.Parse(xml, Url));
        Throws<InvalidDataException>(() => MediaFeedParser.Parse(Rss, Url, 2));
        Assert(!MediaFeedParser.IsFeed("<!DOCTYPE html><html><body>Not a feed</body></html>", "text/html"), "HTML was classified as a media feed.");
        return Task.CompletedTask;
    }
    private static async Task Archive()
    {
        using var files = new Files(); var archive = Path.Combine(files.Directory, "archive.txt"); var transfers = 0;
        var engine = Engine(uri => uri.Host == "feeds.example" ? Response(Page("same")) : Audio(), []);
        var request = Request(files.Directory) with { DownloadArchiveFile = archive };
        var first = await engine.DownloadAsync(request, null, null, default, _ => transfers++);
        var repeated = await engine.DownloadAsync(request, null, null, default, _ => transfers++);
        var other = await engine.DownloadAsync(request with { Url = new("https://feeds.example/another.xml") }, null, null, default, _ => transfers++);
        Assert(first.Succeeded && repeated.Succeeded && other.Succeeded && transfers == 2 && File.ReadAllLines(archive).Length == 2 && File.ReadAllLines(archive).All(line => line.StartsWith("native:feed ")),
            "One feed's reused GUID skipped another feed or repeated a completed episode.");
        HttpResponseMessage Audio() { return new(HttpStatusCode.OK) { Content = new StringContent("audio") }; }
    }
    private static async Task EncodingCheck()
    {
        foreach (var encoding in new[] { Encoding.Latin1, Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.UTF32 })
        {
            var bytes = encoding.GetBytes("<?xml version=\"1.0\" encoding=\"" + encoding.WebName + "\"?>" + Page("Caf\u00e9"));
            var engine = Engine(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }, []);
            var info = await engine.InspectAsync(Request(Path.GetTempPath()), default);
            Assert(info.Entries.Single().Title == "Caf\u00e9", "The XML encoding declaration was ignored: " + encoding.WebName);
        }
    }
    private static async Task Cancellation()
    {
        using var files = new Files(); using var cancellation = new CancellationTokenSource(); var requests = new List<Uri>();
        var engine = Engine(uri => { if (uri != Url) cancellation.Cancel(); return Response(Page("one", "next.xml")); }, requests);
        await Expect<OperationCanceledException>(() => engine.DownloadAsync(Request(files.Directory), null, null, cancellation.Token));
        Assert(requests.Count == 2 && !Directory.EnumerateFiles(files.Directory).Any(), "Canceled feed discovery published media.");
    }
    private static async Task Cookies()
    {
        using var files = new Files(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            var origin = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
            var cookieSeen = false; var server = Serve();
            var result = await new NativeEngine().DownloadAsync(Request(files.Directory) with { Url = new(origin + "/feed") }, null, null, timeout.Token);
            await server;
            Assert(result.Succeeded && cookieSeen && File.ReadAllText(result.OutputPath!) == "audio", "Resolved feed media lost its discovery cookie.");
            async Task Serve()
            {
                for (var index = 0; index < 2; index++)
                {
                    using var client = await listener.AcceptTcpClientAsync(timeout.Token); await using var stream = client.GetStream();
                    var request = new StringBuilder(); var buffer = new byte[1];
                    while (!request.ToString().EndsWith("\r\n\r\n"))
                    { if (request.Length > 8192 || await stream.ReadAsync(buffer, timeout.Token) != 1) throw new IOException("Invalid fixture request."); request.Append((char)buffer[0]); }
                    if (index == 1) cookieSeen = request.ToString().Contains("feed_session=fixture");
                    var body = index == 0 ? Page("one").Replace("https://media.example/one.mp3", origin + "/one.mp3") : "audio";
                    var bytes = Encoding.UTF8.GetBytes(body);
                    var headers = "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: " + bytes.Length + "\r\nContent-Type: " + (index == 0 ? "application/rss+xml\r\nSet-Cookie: feed_session=fixture; Path=/" : "audio/mpeg") + "\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), timeout.Token); await stream.WriteAsync(bytes, timeout.Token);
                }
            }
        }
        finally { listener.Stop(); }
    }
    internal static NativeDownloadRequest Request(string folder) => new() { Url = Url, OutputDirectory = folder, Retries = 0 };
    internal static NativeEngine Engine(Func<Uri, HttpResponseMessage> response, List<Uri> calls, Action? onSession = null) => new(httpFactory: _ =>
    { onSession?.Invoke(); return new MediaHttp(new Handler(request => { calls.Add(request.RequestUri!); return response(request.RequestUri!); })); });
    internal static HttpResponseMessage Response(string text) { var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) }; response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/rss+xml"); return response; }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private sealed class Files : IDisposable
    {
        internal string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(Directory);
            if (!target.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidOperationException("The test folder is outside its owned root.");
            System.IO.Directory.Delete(target, true);
        }
    }
}
