using System.Net;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.Bbc;
using YoutubeDl.Engine.Providers.Svt;

internal static partial class GlobalBroadcastChecks
{
    internal static readonly string[] Modes = ["metadata", "headers", "denied", "drm", "identity", "duration", "foreign", "duplicate", "redirect", "http", "renewal", "changed-renewal", "archive", "cancel", "url", "registry"];
    internal static readonly string[] PolicyModes = ["aliases", "analysis", "arity", "dynamic", "static", "mixed", "default", "dtd", "namespace", "type", "download", "late-dynamic"];
    public static IEnumerable<(string Name, Func<Task> Test)> All => new[] { "bbc", "svt" }.SelectMany(family => Modes.Select(mode => ("Global broadcast " + family + " " + mode, (Func<Task>)(() => Contract(family, mode)))))
        .Concat(new[] { "page-duplicate", "page-script", "cache-streaming", "alias", "version", "rights-expired", "rights-future", "caption-foreign", "caption-duplicate", "live" }.Select(mode => ("Global broadcast SVT " + mode, (Func<Task>)(() => Contract("svt", mode)))))
        .Concat(PolicyModes.Select(mode => ("Global broadcast dynamic policy " + mode, (Func<Task>)(() => DynamicPolicy(mode)))));
    internal static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Contract(string family, string mode)
    {
        using var files = new ExecSponsorChecks.Files(); var f = new Fixture(family) { Mode = mode }; var request = f.Request(files.Directory); using var http = f.Http();
        if (mode == "registry") { Assert(MediaProviderRegistry.CreateDefault().Resolve(request.Url).Info.Id == family, "The new broadcast family was not registered."); return; }
        if (mode == "url")
        {
            foreach (var raw in family == "bbc" ? new[] { "https://www.bbc.com.evil.example/news/videos/c12345678901", "https://user@www.bbc.com/news/videos/c12345678901", "https://www.bbc.com/news/videos/c12345678901?x=1", "https://www.bbc.com/video" } : new[] { "https://www.svtplay.se.evil.example/video/e4z3yQr", "https://user@www.svtplay.se/video/e4z3yQr", "https://www.svtplay.se/video/e4z3yQr?x=1", "https://www.svtplay.se/owned-show" })
            { try { if (family == "bbc") BbcProvider.Id(new(raw)); else SvtProvider.Id(new(raw)); throw new Exception("Accepted an unsupported broadcast URL."); } catch (Exception e) when (e is ArgumentException or NotSupportedException) { } }
            return;
        }
        if (mode == "cancel") { using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); await ProviderFamiliesChecks.Expect<OperationCanceledException>(() => f.Provider.ExtractAsync(request, http, cancellation.Token)); Assert(f.ApiCalls == 0, "Canceled broadcast extraction made a request."); return; }
        if (mode == "archive")
        {
            request = request with { DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") }; await File.WriteAllTextAsync(request.DownloadArchiveFile, (family == "bbc" ? "bbc p0123456" : "svt:play e4z3yQr") + "\n"); var result = await f.Engine.DownloadAsync(request, null, null, default); Assert(result.Succeeded && result.Skipped && f.Transfers == 0, "Archived broadcast transferred media."); return;
        }
        if (mode is "identity" or "duration" or "foreign" or "duplicate" or "redirect" or "http" or "page-duplicate" or "page-script" or "cache-streaming" or "alias" or "version" or "caption-foreign" or "caption-duplicate")
        { await ProviderFamiliesChecks.Expect<Exception>(() => f.Provider.ExtractAsync(request, http, default)); Assert(f.Transfers == 0, "A refused broadcast descriptor transferred media."); return; }
        var page = await f.Provider.ExtractAsync(request, http, default);
        if (mode is "denied" or "drm" or "rights-expired" or "rights-future") { Assert(page.Media.Count == 0 && page.Warnings.Count > 0, "A broadcast rights denial became public playback."); return; }
        Assert(page.DurationSeconds == 3 && page.Media.Count == 1 && page.Extractor == (family == "bbc" ? "bbc" : "svt:play"), "Broadcast selected identity or complete duration changed.");
        if (family == "svt") Assert(page.Subtitles.Select(caption => caption.Language).SequenceEqual(new[] { "sv", "sv-x-sdh" }), "SVT accessibility captions collided with ordinary subtitles.");
        if (mode == "live") Assert(page.IsLive && page.Media[0].VerificationDurationSeconds is null, "A live SVT programme was treated as a complete recorded file.");
        if (mode is "renewal" or "changed-renewal" or "headers")
        {
            using var clean = http.CreatePublicServiceSession(); using var playback = clean.WithAuthorization(page.PlaybackAuthorization!); var path = Path.Combine(files.Directory, "master.txt");
            if (mode == "changed-renewal") await ProviderFamiliesChecks.Expect<InvalidDataException>(() => new FileDownloader(playback).DownloadAsync(page.Media[0].Url, path, null, null, default, 0));
            else { await new FileDownloader(playback).DownloadAsync(page.Media[0].Url, path, null, null, default, 0); Assert(File.ReadAllBytes(path).SequenceEqual(f.Master), "Broadcast renewal changed the selected resource."); Assert(f.Generation == (mode == "renewal" ? 2 : 1), "Broadcast renewal was unbounded or stale."); }
        }
    }
    internal sealed class Fixture(string family)
    {
        internal string Mode = "metadata", Transport = "hls"; internal int ApiCalls, Transfers, ManifestCalls, Generation; internal byte[] Master = "#EXTM3U\n#EXT-X-TARGETDURATION:3\n#EXTINF:3,\npart.ts\n#EXT-X-ENDLIST\n"u8.ToArray(); internal Dictionary<string, byte[]> Files = [];
        internal IMediaProvider Provider => family == "bbc" ? new BbcProvider() : new SvtProvider(); internal NativeEngine Engine => new([Provider], _ => Http());
        internal NativeDownloadRequest Request(string dir) => new() { Url = new(family == "bbc" ? "https://www.bbc.com/news/videos/c12345678901" : "https://www.svtplay.se/video/e4z3yQr/owned-video"), OutputDirectory = dir, FileName = "recording", NoCache = true, NoMtime = true, Retries = 0, FragmentRetries = 0 };
        internal MediaHttp Http() => new(new Handler(this, family), new() { AllowAutoRedirect = false, Headers = { ["Authorization"] = "Bearer owned-source", ["X-Owned-Secret"] = "source" } });
        private string Page(string provider)
        {
            if (provider == "bbc") return Next(new { props = new { pageProps = new { metadata = new { id = "urn:bbc:ares::article:" + (Mode == "identity" ? "c99999999999" : "c12345678901"), seoHeadline = "Owned BBC video", description = "Owned full clip", videoMetadata = new { subType = "clip", format = "video", smpKind = "programme", versions = new[] { new { versionId = Mode == "changed-renewal" && Generation > 0 ? "p0654321" : "p0123456", types = new[] { "Original" }, duration = Mode == "duration" ? 0 : 3, availableTerritories = new { uk = Mode != "denied", nonUk = Mode != "denied" }, drm = Mode == "drm" } }, webcastVersions = System.Array.Empty<object>() } } } } });
            var next = Next(new { query = new { slug = new[] { "video", Mode == "identity" ? "unknown" : "e4z3yQr", "owned-video" } } });
            var details = new { detailsPageByPath = new { item = new { __typename = "Single", svtId = "e4z3yQr", name = "Owned SVT video", duration = Mode == "duration" ? 8 : 3, urls = new { svtplay = "/video/e4z3yQr/owned-video" } }, analytics = new { json = new { contentId = "e4z3yQr" } }, description = "Owned full replay" } };
            var cache = JsonSerializer.Serialize(new { owned = new { hasNext = Mode == "cache-streaming", data = JsonSerializer.Serialize(details) } });
            var script = "<script>window.URQL_DATA = " + cache + (Mode == "page-script" ? "; ownedCall()" : "") + ";</script>";
            return next + script + (Mode == "page-duplicate" ? script : "");
        }
        private object Response(string provider)
        {
            Generation++; var host = Mode == "foreign" ? "other.example" : provider == "bbc" ? "vod-hls-ww-live.akamaized.net" : "svt-vod-1t.akamaized.net"; var extension = Transport == "hls" ? "m3u8" : "mpd"; var url = "https://" + host + "/media/owned-p0123456/" + "master." + extension + "?g=" + Generation;
            if (provider == "bbc")
            {
                var connection = new { protocol = "https", supplier = "mf_akamai", transferFormat = Transport, href = url, authExpires = DateTimeOffset.UtcNow.AddHours(1).ToString("O") };
                return new { media = new[] { new { kind = "video", type = "video/mp4", encoding = "h264", connection = Mode == "duplicate" ? new[] { connection, connection } : new[] { connection } } } };
            }
            var media = new { url, format = Transport == "hls" ? "hls" : "dash-full" };
            object Caption(string type) => new { url = "https://" + (Mode == "caption-foreign" ? "other.example" : host) + "/media/owned-p0123456/" + (Mode == "caption-duplicate" || type == "subtitle" ? "normal" : "sdh") + ".vtt?g=" + Generation, format = "webvtt", language = "sv", type = Mode == "caption-duplicate" ? "subtitle" : type };
            return new { svtId = Mode == "alias" ? "bad" : "jd6op2Y", programVersionId = Mode == "version" ? "bad version" : Mode == "changed-renewal" && Generation > 1 ? "owned-2" : "owned-1", programTitle = "Owned SVT video", episodeTitle = "Owned SVT video", contentDuration = 3, live = Mode == "live", rights = new { validFrom = DateTimeOffset.UtcNow.AddHours(Mode == "rights-future" ? 1 : -1).ToString("O"), validTo = DateTimeOffset.UtcNow.AddHours(Mode == "rights-expired" ? -1 : 1).ToString("O"), drmCopyProtection = Mode == "drm", geoBlockedSweden = Mode == "denied" }, videoReferences = Mode == "duplicate" ? new[] { media, media } : new[] { media }, subtitleReferences = new[] { Caption("subtitle"), Caption("caption-sdh") } };
        }
        private static string Next(object value) => "<script id=\"__NEXT_DATA__\" type=\"application/json\">" + JsonSerializer.Serialize(value) + "</script>";
        private sealed class Handler(Fixture f, string provider) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); var url = request.RequestUri!; Assert(request.Headers.Authorization is null && !request.Headers.Contains("X-Owned-Secret"), "Source credentials entered the public broadcast API or media.");
                if (url.Host is "www.bbc.com" or "www.svtplay.se") { f.ApiCalls++; if (f.Mode == "redirect") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new("https://other.example/stolen") } }); if (f.Mode == "http") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(f.Page(provider)) }); }
                if (url.Host is "open.live.bbc.co.uk" or "api.svt.se") { f.ApiCalls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(f.Response(provider))) }); }
                if (url.AbsolutePath.EndsWith(".m3u8", StringComparison.Ordinal) || url.AbsolutePath.EndsWith(".mpd", StringComparison.Ordinal)) f.ManifestCalls++; else f.Transfers++;
                if (f.Mode is "renewal" or "changed-renewal" && url.Query == "?g=1") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                if (f.Mode == "segment-renewal" && f.Generation == 1 && !url.AbsolutePath.EndsWith(".m3u8", StringComparison.Ordinal) && !url.AbsolutePath.EndsWith(".vtt", StringComparison.Ordinal)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                var name = Path.GetFileName(url.AbsolutePath); var data = f.Files.GetValueOrDefault(name) ?? (name.EndsWith(".vtt", StringComparison.Ordinal) ? "WEBVTT\n\n00:00.000 --> 00:02.500\nOwned SVT caption\n"u8.ToArray() : f.Master);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
            }
        }
    }
}
