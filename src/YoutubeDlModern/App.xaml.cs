using System.Windows;
using System.Windows.Threading;
using YoutubeDlModern.Services;
using YoutubeDlModern.ViewModels;

namespace YoutubeDlModern;

public partial class App : Application
{
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
            await Task.WhenAll(viewModelStop, queueStop);
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
    public static YtDlpEngine Engine { get; private set; } = null!;
    public static FfmpegManager Ffmpeg { get; private set; } = null!;
    public static HistoryService History { get; private set; } = null!;
    public static DownloadQueueService Queue { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;

        Settings = new SettingsService();
        Settings.Load();

        Engine = new YtDlpEngine(Settings);
        Ffmpeg = new FfmpegManager(Settings);
        History = new HistoryService();
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
        window.Show();
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
