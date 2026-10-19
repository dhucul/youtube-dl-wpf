namespace YoutubeDl.Engine.Discovery;

public enum MediaKind
{
    Progressive,
    Hls,
    Dash,
    Sabr,
    Unknown,
    HttpSequence
}

public sealed record MediaFilePart(Uri Url, string Extension, double DurationSeconds, long? FileSize = null);

/// <summary>A playable URL found on a page, before quality selection.</summary>
public sealed class MediaCandidate
{
    public required Uri Url { get; init; }
    internal DateTimeOffset? ResourceExpires { get; init; }
    public IReadOnlyList<MediaFilePart> Parts { get; init; } = [];
    private string? _id;
    public string? Id {get=>_id;init=>_id=value;}
    internal MediaCandidate WithIdentity(string id){var copy=(MediaCandidate)MemberwiseClone();copy._id=id;return copy;}
    /// <summary>Matching nonempty IDs identify tracks whose source timestamps share an origin.</summary>
    public string? TimelineId { get; init; }
    // Providers opt into a fixed local demuxer; downloaded text cannot become an executable playlist.
    internal string? VerificationDemuxer { get; init; }
    internal double? VerificationDurationSeconds { get; init; }
    // Dynamic audio services can add material to the published program length.
    // Keep full decoding/transfer checks and require at least that program length.
    internal double? VerificationMinimumDurationSeconds { get; init; }
    // Some providers expose whole-second program lengths that require a tighter floor.
    internal double? VerificationMinimumDurationToleranceSeconds { get; init; }
    // Exact provider-declared byte count, distinct from informational/estimated format sizes.
    internal long? VerificationFileSize { get; init; }
    // Optional publisher-supplied digest of the complete original object, checked before processing.
    internal string? VerificationSha1 { get; init; }
    internal bool NormalizeMp3ClipBoundary { get; init; }
    internal bool NormalizeHlsTransportContinuity { get; init; }
    internal bool NormalizeHlsPesCounterResets { get; init; }
    // A provider-declared video loop can repeat through one complete supplied
    // audio track. Single-track/video-only selections retain the source cycle.
    internal bool LoopVideoToAudio { get; init; }
    public bool? HasAudio { get; init; }
    public bool? HasVideo { get; init; }
    public string? AudioLanguage { get; init; }
    public bool IsDefaultAudio { get; init; }
    public bool IsFallback { get; init; }
    /// <summary>The provider identifies a complete resource that needs no additional audio track.</summary>
    public bool IsCompleteMedia { get; init; }
    public string? Label { get; init; }
    // Ordinal preference supplied by a provider when its named qualities do not
    // declare pixel dimensions or bandwidth. It must not invent either fact.
    internal int ProviderQualityOrder { get; init; }
    public int? Height { get; init; }
    public int? Width { get; init; }
    public double? Bandwidth { get; init; }
    public double? FramesPerSecond { get; init; }
    public long? FileSize { get; init; }
    public string? VideoCodec { get; init; }
    public string? AudioCodec { get; init; }
    public string? Extension { get; init; }
    public string? MimeType { get; init; }
    public MediaKind Kind { get; init; }
    public string Source { get; init; } = "page";
    private Protocols.Sabr.SabrStreamDescriptor? _sabr;
    public Protocols.Sabr.SabrStreamDescriptor? Sabr { get=>_sabr; init=>_sabr=value; }
    internal MediaCandidate WithSabrSession(Protocols.Sabr.SabrPlaybackSession session)
    {var copy=(MediaCandidate)MemberwiseClone();copy._sabr=Sabr! with {Session=session};return copy;}

    public bool IsAudioOnly =>
        HasVideo != true && (HasVideo == false && HasAudio == true ||
        MimeType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true ||
        Extension is "m4a" or "mp3" or "aac" or "opus" or "ogg" or "flac" or "wav");

    public long Rank =>
        (long)(Height ?? 0) * 1_000_000 +
        (long)Math.Min(Bandwidth ?? 0, 999_999) + Math.Clamp(ProviderQualityOrder, 0, 99) +
        Kind switch
        {
            MediaKind.Progressive or MediaKind.HttpSequence => 500,
            MediaKind.Hls => 200,
            MediaKind.Dash => 100,
            _ => 0
        };
}
