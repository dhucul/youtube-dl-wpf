using System.Buffers.Binary;
using System.Text;

namespace YoutubeDl.Engine.Media;

/// <summary>Identifies QuickTime chapter tracks by their actual movie track references.</summary>
internal static class Mp4ChapterReferences
{
    private const int Limit = 8 * 1024 * 1024;
    private sealed record Box(string Type, int Data, int End);
    internal static async Task<HashSet<uint>> ReadAsync(string path, CancellationToken ct) => Parse(await ReadMovieAsync(path, ct).ConfigureAwait(false));
    internal static async Task<byte[]> ReadMovieAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        var header = new byte[16]; byte[]? movie = null; var count = 0;
        while (stream.Position < stream.Length)
        {
            ct.ThrowIfCancellationRequested();
            if (++count > 4096 || stream.Length - stream.Position < 8) throw new InvalidDataException("The MP4 chapter file has an excessive or truncated box inventory.");
            var start = stream.Position; await stream.ReadExactlyAsync(header.AsMemory(0, 8), ct).ConfigureAwait(false);
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(header); var headerSize = 8;
            if (size == 1) { await stream.ReadExactlyAsync(header.AsMemory(8, 8), ct).ConfigureAwait(false); size = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8)); headerSize = 16; }
            if (size == 0) size = (ulong)(stream.Length - start);
            if (size < (uint)headerSize || size > (ulong)(stream.Length - start)) throw new InvalidDataException("The MP4 chapter file contains an invalid box size.");
            if (Encoding.ASCII.GetString(header, 4, 4) == "moov")
            {
                if (movie is not null || size - (uint)headerSize > Limit) throw new InvalidDataException("The MP4 chapter movie header is duplicate or excessive.");
                movie = new byte[checked((int)size - headerSize)]; await stream.ReadExactlyAsync(movie, ct).ConfigureAwait(false);
            }
            stream.Position = start + (long)size;
        }
        return movie ?? throw new InvalidDataException("The MP4 chapter file has no movie header.");
    }
    internal static IReadOnlyDictionary<uint, string> ParseTrackHandlers(byte[] movie)
    {
        if (movie.Length > Limit) throw new InvalidDataException("The MP4 movie header exceeds its size limit.");
        var handlers = new Dictionary<uint, string>();
        foreach (var track in Boxes(movie, 0, movie.Length).Where(box => box.Type == "trak"))
        {
            if (handlers.Count >= 256) throw new InvalidDataException("The MP4 movie has too many tracks.");
            var children = Boxes(movie, track.Data, track.End); var headers = children.Where(box => box.Type == "tkhd").ToArray(); var media = children.Where(box => box.Type == "mdia").ToArray();
            if (headers.Length != 1 || media.Length != 1) throw new InvalidDataException("An MP4 track has no unique identity/media header.");
            var header = headers[0]; if (header.Data >= header.End || movie[header.Data] is not (0 or 1)) throw new InvalidDataException("The MP4 track header version is unsupported.");
            var offset = header.Data + (movie[header.Data] == 1 ? 20 : 12); if (offset + 4 > header.End) throw new InvalidDataException("The MP4 track ID is truncated.");
            var id = BinaryPrimitives.ReadUInt32BigEndian(movie.AsSpan(offset));
            var types = Boxes(movie, media[0].Data, media[0].End).Where(box => box.Type == "hdlr").ToArray();
            if (types.Length != 1 || types[0].Data + 12 > types[0].End || movie[types[0].Data] != 0) throw new InvalidDataException("An MP4 track has no supported unique media handler.");
            if (id == 0 || !handlers.TryAdd(id, Encoding.ASCII.GetString(movie, types[0].Data + 8, 4))) throw new InvalidDataException("MP4 track IDs must be unique and nonzero.");
        }
        return handlers;
    }
    internal static bool IsLegacyAncillary(string codecTag, string handler) => codecTag == "rtp " && handler == "hint" || codecTag == "mp4s" && handler is "odsm" or "sdsm";
    internal static HashSet<uint> Parse(byte[] movie)
    {
        if (movie.Length > Limit) throw new InvalidDataException("The MP4 chapter movie header exceeds its size limit.");
        var tracks = new HashSet<uint>(); var references = new HashSet<uint>();
        foreach (var track in Boxes(movie, 0, movie.Length).Where(box => box.Type == "trak"))
        {
            if (tracks.Count >= 256) throw new InvalidDataException("The MP4 chapter movie has too many tracks.");
            var children = Boxes(movie, track.Data, track.End); var headers = children.Where(box => box.Type == "tkhd").ToArray();
            if (headers.Length != 1) throw new InvalidDataException("An MP4 track has no unique track header.");
            var tkhd = headers[0];
            if (tkhd.Data >= tkhd.End || movie[tkhd.Data] is not (0 or 1)) throw new InvalidDataException("The MP4 track header version is unsupported.");
            var idPosition = tkhd.Data + (movie[tkhd.Data] == 1 ? 20 : 12);
            if (idPosition + 4 > tkhd.End) throw new InvalidDataException("The MP4 track identity is truncated.");
            var id = BinaryPrimitives.ReadUInt32BigEndian(movie.AsSpan(idPosition));
            if (id == 0 || !tracks.Add(id)) throw new InvalidDataException("MP4 track identities must be unique and nonzero.");
            foreach (var tref in children.Where(box => box.Type == "tref"))
                foreach (var chapter in Boxes(movie, tref.Data, tref.End).Where(box => box.Type == "chap"))
                {
                    if ((chapter.End - chapter.Data) % 4 != 0 || chapter.End - chapter.Data > 1024) throw new InvalidDataException("The MP4 chapter references are malformed or excessive.");
                    for (var offset = chapter.Data; offset < chapter.End; offset += 4) references.Add(BinaryPrimitives.ReadUInt32BigEndian(movie.AsSpan(offset)));
                }
        }
        if (references.Any(id => id == 0 || !tracks.Contains(id))) throw new InvalidDataException("The MP4 chapter references point to a missing track.");
        return references;
    }
    private static List<Box> Boxes(byte[] data, int start, int end)
    {
        var result = new List<Box>();
        while (start < end)
        {
            if (end - start < 8 || result.Count >= 4096) throw new InvalidDataException("The MP4 chapter header contains truncated or excessive child boxes.");
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(start)); var header = 8;
            if (size == 1) { if (end - start < 16) throw new InvalidDataException("The MP4 extended box header is truncated."); size = BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(start + 8)); header = 16; }
            if (size == 0) size = (ulong)(end - start);
            if (size < (uint)header || size > (ulong)(end - start)) throw new InvalidDataException("The MP4 chapter header contains an invalid child box size.");
            var finish = start + checked((int)size); result.Add(new(Encoding.ASCII.GetString(data, start + 4, 4), start + header, finish)); start = finish;
        }
        return result;
    }
}
