using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Providers.YouTube;

/// <summary>Obtains a fresh, identity-checked playback session without owning the download.</summary>
public interface IYouTubePlaybackSource
{
    Task<PageMetadata> CaptureAsync(string videoId, NativeDownloadRequest request, CancellationToken ct);
    Task<PageMetadata> CaptureAsync(string videoId, NativeDownloadRequest request, YouTubePlaybackCookies cookies, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.CookieFile) || !string.IsNullOrWhiteSpace(request.BrowserCookies))
            throw new NotSupportedException("This playback source does not support selected account cookies.");
        return CaptureAsync(videoId, request, ct);
    }
}
