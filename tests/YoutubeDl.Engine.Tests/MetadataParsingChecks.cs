using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.YouTube;

internal static class MetadataParsingChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Metadata parsing capture templates retain descriptive fields and provider identities", Templates),
        ("Metadata parsing named regex groups references and field copies preserve text", RegexAndCopy),
        ("Metadata parsing ordered rules read prior values without mutating source snapshots", Ordered),
        ("Metadata parsing missing sources and unmatched patterns preserve existing metadata", Unmatched),
        ("Metadata parsing optional captures multiline text and empty overrides remain explicit", Optional),
        ("Metadata parsing input templates preserve formatting fallbacks and literal percent", Inputs),
        ("Metadata parsing reserved fields and unsupported stages fail before HTTP or output", Reserved),
        ("Metadata parsing invalid syntax duplicate captures and limits fail before HTTP", Invalid),
        ("Metadata parsing publication uses parsed filenames filters and info JSON", Publication),
        ("Metadata parsing inspected playlist selections retain source positions and snapshots", Snapshots),
        ("Metadata parsing regex timeouts abort ignored playlists before later entries", Timeout),
        ("Metadata parsing cancellation oversized inputs and structured values stay bounded", Bounds),
        ("Metadata parsing YouTube title filters use transformed metadata instead of early originals", YouTube)
    ];
    internal static NativeDownloadRequest Request(string directory, params string[] rules) => new()
    { Url = new("https://parse.example/watch"), OutputDirectory = directory, MetadataParseRules = rules, Retries = 0, DownloadPlaylist = false };
    internal static PageMetadata Page => new()
    {
        Id = "42", ArchiveIdentity = new("parsefixture", "42"), Extractor = "parsefixture", Title = "Artist - Track", Description = "first\nsecond",
        Uploader = "Publisher", DurationSeconds = 2, SourcePageUrl = new("https://parse.example/watch"), UploadDate = new(2026, 1,31),
        Media = [new() { Url = new("https://parse.example/audio.mp3"), Extension = "mp3", Kind = MediaKind.Progressive, HasAudio = true, HasVideo = false }],
        MetadataFields = new Dictionary<string, object?> { ["season_number"] = 3 }
    };
    private static PageMetadata Parse(PageMetadata page, params string[] rules) => NativeMetadataParser.Apply(Request(".", rules), page, null, default);
    private static Task Templates()
    {
        var page = Parse(Page, "title:%(artist)s - %(title)s");
        Assert(page.Title == "Track" && page.ParsedMetadata["artist"] == "Artist" && page.Id == "42" && page.DurationSeconds == 2 && page.ArchiveIdentity == Page.ArchiveIdentity && page.Media[0].Url == Page.Media[0].Url, "Parsing changed source identity or complete media facts.");
        Assert(NativeMetadataParser.EmbeddedTags(Request("."), page)["artist"] == "Artist", "Parsed artist did not reach metadata."); return Task.CompletedTask;
    }
    private static Task RegexAndCopy()
    {
        var page = Parse(Page with { Title = "Artist Artist" }, @"title:(?P<artist>\w+) (?P=artist)", "description:meta_comment");
        Assert(page.ParsedMetadata["artist"] == "Artist" && page.ParsedMetadata["meta_comment"] == "first\nsecond", "Named reference or whole field copy lost text."); return Task.CompletedTask;
    }
    private static Task Ordered()
    {
        var source = Page; var page = Parse(source, "pre_process:title:%(artist)s - %(title)s", "title:album", "%(artist)s / %(album)s:title");
        Assert(page.Title == "Artist / Track" && page.ParsedMetadata["album"] == "Track" && source.Title == "Artist - Track" && source.ParsedMetadata.Count == 0, "Ordered metadata rules mutated the source or ignored previous results."); return Task.CompletedTask;
    }
    private static Task Unmatched()
    {
        var warnings = new List<string>(); var source = Page; var page = NativeMetadataParser.Apply(Request(".", "missing:artist", "title:(?P<artist>^never$)"), source, warnings.Add, default);
        Assert(page == source && warnings.Count == 2 && !warnings.Any(value => value.Contains("Artist - Track")), "Skipped parsing changed metadata or exposed source text."); return Task.CompletedTask;
    }
    private static Task Optional()
    {
        var page = Parse(Page, @"title:(?P<artist>Artist)(?: (?P<album>missing))?", "description:(?s)(?P<meta_comment>.+)", ":(?P<meta_synopsis>)");
        var tags = NativeMetadataParser.EmbeddedTags(Request("."), page);
        Assert(page.ParsedMetadata["album"] is null && tags["comment"] == "first\nsecond" && tags["synopsis"] == "", "Optional captures or empty embedded overrides were lost."); return Task.CompletedTask;
    }
    private static Task Inputs()
    {
        var page = Parse(Page, "%(season_number)02d %% %(missing|fallback)s:meta_album");
        Assert(page.ParsedMetadata["meta_album"] == "03 % fallback", "Input template formatting or fallbacks changed.");
        var request = NativeExtraArguments.Apply(Request("."), ["--metadata-from-title", "%(artist)s - %(title)s", "--parse-metadata=title:album"]);
        Assert(request.MetadataParseRules.SequenceEqual(new[] { "title:%(artist)s - %(title)s", "title:album" }), "Metadata arguments changed ordering or aliases."); return Task.CompletedTask;
    }
    private static async Task Reserved()
    {
        var engine = new NativeEngine(httpFactory: _ => throw new InvalidOperationException("Unsupported metadata reached HTTP."));
        foreach (var field in new[] { "id", "url", "duration", "formats", "additional_urls", "playlist_index", "meta1_language", "http_headers" })
            await Expect<NotSupportedException>(() => engine.DownloadAsync(Request(".", "title:" + field), null, null, default));
        await Expect<NotSupportedException>(() => engine.DownloadAsync(Request(".", "after_move:title:artist"), null, null, default));
    }
    private static async Task Invalid()
    {
        var engine = new NativeEngine(httpFactory: _ => throw new InvalidOperationException("Invalid metadata reached HTTP."));
        foreach (var rule in new[] { "private-value", "title:", "title:(?P<artist>[)", "title:%(artist)s %(artist)s", "title:plain literal without captures" })
            await Expect<ArgumentException>(() => engine.DownloadAsync(Request(".", rule), null, null, default));
        await Expect<ArgumentException>(() => engine.DownloadAsync(Request(".", Enumerable.Repeat("title:artist", 33).ToArray()), null, null, default));
        await Expect<NotSupportedException>(() => engine.DownloadAsync(Request(".", "title:%(artist)02d"), null, null, default));
        await Expect<NotSupportedException>(() => engine.DownloadAsync(Request(".", "title:(?x)(?P<artist>a) # (?P<title>b)"), null, null, default));
    }
    private static async Task Publication()
    {
        using var files = new Files(); var source = Page with { Title = "Artist - ../Track" };
        var request = Request(files.Path, "title:%(artist)s - %(title)s") with { OutputTemplate = "%(artist)s-%(title)s.%(ext)s", WriteInfoJson = true, MatchTitle = "Track" };
        var result = await Engine(source).DownloadAsync(request, null, null, default);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(result.SidecarPaths.Single()));
        Assert(result.Succeeded && Path.GetDirectoryName(result.OutputPath) == files.Path && Path.GetFileName(result.OutputPath) == "Artist-.._Track.mp3" && json.RootElement.GetProperty("title").GetString() == "../Track" && json.RootElement.GetProperty("artist").GetString() == "Artist", "Publication did not sanitize parsed paths or retain text metadata.");
        var rejected = await Engine(source).DownloadAsync(request with { OutputDirectory = Path.Combine(files.Path,"rejected"), RejectTitle = "Track" }, null, null, default);
        Assert(rejected.Filtered && !Directory.Exists(Path.Combine(files.Path,"rejected")), "Parsed title rejection created output.");
    }
    private static async Task Snapshots()
    {
        using var files = new Files(); var source = Page; var collection = source with { IsPlaylist = true, Entries = [new(1,"one","One",new("https://parse.example/one"),2,null,true,source),new(2,"42",source.Title,new("https://parse.example/watch"),2,null,true,source)] };
        var engine = Engine(collection); var request = Request(files.Path, "title:%(artist)s - %(title)s") with { DownloadPlaylist = true, PlaylistItems = "2", OutputTemplate = "%(playlist_index)s-%(title)s.%(ext)s", WriteInfoJson = true };
        var info = await engine.InspectAsync(request, default); var result = await engine.DownloadAsync(request with { PlaylistSnapshot = info.PlaylistSnapshot }, null, null, default);
        Assert(result.Succeeded && Path.GetFileName(result.OutputPaths.Single()) == "2-Track.mp3" && source.Title == "Artist - Track" && source.ParsedMetadata.Count == 0, "Parsed snapshots lost positions or mutated provider metadata.");
    }
    private static async Task Timeout()
    {
        using var files = new Files(); var page = Page with { Description = new string('a', 200000) + "b" }; var source = page with { IsPlaylist = true, Entries = [new(1,"42",page.Title,new("https://parse.example/watch"),2,null,true,page),new(2,"later","Later",new("https://parse.example/later"),2,null,true,page)] };
        var request = Request(Path.Combine(files.Path,"outputs"), "description:(?P<artist>^(?P<nested>a+)+$)") with { DownloadPlaylist = true, IgnoreErrors = true };
        await Expect<NativeMetadataParseException>(() => Task.FromResult(NativeMetadataParser.Apply(request,page,null,default)));
        await Expect<NativeMetadataParseException>(() => Engine(source).DownloadAsync(request, null, null, default));
        Assert(!Directory.Exists(request.OutputDirectory), "Timed-out parsing published output.");
    }
    private static async Task Bounds()
    {
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Expect<OperationCanceledException>(() => Task.FromResult(NativeMetadataParser.Apply(Request(".", "title:artist"), Page, null, canceled.Token)));
        await Expect<NativeMetadataParseException>(() => Task.FromResult(Parse(Page with { Description = new string('a',1024*1024+1) }, "description:artist")));
        await Expect<NativeMetadataParseException>(() => Task.FromResult(Parse(Page with { Description = new string('a',1024*1024+1) }, "%(description)s:artist")));
        try { Parse(Page with { Description = new string('a',1024*1024) }, Enumerable.Range(0,5).Select(index=>"description:field"+index).ToArray()); throw new InvalidOperationException("Combined metadata limit was ignored."); }
        catch (NativeMetadataParseException ex) { Assert(ex.Message.Contains("Combined"), "Combined metadata limit was not exercised."); }
        await Expect<NativeMetadataParseException>(() => Task.FromResult(Parse(Page with { MetadataFields = new Dictionary<string,object?> { ["tags"] = new[] { "one","two" } } }, "tags:artist")));
    }
    private static async Task YouTube()
    {
        var player = JsonSerializer.Serialize(new { playabilityStatus = new { status = "OK" }, videoDetails = new { videoId = "jNQXAC9IVRw", title = "Original", shortDescription = "Changed", lengthSeconds = "18", author = "Uploader" } });
        var request = Request(".", "description:title") with { Url = new("https://www.youtube.com/watch?v=jNQXAC9IVRw"), MatchTitle = "Changed", Simulate = true, IgnoreNoFormatsError = true };
        var engine = new NativeEngine([new YouTubeProvider()], _ => new MediaHttp(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("<script>var ytInitialPlayerResponse=" + player + ";</script>") })));
        var result = await engine.DownloadAsync(request, null, null, default);
        Assert(result.Succeeded && result.Simulated && !result.Filtered, "YouTube rejected the source title before metadata parsing.");
    }
    internal static NativeEngine Engine(PageMetadata page, byte[]? bytes = null, Action? media = null) => new([new Provider(page)], _ => new MediaHttp(new Handler(_ => { media?.Invoke(); return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes ?? "fixture media"u8.ToArray()) }; })));
    internal sealed class Provider(PageMetadata page) : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("parsefixture","Metadata fixture",1000); public bool CanHandle(Uri url) => url.Host == "parse.example";
        private PageMetadata Resolve(NativeDownloadRequest request) => page.IsPlaylist && !request.DownloadPlaylist
            ? page.Entries.FirstOrDefault(entry => entry.Url == request.Url)?.ResolvedMedia ?? page : page;
        public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) { var resolved = Resolve(request); return resolved.IsPlaylist ? null : resolved.ArchiveIdentity; }
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(Resolve(request));
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(response(request)); } }
    private sealed class Files : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory,"test-data","metadata-parsing-"+Guid.NewGuid().ToString("N"));
        internal Files() => Directory.CreateDirectory(Path); public void Dispose() => Directory.Delete(Path,true);
    }
    private static void Assert(bool value,string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
