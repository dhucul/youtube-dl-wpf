namespace YoutubeDlModern.Services;

/// <summary>
/// Runs CPU-bound transformations away from the WPF dispatcher while keeping the
/// amount of queued thread-pool work deliberately small.
/// </summary>
public static class BackgroundWork
{
    private static readonly int ConcurrencyLimit = Math.Clamp(Environment.ProcessorCount / 2, 1, 2);
    private static readonly SemaphoreSlim Gate = new(ConcurrencyLimit, ConcurrencyLimit);

    public static async Task<T> RunAsync<T>(Func<CancellationToken, T> work, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(work);
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var result = work(ct);
                ct.ThrowIfCancellationRequested();
                return result;
            }, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }
}
