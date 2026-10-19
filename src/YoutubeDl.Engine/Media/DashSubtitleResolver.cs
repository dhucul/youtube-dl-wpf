using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Media;

internal static class DashSubtitleResolver
{
    public static async Task<IReadOnlyList<SubtitleTrack>> ResolveAsync(DashManifest manifest, IReadOnlyList<SubtitleTrack> existing,
        NativeDownloadRequest request, MediaHttp http, string workspace, CancellationToken ct)
    {
        var tracks = existing.ToList();
        var descriptors = new Dictionary<SubtitleTrack, DashCaption>();
        foreach (var caption in manifest.CaptionTracks)
        {
            var track = new SubtitleTrack { Url = caption.Representation.Url, Language = caption.Language, Extension = caption.Extension };
            tracks.Add(track); descriptors.Add(track, caption);
        }
        var selected = SubtitleSelection.Select(tracks, request);
        SubtitleFormats.ValidateSelection(request, selected);
        foreach (var track in selected)
        {
            ct.ThrowIfCancellationRequested();
            if (!descriptors.TryGetValue(track, out var caption)) continue;
            if (caption.UnavailableReason is { } reason) throw new NotSupportedException(reason);
            var ttml = Mp4TtmlReader.IsSupportedCodec(caption.Representation.Codecs);
            if (caption.Representation.Codecs != "wvtt" && !ttml) continue;
            await NativeSubtitleDelay.WaitAsync(request, ct).ConfigureAwait(false);
            if (caption.Representation.Segments.Count > 0)
            {
                long received = 0;
                var versions = new Dictionary<Uri, IndexedDashDownloader.ResourceVersion>();
                async Task<byte[]> Load(Uri url, HlsByteRange? range, CancellationToken token)
                {
                    var remaining = Mp4WebVttReader.MaximumInputBytes - Interlocked.Read(ref received);
                    if (remaining <= 0) throw new InvalidDataException("The DASH caption exceeds its input limit.");
                    byte[] part;
                    if (range is null)
                    {
                        using var message = new HttpRequestMessage(HttpMethod.Get, url);
                        using var response = await http.SendAsync(message, token).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength > remaining) throw new InvalidDataException("The DASH caption exceeds its input limit.");
                        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                        using var memory = new MemoryStream(); var buffer = new byte[8192];
                        while (true)
                        {
                            var count = await http.ReadAsync(input, buffer, token).ConfigureAwait(false);
                            if (count == 0) break;
                            if (Interlocked.Add(ref received, count) > Mp4WebVttReader.MaximumInputBytes) throw new InvalidDataException("The DASH caption exceeds its aggregate input limit.");
                            memory.Write(buffer, 0, count);
                        }
                        if (response.Content.Headers.ContentLength is { } expected && memory.Length != expected) throw new EndOfStreamException("The DASH caption resource was incomplete.");
                        return memory.ToArray();
                    }
                    else
                    {
                        using var buffer = new MemoryStream();
                        versions[url] = await IndexedDashDownloader.CopyRangeAsync(http, url, range, buffer, versions.GetValueOrDefault(url), null, request.FragmentRetries ?? request.Retries, remaining, token, request.BufferSize, request.NoResizeBuffer, request.HttpChunkSize).ConfigureAwait(false);
                        part = buffer.ToArray();
                    }
                    if (Interlocked.Add(ref received, part.Length) > Mp4WebVttReader.MaximumInputBytes) throw new InvalidDataException("The DASH caption exceeds its aggregate input limit.");
                    return part;
                }
                var initialization = await Load(caption.Representation.Initialization!, caption.Representation.InitializationRange, ct).ConfigureAwait(false);
                Mp4WebVttReader.ValidateInitialization(initialization, ttml, ct);
                var segments = new List<Mp4WebVttSegment>();
                if (caption.Representation.Segments.Any(segment => segment.Range is not null))
                {
                    // Ranges of a shared entity retain their ordered validator state.
                    foreach (var segment in caption.Representation.Segments)
                        segments.Add(new(await Load(segment.Url, segment.Range, ct).ConfigureAwait(false), segment.Range?.Offset ?? 0));
                }
                else
                {
                    var buffered = new byte[]?[caption.Representation.Segments.Count];
                    await OrderedFragmentTransfer.RunAsync(caption.Representation.Segments, request.ConcurrentFragments,
                        async (segment, index, token) => buffered[index] = await Load(segment.Url, null, token).ConfigureAwait(false),
                        (segment, index, token) => { segments.Add(new(buffered[index]!)); buffered[index] = null; return Task.CompletedTask; }, ct).ConfigureAwait(false);
                }
                var converted = ttml ? Mp4TtmlReader.ReadSegments(initialization, segments, caption.DurationSeconds, caption.OffsetSeconds, ct) :
                    Mp4WebVttReader.ReadSegments(initialization, segments, caption.DurationSeconds, caption.OffsetSeconds, ct);
                tracks[tracks.IndexOf(track)] = new() { Url = track.Url, Language = track.Language, Extension = "vtt", Content = converted };
                continue;
            }
            byte[] bytes;
            if (caption.Representation.SegmentBase is not null)
            {
                var temporary = Path.Combine(workspace, ".dash-caption-" + Guid.NewGuid().ToString("N") + ".mp4");
                try
                {
                    await IndexedDashDownloader.DownloadAsync(http, caption.Representation, temporary, null, null, request.FragmentRetries ?? request.Retries, ct,
                        Mp4WebVttReader.MaximumInputBytes, request.ConcurrentFragments).ConfigureAwait(false);
                    bytes = await File.ReadAllBytesAsync(temporary, ct).ConfigureAwait(false);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            else bytes = await http.GetBytesAsync(track.Url, Mp4WebVttReader.MaximumInputBytes, ct).ConfigureAwait(false);
            var text = ttml ? Mp4TtmlReader.Read(bytes, caption.DurationSeconds, caption.OffsetSeconds, ct) : Mp4WebVttReader.Read(bytes, caption.DurationSeconds, caption.OffsetSeconds, ct);
            tracks[tracks.IndexOf(track)] = new() { Url = track.Url, Language = track.Language, Extension = "vtt", Content = text };
        }
        return tracks;
    }
}
