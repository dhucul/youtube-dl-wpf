using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Media;

internal sealed class HlsSubtitleResolver
{
    private readonly List<SubtitleTrack> _tracks;
    private readonly IReadOnlyList<SubtitleTrack> _selected;
    private readonly Dictionary<SubtitleTrack, HlsSubtitleRendition> _renditions;
    private HlsSubtitleResolver(List<SubtitleTrack> tracks, IReadOnlyList<SubtitleTrack> selected, Dictionary<SubtitleTrack, HlsSubtitleRendition> renditions)
    { _tracks = tracks; _selected = selected; _renditions = renditions; }

    public static HlsSubtitleResolver Prepare(HlsPlaylist master, HlsVariant? variant, IReadOnlyList<SubtitleTrack> existing, NativeDownloadRequest request)
    {
        var tracks = existing.ToList(); var renditions = new Dictionary<SubtitleTrack, HlsSubtitleRendition>();
        if (variant is not null)
        {
            foreach (var group in new[] { (variant.SubtitleGroup, false), (variant.ClosedCaptionGroup, true) })
            {
                if (group.Item1 is null) continue;
                var offered = master.SubtitleRenditions.Where(item => item.Group == group.Item1 && item.InBand == group.Item2).OrderBy(item => item.Forced).ThenByDescending(item => item.Default).ToArray();
                if (offered.Length == 0) throw new InvalidDataException("The selected HLS variant refers to a missing subtitle group.");
                foreach (var rendition in offered)
                {
                    var track = new SubtitleTrack { Url = rendition.Url ?? variant.Url, Language = rendition.Language ?? "und", Extension = "vtt" };
                    tracks.Add(track); renditions.Add(track, rendition);
                }
            }
        }
        var selected = SubtitleSelection.Select(tracks, request).Where(renditions.ContainsKey).ToArray();
        SubtitleFormats.ValidateSelection(request, selected);
        if (selected.Any(track => renditions[track].InBand)) throw new NotSupportedException("Native extraction of in-band HLS closed captions remains pending.");
        if (selected.Length > 0) FfmpegTool.RequireProbe(request.FfmpegPath);
        return new(tracks, selected, renditions);
    }

