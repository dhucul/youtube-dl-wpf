using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Providers.Facebook;

/// <summary>Reads only bounded MP4 box headers and movie metadata, never executes a media helper.</summary>
internal static class FacebookMp4Probe
{
    private const int MaximumMovieBytes = 8 * 1024 * 1024;
    internal sealed record Facts(int Width, int Height, string VideoCodec, double? FramesPerSecond, string? AudioCodec, long? FileSize)
    {
        internal bool HasAudio { get; init; }
    }
    private readonly record struct Box(string Type, int Data, int End);

    internal static async Task<Facts> ReadAsync(MediaHttp http, Uri url, Uri referer, CancellationToken ct)
    {
        long? fileSize = null; string? entity = null; var current = url; var redirects = 0; long readBytes = 0;
        async Task<byte[]> Range(long offset, int count)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.Range = new RangeHeaderValue(offset, checked(offset + count - 1)); request.Headers.Referrer = referer;
                request.Headers.AcceptEncoding.ParseAdd("identity");
                using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    var location = response.Headers.Location; var next = location is null ? null : location.IsAbsoluteUri ? location : new Uri(current, location);
                    if (next is null || !FacebookProvider.MediaHost(next) || ++redirects > 5) throw new InvalidDataException("Facebook redirected its movie metadata outside the permitted media origin.");
                    current = next; continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent)) throw new InvalidDataException("Facebook did not return movie metadata bytes.");
                var range = response.Content.Headers.ContentRange;
                var size = response.StatusCode == HttpStatusCode.PartialContent ? range?.Length : response.Content.Headers.ContentLength;
                if (size is <= 0 || fileSize is not null && size is not null && fileSize != size) throw new InvalidDataException("Facebook changed the movie size during metadata inspection.");
                fileSize ??= size;
                var tag = response.Headers.ETag?.ToString();
                if (entity is not null && tag is not null && entity != tag) throw new InvalidDataException("Facebook changed the movie entity during metadata inspection.");
                entity ??= tag;
                var skip = response.StatusCode == HttpStatusCode.OK ? offset : 0;
                if (response.StatusCode == HttpStatusCode.PartialContent && (range?.Unit != "bytes" || range.From != offset || range.To is not { } last || last - offset + 1 != count || response.Content.Headers.ContentLength is { } length && length != count))
                    throw new InvalidDataException("Facebook returned another movie metadata range.");
                if (skip > MaximumMovieBytes || readBytes + skip + count > MaximumMovieBytes + 65536) throw new InvalidDataException("Facebook movie metadata exceeds its byte budget.");
                readBytes += skip + count;
                await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var discard = new byte[8192];
                while (skip > 0)
                {
                    var received = await http.ReadAsync(input, discard.AsMemory(0, (int)Math.Min(skip, discard.Length)), ct).ConfigureAwait(false);
                    if (received == 0) throw new InvalidDataException("Facebook returned truncated movie metadata."); skip -= received;
                }
                var bytes = new byte[count]; var filled = 0;
                while (filled < bytes.Length)
                {
                    var received = await http.ReadAsync(input, bytes.AsMemory(filled), ct).ConfigureAwait(false);
                    if (received == 0) throw new InvalidDataException("Facebook returned truncated movie metadata."); filled += received;
                }
                return bytes;
            }
        }
        long position = 0;
        for (var index = 0; index < 64; index++)
        {
            var header = await Range(position, 8).ConfigureAwait(false); var type = Encoding.ASCII.GetString(header, 4, 4);
            if (index == 0 && type != "ftyp") throw new InvalidDataException("Facebook did not return an MP4 movie.");
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(header); var headerSize = 8;
            if (size == 1) { size = BinaryPrimitives.ReadUInt64BigEndian(await Range(checked(position + 8), 8).ConfigureAwait(false)); headerSize = 16; }
            if (size == 0 && fileSize is { } total) size = (ulong)(total - position);
            if (size < (ulong)headerSize || size > long.MaxValue || position > long.MaxValue - (long)size || fileSize is { } known && (long)size > known - position)
                throw new InvalidDataException("Facebook returned invalid MP4 box boundaries.");
            if (type == "moov")
            {
                if (size <= (ulong)headerSize || size > MaximumMovieBytes) throw new InvalidDataException("Facebook's movie metadata is empty or exceeds its size limit.");
                var movie = await Range(checked(position + headerSize), (int)size - headerSize).ConfigureAwait(false);
                return Parse(movie, fileSize);
            }
            position += (long)size;
        }
        throw new InvalidDataException("Facebook did not offer a bounded MP4 movie header.");
    }

    internal static Facts Parse(byte[] movie, long? fileSize = null)
    {
        if (movie.Length is 0 or > MaximumMovieBytes) throw new InvalidDataException("Facebook's movie metadata is empty or excessive.");
        var tracks = Boxes(movie, 0, movie.Length).Where(box => box.Type == "trak").ToArray();
        if (tracks.Length is 0 or > 16) throw new InvalidDataException("Facebook returned an invalid movie track inventory.");
        Facts? video = null; var audio = new HashSet<string>(StringComparer.Ordinal);
        foreach (var track in tracks)
        {
            var media = One(Boxes(movie, track.Data, track.End), "mdia"); var children = Boxes(movie, media.Data, media.End);
            var handler = One(children, "hdlr"); Require(handler, 12); var role = Encoding.ASCII.GetString(movie, handler.Data + 8, 4);
            if (role is not ("vide" or "soun")) continue;
            var information = One(children, "minf"); var table = One(Boxes(movie, information.Data, information.End), "stbl"); var rows = Boxes(movie, table.Data, table.End);
            var description = One(rows, "stsd"); Require(description, 8);
            if (BinaryPrimitives.ReadUInt32BigEndian(movie.AsSpan(description.Data + 4)) != 1) throw new NotSupportedException("Facebook's movie has ambiguous sample descriptions.");
            var entries = Boxes(movie, description.Data + 8, description.End);
            if (entries.Count != 1 || entries[0].Type is "encv" or "enca") throw new NotSupportedException("Facebook did not offer one clear movie sample description.");
            var entry = entries[0];
            if (role == "soun") { audio.Add(entry.Type); continue; }
            if (video is not null || entry.Type is not ("avc1" or "avc3" or "hvc1" or "hev1" or "av01" or "vp09")) throw new NotSupportedException("Facebook did not offer one supported clear video track.");
            Require(entry, 78); var width = BinaryPrimitives.ReadUInt16BigEndian(movie.AsSpan(entry.Data + 24)); var height = BinaryPrimitives.ReadUInt16BigEndian(movie.AsSpan(entry.Data + 26));
            if (width == 0 || height == 0) throw new InvalidDataException("Facebook omitted the movie's video dimensions.");
            var clock = One(children, "mdhd"); Require(clock, 4); var version = movie[clock.Data];
            if (version > 1) throw new NotSupportedException("Facebook returned an unsupported MP4 media clock.");
            Require(clock, version == 1 ? 32 : 20); var scale = BinaryPrimitives.ReadUInt32BigEndian(movie.AsSpan(clock.Data + (version == 1 ? 20 : 12)));
            if (scale == 0) throw new InvalidDataException("Facebook returned an empty MP4 media time base.");
            var timing = One(rows, "stts"); Require(timing, 8); var count = BinaryPrimitives.ReadUInt32BigEndian(movie.AsSpan(timing.Data + 4));
            if (count > 100000 || timing.End - timing.Data != 8L + 8L * count) throw new InvalidDataException("Facebook returned an invalid video timing table.");
            uint minimum = uint.MaxValue;
            for (var row = 0; row < count; row++)
            {
                var offset = timing.Data + 8 + row * 8; var samples = BinaryPrimitives.ReadUInt32BigEndian(movie.AsSpan(offset)); var delta = BinaryPrimitives.ReadUInt32BigEndian(movie.AsSpan(offset + 4));
                if (samples == 0 || delta == 0) throw new InvalidDataException("Facebook returned empty video samples or timing."); minimum = Math.Min(minimum, delta);
            }
            double? fps = count == 0 ? null : scale / (double)minimum;
            if (fps is > 1000) throw new InvalidDataException("Facebook returned an unsupported movie frame rate.");
            video = new(width, height, entry.Type, fps, null, fileSize);
        }
        if (video is null) throw new InvalidDataException("Facebook's progressive movie omitted video.");
        return video with { HasAudio = audio.Count > 0, AudioCodec = audio.Count == 1 ? audio.Single() : null };
    }
    private static Box One(List<Box> boxes, string type)
    {
        var matches = boxes.Where(box => box.Type == type).ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidDataException("Facebook's movie omitted or repeated its " + type + " box.");
    }
    private static void Require(Box box, int count) { if (box.End - box.Data < count) throw new InvalidDataException("Facebook returned a truncated movie metadata field."); }
    private static List<Box> Boxes(byte[] bytes, int start, int end)
    {
        var boxes = new List<Box>();
        while (start < end)
        {
            if (end - start < 8 || boxes.Count >= 1024) throw new InvalidDataException("Facebook returned truncated or excessive movie metadata boxes.");
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(start)); var header = 8; var type = Encoding.ASCII.GetString(bytes, start + 4, 4);
            if (size == 1) { if (end - start < 16) throw new InvalidDataException("Facebook returned a truncated extended movie box."); size = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(start + 8)); header = 16; }
            if (size == 0) size = (ulong)(end - start);
            if (size < (ulong)header || size > (ulong)(end - start)) throw new InvalidDataException("Facebook returned invalid movie metadata boundaries.");
            boxes.Add(new(type, start + header, start + (int)size)); start += (int)size;
        }
        return boxes;
    }
}
