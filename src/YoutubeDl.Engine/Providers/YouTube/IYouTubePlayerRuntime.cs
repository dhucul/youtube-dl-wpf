namespace YoutubeDl.Engine.Providers.YouTube;

/// <summary>Transforms a player-bound GoogleVideo URL using the public player script; it does not grant playback access.</summary>
public interface IYouTubePlayerRuntime
{
    IYouTubePlayerSession CreateSession(string playerScript, CancellationToken ct);
}

public interface IYouTubePlayerSession : IDisposable
{
    Uri Transform(Uri url, string signatureParameter, string signature, CancellationToken ct);
}
