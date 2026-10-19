using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Providers;

namespace YoutubeDl.Engine;

/// <summary>A C# media engine with no external extraction process.</summary>
public interface IMediaEngine
{
    IReadOnlyList<MediaProviderInfo> Providers { get; }
    Task<NativeMediaInfo> InspectAsync(NativeDownloadRequest request, CancellationToken ct);
    Task<NativeDownloadResult> DownloadAsync(NativeDownloadRequest request,
        IProgress<DownloadProgress>? progress, Action<string>? log, CancellationToken ct, Action<string>? onFileCompleted = null);
}
