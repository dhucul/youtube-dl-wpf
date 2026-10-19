using System.Diagnostics;
using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class ExtractionDelayChecks
{
    private static readonly string[] Modes = ["arguments", "analysis", "negative", "nan", "infinite", "missing", "preflight", "default", "timing", "parallel", "public-clone", "cancel", "media-isolation", "jobs", "engine"];
    public static IEnumerable<(string Name, Func<Task> Test)> All => Modes.Select(mode => ("Audio catalog extraction delay " + mode, (Func<Task>)(() => Contract(mode))));
    private static async Task Contract(string mode)
    {
        var request = new NativeDownloadRequest { Url = new("https://owned.example/page"), OutputDirectory = Path.GetTempPath() };
        if (mode == "arguments") { var mapped = NativeExtraArguments.Apply(request, ["--sleep-requests", "2", "--sleep-requests=0.08"]); AudioCatalogChecks.Assert(mapped.RequestSleepSeconds == .08, "Request pacing lost fractional seconds or last-option precedence."); return; }
        if (mode == "analysis") { var projected = NativeAnalysisArguments.Select(["--sleep-requests", "0.08", "--sleep-subtitles", "5"]); AudioCatalogChecks.Assert(projected.SequenceEqual(new[] { "--sleep-requests", "0.08" }) && NativeExtraArguments.Apply(request, projected).RequestSleepSeconds == .08, "Read-only analysis lost extraction pacing."); return; }
        if (mode is "negative" or "nan" or "infinite" or "missing") { var args = mode == "missing" ? new[] { "--sleep-requests" } : new[] { "--sleep-requests", mode == "negative" ? "-1" : mode == "nan" ? "NaN" : "Infinity" }; await ProviderFamiliesChecks.Expect<ArgumentException>(() => Task.FromResult(NativeExtraArguments.Apply(request, args))); return; }
        if (mode == "preflight") { var engine = new NativeEngine([new Provider()], _ => throw new Exception("Invalid pacing opened the network.")); await ProviderFamiliesChecks.Expect<ArgumentException>(() => engine.InspectAsync(request with { RequestSleepSeconds = -1 }, default)); await ProviderFamiliesChecks.Expect<ArgumentException>(() => engine.DownloadAsync(request with { RequestSleepSeconds = double.NaN }, null, null, default)); return; }
        if (mode == "default") { AudioCatalogChecks.Assert(!JsonSerializer.Serialize(request).Contains("RequestSleepSeconds", StringComparison.Ordinal), "Default request JSON changed."); using var zero = NativeExtractionDelay.Enter(0); using var active = NativeExtractionDelay.Extracting(); await NativeExtractionDelay.BeforeRequestAsync(default); await NativeExtractionDelay.BeforeRequestAsync(default); return; }
        var handler = new Handler(); using var http = new MediaHttp(handler, new() { AllowAutoRedirect = false });
        if (mode == "engine") { var engine = new NativeEngine([new Provider()], _ => new MediaHttp(handler, new() { AllowAutoRedirect = false })); var info = await engine.InspectAsync(request with { RequestSleepSeconds = .08 }, default); AudioCatalogChecks.Assert(info.Id == "owned" && handler.Times.Count == 2 && handler.Times[1] - handler.Times[0] >= 60, "Native inspection did not pace its real metadata requests."); return; }
        using var job = NativeExtractionDelay.Enter(mode is "cancel" or "media-isolation" or "jobs" ? double.MaxValue : .08);
        async Task Get(MediaHttp client) { using var response = await client.SendAsync(new(HttpMethod.Get, request.Url), default); }
        if (mode == "media-isolation") { await Get(http); await Get(http); AudioCatalogChecks.Assert(handler.Times.Count == 2, "Media outside extraction inherited metadata pacing."); return; }
        if (mode == "jobs") { using var extracting = NativeExtractionDelay.Extracting(); await Get(http); using (NativeExtractionDelay.Enter(double.MaxValue)) await Get(http); AudioCatalogChecks.Assert(handler.Times.Count == 2, "A separate job inherited prior request state."); return; }
        using var scope = NativeExtractionDelay.Extracting();
        if (mode == "cancel") { await Get(http); using var cancellation = new CancellationTokenSource(60); await ProviderFamiliesChecks.Expect<OperationCanceledException>(() => http.SendAsync(new(HttpMethod.Get, request.Url), cancellation.Token)); AudioCatalogChecks.Assert(handler.Times.Count == 1, "Canceled pacing sent an HTTP request."); return; }
        if (mode == "parallel") await Task.WhenAll(Get(http), Get(http), Get(http));
        else { await Get(http); if (mode == "public-clone") { using var clean = http.CreatePublicServiceSession(); await Get(clean); } else await Get(http); }
        AudioCatalogChecks.Assert(handler.Times.Zip(handler.Times.Skip(1)).All(pair => pair.Second - pair.First >= 60), "Metadata requests bypassed shared discovery pacing.");
    }
    private sealed class Handler : HttpMessageHandler
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew(); internal readonly List<long> Times = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); lock (Times) Times.Add(_clock.ElapsedMilliseconds); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("owned metadata") }); }
    }
    private sealed class Provider : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("fixture", "Owned pacing fixture", 999);
        public bool CanHandle(Uri url) => url.Host == "owned.example";
        public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
        {
            using var clean = http.CreatePublicServiceSession(); await clean.GetStringAsync(request.Url, 1024, ct); await clean.GetStringAsync(new("https://owned.example/metadata"), 1024, ct);
            return new() { Id = "owned", Title = "Owned paced source", Extractor = "fixture" };
        }
    }
}
