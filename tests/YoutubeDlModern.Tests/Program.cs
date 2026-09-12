using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;
using YoutubeDlModern.ViewModels;

internal static class Program
{
    private static int _failures;
    private static readonly string TestRoot = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));

    [STAThread]
    private static int Main()
    {
        Directory.CreateDirectory(TestRoot);
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await Check("Format selectors honor offered heights and VP9 spellings", Selectors);
                await Check("Analysis excludes download side effects", AnalysisArguments);
                await Check("Fragment retries can be disabled and estimated sizes have a namespace", DownloadArguments);
                await Check("Playlist progress returns to downloading", PlaylistProgress);
                await Check("Rapid queued cancellation and resume preserve workers", QueueCancellation);
                await Check("Partial playlist files survive failure", PartialFailure);
                await Check("Partial playlist files survive cancellation", PartialCancellation);
                await Check("Shutdown drains successful completion and history", ShutdownCompletion);
                await Check("Preview options are ready before the thumbnail", PreviewReadiness);
                await Check("Discarded and canceled previews reject late results", StalePreview);
                await Check("Changing type or filters clears custom selection", CustomSelection);
                await Check("Saved authentication refreshes without overwriting overrides", Authentication);
                await Check("Backup failure preserves the committed settings", BackupFailure);
                await Check("ffmpeg recovery survives replacement until verification", FfmpegRollback);
                await Check("Canceled and oversized batches enqueue nothing", BatchAdmission);
                await Check("Engine installation publishes its status change", EngineInstallation);
            }
            catch (Exception ex) { _failures++; Console.Error.WriteLine(ex); }
            finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
        });
        Dispatcher.Run();
        Directory.Delete(TestRoot, recursive: true);
        Console.WriteLine(_failures == 0 ? "All 16 regression checks passed." : $"{_failures} check(s) failed.");
        return _failures == 0 ? 0 : 1;
    }

    private static async Task Check(string name, Func<Task> test)
    {
        try { await test().WaitAsync(TimeSpan.FromSeconds(20)); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failures++; Console.Error.WriteLine("FAIL " + name + ": " + ex); }
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("State transition timed out.");
            await Task.Delay(5);
        }
    }

    private static Task Selectors()
    {
        foreach (var height in new[] { "144", "240", "1080", "4320", "1920" })
            Assert(ArgsBuilder.BuildFormatSelector(height, "", "video-audio", "any", 0).Contains("height<=" + height), height);
        var selector = ArgsBuilder.BuildFormatSelector("1080", "", "video-audio", "vp9", 30);
        Assert(selector.Contains("height<=1080") && selector.Contains("fps<=30"), selector);
        var regex = selector.Split("vcodec~='")[1].Split('\'')[0];
        foreach (var codec in new[] { "vp9", "vp9.2", "vp09.00.10.08" })
            Assert(System.Text.RegularExpressions.Regex.IsMatch(codec, regex), codec);
        Assert(!System.Text.RegularExpressions.Regex.IsMatch("vp08.00", regex), "VP8 incorrectly accepted");
        return Task.CompletedTask;
    }

    private static Task AnalysisArguments()
    {
        var settings = new AppSettings();
        settings.General.ExtraArgs = "--no-simulate --exec calc --print-to-file title out.txt --config-locations other.conf --extractor-args youtube:player_client=web --proxy=http://localhost:8080";
        foreach (var args in new[]
        {
            ArgsBuilder.BuildInfoArgs(settings, "https://example.com/video"),
            ArgsBuilder.BuildListFormatsArgs(settings, "https://example.com/video")
        })
        {
            Assert(args.Contains("--simulate") && args.Contains("--ignore-config") && args.Contains("--no-exec"), "Missing analysis contract");
            Assert(!args.Any(x => x is "--no-simulate" or "--exec" or "--print-to-file" or "--config-locations"), "Side effect survived filtering");
            Assert(args.Contains("youtube:player_client=web") && args.Contains("--proxy=http://localhost:8080"), "Connection/extraction override lost");
        }
        return Task.CompletedTask;
    }

    private static Task DownloadArguments()
    {
        var settings = new AppSettings();
        settings.Network.FragmentRetries = 0;
        var args = ArgsBuilder.BuildDownloadArgs(settings, new DownloadOptions(), "https://example.com/video", null);
        Assert(args[args.IndexOf("--fragment-retries") + 1] == "0", "Zero retry option omitted");
        Assert(args.Any(x => x.Contains("progress.total_bytes,progress.total_bytes_estimate")), "Estimated-size alternative is wrong");
        return Task.CompletedTask;
    }

    private static Task PlaylistProgress()
    {
        var item = new DownloadItemViewModel(null!, "https://example.com/video", new(), new(), null, null);
        item.ApplyProgress(new() { IsPostProcessing = true });
        Assert(item.Status == DownloadStatus.Processing, "Missing processing transition");
        item.ApplyProgress(new() { Status = "downloading", DownloadedBytes = 10, TotalBytes = 100 });
        Assert(item.Status == DownloadStatus.Downloading && item.ShowPause && item.Progress == 10, "Next entry did not restore download state");
        item.ApplyProgress(new() { FinalFilePath = "file.mp4" });
        Assert(item.Status == DownloadStatus.Downloading, "Final path changed the phase");
        item.MarkCompleted([]);
        item.ApplyProgress(new() { IsPostProcessing = true });
        Assert(item.Status == DownloadStatus.Completed, "Late progress overwrote terminal state");
        return Task.CompletedTask;
    }

    private static async Task QueueCancellation()
    {
        await using var f = new Fixture();
        var errors = new List<string>();
        f.Queue.QueueError += errors.Add;
        f.Engine.Download = async (ct, _) => { await Task.Delay(20, ct); return new(); };
        for (var index = 0; index < 128; index++)
        {
            var item = f.Enqueue();
            f.Queue.Pause(item);
            f.Queue.StartOrResume(item);
            f.Queue.Cancel(item);
            f.Queue.Remove(item);
        }
        var final = f.Enqueue();
        await Until(() => final.Status == DownloadStatus.Completed);
        await f.Queue.ShutdownAsync();
        Assert(errors.Count == 0, string.Join("; ", errors));
        Assert(final.Cts is null, "Finished run still attached");
    }

    private static async Task PartialFailure()
    {
        await using var f = new Fixture();
        var path = f.MediaFile();
        f.Engine.Download = (_, file) => { file!(path); throw new InvalidOperationException("second entry failed"); };
        var item = f.Enqueue();
        await Until(() => item.Status == DownloadStatus.Failed);
        await f.Queue.ShutdownAsync();
        await f.History.FlushAsync();
        using var reloaded = new HistoryService(f.Directory);
        reloaded.Load();
        Assert(reloaded.Entries.Single().FilePath == path, "Finalized file was not persisted");
        Assert(item.OutputPaths.Contains(path), "Partial output missing from item");
    }

    private static async Task PartialCancellation()
    {
        await using var f = new Fixture();
        var path = f.MediaFile();
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Engine.Download = async (ct, file) =>
        {
            file!(path);
            reported.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new();
        };
        var item = f.Enqueue();
        await reported.Task;
        f.Queue.Cancel(item);
        await f.Queue.ShutdownAsync();
        Assert(item.Status == DownloadStatus.Canceled && f.History.Entries.Single().FilePath == path, "Cancellation erased a completed file");
    }

    private static async Task ShutdownCompletion()
    {
        await using var f = new Fixture();
        var path = f.MediaFile();
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource<DownloadRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Engine.Download = (_, file) => { file!(path); reported.SetResult(); return exited.Task; };
        var item = f.Enqueue();
        await reported.Task;
        var shutdown = f.Queue.ShutdownAsync();
        exited.SetResult(new() { FinalFilePaths = [path] });
        await shutdown;
        await f.History.FlushAsync();
        Assert(item.Status == DownloadStatus.Completed, "Late cancellation replaced successful exit");
        Assert(f.History.Entries.Single().FilePath == path, "Shutdown did not drain file commits");
    }

    private static async Task PreviewReadiness()
    {
        await using var f = new Fixture();
        f.Settings.Data.Formats.DefaultQuality = "1080";
        var thumbnail = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var vm = f.ViewModel((_, _) => thumbnail.Task);
        vm.SelectedQualityKey = "worst";
        vm.UrlInput = "https://example.com/video";
        var fetch = vm.FetchCommand.ExecuteAsync(null);
        await Until(() => vm.HasPreview);
        Assert(vm.SelectedQualityKey == "1080", "Published preview still had previous options");
        vm.SelectedQualityKey = "720";
        thumbnail.SetResult(null);
        await fetch;
        Assert(vm.SelectedQualityKey == "720", "Thumbnail continuation overwrote selection");
    }

    private static async Task StalePreview()
    {
        await using var f = new Fixture();
        var metadata = new TaskCompletionSource<VideoInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Engine.Info = (_, _, _) => metadata.Task;
        using var vm = f.ViewModel();
        vm.UrlInput = "https://example.com/video";
        var fetch = vm.FetchCommand.ExecuteAsync(null);
        vm.ClearInputCommand.Execute(null);
        metadata.SetResult(new() { Title = "Late result" });
        await fetch;
        Assert(!vm.HasPreview && vm.UrlInput == "", "Canceled metadata resurrected the preview");
    }

    private static async Task CustomSelection()
    {
        await using var f = new Fixture();
        using var vm = f.ViewModel();
        vm.CustomFormat = "137+ba/b";
        vm.SelectedQualityKey = "custom";
        vm.AddDownloadMode = "video-only";
        Assert(vm.SelectedQualityKey == "best" && vm.CustomFormat == "", "Type left old stream selection active");
        vm.CustomFormat = "137";
        vm.SelectedQualityKey = "custom";
        vm.AddMaxFps = "30";
        Assert(vm.SelectedQualityKey != "custom", "FPS constraint silently ignored");
    }

    private static async Task Authentication()
    {
        await using var f = new Fixture();
        using var vm = f.ViewModel();
        var draft = f.Settings.CreateDraft();
        draft.Auth.CookiesFromBrowser = "firefox";
        f.Settings.Save(draft);
        Assert(vm.BuildAnalysisOptions().CookiesFromBrowser == "firefox" && vm.AnalysisCookiesFromBrowser == "firefox", "Saved cookies were stale");
        vm.UseSavedAnalysisAuthentication = false;
        vm.AnalysisCookiesFromBrowser = "edge";
        draft.Auth.CookiesFromBrowser = "chrome";
        f.Settings.Save(draft);
        Assert(vm.BuildAnalysisOptions().CookiesFromBrowser == "edge", "Save overwrote explicit authentication override");
        vm.UseSavedAnalysisAuthentication = true;
        Assert(vm.BuildAnalysisOptions().CookiesFromBrowser == "chrome", "Could not return to saved defaults");
    }

    private static Task BackupFailure()
    {
        var dir = Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "settings.json.bak"));
        var settings = new SettingsService(dir);
        var draft = settings.CreateDraft();
        draft.General.MaxConcurrentDownloads = 7;
        settings.Save(draft);
        Assert(settings.Data.General.MaxConcurrentDownloads == 7 && settings.BackupWarning is not null, "Primary commit was reported as an overall failure");
        var reloaded = new SettingsService(dir);
        reloaded.Load();
        Assert(reloaded.Data.General.MaxConcurrentDownloads == settings.Data.General.MaxConcurrentDownloads, "Memory and disk disagree");
        return Task.CompletedTask;
    }

    private static Task FfmpegRollback()
    {
        var dir = Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
        var managed = Path.Combine(dir, "managed");
        var staging = Path.Combine(dir, "staging");
        var backup = Path.Combine(dir, "backup");
        Directory.CreateDirectory(managed);
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(managed, "old.txt"), "old");
        File.WriteAllText(Path.Combine(staging, "new.txt"), "new");
        var manager = new FfmpegManager(new SettingsService(dir), managed);
        manager.CommitManagedInstall(staging, backup);
        Assert(File.Exists(Path.Combine(backup, "old.txt")), "Backup deleted before verification");
        manager.RestoreManagedInstallAfterVerificationFailure(backup);
        Assert(File.Exists(Path.Combine(managed, "old.txt")) && !File.Exists(Path.Combine(managed, "new.txt")), "Rollback failed");
        return Task.CompletedTask;
    }

    private static async Task BatchAdmission()
    {
        await using var f = new Fixture();
        using var vm = f.ViewModel();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        try { vm.EnqueueBatch(["https://example.com/video"], new(), new(), cts.Token); throw new Exception("Canceled batch accepted"); }
        catch (OperationCanceledException) { }
        try { vm.EnqueueBatch(Enumerable.Repeat("https://example.com/video", 10_001).ToList(), new(), new(), default); throw new Exception("Oversized batch accepted"); }
        catch (InvalidOperationException) { }
        Assert(f.Queue.Items.Count == 0, "Batch admission was partial");
    }

    private static async Task EngineInstallation()
    {
        await using var f = new Fixture();
        f.Engine.IsInstalled = false;
        using var vm = f.ViewModel();
        var count = 0;
        vm.EngineStateChanged += () => count++;
        await vm.InstallEngineCommand.ExecuteAsync(null);
        Assert(vm.IsEngineInstalled && count == 1, "Install did not publish readiness");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string Directory { get; } = Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
        public SettingsService Settings { get; }
        public HistoryService History { get; }
        public FakeEngine Engine { get; } = new();
        public DownloadQueueService Queue { get; }
        public Fixture()
        {
            System.IO.Directory.CreateDirectory(Directory);
            Settings = new(Directory);
            History = new(Directory);
            Queue = new(Engine, new FakeFfmpeg(), Settings, History);
        }
        public DownloadItemViewModel Enqueue() => Queue.Enqueue("https://example.com/video", new(), null);
        public QueueViewModel ViewModel(Func<string?, CancellationToken, Task<ImageSource?>>? thumbnail = null) =>
            new(Queue, Settings, Engine, thumbnail ?? ((_, _) => Task.FromResult<ImageSource?>(null)));
        public string MediaFile()
        {
            var path = Path.Combine(Directory, "complete.mp4");
            File.WriteAllText(path, "completed media");
            return path;
        }
        public async ValueTask DisposeAsync()
        {
            await Queue.ShutdownAsync();
            await History.FlushAsync();
            History.Dispose();
        }
    }

    private sealed class FakeFfmpeg : IFfmpegLocator
    {
        public Task<string?> LocateAsync(AppSettings settingsSnapshot, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private sealed class FakeEngine : IYtDlpEngine
    {
        public bool IsInstalled { get; set; } = true;
        public Func<CancellationToken, Action<string>?, Task<DownloadRunResult>> Download { get; set; } = (_, _) => Task.FromResult(new DownloadRunResult());
        public Func<string, CancellationToken, AnalysisOptions?, Task<VideoInfo>> Info { get; set; } = (_, _, _) => Task.FromResult(new VideoInfo { Title = "Video" });
        public Task<VideoInfo> GetVideoInfoAsync(string url, CancellationToken ct, AnalysisOptions? options = null) => Info(url, ct, options);
        public Task<IReadOnlyList<FormatInfo>> ListFormatsAsync(string url, CancellationToken ct, AnalysisOptions? options = null, bool isPlaylist = false) => Task.FromResult<IReadOnlyList<FormatInfo>>([]);
        public Task EnsureInstalledAsync(IProgress<(double Percent, string Message)>? progress, CancellationToken ct) { IsInstalled = true; return Task.CompletedTask; }
        public Task<DownloadRunResult> DownloadAsync(string url, DownloadOptions options, AppSettings settingsSnapshot, string? ffmpegLocation,
            IProgress<ProgressUpdate> progress, Action<string> log, CancellationToken ct, Action<string>? onFileCompleted = null) => Download(ct, onFileCompleted);
    }
}
