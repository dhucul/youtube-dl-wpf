using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using static MusicFamilyFixture;

internal static class MusicFamilyChecks
{
    private static void Check(bool value, string message) => MusicFamilyFixture.Check(value, message);
    internal static IEnumerable<(string Name, Func<Task> Test)> All
    {
        get
        {
            foreach (var mode in Cases) yield return ("Music families public contract " + mode, () => Valid(mode));
            foreach (var family in new[] { "jio", "qq", "net" })
            {
                foreach (var mode in new[] { "grant-identity", "foreign-media", "preview", "denied", "metadata-identity", "duration", "multiple", "album-count", "limit", "duplicate-page", "parent", "owner", "redirect", "authentication" })
                    yield return ("Music families refusal " + family + " " + mode, () => Bad(family, mode));
                foreach (var suffix in new[] { "?extra=1", "#bad", "/extra", "?id=2&id=3" }) yield return ("Music families source boundary " + family + suffix, () => InvalidSource(family, suffix));
            }
            foreach (var mode in new[] { "quiet", "progress", "warnings", "reset", "cache-cleanup", "cache-inspection", "cache-cancel", "cache-empty", "recovery", "analysis" }) yield return ("Music families option " + mode, () => Options(mode));
            yield return ("Music families normalized NetEase fragment retains archive identity", Fragment);
            yield return ("Music families scoped video and program archive identities do not collide", Archives);
            yield return ("Music families zero-duration Jio catalog cells retain unavailable positions", () => Unavailable("jio"));
            yield return ("Music families zero-duration QQ catalog cells retain unavailable positions", () => Unavailable("qq"));
            yield return ("Music families show uses dedicated episode page boundaries", ShowPages);
            yield return ("Music families podcast issuer may delegate the observed Spreaker endpoint", () => Podcast(false));
            yield return ("Music families song grants cannot borrow podcast delegation", () => Podcast(true));
        }
    }
    private static async Task Valid(string mode)
    {
        var fixture = new MusicFamilyFixture(); var info = await fixture.Engine.InspectAsync(Request(mode), default);
        if (Collection(mode))
        {
            Check(info.IsPlaylist && info.PlaylistSnapshot?.Count == (mode is "jio-playlist" or "net-playlist" ? 3 : 2) && info.Entries.Select(entry => entry.Position).SequenceEqual(Enumerable.Range(1, info.Entries.Count)), "Complete inventory positions were lost.");
            Check(fixture.Grants == 0 && fixture.MediaReads == 0, "Inventory resolved selected playback or fetched media.");
            if (mode is "jio-playlist" or "net-playlist") Check(!info.Entries[2].Available, "Missing details lost an unavailable source position.");
        }
        else Check(!info.IsPlaylist && info.Formats.Count == 1 && info.DurationSeconds == 3 && fixture.Grants == (mode == "net-mv" ? 0 : 1) && fixture.MediaReads == 0, "Public track facts or offered format resolution changed.");
    }
    internal static NativeDownloadRequest Request(string mode) => new() { Url = Source(mode), OutputDirectory = Path.GetTempPath(), Retries = 0 };
    private static async Task Bad(string family, string mode)
    {
        var fixture = new MusicFamilyFixture(); var source = family + (mode is "album-count" or "limit" or "parent" ? "-album" : mode is "duplicate-page" or "owner" ? family == "qq" ? "-singer" : "-artist" : "-song"); var pages = 0;
        fixture.Edit = (operation, node) =>
        {
            if (operation == family + "-grant")
            {
                var row = family == "qq" ? node["midurlinfo"]![0]! : family == "net" ? node["data"]![0]! : node;
                if (mode == "grant-identity") row[family == "jio" ? "pid" : family == "qq" ? "songmid" : "id"] = family == "net" ? JsonValue.Create(999) : JsonValue.Create("another");
                if (mode == "foreign-media") row[family == "jio" ? "auth_url" : family == "qq" ? "purl" : "url"] = "https://evil.example/audio.mp3";
                if (mode == "preview") { if (family == "qq") row["filename"] = "RS02media1.mp3"; else if (family == "net") row["freeTrialInfo"] = new JsonObject { ["start"] = 0, ["end"] = 1 }; else row["is_preview"] = true; }
                if (mode == "denied") { if (family == "qq") row["purl"] = ""; else if (family == "net") row["url"] = null; else { node["status"] = "error"; node["auth_url"] = null; } }
            }
            if (operation == family + "-track")
            {
                var key = family == "qq" ? "tracks" : "songs"; var row = node[key]![0]!;
                if (mode == "metadata-identity") { if (family == "jio") row["perma_url"] = "https://www.jiosaavn.com/song/another/otherToken"; else if (family == "qq") row["mid"] = "otherMid"; else row["id"] = 999; }
                if (mode == "duration") { if (family == "jio") row["more_info"]!["duration"] = 0; else row[family == "qq" ? "interval" : "duration"] = 0; }
                if (mode == "multiple") node[key]!.AsArray().Add(row.DeepClone());
            }
            if (operation == family + "-album")
            {
                var parent = family == "net" ? node["album"]! : node;
                if (mode == "album-count") parent[family == "jio" ? "list_count" : family == "qq" ? "totalNum" : "size"] = 3;
                if (mode == "parent") { if (family == "jio") parent["perma_url"] = "https://www.jiosaavn.com/album/another/otherToken"; else if (family == "qq") parent["albumMid"] = "otherToken"; else parent["id"] = 88; }
            }
            if (operation == family + (family == "qq" ? "-singer" : "-artist"))
            {
                if (family == "jio" && node["topSongs"]!.AsArray().Count == 0) return node;
                pages++; var row = family == "jio" ? node["topSongs"]![0]! : family == "qq" ? node["songList"]![0]!["songInfo"]! : node["songs"]![0]!;
                if (mode == "duplicate-page" && pages > 1)
                {
                    if (family == "jio") node["topSongs"] = new JsonArray(Jio()); else if (family == "qq") node["songList"] = new JsonArray(new JsonObject { ["songInfo"] = Qq() }); else node["songs"] = new JsonArray(Net());
                }
                if (mode == "owner") { if (family == "jio") node["artistId"] = "88"; else if (family == "qq") row["singer"]![0]!["mid"] = "another"; else row["artists"]![0]!["id"] = 88; }
            }
            return node;
        };
        var request = Request(source) with { MaximumPlaylistEntries = mode == "limit" ? 1 : 100 };
        var engine = mode == "redirect" ? new NativeEngine(httpFactory: _ => AdaptiveAccountChecks.Http((message, _) =>
        { var response = fixture.Respond(message); response.RequestMessage = new(HttpMethod.Get, "https://invalid.example/redirect"); return Task.FromResult(response); })) : fixture.Engine;
        if (mode == "authentication") request = request with { Authentication = new() { Username = "fixture-user", Password = "fixture-secret" } };
        if (mode is "preview" or "denied") { var info = await engine.InspectAsync(request, default); Check(info.Formats.Count == 0 && fixture.MediaReads == 0, "A denied or preview source became a complete track."); return; }
        await Fails(() => engine.InspectAsync(request, default)); Check(fixture.MediaReads == 0 && fixture.Calls.Count < 12, "Refusal fetched media or continued unsafe pagination.");
    }
    private static Task InvalidSource(string family, string suffix) => Fails(() => new MusicFamilyFixture().Engine.InspectAsync(Request(family + "-song") with { Url = new(Source(family + "-song").AbsoluteUri + suffix) }, default));
    private static async Task Fragment()
    {
        var fixture = new MusicFamilyFixture(); var request = Request("net-song") with { Url = new("https://music.163.com/#/song?id=101") };
        Check((await fixture.Engine.InspectAsync(request, default)).Id == "101", "NetEase fragment normalization changed its canonical song identity.");
    }
    private static Task Archives()
    {
        var provider = new YoutubeDl.Engine.Providers.NetEase.NetEaseProvider();
        Check(provider.GetArchiveIdentity(Request("net-song"))!.Record == "netease 101" && provider.GetArchiveIdentity(Request("net-program"))!.Record == "netease:program 201" && provider.GetArchiveIdentity(Request("net-mv"))!.Record == "netease:mv 301", "Distinct song/program/video identities collided."); return Task.CompletedTask;
    }
    private static async Task Unavailable(string family)
    {
        var fixture = new MusicFamilyFixture { Edit = (operation, node) =>
        {
            if (operation == "jio-album") node["list"]![0]!["more_info"]!["duration"] = 0;
            if (operation == "qq-singer") node["songList"]![0]!["songInfo"]!["interval"] = 0;
            return node;
        } };
        var info = await fixture.Engine.InspectAsync(Request(family == "jio" ? "jio-album" : "qq-singer"), default);
        Check(info.Entries.Count == 2 && !info.Entries[0].Available && fixture.Grants == 0, "Unavailable catalog metadata dropped positions or fetched playback.");
    }
    private static async Task ShowPages()
    {
        var fixture = new MusicFamilyFixture { Edit = (operation, node) => { if (operation == "jio-show") node["episodes"] = new JsonArray(Jio(2, true), Jio(1, true), Jio(1, true)); return node; } };
        var info = await fixture.Engine.InspectAsync(Request("jio-show"), default);
        Check(info.Entries.Select(entry => entry.Id).SequenceEqual(new[] { "episode1", "episode2" }), "Bootstrap episode page size contaminated the dedicated paging contract.");
    }
    private static async Task Podcast(bool song)
    {
        var fixture = new MusicFamilyFixture { Edit = (operation, node) => { if (operation == "jio-grant") node["auth_url"] = "https://api.spreaker.com/download/episode/77/fixture.mp3"; return node; } };
        if (song) { await Fails(() => fixture.Engine.InspectAsync(Request("jio-song"), default)); return; }
        var info = await fixture.Engine.InspectAsync(Request("jio-episode"), default); Check(info.Formats.Single().Url!.Host == "api.spreaker.com" && fixture.MediaReads == 0, "The issuer's known podcast delegation failed.");
    }
    private static async Task Options(string mode)
    {
        using var files = new Owned(); var root = Path.Combine(files.Path, "native-public-player-v1"); Directory.CreateDirectory(root);
        var owned = Path.Combine(root, new string('A', 64) + ".json"); var temporary = Path.Combine(root, new string('B', 64) + "." + new string('c', 32) + ".tmp"); var foreign = Path.Combine(root, "unrelated.json"); var outside = Path.Combine(files.Path, "user-file.json");
        foreach (var path in new[] { owned, temporary, foreign, outside }) await File.WriteAllTextAsync(path, "preserve unless owned cache"); Directory.CreateDirectory(Path.Combine(root, "nested"));
        var logs = new List<string>(); var progress = 0; var callbacks = new List<string>();
        var engine = new NativeEngine([new Stub()], _ => new MediaHttp(new Bytes()));
        var request = new NativeDownloadRequest { Url = new("https://fixture.invalid/audio.mp3"), OutputDirectory = files.Path, FileName = "recording", CacheDirectory = files.Path, Retries = 0 };
        if (mode == "recovery") { Check(DownloadWorkspace.Recipe(request) == DownloadWorkspace.Recipe(request with { RemoveCache = true, Quiet = true, NoProgress = true, NoWarnings = true }), "Output/cache controls invalidated a media recovery recipe."); return; }
        if (mode == "analysis") { Check(NativeAnalysisArguments.Select(["--quiet", "--no-progress", "--no-warnings", "--rm-cache-dir"]).Count == 0, "Download output/cleanup flags leaked into read-only analysis."); return; }
        var args = mode switch { "quiet" => new[] { "--quiet" }, "progress" => ["--no-progress"], "warnings" => ["--no-warnings"], "reset" => ["--quiet", "--no-quiet", "--no-progress", "--progress"], _ => ["--rm-cache-dir"] };
        request = NativeExtraArguments.Apply(request, args);
        if (mode == "cache-inspection") { await engine.InspectAsync(request, default); Check(File.Exists(owned) && File.Exists(temporary), "Read-only inspection removed cache files."); return; }
        if (mode == "cache-cancel") { using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Fails(() => engine.DownloadAsync(request, null, null, cancel.Token)); Check(File.Exists(owned) && File.Exists(temporary), "Pre-canceled cleanup removed cache files."); return; }
        if (mode == "cache-empty") request = request with { CacheDirectory = null };
        var result = await engine.DownloadAsync(request, new Reporter(_ => progress++), logs.Add, default, callbacks.Add);
        Check(result.Succeeded && callbacks.Count == 1 && await File.ReadAllTextAsync(result.OutputPath!) == "fixture audio", "Output controls changed publication or callbacks.");
        if (mode == "quiet") Check(logs.Count == 0, "Quiet output still emitted log callbacks.");
        if (mode == "progress") Check(progress == 0, "Disabled progress emitted reports.");
        if (mode == "reset") Check(progress > 0 && logs.Count > 0, "Explicit output re-enabling lost precedence.");
        if (mode == "warnings") { Check(!logs.Any(text => text.Contains("fixture-warning", StringComparison.Ordinal)), "Warning suppression did not apply."); Check((await engine.InspectAsync(request, default)).Warnings.Contains("fixture-warning"), "Warning suppression discarded inspection facts."); }
        if (mode == "cache-cleanup") Check(!File.Exists(owned) && !File.Exists(temporary) && File.Exists(foreign) && File.Exists(outside) && Directory.Exists(Path.Combine(root, "nested")), "Cache cleanup touched non-owned data.");
        if (mode == "cache-empty") Check(File.Exists(owned), "Unconfigured cleanup touched another cache directory.");
    }
    private sealed class Stub : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("fixture", "Fixture", 1000); public bool CanHandle(Uri url) => true;
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(new PageMetadata { Id = "fixture", Extractor = "fixture", Title = "Fixture", Warnings = ["fixture-warning"], Media = [new() { Url = request.Url, Kind = MediaKind.Progressive, Extension = "mp3", HasAudio = true, HasVideo = false }] });
    }
    private sealed class Bytes : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("fixture audio") }); } }
    private sealed class Reporter(Action<DownloadProgress> action) : IProgress<DownloadProgress> { public void Report(DownloadProgress value) => action(value); }
    private sealed class Owned : IDisposable { internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "music-option-" + Guid.NewGuid().ToString("N")); internal Owned() => Directory.CreateDirectory(Path); public void Dispose() => Directory.Delete(Path, true); }
    internal static async Task Fails(Func<Task> action)
    { try { await action(); } catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentException or OperationCanceledException or FormatException) { return; } throw new Exception("The invalid music/option contract succeeded."); }
}
