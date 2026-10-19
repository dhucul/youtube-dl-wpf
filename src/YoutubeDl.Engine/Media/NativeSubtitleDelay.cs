namespace YoutubeDl.Engine.Media;

/// <summary>One cancellable wait before fetching a selected logical subtitle track.</summary>
internal static class NativeSubtitleDelay
{
    internal static void Validate(double? seconds)
    {
        if (seconds is { } value && (!double.IsFinite(value) || value < 0)) throw new ArgumentException("Subtitle download delays require finite nonnegative seconds.");
    }
    internal static async Task WaitAsync(NativeDownloadRequest request, CancellationToken ct)
    {
        var seconds = request.SubtitleSleepSeconds ?? 0; Validate(seconds); ct.ThrowIfCancellationRequested();
        while (seconds > 0) { var chunk = Math.Min(seconds, 3600); await Task.Delay(TimeSpan.FromSeconds(chunk), ct).ConfigureAwait(false); seconds -= chunk; }
        ct.ThrowIfCancellationRequested();
    }
}
