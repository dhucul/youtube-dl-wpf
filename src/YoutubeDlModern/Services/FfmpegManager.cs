using System.IO;
using System.IO.Compression;
using System.Net.Http;
using YoutubeDlModern.Models;

namespace YoutubeDlModern.Services;

/// <summary>
/// Locates or installs ffmpeg (required for format merging, audio extraction,
/// and most post-processing).
/// </summary>
public sealed class FfmpegManager
{
    public const string PinnedFfmpegVersion = "9.0.1";
    private const string FfmpegZipUrl =
        "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.1-essentials_build.zip";
    private const string FfmpegZipSha256 = "fec81ae03971d9dd4be3ebe02e263bd2ec1d789483f931bdba5f5715e65da2e9";
    private const string FfmpegExeSha256 = "72a489eccd008c2ec2c0a5856c5c75bc3d8bbfa90166c4566865c246445e6aa3";
    private const string FfprobeExeSha256 = "19202b23c0043f15ad1b7bce2344f406fd52bd6efd8f995ce02e7392a1cec52f";
    private const long MaximumArchiveBytes = 150L * 1024 * 1024;
    private const long MaximumExecutableBytes = 160L * 1024 * 1024;
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(20);
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _installGate = new(1, 1);
    private readonly object _integritySync = new();
    private (long FfmpegLength, long FfmpegTicks, long FfprobeLength, long FfprobeTicks)? _verifiedFiles;
    private bool _verifiedIntegrity;

    public FfmpegManager(SettingsService settings) => _settings = settings;

    public string FfmpegDir => Path.Combine(SettingsService.LocalDataDir, "ffmpeg");

    /// <summary>Directory containing ffmpeg.exe, or null. Checks configured path, app-managed dir, then PATH.</summary>
    public Task<string?> LocateAsync(CancellationToken ct = default) =>
        LocateAsync(_settings.CreateSnapshot(), ct);

