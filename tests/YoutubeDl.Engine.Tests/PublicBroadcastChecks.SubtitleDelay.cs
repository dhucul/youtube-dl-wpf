using System.Diagnostics;
using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;

internal static partial class PublicBroadcastChecks
{
    private static IEnumerable<(string Name, Func<Task> Test)> SubtitleDelayChecks => new[] { "arguments", "analysis", "negative", "nan", "infinite", "missing", "inline", "per-track", "cancel", "empty", "default", "huge", "unselected", "failed", "preflight" }.Select(mode => ("Public broadcast subtitle delay " + mode, (Func<Task>)(() => SubtitleDelay(mode))));
    private static async Task SubtitleDelay(string mode)
    {
        using var files = new ExecSponsorChecks.Files(); var request = new NativeDownloadRequest { Url = new("https://owned.example/watch"), OutputDirectory = files.Directory, WriteSubs = true, SubLanguages = "all", SubtitleSleepSeconds = .1 };
        if (mode == "arguments") { var mapped = NativeExtraArguments.Apply(request, ["--sleep-subtitles", "2", "--sleep-subtitles=0.125"]); Assert(mapped.SubtitleSleepSeconds == .125, "Subtitle delay grammar or last-option precedence changed."); return; }
        if (mode == "analysis") { Assert(NativeAnalysisArguments.Select(["--sleep-subtitles", "--proxy", "--no-progress"]).Count == 0, "A download-only subtitle delay value injected analysis connection options."); return; }
        if (mode is "negative" or "nan" or "infinite" or "missing") { var args = mode == "missing" ? new[] { "--sleep-subtitles" } : new[] { "--sleep-subtitles", mode == "negative" ? "-1" : mode == "nan" ? "NaN" : "Infinity" }; await ProviderFamiliesChecks.Expect<ArgumentException>(() => Task.FromResult(NativeExtraArguments.Apply(request, args))); return; }
        if (mode == "default") { var neutral = request with { SubtitleSleepSeconds = null }; Assert(!JsonSerializer.Serialize(neutral).Contains("SubtitleSleepSeconds", StringComparison.Ordinal) && NativeSubtitleDelay.WaitAsync(neutral, default).IsCompletedSuccessfully, "The default subtitle option changed serialization or introduced a wait."); return; }
        if (mode == "huge") { using var cancellation = new CancellationTokenSource(30); await ProviderFamiliesChecks.Expect<OperationCanceledException>(() => NativeSubtitleDelay.WaitAsync(request with { SubtitleSleepSeconds = double.MaxValue }, cancellation.Token)); return; }
        var clock = Stopwatch.StartNew(); var handler = new CaptionDelayHandler(clock, mode == "failed"); using var http = new MediaHttp(handler, new() { AllowAutoRedirect = false });
        var tracks = new[] { new SubtitleTrack { Url = new("https://owned.example/en.vtt"), Language = "en", Content = mode == "inline" ? "WEBVTT\n\n00:00.000 --> 00:01.000\nOwned\n" : null }, new SubtitleTrack { Url = new("https://owned.example/de.vtt"), Language = "de", Content = mode == "inline" ? "WEBVTT\n\n00:00.000 --> 00:01.000\nOwned\n" : null } };
        var page = new PageMetadata { Id = "owned", Title = "Owned", Subtitles = mode == "empty" ? [] : tracks };
        if (mode == "preflight")
        {
            var fixture = new Fixture("ard"); var invalid = fixture.Request(files.Directory) with { SubtitleSleepSeconds = double.NaN }; await ProviderFamiliesChecks.Expect<ArgumentException>(() => fixture.Engine.InspectAsync(invalid, default)); Assert(fixture.ApiCalls == 0, "Invalid subtitle timing reached provider discovery."); return;
        }
        if (mode == "unselected") request = request with { WriteSubs = false };
        if (mode == "inline") request = request with { SubtitleSleepSeconds = 10 };
        if (mode == "cancel")
        {
            using var cancellation = new CancellationTokenSource(30); await ProviderFamiliesChecks.Expect<OperationCanceledException>(() => SubtitleWriter.WriteAsync(request, page, Path.Combine(files.Directory, "recording.mp4"), http, cancellation.Token)); Assert(handler.Times.Count == 0 && !Directory.EnumerateFiles(files.Directory).Any(), "Canceled subtitle waiting fetched or published captions."); return;
        }
        if (mode == "failed") { await ProviderFamiliesChecks.Expect<System.Net.Http.HttpRequestException>(() => SubtitleWriter.WriteAsync(request, page, Path.Combine(files.Directory, "recording.mp4"), http, default)); Assert(handler.Times.Count == 1 && handler.Times[0] >= .08 && !Directory.EnumerateFiles(files.Directory).Any(), "Subtitle delay suppressed a retrieval failure or published output."); return; }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)); using var result = await SubtitleWriter.WriteAsync(request, page, Path.Combine(files.Directory, "recording.mp4"), http, timeout.Token);
        if (mode is "empty" or "unselected" or "inline") Assert(handler.Times.Count == 0 && result.SavedFiles.Count == (mode == "inline" ? 2 : 0), "Subtitle timing delayed or fetched absent/resolved/unselected captions.");
        else Assert(handler.Times.Count == 2 && handler.Times[0] >= .08 && handler.Times[1] - handler.Times[0] >= .08 && result.SavedFiles.Count == 2, "A subtitle track was retrieved before its configured delay.");
    }
    private sealed class CaptionDelayHandler(Stopwatch clock, bool fail) : HttpMessageHandler
    {
        internal List<double> Times = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Times.Add(clock.Elapsed.TotalSeconds); return Task.FromResult(new HttpResponseMessage(fail ? HttpStatusCode.Forbidden : HttpStatusCode.OK) { Content = new StringContent("WEBVTT\n\n00:00.000 --> 00:01.000\nOwned\n") }); }
    }
}
