using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static partial class GlobalBroadcastChecks
{
    private static async Task DynamicPolicy(string mode)
    {
        using var files = new ExecSponsorChecks.Files(); var request = new NativeDownloadRequest { Url = new("https://owned.example/watch"), OutputDirectory = files.Directory };
        if (mode == "aliases")
        {
            foreach (var flag in new[] { "--ignore-dynamic-mpd", "--no-allow-dynamic-mpd" }) Assert(NativeExtraArguments.Apply(request, [flag]).AllowDynamicDash == false, "A captured dynamic disable alias was ignored.");
            foreach (var flag in new[] { "--allow-dynamic-mpd", "--no-ignore-dynamic-mpd" }) Assert(NativeExtraArguments.Apply(request, ["--ignore-dynamic-mpd", flag]).AllowDynamicDash is null, "Dynamic DASH last-option precedence changed."); return;
        }
        if (mode == "analysis") { var flags = new[] { "--ignore-dynamic-mpd", "--allow-dynamic-mpd", "--no-allow-dynamic-mpd", "--no-ignore-dynamic-mpd" }; Assert(NativeAnalysisArguments.Select(flags).SequenceEqual(flags), "WPF analysis dropped dynamic discovery policy."); return; }
        if (mode == "arity") { await ProviderFamiliesChecks.Expect<ArgumentException>(() => Task.FromResult(NativeExtraArguments.Apply(request, ["--ignore-dynamic-mpd=true"]))); return; }
        if (mode == "late-dynamic")
        {
            using var empty = new MediaHttp(new DynamicHandler("dynamic"), new() { AllowAutoRedirect = false }); var downloader = new DashDownloader(empty); await ProviderFamiliesChecks.Expect<NotSupportedException>(() => downloader.DownloadAsync(new DashManifest { IsDynamic = true }, Path.Combine(files.Directory, "never.mp4"), QualityPreference.Best, null, false, null, null, null, default, 0, liveRequest: request with { AllowDynamicDash = false })); return;
        }
        var handler = new DynamicHandler(mode); using var http = new MediaHttp(handler, new() { AllowAutoRedirect = false }); var page = new PageMetadata { Id = "owned", Title = "Owned", Media = [new() { Id = "dash", Kind = MediaKind.Dash, Url = new("https://owned.example/manifest.mpd") }], SourcePageUrl = request.Url };
        if (mode == "mixed") page = page with { Media = page.Media.Concat(new[] { new MediaCandidate { Id = "progressive", Kind = MediaKind.Progressive, Url = new("https://owned.example/file.mp4") } }).ToArray() };
        if (mode is "dtd" or "namespace" or "type") { await ProviderFamiliesChecks.Expect<Exception>(() => NativeDynamicDash.FilterAsync(page, request with { AllowDynamicDash = false }, http, default)); return; }
        if (mode == "download")
        {
            var engine = new NativeEngine([new PolicyProvider(page)], _ => new(new DynamicHandler("dynamic"), new() { AllowAutoRedirect = false })); var result = await engine.DownloadAsync(request with { AllowDynamicDash = false }, null, null, default); Assert(!result.Succeeded && !Directory.EnumerateFiles(files.Directory).Any(), "A disabled dynamic presentation published output."); return;
        }
        var filtered = await NativeDynamicDash.FilterAsync(page, request with { AllowDynamicDash = mode == "default" ? null : false }, http, default);
        Assert(filtered.Media.Count == (mode is "dynamic" ? 0 : 1) && handler.Calls == (mode == "default" ? 0 : 1), "Dynamic policy changed static/alternate formats or default network behavior.");
        Assert(!JsonSerializer.Serialize(request).Contains("AllowDynamicDash", StringComparison.Ordinal), "The optional policy changed existing default request serialization.");
    }
    private sealed class PolicyProvider(PageMetadata page) : IMediaProvider
    {
        public MediaProviderInfo Info => new("owned-policy", "Owned policy fixture", 1); public bool CanHandle(Uri url) => true; public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(page);
    }
    private sealed class DynamicHandler(string mode) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Calls++; Assert(request.RequestUri!.AbsolutePath.EndsWith(".mpd", StringComparison.Ordinal), "Disabled dynamic policy fetched media.");
            var xml = (mode == "dtd" ? "<!DOCTYPE MPD [<!ENTITY owned SYSTEM 'file:///never-read'>]>" : "") + "<MPD xmlns=\"" + (mode == "namespace" ? "urn:foreign" : "urn:mpeg:dash:schema:mpd:2011") + "\" type=\"" + (mode == "static" ? "static" : mode == "type" ? "invalid" : "dynamic") + "\"/>";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) });
        }
    }
}
