namespace YoutubeDl.Engine.Http;

/// <summary>Job-owned request pacing, active only while extracting provider data.</summary>
internal static class NativeExtractionDelay
{
    private static readonly AsyncLocal<State?> Current = new();
    private static readonly AsyncLocal<int> Depth = new();
    internal static void Validate(double? seconds)
    {
        if (seconds is { } value && (!double.IsFinite(value) || value < 0)) throw new ArgumentException("Extraction request delays require finite nonnegative seconds.");
    }
    internal static IDisposable Enter(double? seconds)
    {
        Validate(seconds); var previous = Current.Value; Current.Value = seconds is > 0 ? new(seconds.Value) : null;
        return new Scope(() => Current.Value = previous);
    }
    internal static IDisposable Extracting()
    {
        var previous = Depth.Value; Depth.Value = previous + 1;
        return new Scope(() => Depth.Value = previous);
    }
    internal static async Task BeforeRequestAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var state = Current.Value; if (state is null || Depth.Value == 0) return;
        await state.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (state.Started)
            {
                var remaining = state.Seconds;
                while (remaining > 0) { var part = Math.Min(remaining, 3600); await Task.Delay(TimeSpan.FromSeconds(part), ct).ConfigureAwait(false); remaining -= part; }
            }
            ct.ThrowIfCancellationRequested(); state.Started = true;
        }
        finally { state.Gate.Release(); }
    }
    private sealed class State(double seconds)
    {
        internal readonly double Seconds = seconds;
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal bool Started;
    }
    private sealed class Scope(Action restore) : IDisposable
    {
        private Action? _restore = restore;
        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
    }
}
