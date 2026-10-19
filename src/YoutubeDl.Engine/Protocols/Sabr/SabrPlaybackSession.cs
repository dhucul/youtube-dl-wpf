namespace YoutubeDl.Engine.Protocols.Sabr;

public sealed record SabrPlaybackSession(string VideoId, Uri Endpoint, uint ClientName, string ClientVersion, byte[] Configuration, string? UserAgent = null)
{
    private static readonly IReadOnlyDictionary<uint, byte[]> EmptyContexts = new System.Collections.ObjectModel.ReadOnlyDictionary<uint, byte[]>(new Dictionary<uint, byte[]>());
    [System.Text.Json.Serialization.JsonIgnore]
    internal Func<SabrPlaybackSession,CancellationToken,Task<SabrPlaybackSession>>? Renewal { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal bool SupportsWebM { get; init; } = true;
    public override string ToString()=>"SABR playback session [redacted]";
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[] ProofToken { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[] ClientInfo { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[] ClientAbrState { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[] InitialPlaybackCookie { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyDictionary<uint, byte[]> InitialContexts { get; init; } = EmptyContexts;
}
public sealed record SabrAudioTrack(bool DrcEnabled, string? TrackId = null, bool VoiceBoost = false, int? Channels = null);
public sealed record SabrStreamDescriptor(SabrPlaybackSession Session, SabrFormatId Format)
{
    public SabrAudioTrack? AudioTrack { get; init; }
}
