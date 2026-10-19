using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Download;

/// <summary>One invocation's compiled title predicates and completed-item limit.</summary>
internal sealed class DownloadSelectionState
{
    private readonly Regex? _match;
    private readonly Regex? _reject;
    private readonly int _maximum;
    private readonly NativeDateRange _dates;
    private readonly NativeMetadataFilter _filters;
    private readonly long? _minimumViews;
    private readonly long? _maximumViews;
    private readonly int? _maximumAge;
    private int _completed;
    internal bool Exhausted => _maximum > 0 && _completed >= _maximum;
    internal DownloadSelectionState(NativeDownloadRequest request)
    {
        if (request.MaxDownloads < 0) throw new ArgumentOutOfRangeException(nameof(request.MaxDownloads), "The download limit cannot be negative.");
        _maximum = request.MaxDownloads;
        _match = Compile(request.MatchTitle); _reject = Compile(request.RejectTitle);
        if (request.MinimumViews is < 0 || request.MaximumViews is < 0 || request.MinimumViews is not null && request.MaximumViews is not null && request.MinimumViews > request.MaximumViews || request.MaximumAge is < 0 or > 99)
            throw new NativeSelectionException("The requested view-count or age bounds are invalid.");
        _minimumViews = request.MinimumViews; _maximumViews = request.MaximumViews; _maximumAge = request.MaximumAge;
        _dates = new(request, DateOnly.FromDateTime(DateTime.UtcNow)); _filters = new(request);
    }
    internal string? Rejection(Discovery.PageMetadata page, NativeDownloadRequest request, CancellationToken ct, bool incomplete = false)
    {
        if (Rejection(page.Title, ct) is { } titleReason) return titleReason;
        if (page.ViewCount is < 0 || page.AgeLimit is < 0) throw new InvalidDataException("The provider returned invalid view-count or age metadata.");
        var date = page.UploadDate ?? Discovery.MetadataFacts.Day(page.UploadTimestamp);
        if (date is { } knownDate && !_dates.Contains(knownDate)) return "The upload date is outside the requested range.";
        if (page.ViewCount is { } views && (_minimumViews is { } minimum && views < minimum || _maximumViews is { } maximum && views > maximum))
            return "The view count is outside the requested bounds.";
        if (_maximumAge is { } age && page.AgeLimit is { } requiredAge && requiredAge > age) return "The media's reported age restriction exceeds the requested limit.";
        return _filters.Matches(page, request, ct, incomplete) ? null : "The media does not match the requested metadata filter.";
    }
    internal string? Rejection(string title, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            if (_match is not null && !_match.IsMatch(title)) return "The video title does not match the requested title pattern.";
            ct.ThrowIfCancellationRequested();
            if (_reject is not null && _reject.IsMatch(title)) return "The video title matches the rejected title pattern.";
            return null;
        }
        catch (RegexMatchTimeoutException) { throw new NativeSelectionException("The title pattern exceeded its evaluation time limit."); }
    }
    internal void Record(NativeDownloadResult result, NativeDownloadRequest request)
    {
        if (!result.Succeeded) return;
        var media = result.NewOutputPaths ?? (result.Skipped ? [] : result.OutputPaths);
        if (request.Simulate || media.Count > 0 || result.NewSidecarPaths.Count > 0) _completed++;
    }
    private static Regex? Compile(string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return null;
        if (pattern.Length > 4096) throw new NativeSelectionException("The title pattern exceeds the supported length.");
        try { return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)); }
        catch (ArgumentException) { throw new NativeSelectionException("The title pattern is not a supported regular expression."); }
    }
}

/// <summary>A bad selection expression must stop even a playlist configured to ignore media failures.</summary>
public sealed class NativeSelectionException(string message) : ArgumentException(message);
