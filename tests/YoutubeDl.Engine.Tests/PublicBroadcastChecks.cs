using System.Net;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.Ard;
using YoutubeDl.Engine.Providers.Zdf;
using YoutubeDl.Engine.Providers.Nrk;

internal static partial class PublicBroadcastChecks
{
    internal static readonly string[] Modes = ["metadata", "headers", "identity", "foreign", "duplicate", "denied", "drm", "expired", "http", "redirect", "renewal", "segment-renewal", "changed-renewal", "changed-duration", "archive", "cancel", "registry", "url"];
    internal static readonly string[] CollectionModes = ["collection", "limit", "partial", "parent", "count", "member", "repeated", "unavailable", "snapshot-replaced"];
    public static IEnumerable<(string Name, Func<Task> Test)> All => new[] { "ard", "zdf", "nrk" }.SelectMany(family => Modes.Select(mode => ("Public broadcast " + family + " " + mode, (Func<Task>)(() => Contract(family, mode)))))
        .Concat(CollectionModes.Select(mode => ("Public broadcast ARD " + mode, (Func<Task>)(() => Collection(mode)))))
        .Concat(SubtitleDelayChecks).Concat(TransportChecks);
    internal static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Contract(string family, string mode)
    {
        using var files = new ExecSponsorChecks.Files(); var fixture = new Fixture(family) { Mode = mode }; var request = fixture.Request(files.Directory); using var http = fixture.Http();
        if (mode == "registry") { Assert(MediaProviderRegistry.CreateDefault().Resolve(request.Url).Info.Id == family, "The public broadcast family was not registered."); if (family == "nrk") Assert(MediaProviderRegistry.CreateDefault().Resolve(new("nrk:TEST00000001")).Info.Id == "nrk", "The declared NRK address scheme was not isolated to NRK."); return; }
        if (mode == "url")
        {
            foreach (var url in new[] { request.Url.AbsoluteUri + "?other=1", request.Url.AbsoluteUri.Replace("https://", "https://user@", StringComparison.Ordinal), "https://www.example.com/foreign" })
            { try { if (family == "ard") ArdAddress.Parse(new(url)); else if (family == "zdf") ZdfProvider.Id(new(url)); else NrkProvider.Id(new(url)); throw new Exception("An unsupported public broadcast address was accepted."); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { } }
            if (family == "nrk") Assert(NrkProvider.Id(new("nrk:TEST00000001")) == "TEST00000001", "NRK's opaque address changed the programme ID."); return;
        }
        if (mode == "archive")
        {
            request = request with { DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") }; await File.WriteAllTextAsync(request.DownloadArchiveFile, (family == "ard" ? "ardmediathek " + fixture.ItemId : family == "zdf" ? "zdf owned-100" : "nrk TEST00000001") + "\n"); var result = await fixture.Engine.DownloadAsync(request, null, null, default); Assert(result.Succeeded && result.Skipped && fixture.Transfers == 0 && fixture.ApiCalls == 0, "Known public broadcast archive transferred media or reopened discovery."); return;
        }
        if (mode == "cancel") { using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); await ProviderFamiliesChecks.Expect<OperationCanceledException>(() => fixture.Provider.ExtractAsync(request, http, cancellation.Token)); Assert(fixture.ApiCalls == 0, "Canceled extraction opened the public service."); return; }
        if (mode is "identity" or "foreign" or "duplicate" or "http" or "redirect") { await ProviderFamiliesChecks.Expect<Exception>(() => fixture.Provider.ExtractAsync(request, http, default)); Assert(fixture.Transfers == 0, "An invalid public broadcast source transferred media."); return; }
        var page = await fixture.Provider.ExtractAsync(request, http, default);
        if (mode is "denied" or "drm" or "expired") { Assert(page.Media.Count == 0 && page.Warnings.Count > 0, "An ungranted public broadcast source became playable."); return; }
        Assert(page.Media.Count == 1 && page.DurationSeconds == 3 && page.Title.Length > 0, "The selected complete public broadcast programme changed.");
        if (family != "zdf") Assert(page.Subtitles.Count == 1, "A declared public caption track was lost.");
        if (family == "nrk") Assert(page.Chapters.Count == 1 && page.Chapters[0].StartSeconds == 0, "NRK chapter timing lost its initial zero boundary.");
        if (mode is "renewal" or "changed-renewal" or "changed-duration" or "headers")
        {
            using var clean = http.CreatePublicServiceSession(); using var playback = clean.WithAuthorization(page.PlaybackAuthorization!); var path = Path.Combine(files.Directory, "master.txt");
            if (mode is "changed-renewal" or "changed-duration") await ProviderFamiliesChecks.Expect<InvalidDataException>(() => new FileDownloader(playback).DownloadAsync(page.Media[0].Url, path, null, null, default, 0));
            else { await new FileDownloader(playback).DownloadAsync(page.Media[0].Url, path, null, null, default, 0); Assert(File.ReadAllBytes(path).SequenceEqual(fixture.Master), "Public broadcast renewal changed the selected object."); Assert(fixture.Generation == (mode == "renewal" ? 2 : 1), "Public broadcast grant refresh was unbounded."); }
        }
    }
    private static async Task Collection(string mode)
    {
        using var files = new ExecSponsorChecks.Files(); var fixture = new Fixture("ard") { Mode = mode }; var request = fixture.Request(files.Directory) with { Url = fixture.CollectionUrl, MaximumPlaylistEntries = mode == "limit" ? 10 : 100 }; using var http = fixture.Http();
        if (mode is "limit" or "partial" or "parent" or "count" or "member" or "repeated") { await ProviderFamiliesChecks.Expect<InvalidDataException>(() => fixture.Provider.ExtractAsync(request, http, default)); Assert(fixture.Transfers == 0, "A partial ARD inventory transferred media."); return; }
        var page = await fixture.Provider.ExtractAsync(request, http, default); Assert(page.IsPlaylist && page.Entries.Count == 25 && page.Entries.All(entry => entry.ResolvedMedia is null) && page.Entries.Select(entry => entry.Position).SequenceEqual(Enumerable.Range(1, 25)), "ARD dropped complete publisher inventory positions or retained media links.");
        if (mode == "unavailable") Assert(!page.Entries[^1].Available, "ARD lost an unavailable collection position.");
        if (mode == "snapshot-replaced")
        {
            var info = await fixture.Engine.InspectAsync(request with { ExpandPlaylist = true }, default); fixture.Mode = "replaced"; var result = await fixture.Engine.DownloadAsync(request with { PlaylistSnapshot = info.PlaylistSnapshot, PlaylistItems = "1" }, null, null, default); Assert(fixture.Transfers == 0 && result.OutputPaths.Count == 0, "A changed ARD member transferred replacement media.");
        }
    }
    internal sealed class Fixture(string family)
    {
        internal string Mode = "metadata"; internal int Generation, ApiCalls, Transfers; internal byte[] Master = "#EXTM3U\n#EXT-X-TARGETDURATION:3\n#EXTINF:3,\npart.ts\n#EXT-X-ENDLIST\n"u8.ToArray(); internal Dictionary<string, byte[]> Files = [];
        internal static string Encoded(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        internal string ItemId => Encoded("crid://owned.example/item/1"); internal string ParentId => Encoded("crid://owned.example/show"); internal Uri CollectionUrl => new("https://www.ardmediathek.de/sendung/owned/" + ParentId);
        internal IMediaProvider Provider => family == "ard" ? new ArdProvider() : family == "zdf" ? new ZdfProvider() : new NrkProvider(); internal NativeEngine Engine => new([Provider], _ => Http());
        internal NativeDownloadRequest Request(string dir) => new() { Url = new(family == "ard" ? "https://www.ardmediathek.de/video/" + ItemId : family == "zdf" ? "https://www.zdfheute.de/video/owned/owned-100.html" : "https://tv.nrk.no/program/TEST00000001"), OutputDirectory = dir, FileName = "recording", NoCache = true, NoMtime = true, Retries = 0, FragmentRetries = 0 };
        internal MediaHttp Http() => new(new Handler(this, family), new() { AllowAutoRedirect = false, Headers = { ["Authorization"] = "Bearer owned-source", ["X-Owned-Secret"] = "source" } });
        private object Ard(Uri url)
        {
            var id = url.AbsolutePath.Split('/').Last();
            if (url.AbsolutePath.Contains("/widgets/", StringComparison.Ordinal))
            {
                var second = url.Query.StartsWith("?pageNumber=1", StringComparison.Ordinal); var total = Mode == "count" && second ? 26 : 25; var items = Enumerable.Range(second ? 25 : 1, second ? 1 : Mode == "partial" ? 23 : 24).Select(n =>
                {
                    if (Mode == "repeated" && second) n = 1; if (Mode == "replaced" && n == 1) n = 88; var childId = Encoded("crid://owned.example/item/" + n);
                    return new { id = childId, coreAssetType = "EPISODE", duration = 3, longTitle = "Owned episode " + n, availableTo = DateTimeOffset.UtcNow.AddDays(Mode == "unavailable" && n == 25 ? -1 : 1).ToString("O"), show = new { id = Mode == "member" ? "crid://owned.example/other" : "crid://owned.example/show" }, links = new { target = new { id = childId } } };
                }).ToArray();
                return new { id = Mode == "parent" ? "other" : id, title = "Owned complete programme", compilationType = "itemsOfShow", pagination = new { pageNumber = second ? 1 : 0, pageSize = 24, totalElements = total }, teasers = items };
            }
            Generation++; var duration = Mode == "changed-duration" && Generation > 1 ? 9 : 3; var crid = Encoding.UTF8.GetString(Convert.FromBase64String(id.Replace('-', '+').Replace('_', '/') + new string('=', (4 - id.Length % 4) % 4))); var publication = Mode == "changed-renewal" && Generation > 1 ? "urn:ard:publication:2222222222222222" : "urn:ard:publication:1111111111111111";
            var media = new { url = Media("ard"), mimeType = "application/vnd.apple.mpegurl", videoCodec = "H.264", maxHResolutionPx = 128, maxVResolutionPx = 72, audios = new[] { new { kind = "standard", languageCode = "deu" } } };
            return new { coreAssetType = "EPISODE", links = new { self = new { href = "https://api.ardmediathek.de/page-gateway/pages/ard/item/" + id } }, tracking = new { atiCustomVars = new { metadataId = Mode == "identity" ? "crid://other.example/item" : crid } }, widgets = new[] { new { type = "player_ondemand", blockedByLoginOnly = Mode == "denied", mediaCollection = new { embedded = new { id = publication, drm = Mode == "drm", meta = new { title = "Owned ARD programme", durationSeconds = duration, broadcastedOnDateTime = DateTimeOffset.UtcNow.AddHours(-1).ToString("O"), availableToDateTime = DateTimeOffset.UtcNow.AddHours(Mode == "expired" ? -1 : 1).ToString("O") }, streams = new[] { new { kind = "main", media = Mode == "duplicate" ? new[] { media, media } : new[] { media } } }, subtitles = new[] { new { kind = "normal", languageCode = "deu", sources = new[] { new { kind = "webvtt", url = "https://api.ardmediathek.de/player-service/subtitle/webvtt/urn:ard:subtitle:1111111111111111.vtt?g=" + Generation } } } } } } } } };
        }
        private string Zdf()
        {
            Generation++; var video = new { @context = "https://schema.org", name = "Owned ZDF programme", description = "Full owned clip", duration = Mode == "changed-duration" && Generation > 1 ? "PT9S" : "PT3S", expires = DateTimeOffset.UtcNow.AddHours(Mode == "expired" ? -1 : 1).ToString("O"), author = new { name = "ZDFheute" }, contentUrl = "https://www.zdfheute.de/api/video/" + (Mode == "identity" ? "other" : "owned-100") + ".m3u8", requiresSubscription = Mode == "denied", drm = Mode == "drm" };
            var json = JsonSerializer.SerializeToElement(video); var fields = json.EnumerateObject().ToDictionary(row => row.Name, row => (object?)row.Value.Clone()); fields["@type"] = "VideoObject"; var script = "<script type=\"application/ld+json\">" + JsonSerializer.Serialize(fields) + "</script>"; return script + (Mode == "duplicate" ? script : "");
        }
        private object Nrk(bool manifest)
        {
            if (!manifest) Generation++; var duration = Mode == "changed-duration" && Generation > 1 ? "PT9S" : "PT3S"; var rights = new { isGeoBlocked = false, onDemand = new { from = DateTimeOffset.UtcNow.AddHours(-1).ToString("O"), to = DateTimeOffset.UtcNow.AddHours(Mode == "expired" ? -1 : 1).ToString("O"), hasRightsNow = Mode != "denied" } };
            if (!manifest) return new { id = Mode == "identity" ? "TEST00000002" : "TEST00000001", streamingMode = "onDemand", sourceMedium = "video", playability = "playable", duration, availability = rights, drm = Mode == "drm", preplay = new { titles = new { title = "Owned NRK programme" }, indexPoints = new[] { new { title = "Opening", startPoint = "PT0S" } } }, playable = new { resolve = "/playback/manifest/program/TEST00000001" } };
            var asset = new { url = Media("nrk"), format = "HLS", encrypted = false, encryptionScheme = "none" };
            return new { id = "TEST00000001", streamingMode = "onDemand", playability = "playable", availability = rights, playable = new { duration, assets = Mode == "duplicate" ? new[] { asset, asset } : new[] { asset }, subtitles = new[] { new { language = "nb", type = "ttv", webVtt = "https://undertekst.nrk.no/prod/TEST00000001/owned.vtt?g=" + Generation } } } };
        }
        private string Media(string provider)
        {
            var host = Mode == "foreign" ? "other.example" : provider == "ard" ? "adaptive.tagesschau.de" : provider == "zdf" ? "zdfvod.akamaized.net" : "nrk-od-world-36.akamaized.net";
            return "https://" + host + "/" + (provider == "nrk" ? "test00000001/" : "owned/") + (Mode == "changed-renewal" && Generation > 1 ? "changed/" : "") + "master.m3u8?g=" + Generation;
        }
        private sealed class Handler(Fixture f, string family) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); var url = request.RequestUri!; Assert(request.Headers.Authorization is null && !request.Headers.Contains("X-Owned-Secret"), "Source credentials entered a public broadcaster API or media.");
                if (url.Host == "www.zdfheute.de" && url.AbsolutePath.StartsWith("/api/video/", StringComparison.Ordinal)) { f.ApiCalls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new(f.Media("zdf")) } }); }
                if (url.Host is "api.ardmediathek.de" or "www.zdfheute.de" or "psapi.nrk.no" && !url.AbsolutePath.Contains("/subtitle/", StringComparison.Ordinal))
                {
                    f.ApiCalls++; if (f.Mode == "http") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); if (f.Mode == "redirect") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new("https://other.example/foreign") } });
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(family == "zdf" ? f.Zdf() : JsonSerializer.Serialize(family == "ard" ? f.Ard(url) : f.Nrk(url.AbsolutePath.Contains("/manifest/", StringComparison.Ordinal)))) });
                }
                var manifest = url.AbsolutePath.EndsWith(".m3u8", StringComparison.Ordinal); if (!manifest && !url.AbsolutePath.EndsWith(".vtt", StringComparison.Ordinal)) f.Transfers++;
                if (f.Mode is "renewal" or "changed-renewal" or "changed-duration" && url.Query == "?g=1" || f.Mode == "segment-renewal" && f.Generation == 1 && !manifest && !url.AbsolutePath.EndsWith(".vtt", StringComparison.Ordinal)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                var name = Path.GetFileName(url.AbsolutePath); var bytes = f.Files.GetValueOrDefault(name) ?? (name.EndsWith(".vtt", StringComparison.Ordinal) ? "WEBVTT\n\n00:00.000 --> 00:02.500\nOwned public caption\n"u8.ToArray() : f.Master); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            }
        }
    }
}

