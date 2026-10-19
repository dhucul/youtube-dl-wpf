using System.IO;
using System.Net.Http;
using YoutubeDl.Engine;
using YoutubeDlModern.Models;

namespace YoutubeDlModern.Services;

/// <summary>
/// Tries the built-in engine first. Pages it cannot read, and downloads that
/// use options it does not implement, continue through the yt-dlp fallback.
/// </summary>
public sealed class CompositeEngine : IYtDlpEngine
{
    private readonly NativeEngineAdapter _native;
    private readonly YtDlpEngine _fallback;

    public CompositeEngine(NativeEngineAdapter native, YtDlpEngine fallback)
    {
        _native = native;
        _fallback = fallback;
    }

    public bool IsInstalled => true;

    public YtDlpEngine Fallback => _fallback;

    public Task EnsureInstalledAsync(IProgress<(double Percent, string Message)>? progress, CancellationToken ct) =>
        _fallback.EnsureInstalledAsync(progress, ct);

    public async Task<VideoInfo> GetVideoInfoAsync(string url, CancellationToken ct, AnalysisOptions? options = null)
    {
        if (TryNative(url, out var reason))
        {
            try { return await _native.GetVideoInfoAsync(url, ct, options).ConfigureAwait(false); }
            catch (NativeEngineUnsupportedException) { }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException)
            {
                reason = ex.Message;
            }
        }

        EnsureFallback(reason);
        return await _fallback.GetVideoInfoAsync(url, ct, options).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FormatInfo>> ListFormatsAsync(
        string url, CancellationToken ct, AnalysisOptions? options = null, bool isPlaylist = false)
    {
        if (TryNative(url, out _))
        {
            try { return await _native.ListFormatsAsync(url, ct, options, isPlaylist).ConfigureAwait(false); }
            catch (NativeEngineUnsupportedException) { }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException) { }
        }

        EnsureFallback(null);
        return await _fallback.ListFormatsAsync(url, ct, options, isPlaylist).ConfigureAwait(false);
    }

    public async Task<DownloadRunResult> DownloadAsync(
        string url, DownloadOptions options, AppSettings settingsSnapshot, string? ffmpegLocation,
        IProgress<ProgressUpdate> progress, Action<string> log, CancellationToken ct,
        Action<string>? onFileCompleted = null)
    {
        if (TryNative(url, out var reason) && !NativeEngineAdapter.RequiresFallback(options, settingsSnapshot))
        {
            try
            {
                return await _native.DownloadAsync(
                    url, options, settingsSnapshot, ffmpegLocation, progress, log, ct, onFileCompleted)
                    .ConfigureAwait(false);
            }
            catch (NativeEngineUnsupportedException ex)
            {
                log("Native engine could not handle this page (" + ex.Message + "). Using yt-dlp.");
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException)
            {
                log("Native engine failed (" + ex.Message + "). Using yt-dlp.");
            }
        }
        else if (reason is not null)
        {
            log("Using yt-dlp: " + reason);
        }

        EnsureFallback(null);
        return await _fallback.DownloadAsync(
            url, options, settingsSnapshot, ffmpegLocation, progress, log, ct, onFileCompleted)
            .ConfigureAwait(false);
    }

    private static bool TryNative(string url, out string? reason)
    {
        reason = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            reason = "the address is not an HTTP URL";
            return false;
        }

        if (!NativeEngine.CanHandle(uri))
        {
            reason = "this site needs the fallback engine";
            return false;
        }

        return true;
    }

    private void EnsureFallback(string? reason)
    {
        if (_fallback.IsInstalled) return;
        throw new InvalidOperationException(
            "The built-in engine could not read this page" +
            (string.IsNullOrWhiteSpace(reason) ? "" : " (" + reason + ")") +
            ". Install the yt-dlp fallback from About to continue.");
    }
}
