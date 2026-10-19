using System.Net;
using System.Net.Http.Headers;

namespace YoutubeDl.Engine.Download;

public sealed partial class FileDownloader
{
    private async Task<DateTimeOffset?> DownloadChunksAsync(Uri url, string part, string receiptPath, DirectWriteRecovery? recovery,
        long chunkSize, ByteRateLimiter? limiter, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var existing = !NoContinue && File.Exists(part) ? new FileInfo(part).Length : 0L;
        var receipt = NoContinue ? null : ReadReceipt(receiptPath, url);
        if (receipt is null || !ChunkValidator(receipt) || receipt.Length is { } total && existing > total) { existing = 0; receipt = null; }
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var end = existing > long.MaxValue - (chunkSize - 1) ? long.MaxValue : existing + chunkSize - 1;
            if (receipt?.Length is { } known && existing < known) end = Math.Min(end, known - 1);
            using var request = CreateRequest(url, existing, receipt);
            request.Headers.Range = new RangeHeaderValue(existing, end);
            using var response = await SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                await WriteAsync(response, url, part, receiptPath, recovery, 0, limiter, progress, ct).ConfigureAwait(false);
                return response.Content.Headers.LastModified;
            }
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable || response.StatusCode == HttpStatusCode.PartialContent && !ChunkRange(response, existing, end, receipt))
                return await DownloadCompleteAsync(url, part, receiptPath, recovery, limiter, progress, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.StatusCode != HttpStatusCode.PartialContent) throw new InvalidDataException("The server did not supply a complete or bounded media response.");
            var range = response.Content.Headers.ContentRange!;
            var updated = new ResumeReceipt(ResourceKey(url), response.Headers.ETag?.ToString(), response.Content.Headers.LastModified, range.Length, response.Headers.Date);
            var complete = range.To == range.Length - 1;
            if (!complete && !ChunkValidator(updated))
                return await DownloadCompleteAsync(url, part, receiptPath, recovery, limiter, progress, ct).ConfigureAwait(false);
            await WriteAsync(response, url, part, receiptPath, recovery, existing, limiter, progress, ct, complete).ConfigureAwait(false);
            if (complete) return response.Content.Headers.LastModified;
            existing = checked(range.To!.Value + 1); receipt = updated;
        }
    }
    private async Task<DateTimeOffset?> DownloadCompleteAsync(Uri url, string part, string receiptPath, DirectWriteRecovery? recovery,
        ByteRateLimiter? limiter, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        TransferLog?.Invoke("[native] The server cannot provide matching bounded media ranges; requesting the complete resource.");
        using var request = CreateRequest(url, 0, null); using var response = await SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("The server did not return a complete resource after a range restart.");
        await WriteAsync(response, url, part, receiptPath, recovery, 0, limiter, progress, ct).ConfigureAwait(false);
        return response.Content.Headers.LastModified;
    }
    private static bool ChunkValidator(ResumeReceipt receipt) => receipt.StrongTag is not null || receipt.ETag is null &&
        receipt.LastModified is { } modified && receipt.ResponseDate is { } date && date - modified >= TimeSpan.FromSeconds(60);
    private static bool ChunkRange(HttpResponseMessage response, long offset, long end, ResumeReceipt? receipt)
    {
        var range = response.Content.Headers.ContentRange;
        if (range?.Unit != "bytes" || range.From != offset || range.To is null || range.Length is not > 0 || range.To < offset || range.To > end || range.To >= range.Length ||
            response.Content.Headers.ContentLength is { } length && length != range.To - offset + 1) return false;
        if (offset == 0) return true;
        if (receipt is null || !ChunkValidator(receipt) || receipt.Length is { } expected && expected != range.Length) return false;
        if (receipt.StrongTag is { } tag) return response.Headers.ETag?.Equals(tag) == true;
        return response.Headers.ETag is null && response.Content.Headers.LastModified == receipt.LastModified;
    }
}
