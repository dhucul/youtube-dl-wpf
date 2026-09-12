using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

namespace YoutubeDlModern.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly DownloadQueueService _queue;
    private AppSettings _draft;

    public event Action<string>? NotifyRequested;

    public SettingsViewModel(SettingsService settings, DownloadQueueService queue)
    {
        _settings = settings;
        _queue = queue;
        _draft = settings.CreateDraft();
    }

    /// <summary>The live settings object graph the tabs bind to.</summary>
    public AppSettings S => _draft;

    [RelayCommand]
    private void Save()
    {
        try
        {
            _settings.Save(_draft);
            _draft = _settings.CreateDraft();
            OnPropertyChanged(nameof(S));
            _queue.ApplyConcurrency();
            NotifyRequested?.Invoke(_settings.BackupWarning ?? "Settings saved.");
        }
        catch (Exception ex)
        {
            NotifyRequested?.Invoke("Could not save settings: " + ex.Message);
        }
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        var result = System.Windows.MessageBox.Show(
            "Reset all settings to their defaults?",
            "YoutubeDL Modern",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        _draft = SettingsService.CreateDefault();
        OnPropertyChanged(nameof(S));
        NotifyRequested?.Invoke("Defaults restored. Select Save to apply them.");
    }

    [RelayCommand]
    private void BrowseDownloadDir()
    {
        var dlg = new OpenFolderDialog { Title = "Choose default download folder" };
        if (dlg.ShowDialog() == true)
            S.General.DownloadDirectory = dlg.FolderName;
    }

    [RelayCommand]
    private void BrowseCacheDir()
    {
        var dlg = new OpenFolderDialog { Title = "Choose cache folder" };
        if (dlg.ShowDialog() == true)
            S.General.CacheDir = dlg.FolderName;
    }

    [RelayCommand]
    private void BrowseArchiveFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose download archive file (created if missing)",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            CheckFileExists = false
        };
        if (dlg.ShowDialog() == true)
            S.General.DownloadArchive = dlg.FileName;
    }

    [RelayCommand]
    private void BrowseCookiesFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose cookies file (Netscape format)",
            Filter = "Cookie files (*.txt;*.cookies)|*.txt;*.cookies|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
            S.Auth.CookiesFile = dlg.FileName;
    }

    [RelayCommand]
    private void BrowseFfmpegDir()
    {
        var dlg = new OpenFolderDialog { Title = "Choose folder containing ffmpeg.exe" };
        if (dlg.ShowDialog() == true)
            S.PostProcess.FfmpegLocation = dlg.FolderName;
    }

    [RelayCommand]
    private void OpenDownloadDir()
    {
        try
        {
            System.IO.Directory.CreateDirectory(S.General.DownloadDirectory);
            using var process = Process.Start("explorer.exe", $"\"{S.General.DownloadDirectory}\"");
        }
        catch { /* ignore */ }
    }
}
