using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private sealed class PublicFamilyHandler(string family, Action observe) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            observe(); object value;
            if (family == "tvp") value = request.RequestUri!.AbsolutePath.EndsWith("/playlist", StringComparison.Ordinal)
                ? new { externalVideo = new { externalUid = "90041" }, sources = new { HLS = new[] { new { src = "https://rr-fixture.cdn.tvp.pl/master.m3u8" } } } }
                : new { id = 41, type = "VOD", externalUid = "90041", title = "Selected public programme", duration = 3, webUrl = "https://vod.tvp.pl/programy,88/fixture,41" };
            else if (family == "arte") value = new { data = new { type = "ConfigPlayer", attributes = new { provider = "arte", metadata = new { providerId = "123456-001-A", language = "fr", title = "Selected public programme", duration = new { seconds = 3 } }, restriction = new { allowEmbed = true, ageRestriction = "NONE" }, streams = new[] { new { url = "https://manifest-arte.akamaized.net/123456-001-A.m3u8" } } } } };
            else value = new { payload = new { entity = "Episode", guid = "episode_one_guid", slug = "episode-one", title = "Selected public programme", duration = 3000, link = new { url = "https://sound.orf.at/podcast/oe1/fixture/episode-one" }, podcast = new { id = 19, entity = "Podcast", station = "oe1", slug = "fixture", isOnline = true, title = "Podcast fixture", link = new { url = "https://sound.orf.at/podcast/oe1/fixture" } }, enclosures = new[] { new { type = "audio/mpeg", url = "https://podcast.orf.at/episode_one_guid.mp3", length = 1000 } } } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) });
        }
    }
    private static async Task NativeProviderFamily(string family)
    {
        var folder = Path.Combine(TestRoot, "public-family-" + family); Directory.CreateDirectory(folder); var calls = 0;
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new PublicFamilyHandler(family, () => calls++), new() { AllowAutoRedirect = false }));
        var url = family switch { "tvp" => "https://vod.tvp.pl/programy,88/fixture,41", "arte" => "https://www.arte.tv/fr/videos/123456-001-A/fixture/", _ => "https://sound.orf.at/podcast/oe1/fixture/episode-one" };
        var info = await new NativeEngineAdapter(engine).GetVideoInfoAsync(url, default);
        Assert(info.Title == "Selected public programme" && calls == (family == "tvp" ? 2 : 1) && engine.SupportsGeoVerificationProxy(new(url)), "WPF public-provider routing or metadata changed.");
    }
}
