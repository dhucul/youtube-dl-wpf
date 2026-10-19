using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Download;

public sealed class DownloadProgress
{
    public long DownloadedBytes { get; init; }
    public long? TotalBytes { get; init; }
    public double? BytesPerSecond { get; init; }
}

/// <summary>Downloads to a partial file, validating the remote entity before resuming.</summary>
public sealed partial class FileDownloader
{
    public string? ExternalDownloader { get; init; }
    public IReadOnlyList<string>? ExternalDownloaderArguments { get; init; }
    public bool NoContinue { get; init; }
    internal IndexedDashDownloader.ResourceVersion? CompletedVersion { get; private set; }
    public bool NoPart { get; init; }
    public bool NoMtime { get; init; }
    public string BufferSize { get; init; } = "";
    public bool NoResizeBuffer { get; init; }
    public string HttpChunkSize { get; init; } = "";
    internal Action<string>? TransferLog { get; init; }
    public long? MinimumBytes { get; init; }
    public long? MaximumBytes { get; init; }
    private const int FileBufferSize = 64 * 1024;
    private readonly MediaHttp _http;
    public FileDownloader(MediaHttp http) => _http = http;

    public async Task DownloadAsync(Uri url, string destination, ByteRateLimiter? limiter,
        IProgress<DownloadProgress>? progress, CancellationToken ct, int retries = 3)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retries);
        NativeExternalDownloader.Validate(ExternalDownloader, ExternalDownloaderArguments);
        if (NativeExternalDownloader.Parse(ExternalDownloader, ExternalDownloaderArguments) is { } delegated) _ = NativeExternalDownloader.Resolve(delegated);
        CompletedVersion=null;
        _=new NativeFileSizeLimits(MinimumBytes,MaximumBytes);
        _ = NativeReadBuffer.ParseSize(BufferSize);
        _ = NativeHttpChunkSize.Parse(HttpChunkSize);
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https"))
            throw new ArgumentException("The media address must use HTTP or HTTPS.", nameof(url));
        destination = Path.GetFullPath(destination);
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var part = NoPart ? destination : destination + ".part";
        var receiptPath = NoPart ? destination + ".native-resume.json" : part + ".json";
        using var recovery = NoPart ? new DirectWriteRecovery(destination, receiptPath) : null;
        Exception? last = null;
        for (var attempt = 0; attempt <= retries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var lastModified = await DownloadOnceAsync(url, part, receiptPath, recovery, limiter, progress, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (!NoPart) File.Move(part, destination, overwrite: true);
                if (!NoMtime) NativeFileTime.TrySet(destination, lastModified?.UtcDateTime);
                TryDelete(receiptPath);
                recovery?.Commit();
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (NativeFileSizeException) { throw; }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TimeoutException or OperationCanceledException)
            {
                last = ex;
                if (attempt == retries) break;
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(8, attempt + 1)), ct).ConfigureAwait(false);
            }
        }
        throw new IOException($"Could not download media from {url.Host}. {last?.Message}", last);
    }

    private async Task<DateTimeOffset?> DownloadOnceAsync(Uri url, string part, string receiptPath, DirectWriteRecovery? recovery, ByteRateLimiter? limiter,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        if (NativeHttpChunkSize.Parse(HttpChunkSize) is { } chunkSize)
            return await DownloadChunksAsync(url, part, receiptPath, recovery, chunkSize, limiter, progress, ct).ConfigureAwait(false);
        var existing = !NoContinue && File.Exists(part) ? new FileInfo(part).Length : 0L;
        var receipt = NoContinue ? null : ReadReceipt(receiptPath, url);
        if (receipt is null || !receipt.HasValidator || receipt.Length is { } total && existing > total)
            existing = 0;
        using var request = CreateRequest(url, existing, receipt);
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existing > 0 ||
            response.StatusCode == HttpStatusCode.PartialContent && !ValidRange(response, existing, receipt))
        {
            using var restart = CreateRequest(url, 0, null);
            using var full = await SendAsync(restart, ct).ConfigureAwait(false);
            full.EnsureSuccessStatusCode();
            if (full.StatusCode == HttpStatusCode.PartialContent)
                throw new InvalidDataException("The server returned an invalid partial response to a full download request.");
            await WriteAsync(full, url, part, receiptPath, recovery, 0, limiter, progress, ct).ConfigureAwait(false);
            return full.Content.Headers.LastModified;
        }
        response.EnsureSuccessStatusCode();
        if (response.StatusCode != HttpStatusCode.PartialContent) existing = 0;
        await WriteAsync(response, url, part, receiptPath, recovery, existing, limiter, progress, ct).ConfigureAwait(false);
        return response.Content.Headers.LastModified;
    }

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        _http.SendDownloadAsync(request, NativeExternalDownloader.Parse(ExternalDownloader, ExternalDownloaderArguments), ct);

    private static HttpRequestMessage CreateRequest(Uri url, long existing, ResumeReceipt? receipt)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
        if (existing > 0 && receipt is not null)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
            request.Headers.IfRange = receipt.StrongTag is { } tag
                ? new RangeConditionHeaderValue(tag)
                : new RangeConditionHeaderValue(receipt.LastModified!.Value);
        }
        return request;
    }

    private static bool ValidRange(HttpResponseMessage response, long existing, ResumeReceipt? receipt)
    {
        var range = response.Content.Headers.ContentRange;
        if (range?.Unit != "bytes" || range.From != existing || range.To is null || range.Length is not > 0 ||
            range.To != range.Length - 1 || range.To < existing ||
            response.Content.Headers.ContentLength is { } length && length != range.To - existing + 1)
            return false;
        if (existing == 0) return true;
        if (receipt is null || receipt.Length is { } expected && expected != range.Length) return false;
        if (receipt.StrongTag is { } tag) return response.Headers.ETag?.Equals(tag) == true;
        return receipt.LastModified is { } modified && response.Content.Headers.LastModified == modified;
    }

    private async Task WriteAsync(HttpResponseMessage response, Uri url, string part, string receiptPath, DirectWriteRecovery? recovery, long existing,
        ByteRateLimiter? limiter, IProgress<DownloadProgress>? progress, CancellationToken ct, bool completeMedia = true)
    {
        var expectedBody = response.Content.Headers.ContentLength ?? (response.StatusCode == HttpStatusCode.PartialContent &&
            response.Content.Headers.ContentRange is { From: { } from, To: { } to } ? checked(to - from + 1) : (long?)null);
        var total = response.StatusCode == HttpStatusCode.PartialContent
            ? response.Content.Headers.ContentRange!.Length
            : expectedBody;
        var limits=new NativeFileSizeLimits(MinimumBytes,MaximumBytes);
        if(total is {} declaredSize)limits.Check(declaredSize);
        var receipt = new ResumeReceipt(ResourceKey(url), response.Headers.ETag?.ToString(),
            response.Content.Headers.LastModified, total, response.Headers.Date);
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        recovery?.Prepare(url);
        await using var output = new FileStream(part, existing > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, FileBufferSize, useAsync: true);
        if (output.Length != existing) throw new IOException("The partial file changed while the download was starting.");
        await WriteReceiptAsync(receiptPath, receipt, ct).ConfigureAwait(false);
        var buffer = new NativeReadBuffer(BufferSize, NoResizeBuffer);
        var downloaded = existing;
        var started = Environment.TickCount64;
        while (true)
        {
            var bytes = await buffer.ReadAsync(_http, input, ct).ConfigureAwait(false);
            var count = bytes.Length;
            if (count == 0) break;
            if (expectedBody is { } bodyLength && downloaded - existing + count > bodyLength ||
                total is { } fullLength && downloaded + count > fullLength)
                throw new InvalidDataException("The server sent more media bytes than declared.");
            limits.CheckMaximum(checked(downloaded+count));
            await output.WriteAsync(bytes, ct).ConfigureAwait(false);
            downloaded += count;
            if (limiter is not null) await limiter.ThrottleAsync(count, ct).ConfigureAwait(false);
            progress?.Report(new DownloadProgress
            {
                DownloadedBytes = downloaded,
                TotalBytes = total,
                BytesPerSecond = (downloaded - existing) / Math.Max(0.001, (Environment.TickCount64 - started) / 1000d)
            });
        }
        var expectedEnd = !completeMedia && response.Content.Headers.ContentRange?.To is { } last ? checked(last + 1) : total;
        if (downloaded == 0 || expectedBody is { } expected && downloaded - existing != expected || expectedEnd is { } expectedTotal && downloaded != expectedTotal)
            throw new EndOfStreamException("The media download was incomplete; the partial file was retained.");
        if (completeMedia) limits.Check(downloaded);
        await output.FlushAsync(ct).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
        if(completeMedia)CompletedVersion=new(response.Headers.ETag is {IsWeak:false} tag?tag.ToString():null,response.Content.Headers.LastModified,total??downloaded,response.Headers.Date);
    }

    private static ResumeReceipt? ReadReceipt(string path, Uri url)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > 4096) return null;
            var receipt = JsonSerializer.Deserialize<ResumeReceipt>(stream);
            return receipt?.ResourceKey == ResourceKey(url) && receipt.Length is not < 0 ? receipt : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private static string ResourceKey(Uri url) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url.AbsoluteUri)));

    private static async Task WriteReceiptAsync(string path, ResumeReceipt receipt, CancellationToken ct)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(receipt), ct).ConfigureAwait(false);
            for (var attempt = 0; ; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try { File.Move(temporary, path, overwrite: true); break; }
                catch (Exception ex) when (attempt < 7 && ReceiptReplacementBusy(ex, path))
                {
                    // Rapid chunk updates can overlap a Windows reader or a
                    // delete-pending destination. Retry the atomic rename only.
                    await Task.Delay(Math.Min(80, 5 << attempt), ct).ConfigureAwait(false);
                }
            }
        }
        finally { TryDelete(temporary); }
    }

    private static bool ReceiptReplacementBusy(Exception error, string path)
    {
        if (!OperatingSystem.IsWindows() || error is not (IOException or UnauthorizedAccessException) ||
            (error.HResult & 0xffff) is not (5 or 32 or 33)) return false;
        try { return (File.GetAttributes(path) & (FileAttributes.ReadOnly | FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
        catch (UnauthorizedAccessException) { return true; } // A delete-pending file can deny attribute reads too.
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // NoPart writes the named transfer file directly. A pre-existing file without
    // an owned, validated resume receipt is restored if that direct write fails.
    private sealed class DirectWriteRecovery(string destination, string receiptPath) : IDisposable
    {
        private string? _backup;
        private string? _receiptBackup;
        private bool _prepared;
        private bool _committed;
        public void Prepare(Uri url)
        {
            if (_prepared) return;
            foreach (var path in new[] { destination, receiptPath })
                if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A direct transfer file or receipt cannot be a link.");
            var receipt = ReadReceipt(receiptPath, url);
            var ownedPartial = receipt?.HasValidator == true && File.Exists(destination) &&
                (receipt.Length is null || new FileInfo(destination).Length <= receipt.Length);
            if (File.Exists(destination) && !ownedPartial)
            {
                var backup = Path.Combine(Path.GetDirectoryName(destination)!, ".native-transfer-backup-" + Guid.NewGuid().ToString("N"));
                File.Copy(destination, backup, overwrite: false);
                _backup = backup;
                if (File.Exists(receiptPath))
                {
                    File.Copy(receiptPath, _backup + ".receipt", overwrite: false);
                    _receiptBackup = _backup + ".receipt";
                }
            }
            _prepared = true;
        }
        public void Commit()
        {
            _committed = true;
            if (_backup is not null) TryDelete(_backup);
            if (_receiptBackup is not null) TryDelete(_receiptBackup);
        }
        public void Dispose()
        {
            if (_committed || _backup is null) return;
            File.Move(_backup, destination, overwrite: true);
            if (_receiptBackup is not null) File.Move(_receiptBackup, receiptPath, overwrite: true);
            else if (_prepared) TryDelete(receiptPath);
        }
    }

    private sealed record ResumeReceipt(string ResourceKey, string? ETag, DateTimeOffset? LastModified, long? Length, DateTimeOffset? ResponseDate = null)
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public EntityTagHeaderValue? StrongTag => EntityTagHeaderValue.TryParse(ETag, out var tag) && !tag.IsWeak ? tag : null;
        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasValidator => StrongTag is not null || LastModified is not null;
    }
}
