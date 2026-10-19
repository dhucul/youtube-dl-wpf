using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Providers.Newgrounds;

/// <summary>Optional host-owned browser discovery; media transfer and publication remain in the native core.</summary>
public interface INewgroundsPageSource
{
    Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, CancellationToken ct);
}
