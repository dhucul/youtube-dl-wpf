using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Podomatic;

internal static class PodomaticFeedChecks
{
    internal const string Legacy = "https://bestseopodcast.podomatic.com/entry/2009-09-18T14_24_07-07_00";
    internal const string Current = "Buzzsprout-19895780";
    internal static readonly Uri Final = new("https://rss.buzzsprout.com/2019013.rss");
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new("https://bestseopodcast.podomatic.com/rss2.xml"), OutputDirectory = directory, OutputTemplate = "migrated.%(ext)s" };
    internal static string Media(bool old = false, string key = "fresh") => "https://www.buzzsprout.com/2019013/episodes/" + (old ? "10958456-oldest" : "19895780-current") + ".mp3?literal=A%2Bb&key=" + key;
    internal static string Item(string guid, string media, double duration = 2) => "<item><title>Episode " + guid.Split('/').Last() + "</title><guid isPermaLink='false'>" + WebUtility.HtmlEncode(guid) + "</guid><enclosure url='" + WebUtility.HtmlEncode(media) + "' type='audio/mpeg' length='9999999'/><itunes:duration>" + duration.ToString(System.Globalization.CultureInfo.InvariantCulture) + "</itunes:duration></item>";
    internal static string Feed(string key = "fresh", double duration = 2, bool movedOrder = false) => "<rss version='2.0' xmlns:itunes='http://www.itunes.com/dtds/podcast-1.0.dtd'><channel><title>Migrated publisher</title>" +
        (movedOrder ? Item("new-guid", Media(key: key)) : "") + Item(Current, Media(key: key), duration) + Item(Legacy, Media(true, key), duration) + "</channel></rss>";
    internal static IEnumerable<(string Name, Func<Task> Test)> All => [
        ("Podomatic migrated feeds retain canonical destination opaque GUIDs and verified audio", Facts),
        ("Podomatic feed URLs dispatch to native feed handling while keeping unrelated feeds generic", Routes),
        ("Podomatic migrated feed snapshots refresh resources and preserve inspected GUID positions", Snapshots),
        ("Podomatic migrated feed archives retain canonical identities across repeated selected jobs", Archives),
        ("Podomatic feed migrations reject unsafe destinations HTML external XML and entry limits", Boundaries),
        ("Podomatic changed or duplicate migrated GUID resources cannot silently replace inspected entries", Conflicts),
        ("Podomatic direct unmigrated RSS remains supported without a false migration claim", Unmoved),
        ("Podomatic feed metadata is anonymous and preserves cancellation and denial", Transport)
        ,("Invalid feed chapters preserve media discovery and explicitly refuse requested embedding", ChapterIsolation)
    ];
    private static async Task Facts()
    {
        using var http = Http([]); var page = await new PodomaticProvider().ExtractAsync(Request(Path.GetTempPath()), http, default); var old = page.Entries[1]; var media = old.ResolvedMedia!.Media.Single();
        Assert(page.Extractor == "podomatic:feed" && page.IsPlaylist && page.SourcePageUrl == Final && page.Entries.Count == 2 && old.Id == Legacy && old.Position == 2 && old.ResolvedMedia.ArchiveIdentity!.Extractor == "native:feed", "Migration rewrote canonical feed or legacy identifiers.");
        Assert(media.Url.AbsoluteUri == Media(true) && media.FileSize == 9999999 && media.VerificationFileSize is null && media.VerificationMinimumDurationSeconds == 2 && media.VerificationDemuxer == "mp3" && old.ResolvedMedia.AnonymousPlayback && page.Warnings.Count == 1, "Migrated audio omitted verification or treated an estimate as exact bytes.");
    }
    private static Task Routes()
    {
        var registry = YoutubeDl.Engine.Providers.MediaProviderRegistry.CreateDefault(); Assert(registry.Resolve(Request(Path.GetTempPath()).Url).Info.Id == "podomatic" && registry.Resolve(new Uri("https://rss.buzzsprout.com/2019013.rss")).Info.Id == "generic" && !registry.CanHandle(new Uri("https://bestseopodcast.podomatic.com/rss2.xml?token=private")), "Feed dispatch bypassed private-token handling or replaced unrelated generic routes."); return Task.CompletedTask;
    }
    private static async Task Snapshots()
    {
        await Temporary(async directory => { var calls = new List<Uri>(); var current = Feed("stale"); var engine = new NativeEngine([new PodomaticProvider()], _ => Http(calls, () => current)); var request = Request(directory) with { PlaylistItems = "2", SkipDownload = true, WriteInfoJson = true };
            var info = await engine.InspectAsync(request, default); current = Feed("fresh", movedOrder: true); calls.Clear(); var result = await engine.DownloadAsync(request with { PlaylistSnapshot = info.PlaylistSnapshot }, null, null, default);
            Assert(result.Succeeded && calls.Count == 1, "Snapshot refreshed episode APIs instead of the migrated parent feed."); using var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(result.SidecarPaths.Single())); Assert(json.RootElement.GetProperty("id").GetString() == Legacy && json.RootElement.GetProperty("playlist_index").GetInt32() == 2, "Migration replaced an opaque GUID or inspected position with the new order."); });
    }
    private static async Task Archives()
    {
        await Temporary(async directory => { var calls = new List<Uri>(); var engine = new NativeEngine([new PodomaticProvider()], _ => Http(calls)); var request = Request(directory) with { PlaylistItems = "2" }; var info = await engine.InspectAsync(request, default);
            var archive = Path.Combine(directory, "archive.txt"); using var http = Http([]); var page = await new PodomaticProvider().ExtractAsync(request, http, default); await File.WriteAllTextAsync(archive, page.Entries[1].ResolvedMedia!.ArchiveIdentity!.Record + "\n"); calls.Clear();
            var result = await engine.DownloadAsync(request with { DownloadArchiveFile = archive, PlaylistSnapshot = info.PlaylistSnapshot }, null, null, default); Assert(result.Succeeded && calls.Count == 1, "Archived migrated selection transferred media or resolved legacy GUIDs as old episode URLs."); });
    }
    private static async Task Boundaries()
    {
        using var unsafeFeed = Http([], final: new("http://rss.buzzsprout.com/2019013.rss")); await Expect<InvalidDataException>(() => new PodomaticProvider().ExtractAsync(Request(Path.GetTempPath()), unsafeFeed, default));
        using var html = Http([], () => "<html>Sign in</html>"); await Expect<InvalidDataException>(() => new PodomaticProvider().ExtractAsync(Request(Path.GetTempPath()), html, default));
        using var dtd = Http([], () => "<!DOCTYPE rss [<!ENTITY external SYSTEM 'file:///private'>]>" + Feed()); await Expect<InvalidDataException>(() => new PodomaticProvider().ExtractAsync(Request(Path.GetTempPath()), dtd, default));
        using var oversized = Http([]); await Expect<InvalidDataException>(() => new PodomaticProvider().ExtractAsync(Request(Path.GetTempPath()) with { MaximumPlaylistEntries = 1 }, oversized, default));
    }
    private static async Task Conflicts()
    {
        await Temporary(async directory => { var current = Feed(); var engine = new NativeEngine([new PodomaticProvider()], _ => Http([], () => current)); var request = Request(directory) with { PlaylistItems = "2", SkipDownload = true, WriteInfoJson = true }; var info = await engine.InspectAsync(request, default);
            current = Feed().Replace(WebUtility.HtmlEncode(Legacy), "replacement-guid"); var missing = await engine.DownloadAsync(request with { PlaylistSnapshot = info.PlaylistSnapshot }, null, null, default); Assert(!missing.Succeeded && missing.SidecarPaths.Count == 0, "A removed GUID silently selected its replacement media.");
            current = Feed().Replace("</channel>", Item(Legacy, Media()) + "</channel>"); await Expect<InvalidDataException>(() => engine.InspectAsync(request, default)); });
    }
    private static async Task Unmoved()
    { using var http = Http([], final: Request(Path.GetTempPath()).Url); var page = await new PodomaticProvider().ExtractAsync(Request(Path.GetTempPath()), http, default); Assert(page.Entries.Count == 2 && page.Warnings.Count == 0, "Unmigrated feed was falsely labeled a migration."); }
    internal static string BadChapters(string feed) => feed.Replace("<rss ", "<rss xmlns:psc='http://podlove.org/simple-chapters' ").Replace("</item>", "<psc:chapters><psc:chapter start='0' title='Opening'/><psc:chapter start='10' title='Beyond the listed duration'/></psc:chapters></item>");
    private static async Task ChapterIsolation()
    {
        using var http = Http([], () => BadChapters(Feed())); var page = await new PodomaticProvider().ExtractAsync(Request(Path.GetTempPath()), http, default); var old = page.Entries[1].ResolvedMedia!;
        Assert(page.Entries.Count == 2 && old.Media.Count == 1 && old.Chapters.Count == 0 && old.ChapterRefusalReason is not null && old.Warnings.Count == 1, "Ancillary chapter errors discarded media or lost their explicit refusal.");
    }
    private static async Task Transport()
    {
        var options = new MediaHttpOptions(); options.Headers["Authorization"] = "Bearer fixture"; options.Headers["Cookie"] = "fixture=secret"; using var http = Http([], options: options, observe: r => Assert(!r.Headers.Contains("Authorization") && !r.Headers.Contains("Cookie"), "Caller credentials reached the migrated feed.")); _ = await new PodomaticProvider().ExtractAsync(Request(Path.GetTempPath()), http, default);
        using var stop = new CancellationTokenSource(); stop.Cancel(); await Expect<OperationCanceledException>(() => new PodomaticProvider().ExtractAsync(Request(Path.GetTempPath()), http, stop.Token));
        using var denied = Http([], status: HttpStatusCode.Forbidden); await Expect<HttpRequestException>(() => new PodomaticProvider().ExtractAsync(Request(Path.GetTempPath()), denied, default));
    }
    internal static MediaHttp Http(List<Uri> calls, Func<string>? feed = null, Func<HttpRequestMessage, HttpResponseMessage>? media = null, Uri? final = null, MediaHttpOptions? options = null, Action<HttpRequestMessage>? observe = null, HttpStatusCode status = HttpStatusCode.OK) => new(new Handler(r =>
    { calls.Add(r.RequestUri!); observe?.Invoke(r); if (r.RequestUri!.Host == "bestseopodcast.podomatic.com") return new(status) { Content = new StringContent(feed?.Invoke() ?? Feed()), RequestMessage = new(HttpMethod.Get, final ?? Final) };
        return media?.Invoke(r) ?? throw new InvalidOperationException("Unexpected migrated feed request."); }), options ?? new());
    private static async Task Temporary(Func<string, Task> test) { var directory = Path.Combine(Path.GetTempPath(), "podomatic-feed-check-" + Guid.NewGuid()); Directory.CreateDirectory(directory); try { await test(directory); } finally { Directory.Delete(directory, true); } }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
}
