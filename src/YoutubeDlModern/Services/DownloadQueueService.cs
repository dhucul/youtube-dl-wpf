using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using YoutubeDlModern.Models;
using YoutubeDlModern.ViewModels;

namespace YoutubeDlModern.Services;

/// <summary>
/// Owns the download queue: scheduling with bounded concurrency,
/// pause (stop-and-resume via yt-dlp's own resume support), cancel, retry,
/// completion history and notifications. All public members are called on the UI thread.
/// </summary>
public sealed class DownloadQueueService : IAsyncDisposable
{
    private const int WorkerCount = 8;
    private const int MaximumQueueItems = 10_000;
    private sealed class ConcurrencyWaiter
    {
        public TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LinkedListNode<ConcurrencyWaiter>? Node { get; set; }
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    private sealed record ScheduledRun(DownloadItemViewModel Item, CancellationTokenSource Cts);

    private const int MaxPendingLogLines = 500;

    private readonly IYtDlpEngine _engine;
    private readonly IFfmpegLocator _ffmpeg;
    private readonly SettingsService _settings;
    private readonly HistoryService _history;
    private readonly SynchronizationContext _ui;

    private readonly object _concurrencySync = new();
    private readonly object _runsSync = new();
    // Runs not yet taken by a worker. Pausing or canceling one removes it here, so the
    // schedule never holds more than one run per queued item, however often it is toggled.
    private readonly object _scheduleSync = new();
    private readonly LinkedList<ScheduledRun> _pendingRuns = new();
    private readonly Dictionary<CancellationTokenSource, LinkedListNode<ScheduledRun>> _pendingByCts = new();
    private readonly SemaphoreSlim _pendingSignal = new(0);
    private bool _schedulingCompleted;
    private readonly LinkedList<ConcurrencyWaiter> _concurrencyWaiters = new();
    private readonly HashSet<Task> _activeRuns = new();
    private int _concurrencyLimit;
    private int _activeDownloadCount;
    private bool _isDisposed;

    public ObservableCollection<DownloadItemViewModel> Items { get; } = new();

    /// <summary>Raised (on UI thread) when an item finishes successfully.</summary>
    public event EventHandler<DownloadItemViewModel>? ItemCompleted;
    /// <summary>Raised (on UI thread) when an item fails.</summary>
    public event EventHandler<DownloadItemViewModel>? ItemFailed;
    /// <summary>Raised (on UI thread) for an unexpected scheduler failure.</summary>
    public event Action<string>? QueueError;

    public DownloadQueueService(IYtDlpEngine engine, IFfmpegLocator ffmpeg, SettingsService settings, HistoryService history)
    {
        _engine = engine;
        _ffmpeg = ffmpeg;
        _settings = settings;
        _history = history;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _concurrencyLimit = ConcurrencyLimit;
        for (var index = 0; index < WorkerCount; index++)
            TrackRun(WorkerLoopAsync());
    }

    private int ConcurrencyLimit => Math.Clamp(_settings.Data.General.MaxConcurrentDownloads, 1, 8);

    public bool CanEnqueue(int count) => count >= 0 && Items.Count <= MaximumQueueItems - count;

    public void ApplyConcurrency()
    {
        List<ConcurrencyWaiter> granted;
        lock (_concurrencySync)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _concurrencyLimit = ConcurrencyLimit;
            granted = TakeAvailableWaitersLocked();
        }

        CompleteGrantedWaiters(granted);
    }

    public DownloadItemViewModel Enqueue(
        string url,
        DownloadOptions options,
        VideoInfo? preview,
        ImageSource? thumbnail = null,
        AppSettings? settingsSnapshot = null)
    {
        if (!CanEnqueue(1))
            throw new InvalidOperationException(
                $"The queue is limited to {MaximumQueueItems:N0} items. Remove finished items before adding more.");
        var item = new DownloadItemViewModel(
            this, url, options, settingsSnapshot ?? _settings.CreateSnapshot(), preview, thumbnail);
        Items.Add(item);
        try
        {
            StartOrResume(item);
        }
        catch
        {
            Items.Remove(item);
            throw;
        }
        return item;
    }

