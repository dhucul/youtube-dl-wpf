using System.Windows;
using System.Windows.Threading;
using YoutubeDlModern.Services;
using YoutubeDlModern.ViewModels;

namespace YoutubeDlModern;

public partial class App : Application
{
    private int _isHandlingUnhandledException;
    private MainViewModel? _mainViewModel;

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
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mainViewModel?.Dispose(); } catch { /* best effort */ }
        try { Queue?.Dispose(); } catch { /* best effort */ }
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
            Shutdown(-1);
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
            Shutdown(-1);
        }
    }
}
