using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.PeerTube;

internal static class PeerTubeChecks
{
    private const string Id = "73556243-7a9b-496c-a740-f80e42ee0ad9";
    private const string ShortId = "ff2EVqgHrQX5WJyJJc7Uax";
    private const string PlaylistId = "11111111-2222-3333-4444-555555555555";
    private const string Json = """
    {"id":42,"uuid":"73556243-7a9b-496c-a740-f80e42ee0ad9","shortUUID":"ff2EVqgHrQX5WJyJJc7Uax","name":"PeerTube fixture","duration":9,"description":"Description","thumbnailPath":"/thumbnail.png","account":{"displayName":"Creator"},
     "files":[{"id":1,"fileUrl":"https://cdn.example/360.mp4","size":12,"resolution":{"id":360,"label":"360p"},"width":640,"height":360,"fps":24,"hasAudio":true,"hasVideo":true}],
     "streamingPlaylists":[{"id":1,"type":1,"playlistUrl":"https://cdn.example/master.m3u8","files":[{"id":2,"fileUrl":"https://cdn.example/720-fragmented.mp4","size":14,"resolution":{"id":720,"label":"720p"},"width":1280,"height":720,"fps":30,"hasAudio":true,"hasVideo":true}]}]}
    """;
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("PeerTube routes preserve UUID, short, numeric, and instance identifiers", () => Sync(Addresses)),
        ("PeerTube native metadata retains progressive and fragmented file details", () => Sync(Metadata)),
        ("PeerTube captions and finite chapter ranges use typed native tracks", () => Sync(Tracks)),
        ("PeerTube identity mismatches and invalid resource documents fail explicitly", () => Sync(Invalid)),
        ("Native PeerTube downloads exposed media and automatic captions", Download),
        ("Older PeerTube optional endpoints can be absent without losing media", OptionalEndpoints),
        ("PeerTube playlist pagination retains unavailable positions and reports clip limits", Playlist),
        ("PeerTube changed playlist totals and exhausted pages never silently truncate", PlaylistFailure)
    ];
    private static void Addresses()
    {
        foreach (var path in new[] { "w/" + ShortId, "videos/watch/" + Id, "videos/embed/" + Id, "videos/watch/42" })
            Assert(PeerTubeAddress.Parse(new Uri("https://instance.example:9443/" + path)) is { IsPlaylist: false } address && address.Instance.Port == 9443, "A video route or custom instance port was lost.");
        Assert(PeerTubeAddress.Parse(new Uri("https://instance.example/w/p/" + PlaylistId)).IsPlaylist, "Playlist route missing.");
        Assert(!PeerTubeAddress.CanHandle(new Uri("https://user@instance.example/w/" + ShortId)) && !PeerTubeAddress.CanHandle(new Uri("https://instance.example/w/not-a-video")), "Malformed routes accepted.");
    }
    private static void Metadata()
    {
        var page = PeerTubeProvider.ParseVideo(Json, Address());
        Assert(page.Id == Id && page.Title == "PeerTube fixture" && page.DurationSeconds == 9 && page.Uploader == "Creator", "Video metadata lost.");
        Assert(page.Media.Count == 3 && page.ReportedFormats.Single(format => format.Id == "hls-2") is { Height: 720, Width: 1280, FramesPerSecond: 30, FileSize: 14, HasAudio: true }, "Fragmented media details lost.");
        Assert(page.ThumbnailUrl == "https://instance.example/thumbnail.png", "Relative thumbnail URL was changed.");
        Assert(PeerTubeProvider.ParseVideo(Json, PeerTubeAddress.Parse(new Uri("https://instance.example/videos/watch/42"))).Id == Id, "Numeric identity was rejected.");
    }
    private static void Tracks()
    {
        var captions = PeerTubeProvider.ParseCaptions(Captions, Address().Instance);
        Assert(captions.Single() is { Language: "cy", Automatic: true } && captions[0].Url.AbsoluteUri == "https://instance.example/caption.vtt", "Caption facts lost.");
        var chapters = PeerTubeProvider.ParseChapters("{\"chapters\":[{\"title\":\"First\",\"timecode\":0},{\"title\":\"Second\",\"timecode\":3}]}", 9);
        Assert(chapters.Count == 2 && chapters[0].EndSeconds == 3 && chapters[1].EndSeconds == 9, "Finite chapter ranges lost.");
    }
    private static void Invalid()
    {
        Throws<InvalidDataException>(() => PeerTubeProvider.ParseVideo(Json.Replace(Id, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee").Replace(ShortId, "wrong"), Address()));
        Throws<InvalidDataException>(() => PeerTubeProvider.ParseVideo(Json.Replace("https://cdn.example/360.mp4", "file:///private.mp4"), Address()));
        Throws<InvalidDataException>(() => PeerTubeProvider.ParseVideo(new string(' ', 4 * 1024 * 1024 + 1), Address()));
        Throws<InvalidDataException>(() => PeerTubeProvider.ParseCaptions("{\"data\":null}", Address().Instance));
        Throws<InvalidDataException>(() => PeerTubeProvider.ParseChapters("{\"chapters\":[{\"title\":\"Bad\",\"timecode\":10}]}", 9));
    }
    private static async Task Download()
    {
        using var files = new Files();
        var requests = new List<string>();
        var engine = Engine(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            requests.Add(path);
            return Ok(path.EndsWith("/captions") ? Captions : path.EndsWith("/chapters") ? "{\"chapters\":[]}" : path == "/caption.vtt" ? CaptionChecks.Caption : path.EndsWith(".mp4") ? "fixture-media" : Json);
        });
        var result = await engine.DownloadAsync(Request(files.Directory) with { FormatId = "web-1", WriteAutoSubs = true }, null, null, default);
        Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == "fixture-media" && File.ReadAllText(Path.Combine(files.Directory, "result.cy.vtt")) == CaptionChecks.Caption && requests.Count == 5,
            "The native PeerTube path selected the wrong stream or lost automatic captions.");
    }
    private static async Task OptionalEndpoints()
    {
        var info = await Engine(request => request.RequestUri!.AbsolutePath.EndsWith(ShortId) ? Ok(Json) : new HttpResponseMessage(HttpStatusCode.NotFound))
            .InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Formats.Count == 3 && info.Subtitles.Count == 0 && info.Chapters.Count == 0, "Missing optional endpoints removed media formats.");
    }
    private static async Task Playlist()
    {
        using var files = new Files();
        var queries = new List<string>();
        var engine = PlaylistEngine(queries);
        var info = await engine.InspectAsync(PlaylistRequest(files.Directory), default);
        Assert(info.IsPlaylist && info.Entries.Count == 3 && !info.Entries[1].Available && info.Entries[2].ResolvedMedia!.Warnings.Single().Contains("ranges"), "Unavailable or clipped entries were changed.");
        Assert(queries.SequenceEqual(["?start=0&count=100", "?start=2&count=100"]), "Continuation offsets changed.");
        var result = await engine.DownloadAsync(PlaylistRequest(files.Directory) with { PlaylistItems = "3" }, null, null, default);
        Assert(!result.Succeeded && result.OutputPaths.Count == 0 && result.Failures[0].Message.Contains("ranges"), "A clipped playlist entry silently became the full video.");
    }
    private static async Task PlaylistFailure()
    {
        foreach (var changed in new[] { false, true })
            await Expect<InvalidDataException>(() => PlaylistEngine([], changed, emptyPage: !changed).InspectAsync(PlaylistRequest(Path.GetTempPath()), default));
    }
    private static NativeEngine PlaylistEngine(List<string> queries, bool changed = false, bool emptyPage = false) => Engine(request =>
    {
        var url = request.RequestUri!;
        if (!url.AbsolutePath.EndsWith("/videos")) return Ok(JsonSerializer.Serialize(new { uuid = PlaylistId, displayName = "PeerTube playlist" }));
        queries.Add(url.Query);
        var first = url.Query.StartsWith("?start=0");
        var entries = first ? new object[]
        {
            new { position = 1, video = new { uuid = Id, name = "First", duration = 9 } },
            new { position = 2, video = (object?)null }
        } : emptyPage ? [] : [new { position = 3, startTimestamp = 2, video = new { uuid = Id, name = "Clip", duration = 9 } }];
        return Ok(JsonSerializer.Serialize(new { total = changed && !first ? 4 : 3, data = entries }));
    });
    private const string Captions = "{\"total\":1,\"data\":[{\"language\":{\"id\":\"cy\"},\"automaticallyGenerated\":true,\"captionPath\":\"/caption.vtt\"}]}";
    private static PeerTubeAddress Address() => PeerTubeAddress.Parse(new Uri("https://instance.example/w/" + ShortId));
    private static NativeDownloadRequest Request(string directory) => new() { Url = new Uri("https://instance.example/w/" + ShortId), OutputDirectory = directory, FileName = "result", Retries = 0 };
    private static NativeDownloadRequest PlaylistRequest(string directory) => Request(directory) with { Url = new Uri("https://instance.example/w/p/" + PlaylistId) };
    private static NativeEngine Engine(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(httpFactory: _ => new MediaHttp(new Handler(respond)));
    private static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Files : IDisposable
    {
        public string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        public Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
