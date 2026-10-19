using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.Niconico;
using YoutubeDl.Engine.Providers.RadioFrance;

internal static partial class CatalogSessionChecks
{
    internal static readonly string[] NicoModes = ["metadata", "headers", "renewal", "concurrent", "changed-duration", "changed-id", "watch-id", "type", "private", "deleted", "login", "paid", "drm", "no-formats", "duplicate", "foreign", "expired", "bad-cookie", "no-cookie", "grant-denied", "watch-denied", "redirect", "key", "track", "registry", "url"];
    internal static readonly string[] RfModes = ["profile", "schedule", "missing-link", "unavailable", "unsupported-route", "fallback-image", "previous-cursor", "player-id", "capped", "partial", "cursor", "duplicate", "parent", "canonical", "station", "type", "clock", "horizon", "limit", "member-replaced"];
    public static IEnumerable<(string Name, Func<Task> Test)> All => NicoModes.Select(mode => ("Catalog session Nico " + mode, (Func<Task>)(() => NicoContract(mode)))).Concat(RfModes.Select(mode => ("Catalog session RF " + mode, (Func<Task>)(() => RfContract(mode)))));
    private static async Task NicoContract(string mode)
    {
        using var files = new ExecSponsorChecks.Files(); var f = new NicoFixture { Mode = mode }; var request = f.Request(files.Directory); using var http = f.Http();
        if (mode == "registry") { Assert(MediaProviderRegistry.CreateDefault().Resolve(request.Url).Info.Id == "niconico", "The missing Niconico family was not registered."); return; }
        if (mode == "url")
        {
            foreach (var raw in new[] { "https://www.nicovideo.jp.evil.example/watch/sm9", "https://user@www.nicovideo.jp/watch/sm9", "https://www.nicovideo.jp/watch/sm9?list=1", "https://www.nicovideo.jp/watch/9", "https://www.nicovideo.jp/user/4" }) { try { NiconicoProvider.Id(new(raw)); throw new Exception("An unsupported Nico URL was accepted."); } catch (Exception e) when (e is ArgumentException or NotSupportedException) { } }
            Assert(NiconicoProvider.Id(new("https://embed.nicovideo.jp/watch/sm9")) == "sm9", "Niconico watch aliases changed identity."); return;
        }
        if (mode is "watch-id" or "type" or "no-formats" or "duplicate" or "foreign" or "expired" or "bad-cookie" or "no-cookie" or "grant-denied" or "watch-denied" or "key" or "track") { await ProviderFamiliesChecks.Expect<Exception>(() => f.Provider.ExtractAsync(request, http, default)); Assert(f.Transfers == 0, "A refused Niconico grant transferred media."); return; }
        var page = await f.Provider.ExtractAsync(request, http, default);
        if (mode is "private" or "deleted" or "login" or "paid" or "drm") { Assert(page.Media.Count == 0 && page.Warnings.Count > 0 && f.Grants == 0, "A protected Nico watch page obtained a guest substitute."); return; }
        Assert(page.Id == "sm9" && page.Extractor == "Niconico" && page.DurationSeconds == 3 && page.Media.Count == 1 && page.Media[0].Kind == YoutubeDl.Engine.Discovery.MediaKind.Hls, "Niconico lost its full selected identity.");
        Assert(f.Outputs.Count == 4 && f.Outputs.Distinct().Count() == 4, "Niconico dropped offered audio/video quality pairs.");
        if (mode is "renewal" or "concurrent" or "changed-duration" or "changed-id" or "redirect" or "headers")
        {
            using var clean = http.CreatePublicServiceSession(preserveForwardedFor: true); using var playback = clean.WithAuthorization(page.PlaybackAuthorization!);
            async Task ReadOne(int i) { var path = Path.Combine(files.Directory, "master-" + i); await new FileDownloader(playback).DownloadAsync(page.Media[0].Url, path, null, null, default, 0); Assert(File.ReadAllBytes(path).SequenceEqual(f.Master), "A Nico delivery grant changed its object."); }
            if (mode is "changed-duration" or "changed-id" or "redirect") await ProviderFamiliesChecks.Expect<Exception>(() => ReadOne(0));
            else { await Task.WhenAll(Enumerable.Range(0, mode == "concurrent" ? 5 : 1).Select(ReadOne)); Assert(f.Grants == (mode is "renewal" or "concurrent" ? 2 : 1), "Nico rejected generations did not share a bounded refresh."); }
            Assert(f.ForeignCalls == 0, "A Nico cookie followed an undeclared redirect.");
        }
    }
    private static async Task RfContract(string mode)
    {
        using var files = new ExecSponsorChecks.Files(); var f = new RfFixture { Mode = mode }; var request = f.Request(files.Directory); using var http = f.Http();
        if (mode is "capped" or "partial" or "cursor" or "duplicate" or "parent" or "canonical" or "station" or "type" or "clock" or "horizon" or "limit" or "previous-cursor" or "player-id") { if (mode == "limit") request = request with { MaximumPlaylistEntries = 2 }; await ProviderFamiliesChecks.Expect<Exception>(() => f.Provider.ExtractAsync(request, http, default)); Assert(f.Transfers == 0, "An incomplete RF inventory transferred media."); return; }
        var page = await f.Provider.ExtractAsync(request, http, default);
        Assert(page.IsPlaylist && page.Entries.Count == 3 && page.Entries.All(row => row.ResolvedMedia is null) && page.Entries.Select(row => row.Position).SequenceEqual(new[] { 1, 2, 3 }), "RF extended inventory omitted positions or retained playback URLs.");
        Assert(page.Extractor == (f.Schedule ? "RadioFranceProgramSchedule" : "RadioFranceProfile"), "RF route lost its exact provider identity.");
        if (mode == "missing-link") Assert(page.Entries[1].Url is null && !page.Entries[1].Available, "A schedule entry without a page became playable.");
        if (mode == "unavailable") Assert(!page.Entries[1].Available, "A pending scheduled broadcast became available.");
        if (mode == "unsupported-route") Assert(page.Entries[1].Url is not null && !page.Entries[1].Available && page.Entries[1].Id == RadioCompletionChecks.RadioFranceFixture.Uuid(1234568), "An unsupported profile route was dropped or became playable.");
        if (mode == "fallback-image") Assert(page.Entries[0].ThumbnailUrl == "https://www.radiofrance.fr/_app/immutable/assets/owned.jpg", "The publisher fallback image did not retain its exact origin.");
        if (mode == "member-replaced") { f.Replaced = true; await ProviderFamiliesChecks.Expect<InvalidDataException>(() => f.Provider.ExtractAsync(request with { Url = page.Entries[0].Url! }, http, default)); }
        Assert(f.Transfers == 0, "RF inventory inspection transferred full media.");
    }
    internal static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal sealed class NicoFixture
    {
        internal string Mode = "metadata"; internal int Grants, Watches, Transfers, ForeignCalls; internal List<string> Outputs = []; internal Dictionary<string, byte[]> Files = []; internal byte[] Master = "#EXTM3U\n#EXT-X-TARGETDURATION:3\n#EXTINF:3,\npart.ts\n#EXT-X-ENDLIST\n"u8.ToArray();
        internal NiconicoProvider Provider => new(); internal NativeEngine Engine => new([Provider], _ => Http());
        internal NativeDownloadRequest Request(string directory) => new() { Url = new("https://www.nicovideo.jp/watch/sm9"), OutputDirectory = directory, FileName = "recording", NoCache = true, Retries = 0, FragmentRetries = 0, NoMtime = true };
        internal MediaHttp Http() => new(new Handler(this), new() { AllowAutoRedirect = false, Headers = { ["Authorization"] = "Bearer owned-source", ["X-Owned-Secret"] = "source" } });
        private JsonElement Watch()
        {
            var generation = Interlocked.Increment(ref Watches);
            return JsonSerializer.SerializeToElement(new { meta = new { status = 200 }, data = new { response = new { client = new { watchId = Mode == "watch-id" ? "sm99" : "sm9", watchTrackId = Mode == "track" ? "bad track" : "owned_" + generation }, video = new { id = Mode == "changed-id" && generation > 1 ? "sm99" : "sm9", contentType = Mode == "type" ? "short" : "long", title = "Owned Niconico video", duration = Mode == "changed-duration" && generation > 1 ? 9 : 3, registeredAt = "2020-01-01T00:00:00Z", isPrivate = Mode == "private", isDeleted = Mode == "deleted", isAuthenticationRequired = Mode == "login", watchableUserTypeForPayment = "all" }, payment = new { video = new { billingType = Mode == "paid" ? "paid" : "free" } }, media = new { drm = Mode == "drm", domand = new { accessRightKey = Mode == "key" ? "" : "owned-key-" + generation, videos = new[] { new { id = "video-h264-360p", isAvailable = Mode != "no-formats" }, new { id = Mode == "duplicate" ? "video-h264-360p" : "video-h264-240p", isAvailable = Mode != "no-formats" } }, audios = new[] { new { id = "audio-aac-128kbps", isAvailable = true }, new { id = "audio-aac-64kbps", isAvailable = true } } } } } } });
        }
        private sealed class Handler(NicoFixture f) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); var u = request.RequestUri!;
                Assert(request.Headers.Authorization is null && !request.Headers.Contains("X-Owned-Secret"), "Source credentials entered the Nico service or media.");
                if (u.Host == "www.nicovideo.jp") { if (f.Mode == "watch-denied") return new(HttpStatusCode.Forbidden); return Json(f.Watch()); }
                if (u.Host == "nvapi.nicovideo.jp")
                {
                    var generation = Interlocked.Increment(ref f.Grants); Assert(request.Method == HttpMethod.Post && request.Headers.GetValues("X-Access-Right-Key").Single() == "owned-key-" + f.Watches && request.Headers.GetValues("X-Frontend-Id").Single() == "6", "Nico invented or leaked the guest grant identity.");
                    var body = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(ct)); f.Outputs = body.RootElement.GetProperty("outputs").EnumerateArray().Select(row => string.Join('/', row.EnumerateArray().Select(x => x.GetString()))).ToList();
                    if (f.Mode == "grant-denied") return new(HttpStatusCode.Forbidden);
                    var result = Json(JsonSerializer.SerializeToElement(new { meta = new { status = 201 }, data = new { contentUrl = "https://" + (f.Mode == "foreign" ? "other.example" : "delivery.domand.nicovideo.jp") + "/hlsbid/bid" + generation + "/master.m3u8", createTime = DateTimeOffset.UtcNow.ToString("O"), expireTime = DateTimeOffset.UtcNow.AddHours(f.Mode == "expired" ? -1 : 1).ToString("O") } })); result.StatusCode = HttpStatusCode.Created;
                    if (f.Mode != "no-cookie") result.Headers.TryAddWithoutValidation("Set-Cookie", "domand_bid=owned-bid-" + generation + "; Domain=" + (f.Mode == "bad-cookie" ? "nvapi.nicovideo.jp" : ".nicovideo.jp") + "; Path=/; Secure; HttpOnly"); return result;
                }
                if (u.Host != "delivery.domand.nicovideo.jp") { Interlocked.Increment(ref f.ForeignCalls); throw new Exception("Foreign Nico transfer"); }
                if (!u.AbsolutePath.EndsWith(".m3u8", StringComparison.Ordinal)) Interlocked.Increment(ref f.Transfers); Assert(!request.Headers.Contains("X-Access-Right-Key") && !request.Headers.Contains("X-Frontend-Id"), "Nico grant headers entered media."); Assert(request.Headers.GetValues("Cookie").Single() == "domand_bid=owned-bid-" + f.Grants, "Nico delivery did not use the current scoped cookie.");
                if (f.Mode == "redirect") return new(HttpStatusCode.Redirect) { Headers = { Location = new("https://other.example/stolen") } };
                if (f.Mode is "renewal" or "concurrent" or "changed-id" or "changed-duration" && u.AbsolutePath.Contains("/bid1/", StringComparison.Ordinal)) return new(HttpStatusCode.Forbidden);
                if (f.Mode == "segment-renewal" && f.Grants == 1 && u.AbsolutePath.EndsWith(".ts", StringComparison.Ordinal)) return new(HttpStatusCode.Forbidden);
                var name = Path.GetFileName(u.AbsolutePath); return new(HttpStatusCode.OK) { Content = new ByteArrayContent(f.Files.GetValueOrDefault(name) ?? f.Master) };
            }
        }
        private static HttpResponseMessage Json(JsonElement value) => new(HttpStatusCode.OK) { Content = new StringContent(value.GetRawText(), Encoding.UTF8, "application/json") };
    }
    internal sealed class RfFixture
    {
        internal string Mode = "profile"; internal bool Replaced; internal int Transfers, ApiCalls; internal byte[] Data = "owned audio"u8.ToArray();
        internal bool Schedule => Mode is "schedule" or "missing-link" or "unavailable" or "clock" or "horizon" or "station" or "player-id";
        internal RadioFranceProvider Provider => new(); internal NativeEngine Engine => new([Provider], _ => Http());
        internal NativeDownloadRequest Request(string dir) => new() { Url = new("https://www.radiofrance.fr/" + (Schedule ? "franceculture/grille-programmes" : "personnes/owned")), OutputDirectory = dir, FileName = "recording", NoCache = true, NoMtime = true, MaximumPlaylistEntries = 100, Retries = 0 };
        internal MediaHttp Http() => new(new Handler(this), new() { AllowAutoRedirect = false });
        private JsonElement Response(Uri url)
        {
            var path = url.AbsolutePath[..^"/__data.json".Length].Trim('/'); var second = url.Query == "?p=2"; JsonObject Item(int n)
            {
                var id = 1234566 + n;
                return JsonSerializer.SerializeToNode(new { __typename = "Expression", id = RadioCompletionChecks.RadioFranceFixture.Uuid(Schedule ? id + 10000 : id), playerId = Mode == "player-id" ? "invalid" : RadioCompletionChecks.RadioFranceFixture.Uuid(id), brand = Mode == "station" ? "franceinter" : "franceculture", type = "episode", startTimeUnix = 1577836800L + (Mode == "clock" ? 0 : Mode == "horizon" ? n * 130000L : n * 1800L), hasManifestationAudio = Mode != "unavailable" || n != 2, duration = 3, manifestation = new { __typename = "ManifestationAudio", duration = 3 }, visual = Mode == "fallback-image" ? new { src = "/_app/immutable/assets/owned.jpg" } : null, titleProps = new { title = "Owned episode " + n, href = Mode == "missing-link" && n == 2 ? "" : Mode == "unsupported-route" && n == 2 ? "/savoirs-plus/podcasts/owned/episode-" + id : "/franceculture/podcasts/owned/episode-" + id } })!.AsObject();
            }
            var items = Schedule ? new JsonArray(Item(1), Item(2), Item(3)) : second ? new JsonArray(Item(Mode == "duplicate" ? 1 : 3)) : new JsonArray(Item(1), Item(2));
            var content = JsonSerializer.SerializeToNode(new { __typename = Schedule ? "PageTemplate" : "Personality", id = second && Mode == "parent" ? RadioCompletionChecks.RadioFranceFixture.Uuid(8) : RadioCompletionChecks.RadioFranceFixture.Parent, title = "Owned RF inventory", path, brand = "franceculture", displayFormat = "grid", pagination = new { lastPage = 2 }, documents = new { total = Mode == "capped" ? 100 : Mode == "partial" ? 4 : 3, next = second ? null : Mode == "cursor" ? "invalid" : "Mg==", prev = second ? Mode == "previous-cursor" ? "MA==" : "" : null, items } })!.AsObject();
            if (Schedule) content["programs"] = items.DeepClone();
            return RadioCompletionChecks.RadioFranceFixture.Encode(new { content, contentType = Schedule ? "PageTemplate" : "Personality", pageType = Mode == "type" ? "Other" : Schedule ? "ProgramGrid" : "Personality", seo = new { canonicalUrl = "https://www.radiofrance.fr/" + (Mode == "canonical" ? "other" : path) }, station = new { name = "franceculture" } });
        }
        private sealed class Handler(RfFixture f) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); var url = request.RequestUri!; JsonElement value;
                if (url.Host == "www.radiofrance.fr")
                {
                    f.ApiCalls++; if (url.AbsolutePath.Contains("/podcasts/", StringComparison.Ordinal)) { var fixture = new RadioCompletionChecks.RadioFranceFixture { Mode = f.Replaced ? "replaced" : "metadata" }; value = fixture.Response(url); } else value = f.Response(url);
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(value.GetRawText()) });
                }
                f.Transfers++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(f.Data) });
            }
        }
    }
}
