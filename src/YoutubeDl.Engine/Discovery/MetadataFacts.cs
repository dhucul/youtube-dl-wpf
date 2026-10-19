using System.Globalization;
using System.Text.Json;

namespace YoutubeDl.Engine.Discovery;

internal static class MetadataFacts
{
    internal static DateOnly? Date(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateOnly.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return day;
        if (text.Length >= 10 && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)) return day;
        return Timestamp(text) is { } time ? DateOnly.FromDateTime(time.UtcDateTime) : null;
    }
    internal static DateTimeOffset? Timestamp(string? text) => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value) ? value : null;
    internal static DateTimeOffset? Unix(long? value, bool milliseconds = false)
    {
        if (value is null) return null;
        try { return milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value) : DateTimeOffset.FromUnixTimeSeconds(value.Value); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
    internal static DateOnly? Day(DateTimeOffset? timestamp) => timestamp is { } value ? DateOnly.FromDateTime(value.UtcDateTime) : null;
    internal static object? Field(string name, PageMetadata page, NativeDownloadRequest request) => page.ParsedMetadata.TryGetValue(name, out var parsed) ? parsed : name switch
    {
        "id" => page.Id, "title" or "fulltitle" => page.Title, "description" => page.Description,
        "uploader" or "creator" or "channel" => page.Uploader, "extractor" or "extractor_key" => page.Extractor,
        "duration" => page.DurationSeconds, "is_live" => page.IsLive, "view_count" => page.ViewCount, "age_limit" => page.AgeLimit,
        "upload_date" => (page.UploadDate ?? Day(page.UploadTimestamp))?.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
        "timestamp" => page.UploadTimestamp?.ToUnixTimeMilliseconds() / 1000m,
        "webpage_url" => (page.SourcePageUrl ?? request.Url).AbsoluteUri, "original_url" => request.Url.AbsoluteUri,
        "webpage_url_domain" => (page.SourcePageUrl ?? request.Url).Host,
        "playlist" or "playlist_title" => request.PlaylistTitle, "playlist_id" => request.PlaylistId, "playlist_index" => request.PlaylistIndex,
        "playlist_autonumber" => request.PlaylistAutoNumber, "playlist_count" or "n_entries" => request.PlaylistCount,
        _ => page.MetadataFields.TryGetValue(name, out var value) ? Scalar(value) : null
    };
    private static object? Scalar(object? value) => value is JsonElement json ? json.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null, JsonValueKind.String => json.GetString(), JsonValueKind.True => true, JsonValueKind.False => false,
        JsonValueKind.Number => json.TryGetDecimal(out var number) ? number : throw new Download.NativeSelectionException("A numeric metadata value exceeds the filter's supported precision."),
        _ => json
    } : value;
}
