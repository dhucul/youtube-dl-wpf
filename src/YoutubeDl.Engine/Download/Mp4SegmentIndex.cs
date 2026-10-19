using System.Buffers.Binary;
using System.Text;

namespace YoutubeDl.Engine.Download;

public sealed record Mp4IndexedSegment(HlsByteRange Range, ulong StartTime, uint Duration);
public sealed record Mp4SegmentIndex(uint TrackId, uint Timescale, ulong EarliestTime, IReadOnlyList<Mp4IndexedSegment> Segments);

/// <summary>Reads a bounded ISO-BMFF sidx range without trusting offsets, box sizes or reference counts.</summary>
public static class Mp4SegmentIndexParser
{
    public const int MaximumIndexBytes = 1024 * 1024;
    public static Mp4SegmentIndex Parse(ReadOnlySpan<byte> data, long absoluteOffset, uint? expectedTimescale = null)
    {
        if (absoluteOffset < 0 || data.Length is < 8 or > MaximumIndexBytes) throw new InvalidDataException("The DASH index range is invalid or excessive.");
        try
        {
            Mp4SegmentIndex? result = null;
            var position = 0;
            while (position < data.Length)
            {
                if (data.Length - position < 8) throw new InvalidDataException("The MP4 index contains a truncated box header.");
                var header = 8;
                ulong size = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
                var type = Encoding.ASCII.GetString(data.Slice(position + 4, 4));
                if (size == 1)
                {
                    if (data.Length - position < 16) throw new InvalidDataException("The MP4 index has an incomplete extended box size.");
                    size = BinaryPrimitives.ReadUInt64BigEndian(data[(position + 8)..]); header = 16;
                }
                if (size < (ulong)header || size > (ulong)(data.Length - position)) throw new InvalidDataException("The MP4 index box exceeds its supplied range.");
                if (type == "sidx")
                {
                    if (result is not null) throw new NotSupportedException("Multiple segment indexes in one DASH range remain unsupported.");
                    result = ReadIndex(data.Slice(position + header, (int)size - header), checked(absoluteOffset + position + (long)size), expectedTimescale);
                }
                else if (type is not ("free" or "skip" or "styp")) throw new NotSupportedException("The DASH index range contains an unsupported MP4 box: " + type);
                position += (int)size;
            }
            return result ?? throw new InvalidDataException("The DASH range contains no MP4 segment index.");
        }
        catch (OverflowException ex) { throw new InvalidDataException("The DASH index contains overflowing byte or time offsets.", ex); }
    }

    private static Mp4SegmentIndex ReadIndex(ReadOnlySpan<byte> data, long boxEnd, uint? expectedTimescale)
    {
        var reader = new Reader(data);
        var versionFlags = reader.UInt32();
        var version = versionFlags >> 24;
        if (version > 1) throw new NotSupportedException("This MP4 segment index version is unsupported.");
        if ((versionFlags & 0xffffff) != 0) throw new InvalidDataException("The MP4 segment index has unexpected flags.");
        var track = reader.UInt32(); var timescale = reader.UInt32();
        if (timescale == 0 || expectedTimescale is { } expected && expected != timescale) throw new InvalidDataException("The DASH index timescale is zero or differs from the manifest.");
        var earliest = version == 0 ? reader.UInt32() : reader.UInt64();
        var firstOffset = version == 0 ? reader.UInt32() : reader.UInt64();
        var reservedCount = reader.UInt32();
        if ((reservedCount >> 16) != 0 || (reservedCount & 0xffff) == 0) throw new InvalidDataException("The DASH index has invalid reserved bits or no media references.");
        var count = (int)(reservedCount & 0xffff);
        if (reader.Remaining != count * 12) throw new InvalidDataException("The DASH index reference count does not match its box length.");
        var offset = checked(boxEnd + checked((long)firstOffset));
        var time = earliest;
        var result = new List<Mp4IndexedSegment>(count);
        for (var index = 0; index < count; index++)
        {
            var reference = reader.UInt32(); var duration = reader.UInt32(); reader.UInt32(); // SAP describes random-access points; full downloads retain every segment.
            if ((reference & 0x80000000) != 0) throw new NotSupportedException("Nested DASH segment indexes remain unsupported.");
            var size = reference & 0x7fffffff;
            if (size == 0 || duration == 0) throw new InvalidDataException("The DASH index contains an empty media reference.");
            result.Add(new(new(offset, size), time, duration));
            offset = checked(offset + size); time = checked(time + duration);
        }
        return new(track, timescale, earliest, result);
    }

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private ReadOnlySpan<byte> _remaining = data;
        public int Remaining => _remaining.Length;
        public uint UInt32() { Require(4); var value = BinaryPrimitives.ReadUInt32BigEndian(_remaining); _remaining = _remaining[4..]; return value; }
        public ulong UInt64() { Require(8); var value = BinaryPrimitives.ReadUInt64BigEndian(_remaining); _remaining = _remaining[8..]; return value; }
        private void Require(int count) { if (_remaining.Length < count) throw new InvalidDataException("The MP4 segment index is truncated."); }
    }
}
