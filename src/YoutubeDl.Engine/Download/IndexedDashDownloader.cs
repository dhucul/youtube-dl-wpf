using System.Net;
using System.Net.Http.Headers;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Download;

internal static class IndexedDashDownloader
{
    internal sealed record ResourceVersion(string? ETag, DateTimeOffset? Modified, long? Length, DateTimeOffset? ResponseDate = null);

    public static async Task DownloadAsync(MediaHttp http, DashRepresentation representation, string destination, ByteRateLimiter? limiter,
        IProgress<DownloadProgress>? progress, int retries, CancellationToken ct, long maximumOutputBytes = long.MaxValue, int concurrentFragments = 1, bool noPart = false, string? bufferSize = null, bool noResizeBuffer = false, string? httpChunkSize = null,bool noContinue=false,string? recoveryScope=null)
    {
        OrderedFragmentTransfer.Validate(concurrentFragments);
        var descriptor = representation.SegmentBase ?? throw new ArgumentException("No DASH index was supplied.");
        using var indexBytes = new MemoryStream();
        var version = await CopyRangeAsync(http, representation.Url, descriptor.IndexRange, indexBytes, null, limiter, retries, Mp4SegmentIndexParser.MaximumIndexBytes, ct, bufferSize, noResizeBuffer, httpChunkSize).ConfigureAwait(false);
        var index = Mp4SegmentIndexParser.Parse(indexBytes.ToArray(), descriptor.IndexRange.Offset, descriptor.Timescale);
        var first = index.Segments[0].Range.Offset;
        var indexEnd = checked(descriptor.IndexRange.Offset + descriptor.IndexRange.Length);
        var last = index.Segments[^1];
        if (checked(last.Range.Offset + last.Range.Length) > maximumOutputBytes) throw new InvalidDataException("The indexed DASH resource exceeds its output size limit.");
        if (first < indexEnd) throw new InvalidDataException("The DASH media ranges overlap the requested index.");
        if (first > 16 * 1024 * 1024) throw new NotSupportedException("The indexed DASH initialization prefix exceeds the native 16 MiB limit.");
        if (version.Length is { } total && checked(last.Range.Offset + last.Range.Length) > total)
            throw new InvalidDataException("The DASH index points beyond the media resource.");
        if (descriptor.PeriodDurationSeconds is { } duration)
        {
            if (!double.IsFinite(duration) || duration <= 0) throw new InvalidDataException("The indexed DASH period has an invalid duration.");
            var lastStart = (double)last.StartTime / index.Timescale;
            var end = ((double)last.StartTime + last.Duration) / index.Timescale;
            var padding = Math.Max(0.1, representation.FramesPerSecond is > 0 and var fps ? 1 / fps : 0);
            var tolerance = Math.Max((double)last.Duration / index.Timescale, padding);
            if (end - duration > padding || lastStart > duration + padding || end + tolerance < duration)
                throw new NotSupportedException("The DASH period and complete index describe different presentation windows.");
        }

        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var staging = Path.Combine(Path.GetDirectoryName(destination)!, ".dash-indexed-" + Guid.NewGuid().ToString("N"));

        var facts=System.Text.Json.JsonSerializer.Serialize(new{Scope=recoveryScope,Url=http.RecoveryIdentity(representation.Url),representation.Id,representation.Codecs,representation.MimeType,representation.Height,representation.FramesPerSecond,Index=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(indexBytes.ToArray()))});
        using var checkpoint=noContinue?null:await AdaptiveFragmentCheckpoint.OpenAsync(http,destination,facts,ct).ConfigureAwait(false);
        Directory.CreateDirectory(staging);
        var temporary = Path.Combine(staging, noPart ? "assembled.media" : "assembled.part");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                // Retain the file's byte positions, including sidx and any harmless prefix boxes.
                // This also preserves valid absolute offsets in non-CMAF movie fragments.
                if (descriptor.IndexRange.Offset > 0)
                    await CopyRangeAsync(http, representation.Url, new(0, descriptor.IndexRange.Offset), output, version, limiter, retries, 16 * 1024 * 1024, ct, bufferSize, noResizeBuffer, httpChunkSize).ConfigureAwait(false);
                await output.WriteAsync(indexBytes.ToArray(), ct).ConfigureAwait(false);
                if (first > indexEnd)
                    await CopyRangeAsync(http, representation.Url, new(indexEnd, first - indexEnd), output, version, limiter, retries, 16 * 1024 * 1024, ct, bufferSize, noResizeBuffer, httpChunkSize).ConfigureAwait(false);
                string FragmentPath(int number) => Path.Combine(staging, "fragment-" + number);
                await OrderedFragmentTransfer.RunAsync(index.Segments, concurrentFragments, async (segment, number, token) =>
                {
                    async Task<ResourceVersion?> Fetch(string path,CancellationToken cancel)
                    {
                        await using var fragment=new FileStream(path,FileMode.Create,FileAccess.ReadWrite,FileShare.None,65536,FileOptions.Asynchronous);
                        var actual=await CopyRangeAsync(http,representation.Url,segment.Range,fragment,version,limiter,retries,512L*1024*1024,cancel,bufferSize,noResizeBuffer,httpChunkSize).ConfigureAwait(false);
                        await fragment.FlushAsync(cancel).ConfigureAwait(false);fragment.Flush(true);return actual;
                    }
                    if(checkpoint is not null)await checkpoint.CopyAsync(number,representation.Url,segment.Range,null,FragmentPath(number),Fetch,token).ConfigureAwait(false);
                    else _=await Fetch(FragmentPath(number),token).ConfigureAwait(false);
                }, async (segment, number, token) =>
                {
                    if (output.Position != segment.Range.Offset) throw new InvalidDataException("The DASH range sequence does not preserve resource offsets.");
                    var fragment = FragmentPath(number);
                    await using (var input = File.OpenRead(fragment)) await input.CopyToAsync(output, token).ConfigureAwait(false);
                    File.Delete(fragment);
                    progress?.Report(new DownloadProgress { DownloadedBytes = output.Length });
                }, ct).ConfigureAwait(false);
                await output.FlushAsync(ct).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
            if(recoveryScope is null)checkpoint?.Complete();
        }
        finally
        {
            try { foreach (var file in Directory.EnumerateFiles(staging)) File.Delete(file); Directory.Delete(staging); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    internal static async Task<ResourceVersion> CopyRangeAsync(MediaHttp http, Uri url, HlsByteRange range, Stream destination, ResourceVersion? expected,
        ByteRateLimiter? limiter, int retries, long maximumBytes, CancellationToken ct, string? bufferSize = null, bool noResizeBuffer = false, string? httpChunkSize = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retries);
        if (range.Offset < 0 || range.Length <= 0 || range.Length > maximumBytes || range.Offset > long.MaxValue - range.Length)
            throw new InvalidDataException("The indexed DASH byte range is invalid or excessive.");
        var start = destination.Position;
        if (NativeHttpChunkSize.Parse(httpChunkSize) is { } chunkSize && chunkSize < range.Length)
        {
            var version = expected;
            long copied = 0;
            try
            {
                while (copied < range.Length)
                {
                    ct.ThrowIfCancellationRequested();
                    var length = Math.Min(chunkSize, range.Length - copied);
                    var actual = await CopyRangeAsync(http, url, new(checked(range.Offset + copied), length), destination, version,
                        limiter, retries, maximumBytes, ct, bufferSize, noResizeBuffer).ConfigureAwait(false);
                    copied += length;
                    if (copied < range.Length && !(actual.ETag is not null || actual.Modified is { } modified && actual.ResponseDate is { } date && date - modified >= TimeSpan.FromSeconds(60)))
                    {
                        // An unidentifiable resource cannot be stitched safely. Retrieve
                        // the complete protocol-required range as one response instead.
                        destination.Position = start; destination.SetLength(start);
                        return await CopyRangeAsync(http, url, range, destination, expected, limiter, retries, maximumBytes, ct, bufferSize, noResizeBuffer).ConfigureAwait(false);
                    }
                    version = actual;
                }
                return version!;
            }
            catch
            {
                destination.Position = start; destination.SetLength(start);
                throw;
            }
        }
        for (var attempt = 0; ; attempt++)
        {
            destination.Position = start; destination.SetLength(start);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new RangeHeaderValue(range.Offset, range.Offset + range.Length - 1);
                request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
                if (expected?.ETag is { } tag) request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(tag));
                else if (expected?.Modified is { } modified) request.Headers.IfUnmodifiedSince = modified;
                using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var contentRange = response.Content.Headers.ContentRange;
                if (response.StatusCode != HttpStatusCode.PartialContent || contentRange?.Unit != "bytes" || contentRange.From != range.Offset ||
                    contentRange.To != range.Offset + range.Length - 1 || response.Content.Headers.ContentLength is { } declared && declared != range.Length ||
                    contentRange.Length is { } length && length <= contentRange.To)
                    throw new InvalidDataException("The server did not honor the requested DASH byte range.");
                var actual = new ResourceVersion(response.Headers.ETag is { IsWeak: false } etag ? etag.ToString() : null,
                    response.Content.Headers.LastModified, contentRange.Length, response.Headers.Date);
                if (expected is not null && (expected.Length is { } total && actual.Length != total ||
                    expected.ETag is { } previousTag && actual.ETag != previousTag ||
                    expected.Modified is { } previousDate && actual.Modified != previousDate))
                    throw new InvalidDataException("The DASH resource changed after its segment index was read.");
                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var buffer = new NativeReadBuffer(bufferSize, noResizeBuffer);
                long received = 0;
                while (true)
                {
                    var bytes = await buffer.ReadAsync(http, source, ct).ConfigureAwait(false);
                    var read = bytes.Length;
                    if (read == 0) break;
                    if (received + read > range.Length) throw new InvalidDataException("The DASH range response exceeded its requested length.");
                    await destination.WriteAsync(bytes, ct).ConfigureAwait(false);
                    received += read;
                    if (limiter is not null) await limiter.ThrottleAsync(read, ct).ConfigureAwait(false);
                }
                if (received != range.Length) throw new EndOfStreamException("The DASH range response was incomplete.");
                return actual;
            }
            catch (Exception ex) when (attempt < retries && !ct.IsCancellationRequested && ex is IOException or HttpRequestException or TimeoutException)
            { await Task.Delay(TimeSpan.FromSeconds(Math.Min(8, attempt + 1)), ct).ConfigureAwait(false); }
        }
    }
}

