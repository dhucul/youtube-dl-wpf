using YoutubeDlModern.Models;

namespace YoutubeDlModern.Services;

public interface IYtDlpEngine
{
    bool IsInstalled { get; }
    bool IsBuiltIn => false;
    string DisplayName => "yt-dlp";
    string EnginePath => "";
    string EngineDir => AppContext.BaseDirectory;
    Task<string?> GetVersionAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Task.FromResult<string?>(null); }
    Task ReinstallAsync(Action<string> log, CancellationToken ct) => EnsureInstalledAsync(null, ct);
    Task<VideoInfo> GetVideoInfoAsync(string url, CancellationToken ct, AnalysisOptions? options = null);
    Task<IReadOnlyList<FormatInfo>> ListFormatsAsync(string url, CancellationToken ct,
        AnalysisOptions? options = null, bool isPlaylist = false);
    Task EnsureInstalledAsync(IProgress<(double Percent, string Message)>? progress, CancellationToken ct);
    Task<DownloadRunResult> DownloadAsync(string url, DownloadOptions options,
        AppSettings settingsSnapshot, string? ffmpegLocation, IProgress<ProgressUpdate> progress,
        Action<string> log, CancellationToken ct, Action<string>? onFileCompleted = null);
}

public interface IFfmpegLocator
{
    Task<string?> LocateAsync(AppSettings settingsSnapshot, CancellationToken ct = default);
}
