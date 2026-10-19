using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.YouTube;

internal static class ChapterChecks
{
    internal static IReadOnlyList<ChapterMarker> Markers =>
    [new() { Title = "Opening;#=\\\nartist=literal", StartSeconds = 0, EndSeconds = 0.1 }, new() { Title = "Ending", StartSeconds = 0.1, EndSeconds = 0.3 }];
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("YouTube chapter extraction uses the complete native timeline", () => Sync(NativeTimeline)),
        ("YouTube macro markers and description timestamps form finite chapters", () => Sync(Alternatives)),
        ("Chapter metadata escapes titles and rejects invalid ranges", () => Sync(Metadata)),
        ("Native chapter inspection retains markers and validates before media transfer", Inspection),
        ("A chapter option on media with no chapter markers needs no processing", NoChapters)
    ];

    private static void NativeTimeline()
    {
        const string initial = """
        {"playerOverlays":{"markersMap":[{"key":"DESCRIPTION_CHAPTERS","value":{"chapters":[
          {"chapterRenderer":{"timeRangeStartMillis":0,"title":{"simpleText":"First {chapter}"}}},
          {"chapterRenderer":{"timeRangeStartMillis":5000,"title":{"runs":[{"text":"Second"}]}}}
        ]}},{"key":"HEATSEEKER","value":{"chapters":[{"chapterRenderer":{"timeRangeStartMillis":2,"title":{"simpleText":"Suggestion"}}}]}}]}}
        """;
        var chapters = YouTubeChapterParser.Parse(initial, 12);
        Assert(chapters.Count == 2 && chapters[0].StartSeconds == 0 && chapters[0].EndSeconds == 5 && chapters[1].EndSeconds == 12, "Native chapter boundaries lost.");
        var page = YouTubePlayerParser.ParsePage(YouTubeChecks.FixtureHtml + "<script>ytInitialData=" + initial + ";</script>", "abcdefghijk");
        Assert(page.Chapters.Count == 2 && page.Chapters[1].EndSeconds == 19, "Page parsing did not retain the native chapter timeline.");
        Assert(YouTubeChapterParser.Parse(initial.Replace("5000", "0"), 12).Count == 0, "An invalid timeline was presented as complete.");
        Assert(YouTubeChapterParser.Parse(initial.Replace("playerOverlays", "sidebar"), 12).Count == 0, "Related-video chapters were attached to the current video.");
    }
    private static void Alternatives()
    {
        const string json = """
        {"engagementPanels":[{"macroMarkersListRenderer":{"contents":[
          {"macroMarkersListItemRenderer":{"title":{"simpleText":"Start"},"onTap":{"watchEndpoint":{"startTimeSeconds":0}}}},
          {"macroMarkersListItemRenderer":{"title":{"simpleText":"Finish"},"timeDescription":{"simpleText":"0:10"}}}
        ]}}]}
        """;
        var chapters = YouTubeChapterParser.Parse(json, 20);
        Assert(chapters.Count == 2 && chapters[1].StartSeconds == 10 && chapters[1].EndSeconds == 20, "Macro-marker timeline lost.");
        var description = "Notes\n00:00 - First\n00:10 Second\n00:20 | Third\nOther text";
        Assert(YouTubeChapterParser.Parse(null, 30, description).Select(chapter => chapter.Title).SequenceEqual(["First", "Second", "Third"]), "Description chapters lost.");
        foreach (var invalid in new[] { "00:10 First\n00:20 Second\n00:25 Third", "00:00 First\n00:60 Second\n01:20 Third", "00:00 First\n00:20 Second\n00:10 Third" })
            Assert(YouTubeChapterParser.Parse(null, 90, invalid).Count == 0, "Ordinary or invalid timestamps became chapters.");
    }
    private static void Metadata()
    {
        var text = ChapterMetadataWriter.Render(Markers);
        Assert(text.StartsWith(";FFMETADATA1\n") && text.Contains("START=100\nEND=300") && text.Contains("Opening\\;\\#\\=\\\\\\\nartist\\=literal"), "The chapter document lost timebase or literal escaping.");
        foreach (var marker in new[]
        {
            new ChapterMarker { Title = "Bad", StartSeconds = double.NaN, EndSeconds = 1 },
            new ChapterMarker { Title = "Bad", StartSeconds = -1, EndSeconds = 1 },
            new ChapterMarker { Title = "Bad", StartSeconds = 2, EndSeconds = 1 },
            new ChapterMarker { Title = "Bad", StartSeconds = 0, EndSeconds = 0.00001 }
        }) Throws<InvalidDataException>(() => ChapterMetadataWriter.Render([marker]));
        Throws<InvalidDataException>(() => ChapterMetadataWriter.Render([Markers[1], Markers[0]]));
    }
    private static async Task Inspection()
    {
        using var files = new Files();
        var requests = 0;
        var engine = CreateEngine([1, 2, 3], Markers, () => requests++);
        var info = await engine.InspectAsync(Request(files.Directory), default);
        Assert(info.Chapters.Count == 2, "Inspection dropped chapter metadata.");
        await Expect<InvalidOperationException>(() => engine.DownloadAsync(Request(files.Directory) with { EmbedChapters = true }, null, null, default));
        Assert(requests == 0 && !Directory.EnumerateFiles(files.Directory).Any(), "Chapter processing required ffmpeg after media transfer.");
        var invalid = CreateEngine([1], [new() { Title = "Invalid", StartSeconds = 3, EndSeconds = 2 }], () => requests++);
        await Expect<InvalidDataException>(() => invalid.DownloadAsync(Request(files.Directory) with { EmbedChapters = true }, null, null, default));
        Assert(requests == 0, "Invalid chapter metadata reached media transfer.");
    }
    private static async Task NoChapters()
    {
        using var files = new Files();
        var result = await CreateEngine([1, 2, 3], []).DownloadAsync(Request(files.Directory) with { EmbedChapters = true }, null, null, default);
        Assert(result.Succeeded && File.ReadAllBytes(result.OutputPath!).SequenceEqual(new byte[] { 1, 2, 3 }), "An empty chapter option refused media unnecessarily.");
    }
    internal static NativeEngine CreateEngine(byte[] media, IReadOnlyList<ChapterMarker> chapters, Action? onMedia = null) => new([new Provider(chapters)], _ => new MediaHttp(new Handler(_ =>
    { onMedia?.Invoke(); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(media) }; })));
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new Uri("https://media.example/clip.mp4"), OutputDirectory = directory, FileName = "result", VideoOnly = true, Retries = 0 };
    private sealed class Provider(IReadOnlyList<ChapterMarker> chapters) : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("fixture-chapters", "Chapters fixture", 200);
        public bool CanHandle(Uri url) => url.Host == "media.example";
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(new PageMetadata
        { Id = "fixture", Title = "Chapter fixture", Chapters = chapters, Media = [new() { Url = request.Url, Extension = "mp4", HasVideo = true, HasAudio = false }] });
    }
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private sealed class Files : IDisposable
    {
        public string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        public Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
