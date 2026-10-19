using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.Newgrounds;

internal static class NewgroundsChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Newgrounds literal player data is parsed without executing computed expressions", Literals),
        ("Newgrounds public audio facts and literal supplied source remain bound", Audio),
        ("Newgrounds numeric namespaces and aliases preserve typed archive identity", Aliases),
        ("Newgrounds unsupported filtered private and credential routes cannot fall through", Boundaries),
        ("Newgrounds conflicting player source image and identity records fail", Conflicts),
        ("Newgrounds movie barriers bind the offered MP4 qualities to their project", Video),
        ("Newgrounds wrong movie project and canonical identity cannot be accepted", VideoConflicts),
        ("Newgrounds complete creator continuation preserves original positions", Creator),
        ("Newgrounds missing duplicate changing and excessive creator inventories fail", CreatorErrors),
        ("Newgrounds playlist metadata retains declared order and unavailable positions", Playlist),
        ("Newgrounds changed missing and conflicting playlist records fail", PlaylistErrors),
        ("Newgrounds inspected selections refresh only selected immutable media", Snapshots),
        ("Newgrounds typed archives skip source access and completion effects", Archives),
        ("Newgrounds public browser discovery is explicit cancellable and limited to denials", Browser)
    ];
    private static Task Literals()
    {
        var parsed = JavaScriptDataLiteral.ObjectArgument(" {'id':42,'url':'https:\\/\\/audio.ngfiles.com/a.mp3','ok':true,'values':[null,-1.5]} ,0)", 0, out var end, default);
        Assert(parsed["id"]!.GetValue<double>() == 42 && parsed["url"]!.GetValue<string>() == "https://audio.ngfiles.com/a.mp3" && end > 10, "Literal values changed.");
        foreach (var source in new[] { "{'id':alert(1)}", "{'id':window.secret}", "{'id':42,'id':43}", "{'id':1+2}", "{'url':'unterminated}" })
            ExpectSync<InvalidDataException>(() => JavaScriptDataLiteral.ObjectArgument(source, 0, out _, default));
        return Task.CompletedTask;
    }
    private static async Task Audio()
    {
        using var http = Http(); var page = await new NewgroundsProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
        Assert(page.Id == "42" && page.ArchiveIdentity == new DownloadArchiveIdentity("newgrounds:audio", "42") && page.Uploader == "Artist" && page.DurationSeconds == 2 &&
            page.Media.Single().Url.AbsoluteUri == AudioUrl && page.Media.Single().VerificationDemuxer == "mp3" && page.ViewCount == 12 && page.UploadDate == new DateOnly(2020, 1, 1), "Newgrounds audio facts changed.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "http://newgrounds.com/audio/listen/42", "https://www.newgrounds.com/audio/listen/42/" })
        { using var http = Http(); var page = await new NewgroundsProvider().ExtractAsync(Request(Path.GetTempPath()) with { Url = new(url) }, http, default); Assert(page.Id == "42", "Audio alias changed ID."); }
        var provider = new NewgroundsProvider();
        Assert(provider.GetArchiveIdentity(Request(Path.GetTempPath())) != provider.GetArchiveIdentity(Request(Path.GetTempPath(), "video")), "Audio and portal numeric IDs collide in native archives.");
    }
    private static Task Boundaries()
    {
        foreach (var url in new[] { "https://www.newgrounds.com/audio/listen/42?token=secret", "https://www.newgrounds.com/audio/listen/42#t=1", "https://user:secret@www.newgrounds.com/audio/listen/42",
            "https://www.newgrounds.com:8443/audio/listen/42", "https://www.newgrounds.com/passport", "https://artist.newgrounds.com/audio?page=2", "https://newgrounds.com.evil.example/audio/listen/42" })
            Assert(!new NewgroundsProvider().CanHandle(new(url)), "Unsupported Newgrounds route accepted.");
        Assert(!new GenericPageProvider().CanHandle(new("https://www.newgrounds.com/private/42")), "Unsupported Newgrounds reached generic extraction.");
        Assert(!NewgroundsProvider.IsMetadataAddress(new("https://www.newgrounds.com/portal/video/43"), new("https://www.newgrounds.com/portal/view/42")), "Browser metadata scope widened to another movie.");
        return Task.CompletedTask;
    }
    private static async Task Conflicts()
    {
        foreach (var html in new[] { AudioHtml().Replace("'generic_id':42", "'generic_id':43"), AudioHtml().Replace("'type_id':3", "'type_id':1"),
            AudioHtml().Replace("42_Test.mp3", "43_Test.mp3"), AudioHtml().Replace("audio.ngfiles.com", "other.example"), AudioHtml().Replace("42_cover", "43_cover") })
        { using var http = Http(audio: html); await Expect<InvalidDataException>(() => new NewgroundsProvider().ExtractAsync(Request(Path.GetTempPath()), http, default)); }
    }
    private static async Task Video()
    {
        using var http = Http(); var page = await new NewgroundsProvider().ExtractAsync(Request(Path.GetTempPath(), "video"), http, default);
        Assert(page.Media.Count == 2 && page.Media.Select(media => media.Height).Order().SequenceEqual(new int?[] { 360, 720 }) && page.Media.All(media => media.HasVideo == true && media.VerificationDemuxer == "mov"), "Newgrounds movie binding changed.");
    }
    private static async Task VideoConflicts()
    {
        foreach (var json in new[] { VideoJson().Replace("\"id\":42", "\"id\":43"), VideoJson().Replace("7_alternate", "8_alternate"), VideoJson().Replace("uploads.ungrounded.net", "other.example") })
        { using var http = Http(video: json); await Expect<InvalidDataException>(() => new NewgroundsProvider().ExtractAsync(Request(Path.GetTempPath(), "video"), http, default)); }
        using var noMovie = Http(movie: "<h2>Game</h2>"); await Expect<NotSupportedException>(() => new NewgroundsProvider().ExtractAsync(Request(Path.GetTempPath(), "video"), noMovie, default));
    }
    private static async Task Creator()
    {
        using var http = Http(); var page = await new NewgroundsProvider().ExtractAsync(Request(Path.GetTempPath(), "user"), http, default);
        Assert(page.Entries.Count == 2 && page.Entries.Select(entry => entry.Id).SequenceEqual(new[] { "42", "43" }) && page.Entries[1].Position == 2 && page.Entries[1].ExpectedArchiveIdentity == new DownloadArchiveIdentity("newgrounds:audio", "43"), "Creator continuation changed source positions.");
    }
    private static async Task CreatorErrors()
    {
        foreach (var continuation in new[] { CreatorPage().Replace("/audio?page=2", "/audio?page=3"), CreatorPage().Replace("/listen/43", "/listen/42"), CreatorPage().Replace("\"items\"", "\"missing\"") })
        { using var http = Http(creatorPage: continuation); await Expect<InvalidDataException>(() => new NewgroundsProvider().ExtractAsync(Request(Path.GetTempPath(), "user"), http, default)); }
        using var limited = Http(); await Expect<NotSupportedException>(() => new NewgroundsProvider().ExtractAsync(Request(Path.GetTempPath(), "user") with { MaximumPlaylistEntries = 1 }, limited, default));
        var reads = 0; using var changed = Http(creatorRoot: () => ++reads == 1 ? CreatorHtml() : CreatorHtml().Replace("<strong>2", "<strong>3"));
        await Expect<InvalidDataException>(() => new NewgroundsProvider().ExtractAsync(Request(Path.GetTempPath(), "user"), changed, default));
    }
    private static async Task Playlist()
    {
        using var http = Http(); var page = await new NewgroundsProvider().ExtractAsync(Request(Path.GetTempPath(), "playlist"), http, default);
        Assert(page.Entries.Count == 2 && page.Entries[0].Available && !page.Entries[1].Available && page.Entries[1].Position == 2 && page.Entries[1].Url is null, "Playlist unavailable positions changed.");
    }
    private static async Task PlaylistErrors()
    {
        foreach (var json in new[] { PlaylistJson().Replace("\"42\":\"<li", "\"44\":\"<li"), PlaylistJson().Replace("/listen/42", "/listen/44"), PlaylistJson().Replace("\"success\":true", "\"success\":false") })
        { using var http = Http(playlistJson: json); await Expect<Exception>(() => new NewgroundsProvider().ExtractAsync(Request(Path.GetTempPath(), "playlist"), http, default)); }
        var reads = 0; using var changed = Http(playlistRoot: () => ++reads == 1 ? PlaylistHtml() : PlaylistHtml().Replace("[3,43]", "[3,44]"));
        await Expect<InvalidDataException>(() => new NewgroundsProvider().ExtractAsync(Request(Path.GetTempPath(), "playlist"), changed, default));
    }
    private static async Task Snapshots()
    {
        using var files = new Files(); var calls = new List<Uri>(); var engine = new NativeEngine([new NewgroundsProvider()], _ => Http(calls: calls));
        var request = Request(files.Path, "user") with { Simulate = true, PlaylistItems = "2" }; var inspected = await engine.InspectAsync(request, default); calls.Clear();
        var result = await engine.DownloadAsync(request with { PlaylistSnapshot = inspected.PlaylistSnapshot }, null, null, default);
        Assert(result.Succeeded && calls.Count == 1 && calls[0].AbsolutePath == "/audio/listen/43", "Snapshot refreshed an unselected creator source.");
    }
    private static async Task Archives()
    {
        using var files = new Files(); var archive = Path.Combine(files.Path, "archive.txt"); await File.WriteAllTextAsync(archive, "newgrounds:audio 42\n");
        var engine = new NativeEngine([new NewgroundsProvider()], _ => throw new InvalidOperationException("Archived source contacted Newgrounds."));
        var result = await engine.DownloadAsync(Request(files.Path) with { DownloadArchiveFile = archive }, null, null, default, _ => throw new InvalidOperationException("Archived source emitted completion."));
        Assert(result.Succeeded && result.Skipped, "Typed Newgrounds archive did not skip.");
    }
    private static async Task Browser()
    {
        var source = new FakeBrowser(); using var denied = new MediaHttp(new Handler(_ => new(HttpStatusCode.Forbidden) { Content = new StringContent("NG Guard") }));
        var result = await new NewgroundsProvider(source).ExtractAsync(Request(Path.GetTempPath()), denied, default); Assert(source.Calls == 1 && result.Id == "42", "Explicit browser discovery was dropped.");
        using var error = new MediaHttp(new Handler(_ => new(HttpStatusCode.InternalServerError) { Content = new StringContent("failed") }));
        await Expect<HttpRequestException>(() => new NewgroundsProvider(source).ExtractAsync(Request(Path.GetTempPath()), error, default)); Assert(source.Calls == 1, "Server errors were hidden by browser fallback.");
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Expect<OperationCanceledException>(() => new NewgroundsProvider(source).ExtractAsync(Request(Path.GetTempPath()), denied, cancel.Token));
    }

    internal static NativeDownloadRequest Request(string output, string kind = "audio") => new() { Url = new(kind switch { "video" => "https://www.newgrounds.com/portal/view/42", "user" => "https://artist.newgrounds.com/audio", "playlist" => "https://www.newgrounds.com/playlist/7", _ => "https://www.newgrounds.com/audio/listen/42" }), OutputDirectory = output, OutputTemplate = "%(id)s.%(ext)s" };
    internal const string AudioUrl = "https://audio.ngfiles.com/0/42_Test.mp3?f123";
    internal static string AudioHtml(string id = "42", int duration = 2) => "<html><meta property='og:title' content='Audio'><h2>Audio</h2><dl><dt>Uploaded</dt><dd>Jan 1, 2020<br>1:00 PM EST</dd><dt>Listens</dt><dd>12</dd></dl><script>var player=NgAudioPlayer.fromListenPage({'generic_id':" + id + ",'type_id':3,'duration':" + duration + ",'url':'https://audio.ngfiles.com/0/" + id + "_Test.mp3?f123','title':'Audio','author':'Artist','icon_url':'https://aicon.ngfiles.com/0/" + id + "_cover.webp'},128);</script></html>";
    private static string MovieHtml() => "<html><meta property='og:title' content='Movie'><h2>Movie</h2><div id='authorlinks7'></div><script>ngutils.components.video.player.getGlobal({\"barrier\":\"<div data-movie-id=\\\"42\\\"></div>\"});</script></html>";
    internal static string VideoJson() => "{\"id\":42,\"title\":\"Movie\",\"author\":\"Artist\",\"sources\":{\"720p\":[{\"src\":\"https://uploads.ungrounded.net/alternate/0/7_alternate_9.720p.mp4?literal%2Bquery\",\"type\":\"video/mp4\"}],\"360p\":[{\"src\":\"https://uploads.ungrounded.net/alternate/0/7_alternate_9.360p.mp4\",\"type\":\"video/mp4\"}]}}";
    private static string Row(string id) => "<a href='https://www.newgrounds.com/audio/listen/" + id + "' class='item-audiosubmission' title='Audio " + id + "'><h4>Audio " + id + "</h4></a>";
    private static string CreatorHtml() => "<html><meta property='og:title' content='Artist audio'><a href='/audio' class='user-header-button'><span>AUDIO</span><strong>2</strong></a>" + Row("42") + "<script>UsersContentController.loadMore($('#x'),\"https://artist.newgrounds.com/audio?page=2\",\"li\",'ul.itemlist','x');</script></html>";
    private static string CreatorPage() => JsonSerializer.Serialize(new { url = "https://artist.newgrounds.com/audio?page=2", fresh_url = "https://artist.newgrounds.com/audio", items = new Dictionary<string, string[]> { ["2020"] = [Row("43")] }, load_more = "" });
    private static string PlaylistHtml() => "<html><div id='playlist_list_pod'><h2>List</h2><ul id='playlist_list'><li data-visual-link='[3,42]'></li><li data-visual-link='[3,43]'></li></ul></div></html>";
    private static string PlaylistJson() => "{\"success\":true,\"partials\":{\"3\":{\"42\":\"<li>Audio</li>\",\"43\":\"<li class='visual-link-unsuitable'>Unavailable</li>\"}},\"simples\":{\"3\":{\"42\":{\"id\":42,\"title\":\"Audio\",\"url\":\"https://www.newgrounds.com/audio/listen/42\",\"status\":{\"unpublished\":false}},\"43\":{\"id\":43,\"title\":\"Unavailable\",\"url\":\"https://www.newgrounds.com/audio/listen/43\",\"status\":{\"unpublished\":false}}}}}";
    internal static MediaHttp Http(string? audio = null, string? movie = null, string? video = null, string? creatorPage = null, string? playlistJson = null, Func<string>? creatorRoot = null, Func<string>? playlistRoot = null, List<Uri>? calls = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null)
        => new(new Handler(request =>
        {
            var uri = request.RequestUri!; calls?.Add(uri); var body = uri.AbsolutePath switch { "/audio/listen/42" => audio ?? AudioHtml(), "/audio/listen/43" => AudioHtml("43"),
                "/portal/view/42" => movie ?? MovieHtml(), "/portal/video/42" => video ?? VideoJson(), "/audio" => uri.Query.Length == 0 ? creatorRoot?.Invoke() ?? CreatorHtml() : creatorPage ?? CreatorPage(),
                "/playlist/7" => playlistRoot?.Invoke() ?? PlaylistHtml(), "/visual-links-fetch" => playlistJson ?? PlaylistJson(), _ => null };
            if (body is null) return media?.Invoke(request) ?? throw new InvalidOperationException("Unexpected Newgrounds source access.");
            return new(HttpStatusCode.OK) { Content = new StringContent(body), RequestMessage = request };
        }));
    private sealed class FakeBrowser : INewgroundsPageSource { internal int Calls; public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(new PageMetadata { Id = "42", Title = "Audio" }); } }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(response(request)); } }
    private sealed class Files : IDisposable { internal string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "test-data", "newgrounds-" + Guid.NewGuid().ToString("N")); internal Files() => Directory.CreateDirectory(Path); public void Dispose() => Directory.Delete(Path, true); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static void ExpectSync<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
