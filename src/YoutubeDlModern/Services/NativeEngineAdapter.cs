using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDlModern.Models;

namespace YoutubeDlModern.Services;

/// <summary>
/// Adapts the in-process engine to the existing queue contract during migration.
/// </summary>
public sealed partial class NativeEngineAdapter : IYtDlpEngine
{
    private readonly NativeEngine _engine;
    private readonly SettingsService? _settings;
    // The queue owns UI dispatch. Forward each report before the producer continues,
    // preserving order and preventing reports from outliving the engine operation.
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        private readonly object _sync = new();
        public void Report(T value)
        {
            lock (_sync) report(value);
        }
    }
    public NativeEngineAdapter(NativeEngine? engine = null, SettingsService? settings = null)
    {
        _engine = engine ?? new(playerRuntime: new YoutubeDl.Engine.PlayerRuntime.ManagedYouTubePlayerRuntime(),
            youTubePlaybackSource: new YoutubeDl.Engine.BrowserRuntime.YouTubeBrowserPlaybackSource(), newgroundsPageSource: new YoutubeDl.Engine.BrowserRuntime.NewgroundsBrowserPageSource());
        _settings = settings;
    }

    public bool IsInstalled => true;
    public bool IsBuiltIn => true;
    public string DisplayName => "Built-in downloader";
    public string EnginePath => Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "YoutubeDlModern.exe");
    public string EngineDir => System.IO.Path.GetDirectoryName(EnginePath) ?? AppContext.BaseDirectory;
    public Task<string?> GetVersionAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Task.FromResult<string?>(NativeEngine.Version); }
    public Task ReinstallAsync(Action<string> log, CancellationToken ct) => EnsureInstalledAsync(null, ct);

    public Task EnsureInstalledAsync(IProgress<(double Percent, string Message)>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        progress?.Report((100, "The downloader is built in."));
        return Task.CompletedTask;
    }

    public async Task<VideoInfo> GetVideoInfoAsync(string url, CancellationToken ct, AnalysisOptions? options = null)
    {
        var info = await _engine.InspectAsync(CreateAnalysisRequest(url, options), ct).ConfigureAwait(false);
        RequireAvailableMedia(info);
        return ToVideoInfo(url, info);
    }

    private static void RequireAvailableMedia(NativeMediaInfo info)
    {
        if (!info.Formats.Any(format => PreviewFormat(info, format)) && !info.IsPlaylist)
            throw new NativeEngineUnsupportedException(info.Formats.Select(format => format.UnavailableReason).FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason)) ??
                info.Warnings.FirstOrDefault() ?? "No downloadable media was found on this page.");
    }

    private static bool PreviewFormat(NativeMediaInfo info, NativeFormat format) => format.Availability == FormatAvailability.Ready ||
        info.Extractor == "youtube" && format.Availability == FormatAvailability.AdaptiveTransportRequired;

    public async Task<IReadOnlyList<FormatInfo>> ListFormatsAsync(
        string url, CancellationToken ct, AnalysisOptions? options = null, bool isPlaylist = false)
    {
        var request = CreateAnalysisRequest(url, options) with { DownloadPlaylist = isPlaylist, ExpandPlaylist = isPlaylist };
        var visited = new HashSet<Uri>();
        var resolvedPages = new HashSet<YoutubeDl.Engine.Discovery.PageMetadata>(ReferenceEqualityComparer.Instance);
        NativeMediaInfo? resolvedInfo = null;
        for (var depth = 0; depth < 16; depth++)
        {
            ct.ThrowIfCancellationRequested();
            NativeMediaInfo info;
            if (resolvedInfo is not null) { info = resolvedInfo; resolvedInfo = null; }
            else
            {
                if (!visited.Add(request.Url)) throw new NativeEngineUnsupportedException("The first playlist entry forms a collection cycle.");
                info = await _engine.InspectAsync(request, ct).ConfigureAwait(false);
            }
            if (!info.IsPlaylist)
            {
                RequireAvailableMedia(info);
                return ToVideoInfo(request.Url.AbsoluteUri, info).Formats ?? [];
            }
            var entry = info.Entries.OrderBy(item => item.Position).FirstOrDefault();
            if (entry is null || !entry.Available)
                throw new NativeEngineUnsupportedException("The first playlist entry is unavailable or has no playable address.");
            if (entry.ResolvedMedia is { } metadata)
            {
                if (!resolvedPages.Add(metadata)) throw new NativeEngineUnsupportedException("The first playlist entry forms a resolved collection cycle.");
                if (metadata.DrmRefused) throw new NativeEngineUnsupportedException(metadata.RefusalReason ?? "The first playlist entry does not offer available clear media.");
                request = request with { Url = entry.Url ?? metadata.SourcePageUrl ?? request.Url, DownloadPlaylist = false, ExpandPlaylist = false };
                resolvedInfo = NativeEngine.Describe(request.Url, metadata);
            }
            else
            {
                if (entry.Url is null) throw new NativeEngineUnsupportedException("The first playlist entry has no playable address.");
                request = request with { Url = entry.Url, DownloadPlaylist = false, ExpandPlaylist = false };
            }
        }
        throw new NativeEngineUnsupportedException("The first playlist entry exceeds the collection traversal limit.");
    }

    public async Task<DownloadRunResult> DownloadAsync(
        string url, DownloadOptions options, AppSettings settingsSnapshot, string? ffmpegLocation,
        IProgress<ProgressUpdate> progress, Action<string> log, CancellationToken ct,
        Action<string>? onFileCompleted = null)
    {
        NativeDownloadRequest request;
        try
        {
            ValidateDownloadSettings(settingsSnapshot,url);
            ValidateJobOptions(options);
            request = NativeExtraArguments.Apply(CreateRequest(url, options, settingsSnapshot, ffmpegLocation),
                ProcessRunner.SplitArguments(settingsSnapshot.General.ExtraArgs).Concat(ProcessRunner.SplitArguments(options.ExtraArgs)));
            _engine.ValidateAuthenticationOptions(request);
        }
        catch (NotSupportedException ex) { throw new NativeEngineUnsupportedException(ex.Message); }
        if (request.DownloadPlaylist && options.NativePlaylistSnapshot is { } selection) request = request with { PlaylistSnapshot = selection };
        var result = await _engine.DownloadAsync(request, new InlineProgress<DownloadProgress>(update =>
        {
            double? eta = update.TotalBytes is > 0 && update.BytesPerSecond is > 0
                ? (update.TotalBytes.Value - update.DownloadedBytes) / update.BytesPerSecond.Value
                : null;
            progress.Report(new ProgressUpdate
            {
                DownloadedBytes = update.DownloadedBytes,
                TotalBytes = update.TotalBytes,
                SpeedBytesPerSec = update.BytesPerSecond,
                EtaSeconds = eta,
                Status = "downloading"
            });
        }), log, ct, onFileCompleted).ConfigureAwait(false);

        if (!result.Succeeded && result.OutputPaths.Count == 0 && result.SidecarPaths.Count == 0)
            throw new NativeEngineUnsupportedException(result.Error ?? "The native engine could not download this page.");

        return new DownloadRunResult { ExitCode = result.Succeeded ? 0 : 1, ErrorSummary = result.Error ?? "", FinalFilePaths = result.OutputPaths.ToList() };
    }

    internal static bool RequiresFallback(DownloadOptions options, AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(options.ExtraArgs) || !string.IsNullOrWhiteSpace(settings.General.ExtraArgs))
            return true;
        return false;
    }
}

public sealed class NativeEngineUnsupportedException(string message) : Exception(message);
