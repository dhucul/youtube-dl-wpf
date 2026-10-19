using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;

namespace YoutubeDl.Engine.Download;

public sealed record HlsDownloadTiming(IReadOnlyList<decimal> OriginalPeriodStarts);

/// <summary>Downloads completed HLS playlists with AES-128, initialization sections, and byte ranges.</summary>
public sealed class HlsDownloader
{
    public bool NoContinue { get; init; }
    internal string? RecoveryScope { get; init; }
    internal bool RetainCheckpointAfterTransfer { get; init; }
    public bool NoPart { get; init; }
    public string BufferSize { get; init; } = "";
    public bool NoResizeBuffer { get; init; }
    public string HttpChunkSize { get; init; } = "";
    internal Action<string>? TransferLog { get; init; }
    internal bool NormalizeTransportContinuity { get; init; }
    internal bool NormalizePesCounterResets { get; init; }
    internal RetainedMediaCollector? RetainedMedia { get; init; }
    internal LiveDashSelectionPolicy? TransitionPolicy {get;init;}
    private readonly MediaHttp _http;
    public HlsDownloader(MediaHttp http) => _http = http;
    internal Task CaptureResourceAsync(Uri url,HlsByteRange? range,string path,ByteRateLimiter limiter,int retries,CancellationToken ct) =>
        range is null ? new FileDownloader(_http) {NoContinue=true,NoPart=NoPart,BufferSize=BufferSize,NoResizeBuffer=NoResizeBuffer,HttpChunkSize=HttpChunkSize,TransferLog=TransferLog}.DownloadAsync(url,path,limiter,null,ct,retries)
        : DownloadRangeAsync(url,range,path,limiter,retries,ct);
    public async Task<HlsPlaylist> ReadAsync(Uri playlistUrl, CancellationToken ct)
    {
        var document = await _http.GetDocumentAsync(playlistUrl, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        await _http.ObservePlaybackManifestAsync(document,ct).ConfigureAwait(false);
        var parsed=HlsPlaylistParser.Parse(document.Text, document.EffectiveUrl??document.Url);
        var maps=new Dictionary<HlsInitialization,HlsInitialization>(ReferenceEqualityComparer.Instance);
        var keys=new Dictionary<HlsEncryption,HlsEncryption>(ReferenceEqualityComparer.Instance);
        HlsEncryption? Key(HlsEncryption? key){if(key is null)return null;if(!keys.TryGetValue(key,out var canonical))keys[key]=canonical=new(_http.CanonicalizePlaybackUri(key.KeyUrl),key.IV);return canonical;}
        var segments=parsed.MediaSegments.Select(segment=>
        {
            HlsInitialization? initialization=null;
            if(segment.Initialization is { } original&&!maps.TryGetValue(original,out initialization))maps[original]=initialization=new(_http.CanonicalizePlaybackUri(original.Url),original.Range,Key(original.Encryption));
            return segment with {Url=_http.CanonicalizePlaybackUri(segment.Url),Encryption=Key(segment.Encryption),Initialization=initialization};
        }).ToArray();
        return new HlsPlaylist {IsMaster=parsed.IsMaster,Variants=parsed.Variants.Select(v=>new HlsVariant{Url=_http.CanonicalizePlaybackUri(v.Url),Height=v.Height,Bandwidth=v.Bandwidth,VideoCodec=v.VideoCodec,FramesPerSecond=v.FramesPerSecond,AudioGroup=v.AudioGroup,SubtitleGroup=v.SubtitleGroup,ClosedCaptionGroup=v.ClosedCaptionGroup}).ToArray(),
            AudioRenditions=parsed.AudioRenditions.Select(r=>r with {Url=r.Url is null?null:_http.CanonicalizePlaybackUri(r.Url)}).ToArray(),SubtitleRenditions=parsed.SubtitleRenditions.Select(r=>r with {Url=r.Url is null?null:_http.CanonicalizePlaybackUri(r.Url)}).ToArray(),
            SourceUrl=_http.CanonicalizePlaybackUri(parsed.SourceUrl!),TargetDurationSeconds=parsed.TargetDurationSeconds,MediaSequence=parsed.MediaSequence,PlaylistType=parsed.PlaylistType,
            IsComplete=parsed.IsComplete,UnsupportedFeatures=parsed.UnsupportedFeatures,MediaSegments=segments,Segments=segments.Select(segment=>segment.Url).ToArray()};
    }

    public async Task<HlsDownloadTiming> DownloadAsync(HlsPlaylist playlist, string destination, IProgress<DownloadProgress>? progress,
        CancellationToken ct, ByteRateLimiter? limiter = null, int retries = 3, string? ffmpeg = null, IReadOnlyList<double>? periodDurations = null, int concurrentFragments = 1)
    {
        OrderedFragmentTransfer.Validate(concurrentFragments);
        if (playlist.IsMaster || playlist.Segments.Count == 0) throw new InvalidDataException("Choose a media playlist before downloading HLS.");
        if (playlist.UnsupportedFeatures.Count > 0) throw new NotSupportedException("HLS support is pending for: " + string.Join(", ", playlist.UnsupportedFeatures));
        if (!playlist.IsComplete) throw new NotSupportedException("Live HLS recording is not implemented yet.");
        var segments = playlist.MediaSegments.Count > 0 ? playlist.MediaSegments : playlist.Segments.Select((url, index) => new HlsSegment(url, (ulong)index, null, null, null)).ToArray();
        if (segments.Zip(segments.Skip(1)).Any(pair => pair.Second.DiscontinuitySequence < pair.First.DiscontinuitySequence)) throw new InvalidDataException("The HLS timeline periods are out of order.");
        var periods = segments.GroupBy(segment => segment.DiscontinuitySequence).Select(group => group.ToArray()).ToArray();
        var clipped=segments.Any(segment=>segment.TrimStartSeconds>0||segment.TrimEndSeconds>0);
        var joins = periods.Length > 1 || clipped || TransitionPolicy?.NormalizeTransitions==true;
        var fragmented = segments[0].Initialization is not null;
        if (joins)
        {
            if (periods.Length > 1000 || segments.Any(segment => (segment.Initialization is not null) != fragmented) ||
                periods.Any(period => period.Any(segment => !ReferenceEquals(segment.Initialization, period[0].Initialization))))
                throw new NotSupportedException("The HLS timeline changes initialization within a period or mixes segment container layouts.");
            if (segments.Any(segment => segment.DurationSeconds is not > 0) || periods.Any(period => !double.IsFinite(period.Sum(segment => segment.DurationSeconds!.Value))))
                throw new InvalidDataException("HLS discontinuities require finite segment durations.");
            if (string.IsNullOrWhiteSpace(ffmpeg) || !File.Exists(ffmpeg)) throw new NotSupportedException("ffmpeg is required to join HLS timeline changes.");
            FfmpegTool.RequireProbe(ffmpeg, "validate HLS timeline changes");
            if (periodDurations is not null && (periodDurations.Count != periods.Length || periodDurations.Any(duration => !double.IsFinite(duration) || duration <= 0)))
                throw new InvalidDataException("The shared HLS period durations are invalid.");
        }
        destination = Path.GetFullPath(destination);
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var staging = Path.Combine(directory, ".hls-" + Guid.NewGuid().ToString("N"));

        var facts=System.Text.Json.JsonSerializer.Serialize(new{Scope=RecoveryScope,Manifest=playlist.SourceUrl is null?null:_http.RecoveryIdentity(playlist.SourceUrl),Segments=segments.Select(s=>new{
            Url=_http.RecoveryIdentity(s.Url),s.Sequence,s.DiscontinuitySequence,s.Range,s.DurationSeconds,s.ProgramDateTime,s.TrimStartSeconds,s.TrimEndSeconds,
            Key=s.Encryption is null?null:_http.RecoveryIdentity(s.Encryption.KeyUrl),IV=s.Encryption?.IV,
            Init=s.Initialization is null?null:new{Url=s.Initialization.InlineData is null?_http.RecoveryIdentity(s.Initialization.Url):null,s.Initialization.Range,Inline=s.Initialization.InlineData is { } bytes?Convert.ToHexString(SHA256.HashData(bytes)):null,Key=s.Initialization.Encryption is null?null:_http.RecoveryIdentity(s.Initialization.Encryption.KeyUrl),IV=s.Initialization.Encryption?.IV}})});
        using var checkpoint=!NoContinue&&(playlist.SourceUrl is not null||RecoveryScope is not null)?await AdaptiveFragmentCheckpoint.OpenAsync(_http,destination,facts,ct).ConfigureAwait(false):null;
        Directory.CreateDirectory(staging);
        var extension = fragmented ? ".mp4" : ".ts";
        var temporary = Path.Combine(staging, joins || NoPart ? "assembled" + extension : "assembled.part");
        var keys = new Dictionary<HlsEncryption, byte[]>(ReferenceEqualityComparer.Instance);
        var files = new List<(string Path, double Duration)>();
        var edits = new List<MediaPeriodEdit>();
        long downloaded = 0;
        var checkpointOffset=0;
        try
        {
            for (var index = 0; index < periods.Length; index++)
            {
                var path = joins ? Path.Combine(staging, "period-" + index + extension) : temporary;
                HlsInitialization? lastInitialization = null;
                var resources = new List<Fragment>();
                foreach (var segment in periods[index])
                {
                    if (segment.Initialization is { } initialization && !ReferenceEquals(initialization, lastInitialization))
                    {
                        resources.Add(new(initialization.Url, initialization.Range, initialization.Encryption, segment.Sequence,initialization.InlineData));
                        lastInitialization = initialization;
                    }
                    resources.Add(new(segment.Url, segment.Range, segment.Encryption, segment.Sequence));
                }
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true);
                string FragmentPath(int number) => Path.Combine(staging, "fragment-" + number);
                await OrderedFragmentTransfer.RunAsync(resources, concurrentFragments, async (resource, number, token) =>
                {
                    var fragment = FragmentPath(number);
                    async Task<IndexedDashDownloader.ResourceVersion?> Fetch(string path,CancellationToken cancel)
                    {
                        if(resource.InlineData is { } bytes){if(File.Exists(path))File.Delete(path);await AdaptiveFragmentCheckpoint.DurableWriteAsync(path,bytes,cancel).ConfigureAwait(false);return null;}
                        if(resource.Range is not null)return await DownloadRangeAsync(resource.Url,resource.Range,path,limiter,retries,cancel).ConfigureAwait(false);
                        var transfer=new FileDownloader(_http){NoContinue=NoContinue,NoPart=NoPart,BufferSize=BufferSize,NoResizeBuffer=NoResizeBuffer,HttpChunkSize=HttpChunkSize,TransferLog=TransferLog,MaximumBytes=512L*1024*1024};
                        await transfer.DownloadAsync(resource.Url,path,limiter,null,cancel,retries).ConfigureAwait(false);return transfer.CompletedVersion;
                    }
                    if(checkpoint is not null)await checkpoint.CopyAsync(checkpointOffset+number,resource.Url,resource.Range,resource.InlineData,fragment,Fetch,token).ConfigureAwait(false);
                    else _=await Fetch(fragment,token).ConfigureAwait(false);
                }, async (resource, number, token) =>
                {
                    await AppendResourceAsync(FragmentPath(number), resource.Encryption, resource.Sequence, output, keys, token).ConfigureAwait(false);
                    progress?.Report(new DownloadProgress { DownloadedBytes = downloaded + output.Length });
                }, ct).ConfigureAwait(false);
                checkpointOffset+=resources.Count;
                await output.FlushAsync(ct).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
                downloaded += output.Length;
                if ((NormalizeTransportContinuity || NormalizePesCounterResets) && !fragmented)
                {
                    await output.DisposeAsync().ConfigureAwait(false);
                    _ = NormalizePesCounterResets ? await TransportContinuity.NormalizePesResetsAsync(path, ct).ConfigureAwait(false) : await TransportContinuity.NormalizeAsync(path, ct).ConfigureAwait(false);
                }
                if (joins)
                {
                    var duration=(decimal)(periodDurations?[index]??periods[index].Sum(segment=>segment.DurationSeconds!.Value));
                    var leading=periods[index][0].TrimStartSeconds;var trailing=periods[index][^1].TrimEndSeconds;
                    if(periods[index].Skip(1).Any(s=>s.TrimStartSeconds!=0)||periods[index].SkipLast(1).Any(s=>s.TrimEndSeconds!=0)||leading<0||trailing<0)
                        throw new InvalidDataException("A media period contains an internal clipping boundary.");
                    edits.Add(new(duration+leading+trailing,leading,duration));files.Add((path,(double)(duration+leading+trailing)));
                }
            }
            IReadOnlyList<decimal> starts=[];
            if(TransitionPolicy is {NormalizeTransitions:true} transition)
            {
                if(!fragmented)throw new NotSupportedException("DASH transition conversion requires clear fragmented MP4 periods.");
                starts=await FfmpegTool.JoinConvertedPeriodsAsync(ffmpeg!,files,edits,temporary,transition,RetainedMedia,ct).ConfigureAwait(false);
            }
            else if(clipped)
            {
                if(!fragmented)throw new NotSupportedException("Period clipping requires clear MP4 tracks.");
                starts=await FfmpegTool.JoinClippedPeriodsAsync(ffmpeg!,files,edits,temporary,RetainedMedia,ct).ConfigureAwait(false);
            }
            else if(joins)starts=await FfmpegTool.JoinHlsPeriodsAsync(ffmpeg!,files,temporary,fragmented,ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
            if(!RetainCheckpointAfterTransfer)checkpoint?.Complete();
            return new(starts);
        }
        finally
        {
            foreach (var key in keys.Values) CryptographicOperations.ZeroMemory(key);
            try { foreach (var file in Directory.EnumerateFiles(staging)) File.Delete(file); Directory.Delete(staging); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private sealed record Fragment(Uri Url, HlsByteRange? Range, HlsEncryption? Encryption, ulong Sequence,byte[]? InlineData=null);
    private async Task AppendResourceAsync(string path, HlsEncryption? encryption, ulong sequence,
        Stream output, Dictionary<HlsEncryption, byte[]> keys, CancellationToken ct)
    {
        await using var input = File.OpenRead(path);
        if (encryption is null) await input.CopyToAsync(output, ct).ConfigureAwait(false);
        else
        {
            if (!keys.TryGetValue(encryption, out var key))
            {
                key = await _http.GetBytesAsync(encryption.KeyUrl, 16, ct).ConfigureAwait(false);
                if (key.Length != 16) throw new InvalidDataException("The HLS AES-128 key must contain exactly 16 bytes.");
                keys[encryption] = key;
            }
            var iv = encryption.IV ?? new byte[16];
            if (encryption.IV is null) BinaryPrimitives.WriteUInt64BigEndian(iv.AsSpan(8), sequence);
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var decryptor = aes.CreateDecryptor();
            await using var decrypted = new CryptoStream(input, decryptor, CryptoStreamMode.Read, leaveOpen: true);
            await decrypted.CopyToAsync(output, ct).ConfigureAwait(false);
        }
        input.Close();
        File.Delete(path);
    }

    private async Task<IndexedDashDownloader.ResourceVersion> DownloadRangeAsync(Uri url, HlsByteRange range, string path, ByteRateLimiter? limiter, int retries, CancellationToken ct)
    {
        await using var destination = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 65536, useAsync: true);
        var version=await IndexedDashDownloader.CopyRangeAsync(_http, url, range, destination, null, limiter, retries, 512L * 1024 * 1024,
            ct, BufferSize, NoResizeBuffer, HttpChunkSize).ConfigureAwait(false);
        await destination.FlushAsync(ct).ConfigureAwait(false);destination.Flush(true);return version;
    }
}

