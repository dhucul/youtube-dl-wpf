using System.IO;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static async Task NativeDelaySettings()
    {
        var requests = new List<NativeDownloadRequest>();
        var adapter = new NativeEngineAdapter(new NativeEngine(httpFactory: request => { requests.Add(request); return new MediaHttp(new NativeCookieMappingHandler()); }));
        var settings = new AppSettings(); settings.Network.SleepInterval = 2; settings.Network.MaxSleepInterval = 3;
        var options = new DownloadOptions { OutputDirectory = TestRoot, AddMetadata = false, EmbedChapters = false, ExtraArgs = "--simulate" };
        await adapter.DownloadAsync("https://media.example/delay.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].SleepIntervalSeconds == 2 && requests[^1].MaximumSleepIntervalSeconds == 3, "Saved delays were dropped.");
        options.ExtraArgs = "--simulate --min-sleep-interval 0.25 --max-sleep-interval 0.5";
        await adapter.DownloadAsync("https://media.example/delay.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].SleepIntervalSeconds == 0.25 && requests[^1].MaximumSleepIntervalSeconds == 0.5 &&
            settings.Network.SleepInterval == 2 && settings.Network.MaxSleepInterval == 3, "Job delay overrides lost precision or modified saved settings.");
        using var cancel = new CancellationTokenSource(); var waited = false; var callbacks = 0;
        options.OutputDirectory = Path.Combine(TestRoot, "native-delay-cancelled"); options.ExtraArgs = "--sleep-interval 1 --max-sleep-interval 1";
        try
        {
            await adapter.DownloadAsync("https://media.example/delay.mp4", options, settings, null, new Progress<ProgressUpdate>(),
                line => { if (line.StartsWith("[native] Waiting ", StringComparison.Ordinal)) { waited = true; cancel.Cancel(); } }, cancel.Token, _ => callbacks++);
            throw new InvalidOperationException("Cancelling the wait completed a download.");
        }
        catch (OperationCanceledException)
        {
            Assert(waited && callbacks == 0 && (!Directory.Exists(options.OutputDirectory) || Directory.GetFiles(options.OutputDirectory, "*.mp4").Length == 0),
                "Cancellation during the saved delay published media or history.");
        }
    }
}
