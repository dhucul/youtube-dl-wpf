namespace YoutubeDl.Engine.Discovery;

public sealed class SubtitleTrack
{
    public required Uri Url { get; init; }
    public string Language { get; init; } = "";
    public string Extension { get; init; } = "vtt";
    public bool Automatic { get; init; }
    internal string? ResourceIdentity { get; init; }
    internal DateTimeOffset? ResourceExpires { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Content { get; init; }
}

public sealed class ChapterMarker
{
    public required string Title { get; init; }
    public double StartSeconds { get; init; }
    public double EndSeconds { get; init; }
}
