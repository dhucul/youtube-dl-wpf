using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

namespace YoutubeDlModern.ViewModels;

public partial class DownloadItemViewModel : ObservableObject
{
    private const int MaxLogLines = 400;
    private readonly DownloadQueueService _queue;

    public DownloadItemViewModel(
        DownloadQueueService queue,
        string url,
        DownloadOptions options,
        AppSettings settingsSnapshot,
        VideoInfo? preview,
        ImageSource? thumbnail)
    {
        _queue = queue;
        Url = url;
        Options = options.CopyForQueue();
        if (preview?.NativePlaylistSnapshot is { } selection) Options.NativePlaylistSnapshot = selection;
        SettingsSnapshot = settingsSnapshot;
        _title = preview?.IsPlaylist == true
            ? $"Playlist: {preview.DisplayTitle} ({preview.Entries?.Count ?? 0} videos)"
            : preview?.DisplayTitle ?? url;
        _thumbnail = thumbnail;
        _addedAt = DateTime.Now;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Url { get; }
    public DownloadOptions Options { get; }
    public AppSettings SettingsSnapshot { get; }
    public IReadOnlyList<string> OutputPaths { get; private set; } = [];

    [ObservableProperty] private string _title;
    [ObservableProperty] private ImageSource? _thumbnail;
    [ObservableProperty] private DownloadStatus _status = DownloadStatus.Queued;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _sizeText = "";
    [ObservableProperty] private string _speedText = "";
    [ObservableProperty] private string _etaText = "";
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private string _outputPath = "";
    [ObservableProperty] private DateTime _addedAt;

    public ObservableCollection<string> Log { get; } = new();

    internal CancellationTokenSource? Cts { get; set; }
    internal bool UserCanceled { get; set; }
    private readonly HashSet<string> _recordedFiles = new(StringComparer.OrdinalIgnoreCase);

    internal bool RecordCompletedFile(string path)
    {
        if (!_recordedFiles.Add(path)) return false;
        OutputPaths = _recordedFiles.ToList();
        OutputPath = path;
        return true;
    }

    // ---- State-driven button visibility ----
    public bool ShowPause => Status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Processing;
    public bool ShowResume => Status is DownloadStatus.Paused or DownloadStatus.Failed or DownloadStatus.Canceled;
    public bool ShowCancel => Status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Processing;
    public bool ShowRemove => Status is DownloadStatus.Paused or DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Canceled;
    public bool ShowOpenFile => Status == DownloadStatus.Completed && FileLaunchSafety.IsSafeMediaFile(OutputPath);
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public bool IsIndeterminate => Status == DownloadStatus.Processing ||
                                   (Status == DownloadStatus.Downloading && Progress <= 0 && string.IsNullOrEmpty(SizeText));

    public string StatusText => Status switch
    {
        DownloadStatus.Queued => "Queued",
        DownloadStatus.Downloading => "Downloading",
        DownloadStatus.Processing => "Processing",
        DownloadStatus.Paused => "Paused",
        DownloadStatus.Completed => "Completed",
        DownloadStatus.Failed => "Failed",
        DownloadStatus.Canceled => "Canceled",
        _ => ""
    };

    partial void OnStatusChanged(DownloadStatus value)
    {
        OnPropertyChanged(nameof(ShowPause));
        OnPropertyChanged(nameof(ShowResume));
        OnPropertyChanged(nameof(ShowCancel));
        OnPropertyChanged(nameof(ShowRemove));
        OnPropertyChanged(nameof(ShowOpenFile));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsIndeterminate));
    }

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnOutputPathChanged(string value) => OnPropertyChanged(nameof(ShowOpenFile));
    partial void OnProgressChanged(double value) => OnPropertyChanged(nameof(IsIndeterminate));
    partial void OnSizeTextChanged(string value) => OnPropertyChanged(nameof(IsIndeterminate));

    // ---- State transitions (called by the queue service, always on UI thread) ----
    internal void Reset()
    {
        // Scheduled runs own disposal, including runs canceled before being dequeued.
        Cts = new CancellationTokenSource();
        UserCanceled = false;
        ErrorText = "";
        Progress = 0;
        SizeText = "";
        SpeedText = "";
        EtaText = "";
        OutputPath = OutputPaths.LastOrDefault() ?? "";
        Status = DownloadStatus.Queued;
    }

    internal void MarkDownloading() => Status = DownloadStatus.Downloading;

    internal void ApplyProgress(ProgressUpdate u)
    {
        if (Status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Canceled or DownloadStatus.Paused)
            return;
        if (!string.IsNullOrWhiteSpace(u.FinalFilePath))
        {
            OutputPath = u.FinalFilePath!;
            return;
        }

        if (u.IsPostProcessing)
        {
            Status = DownloadStatus.Processing;
            return;
        }

        if (u.Status is "downloading" or "finished" || u.DownloadedBytes.HasValue)
            Status = DownloadStatus.Downloading;

        if (u.DownloadedBytes is > 0 || u.TotalBytes is > 0)
        {
            var dl = Formatting.FormatBytes(u.DownloadedBytes);
            var tot = Formatting.FormatBytes(u.TotalBytes);
            SizeText = tot.Length > 0 ? $"{dl} / {tot}" : dl;
        }

        if (u.DownloadedBytes is > 0 && u.TotalBytes is > 0)
            Progress = Math.Clamp(100.0 * u.DownloadedBytes.Value / u.TotalBytes.Value, 0, 100);

        SpeedText = Formatting.FormatSpeed(u.SpeedBytesPerSec);
        EtaText = Formatting.FormatEta(u.EtaSeconds) is { Length: > 0 } eta ? $"ETA {eta}" : "";
    }

    internal void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        Log.Add(line);
        while (Log.Count > MaxLogLines)
            Log.RemoveAt(0);
    }

    internal void MarkPaused()
    {
        Status = DownloadStatus.Paused;
        SpeedText = "";
        EtaText = "";
        DetachCancellationSource();
    }

    internal void MarkCanceled()
    {
        Status = DownloadStatus.Canceled;
        SpeedText = "";
        EtaText = "";
        DetachCancellationSource();
    }

    internal void MarkCompleted(IReadOnlyList<string> finalPaths)
    {
        foreach (var path in finalPaths) RecordCompletedFile(path);
        var finalPath = OutputPaths.LastOrDefault();
        if (!string.IsNullOrWhiteSpace(finalPath))
            OutputPath = finalPath!;
        Status = DownloadStatus.Completed;
        Progress = 100;
        SpeedText = "";
        EtaText = "";
        DetachCancellationSource();
    }

    internal void MarkFailed(string message)
    {
        ErrorText = string.IsNullOrWhiteSpace(message) ? "Download failed." : message;
        Status = DownloadStatus.Failed;
        SpeedText = "";
        EtaText = "";
        DetachCancellationSource();
    }

    private void DetachCancellationSource() => Cts = null;

    // ---- Commands ----
    [RelayCommand] private void Pause() => _queue.Pause(this);
    [RelayCommand] private void Resume() => _queue.StartOrResume(this);
    [RelayCommand] private void Cancel() => _queue.Cancel(this);
    [RelayCommand] private void Remove() => _queue.Remove(this);

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(OutputPath) && File.Exists(OutputPath))
            {
                using var process = Process.Start("explorer.exe", $"/select,\"{OutputPath}\"");
            }
            else
            {
                var dir = !string.IsNullOrWhiteSpace(OutputPath)
                    ? Path.GetDirectoryName(OutputPath)
                    : null;
                var fallback = !string.IsNullOrWhiteSpace(Options.OutputDirectory)
                    ? Options.OutputDirectory
                    : SettingsSnapshot.General.DownloadDirectory;
                using var process = Process.Start("explorer.exe", $"\"{dir ?? fallback}\"");
            }
        }
        catch { /* explorer unavailable */ }
    }

    [RelayCommand]
    private void OpenFile()
    {
        try
        {
            if (FileLaunchSafety.IsSafeMediaFile(OutputPath))
            {
                using var process = Process.Start(new ProcessStartInfo(OutputPath) { UseShellExecute = true });
            }
        }
        catch { /* no handler */ }
    }

    [RelayCommand]
    private void CopyUrl()
    {
        try { System.Windows.Clipboard.SetText(Url); } catch { /* clipboard busy */ }
    }
}
