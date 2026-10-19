using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.Orf;

internal static class OrfBroadcastChecks
{
    internal static readonly string[] Modes = ["metadata", "collection", "unavailable", "limit", "incomplete", "wrong-id", "wrong-type", "foreign-media", "denied", "drm", "headers", "renewal", "changed-renewal", "redirect", "api-http", "archive"];
    public static IEnumerable<(string Name, Func<Task> Test)> All => new[] { "on", "radio" }.SelectMany(family => Modes.Select(mode => ("ORF broadcast " + family + " " + mode, (Func<Task>)(() => Contract(family, mode)))))
        .Concat(new[] { "gap", "overlap", "range", "channel", "template", "part-id", "station", "coverage", "item", "sequence", "programme", "live", "live-config", "pending" }.Select(mode => ("ORF broadcast radio " + mode, (Func<Task>)(() => Contract("radio", mode)))))
        .Concat(new[] { "profile", "page-change", "duplicate", "resource-id", "non-gapless", "caption", "caption-host", "public-client", "client-redirect", "youth", "not-yet", "expired", "source-drm" }.Select(mode => ("ORF broadcast on " + mode, (Func<Task>)(() => Contract("on", mode)))))
        .Concat(new[] { "media-redirect", "media-redirect-denied" }.Select(mode => ("ORF broadcast on " + mode, (Func<Task>)(() => Contract("on", mode)))))
        .Append(("ORF broadcast address boundaries", Addresses));
    internal static NativeDownloadRequest Request(Fixture f, string directory, bool collection = false) => new() { Url = collection ? f.Collection : f.Url, OutputDirectory = directory, FileName = "recording", Retries = 0, FragmentRetries = 0, MaximumPlaylistEntries = 1000, NoMtime = true };
    private static async Task Contract(string family, string mode)
    {
        using var files = new ExecSponsorChecks.Files(); var f = new Fixture(family) { Mode = mode }; var collection = mode is "collection" or "unavailable" or "limit" or "incomplete" or "profile" or "page-change" or "duplicate";
        var request = Request(f, files.Directory, collection); if (mode == "limit") request = request with { MaximumPlaylistEntries = 1 }; using var http = f.Http(mode == "headers");
        if (mode == "archive")
        {
            request = request with { DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") }; await File.WriteAllTextAsync(request.DownloadArchiveFile, "orf:" + family + " " + f.Identity + "\n");
            var result = await f.Engine.DownloadAsync(request, null, null, default); Assert(result.Succeeded && result.Skipped && f.Transfers == 0, "An archived ORF broadcast transferred media."); return;
        }
        if (mode is "wrong-id" or "wrong-type" or "foreign-media" or "limit" or "incomplete" or "redirect" or "api-http" or "gap" or "overlap" or "range" or "channel" or "template" or "part-id" or "station" or "coverage" or "live-config" or "profile" or "page-change" or "duplicate" or "resource-id" or "non-gapless" or "caption-host" or "public-client" or "client-redirect" or "source-drm")
        { await ProviderFamiliesChecks.Expect<Exception>(() => f.Provider.ExtractAsync(request, http, default)); Assert(f.Transfers == 0, "A refused ORF descriptor transferred media."); return; }
        var page = await f.Provider.ExtractAsync(request, http, default);
        if (mode is "denied" or "drm" or "pending" or "youth" or "not-yet" or "expired") { Assert(page.Media.Count == 0 && page.Warnings.Count > 0, "An ORF denial became successful playback."); return; }
        if (collection)
        {
            Assert(page.IsPlaylist && page.Entries.Count == (family == "on" ? 101 : 2) && page.Entries.All(row => row.ResolvedMedia is null) && page.Entries.Select(row => row.Position).SequenceEqual(Enumerable.Range(1, page.Entries.Count)), "ORF lost complete inventory order or retained playback resources.");
            if (mode == "unavailable") Assert(!page.Entries.Last().Available, "ORF hid an unavailable member."); Assert(f.Transfers == 0, "ORF inventory transferred media."); return;
        }
        Assert(page.Id == f.Identity && page.Media.Count > 0, "ORF changed its selected identity.");
        if (mode == "sequence") Assert(page.Media[0].Parts.Count == 2 && page.DurationSeconds == 6, "ORF omitted an ordered radio part.");
        if (mode == "item") Assert(page.DurationSeconds == 3, "ORF rounded editorial duration overrode the supplied audio interval.");
        if (mode == "live") Assert(page.IsLive && page.DurationSeconds is null && page.Media[0].IsAudioOnly, "ORF live radio became an archived programme.");
        if (mode == "caption") Assert(page.Subtitles.Count == 1 && page.Subtitles[0].Language == "de" && page.Subtitles[0].Extension == "vtt", "ORF lost its declared caption track.");
        if (mode == "headers") Assert(f.ApiHeaders.All(headers => !headers.Contains("Cookie") && !headers.Contains("X-Owned-Secret") && !headers.Contains("Referer")), "Source headers entered the ORF verification API.");
        if (mode is "renewal" or "changed-renewal" or "media-redirect" or "media-redirect-denied")
        {
            using var anonymous = http.CreateAnonymousSession(); using var playback = anonymous.WithAuthorization(page.PlaybackAuthorization!); var target = Path.Combine(files.Directory, "media.bin");
            if (mode is "changed-renewal" or "media-redirect-denied") { await ProviderFamiliesChecks.Expect<Exception>(() => new FileDownloader(playback).DownloadAsync(page.Media[0].Url, target, null, null, default, 0)); Assert(!File.Exists(target) && f.Transfers == 1, "A changed ORF renewal or redirect transferred replacement media."); }
            else { await new FileDownloader(playback).DownloadAsync(page.Media[0].Url, target, null, null, default, 0); Assert(File.ReadAllBytes(target).SequenceEqual(f.Data) && f.Transfers == 2 && f.Generation == (mode == "renewal" ? 2 : 1), "ORF failed its bounded public playback renewal or approved redirect."); }
        }
    }
    private static Task Addresses()
    {
        Assert(OrfOnAddress.Parse(new("https://on.orf.at/video/41/fixture")).Id == "41", "ORF ON selected the wrong episode.");
        Assert(OrfRadioAddress.Parse(new("https://sound.orf.at/play/live/oe1")).Kind == "live", "ORF lost live station identity.");
        foreach (var url in new[] { "https://on.orf.at/video/41?redirect=x", "https://on.orf.at.evil.example/video/41", "https://on.orf.at/video/0", "https://user@on.orf.at/video/41" }) { try { OrfOnAddress.Parse(new(url)); throw new Exception("Accepted invalid ON URL."); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { } }
        foreach (var url in new[] { "https://sound.orf.at/play/live/private", "https://sound.orf.at/radio/oe1/beitrag/41?token=x", "https://sound.orf.at/radio/oe1/sendung/0" }) { try { OrfRadioAddress.Parse(new(url)); throw new Exception("Accepted invalid radio URL."); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { } }
        return Task.CompletedTask;
    }
    internal static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal sealed class Fixture(string family)
    {
        internal string Family => family; internal string Mode = "metadata"; internal int Api, Transfers, PayloadTransfers, Generation; internal byte[] Data = "owned complete media"u8.ToArray(); internal Dictionary<string, byte[]> Files = []; internal List<string[]> ApiHeaders = []; internal bool Live;
        internal bool Item => Mode == "item"; internal Uri Url => new(family == "on" ? "https://on.orf.at/video/41/fixture" : Live || Mode is "live" or "live-config" ? "https://sound.orf.at/play/live/oe1" : Item ? "https://sound.orf.at/radio/oe1/beitrag/41" : Mode == "programme" ? "https://sound.orf.at/radio/oe1/sendung/410" : "https://audioapi.orf.at/oe1/api/json/5.0/broadcast/41");
        internal Uri Collection => new(family == "on" ? "https://on.orf.at/profile/19/fixture" : "https://sound.orf.at/radio/oe1/kalender");
        internal string Identity => family == "on" ? "41" : Live || Mode is "live" or "live-config" ? "oe1:live" : Item ? "oe1:item:41" : "oe1:broadcast:41";
        internal IMediaProvider Provider => family == "on" ? new OrfOnProvider() : new OrfRadioProvider(); internal NativeEngine Engine => new([Provider], _ => Http());
        internal MediaHttp Http(bool headers = false)
        {
            var options = new MediaHttpOptions { AllowAutoRedirect = false }; if (headers) { options.Headers["Authorization"] = "Bearer owned-source"; options.Headers["X-Owned-Secret"] = "owned"; options.Referer = "https://owned.example/secret"; }
            return new MediaHttp(new Handler(this), options);
        }
        private sealed class Handler(Fixture f) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); var u = request.RequestUri!;
                if (u.Host is "on.orf.at" or "audioapi.orf.at" or "orf.at" || u.Host == "api-tvthek.orf.at" && !u.AbsolutePath.StartsWith("/assets/subtitles/", StringComparison.Ordinal))
                {
                    f.Api++; f.ApiHeaders.Add(request.Headers.Select(header => header.Key).ToArray());
                    if (u.Host == "on.orf.at") return Task.FromResult(new HttpResponseMessage(f.Mode == "client-redirect" ? HttpStatusCode.Redirect : HttpStatusCode.OK) { Content = new StringContent("api:{baseUrl:\"" + (f.Mode == "public-client" ? "https://other.example/" : "https://api-tvthek.orf.at/api/v4.3/") + "\",user:\"owned-public\",password:\"owned-public-config\"}") });
                    if (u.Host == "api-tvthek.orf.at") Assert(request.Headers.Authorization?.Scheme == "Basic" && request.Headers.Authorization.Parameter == Convert.ToBase64String("owned-public:owned-public-config"u8), "ORF API did not use only the issued public client credential.");
                    else Assert(request.Headers.Authorization is null, "An unrelated API received ORF public client credentials.");
                    if (f.Mode == "api-http") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                    if (f.Mode == "redirect") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new("https://other.example/api") } });
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(f.Response(u).ToJsonString()) });
                }
                Assert(request.Headers.Authorization is null && !request.Headers.Contains("X-Owned-Secret"), "ORF media received issuer/source credentials."); f.Transfers++; if (!u.AbsolutePath.EndsWith(".m3u8", StringComparison.Ordinal) && !u.AbsolutePath.EndsWith(".mpd", StringComparison.Ordinal)) f.PayloadTransfers++;
                if (f.Mode is "media-redirect" or "media-redirect-denied" && u.Host == "apasfiis.sf.apa.at") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new("https://" + (f.Mode == "media-redirect" ? "vod-ww.mdn.ors.at" : "other.mdn.ors.at") + u.PathAndQuery) } });
                if (f.Mode is "renewal" or "changed-renewal" && f.Transfers == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                var name = Path.GetFileName(u.AbsolutePath); if (name == "manifest.m3u8") name = "master.m3u8"; if (u.Host == "loopstream01.apa.at") name = u.Query.Contains("owned_1.mp3", StringComparison.Ordinal) ? "source-part1.mp3" : "source-part0.mp3"; var data = f.Files.GetValueOrDefault(name) ?? f.Data;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
            }
        }
        private static JsonObject Obj(object value) => (JsonObject)JsonSerializer.SerializeToNode(value)!;
        private JsonObject OnRow(int id)
        {
            var actual = Mode == "wrong-id" || Mode == "changed-renewal" && Generation > 1 ? id + 1000 : id;
            return Obj(new { id = actual, title = "Owned ORF episode", exact_duration = Mode == "short-media" ? 8000 : 3000, duration_seconds = 3, video_type = Mode == "wrong-type" ? "livestream" : "episode", is_gapless = Mode != "non-gapless", is_drm_protected = Mode == "drm", is_geo_ip_blocked = Mode == "denied", has_active_youth_protection = Mode == "youth", livedate = Mode == "not-yet" ? "2099-01-01T00:00:00Z" : "2020-01-01T00:00:00Z", killdate = Mode == "expired" || Mode == "unavailable" && id == 141 ? "2020-01-02T00:00:00Z" : (string?)null, _links = new { self = new { href = "https://api-tvthek.orf.at/api/v4.3/episode/" + actual }, profile = new { href = "https://api-tvthek.orf.at/api/v4.3/profile/" + (Mode == "profile" ? 20 : 19) } },
                sources = new { hls = new[] { new { quality_key = "Q4A", src = "https://" + (Mode == "foreign-media" ? "other.example" : "apasfiis.sf.apa.at") + "/owned_" + (Mode == "resource-id" ? 99 : id) + "__/master.m3u8?generation=" + Generation, is_drm_protected = Mode == "source-drm" } }, dash = new[] { new { quality_key = "Q4A", src = "https://apasfiis.sf.apa.at/owned_" + id + "__/master.mpd?generation=" + Generation, is_drm_protected = Mode == "source-drm" } } }, _embedded = new { subtitle = Mode is "caption" or "caption-host" ? new { id = 7, vtt_url = "https://" + (Mode == "caption-host" ? "other.example" : "api-tvthek.orf.at") + "/assets/subtitles/owned.vtt" } : null } });
        }
        private JsonObject Stream(int part = 0)
        {
            var start = DateTimeOffset.Parse("2020-01-01T00:00:00Z").AddSeconds(part * 3 + (part == 1 && Mode == "gap" ? 1 : part == 1 && Mode == "overlap" ? -1 : 0)); var finish = start.AddSeconds(3);
            return Obj(new { start, end = finish, offsetStart = 0, offsetEnd = 3000, duration = 3000, host = Mode == "foreign-media" ? "other.example" : "loopstream01.apa.at", channel = Mode == "channel" ? "oe3" : "oe1", loopStreamId = "owned_" + part + ".mp3", urls = new { progressive = "https://" + (Mode == "foreign-media" ? "other.example" : "loopstream01.apa.at") + "/?channel=oe1&id=owned_" + (Mode == "part-id" ? 99 : part) + ".mp3&offsetende=" + (Mode == "range" ? 4000 : 3000) + "{&shoutcast}{&player}{&referer}{&userid}" + (Mode == "template" ? "{&unknown}" : "") } });
        }
        private JsonObject RadioRow(int id = 41, bool item = false)
        {
            var sequence = Mode is "sequence" or "gap" or "overlap"; var actual = Mode == "wrong-id" || Mode == "changed-renewal" && Generation > 1 ? id + 1000 : id;
            var row = Obj(new { id = actual, station = Mode == "station" ? "oe3" : "oe1", entity = Mode == "wrong-type" ? "Other" : item ? "BroadcastItem" : "Broadcast", href = "https://audioapi.orf.at/oe1/api/json/5.0/" + (item ? "broadcastitem/" : "broadcast/") + actual, programKey = "410", broadcastDay = 20200101, title = "Owned ORF broadcast", duration = sequence ? 6000 : item ? 2900 : Mode == "coverage" ? 4000 : 3000, start = "2020-01-01T00:00:00Z", end = sequence ? "2020-01-01T00:00:06Z" : "2020-01-01T00:00:03Z", state = Mode == "pending" ? "P" : "C", isOnDemand = Mode != "denied", expiry = Mode == "unavailable" && id == 42 ? "2020-01-02T00:00:00Z" : "2099-01-01T00:00:00Z", drm = Mode == "drm" });
            row["programKey"] = (id * 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (item) row["stream"] = Stream(); else row["streams"] = sequence ? new JsonArray(Stream(), Stream(1)) : new JsonArray(Stream()); return row;
        }
        private JsonNode Response(Uri u)
        {
            if (u.Host == "orf.at") return Obj(new { stations = new { oe1 = new { name = "Owned ORF live", liveStreamUrlTemplate = Mode == "live-config" ? "https://other.example/{quality}" : "https://orf-live-oe1.mdn.ors.at/out/u/oe1/{quality}/manifest.m3u8" } } });
            if (family == "on")
            {
                if (u.AbsolutePath.EndsWith("/profile/19", StringComparison.Ordinal)) return Obj(new { id = 19, title = "Owned series", _links = new { self = new { href = "https://api-tvthek.orf.at/api/v4.3/profile/19" } } });
                if (u.AbsolutePath.EndsWith("/episodes", StringComparison.Ordinal))
                {
                    var page = u.Query.EndsWith("page=1", StringComparison.Ordinal) ? 1 : 2; var rows = Enumerable.Range(page == 1 ? 41 : 141, page == 1 ? 100 : Mode == "incomplete" ? 0 : 1).Select(id => OnRow(Mode == "duplicate" && page == 2 ? 41 : id)).ToArray();
                    return Obj(new { page, limit = 100, pages = 2, total = Mode == "page-change" && page == 2 ? 102 : 101, _embedded = new { items = rows } });
                }
                Generation++; return OnRow(41);
            }
            if (u.AbsolutePath.EndsWith("/broadcasts", StringComparison.Ordinal))
            {
                var rows = new JsonArray(RadioRow(), RadioRow(42)); if (Mode == "incomplete") rows[1]!["broadcastDay"] = 20200102;
                return Obj(new { payload = new[] { new { day = 20200101, broadcasts = rows } } });
            }
            Generation++; return Obj(new { payload = RadioRow(item: Item) });
        }
    }
}
