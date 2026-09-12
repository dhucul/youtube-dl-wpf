using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YoutubeDlModern.Models;

namespace YoutubeDlModern.Services;

/// <summary>Persisted record of completed downloads (%APPDATA%\YoutubeDlModern\history.json).</summary>
public sealed class HistoryService : IDisposable
{
    private const int MaxEntries = 500;
    private const int MaximumHistoryBytes = 24 * 1024 * 1024;
    private readonly object _saveSync = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly SynchronizationContext _ui = SynchronizationContext.Current ?? new SynchronizationContext();
    private CancellationTokenSource? _debounceCts;
    private Task _pendingSave = Task.CompletedTask;
    private bool _isDisposed;
    private readonly string _storageDirectory;
    private string FilePath => Path.Combine(_storageDirectory, "history.json");
    public HistoryService(string? storageDirectory = null) => _storageDirectory = storageDirectory ?? SettingsService.AppDataDir;

    public ObservableCollection<HistoryEntry> Entries { get; } = new();
    public event Action<string>? PersistenceFailed;

    public void Load()
    {
        List<HistoryEntry>? list = null;
        if (!TryLoad(FilePath, out list))
            _ = TryLoad(AtomicFile.BackupPath(FilePath), out list);

        Entries.Clear();
        foreach (var entry in (list ?? new List<HistoryEntry>())
                     .Where(entry => entry is not null)
                     .OrderByDescending(entry => entry.CompletedAt)
                     .Take(MaxEntries))
        {
            Normalize(entry);
            Entries.Add(entry);
        }
        if (list is { Count: > 0 })
            ScheduleSave(); // Migrates any legacy plaintext URLs to DPAPI-protected storage.
    }

    public void Add(HistoryEntry entry)
        => AddRange([entry]);

    public void AddRange(IEnumerable<HistoryEntry> entries)
    {
        var additions = entries.ToList();
        if (additions.Count == 0)
            return;
        for (var index = additions.Count - 1; index >= 0; index--)
        {
            var entry = additions[index];
            Normalize(entry);
            Entries.Insert(0, entry);
        }
        while (Entries.Count > MaxEntries)
            Entries.RemoveAt(Entries.Count - 1);
        ScheduleSave();
    }

    public void Remove(HistoryEntry entry)
    {
        Entries.Remove(entry);
        ScheduleSave();
    }

    public void Clear()
    {
        Entries.Clear();
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var snapshot = Entries.Take(MaxEntries).Select(Clone).ToList();
        lock (_saveSync)
        {
            var nextCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            var previousCts = _debounceCts;
            _debounceCts = nextCts;
            previousCts?.Cancel();
            _pendingSave = SaveSnapshotAsync(snapshot, nextCts);
        }
    }

    private async Task SaveSnapshotAsync(List<HistoryEntry> snapshot, CancellationTokenSource operationCts)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), operationCts.Token).ConfigureAwait(false);
            await _writeGate.WaitAsync(operationCts.Token).ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    var persisted = snapshot.Select(Clone).ToList();
                    foreach (var entry in persisted)
                        entry.Url = SettingsSecrets.ProtectValue(entry.Url);
                    var json = JsonSerializer.Serialize(persisted);
                    AtomicFile.WriteAllText(FilePath, json, createBackup: false);
                    AtomicFile.WriteAllText(AtomicFile.BackupPath(FilePath), json, createBackup: false);
                }, operationCts.Token).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
        }
        catch (OperationCanceledException) when (operationCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _ui.Post(_ => PersistenceFailed?.Invoke("Could not save download history: " + ex.Message), null);
        }
        finally
        {
            lock (_saveSync)
            {
                if (ReferenceEquals(_debounceCts, operationCts))
                    _debounceCts = null;
            }
            operationCts.Dispose();
        }
    }

    public Task FlushAsync()
    {
        lock (_saveSync)
            return _pendingSave;
    }

    private static bool TryLoad(string path, out List<HistoryEntry>? list)
    {
        list = null;
        if (!File.Exists(path)) return false;

        try
        {
            list = JsonSerializer.Deserialize<List<HistoryEntry>>(AtomicFile.ReadAllText(path, MaximumHistoryBytes));
            if (list is not null)
            {
                foreach (var entry in list.Where(static entry => entry is not null))
                    entry.Url = SettingsSecrets.UnprotectValue(entry.Url ?? string.Empty);
            }
            return list is not null;
        }
        catch (JsonException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (CryptographicException) { return false; }
        catch (FormatException) { return false; }
        catch (DecoderFallbackException) { return false; }
    }

    private static void Normalize(HistoryEntry entry)
    {
        entry.Id ??= Guid.NewGuid().ToString("N");
        entry.Title ??= string.Empty;
        entry.Url ??= string.Empty;
        entry.FilePath ??= string.Empty;
        entry.Site ??= string.Empty;
        if (entry.FileSize < 0) entry.FileSize = 0;
    }

    private static HistoryEntry Clone(HistoryEntry entry) => new()
    {
        Id = entry.Id,
        Title = entry.Title,
        Url = entry.Url,
        FilePath = entry.FilePath,
        FileSize = entry.FileSize,
        CompletedAt = entry.CompletedAt,
        Site = entry.Site
    };

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        try { FlushAsync().GetAwaiter().GetResult(); } catch { /* persistence errors are already reported */ }
        _lifetimeCts.Cancel();
        lock (_saveSync)
            _debounceCts?.Cancel();
        _lifetimeCts.Dispose();
        _writeGate.Dispose();
    }
}