    public async Task<IReadOnlyList<SubtitleTrack>> ResolveAsync(MediaHttp http, string timingMedia, HlsPlaylist mediaPlaylist, NativeDownloadRequest request, CancellationToken ct)
    {
        if (_selected.Count == 0) return _tracks;
        if (mediaPlaylist.MediaSegments.Select(segment => segment.DiscontinuitySequence).Distinct().Count() > 1)
            throw new NotSupportedException("HLS subtitle timestamp mapping across timeline changes remains pending; use an exposed full caption document when available.");
        var mediaDurations = mediaPlaylist.MediaSegments.Select(segment => segment.DurationSeconds).ToArray();
        if (mediaDurations.Length == 0 || mediaDurations.Any(value => value is null or <= 0) || !double.IsFinite(mediaDurations.Sum(value => value!.Value)))
            throw new InvalidDataException("HLS subtitle synchronization requires a finite media duration.");
        var duration = mediaDurations.Sum(value => (decimal)value!.Value);
        var origin = await FfmpegTool.ReadStartTimeAsync(request.FfmpegPath!, timingMedia, ct).ConfigureAwait(false);
        foreach (var track in _selected)
        {
            ct.ThrowIfCancellationRequested();
            await NativeSubtitleDelay.WaitAsync(request, ct).ConfigureAwait(false);
            var playlist = await new HlsDownloader(http).ReadAsync(_renditions[track].Url!, ct).ConfigureAwait(false);
            if (playlist.IsMaster || !playlist.IsComplete || playlist.UnsupportedFeatures.Count > 0)
                throw new NotSupportedException("The HLS subtitle rendition must be a complete supported media playlist.");
            if (playlist.MediaSegments.Select(segment => segment.DiscontinuitySequence).Distinct().Count() > 1) throw new NotSupportedException("Discontinuous HLS subtitle renditions remain pending.");
            if (playlist.MediaSegments.Count is 0 or > 10000) throw new InvalidDataException("The HLS subtitle segment count is empty or excessive.");
            var keys = new Dictionary<HlsEncryption, byte[]>(ReferenceEqualityComparer.Instance);
            var headers = new Dictionary<HlsInitialization, string>(ReferenceEqualityComparer.Instance);
            var versions = new Dictionary<Uri, IndexedDashDownloader.ResourceVersion>();
            long bytesRead = 0;
            try
            {
                async Task<byte[]> Load(Uri url, HlsByteRange? range, HlsEncryption? encryption, ulong sequence)
                {
                    var remaining = 8 * 1024 * 1024 - bytesRead;
                    if (remaining <= 0) throw new InvalidDataException("The HLS caption exceeds its input limit.");
                    byte[] bytes;
                    if (range is null) bytes = await http.GetBytesAsync(url, remaining, ct).ConfigureAwait(false);
                    else
                    {
                        using var buffer = new MemoryStream();
                        versions[url] = await IndexedDashDownloader.CopyRangeAsync(http, url, range, buffer, versions.GetValueOrDefault(url), null, request.FragmentRetries ?? request.Retries, remaining, ct, request.BufferSize, request.NoResizeBuffer, request.HttpChunkSize).ConfigureAwait(false);
                        bytes = buffer.ToArray();
                    }
                    bytesRead += bytes.Length;
                    if (encryption is null) return bytes;
                    if (!keys.TryGetValue(encryption, out var key))
                    {
                        key = await http.GetBytesAsync(encryption.KeyUrl, 16, ct).ConfigureAwait(false);
                        if (key.Length != 16) throw new InvalidDataException("The HLS caption key has an invalid length.");
                        keys.Add(encryption, key);
                    }
                    var iv = encryption.IV ?? new byte[16];
                    if (encryption.IV is null) BinaryPrimitives.WriteUInt64BigEndian(iv.AsSpan(8), sequence);
                    using var aes = Aes.Create(); aes.Key = key;
                    return aes.DecryptCbc(bytes, iv, PaddingMode.PKCS7);
                }
                var documents = new List<HlsWebVttSegment>(); decimal start = 0;
                foreach (var segment in playlist.MediaSegments)
                {
                    if (segment.DurationSeconds is not > 0 or >= 40000) throw new NotSupportedException("The HLS caption segment duration is missing or excessive.");
                    string? header = null;
                    if (segment.Initialization is { } initialization)
                    {
                        if (!headers.TryGetValue(initialization, out header))
                        {
                            header = new UTF8Encoding(false, true).GetString(await Load(initialization.Url, initialization.Range, initialization.Encryption, segment.Sequence).ConfigureAwait(false));
                            headers.Add(initialization, header);
                        }
                    }
                    var text = new UTF8Encoding(false, true).GetString(await Load(segment.Url, segment.Range, segment.Encryption, segment.Sequence).ConfigureAwait(false));
                    var length = (decimal)segment.DurationSeconds.Value;
                    documents.Add(new(text, start, length, header)); start += length;
                }
                var result = HlsWebVttReader.Join(documents, origin, duration, ct);
                _tracks[_tracks.IndexOf(track)] = new() { Url = track.Url, Language = track.Language, Extension = "vtt", Content = result };
            }
            finally { foreach (var key in keys.Values) CryptographicOperations.ZeroMemory(key); }
        }
        return _tracks;
    }
    internal HlsLiveCaptionCapture Live(MediaHttp http,NativeDownloadRequest request,ByteRateLimiter limiter,string? destination=null)=>
        new(http,_tracks,_selected.Select(track=>(track,_renditions[track].Url!)),request,limiter,destination);
}
