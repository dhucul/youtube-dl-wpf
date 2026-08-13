namespace YoutubeDlModern.Models;

public enum DownloadStatus
{
    Queued,
    Downloading,
    Processing,
    Paused,
    Completed,
    Failed,
    Canceled
}

/// <summary>
/// Progress update parsed from yt-dlp's machine-readable --progress-template output.
/// Null values mean the field was not available (yt-dlp prints "NA").
/// </summary>
public sealed class ProgressUpdate
{
    public long? DownloadedBytes { get; set; }
    public long? TotalBytes { get; set; }
    public double? SpeedBytesPerSec { get; set; }
    public double? EtaSeconds { get; set; }
    public string? Status { get; set; }
    public bool IsPostProcessing { get; set; }
    public string? FinalFilePath { get; set; }
}
