using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Providers;

public sealed record MediaProviderInfo(string Id, string Name, int Priority);

/// <summary>One independently implemented site or generic media extractor.</summary>
public interface IMediaProvider
{
    MediaProviderInfo Info { get; }
    NativeAuthenticationCapabilities AuthenticationCapabilities => NativeAuthenticationCapabilities.None;
    bool SupportsGeoVerificationProxy => false;
    bool CanHandle(Uri url);
    Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => null;
    Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct);
}
