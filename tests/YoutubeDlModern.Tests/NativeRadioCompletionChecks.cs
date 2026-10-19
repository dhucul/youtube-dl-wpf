using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static async Task NativeGeoSettingHeaders()
    {
        var service = new SettingsService(Path.Combine(TestRoot, "owned-geographic-settings")); var settings = new AppSettings(); settings.Network.GeoBypassCountry = "PL"; service.Save(settings); var requests = new List<NativeDownloadRequest>(); var adapter = new NativeEngineAdapter(AdapterEngine((request, _) => { requests.Add(request); return Task.FromResult(AdapterVideo()); }), service);
        await adapter.GetVideoInfoAsync("https://media.example/watch", default); Assert(requests.Last().GeoVerification?.ForwardedForMode == "PL", "WPF did not map the forwarded country.");
        settings.Network.GeoBypass = false; settings.Network.GeoBypassIpBlock = "8.8.8.8/32"; service.Save(settings); await adapter.GetVideoInfoAsync("https://media.example/watch", default); Assert(requests.Last().GeoVerification?.ForwardedForMode == "8.8.8.8/32", "WPF classic explicit IP precedence changed.");
        settings.General.ExtraArgs = "--no-geo-bypass"; service.Save(settings); await adapter.GetVideoInfoAsync("https://media.example/watch", default); Assert(requests.Last().GeoVerification?.ForwardedForMode == "never", "WPF argument precedence lost the explicit disable flag.");
        settings.General.ExtraArgs = ""; settings.Network.GeoBypassIpBlock = "invalid-owned-value"; service.Save(settings); var count = requests.Count;
        try { await adapter.GetVideoInfoAsync("https://media.example/watch", default); throw new Exception("WPF accepted an invalid geographic block."); } catch (NativeEngineUnsupportedException ex) { Assert(!ex.Message.Contains("invalid-owned-value", StringComparison.Ordinal) && requests.Count == count, "Invalid geographic settings reached extraction or leaked values."); }
    }
    private sealed class RadioFranceAppHandler(Action observe) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            observe(); object?[] data = [new { content = 1, seo = 2, station = 3, contentType = 4, pageType = 4 }, new { __typename = 4, type = 5, id = 6, brand = 7, path = 8, title = 9, isStreamable = 10, duration = 11, startDate = 12, manifestation = 13 }, new { canonicalUrl = 14 }, new { name = 7, title = 15 }, "Expression", "episode", "22222222-2222-4222-8222-000001234567", "franceculture", "franceculture/podcasts/owned/episode-1234567", "Owned RF episode", true, 3, 1577836800, new { __typename = 16, id = 17, duration = 11, url = 18 }, "https://www.radiofrance.fr/franceculture/podcasts/owned/episode-1234567", "France Culture", "ManifestationAudio", "33333333-3333-4333-8333-333333333333", "https://media.radiofrance-podcast.net/owned.m4a"];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { type = "data", nodes = new[] { new { type = "data", data } } })) });
        }
    }
    private static async Task NativeRadioFranceRoute()
    {
        var calls = 0; var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new RadioFranceAppHandler(() => calls++), new() { AllowAutoRedirect = false })); var info = await new NativeEngineAdapter(engine).GetVideoInfoAsync("https://www.radiofrance.fr/franceculture/podcasts/owned/episode-1234567", default);
        Assert(info.Title == "Owned RF episode" && calls == 1, "WPF Radio France routing or current JSON data extraction failed.");
    }
}
