using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.YouTube;

internal static class PracticalYouTubeChecks
{
    public static IEnumerable<(string Name, Func<Task> Test)> All => new[] { "timeout", "startup", "repeat", "cancel", "denied" }.Select(mode => ("Practical YouTube public timeout " + mode, (Func<Task>)(() => Contract(mode))));
    private static async Task Contract(string mode)
    {
        using var cancellation = new CancellationTokenSource(); var calls = new List<string>(); var source = new TimedOutSource(mode == "cancel" ? cancellation.Cancel : null, mode == "startup");
        using var http = new MediaHttp(new SabrFixture.Handler(message =>
        {
            var path = message.RequestUri!.AbsolutePath; calls.Add(path);
            if (path == "/watch") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SabrFixture.Watch) });
            if (path != "/youtubei/v1/player") throw new Exception("An unexpected request followed a browser timeout.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(mode == "denied" ? "{\"playabilityStatus\":{\"status\":\"LOGIN_REQUIRED\",\"reason\":\"Owned unavailable public playback\"}}" : SabrFixture.Player().ToJsonString()) });
        }));
        var engine = new NativeEngine(youTubePlaybackSource: source, httpFactory: _ => http);
        var request = new NativeDownloadRequest { Url = new("https://www.youtube.com/watch?v=" + SabrFixture.VideoId), OutputDirectory = Path.GetTempPath(), Retries = 0 };
        if (mode == "cancel") { await ProviderFamiliesChecks.Expect<OperationCanceledException>(() => engine.InspectAsync(request, cancellation.Token)); PracticalFacebookChecks.Assert(calls.SequenceEqual(new[] { "/watch" }), "Cancellation entered another playback path."); return; }
        if (mode == "repeat")
        {
            var provider = new YouTubeProvider(playbackSource: source);
            await provider.ExtractAsync(request, http, default); await provider.ExtractAsync(request, http, default);
            PracticalFacebookChecks.Assert(source.Calls == 1 && calls.SequenceEqual(new[] { "/watch", "/youtubei/v1/player", "/watch", "/youtubei/v1/player" }), "A renewed public mobile session re-entered the failed browser client."); return;
        }
        var info = await engine.InspectAsync(request, default);
        PracticalFacebookChecks.Assert(source.Calls == 1 && calls.SequenceEqual(new[] { "/watch", "/youtubei/v1/player" }), "Public timeout recovery was not bounded to one declared mobile request.");
        PracticalFacebookChecks.Assert(mode == "denied" ? !info.Formats.Any(format => format.Availability == FormatAvailability.Ready) : info.Formats.Any(format => format.Availability == FormatAvailability.Ready), "Timeout recovery ignored public playback permission.");
    }
    private sealed class TimedOutSource(Action? cancel, bool startup) : IYouTubePlaybackSource
    {
        internal int Calls;
        public Task<PageMetadata> CaptureAsync(string videoId, NativeDownloadRequest request, CancellationToken ct) { Calls++; cancel?.Invoke(); throw startup ? new System.Runtime.InteropServices.COMException("Owned browser startup failure", unchecked((int)0x80080005)) : new TimeoutException("Owned browser startup timeout"); }
    }
}
