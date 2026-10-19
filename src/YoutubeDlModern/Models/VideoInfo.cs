using System.Text.Json.Serialization;

namespace YoutubeDlModern.Models;

/// <summary>
/// Video/playlist metadata as returned by `yt-dlp -J` (dump-single-json).
/// Only the fields the GUI consumes are mapped; everything else is ignored.
/// </summary>
public sealed class VideoInfo
{
    [JsonIgnore] internal YoutubeDl.Engine.NativePlaylistSnapshot? NativePlaylistSnapshot { get; init; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("uploader")] public string? Uploader { get; set; }
    [JsonPropertyName("channel")] public string? Channel { get; set; }
    [JsonPropertyName("duration")] public double? Duration { get; set; }
    [JsonPropertyName("view_count")] public long? ViewCount { get; set; }
    [JsonPropertyName("upload_date")] public string? UploadDate { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("webpage_url")] public string? WebpageUrl { get; set; }
    [JsonPropertyName("thumbnail")] public string? Thumbnail { get; set; }
    [JsonPropertyName("extractor_key")] public string? ExtractorKey { get; set; }
    [JsonPropertyName("is_live")] public bool? IsLive { get; set; }
    [JsonPropertyName("_type")] public string? Type { get; set; }

    [JsonPropertyName("thumbnails")] public List<ThumbnailInfo>? Thumbnails { get; set; }
    [JsonPropertyName("formats")] public List<FormatInfo>? Formats { get; set; }
    [JsonPropertyName("entries")] public List<PlaylistEntry>? Entries { get; set; }

    [JsonPropertyName("subtitles")] public Dictionary<string, List<SubtitleFormat>>? Subtitles { get; set; }
    [JsonPropertyName("automatic_captions")] public Dictionary<string, List<SubtitleFormat>>? AutomaticCaptions { get; set; }

    [JsonIgnore] public bool IsPlaylist =>
        string.Equals(Type, "playlist", StringComparison.OrdinalIgnoreCase) ||
        Entries?.Any(static entry => entry is not null) == true;

    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "(untitled)" : Title!;

    [JsonIgnore]
    public string UploaderText => Uploader ?? Channel ?? "";

    [JsonIgnore]
    public string? BestThumbnailUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Thumbnail)) return Thumbnail;
            var best = Thumbnails?.Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Url))
                                 .OrderByDescending(t => (long)(t.Width ?? 0) * (t.Height ?? 0))
                                 .FirstOrDefault();
            return best?.Url;
        }
    }

    [JsonIgnore]
    public string DurationText
    {
        get
        {
            return Formatting.FormatDuration(Duration, IsLive == true ? "LIVE" : "");
        }
    }

    /// <summary>Distinct sorted video heights available (e.g. 2160, 1080, 720), descending.</summary>
    [JsonIgnore]
    public IReadOnlyList<int> AvailableHeights =>
        (Formats ?? new List<FormatInfo>())
        .Where(f => f is not null && f.Height is > 0 && f.Vcodec != "none")
        .Select(f => f.Height!.Value)
        .Distinct()
        .OrderByDescending(h => h)
        .ToList();

    /// <summary>All subtitle language codes (manual subs first, then auto captions).</summary>
    [JsonIgnore]
    public IReadOnlyList<string> SubtitleLanguages
    {
        get
        {
            var langs = new List<string>();
            if (Subtitles is not null) langs.AddRange(Subtitles.Keys);
            return langs.Distinct().OrderBy(l => l).ToList();
        }
    }

    [JsonIgnore]
    public IReadOnlyList<string> AutoCaptionLanguages =>
        AutomaticCaptions is null ? Array.Empty<string>() : AutomaticCaptions.Keys.OrderBy(l => l).ToList();
}

public sealed class ThumbnailInfo
{
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("width")] public int? Width { get; set; }
    [JsonPropertyName("height")] public int? Height { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
}