    public void StartOrResume(DownloadItemViewModel item)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (item.Status is DownloadStatus.Downloading or DownloadStatus.Processing)
            return;
        if (item.Status == DownloadStatus.Queued && item.Cts is not null)
            return; // already scheduled

        item.Reset();
        var runCts = item.Cts ?? throw new InvalidOperationException("A scheduled download requires cancellation state.");
        lock (_scheduleSync)
        {
            var node = _pendingRuns.AddLast(new ScheduledRun(item, runCts));
            _pendingByCts.Add(runCts, node);
        }
        _pendingSignal.Release();
    }

    internal int PendingRunCount
    {
        get { lock (_scheduleSync) return _pendingRuns.Count; }
    }

    /// <summary>Removes a run no worker has taken yet. The caller then owns its source.</summary>
    private bool TryUnschedule(CancellationTokenSource runCts)
    {
        lock (_scheduleSync)
        {
            if (!_pendingByCts.Remove(runCts, out var node))
                return false;
            _pendingRuns.Remove(node);
        }
        _pendingSignal.Wait(0); // Keep the signal count in step; a miss only costs a spurious wake.
        return true;
    }

    private void StopQueuedRun(DownloadItemViewModel item, bool userCanceled)
    {
        var runCts = item.Cts;
        var unscheduled = runCts is not null && TryUnschedule(runCts);
        item.UserCanceled = userCanceled;
        runCts?.Cancel();
        if (userCanceled) item.MarkCanceled();
        else item.MarkPaused();
        // A run a worker already holds is disposed by that worker.
        if (unscheduled) runCts!.Dispose();
    }

    public void Pause(DownloadItemViewModel item)
    {
        if (item.Status == DownloadStatus.Queued)
        {
            // Never started — just take it out of the schedule.
            StopQueuedRun(item, userCanceled: false);
            return;
        }
        if (item.Status is DownloadStatus.Downloading or DownloadStatus.Processing)
            item.Cts?.Cancel(); // engine process is killed; partial file stays for -c resume
    }

    public void Cancel(DownloadItemViewModel item)
    {
        if (item.Status == DownloadStatus.Queued)
        {
            StopQueuedRun(item, userCanceled: true);
            return;
        }
        if (item.Status is DownloadStatus.Downloading or DownloadStatus.Processing)
        {
            item.UserCanceled = true;
            item.Cts?.Cancel();
        }
    }

    public void Remove(DownloadItemViewModel item)
    {
        if (item.Status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Processing)
            Cancel(item);
        Items.Remove(item);
    }

    public void StartAll()
    {
        foreach (var item in Items.Where(i => i.Status is DownloadStatus.Paused or DownloadStatus.Failed or DownloadStatus.Canceled))
            StartOrResume(item);
    }

    public void PauseAll()
    {
        foreach (var item in Items.Where(i => i.Status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Processing))
            Pause(item);
    }

    public void ClearCompleted()
    {
        var done = Items.Where(i => i.Status == DownloadStatus.Completed).ToList();
        foreach (var item in done)
            Items.Remove(item);
    }

    /// <summary>Cancels every scheduled or active run. Cancellation kills the process tree synchronously.</summary>
    public void CancelAll()
    {
        foreach (var item in Items.ToList())
        {
            if (item.Status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Processing)
            {
                item.UserCanceled = true;
                item.Cts?.Cancel();
            }
        }
    }

    private async Task RunItemAsync(DownloadItemViewModel item, CancellationTokenSource runCts)
    {
        var ct = runCts.Token; // Only this worker disposes the source.
        var acquired = false;
        var fileCommits = new List<Task>();
        var pendingLogs = new ConcurrentQueue<string>();
        var pendingLogCount = 0;
        var logDrainScheduled = 0;

        void PostIfCurrent(Action action) => _ui.Post(_ =>
        {
            if (ReferenceEquals(item.Cts, runCts))
                action();
        }, null);

        void ScheduleLogDrain()
        {
            if (Interlocked.CompareExchange(ref logDrainScheduled, 1, 0) != 0)
                return;

            _ui.Post(state =>
            {
                if (ReferenceEquals(item.Cts, runCts))
                {
                    while (pendingLogs.TryDequeue(out var line))
                    {
                        Interlocked.Decrement(ref pendingLogCount);
                        item.AppendLog(line);
                    }
                }
                else
                {
                    while (pendingLogs.TryDequeue(out _))
                        Interlocked.Decrement(ref pendingLogCount);
                }

                Volatile.Write(ref logDrainScheduled, 0);
                if (!pendingLogs.IsEmpty)
                    ScheduleLogDrain();
            }, null);
        }

        void QueueLog(string line)
        {
            pendingLogs.Enqueue(line);
            Interlocked.Increment(ref pendingLogCount);
            while (Volatile.Read(ref pendingLogCount) > MaxPendingLogLines && pendingLogs.TryDequeue(out _))
                Interlocked.Decrement(ref pendingLogCount);
            ScheduleLogDrain();
        }

        try
        {
            await AcquireConcurrencySlotAsync(ct).ConfigureAwait(false);
            acquired = true;

            await OnUiAsync(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (!ReferenceEquals(item.Cts, runCts)) throw new OperationCanceledException(ct);
                item.MarkDownloading();
            }).ConfigureAwait(false);

            var ffmpegDir = await _ffmpeg.LocateAsync(item.SettingsSnapshot, ct).ConfigureAwait(false);
            var result = await _engine.DownloadAsync(
                item.Url, item.Options, item.SettingsSnapshot, ffmpegDir,
                new CallbackProgress<ProgressUpdate>(u => PostIfCurrent(() => item.ApplyProgress(u))),
                QueueLog, ct,
                path => fileCommits.Add(OnUiAsync(() =>
                {
                    // A finalized file remains a success even if a later playlist entry
                    // fails, or cancellation arrives before the UI handles this callback.
                    if (item.RecordCompletedFile(path))
                        _history.Add(new HistoryEntry
                        {
                            Title = Path.GetFileNameWithoutExtension(path),
                            Url = item.Url,
                            FilePath = path,
                            FileSize = TryGetFileSize(path),
                            CompletedAt = DateTime.Now,
                            Site = TryGetHost(item.Url)
                        });
                }))).ConfigureAwait(false);

            // Process exit is authoritative. A late cancellation must not erase success.
            await OnUiAsync(() =>
            {
                if (!ReferenceEquals(item.Cts, runCts)) return;
                // An item the user removed mid-run still records its files, but is not announced.
                var stillListed = Items.Contains(item);
                if (result.ExitCode == 0)
                {
                    item.MarkCompleted(result.FinalFilePaths);
                    if (stillListed) ItemCompleted?.Invoke(this, item);
                }
                else
                {
                    item.MarkFailed(result.ErrorSummary);
                    if (stillListed) ItemFailed?.Invoke(this, item);
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await OnUiAsync(() =>
            {
                if (!ReferenceEquals(item.Cts, runCts)) return;
                if (item.UserCanceled) item.MarkCanceled();
                else item.MarkPaused();
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await OnUiAsync(() =>
            {
                if (!ReferenceEquals(item.Cts, runCts)) return;
                item.MarkFailed(ex.Message);
                ItemFailed?.Invoke(this, item);
            }).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                // The engine awaits every file-completion callback before DownloadAsync
                // returns or throws, so no producer can append another commit after this point.
                await Task.WhenAll(fileCommits).ConfigureAwait(false);
            }
            finally
            {
                if (acquired) ReleaseConcurrencySlot();
            }
        }
    }

    private Task OnUiAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(_ =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        }, null);
        return completion.Task;
    }

    private async Task AcquireConcurrencySlotAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ConcurrencyWaiter? waiter;
        lock (_concurrencySync)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_activeDownloadCount < _concurrencyLimit)
            {
                _activeDownloadCount++;
                return;
            }

            waiter = new ConcurrencyWaiter();
            waiter.Node = _concurrencyWaiters.AddLast(waiter);
        }

        using var registration = ct.Register(() => CancelConcurrencyWaiter(waiter, ct));
        await waiter.Completion.Task.ConfigureAwait(false);
    }

    private void CancelConcurrencyWaiter(ConcurrencyWaiter waiter, CancellationToken ct)
    {
        var removed = false;
        lock (_concurrencySync)
        {
            if (waiter.Node?.List == _concurrencyWaiters)
            {
                _concurrencyWaiters.Remove(waiter.Node);
                waiter.Node = null;
                removed = true;
            }
        }

        if (removed)
            waiter.Completion.TrySetCanceled(ct);
    }

    private void ReleaseConcurrencySlot()
    {
        List<ConcurrencyWaiter> granted;
        lock (_concurrencySync)
        {
            if (_activeDownloadCount > 0)
                _activeDownloadCount--;
            granted = TakeAvailableWaitersLocked();
        }

        CompleteGrantedWaiters(granted);
    }

    private List<ConcurrencyWaiter> TakeAvailableWaitersLocked()
    {
        var granted = new List<ConcurrencyWaiter>();
        while (!_isDisposed && _activeDownloadCount < _concurrencyLimit &&
               _concurrencyWaiters.First is { } node)
        {
            _concurrencyWaiters.RemoveFirst();
            node.Value.Node = null;
            _activeDownloadCount++;
            granted.Add(node.Value);
        }
        return granted;
    }

    private static void CompleteGrantedWaiters(IEnumerable<ConcurrencyWaiter> granted)
    {
        foreach (var waiter in granted)
            waiter.Completion.TrySetResult(true);
    }

    private void TrackRun(Task run)
    {
        lock (_runsSync)
            _activeRuns.Add(run);

        _ = ObserveRunAsync(run);
    }

    private async Task ObserveRunAsync(Task run)
    {
        try
        {
            await run.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _ui.Post(_ => QueueError?.Invoke("Download queue error: " + ex.Message), null);
        }
        finally
        {
            lock (_runsSync)
                _activeRuns.Remove(run);
        }
    }

    private async Task<ScheduledRun?> TakeNextRunAsync()
    {
        while (true)
        {
            await _pendingSignal.WaitAsync().ConfigureAwait(false);
            lock (_scheduleSync)
            {
                if (_pendingRuns.First is { } node)
                {
                    _pendingRuns.RemoveFirst();
                    _pendingByCts.Remove(node.Value.Cts);
                    return node.Value;
                }
                if (_schedulingCompleted) return null;
            }
        }
    }

    private async Task WorkerLoopAsync()
    {
        while (await TakeNextRunAsync().ConfigureAwait(false) is { } scheduled)
        {
            try
            {
                // Canceled queued runs are still drained, and their sources disposed
                // here. The UI may already have scheduled a different run for the item.
                if (!scheduled.Cts.IsCancellationRequested)
                    await RunItemAsync(scheduled.Item, scheduled.Cts).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await OnUiAsync(() => QueueError?.Invoke("Download queue error: " + ex.Message))
                    .ConfigureAwait(false);
            }
            finally
            {
                await OnUiAsync(() =>
                {
                    if (ReferenceEquals(scheduled.Item.Cts, scheduled.Cts))
                    {
                        if (scheduled.Item.UserCanceled) scheduled.Item.MarkCanceled();
                        else scheduled.Item.MarkPaused();
                    }
                }).ConfigureAwait(false);
                scheduled.Cts.Dispose();
            }
        }
    }

    public async Task ShutdownAsync()
    {
        List<ConcurrencyWaiter> waiting;
        var beginShutdown = false;
        lock (_concurrencySync)
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                beginShutdown = true;
                waiting = _concurrencyWaiters.ToList();
                _concurrencyWaiters.Clear();
                foreach (var waiter in waiting)
                    waiter.Node = null;
            }
            else
            {
                waiting = [];
            }
        }

        if (beginShutdown)
        {
            lock (_scheduleSync)
                _schedulingCompleted = true;
            // One wake per worker lets each observe completion once the remaining runs drain.
            _pendingSignal.Release(WorkerCount);
            CancelAll();
            foreach (var waiter in waiting)
                waiter.Completion.TrySetCanceled();
        }

        Task[] workers;
        lock (_runsSync)
            workers = _activeRuns.ToArray();
        try { await Task.WhenAll(workers).ConfigureAwait(false); }
        catch { /* worker faults are reported by ObserveRunAsync */ }
    }

    public async ValueTask DisposeAsync() => await ShutdownAsync().ConfigureAwait(false);

    private static string TryGetHost(string url)
    {
        try { return new Uri(url).Host; } catch { return ""; }
    }

    private static long TryGetFileSize(string path)
    {
        try { return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }
}
