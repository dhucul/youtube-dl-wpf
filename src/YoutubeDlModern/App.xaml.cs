using System.Windows;
using System.Windows.Threading;
using YoutubeDlModern.Services;
using YoutubeDlModern.ViewModels;

namespace YoutubeDlModern;

public partial class App : Application
{
    private static readonly TimeSpan ShutdownDeadline = TimeSpan.FromSeconds(30);
    private int _isHandlingUnhandledException;
    private MainViewModel? _mainViewModel;
    private bool _isShuttingDown;
    private bool _shutdownComplete;

    private async Task RequestShutdownAsync(int exitCode = 0)
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;
        if (MainWindow is not null) MainWindow.IsEnabled = false;
        await Dispatcher.Yield(DispatcherPriority.Background); // Let the Closing event return first.
        try
        {
            var viewModelStop = _mainViewModel?.StopAsync() ?? Task.CompletedTask;
            var queueStop = Queue?.ShutdownAsync() ?? Task.CompletedTask;
            var stopping = Task.WhenAll(viewModelStop, queueStop);
            // Canceled work normally stops at once. Never leave a disabled window open
            // indefinitely; interrupted transfers resume from their partial files.
            if (await Task.WhenAny(stopping, Task.Delay(ShutdownDeadline)) == stopping)
                await stopping;
            else
                _ = stopping.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
            if (History is not null) await History.FlushAsync();
        }
        catch (Exception ex)
        {
            exitCode = -1;
            MessageBox.Show("Could not finish closing: " + ex.Message, "YoutubeDL Modern");
        }
        finally
        {
            _shutdownComplete = true;
            Shutdown(exitCode);
        }
    }

    public static SettingsService Settings { get; private set; } = null!;
    public static IYtDlpEngine Engine { get; private set; } = null!;
    public static FfmpegManager Ffmpeg { get; private set; } = null!;
    public static HistoryService History { get; private set; } = null!;
    public static DownloadQueueService Queue { get; private set; } = null!;

    internal static IYtDlpEngine CreateDownloadEngine(SettingsService settings) => new NativeEngineAdapter(settings: settings);

    internal static string? ResolveStorageDirectory(string[] arguments)
    {
        var index = Array.IndexOf(arguments, "--data-directory");
        if (index < 0) return null;
        if (index + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index + 1]) || arguments[index + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("The data-directory option needs a folder path.");
        return System.IO.Path.GetFullPath(arguments[index + 1]);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;

        var storageDirectory = ResolveStorageDirectory(e.Args);
        var verifyStartup = e.Args.Contains("--verify-startup", StringComparer.Ordinal);
        if (verifyStartup && storageDirectory is null) throw new ArgumentException("Startup verification requires an isolated data directory.");
        Settings = new SettingsService(storageDirectory);
        Settings.Load();

        Engine = CreateDownloadEngine(Settings);
        Ffmpeg = new FfmpegManager(Settings);
        History = new HistoryService(storageDirectory);
        History.Load();
        Queue = new DownloadQueueService(Engine, Ffmpeg, Settings, History);

        var mainVm = new MainViewModel(Queue, Settings, History, Engine, Ffmpeg);
        _mainViewModel = mainVm;
        var window = new MainWindow { DataContext = mainVm };
        MainWindow = window;
        window.Closing += (_, args) =>
        {
            if (_shutdownComplete) return;
            args.Cancel = true;
            _ = RequestShutdownAsync();
        };
        if (verifyStartup)
        {
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await mainVm.RefreshEngineStatusAsync();
                    var view = (FrameworkElement)window.Content;
                    view.Measure(new Size(1120, 760)); view.Arrange(new Rect(0, 0, 1120, 760)); view.UpdateLayout();
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1120, 760, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(view);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    System.IO.Directory.CreateDirectory(storageDirectory!);
                    using (var output = System.IO.File.Create(System.IO.Path.Combine(storageDirectory!, "startup.png"))) encoder.Save(output);
                    var report = new { Engine.IsBuiltIn, Engine.IsInstalled, Engine.DisplayName, Version = await Engine.GetVersionAsync(), mainVm.EngineStatusText, NativeAdapter = Engine is NativeEngineAdapter, HistoryEntries = History.Entries.Count };
                    await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(storageDirectory!, "startup-verification.json"), System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    await RequestShutdownAsync();
                }
                catch { await RequestShutdownAsync(-1); }
            });
        }
        else window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mainViewModel?.Dispose(); } catch { /* best effort */ }
        // Normal closing has already awaited the queue. Never block this dispatcher
        // waiting for workers during an emergency/system exit.
        if (!_shutdownComplete)
            try { Queue?.CancelAll(); } catch { /* best effort */ }
        try { History?.FlushAsync().GetAwaiter().GetResult(); } catch { /* best effort */ }
        try { History?.Dispose(); } catch { /* best effort */ }
        try { Settings.Save(); } catch { /* best effort */ }
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (Interlocked.Exchange(ref _isHandlingUnhandledException, 1) != 0)
        {
            _ = RequestShutdownAsync(-1);
            return;
        }

        try
        {
            MessageBox.Show(
                $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nThe application will close to prevent repeated failures.",
                "YoutubeDL Modern",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // If the error dialog itself cannot be displayed, still terminate cleanly.
        }
        finally
        {
            _ = RequestShutdownAsync(-1);
        }
    }
}
