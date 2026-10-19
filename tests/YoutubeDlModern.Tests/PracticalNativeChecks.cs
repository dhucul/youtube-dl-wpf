using System.IO;
using YoutubeDl.Engine;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;
using YoutubeDlModern.ViewModels;

internal static partial class Program
{
    private static async Task PracticalNativeStartup()
    {
        var directory = Path.Combine(TestRoot, "practical-native-startup"); var service = new SettingsService(directory); service.Save(new());
        foreach (var live in new[] { false, true })
        {
            var draft = service.CreateDraft(); draft.Formats.NativeLiveDash = live; service.Save(draft);
            var engine = YoutubeDlModern.App.CreateDownloadEngine(service);
            Assert(engine is NativeEngineAdapter && engine.IsBuiltIn && engine.IsInstalled, "Production startup still depends on an external downloader or saved live toggle.");
            Assert(await engine.GetVersionAsync() == NativeEngine.Version && !engine.EnginePath.EndsWith("yt-dlp.exe", StringComparison.OrdinalIgnoreCase), "Built-in readiness opened or advertised an external engine.");
            await engine.EnsureInstalledAsync(null, default);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { await engine.EnsureInstalledAsync(null, cancellation.Token); throw new Exception("Canceled built-in readiness was accepted."); } catch (OperationCanceledException) { }
        }
    }
    private static async Task PracticalNativeStatus()
    {
        NativeDownloadRequest? inspected = null;
        var preview = new NativeEngineAdapter(AdapterEngine((request, _) => { inspected = request; return Task.FromResult(AdapterVideo()); }));
        await preview.GetVideoInfoAsync("https://www.youtube.com/watch?v=jNQXAC9IVRw", default);
        Assert(inspected?.SkipDownload == true, "YouTube preview initialized browser playback instead of metadata-only extraction.");
        var directory = Path.Combine(TestRoot, "practical-native-status"); var service = new SettingsService(directory); service.Save(new()); using var history = new HistoryService(directory);
        IYtDlpEngine engine = new NativeEngineAdapter(AdapterEngine((_, _) => Task.FromResult(AdapterVideo())), service); var queue = new DownloadQueueService(engine, new FakeFfmpeg(), service, history); using var model = new MainViewModel(queue, service, history, engine, new FfmpegManager(service));
        try
        {
            await model.RefreshEngineStatusAsync(); await model.About.RefreshAsync();
            Assert(model.EngineStatusText.StartsWith("Built-in downloader", StringComparison.Ordinal) && model.About.IsEngineInstalled && !model.About.CanReinstallEngine && !model.About.ReinstallEngineCommand.CanExecute(null), "The built-in app still presents external installer or backend status controls.");
        }
        finally { await queue.ShutdownAsync(); await model.StopAsync(); }
    }
}
