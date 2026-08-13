using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoutubeDlModern.Services;

namespace YoutubeDlModern.ViewModels;

public partial class AboutViewModel : ObservableObject, IDisposable
{
    private const int MaxLogChars = 6000;

    private readonly YtDlpEngine _engine;
    private readonly FfmpegManager _ffmpeg;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private CancellationTokenSource? _refreshCts;
    private int _refreshVersion;

    public event Action<string>? NotifyRequested;
    public event Action? EngineStateChanged;

    public AboutViewModel(YtDlpEngine engine, FfmpegManager ffmpeg)
    {
        _engine = engine;
        _ffmpeg = ffmpeg;
    }

    public string AppVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public string EnginePath => _engine.EnginePath;

    [ObservableProperty] private string _engineVersionText = "checking…";
    [ObservableProperty] private bool _isEngineInstalled;
    [ObservableProperty] private bool _isEngineBusy;
    [ObservableProperty] private string _engineLog = "";
    [ObservableProperty] private double _engineProgress;

    [ObservableProperty] private string _ffmpegStatusText = "checking…";
    [ObservableProperty] private bool _isFfmpegBusy;
    [ObservableProperty] private bool _isFfmpegAvailable;

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _refreshGate.WaitAsync(ct);
        try
        {
        var refreshVersion = Interlocked.Increment(ref _refreshVersion);
        var isEngineInstalled = _engine.IsInstalled;
        string engineVersionText;
        if (isEngineInstalled)
        {
            var version = await _engine.GetVersionAsync(ct);
            engineVersionText = version ?? "installed (version unknown)";
        }
        else
        {
            engineVersionText = "not installed";
        }

        var ffmpeg = await _ffmpeg.LocateAsync(ct);
        if (refreshVersion != Volatile.Read(ref _refreshVersion)) return;
        IsEngineInstalled = isEngineInstalled;
        EngineVersionText = engineVersionText;
        IsFfmpegAvailable = ffmpeg is not null;
        FfmpegStatusText = ffmpeg ?? "not found — required for merging formats and audio extraction";
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void Refresh()
    {
        var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var previous = Interlocked.Exchange(ref _refreshCts, refreshCts);
        previous?.Cancel();
        previous?.Dispose();
        _ = RefreshSafelyAsync(refreshCts);
    }

    private async Task RefreshSafelyAsync(CancellationTokenSource refreshCts)
    {
        try
        {
            await RefreshAsync(refreshCts.Token);
        }
        catch (OperationCanceledException) when (refreshCts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            NotifyRequested?.Invoke("Could not refresh component status: " + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _refreshCts, null, refreshCts), refreshCts))
                refreshCts.Dispose();
        }
    }

    [RelayCommand]
    private async Task UpdateEngineAsync()
    {
        if (IsEngineBusy) return;
        IsEngineBusy = true;
        EngineLog = "";
        try
        {
            await _engine.UpdateAsync(AppendEngineLog, _lifetimeCts.Token);
            AppendEngineLog("Done.");
            await RefreshAsync(_lifetimeCts.Token);
            EngineStateChanged?.Invoke();
            NotifyRequested?.Invoke($"Engine is up to date ({EngineVersionText}).");
        }
        catch (Exception ex)
        {
            AppendEngineLog("Error: " + ex.Message);
            NotifyRequested?.Invoke("Engine update failed: " + ex.Message);
        }
        finally
        {
            IsEngineBusy = false;
        }
    }

    [RelayCommand]
    private async Task InstallFfmpegAsync()
    {
        if (IsFfmpegBusy) return;
        IsFfmpegBusy = true;
        try
        {
            await _ffmpeg.EnsureInstalledAsync(
                new Progress<(double Percent, string Message)>(p => FfmpegStatusText = p.Message),
                _lifetimeCts.Token);
            await RefreshAsync(_lifetimeCts.Token);
            NotifyRequested?.Invoke("ffmpeg installed.");
        }
        catch (Exception ex)
        {
            FfmpegStatusText = "install failed: " + ex.Message;
            NotifyRequested?.Invoke("ffmpeg install failed: " + ex.Message);
        }
        finally
        {
            IsFfmpegBusy = false;
        }
    }

    [RelayCommand]
    private void OpenLink(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                using (Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })) { }
        }
        catch { /* no registered handler */ }
    }

    [RelayCommand]
    private void OpenEngineFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(_engine.EngineDir);
            using var process = Process.Start("explorer.exe", $"\"{_engine.EngineDir}\"");
        }
        catch { /* ignore */ }
    }

    private void AppendEngineLog(string line)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            try { dispatcher.Invoke(() => AppendEngineLog(line)); }
            catch { /* application is shutting down */ }
            return;
        }

        EngineLog += line + Environment.NewLine;
        if (EngineLog.Length > MaxLogChars)
            EngineLog = EngineLog[^MaxLogChars..];
    }

    public void Dispose()
    {
        _lifetimeCts.Cancel();
        var refreshCts = Interlocked.Exchange(ref _refreshCts, null);
        refreshCts?.Cancel();
        refreshCts?.Dispose();
        _lifetimeCts.Dispose();
    }
}
