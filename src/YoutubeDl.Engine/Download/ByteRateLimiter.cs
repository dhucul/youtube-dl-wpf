namespace YoutubeDl.Engine.Download;

/// <summary>Smooths a byte-per-second cap across a single download.</summary>
public sealed class ByteRateLimiter
{
    private readonly long? _bytesPerSecond;
    private readonly object _sync = new();
    private double _reservedUntil;

    public ByteRateLimiter(long? bytesPerSecond)
    {
        _bytesPerSecond = bytesPerSecond is > 0 ? bytesPerSecond : null;
        _reservedUntil = Environment.TickCount64 - 1000d;
    }

    public async Task ThrottleAsync(int bytesJustWritten, CancellationToken ct)
    {
        if (_bytesPerSecond is not > 0 || bytesJustWritten <= 0) return;
        double until;
        lock (_sync)
        {
            var now = Environment.TickCount64;
            _reservedUntil = Math.Max(now - 1000d, _reservedUntil) + bytesJustWritten * 1000d / _bytesPerSecond.Value;
            until = _reservedUntil;
        }

        while (until - Environment.TickCount64 is > 0 and var delay)
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Ceiling(Math.Min(delay, 60000))), ct).ConfigureAwait(false);
    }

    public static long? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        var multiplier = 1L;
        if (text.EndsWith("K", StringComparison.OrdinalIgnoreCase)) { multiplier = 1024; text = text[..^1]; }
        else if (text.EndsWith("M", StringComparison.OrdinalIgnoreCase)) { multiplier = 1024 * 1024; text = text[..^1]; }
        else if (text.EndsWith("G", StringComparison.OrdinalIgnoreCase)) { multiplier = 1024L * 1024 * 1024; text = text[..^1]; }
        else if (text.EndsWith("T", StringComparison.OrdinalIgnoreCase)) { multiplier = 1L << 40; text = text[..^1]; }
        else if (text.EndsWith("P", StringComparison.OrdinalIgnoreCase)) { multiplier = 1L << 50; text = text[..^1]; }
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ||
            !double.IsFinite(number) || number * multiplier < 1 || number * multiplier >= long.MaxValue)
            throw new FormatException("The download rate must be at least one byte per second within the supported integer range, optionally followed by K, M, G, T or P.");
        return (long)(number * multiplier);
    }
}
