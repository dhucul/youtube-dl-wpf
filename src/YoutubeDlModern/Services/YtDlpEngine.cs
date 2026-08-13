using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDlModern.Models;

namespace YoutubeDlModern.Services;

public sealed class DownloadRunResult
{
    public int ExitCode { get; set; }
    public string ErrorSummary { get; set; } = "";
    public List<string> FinalFilePaths { get; set; } = [];
    public string? FinalFilePath => FinalFilePaths.LastOrDefault();
}

/// <summary>
/// Owns the yt-dlp engine binary: installation, self-update, version info,
/// metadata queries (-J) and download execution with machine-readable progress.
/// </summary>
public sealed class YtDlpEngine
{
    public const string PinnedEngineVersion = "2026.07.04";
    private const string EngineDownloadUrl =
        "https://github.com/yt-dlp/yt-dlp/releases/download/2026.07.04/yt-dlp.exe";
    private const string EngineSha256 = "52fe3c26dcf71fbdc85b528589020bb0b8e383155cfa81b64dd447bbe35e24b8";
    private const long MaximumEngineBytes = 32L * 1024 * 1024;
    private const int MaximumMetadataCharacters = 24 * 1024 * 1024;
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FormatLookupTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(15);
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _maintenanceGate = new(1, 1);
    private readonly object _operationSync = new();
    private readonly object _integritySync = new();
    private int _activeOperationCount;
    private TaskCompletionSource<bool>? _operationsIdle;
    private long _verifiedLength = -1;
    private long _verifiedWriteTicks = -1;
    private bool _verifiedIntegrity;

    private sealed class OperationLease(YtDlpEngine owner) : IDisposable
    {
        private YtDlpEngine? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ExitOperation();
    }

    public YtDlpEngine(SettingsService settings) => _settings = settings;

