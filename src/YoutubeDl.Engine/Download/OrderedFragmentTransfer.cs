using System.Runtime.ExceptionServices;

namespace YoutubeDl.Engine.Download;

/// <summary>Bounds active and buffered fragments while consuming them in manifest order.</summary>
internal static class OrderedFragmentTransfer
{
    internal static void Validate(int concurrency)
    {
        if (concurrency is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(concurrency), "Choose between 1 and 32 concurrent fragments.");
    }
    internal static async Task RunAsync<T>(IReadOnlyList<T> items, int concurrency,
        Func<T, int, CancellationToken, Task> fetch, Func<T, int, CancellationToken, Task> consume, CancellationToken ct)
    {
        Validate(concurrency); ct.ThrowIfCancellationRequested();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pending = new Queue<(int Index, Task Transfer)>();
        ExceptionDispatchInfo? failure = null;
        var next = 0;
        try
        {
            while (next < items.Count && pending.Count < concurrency) Start();
            while (pending.TryDequeue(out var current))
            {
                await current.Transfer.ConfigureAwait(false);
                stop.Token.ThrowIfCancellationRequested();
                await consume(items[current.Index], current.Index, stop.Token).ConfigureAwait(false);
                if (next < items.Count) Start();
            }
        }
        catch (Exception ex)
        {
            Interlocked.CompareExchange(ref failure, ExceptionDispatchInfo.Capture(ex), null);
            Cancel();
            try { await Task.WhenAll(pending.Select(item => item.Transfer)).ConfigureAwait(false); } catch { }
            ct.ThrowIfCancellationRequested();
            failure.Throw();
            throw;
        }
        void Start()
        {
            var index = next++;
            pending.Enqueue((index, Fetch(index)));
        }
        async Task Fetch(int index)
        {
            try { stop.Token.ThrowIfCancellationRequested(); await fetch(items[index], index, stop.Token).ConfigureAwait(false); }
            catch (Exception ex)
            {
                if (!stop.IsCancellationRequested) Interlocked.CompareExchange(ref failure, ExceptionDispatchInfo.Capture(ex), null);
                Cancel(); throw;
            }
        }
        void Cancel()
        {
            try { stop.Cancel(); }
            catch (Exception ex) { Interlocked.CompareExchange(ref failure, ExceptionDispatchInfo.Capture(ex), null); }
        }
    }
}
