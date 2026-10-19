using System.Net;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.YouTube;
using YoutubeDl.Engine.Providers.Vimeo;
using YoutubeDl.Engine.Providers.Wistia;

internal static class ThumbnailChecks
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private static readonly byte[] Jpeg = [0xff, 0xd8, 0xff, 0xd9];
    private static readonly byte[] Webp = "RIFF0000WEBPfixture"u8.ToArray();
    private static PageMetadata Page() => new() { Id = "thumbs", Title = "Thumbnails", Extractor = "fixture", ThumbnailUrl = "https://images.example/large.jpg",
        Thumbnails = [new() { Id = "small", Url = new("https://images.example/small.jpg"), Width = 100, Height = 100 },
            new() { Id = "../poster", Url = new("https://images.example/large.jpg"), Width = 500, Height = 500 },
            new() { Id = "small", Url = new("https://images.example/webp.jpg"), Width = 320, Height = 180 },
            new() { Id = "duplicate", Url = new("https://images.example/large.jpg") }] };
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Thumbnail inventories preserve preferred images and safe distinct IDs", () => Sync(Inventory)),
        ("All-thumbnail jobs save unique supplied URLs using actual image formats", SaveAll),
        ("Single-thumbnail requests retain their existing filename and preferred selection", Single),
        ("All-thumbnail no-overwrite preserves each existing variant", Existing),
        ("Failed all-thumbnail downloads cannot publish a partial set", () => Failure(false)),
        ("Canceled all-thumbnail downloads preserve existing files", () => Failure(true)),
        ("Oversized and invalid thumbnail inventories fail before image transfer", Limits),
        ("Thumbnail saving recognizes GIF, BMP and compatible-brand AVIF containers", Formats),
        ("YouTube, Vimeo and Wistia keep their exposed thumbnail variants", () => Sync(ProviderInventories)),
        ("Thumbnail-only inventories provide a preferred image in native inspection", Inspection)
    ];
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Inventory()
    {
        var page = ThumbnailCatalog.Prepare(Page());
        Assert(page.Thumbnails.Count == 3 && page.Thumbnails.Select(image => image.Id).SequenceEqual(new[] { "small", "___poster", "small-2" }) && page.ThumbnailUrl == Page().ThumbnailUrl,
            "Thumbnail identity, deduplication or preferred selection changed.");
        var fallback = ThumbnailCatalog.Prepare(Page() with { ThumbnailUrl = null });
        Assert(fallback.ThumbnailUrl == Page().ThumbnailUrl, "An inventory with dimensions did not choose its highest-area image.");
    }
    private static async Task SaveAll()
    {
        using var files = new Files(); var calls = new List<string>(); var request = Request(files.Directory) with { WriteInfoJson = true, EmbedThumbnail = true };
        var result = await Engine(Page(), calls).DownloadAsync(request, null, null, default);
        Assert(result.Succeeded && result.SidecarPaths.Count == 4 && result.OutputPaths.Count == 0 && calls.Count == 3 && calls.Distinct().Count() == 3, "All-thumbnail saving fetched duplicates or produced media.");
        Assert(File.ReadAllBytes(Path.Combine(files.Directory, "art.small.png")).AsSpan().SequenceEqual(Png) && File.ReadAllBytes(Path.Combine(files.Directory, "art.___poster.jpg")).AsSpan().SequenceEqual(Jpeg) &&
            File.ReadAllBytes(Path.Combine(files.Directory, "art.small-2.webp")).AsSpan().SequenceEqual(Webp), "Thumbnail names or original bytes did not match their actual formats.");
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(files.Directory, "art.info.json")));
        Assert(json.RootElement.GetProperty("thumbnails").GetArrayLength() == 3, "Info JSON omitted thumbnail inventory.");
    }
    private static async Task Single()
    {
        using var files = new Files(); var calls = new List<string>();
        var result = await Engine(Page(), calls).DownloadAsync(Request(files.Directory) with { WriteAllThumbnails = false, WriteThumbnail = true }, null, null, default);
        Assert(result.Succeeded && result.SidecarPaths.Single() == Path.Combine(files.Directory, "art.jpg") && calls.SequenceEqual(new[] { "/large.jpg" }), "Single-thumbnail behavior changed when an inventory was present.");
    }
    private static async Task Existing()
    {
        using var files = new Files(); var path = Path.Combine(files.Directory, "art.small.png"); File.WriteAllText(path, "keep existing");
        var result = await Engine(Page(), []).DownloadAsync(Request(files.Directory) with { NoOverwrites = true }, null, null, default);
        Assert(result.SidecarPaths.Count == 3 && result.NewSidecarPaths.Count == 2 && File.ReadAllText(path) == "keep existing", "No-overwrite did not apply to individual thumbnails.");
    }
    private static async Task Failure(bool cancel)
    {
        using var files = new Files(); using var stop = new CancellationTokenSource(); var old = Path.Combine(files.Directory, "art.small.png"); File.WriteAllText(old, "old thumbnail");
        var engine = Engine(Page(), [], path => { if (cancel && path == "/large.jpg") stop.Cancel(); }, bad: !cancel);
        var action = () => engine.DownloadAsync(Request(files.Directory), null, null, stop.Token);
        if (cancel) await Expect<OperationCanceledException>(action); else await Expect<InvalidDataException>(action);
        Assert(File.ReadAllText(old) == "old thumbnail" && Directory.EnumerateFiles(files.Directory).Count() == 1, "A failed image set overwrote previous files or published partial images.");
    }
    private static async Task Limits()
    {
        using var files = new Files(); var calls = new List<string>();
        var excessive = Page() with { Thumbnails = Enumerable.Range(0, 257).Select(index => new ThumbnailCandidate { Url = new("https://images.example/" + index) }).ToArray() };
        await Expect<InvalidDataException>(() => Engine(excessive, calls).DownloadAsync(Request(files.Directory), null, null, default));
        var invalid = Page() with { Thumbnails = [new() { Url = new("file:///private/image.jpg") }] };
        await Expect<InvalidDataException>(() => Engine(invalid, calls).InspectAsync(Request(files.Directory), default));
        Assert(calls.Count == 0 && !Directory.EnumerateFiles(files.Directory).Any(), "Invalid inventory reached image transfer or publication.");
    }
    private static async Task Formats()
    {
        using var files = new Files();
        var avif = new byte[24]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(avif, 24); "ftypmif1"u8.CopyTo(avif.AsSpan(4)); "avif"u8.CopyTo(avif.AsSpan(16));
        var images = new Dictionary<string, byte[]> { ["gif"] = "GIF89a0000000"u8.ToArray(), ["bmp"] = "BM000000000000"u8.ToArray(), ["avif"] = avif };
        var page = Page() with { ThumbnailUrl = null, Thumbnails = images.Keys.Select(id => new ThumbnailCandidate { Id = id, Url = new("https://images.example/" + id) }).ToArray() };
        var engine = new NativeEngine([new Provider(page)], _ => new MediaHttp(new Handler(request => new(HttpStatusCode.OK) { Content = new ByteArrayContent(images[request.RequestUri!.AbsolutePath.TrimStart('/')]) })));
        var result = await engine.DownloadAsync(Request(files.Directory), null, null, default);
        Assert(result.SidecarPaths.Count == 3 && images.Keys.All(extension => File.Exists(Path.Combine(files.Directory, "art." + extension + "." + extension))), "A supported thumbnail container was mislabeled.");
    }
    private static void ProviderInventories()
    {
        var youtube = YouTubePlayerParser.ParseResponse("""{"playabilityStatus":{"status":"OK"},"videoDetails":{"videoId":"abcdefghijk","title":"Fixture","thumbnail":{"thumbnails":[{"url":"https://images.example/one.jpg","width":120,"height":90},{"url":"https://images.example/two.jpg","width":480,"height":360}]}}}""", "abcdefghijk");
        Assert(youtube.Thumbnails.Count == 2 && youtube.ThumbnailUrl == "https://images.example/two.jpg", "YouTube reduced its thumbnail inventory to one image.");
        var vimeo = VimeoProvider.ParseConfiguration("""{"video":{"id":12345,"thumbs":{"640":"https://images.example/one.jpg","1280":"https://images.example/two.jpg"}},"request":{"files":{}}}""", "12345");
        Assert(ThumbnailCatalog.Prepare(vimeo).Thumbnails.Count == 2 && ThumbnailCatalog.Prepare(vimeo).ThumbnailUrl == "https://images.example/two.jpg", "Vimeo legacy thumbnail sizes were lost.");
        var wistia = WistiaProvider.ParseMedia("""{"media":{"hashedId":"e4a27b971d","assets":[{"type":"still_image","url":"https://images.example/one.jpg","status":2},{"type":"still_image","url":"https://images.example/two.jpg","status":2}]}}""", "e4a27b971d");
        Assert(wistia.Thumbnails.Count == 2, "Wistia still-image variants were dropped.");
    }
    private static async Task Inspection()
    {
        var info = await Engine(Page() with { ThumbnailUrl = null }, []).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Thumbnails.Count == 3 && info.ThumbnailUrl == Page().ThumbnailUrl, "Native inspection lost its thumbnail inventory or preferred image.");
        Assert(NativeExtraArguments.Apply(Request(Path.GetTempPath()) with { WriteAllThumbnails = false }, ["--write-all-thumbnails"]).WriteAllThumbnails, "The all-thumbnail extra argument was not mapped.");
    }
    private static NativeEngine Engine(PageMetadata page, List<string> calls, Action<string>? before = null, bool bad = false) => new([new Provider(page)], _ => new MediaHttp(new Handler(request =>
    {
        var path = request.RequestUri!.AbsolutePath; calls.Add(path); before?.Invoke(path);
        return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bad && path == "/large.jpg" ? "<html>error</html>"u8.ToArray() : path == "/small.jpg" ? Png : path == "/large.jpg" ? Jpeg : Webp) };
    })));
    private static NativeDownloadRequest Request(string directory) => new() { Url = new("https://fixture.example/watch"), OutputDirectory = directory, FileName = "art", SkipDownload = true, IgnoreNoFormatsError = true, WriteAllThumbnails = true, Retries = 0 };
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Provider(PageMetadata page) : IMediaProvider
    { public MediaProviderInfo Info { get; } = new("fixture", "Fixture", 1000); public bool CanHandle(Uri url) => true; public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(page); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, "thumbnails-" + Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() { var path = Path.GetFullPath(Directory); if (!path.StartsWith(Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Test folder escaped its root."); System.IO.Directory.Delete(path, true); }
    }
}
