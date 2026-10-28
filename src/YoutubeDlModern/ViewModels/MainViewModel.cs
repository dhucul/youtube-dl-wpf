using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoutubeDlModern.Services;

namespace YoutubeDlModern.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IYtDlpEngine _engine;
    private readonly SettingsService _engineSettings;
    private bool _nativeLiveState;
    private readonly bool _checkEngineVersionOnStart;
    private readonly DispatcherTimer _notifyTimer;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private int _engineRefreshVersion;
    private bool _isDisposed;
    private Task _engineStatusRefresh = Task.CompletedTask;

    public QueueViewModel Queue { get; }
    public SettingsViewModel Settings { get; }
    public HistoryViewModel History { get; }
    public HelpViewModel Help { get; } = new();
    public AboutViewModel About { get; }

    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private string _selectedNav = "Queue";
    [ObservableProperty] private string _engineStatusText = "engine: checking...";
    [ObservableProperty] private string _notificationText = "";
    [ObservableProperty] private bool _isNotificationVisible;

    public MainViewModel(
        DownloadQueueService queueService,
        SettingsService settings,
        HistoryService history,
        IYtDlpEngine engine,
        FfmpegManager ffmpeg)
    {
        _engine = engine;
        _engineSettings=settings;_nativeLiveState=engine.IsBuiltIn;
        _checkEngineVersionOnStart = settings.Data.General.CheckEngineUpdatesOnStart;

        Queue = new QueueViewModel(queueService, settings, engine);
        Settings = new SettingsViewModel(settings, queueService);
        History = new HistoryViewModel(history);
        About = new AboutViewModel(engine, ffmpeg);

        _currentPage = Queue;

        Queue.NotifyRequested += Notify;
        Queue.EngineStateChanged += () => StartEngineStatusRefresh(forceVersionCheck: true);
        Settings.NotifyRequested += Notify;
        About.NotifyRequested += Notify;
        About.EngineStateChanged += () => StartEngineStatusRefresh(forceVersionCheck: true);

        queueService.ItemCompleted += (_, item) => Notify($"Completed: {item.Title}");
        queueService.ItemFailed += (_, item) => Notify($"Failed: {item.Title}");

        queueService.QueueError += Notify;
        history.PersistenceFailed += Notify;

        _notifyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _notifyTimer.Tick += (_, _) =>
        {
            IsNotificationVisible = false;
            _notifyTimer.Stop();
        };

        settings.Changed+=OnEngineSettingsChanged;
        StartEngineStatusRefresh();

        if (settings.LoadWarning is not null || history.LoadWarning is not null)
            Notify(string.Join(" ", new[] { settings.LoadWarning, history.LoadWarning }.OfType<string>()));
    }
    private void OnEngineSettingsChanged()
    {
        if(_isDisposed||_nativeLiveState==_engine.IsBuiltIn)return;
        _nativeLiveState=_engine.IsBuiltIn;
        _notifyTimer.Dispatcher.InvokeAsync(()=>{if(!_isDisposed)StartEngineStatusRefresh();});
    }

    private void StartEngineStatusRefresh(bool forceVersionCheck = false)
    {
        var refresh = RefreshEngineStatusAsync(forceVersionCheck);
        _engineStatusRefresh = _engineStatusRefresh.IsCompleted ? refresh : Task.WhenAll(_engineStatusRefresh, refresh);
    }

    public async Task RefreshEngineStatusAsync(bool forceVersionCheck = false)
    {
        var refreshVersion = Interlocked.Increment(ref _engineRefreshVersion);
        try
        {
            string status;
            if (_engine.IsBuiltIn)
            {
                status = _engine.DisplayName + " " + YoutubeDl.Engine.NativeEngine.Version;
            }
            else if (!_engine.IsInstalled)
            {
                status = "engine: not installed";
            }
            else if (_checkEngineVersionOnStart || forceVersionCheck)
            {
                var version = await _engine.GetVersionAsync(_lifetimeCts.Token);
                status = version is null ? "engine: error" : $"yt-dlp {version}";
            }
            else
            {
                status = "yt-dlp installed";
            }

            if (_isDisposed || refreshVersion != Volatile.Read(ref _engineRefreshVersion)) return;
            EngineStatusText = status;
            Queue.RefreshEngineState();
        }
        catch
        {
            if (_isDisposed || refreshVersion != Volatile.Read(ref _engineRefreshVersion)) return;
            EngineStatusText = "engine: error";
            Queue.RefreshEngineState();
        }
    }

    [RelayCommand]
    private void Navigate(string page)
    {
        SelectedNav = page;
        CurrentPage = page switch
        {
            "Settings" => Settings,
            "History" => History,
            "Help" => Help,
            "About" => About,
            _ => Queue
        };

        if (page == "About")
            About.Refresh();
    }

    public void Notify(string message)
    {
        if (_isDisposed) return;
        NotificationText = message;
        IsNotificationVisible = true;
        _notifyTimer.Stop();
        _notifyTimer.Start();
    }

    public async Task StopAsync()
    {
        Dispose();
        await Task.WhenAll(Queue.WaitForPendingWorkAsync(), About.WaitForPendingWorkAsync(), _engineStatusRefresh);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _engineSettings.Changed-=OnEngineSettingsChanged;
        _notifyTimer.Stop();
        _lifetimeCts.Cancel();
        Queue.Dispose();
        About.Dispose();
        _lifetimeCts.Dispose();
    }
}
