using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

namespace YoutubeDlModern.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly HistoryService _history;

    public ObservableCollection<HistoryEntry> Entries => _history.Entries;

    [ObservableProperty] private bool _hasEntries;

    public HistoryViewModel(HistoryService history)
    {
        _history = history;
        _hasEntries = Entries.Count > 0;
        Entries.CollectionChanged += (_, _) => HasEntries = Entries.Count > 0;
    }

    [RelayCommand]
    private void Clear()
    {
        var result = System.Windows.MessageBox.Show(
            "Clear the entire download history?",
            "YoutubeDL Modern",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (result == System.Windows.MessageBoxResult.Yes)
            _history.Clear();
    }

    [RelayCommand]
    private void RemoveEntry(HistoryEntry? entry)
    {
        if (entry is not null)
            _history.Remove(entry);
    }

    [RelayCommand]
    private void OpenFolder(HistoryEntry? entry)
    {
        if (entry is null) return;
        try
        {
            if (!string.IsNullOrWhiteSpace(entry.FilePath) && File.Exists(entry.FilePath))
            {
                using var process = Process.Start("explorer.exe", $"/select,\"{entry.FilePath}\"");
            }
            else if (!string.IsNullOrWhiteSpace(entry.FilePath))
            {
                var dir = Path.GetDirectoryName(entry.FilePath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    using var process = Process.Start("explorer.exe", $"\"{dir}\"");
                }
            }
        }
        catch { /* ignore */ }
    }

    [RelayCommand]
    private void OpenFile(HistoryEntry? entry)
    {
        if (entry is null) return;
        try
        {
            if (FileLaunchSafety.IsSafeMediaFile(entry.FilePath))
            {
                using var process = Process.Start(new ProcessStartInfo(entry.FilePath) { UseShellExecute = true });
            }
        }
        catch { /* ignore */ }
    }
}
