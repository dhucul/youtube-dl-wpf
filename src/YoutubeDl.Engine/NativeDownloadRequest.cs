namespace YoutubeDl.Engine;

/// <summary>
/// Typed native download request. Unsupported options require explicit capability validation.
/// </summary>
public sealed record NativeDownloadRequest
{
    public required Uri Url { get; init; }
    public required string OutputDirectory { get; init; }
    public string FileName { get; init; } = "";
    public string OutputTemplate { get; init; } = "";
    public bool RestrictFilenames { get; init; }
    public bool NoOverwrites { get; init; }
    public bool NoPostOverwrites { get; init; }
    public string RecodeVideo { get; init; } = "";
    public bool NoContinue { get; init; }
    public bool NoPart { get; init; }
    public bool NoMtime { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? CacheDirectory { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool NoCache { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool RemoveCache { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Quiet { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool NoProgress { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool NoWarnings { get; init; }
    public string DownloadArchiveFile { get; init; } = "";
    public string? PlaylistTitle { get; init; }
    public string? PlaylistId { get; init; }
    public int? PlaylistIndex { get; init; }
    public int? PlaylistAutoNumber { get; init; }
    public int? PlaylistCount { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal IReadOnlyList<NativePlaylistContext> PlaylistParents { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    internal IReadOnlyList<string> PlaylistAncestors { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    internal Download.PlaylistTraversalState? PlaylistTraversal { get; init; }
    public bool AudioOnly { get; init; }
    public bool VideoOnly { get; init; }
    public bool KeepOriginal { get; init; }
    public bool SkipDownload { get; init; }
    /// <summary>Returns format inventory without establishing provider playback sessions.</summary>
    public bool MetadataInventoryOnly { get; init; }
    public bool Simulate { get; init; }
    public bool IgnoreNoFormatsError { get; init; }
    public bool ForceWriteArchive { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal Download.RetainedMediaCollector? RetainedMedia { get; init; }
    internal string? AdaptiveRecoveryScope { get; init; }
    public string? FormatId { get; init; }
    public string FormatSelector { get; init; } = "";
    public string VideoCodec { get; init; } = "any";
    public int MaximumFrameRate { get; init; }
    public bool DownloadPlaylist { get; init; } = true;
    public bool ExpandPlaylist { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public NativePlaylistSnapshot? PlaylistSnapshot { get; init; }
    public int MaximumPlaylistEntries { get; init; } = 1000;
    public string PlaylistItems { get; init; } = "";
    public int PlaylistStart { get; init; }
    public int PlaylistEnd { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal bool PlaylistEndExplicit { get; init; }
    public bool PlaylistReverse { get; init; }
    public bool PlaylistRandom { get; init; }
    public bool IgnoreErrors { get; init; } = true;
    public string MatchTitle { get; init; } = "";
    public string RejectTitle { get; init; } = "";
    public int MaxDownloads { get; init; }
    public string Date { get; init; } = "";
    public string DateBefore { get; init; } = "";
    public string DateAfter { get; init; } = "";
    public long? MinimumViews { get; init; }
    public long? MaximumViews { get; init; }
    public int? MaximumAge { get; init; }
    public string MatchFilter { get; init; } = "";
    public string MinimumFileSize { get; init; } = "";
    public string MaximumFileSize { get; init; } = "";
    public IReadOnlyList<string> MatchFilters { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    internal Download.DownloadSelectionState? SelectionState { get; init; }
    public string AudioFormat { get; init; } = "mp3";
    public string AudioQuality { get; init; } = "5";
    public string Container { get; init; } = "";
    /// <summary>Ordered container choices used only when joining separate video and audio.</summary>
    public string MergeOutputFormat { get; init; } = "";
    public bool PreferFreeFormats { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? AllowDynamicDash { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public double? RequestSleepSeconds { get; init; }
    public Download.QualityPreference Quality { get; init; } = Download.QualityPreference.Best;
    public int? MaximumHeight { get; init; }
    /// <summary>Optional live DASH policy. Null retains literal and stable-profile selection behavior.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public LiveDashSelectionPolicy? LiveDashSelection { get; init; }
    public string? CookieFile { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public NativeAuthenticationOptions Authentication { get; init; } = new();
    public string? Proxy { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public NativeGeoVerificationOptions? GeoVerification { get; init; }
    public string? SourceAddress { get; init; }
    public int IpVersion { get; init; }
    public bool NoCheckCertificate { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyDictionary<string, string> HttpHeaders { get; init; } = new Dictionary<string, string>();
    public string? UserAgent { get; init; }
    public string? Referer { get; init; }
    public string? RateLimit { get; init; }
    public string BufferSize { get; init; } = "";
    public bool NoResizeBuffer { get; init; }
    public string HttpChunkSize { get; init; } = "";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ExternalDownloader { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ExternalDownloaderArguments { get; init; }
    public int Retries { get; init; } = 3;
    public int? FragmentRetries { get; init; }
    public int ConcurrentFragments { get; init; } = 1;
    public int TimeoutSeconds { get; init; } = 30;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public double? SubtitleSleepSeconds { get; init; }
    public double? SleepIntervalSeconds { get; init; }
    public double? MaximumSleepIntervalSeconds { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal Action<string>? DownloadDelayLog { get; init; }
    public string? FfmpegPath { get; init; }
    public bool WriteSubs { get; init; }
    public bool WriteAutoSubs { get; init; }
    public bool EmbedSubs { get; init; }
    public string SubLanguages { get; init; } = "";
    public string SubFormat { get; init; } = "";
    public string ConvertSubs { get; init; } = "";
    public bool WriteThumbnail { get; init; }
    public bool WriteAllThumbnails { get; init; }
    public bool EmbedThumbnail { get; init; }
    public bool WriteDescription { get; init; }
    public bool WriteInfoJson { get; init; }
    public bool EmbedChapters { get; init; }
    public bool AddMetadata { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ExecCommands { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SponsorBlockMark { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SponsorBlockRemove { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal SponsorBlockPlan? SponsorEdits { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Fixup { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? PostprocessorArguments { get; init; }
    public IReadOnlyList<string> MetadataParseRules { get; init; } = [];
    public string? BrowserCookies { get; init; }
    public string? CaptureText { get; init; }
    /// <summary>Maximum recorded HLS media time. An ended event may finish sooner.</summary>
    public double? LiveDurationSeconds { get; init; }
    public bool LiveFromStart { get; init; }
}

public sealed class NativeMediaInfo
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? ThumbnailUrl { get; init; }
    public IReadOnlyList<Discovery.ThumbnailCandidate> Thumbnails { get; init; } = [];
    public string? Uploader { get; init; }
    public string Extractor { get; init; } = "generic";
    public double? DurationSeconds { get; init; }
    public DateOnly? UploadDate { get; init; }
    public DateTimeOffset? UploadTimestamp { get; init; }
    public long? ViewCount { get; init; }
    public int? AgeLimit { get; init; }
    public bool IsLive { get; init; }
    public Uri? PlayerScriptUrl { get; init; }
    public string? PlaylistId { get; init; }
    public bool IsPlaylist { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public NativePlaylistSnapshot? PlaylistSnapshot { get; init; }
    public IReadOnlyList<NativePlaylistEntry> Entries { get; init; } = [];
    public IReadOnlyList<Discovery.SubtitleTrack> Subtitles { get; init; } = [];
    public IReadOnlyList<Discovery.ChapterMarker> Chapters { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<NativeFormat> Formats { get; init; } = [];
}

public sealed class NativeFormat
{
    public required string Id { get; init; }
    public Uri? Url { get; init; }
    public string? MimeType { get; init; }
    public int? Width { get; init; }
    public double? Bandwidth { get; init; }
    public string? Extension { get; init; }
    public int? Height { get; init; }
    public string? Note { get; init; }
    public bool AudioOnly { get; init; }
    public bool? HasAudio { get; init; }
    public bool? HasVideo { get; init; }
    public string? AudioCodec { get; init; }
    public string? VideoCodec { get; init; }
    public double? FramesPerSecond { get; init; }
    public long? FileSize { get; init; }
    public string? AudioLanguage { get; init; }
    public bool IsDefaultAudio { get; init; }
    public FormatAvailability Availability { get; init; } = FormatAvailability.Ready;
    public string? UnavailableReason { get; init; }
    public string Protocol { get; init; } = "https";
}

public enum FormatAvailability
{
    Ready,
    PlayerChallengeRequired,
    AdaptiveTransportRequired,
    DrmProtected,
    MissingUrl
}

public sealed class NativeDownloadResult
{
    internal Download.DownloadWorkspace.Completion? PublicationCompletion {get;init;}
    [System.Text.Json.Serialization.JsonIgnore]
    internal bool CompletionsForwarded { get; init; }
    public bool Succeeded { get; init; }
    public bool Skipped { get; init; }
    public string? OutputPath { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<string> OutputPaths { get; init; } = [];
    public IReadOnlyList<string>? NewOutputPaths { get; init; }
    public IReadOnlyList<string> SidecarPaths { get; init; } = [];
    public IReadOnlyList<string> NewSidecarPaths { get; init; } = [];
    public bool Simulated { get; init; }
    public bool Filtered { get; init; }
    public bool DownloadLimitReached { get; init; }
    public IReadOnlyList<NativeDownloadFailure> Failures { get; init; } = [];
}

public sealed record NativePlaylistEntry(int Position, string? Id, string Title, Uri? Url, double? DurationSeconds,
    string? ThumbnailUrl, bool Available, Discovery.PageMetadata? ResolvedMedia = null,
    [property: System.Text.Json.Serialization.JsonIgnore] Download.DownloadArchiveIdentity? ExpectedArchiveIdentity = null,
    [property: System.Text.Json.Serialization.JsonIgnore] string? CollectionMemberId=null);
public sealed record NativePlaylistContext(string? Id, string Title, int Index, int AutoNumber, int Count);
public sealed record NativeDownloadFailure(int Position, string Title, string Message, IReadOnlyList<NativePlaylistContext>? Parents = null);
