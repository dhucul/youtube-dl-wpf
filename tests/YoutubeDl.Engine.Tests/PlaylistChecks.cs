using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.YouTube;

internal static class PlaylistChecks
{
    private static readonly string[] Ids = ["aaaaaaaaaaa", "bbbbbbbbbbb", "ccccccccccc", "ddddddddddd"];
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Playlist parsers retain legacy and current entries without sidebar suggestions", () => Sync(Parser)),
        ("Playlist selections preserve positions and enforce bounded ranges", () => Sync(Selection)),
        ("YouTube playlist continuations post the current client context", Pagination),
        ("Repeated continuations and oversized playlists fail instead of truncating", Bounds),
        ("Playlist downloads report each failure and preserve successful files", () => Download(ignoreErrors: true)),
        ("Playlist downloads stop after a failure when requested", () => Download(ignoreErrors: false)),
        ("Playlist cancellation retains completed files and their notifications", Cancellation),
        ("A playlist completion callback error retains the already published result", CallbackFailure),
        ("Playlist reverse selection retains source positions and completion order", ReverseSelection),
        ("Playlist reverse stop-on-error retains completed items and original failure positions", ReverseFailure),
        ("Playlist random ordering retains the selected inventory without mutating provider entries", () => Sync(RandomSelection)),
        ("Playlist random cancellation stops after the first completed selected item", RandomCancellation),
        ("Playlist random ordering preserves archive identities and skips recorded items", RandomArchive)
    ];

    private static void Parser()
    {
        var json = """
        {"metadata":{"playlistMetadataRenderer":{"title":"Native playlist"}},"contents":[
          {"playlistVideoRenderer":{"videoId":"aaaaaaaaaaa","title":{"runs":[{"text":"A & B"}]},"lengthSeconds":"62","thumbnail":{"thumbnails":[{"url":"https://img.example/small","width":20},{"url":"https://img.example/large","width":200}]}}},
          {"playlistVideoRenderer":{"title":{"simpleText":"Private video"},"isPlayable":false}},
          {"lockupViewModel":{"contentType":"LOCKUP_CONTENT_TYPE_VIDEO","contentId":"ccccccccccc","metadata":{"lockupMetadataViewModel":{"title":{"content":"Modern"}}},"contentImage":{"thumbnailViewModel":{"image":{"sources":[{"url":"https://img.example/current","width":100}]},"overlays":[{"thumbnailBadgeViewModel":{"text":"1:02:03"}}]}}}},
          {"continuationItemViewModel":{"continuationCommand":{"token":"next"}}}
        ],"sidebar":{"playlistVideoRenderer":{"videoId":"ddddddddddd"}}}
        """;
        var page = YouTubePlaylistParser.Parse(json, 5);
        Assert(page.Title == "Native playlist" && page.Entries.Count == 3 && page.Continuation == "next", "Playlist shape lost.");
        Assert(page.Entries[0] is { Position: 5, Title: "A & B", DurationSeconds: 62, ThumbnailUrl: "https://img.example/large" }, "Legacy fields lost.");
        Assert(page.Entries[1] is { Position: 6, Available: false, Url: null }, "Unavailable position was dropped.");
        Assert(page.Entries[2] is { Position: 7, Title: "Modern", DurationSeconds: 3723, ThumbnailUrl: "https://img.example/current" }, "Current fields lost.");
        Throws<InvalidDataException>(() => YouTubePlaylistParser.Parse(json, maximumEntries: 2));
        Throws<InvalidDataException>(() => YouTubePlaylistParser.Parse(json.Replace("aaaaaaaaaaa", "short")));
    }

    private static void Selection()
    {
        var entries = Ids.Select((id, i) => new NativePlaylistEntry(i + 1, id, id, new Uri("https://www.youtube.com/watch?v=" + id), null, null, true)).ToArray();
        var request = Request(Path.GetTempPath()) with { PlaylistItems = "4,1-3,2", PlaylistStart = 2, PlaylistEnd = 4 };
        Assert(PlaylistSelection.Select(entries, request).Select(entry => entry.Position).SequenceEqual([4, 1, 2, 3]), "Explicit selections lost order, duplicated entries, or did not override start/end settings.");
        foreach (var text in new[] { "1,,2", "::0", "1:2:3:4", "1:two", "--", "1:3:" })
            Throws<ArgumentException>(() => PlaylistSelection.Select(entries, request with { PlaylistItems = text }));
        Throws<ArgumentOutOfRangeException>(() => PlaylistSelection.Select(entries, request with { MaximumPlaylistEntries = int.MaxValue }));
        Assert(PlaylistSelection.Select(entries, request with { PlaylistItems = "5" }).Count == 0, "Out-of-range selection did not produce an empty work list.");
    }

    private static async Task Pagination()
    {
        var requests = new List<string>();
        var engine = Engine(requests);
        var info = await engine.InspectAsync(Request(Path.GetTempPath()) with { ExpandPlaylist = true }, default);
        Assert(info.IsPlaylist && info.Id == "PLfixture" && info.Entries.Select(entry => entry.Position).SequenceEqual([1, 2, 3, 4]), "Enumeration lost entries.");
        Assert(requests.Count == 2 && requests[0].Contains("/playlist?list=PLfixture") && requests[1].Contains("/youtubei/v1/browse"), "Pagination did not use native browse requests.");
        await Expect<InvalidOperationException>(() => engine.DownloadAsync(Request(Path.GetTempPath()) with
        { Url = new Uri("https://www.youtube.com/playlist?list=PLfixture"), DownloadPlaylist = false }, null, null, default));
    }

    private static async Task Bounds()
    {
        foreach (var repeat in new[] { false, true })
        {
            var engine = Engine([], repeatToken: repeat);
            await Expect<InvalidDataException>(() => engine.InspectAsync(Request(Path.GetTempPath()) with
            { ExpandPlaylist = true, MaximumPlaylistEntries = repeat ? 1000 : 2 }, default));
        }
    }

    private static async Task Download(bool ignoreErrors)
    {
        using var files = new Files();
        var completed = new List<string>();
        var requests = new List<string>();
        var result = await Engine(requests).DownloadAsync(Request(files.Directory) with { IgnoreErrors = ignoreErrors }, null, null, default, completed.Add);
        Assert(!result.Succeeded && result.OutputPaths.SequenceEqual(completed), "Partial results were not reported exactly once.");
        Assert(result.OutputPaths.Count == (ignoreErrors ? 2 : 1) && result.Failures.Count == (ignoreErrors ? 2 : 1), "Failure continuation policy ignored.");
        Assert(result.Failures[0].Position == 2 && result.OutputPaths.All(path => File.ReadAllText(path) == "combined"), "Completed media was discarded.");
        Assert(Path.GetFileName(result.OutputPaths[0]).StartsWith("001 - ") && (!ignoreErrors || Path.GetFileName(result.OutputPaths[1]).StartsWith("004 - ")), "Original playlist positions lost.");
        Assert(!requests.Any(url => url.Contains("watch?v=" + Ids[1])), "An unavailable item was requested.");
    }

    private static async Task Cancellation()
    {
        using var files = new Files();
        using var cancellation = new CancellationTokenSource();
        var completed = new List<string>();
        var requests = new List<string>();
        await Expect<OperationCanceledException>(() => Engine(requests).DownloadAsync(Request(files.Directory), null, null, cancellation.Token, path =>
        { completed.Add(path); cancellation.Cancel(); }));
        Assert(completed.Count == 1 && File.ReadAllText(completed[0]) == "combined", "Cancellation removed a completed file.");
        Assert(requests.Count(url => url.Contains("/watch?")) == 1, "Cancellation started another playlist item.");
    }

    private static async Task CallbackFailure()
    {
        using var files = new Files();
        var result = await Engine([]).DownloadAsync(Request(files.Directory) with { PlaylistItems = "1" }, null, null, default,
            _ => throw new InvalidOperationException("Fixture callback failure"));
        Assert(!result.Succeeded && result.Failures.Count == 1 && result.OutputPaths.Count == 1 && File.ReadAllText(result.OutputPaths[0]) == "combined",
            "The completion callback hid a published file.");
    }

    private static async Task ReverseSelection()
    {
        using var files = new Files(); var requests = new List<string>(); var completed = new List<string>();
        var request = Request(files.Directory) with { PlaylistItems = "1,4", PlaylistReverse = true };
        var engine = Engine(requests);
        var info = await engine.InspectAsync(request with { ExpandPlaylist = true }, default);
        Assert(info.Entries.Select(entry => entry.Position).SequenceEqual([1, 2, 3, 4]), "Download ordering changed the preview's source inventory.");
        var result = await engine.DownloadAsync(request, null, null, default, completed.Add);
        Assert(result.Succeeded && result.OutputPaths.SequenceEqual(completed) && completed.Count == 2 &&
            Path.GetFileName(completed[0]).StartsWith("004 - ") && Path.GetFileName(completed[1]).StartsWith("001 - "),
            "Reverse ordering renumbered entries or changed the completion sequence.");
        Assert(requests.Where(url => url.Contains("/watch?")).Select(url => new Uri(url).Query).SequenceEqual(["?v=" + Ids[3], "?v=" + Ids[0]]),
            "Reverse selection fetched unselected entries or downloaded in source order.");
    }

    private static async Task ReverseFailure()
    {
        using var files = new Files(); var requests = new List<string>();
        var result = await Engine(requests).DownloadAsync(Request(files.Directory) with { PlaylistReverse = true, IgnoreErrors = false }, null, null, default);
        Assert(!result.Succeeded && result.Failures.Single().Position == 3 && result.OutputPaths.Count == 1 &&
            Path.GetFileName(result.OutputPaths[0]).StartsWith("004 - ") && File.ReadAllText(result.OutputPaths[0]) == "combined",
            "Reverse stop-on-error lost the completed last item or reported the wrong source position.");
        Assert(requests.Count(url => url.Contains("/watch?")) == 2, "Reverse stop-on-error continued after a failure.");
    }

    private static void RandomSelection()
    {
        var entries = Enumerable.Range(1, 64).Select(position => new NativePlaylistEntry(position, position.ToString(), "Item " + position,
            new Uri("https://media.example/" + position + ".mp4"), null, null, position != 12)).ToArray();
        var request = Request(Path.GetTempPath()) with { PlaylistRandom = true, PlaylistReverse = true, PlaylistItems = "2-10,12-63", PlaylistStart = 5, PlaylistEnd = 60 };
        var expected = entries.Where(entry => entry.Position is >= 2 and <= 63 and not 11).ToArray();
        var shuffled = PlaylistSelection.Select(entries, request, new Random(12345));
        var repeated = PlaylistSelection.Select(entries, request, new Random(12345));
        Assert(shuffled.SequenceEqual(repeated) && !shuffled.SequenceEqual(expected) && !shuffled.SequenceEqual(expected.Reverse()), "Seeded random ordering was not applied after selection.");
        Assert(shuffled.OrderBy(entry => entry.Position).SequenceEqual(expected) && shuffled.All(entry => ReferenceEquals(entry, entries[entry.Position - 1])),
            "Random ordering dropped, duplicated, renumbered or recreated selected entries.");
        Assert(entries.Select(entry => entry.Position).SequenceEqual(Enumerable.Range(1, 64)) && !shuffled.Single(entry => entry.Position == 12).Available,
            "Random ordering mutated the provider inventory or removed an unavailable position.");
    }

    private static async Task RandomCancellation()
    {
        using var files = new Files(); using var stop = new CancellationTokenSource();
        var requests = new List<string>(); var completed = new List<string>();
        await Expect<OperationCanceledException>(() => Engine(requests).DownloadAsync(Request(files.Directory) with { PlaylistRandom = true, PlaylistItems = "1,4" },
            null, null, stop.Token, path => { completed.Add(path); stop.Cancel(); }));
        Assert(completed.Count == 1 && File.ReadAllText(completed[0]) == "combined" && requests.Count(url => url.Contains("/watch?")) == 1,
            "Random playlist cancellation removed a completed item or started the next one.");
    }

    private static async Task RandomArchive()
    {
        using var files = new Files(); var archive = Path.Combine(files.Directory, "archive.txt");
        File.WriteAllText(archive, "youtube " + Ids[0] + "\n");
        var requests = new List<string>(); var completed = new List<string>();
        var result = await Engine(requests).DownloadAsync(Request(files.Directory) with { PlaylistRandom = true, PlaylistItems = "1,4", DownloadArchiveFile = archive },
            null, null, default, completed.Add);
        Assert(result.Succeeded && completed.Count == 1 && result.OutputPaths.SequenceEqual(completed) && Path.GetFileName(completed[0]).StartsWith("004 - "),
            "Random ordering reported an archived item as a new download.");
        Assert(!requests.Any(url => url.Contains("watch?v=" + Ids[0])) && File.ReadAllLines(archive).SequenceEqual(["youtube " + Ids[0], "youtube " + Ids[3]]),
            "Random ordering changed archive identities or downloaded a recorded item.");
    }

    private static NativeDownloadRequest Request(string directory) => new()
    { Url = new Uri("https://www.youtube.com/watch?v=" + Ids[0] + "&list=PLfixture"), OutputDirectory = directory, FormatId = "18", Retries = 0 };

    private static NativeEngine Engine(List<string> requests, bool repeatToken = false) => new(httpFactory: _ => new MediaHttp(new Handler(async request =>
    {
        var url = request.RequestUri!;
        requests.Add(url.AbsoluteUri);
        string content;
        if (url.AbsolutePath == "/playlist")
            content = "<script>ytInitialData=" + Page([Entry(0), Entry(1, false)], "next", initial: true) + ";</script>{\"INNERTUBE_CLIENT_VERSION\":\"fixture.1\"}";
        else if (url.AbsolutePath == "/youtubei/v1/browse")
        {
            Assert(request.Method == HttpMethod.Post && request.Headers.GetValues("X-YouTube-Client-Version").Single() == "fixture.1", "Client headers missing.");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert(body.RootElement.GetProperty("continuation").GetString() == "next" && body.RootElement.GetProperty("context").GetProperty("client").GetProperty("clientVersion").GetString() == "fixture.1", "Client body missing.");
            content = Page([Entry(2), Entry(3)], repeatToken ? "next" : null, initial: false);
        }
        else if (url.AbsolutePath == "/watch")
        {
            var id = url.Query.Split("v=")[1];
            if (id == Ids[2]) return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("Fixture failure") };
            content = YouTubeChecks.FixtureHtml.Replace("abcdefghijk", id);
        }
        else content = "combined";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) };
    })));

    private static object Entry(int index, bool available = true) => new
    { playlistVideoRenderer = new { videoId = Ids[index], title = new { simpleText = "Item " + (index + 1) }, isPlayable = available } };
    private static string Page(object[] entries, string? token, bool initial)
    {
        var items = entries.ToList();
        if (token is not null) items.Add(new { continuationItemRenderer = new { continuationEndpoint = new { continuationCommand = new { token } } } });
        return initial
            ? JsonSerializer.Serialize(new { metadata = new { playlistMetadataRenderer = new { title = "Fixture playlist" } }, contents = items })
            : JsonSerializer.Serialize(new { onResponseReceivedActions = new[] { new { appendContinuationItemsAction = new { continuationItems = items } } } });
    }
    private static Task Sync(Action test) { test(); return Task.CompletedTask; }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => response(request); }
    private sealed class Files : IDisposable
    {
        public string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        public Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