    public string EngineDir => Path.Combine(SettingsService.LocalDataDir, "engine");
    public string EnginePath => Path.Combine(EngineDir, "yt-dlp.exe");
    public bool EngineFileExists => File.Exists(EnginePath);
    public bool IsInstalled => IsPinnedEngineValid();

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        using var operation = await EnterOperationAsync(ct).ConfigureAwait(false);
        if (!IsInstalled) return null;
        try
        {
            var r = await ProcessRunner.RunAsync(
                EnginePath, new[] { "--version" }, ct: ct, timeout: TimeSpan.FromSeconds(15));
            return r.ExitCode == 0 ? r.StdOut.Trim() : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    /// <summary>Fetches video/playlist metadata (classic -J behavior).</summary>
    public async Task<VideoInfo> GetVideoInfoAsync(
        string url,
        CancellationToken ct,
        AnalysisOptions? options = null)
    {
        using var operation = await EnterOperationAsync(ct).ConfigureAwait(false);
        EnsureInstalledOrThrow();
        var args = ArgsBuilder.BuildInfoArgs(_settings.Data, url, options);
        var result = await ProcessRunner.RunAsync(
            EnginePath, args, ct: ct, timeout: MetadataTimeout,
            maximumCapturedCharacters: MaximumMetadataCharacters,
            maximumLineCharacters: MaximumMetadataCharacters);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(SummarizeError(RedactProcessText(result.StdErr, args)));
        try
        {
            return await BackgroundWork.RunAsync(
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    var info = JsonSerializer.Deserialize<VideoInfo>(result.StdOut)
                               ?? throw new InvalidOperationException("The engine returned empty metadata for this URL.");
                    NormalizeVideoInfo(info);
                    return info;
                },
                ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Could not parse the metadata returned by the engine: " + ex.Message);
        }
    }

    /// <summary>Structured format listing for a video or the first playlist entry.</summary>
    public async Task<IReadOnlyList<FormatInfo>> ListFormatsAsync(
        string url,
        CancellationToken ct,
        AnalysisOptions? options = null,
        bool isPlaylist = false)
    {
        using var operation = await EnterOperationAsync(ct).ConfigureAwait(false);
        EnsureInstalledOrThrow();
        var args = ArgsBuilder.BuildListFormatsArgs(_settings.Data, url, options, isPlaylist);
        var result = await ProcessRunner.RunAsync(
            EnginePath, args, ct: ct, timeout: FormatLookupTimeout,
            maximumCapturedCharacters: MaximumMetadataCharacters,
            maximumLineCharacters: MaximumMetadataCharacters);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(SummarizeError(RedactProcessText(result.StdErr, args)));
        try
        {
            return await BackgroundWork.RunAsync<IReadOnlyList<FormatInfo>>(
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    var info = JsonSerializer.Deserialize<VideoInfo>(result.StdOut)
                               ?? throw new InvalidOperationException("The engine returned empty format information for this URL.");
                    NormalizeVideoInfo(info);
                    var source = info.Formats ?? info.Entries?.FirstOrDefault()?.Formats;
                    if (source is null)
                        throw new InvalidOperationException("The engine returned no selectable formats for this URL.");

                    var formats = new List<FormatInfo>(source.Count);
                    foreach (var format in source)
                    {
                        token.ThrowIfCancellationRequested();
                        if (format is not null && !string.IsNullOrWhiteSpace(format.FormatId))
                            formats.Add(format);
                    }

                    if (formats.Count == 0)
                        throw new InvalidOperationException("The engine returned no selectable formats for this URL.");
                    return formats;
                },
                ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Could not parse the formats returned by the engine: " + ex.Message);
        }
    }

    /// <summary>
    /// Runs a download, reporting structured progress parsed from
    /// --progress-template output and human-readable log lines.
    /// </summary>
    public async Task<DownloadRunResult> DownloadAsync(
        string url,
        DownloadOptions options,
        AppSettings settingsSnapshot,
        string? ffmpegLocation,
        IProgress<ProgressUpdate> progress,
        Action<string> log,
        CancellationToken ct)
    {
        using var operation = await EnterOperationAsync(ct).ConfigureAwait(false);
        EnsureInstalledOrThrow();

        var args = ArgsBuilder.BuildDownloadArgs(settingsSnapshot, options, url, ffmpegLocation);
        var redact = CreateRedactor(args);
        log("yt-dlp [configured arguments redacted] " + QuoteForLog(RedactUrl(url)));

        string? lastError = null;
        var finalFiles = new List<string>();

        void HandleStdout(string line)
        {
            if (line.StartsWith(ArgsBuilder.ProgressPrefix, StringComparison.Ordinal))
            {
                progress.Report(ParseProgress(line));
            }
            else if (line.StartsWith(ArgsBuilder.PostProcessMarker, StringComparison.Ordinal))
            {
                progress.Report(new ProgressUpdate { IsPostProcessing = true, Status = "processing" });
                var suffix = line.Length > ArgsBuilder.PostProcessMarker.Length + 1
                    ? line[(ArgsBuilder.PostProcessMarker.Length + 1)..] : "";
                log(redact("[postprocessing] " + suffix));
            }
            else if (line.StartsWith(ArgsBuilder.FinalFilePrefix, StringComparison.Ordinal))
            {
                var finalFile = line[ArgsBuilder.FinalFilePrefix.Length..].Trim();
                if (!string.IsNullOrWhiteSpace(finalFile) &&
                    !finalFiles.Contains(finalFile, StringComparer.OrdinalIgnoreCase))
                    finalFiles.Add(finalFile);
                progress.Report(new ProgressUpdate { FinalFilePath = finalFile });
            }
            else
            {
                log(redact(line));
            }
        }

        void HandleStderr(string line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                lastError = redact(line.Trim());
            log(redact(line));
        }

        var result = await ProcessRunner.RunAsync(
            EnginePath, args,
            onStdoutLine: HandleStdout,
            onStderrLine: HandleStderr,
            captureOutput: false,
            ct: ct);

        return new DownloadRunResult
        {
            ExitCode = result.ExitCode,
            ErrorSummary = lastError ?? "",
            FinalFilePaths = finalFiles
        };
    }

    /// <summary>Downloads the application-pinned yt-dlp.exe and verifies its published digest.</summary>
    public async Task EnsureInstalledAsync(IProgress<(double Percent, string Message)>? progress, CancellationToken ct)
    {
        await _maintenanceGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await WaitForOperationsIdleAsync(ct).ConfigureAwait(false);
            await EnsureInstalledCoreAsync(progress, ct, force: false).ConfigureAwait(false);
        }
        finally
        {
            _maintenanceGate.Release();
        }
    }