public sealed class FormatInfo
{
    [JsonPropertyName("format_id")] public string? FormatId { get; set; }
    [JsonPropertyName("format_note")] public string? FormatNote { get; set; }
    [JsonPropertyName("ext")] public string? Ext { get; set; }
    [JsonPropertyName("vcodec")] public string? Vcodec { get; set; }
    [JsonPropertyName("acodec")] public string? Acodec { get; set; }
    [JsonPropertyName("width")] public int? Width { get; set; }
    [JsonPropertyName("height")] public int? Height { get; set; }
    [JsonPropertyName("fps")] public double? Fps { get; set; }
    [JsonPropertyName("filesize")] public long? Filesize { get; set; }
    [JsonPropertyName("filesize_approx")] public long? FilesizeApprox { get; set; }
    [JsonPropertyName("tbr")] public double? Tbr { get; set; }
    [JsonPropertyName("abr")] public double? Abr { get; set; }
    [JsonPropertyName("protocol")] public string? Protocol { get; set; }
    [JsonPropertyName("resolution")] public string? Resolution { get; set; }

    [JsonIgnore] public bool IsAudioOnly => (Vcodec is null or "none") && Acodec is not (null or "none");

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            var res = Height is > 0 ? $"{Height}p" : (IsAudioOnly ? "audio" : (Resolution ?? "?"));
            var fps = Fps is > 30 ? $"{Fps:0}fps " : "";
            var size = Formatting.FormatBytes(Filesize ?? FilesizeApprox);
            var sizeText = size.Length > 0 ? $" ~{size}" : "";
            var note = string.IsNullOrWhiteSpace(FormatNote) ? "" : $" ({FormatNote})";
            return $"{FormatId}  {res} {fps}{Ext}{sizeText}{note}";
        }
    }
}

public sealed class SubtitleFormat
{
    [JsonPropertyName("ext")] public string? Ext { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public sealed class PlaylistEntry
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("webpage_url")] public string? WebpageUrl { get; set; }
    [JsonPropertyName("duration")] public double? Duration { get; set; }
    [JsonPropertyName("thumbnail")] public string? Thumbnail { get; set; }
    [JsonPropertyName("formats")] public List<FormatInfo>? Formats { get; set; }

    [JsonIgnore] public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? (Id ?? "(untitled)") : Title!;

    [JsonIgnore]
    public string DurationText => Formatting.FormatDuration(Duration);
}

/// <summary>Small shared formatting helpers used across models/view-models.</summary>
public static class Formatting
{
    public static string FormatBytes(long? bytes)
    {
        if (bytes is null or <= 0) return "";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes.Value;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    public static string FormatSpeed(double? bytesPerSec)
    {
        if (bytesPerSec is null or <= 0 || !double.IsFinite(bytesPerSec.Value) || bytesPerSec > long.MaxValue)
            return "";
        return FormatBytes((long)bytesPerSec.Value) + "/s";
    }

    public static string FormatEta(double? seconds)
    {
        if (seconds is null or < 0 || !double.IsFinite(seconds.Value) || seconds > TimeSpan.MaxValue.TotalSeconds)
            return "";
        var ts = TimeSpan.FromSeconds(seconds.Value);
        return FormatTime(ts);
    }

    public static string FormatDuration(double? seconds, string fallback = "")
    {
        if (seconds is null or <= 0 || !double.IsFinite(seconds.Value) || seconds > TimeSpan.MaxValue.TotalSeconds)
            return fallback;
        var ts = TimeSpan.FromSeconds(seconds.Value);
        return FormatTime(ts);
    }

    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1
        ? FormattableString.Invariant($"{value.Ticks / TimeSpan.TicksPerHour}:{value.Minutes:00}:{value.Seconds:00}")
        : value.ToString(@"m\:ss");

    public static string FormatCount(long? count)
    {
        if (count is null) return "";
        if (count >= 1_000_000) return $"{count.Value / 1_000_000.0:0.#}M";
        if (count >= 1_000) return $"{count.Value / 1_000.0:0.#}K";
        return count.Value.ToString();
    }
}
