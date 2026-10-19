using System.Globalization;

namespace YoutubeDl.Engine.Download;

/// <summary>A cancellable delay immediately before a selected item's media transfer.</summary>
internal sealed class NativeDownloadDelay
{
    private readonly double _minimum;
    private readonly double _maximum;

    internal NativeDownloadDelay(NativeDownloadRequest request)
    {
        var minimum = request.SleepIntervalSeconds;
        var maximum = request.MaximumSleepIntervalSeconds;
        if (minimum is { } low && (!double.IsFinite(low) || low < 0) ||
            maximum is { } high && (!double.IsFinite(high) || high < 0))
            throw new ArgumentException("Download delays must be finite nonnegative seconds.");
        if (maximum is not null && minimum is null)
            throw new ArgumentException("A maximum download delay requires a minimum download delay.");
        _minimum = minimum ?? 0;
        _maximum = maximum ?? _minimum;
        if (_maximum < _minimum)
            throw new ArgumentException("The maximum download delay cannot be less than the minimum.");
    }

    internal double Select(double fraction)
    {
        if (!double.IsFinite(fraction) || fraction is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(fraction));
        // Weighted interpolation keeps finite bounds finite even near double.MaxValue.
        return Math.Clamp(_minimum * (1 - fraction) + _maximum * fraction, _minimum, _maximum);
    }

    internal async Task WaitAsync(Action<string>? log, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var seconds = Select(Random.Shared.NextDouble());
        if (seconds == 0) return;
        log?.Invoke("[native] Waiting " + seconds.ToString("F2", CultureInfo.InvariantCulture) + " seconds before downloading.");
        // Short chunks support large configured intervals without timer overflow.
        while (seconds > 0)
        {
            var chunk = Math.Min(seconds, 3600);
            await Task.Delay(TimeSpan.FromSeconds(chunk), ct).ConfigureAwait(false);
            seconds -= chunk;
        }
        ct.ThrowIfCancellationRequested();
    }
}