    private async Task EnsureInstalledCoreAsync(
        IProgress<(double Percent, string Message)>? progress,
        CancellationToken ct,
        bool force)
    {
        if (!force && IsInstalled) return;
        Directory.CreateDirectory(EngineDir);

        var tmp = Path.Combine(EngineDir, $"yt-dlp-{Guid.NewGuid():N}.download");
        using var timeoutCts = new CancellationTokenSource(InstallTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var requestToken = linkedCts.Token;
        try
        {
            await SecureDownload.DownloadFileAsync(
                Http, EngineDownloadUrl, tmp, MaximumEngineBytes, EngineSha256,
                $"yt-dlp {PinnedEngineVersion}", progress, requestToken).ConfigureAwait(false);
            requestToken.ThrowIfCancellationRequested();
            File.Move(tmp, EnginePath, overwrite: true);
            InvalidateIntegrityCache();
            if (!IsPinnedEngineValid())
                throw new InvalidOperationException("The installed yt-dlp engine failed post-install verification.");
            progress?.Report((100, $"yt-dlp {PinnedEngineVersion} installed and verified."));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"Downloading yt-dlp exceeded the {InstallTimeout.TotalMinutes:0}-minute timeout.");
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort cleanup */ }
        }
    }

    /// <summary>Replaces the engine with the application-pinned, verified release.</summary>
    public async Task UpdateAsync(Action<string> log, CancellationToken ct)
    {
        await _maintenanceGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await WaitForOperationsIdleAsync(ct).ConfigureAwait(false);
            log($"Installing verified yt-dlp {PinnedEngineVersion}...");
            await EnsureInstalledCoreAsync(
                new Progress<(double Percent, string Message)>(p => log(p.Message)), ct, force: true)
                .ConfigureAwait(false);
            log("Engine installation verified.");
        }
        finally
        {
            _maintenanceGate.Release();
        }
    }

    private async Task<IDisposable> EnterOperationAsync(CancellationToken ct)
    {
        await _maintenanceGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (_operationSync)
            {
                if (_activeOperationCount == 0)
                {
                    _operationsIdle = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }

                _activeOperationCount++;
            }

            return new OperationLease(this);
        }
        finally
        {
            _maintenanceGate.Release();
        }
    }

    private void ExitOperation()
    {
        TaskCompletionSource<bool>? idle = null;
        lock (_operationSync)
        {
            if (_activeOperationCount <= 0)
                return;

            _activeOperationCount--;
            if (_activeOperationCount == 0)
            {
                idle = _operationsIdle;
                _operationsIdle = null;
            }
        }

        idle?.TrySetResult(true);
    }

    private async Task WaitForOperationsIdleAsync(CancellationToken ct)
    {
        Task idleTask;
        lock (_operationSync)
        {
            idleTask = _activeOperationCount == 0
                ? Task.CompletedTask
                : _operationsIdle!.Task;
        }

        await idleTask.WaitAsync(ct).ConfigureAwait(false);
    }

    private void EnsureInstalledOrThrow()
    {
        if (!IsInstalled)
            throw new InvalidOperationException(
                "The yt-dlp engine is not installed yet. Use the Install button on the Downloads or About page first.");
    }

    private static ProgressUpdate ParseProgress(string line)
    {
        // YTPROG|downloaded|total|speed|eta|status
        var parts = line.Split('|');
        return new ProgressUpdate
        {
            DownloadedBytes = parts.Length > 1 ? ParseLong(parts[1]) : null,
            TotalBytes = parts.Length > 2 ? ParseLong(parts[2]) : null,
            SpeedBytesPerSec = parts.Length > 3 ? ParseDouble(parts[3]) : null,
            EtaSeconds = parts.Length > 4 ? ParseDouble(parts[4]) : null,
            Status = parts.Length > 5 ? parts[5] : null
        };
    }

    private static long? ParseLong(string raw)
    {
        if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) &&
            double.IsFinite(v) && v is >= 0 and <= long.MaxValue)
            return (long)v;
        return null;
    }

    private static double? ParseDouble(string raw)
    {
        if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) &&
            double.IsFinite(v) && v >= 0)
            return v;
        return null;
    }

    private static string SummarizeError(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var errorLine = lines.LastOrDefault(l => l.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                        ?? lines.LastOrDefault();
        return string.IsNullOrWhiteSpace(errorLine)
            ? "The engine could not process this URL."
            : errorLine;
    }

    private static string QuoteForLog(string arg) =>
        arg.Contains(' ') || arg.Contains('"') ? $"\"{arg}\"" : arg;

    private bool IsPinnedEngineValid()
    {
        try
        {
            var info = new FileInfo(EnginePath);
            if (!info.Exists || info.Length <= 0)
                return false;

            lock (_integritySync)
            {
                if (_verifiedLength == info.Length && _verifiedWriteTicks == info.LastWriteTimeUtc.Ticks)
                    return _verifiedIntegrity;
                _verifiedIntegrity = SecureDownload.FileMatchesSha256(EnginePath, EngineSha256);
                _verifiedLength = info.Length;
                _verifiedWriteTicks = info.LastWriteTimeUtc.Ticks;
                return _verifiedIntegrity;
            }
        }
        catch
        {
            return false;
        }
    }

    private void InvalidateIntegrityCache()
    {
        lock (_integritySync)
        {
            _verifiedLength = -1;
            _verifiedWriteTicks = -1;
            _verifiedIntegrity = false;
        }
    }

    private static void NormalizeVideoInfo(VideoInfo info)
    {
        if (info.Thumbnails is not null)
            info.Thumbnails = info.Thumbnails.Where(static value => value is not null).ToList();
        if (info.Formats is not null)
            info.Formats = info.Formats.Where(static value => value is not null).ToList();
        if (info.Entries is not null)
        {
            info.Entries = info.Entries.Where(static value => value is not null).ToList();
            foreach (var entry in info.Entries)
            {
                if (entry.Formats is not null)
                    entry.Formats = entry.Formats.Where(static value => value is not null).ToList();
            }
        }
    }

    private static string RedactUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return "<url>";
        try
        {
            var builder = new UriBuilder(uri)
            {
                UserName = string.Empty,
                Password = string.Empty,
                Query = string.Empty,
                Fragment = string.Empty
            };
            return builder.Uri.AbsoluteUri;
        }
        catch
        {
            return $"{uri.Scheme}://{uri.Host}/";
        }
    }

    private static string RedactProcessText(string text, IReadOnlyList<string> args) =>
        CreateRedactor(args)(text);

    private static Func<string, string> CreateRedactor(IReadOnlyList<string> args)
    {
        var sensitiveFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "-u", "--username", "-p", "--password", "--twofactor", "--video-password",
            "--ap-username", "--ap-password", "--user-agent", "--referer",
            "--proxy", "--geo-verification-proxy", "--add-headers", "--cookies",
            "--cookies-from-browser", "--netrc-location"
        };
        var secrets = new List<string>();
        for (var index = 0; index + 1 < args.Count; index++)
        {
            if (sensitiveFlags.Contains(args[index]) && !string.IsNullOrEmpty(args[index + 1]))
                secrets.Add(args[index + 1]);
        }
        foreach (var argument in args)
        {
            var separator = argument.IndexOf('=');
            if (separator > 0 && sensitiveFlags.Contains(argument[..separator]) &&
                separator + 1 < argument.Length)
                secrets.Add(argument[(separator + 1)..]);
        }

        return input =>
        {
            var redacted = input;
            foreach (var secret in secrets.OrderByDescending(static value => value.Length))
                redacted = redacted.Replace(secret, "<redacted>", StringComparison.Ordinal);
            return Regex.Replace(
                redacted,
                @"https?://[^\s\""'<>]+",
                match => RedactUrl(match.Value),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
        };
    }
}
