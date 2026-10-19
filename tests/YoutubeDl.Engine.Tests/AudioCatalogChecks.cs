using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.ArdSounds;

internal static class AudioCatalogChecks
{
    internal static readonly string[] Modes = ["metadata", "aac", "headers", "identity", "foreign", "duplicate", "denied", "drm", "expired", "future", "http", "redirect", "renewal", "changed-renewal", "changed-duration", "changed-show", "archive", "cancel", "registry", "url"];
    internal static readonly string[] CollectionModes = ["collection", "limit", "partial", "parent", "count", "member", "repeated", "cursor", "next", "query-error", "unavailable", "snapshot-replaced"];
    public static IEnumerable<(string Name, Func<Task> Test)> All => Modes.Select(mode => ("Audio catalog ARD Sounds " + mode, (Func<Task>)(() => Contract(mode))))
        .Concat(CollectionModes.Select(mode => ("Audio catalog ARD Sounds " + mode, (Func<Task>)(() => Collection(mode)))));
    internal static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Contract(string mode)
    {
        using var files = new ExecSponsorChecks.Files(); var fixture = new Fixture { Mode = mode }; var request = fixture.Request(files.Directory); using var http = fixture.Http();
        if (mode == "registry") { foreach (var host in new[] { "www.ardsounds.de", "ardsounds.de", "www.ardaudiothek.de", "ardaudiothek.de" }) Assert(MediaProviderRegistry.CreateDefault().Resolve(new("https://" + host + "/episode/" + fixture.ItemId + "/")).Info.Id == "ardsounds", "ARD Sounds or its legacy public host was not registered."); return; }
        if (mode == "url") { foreach (var url in new[] { request.Url + "?other=1", request.Url.AbsoluteUri.Replace("https://", "https://user@", StringComparison.Ordinal), "https://www.ardsounds.de/episode/11333865/", "https://other.example/episode/" + fixture.ItemId }) await ProviderFamiliesChecks.Expect<Exception>(() => Task.FromResult(ArdSoundsAddress.Parse(new(url)))); return; }
        if (mode == "archive") { request = request with { DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") }; await File.WriteAllTextAsync(request.DownloadArchiveFile, "ardaudiothek " + fixture.ItemId + "\n"); var result = await fixture.Engine.DownloadAsync(request, null, null, default); Assert(result.Succeeded && result.Skipped && fixture.ApiCalls == 0 && fixture.Transfers == 0, "Archived audio reopened discovery or transferred media."); return; }
        if (mode == "cancel") { using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); await ProviderFamiliesChecks.Expect<OperationCanceledException>(() => fixture.Provider.ExtractAsync(request, http, cancellation.Token)); Assert(fixture.ApiCalls == 0, "Canceled audio extraction contacted the publisher."); return; }
        if (mode is "identity" or "foreign" or "duplicate" or "http" or "redirect") { await ProviderFamiliesChecks.Expect<Exception>(() => fixture.Provider.ExtractAsync(request, http, default)); Assert(fixture.Transfers == 0, "Invalid ARD audio transferred a source."); return; }
        var page = await fixture.Provider.ExtractAsync(request, http, default);
        if (mode is "denied" or "drm" or "expired" or "future") { Assert(page.Media.Count == 0 && page.Warnings.Count > 0, "Unavailable ARD audio became playable."); return; }
        Assert(page.Id == fixture.ItemId && page.Extractor == "ARDAudiothek" && page.Media.Count == 1 && page.DurationSeconds == 3 && page.Media[0].IsAudioOnly && page.Media[0].IsCompleteMedia, "Complete selected audio metadata changed.");
        if (mode is "renewal" or "changed-renewal" or "changed-duration" or "changed-show" or "headers")
        {
            using var clean = http.CreatePublicServiceSession(); using var playback = clean.WithAuthorization(page.PlaybackAuthorization!); var path = Path.Combine(files.Directory, "audio.bin");
            if (mode.StartsWith("changed-", StringComparison.Ordinal)) await ProviderFamiliesChecks.Expect<InvalidDataException>(() => new FileDownloader(playback).DownloadAsync(page.Media[0].Url, path, null, null, default, 0));
            else { await new FileDownloader(playback).DownloadAsync(page.Media[0].Url, path, null, null, default, 0); Assert(File.ReadAllBytes(path).SequenceEqual(fixture.Bytes) && fixture.Generation == (mode == "renewal" ? 2 : 1), "ARD audio renewal changed bytes or retried without bounds."); }
        }
    }
    private static async Task Collection(string mode)
    {
        using var files = new ExecSponsorChecks.Files(); var fixture = new Fixture { Mode = mode }; var request = fixture.Request(files.Directory) with { Url = fixture.CollectionUrl, MaximumPlaylistEntries = mode == "limit" ? 5 : 100 }; using var http = fixture.Http();
        if (mode is "limit" or "partial" or "parent" or "count" or "member" or "repeated" or "cursor" or "next" or "query-error") { await ProviderFamiliesChecks.Expect<InvalidDataException>(() => fixture.Provider.ExtractAsync(request, http, default)); Assert(fixture.Transfers == 0, "A partial show inventory transferred audio."); return; }
        var page = await fixture.Provider.ExtractAsync(request, http, default); Assert(page.IsPlaylist && page.Extractor == "ARDAudiothekPlaylist" && page.Entries.Count == 13 && page.Entries.All(row => row.ResolvedMedia is null) && page.Entries.Select(row => row.Position).SequenceEqual(Enumerable.Range(1, 13)), "ARD show positions were truncated or retained playback URLs.");
        if (mode == "unavailable") Assert(!page.Entries[^1].Available, "An unavailable show member was dropped.");
        if (mode == "snapshot-replaced") { var info = await fixture.Engine.InspectAsync(request with { ExpandPlaylist = true }, default); fixture.Mode = "replaced"; var result = await fixture.Engine.DownloadAsync(request with { PlaylistSnapshot = info.PlaylistSnapshot, PlaylistItems = "1" }, null, null, default); Assert(result.OutputPaths.Count == 0 && fixture.Transfers == 0, "An inspected show selection was replaced by a different episode."); }
    }
    internal sealed class Fixture
    {
        internal string Mode = "metadata", Extension = "mp3"; internal int Generation, ApiCalls, Transfers; internal byte[] Bytes = "complete owned audio"u8.ToArray();
        internal static string Episode(int n) => "urn:ard:episode:" + n.ToString("x16");
        internal string ItemId => Episode(1); internal const string Show = "urn:ard:show:1111111111111111"; internal Uri CollectionUrl => new("https://www.ardsounds.de/sendung/owned/" + Show + "/");
        internal IMediaProvider Provider => new ArdSoundsProvider(); internal NativeEngine Engine => new([Provider], _ => Http());
        internal NativeDownloadRequest Request(string directory) => new() { Url = new("https://www.ardsounds.de/episode/" + ItemId + "/"), OutputDirectory = directory, FileName = "audio", NoCache = true, NoMtime = true, Retries = 0 };
        internal MediaHttp Http() => new(new Handler(this), new() { AllowAutoRedirect = false, Headers = { ["Authorization"] = "Bearer owned-secret", ["X-Owned-Secret"] = "source" } });
        private object Item(int n, bool inventory)
        {
            if (Mode == "repeated" && n == 13) n = 1; if (Mode == "replaced" && n == 1) n = 88; var id = Episode(n); var ext = Mode == "aac" ? "m4a" : Extension; var file = ext == "m4a" ? "audio.mp4" : "audio.mp3";
            var media = new { url = "https://" + (Mode == "foreign" ? "other.example" : "swr-dl.ard-mcdn.de") + "/owned/" + file + "?g=" + Generation, mimeType = ext == "m4a" ? "audio/aac" : "audio/mp3", downloadUrl = "https://" + (Mode == "foreign" ? "other.example" : "swr-dl.ard-mcdn.de") + "/owned/" + file + "?g=" + Generation, allowDownload = true };
            return new { id = n.ToString(), assetId = Mode == "identity" ? Episode(99) : id, coreType = "EPISODE", coreId = Mode == "changed-renewal" && Generation > 1 ? "urn:ard:publication:2222222222222222" : "urn:ard:publication:1111111111111111", title = "Owned audio " + n, description = "Complete owned audio", duration = Mode == "changed-duration" && Generation > 1 ? 9 : 3, isPublished = Mode != "denied" && !(Mode == "unavailable" && n == 13), status = "PUBLISHED", drm = Mode == "drm", publishDate = DateTimeOffset.UtcNow.AddDays(Mode == "future" ? 1 : -1).ToString("O"), path = "/episode/" + id + "/", programSet = new { id = "100", coreId = Mode == "member" || Mode == "changed-show" && Generation > 1 ? "urn:ard:show:2222222222222222" : Show, title = "Owned show", path = CollectionUrl.AbsolutePath, publicationService = new { title = "Owned broadcaster" } }, audioList = Mode == "duplicate" ? new[] { new { availableTo = DateTimeOffset.UtcNow.AddDays(1).ToString("O") }, new { availableTo = DateTimeOffset.UtcNow.AddDays(1).ToString("O") } } : new[] { new { availableTo = DateTimeOffset.UtcNow.AddDays(Mode == "expired" ? -1 : 1).ToString("O") } }, audios = inventory ? new[] { media } : Mode == "duplicate" ? new[] { media, media } : new[] { media } };
        }
        private string Page()
        {
            Generation++; var data = new { page = "/episode/[...slug]", props = new { pageProps = new { initialData = new { data = new { item = Item(1, false) } } } } };
            return "<script id=\"__NEXT_DATA__\" type=\"application/json\">" + JsonSerializer.Serialize(data) + "</script>";
        }
        private object Inventory(Uri url)
        {
            var second = Uri.UnescapeDataString(url.Query).Contains("offset:12", StringComparison.Ordinal); return new { errors = Mode == "query-error" ? new[] { new { message = "Owned incomplete result" } } : [], data = new { result = new { id = "100", coreId = Mode == "parent" ? "urn:ard:show:2222222222222222" : Show, title = "Owned full show", path = CollectionUrl.AbsolutePath, numberOfElements = Mode == "count" && second ? 14 : 13, items = new { pageInfo = new { hasNextPage = Mode == "next" ? second : !second, endCursor = Mode == "cursor" ? "same" : second ? "last" : "first" }, nodes = Enumerable.Range(second ? 13 : 1, second ? 1 : Mode == "partial" ? 11 : 12).Select(n => Item(n, true)).ToArray() } } } };
        }
        private sealed class Handler(Fixture f) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); var url = request.RequestUri!; Assert(request.Headers.Authorization is null && !request.Headers.Contains("X-Owned-Secret"), "Private source headers entered ARD's public service.");
                if (url.Host is "www.ardsounds.de" or "api.ardaudiothek.de")
                {
                    f.ApiCalls++; if (f.Mode == "http") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); if (f.Mode == "redirect") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new("https://other.example/foreign") } });
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(url.Host == "www.ardsounds.de" ? f.Page() : JsonSerializer.Serialize(f.Inventory(url))) });
                }
                if ((f.Mode == "renewal" || f.Mode.StartsWith("changed-", StringComparison.Ordinal)) && url.Query == "?g=1") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                f.Transfers++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(f.Bytes) });
            }
        }
    }
}
