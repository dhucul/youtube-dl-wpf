using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Providers;

public sealed class DirectMediaProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("direct", "Direct media files and manifests", 100);
    public bool CanHandle(Uri url) => !Vocaroo.VocarooAddress.IsHost(url.Host) && !Clyp.ClypAddress.IsHost(url.Host) && PageMediaExtractor.Classify(url) != MediaKind.Unknown;
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) =>
        Download.DownloadArchiveIdentity.FromMetadata(new PageMetadata { Extractor = Info.Id }, request.Url);

    public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new PageMetadata
        {
            Extractor = Info.Id,
            Title = Path.GetFileNameWithoutExtension(request.Url.AbsolutePath),
            Media = [new MediaCandidate
            {
                Url = request.Url,
                Extension = PageMediaExtractor.ExtensionFrom(request.Url),
                Kind = PageMediaExtractor.Classify(request.Url),
                Source = Info.Id
            }]
        });
    }
}
