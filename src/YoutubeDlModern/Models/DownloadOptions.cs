namespace YoutubeDlModern.Models;

/// <summary>Per-download options chosen in the "add download" card.</summary>
public sealed class DownloadOptions
{
    [System.Text.Json.Serialization.JsonIgnore]
    internal YoutubeDl.Engine.NativePlaylistSnapshot? NativePlaylistSnapshot { get; set; }
    internal DownloadOptions CopyForQueue() => (DownloadOptions)MemberwiseClone();
    /// <summary>video-audio | video-only | audio-only</summary>
    public string DownloadMode { get; set; } = "video-audio";
    /// <summary>best | 2160 | 1440 | 1080 | 720 | 480 | 360 | worst | custom</summary>
    public string QualityPreset { get; set; } = "best";
    public string CustomFormat { get; set; } = "";
    public string VideoCodec { get; set; } = "any";
    public int MaxFps { get; set; }
    public string Container { get; set; } = "auto";
    public string AudioFormat { get; set; } = "best";
    public string AudioQuality { get; set; } = "0";
    public bool WriteSubs { get; set; }
    public bool WriteAutoSubs { get; set; }
    public bool EmbedSubs { get; set; }
    public string SubLangs { get; set; } = "";
    public bool WriteThumbnail { get; set; }
    public bool EmbedThumbnail { get; set; }
    public bool AddMetadata { get; set; } = true;
    public bool EmbedChapters { get; set; } = true;
    public string OutputDirectory { get; set; } = "";
    public string OutputTemplate { get; set; } = "";
    public bool DownloadPlaylist { get; set; } = true;
    public string PlaylistItems { get; set; } = "";
    public int PlaylistStart { get; set; }
    public int PlaylistEnd { get; set; }
    public string CookiesFile { get; set; } = "";
    public string CookiesFromBrowser { get; set; } = "none";
    public string RateLimit { get; set; } = "";
    public int Retries { get; set; } = 10;
    public int ConcurrentFragments { get; set; } = 1;
    public string ExtraArgs { get; set; } = "";
}

/// <summary>Options that affect metadata analysis before an item is queued.</summary>
public sealed class AnalysisOptions
{
    /// <summary>quick | full | playlist</summary>
    public string Mode { get; set; } = "quick";
    public string CookiesFile { get; set; } = "";
    public string CookiesFromBrowser { get; set; } = "none";
}

public sealed class HistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long FileSize { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.Now;
    public string Site { get; set; } = "";
}
