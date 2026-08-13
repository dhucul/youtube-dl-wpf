using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using YoutubeDlModern.Models;

namespace YoutubeDlModern.Services;

internal static class SecureDownload
{
    private const int BufferSize = 81920;

    public static async Task<long> DownloadFileAsync(
        HttpClient http,
        string url,
        string destinationPath,
        long maximumBytes,
        string expectedSha256,
        string progressLabel,
        IProgress<(double Percent, string Message)>? progress,
        CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        var expectedHash = Convert.FromHexString(expectedSha256);
        if (expectedHash.Length != 32)
            throw new ArgumentException("A SHA-256 digest must contain exactly 32 bytes.", nameof(expectedSha256));

        using var response = await http.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength is > 0 && declaredLength > maximumBytes)
            throw new InvalidDataException(
                $"The {progressLabel} download declares {Formatting.FormatBytes(declaredLength)}, " +
                $"which exceeds the {Formatting.FormatBytes(maximumBytes)} safety limit.");

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var destination = new FileStream(
            destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = GC.AllocateUninitializedArray<byte>(BufferSize);
        long received = 0;
        while (true)
        {
            var count = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (count == 0)
                break;

            received = checked(received + count);
            if (received > maximumBytes)
                throw new InvalidDataException(
                    $"The {progressLabel} download exceeded the {Formatting.FormatBytes(maximumBytes)} safety limit.");

            hash.AppendData(buffer, 0, count);
            await destination.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);

            var percent = declaredLength is > 0 ? 100.0 * received / declaredLength.Value : 0;
            progress?.Report((percent, declaredLength is > 0
                ? $"Downloading {progressLabel}... {percent:0}%"
                : $"Downloading {progressLabel}... {Formatting.FormatBytes(received)}"));
        }

        if (received == 0)
            throw new InvalidDataException($"The {progressLabel} download was empty.");
        if (declaredLength is > 0 && received != declaredLength.Value)
            throw new InvalidDataException(
                $"The {progressLabel} download was incomplete ({received} of {declaredLength.Value} bytes).");

        var actualHash = hash.GetHashAndReset();
        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
            throw new CryptographicException(
                $"The {progressLabel} download failed SHA-256 verification and was not installed.");

        await destination.FlushAsync(ct).ConfigureAwait(false);
        destination.Flush(flushToDisk: true);
        return received;
    }

    public static bool FileMatchesSha256(string path, string expectedSha256)
    {
        try
        {
            if (!File.Exists(path))
                return false;
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
                FileOptions.SequentialScan);
            var actual = SHA256.HashData(stream);
            return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expectedSha256));
        }
        catch
        {
            return false;
        }
    }
}
