using System.IO;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private sealed class RtveMappingProvider(Func<NativeDownloadRequest, Task<PageMetadata>> extract) : IMediaProvider
    {
        public MediaProviderInfo Info => new("rtve-mapping", "Owned RTVE mapping fixture", 999);
        public bool SupportsGeoVerificationProxy => true;
        public bool CanHandle(Uri url) => true;
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => extract(request);
    }
    private static async Task NativeRtveVerificationMapping(string mode)
    {
        var folder = Path.Combine(TestRoot, "rtve-verification-" + mode); Directory.CreateDirectory(folder);
        var settings = new AppSettings(); settings.Network.GeoVerificationProxy = mode == "invalid" ? "http://user:secret-configured-value@proxy.example" : "http://proxy.example:8080";
        var captured = new List<NativeDownloadRequest>(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var engine = new NativeEngine([new RtveMappingProvider(async request => { captured.Add(request); if (mode == "immutable") { entered.SetResult(); await release.Task; } return AdapterVideo(); })]);
        if (mode == "unsupported") engine = AdapterEngine((_, _) => throw new Exception("An unsupported verification setting reached discovery."));
        if (mode == "analysis")
        {
            settings.General.ExtraArgs = "--geo-verification-proxy http://analysis-proxy.example:8081";
            var service = new SettingsService(Path.Combine(folder, "settings")); service.Save(settings);
            await new NativeEngineAdapter(engine, service).GetVideoInfoAsync("https://www.rtve.es/a/41/", default);
            Assert(captured.Single().GeoVerification!.Proxy == "http://analysis-proxy.example:8081", "Analysis did not retain its explicit provider verification route."); return;
        }
        var options = new DownloadOptions { OutputDirectory = folder, AddMetadata = false, EmbedChapters = false, ExtraArgs = mode == "overrides" ? "--simulate --geo-verification-proxy http://job-proxy.example:8082" : "--simulate" };
        var task = new NativeEngineAdapter(engine).DownloadAsync("https://www.rtve.es/a/41/", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        if (mode == "immutable") { await entered.Task; settings.Network.GeoVerificationProxy = "changed"; release.SetResult(); }
        if (mode is "invalid" or "unsupported")
        {
            try { await task; throw new Exception("A refused verification setting was accepted."); }
            catch (NativeEngineUnsupportedException ex) { Assert(captured.Count == 0 && ex.Message.Contains("Network.GeoVerificationProxy", StringComparison.Ordinal) && !ex.Message.Contains("secret-configured-value", StringComparison.Ordinal), "Verification validation leaked settings or lost its identity."); }
            return;
        }
        var result = await task; Assert(result.ExitCode == 0 && captured.Single().GeoVerification!.Proxy == (mode == "overrides" ? "http://job-proxy.example:8082" : "http://proxy.example:8080"), "Saved/overridden verification connection was not captured immutably.");
    }
}
