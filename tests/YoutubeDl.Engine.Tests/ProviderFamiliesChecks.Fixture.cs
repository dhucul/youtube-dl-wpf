using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static partial class ProviderFamiliesChecks
{
    internal sealed class Fixture(string family)
    {
        internal string Family => family; internal string Mode = "metadata"; internal int Api, Transfers, Generation; internal bool Live;
        internal byte[] Data = "complete owned provider media"u8.ToArray(); internal Dictionary<string, byte[]> Files = [];
        internal List<(string Host, string[] Headers)> Requests = [];
        internal string MediaHost => family switch { "tvp" => "rr-fixture.cdn.tvp.pl", "arte" => "manifest-arte.akamaized.net", _ => "podcast.orf.at" };
        internal string ApiHost => family switch { "tvp" => "vod.tvp.pl", "arte" => "api.arte.tv", _ => "audioapi.orf.at" };
        internal Uri Url => new(family switch { "tvp" => Live ? "https://vod.tvp.pl/live,1/channel,41" : "https://vod.tvp.pl/programy,88/fixture,41", "arte" => "https://www.arte.tv/fr/videos/123456-001-A/fixture/", _ => "https://sound.orf.at/podcast/oe1/fixture/episode-one" });
        internal Uri CollectionUrl => new(family switch { "tvp" => "https://vod.tvp.pl/seriale,18/fixture-odcinki,19", "arte" => "https://www.arte.tv/fr/videos/RC-123456/", _ => "https://sound.orf.at/podcast/oe1/fixture" });
        internal IMediaProvider Provider => family switch { "tvp" => new YoutubeDl.Engine.Providers.Tvp.TvpProvider(), "arte" => new YoutubeDl.Engine.Providers.Arte.ArteProvider(), _ => new YoutubeDl.Engine.Providers.Orf.OrfPodcastProvider() };
        internal string Identity => family switch { "tvp" => "41", "arte" => "123456-001-A_fr", _ => "episode_one_guid" };
        internal string Extractor => family switch { "tvp" => "tvp:vod", "arte" => "ArteTV", _ => "orf:podcast" };
        internal NativeEngine Engine => new([Provider], _ => Http());
        internal MediaHttp Http(bool headers = false)
        {
            var options = new MediaHttpOptions { AllowAutoRedirect = false };
            if (headers) { options.Headers["Authorization"] = "Bearer owned-secret"; options.Headers["X-Fixture-Secret"] = "owned"; options.Referer = "https://source.example/private"; }
            return new MediaHttp(new Handler(this), options);
        }
        private sealed class Handler(Fixture owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); var url = request.RequestUri!; owner.Requests.Add((url.Host, request.Headers.Select(x => x.Key).ToArray()));
                if (url.Host == owner.ApiHost)
                {
                    owner.Api++; if (owner.Mode == "http") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                    if (owner.Mode == "redirect") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new("https://other.example/api") } });
                    var value = owner.Response(url); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(value.ToJsonString()) });
                }
                owner.Transfers++;
                if (owner.Mode is "cache-redirect" or "redirect-denied" && url.Host == owner.MediaHost) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new(owner.Mode == "cache-redirect" ? "https://ec05-waw2.v4.cache.orange.pl/master.m3u8" : "https://other.example/master.m3u8") } });
                if (owner.Mode == "renewal" && owner.Transfers == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                var data = owner.Files.GetValueOrDefault(url.AbsolutePath.TrimStart('/')) ?? (owner.Family == "arte" && url.AbsolutePath.EndsWith(".m3u8", StringComparison.Ordinal) ? owner.Files.GetValueOrDefault("master.m3u8") : null) ?? owner.Data;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
            }
        }
        private static JsonObject Obj(object value) => (JsonObject)JsonSerializer.SerializeToNode(value)!;
        private string Source(string extension = "m3u8", string? identity = null) => "https://" + (Mode == "foreign-media" ? "other.example" : MediaHost) + "/" + (family is "arte" or "orf" ? Mode == "resource-id" ? "other" : identity : "master") + "." + extension + "?generation=" + Generation;
        private JsonObject TvpRow(string id = "41", bool episode = false) => Obj(new { id = int.Parse(id), type = Live ? "LIVE" : episode ? "EPISODE" : "VOD", externalUid = "900" + id, title = "Selected programme " + id, duration = 3, number = Mode == "materials" ? 1 : int.Parse(id) - 40, active = true, payable = Mode == "denied", loginRequired = false, webUrl = Live ? "https://vod.tvp.pl/live,1/channel," + id : "https://vod.tvp.pl/programy,88/fixture," + id, season = new { id = 9, serial = new { id = 19 } } });
        private JsonObject ArteMetadata(string id) => Obj(new { providerId = id, language = "fr", title = "Selected programme " + id, duration = new { seconds = 3 }, images = new object[] { new { url = "https://api-cdn.arte.tv/image" } }, link = new { url = "https://www.arte.tv/fr/videos/" + id + "/fixture/" }, config = new { nextPage = (string?)null } });
        private JsonObject OrfParent() => Obj(new { id = 19, entity = "Podcast", station = "oe1", slug = "fixture", isOnline = true, title = "Podcast fixture", author = "ORF fixture", link = new { url = "https://sound.orf.at/podcast/oe1/fixture" }, episodeCount = 2 });
        private JsonObject OrfEpisode(string slug = "episode-one") => Obj(new { entity = "Episode", station = "oe1", guid = slug.Replace('-', '_') + "_guid", slug, title = "Selected episode " + slug, duration = 3000, published = "2020-01-01T00:00:00Z", expiry = Mode == "denied" ? "2020-02-01T00:00:00Z" : "2099-01-01T00:00:00Z", link = new { url = "https://sound.orf.at/podcast/oe1/fixture/" + slug }, enclosures = new object[] { new { type = "audio/mpeg", url = Source("mp3", slug.Replace('-', '_') + "_guid"), length = Data.Length } } });
        internal JsonNode Response(Uri url)
        {
            JsonObject value;
            if (family == "tvp")
            {
                if (url.AbsolutePath.EndsWith("/playlist", StringComparison.Ordinal)) { Generation++; return Obj(new { externalVideo = new { externalUid = Mode == "identity" ? "wrong" : "900" + url.AbsolutePath.Split('/')[3] }, sources = new { HLS = new[] { new { src = Source() } }, DASH = new[] { new { src = Source("mpd") } } }, drm = Mode == "drm" }); }
                if (url.AbsolutePath.EndsWith("/seasons", StringComparison.Ordinal)) return new JsonArray(Obj(new { id = 9, type = "SEASON", display = Mode == "incomplete" ? "3 - 1" : Mode == "materials" ? "1 - 1" : "2 - 1" }));
                if (url.AbsolutePath.EndsWith("/episodes", StringComparison.Ordinal)) { var a = TvpRow("41", true); var b = TvpRow(Mode == "moved" ? "43" : "42", true); if (Mode == "unavailable") b["payable"] = true; return new JsonArray(a, b); }
                if (url.AbsolutePath.Contains("/serials/", StringComparison.Ordinal)) return Obj(new { id = 19, type = "SERIAL", title = "Series fixture", webUrl = CollectionUrl.AbsoluteUri });
                var id = url.AbsolutePath.Split('/').Last(); value = TvpRow(id);
                if (Mode == "wrong-type") value["type"] = "SERIAL";
                if (Mode == "wrong-id") value["id"] = 99;
                return value;
            }
            if (family == "arte")
            {
                var collection = url.AbsolutePath.Contains("/playlist/", StringComparison.Ordinal); var id = url.AbsolutePath.Split('/').Last(); var metadata = ArteMetadata(Mode == "wrong-id" || Mode == "edition" ? "other" : id);
                if (collection)
                {
                    if (Mode == "incomplete") metadata["config"]!["nextPage"] = "https://api.arte.tv/next";
                    var a = ArteMetadata("123456-001-A"); a["language"] = "fr"; var b = ArteMetadata(Mode == "moved" ? "123456-003-A" : "123456-002-A"); b["language"] = "fr";
                    if (Mode == "unavailable") b["error"] = Obj(new { code = "NO_RIGHTS" });
                    return Obj(new { data = new { type = "Playlist", attributes = new { provider = "arte", metadata, items = new[] { a, b } } } });
                }
                Generation++;
                return Obj(new { data = new { type = Mode == "wrong-type" ? "Other" : "ConfigPlayer", attributes = new { provider = "arte", metadata, live = false, error = Mode == "denied" ? new { code = "NO_RIGHTS" } : null, drm = Mode == "drm", restriction = new { allowEmbed = true, ageRestriction = Mode == "age" ? "AGE_16" : "NONE", geoblocking = new { restrictedArea = Mode == "region" } }, streams = new[] { new { url = Source(identity: id), protocol = "API_HLS_NG_MA" } }, chapters = new { elements = new[] { new { title = "First", startTime = 0 }, new { title = "Second", startTime = Mode == "chapter" ? 9 : 1 } } } } } });
            }
            var series = url.Query == "?episodes";
            if (series)
            {
                value = OrfParent(); var first = OrfEpisode(); var second = OrfEpisode(Mode == "moved" ? "episode-three" : "episode-two");
                if (Mode == "unavailable") second["expiry"] = "2020-02-01T00:00:00Z";
                value["episodes"] = new JsonArray(first, second); if (Mode == "incomplete") value["episodeCount"] = 3;
            }
            else
            {
                Generation++; value = OrfEpisode(url.AbsolutePath.Split('/').Last()); value["podcast"] = OrfParent(); value["drm"] = Mode == "drm";
                if (Mode == "wrong-id") value["link"]!["url"] = "https://sound.orf.at/podcast/oe1/fixture/other";
                if (Mode == "wrong-type") value["entity"] = "Other";
                if (Mode == "size") value["enclosures"]![0]!["length"] = 0;
            }
            return Obj(new { payload = value });
        }
    }
}
