using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private sealed class OrfBroadcastHandler(string family, Action observe) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            observe(); if (request.RequestUri!.Host == "on.orf.at") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("api:{baseUrl:\"https://api-tvthek.orf.at/api/v4.3/\",user:\"owned-public\",password:\"owned-config\"}") });
            object row = family == "on" ? new { id = 41, title = "Owned ORF programme", duration_seconds = 3, video_type = "episode", is_gapless = true, _links = new { self = new { href = "https://api-tvthek.orf.at/api/v4.3/episode/41" } }, sources = new { hls = new[] { new { src = "https://apasfiis.sf.apa.at/owned_41__/master.m3u8", quality_key = "Q4A" } }, dash = System.Array.Empty<object>() } }
                : new { payload = new { id = 41, station = "oe1", entity = "Broadcast", href = "https://audioapi.orf.at/oe1/api/json/5.0/broadcast/41", title = "Owned ORF programme", duration = 3000, state = "C", isOnDemand = true, start = "2020-01-01T00:00:00Z", end = "2020-01-01T00:00:03Z", streams = new[] { new { start = "2020-01-01T00:00:00Z", end = "2020-01-01T00:00:03Z", duration = 3000, offsetStart = 0, offsetEnd = 3000, host = "loopstream01.apa.at", channel = "oe1", loopStreamId = "owned.mp3", urls = new { progressive = "https://loopstream01.apa.at/?channel=oe1&id=owned.mp3&offsetende=3000{&player}" } } } } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(row)) });
        }
    }
    private static async Task NativeOrfBroadcast(string family)
    {
        var calls = 0; var native = new NativeEngine(httpFactory: _ => new MediaHttp(new OrfBroadcastHandler(family, () => calls++), new() { AllowAutoRedirect = false }));
        var url = family == "on" ? "https://on.orf.at/video/41/fixture" : "https://audioapi.orf.at/oe1/api/json/5.0/broadcast/41";
        var info = await new NativeEngineAdapter(native).GetVideoInfoAsync(url, default); Assert(info.Title == "Owned ORF programme" && calls == (family == "on" ? 2 : 1) && native.SupportsGeoVerificationProxy(new(url)), "WPF ORF routing or metadata changed.");
    }
}