    public async Task<string?> LocateAsync(AppSettings settingsSnapshot, CancellationToken ct = default)
    {
        await _installGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await LocateCoreAsync(settingsSnapshot, ct).ConfigureAwait(false);
        }
        finally
        {
            _installGate.Release();
        }
    }

    private async Task<string?> LocateCoreAsync(AppSettings settingsSnapshot, CancellationToken ct)
    {
        var configured = settingsSnapshot.PostProcess.FfmpegLocation;
        if (!string.IsNullOrWhiteSpace(configured) && HasCompleteInstall(configured))
            return configured;

        if (await Task.Run(IsManagedInstallComplete, ct).ConfigureAwait(false))
            return FfmpegDir;

        try
        {
            var result = await ProcessRunner.RunAsync(
                "where.exe", new[] { "ffmpeg" }, ct: ct, timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                foreach (var candidate in result.StdOut.Split(
                    '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!File.Exists(candidate))
                        continue;

                    var directory = Path.GetDirectoryName(candidate);
                    if (!string.IsNullOrWhiteSpace(directory) && HasCompleteInstall(directory))
                        return directory;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // ffmpeg is not available on PATH.
        }

        return null;
    }

    /// <summary>Downloads an essentials ffmpeg build and installs both required executables.</summary>
    public async Task EnsureInstalledAsync(IProgress<(double Percent, string Message)>? progress, CancellationToken ct)
    {
        await _installGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureInstalledCoreAsync(progress, ct).ConfigureAwait(false);
        }
        finally
        {
            _installGate.Release();
        }
    }

    private async Task EnsureInstalledCoreAsync(
        IProgress<(double Percent, string Message)>? progress,
        CancellationToken ct)
    {
        if (await Task.Run(IsManagedInstallComplete, ct).ConfigureAwait(false)) return;

        var operationId = Guid.NewGuid().ToString("N");
        var zipPath = Path.Combine(Path.GetTempPath(), $"ffmpeg-{operationId}.zip");
        var stagingDir = Path.Combine(SettingsService.LocalDataDir, $".ffmpeg-staging-{operationId}");
        var backupDir = Path.Combine(SettingsService.LocalDataDir, $".ffmpeg-backup-{operationId}");
        using var timeoutCts = new CancellationTokenSource(InstallTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var requestToken = linkedCts.Token;

        try
        {
            await SecureDownload.DownloadFileAsync(
                Http, FfmpegZipUrl, zipPath, MaximumArchiveBytes, FfmpegZipSha256,
                $"ffmpeg {PinnedFfmpegVersion}", progress, requestToken).ConfigureAwait(false);

            requestToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(SettingsService.LocalDataDir);
            Directory.CreateDirectory(stagingDir);
            progress?.Report((100, "Extracting ffmpeg..."));

            using (var zip = ZipFile.OpenRead(zipPath))
            {
                if (zip.Entries.Count > 10_000)
                    throw new InvalidDataException("The ffmpeg archive contains too many entries.");
                foreach (var entry in zip.Entries)
                {
                    requestToken.ThrowIfCancellationRequested();
                    var name = entry.FullName.Replace('\\', '/');
                    string? target = name.EndsWith("bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase) ? "ffmpeg.exe"
                        : name.EndsWith("bin/ffprobe.exe", StringComparison.OrdinalIgnoreCase) ? "ffprobe.exe"
                        : null;
                    if (target is not null)
                        await ExtractBoundedAsync(
                            entry, Path.Combine(stagingDir, target), MaximumExecutableBytes, requestToken)
                            .ConfigureAwait(false);
                }
            }

            var stagedFfmpeg = Path.Combine(stagingDir, "ffmpeg.exe");
            var stagedFfprobe = Path.Combine(stagingDir, "ffprobe.exe");
            if (!SecureDownload.FileMatchesSha256(stagedFfmpeg, FfmpegExeSha256) ||
                !SecureDownload.FileMatchesSha256(stagedFfprobe, FfprobeExeSha256))
                throw new InvalidOperationException("The extracted ffmpeg binaries failed SHA-256 verification.");

            requestToken.ThrowIfCancellationRequested();
            CommitManagedInstall(stagingDir, backupDir);
            InvalidateIntegrityCache();
            if (!await Task.Run(IsManagedInstallComplete, requestToken).ConfigureAwait(false))
            {
                RestoreManagedInstallAfterVerificationFailure(backupDir);
                throw new InvalidOperationException("The installed ffmpeg files failed post-install verification.");
            }
            progress?.Report((100, $"ffmpeg {PinnedFfmpegVersion} installed and verified."));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"Downloading ffmpeg exceeded the {InstallTimeout.TotalMinutes:0}-minute timeout.");
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { /* best effort cleanup */ }
            try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true); } catch { /* best effort cleanup */ }
            try
            {
                if (Directory.Exists(backupDir) && IsManagedInstallComplete())
                    Directory.Delete(backupDir, recursive: true);
            }
            catch { /* keep a backup that could not be cleaned up */ }
        }
    }

    private void CommitManagedInstall(string stagingDir, string backupDir)
    {
        var previousInstallMoved = false;
        if (Directory.Exists(FfmpegDir))
        {
            Directory.Move(FfmpegDir, backupDir);
            previousInstallMoved = true;
        }

        try
        {
            // Staging is deliberately created beside the destination so this rename is atomic.
            Directory.Move(stagingDir, FfmpegDir);
        }
        catch
        {
            if (previousInstallMoved)
            {
                if (Directory.Exists(FfmpegDir))
                    Directory.Delete(FfmpegDir, recursive: true);
                Directory.Move(backupDir, FfmpegDir);
            }
            else if (Directory.Exists(FfmpegDir))
            {
                Directory.Delete(FfmpegDir, recursive: true);
            }

            throw;
        }

        if (previousInstallMoved)
        {
            try { Directory.Delete(backupDir, recursive: true); }
            catch { /* the new complete install is already active */ }
        }
    }

    private void RestoreManagedInstallAfterVerificationFailure(string backupDir)
    {
        if (Directory.Exists(FfmpegDir))
            Directory.Delete(FfmpegDir, recursive: true);
        if (Directory.Exists(backupDir))
            Directory.Move(backupDir, FfmpegDir);
        InvalidateIntegrityCache();
    }

    private bool IsManagedInstallComplete()
    {
        var ffmpegPath = Path.Combine(FfmpegDir, "ffmpeg.exe");
        var ffprobePath = Path.Combine(FfmpegDir, "ffprobe.exe");
        try
        {
            var ffmpeg = new FileInfo(ffmpegPath);
            var ffprobe = new FileInfo(ffprobePath);
            if (!ffmpeg.Exists || !ffprobe.Exists || ffmpeg.Length <= 0 || ffprobe.Length <= 0)
                return false;
            var identity = (ffmpeg.Length, ffmpeg.LastWriteTimeUtc.Ticks, ffprobe.Length, ffprobe.LastWriteTimeUtc.Ticks);
            lock (_integritySync)
            {
                if (_verifiedFiles == identity)
                    return _verifiedIntegrity;
                _verifiedIntegrity =
                    SecureDownload.FileMatchesSha256(ffmpegPath, FfmpegExeSha256) &&
                    SecureDownload.FileMatchesSha256(ffprobePath, FfprobeExeSha256);
                _verifiedFiles = identity;
                return _verifiedIntegrity;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool HasCompleteInstall(string directory) =>
        IsNonEmptyFile(Path.Combine(directory, "ffmpeg.exe")) &&
        IsNonEmptyFile(Path.Combine(directory, "ffprobe.exe"));

    private static bool IsNonEmptyFile(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length > 0; }
        catch { return false; }
    }

    private void InvalidateIntegrityCache()
    {
        lock (_integritySync)
        {
            _verifiedFiles = null;
            _verifiedIntegrity = false;
        }
    }

    private static async Task ExtractBoundedAsync(
        ZipArchiveEntry entry,
        string destinationPath,
        long maximumBytes,
        CancellationToken ct)
    {
        if (entry.Length <= 0 || entry.Length > maximumBytes)
            throw new InvalidDataException($"Archive entry '{entry.FullName}' has an unsafe size.");
        if (entry.CompressedLength > 0 && entry.Length / Math.Max(1, entry.CompressedLength) > 250)
            throw new InvalidDataException($"Archive entry '{entry.FullName}' has an unsafe compression ratio.");

        await using var source = entry.Open();
        await using var destination = new FileStream(
            destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
        var buffer = GC.AllocateUninitializedArray<byte>(81920);
        long copied = 0;
        while (true)
        {
            var count = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (count == 0)
                break;
            copied = checked(copied + count);
            if (copied > maximumBytes || copied > entry.Length)
                throw new InvalidDataException($"Archive entry '{entry.FullName}' exceeded its declared size.");
            await destination.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
        }
        if (copied != entry.Length)
            throw new InvalidDataException($"Archive entry '{entry.FullName}' was incomplete.");
        await destination.FlushAsync(ct).ConfigureAwait(false);
        destination.Flush(flushToDisk: true);
    }
}
