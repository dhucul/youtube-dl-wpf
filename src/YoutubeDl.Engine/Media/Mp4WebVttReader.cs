using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace YoutubeDl.Engine.Media;

public sealed record Mp4WebVttSegment(byte[] Data, long ResourceOffset = 0);

/// <summary>Converts a bounded fragmented ISO-BMFF wvtt track into a standalone WebVTT document.</summary>
public static class Mp4WebVttReader
{
    public const int MaximumInputBytes = 8 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private sealed record Box(string Type, int Start, int Data, int End);
    private sealed record Track(uint Id, uint Timescale, uint Duration, uint Size, string Configuration);
    private sealed record Cue(decimal Start, decimal End, string Id, string Settings, string Text, string OriginalTime);

    public static string Read(byte[] bytes, double? durationSeconds = null, double offsetSeconds = 0, CancellationToken ct = default) =>
        ReadSegments(bytes, [new(bytes)], durationSeconds, offsetSeconds, ct);

    public static string ReadSegments(byte[] initialization, IReadOnlyList<Mp4WebVttSegment> segments, double? durationSeconds = null, double offsetSeconds = 0, CancellationToken ct = default)
        => ReadSegmentsCore(initialization, segments, durationSeconds, offsetSeconds, ct, ttml: false);

    internal static string ReadTtmlSegments(byte[] initialization, IReadOnlyList<Mp4WebVttSegment> segments, double? durationSeconds, double offsetSeconds, CancellationToken ct)
        => ReadSegmentsCore(initialization, segments, durationSeconds, offsetSeconds, ct, ttml: true);

    internal static void ValidateInitialization(byte[] initialization, bool ttml, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (initialization.Length > MaximumInputBytes) throw new InvalidDataException("The MP4 caption initialization exceeds its size limit.");
        _ = ReadTrack(initialization, One(Boxes(initialization, 0, initialization.Length), "moov"), ttml);
    }

    private static string ReadSegmentsCore(byte[] initialization, IReadOnlyList<Mp4WebVttSegment> segments, double? durationSeconds, double offsetSeconds, CancellationToken ct, bool ttml)
    {
        var length = (long)initialization.Length + segments.Where(segment => !(segments.Count == 1 && ReferenceEquals(segment.Data, initialization))).Sum(segment => (long)segment.Data.Length);
        if (length > MaximumInputBytes || segments.Count > 10000 || segments.Any(segment => segment.ResourceOffset < 0) || !double.IsFinite(offsetSeconds) || offsetSeconds < 0 ||
            durationSeconds is { } duration && (!double.IsFinite(duration) || duration <= 0)) throw new InvalidDataException("The MP4 caption input or presentation window is invalid.");
        try
        {
            ct.ThrowIfCancellationRequested();
            var top = Boxes(initialization, 0, initialization.Length);
            var movie = One(top, "moov");
            var track = ReadTrack(initialization, movie, ttml);
            var cues = new List<Cue>(); var continuing = new Dictionary<(string, string, string, string), int>();
            var samples = 0;
            var textBudget = 0;
            ulong? previousDecodeEnd = null;
            foreach (var segment in segments)
            {
                var bytes = segment.Data;
                var boxesInSegment = Boxes(bytes, 0, bytes.Length);
                var media = boxesInSegment.Where(box => box.Type == "mdat").ToArray();
                var fragments = boxesInSegment.Where(box => box.Type == "moof").ToArray();
                if (fragments.Length == 0) throw new NotSupportedException("Non-fragmented MP4 caption sample tables remain unsupported.");
                foreach (var fragment in fragments)
                {
                    ct.ThrowIfCancellationRequested();
                    var trafs = Children(bytes, fragment).Where(box => box.Type == "traf").ToArray();
                    foreach (var traf in trafs)
                    {
                        var boxes = Children(bytes, traf);
                        var tfhd = new Reader(Payload(bytes, One(boxes, "tfhd")));
                        var flags = tfhd.U32(); var id = tfhd.U32();
                        if (id != track.Id) continue;
                        if ((flags & ~0x0003003bU) != 0 || boxes.Any(box => box.Type is "senc" or "saiz" or "saio"))
                            throw new NotSupportedException("Protected or unsupported MP4 caption fragments cannot be converted.");
                        if (ttml && boxes.Any(box => box.Type == "subs")) throw new NotSupportedException("TTML auxiliary subsamples require additional native support.");
                        var explicitBase = (flags & 1) != 0;
                        var baseOffset = explicitBase ? checked((long)tfhd.U64() - segment.ResourceOffset) : fragment.Start;
                        if (!explicitBase && (flags & 0x20000) == 0 && trafs.Length > 1)
                            throw new NotSupportedException("Implicit offsets across multiple MP4 caption tracks remain unsupported.");
                        if ((flags & 2) != 0 && tfhd.U32() != 1) throw new NotSupportedException("Changing MP4 caption sample descriptions remain unsupported.");
                        var defaultDuration = (flags & 8) != 0 ? tfhd.U32() : track.Duration;
                        var defaultSize = (flags & 16) != 0 ? tfhd.U32() : track.Size;
                        if ((flags & 32) != 0) tfhd.U32();
                        tfhd.End();
                        var tfdt = new Reader(Payload(bytes, One(boxes, "tfdt")));
                        var timeFlags = tfdt.U32();
                        if (timeFlags is not (0 or 0x01000000)) throw new NotSupportedException("Unsupported MP4 caption decode-time version.");
                        var decode = timeFlags == 0 ? tfdt.U32() : tfdt.U64(); tfdt.End();
                        if (previousDecodeEnd is { } previous && decode < previous) throw new InvalidDataException("MP4 caption fragments repeat or overlap their decode timeline.");
                        long? nextData = null;
                        var runs = boxes.Where(box => box.Type == "trun").ToArray();
                        if (runs.Length == 0) throw new InvalidDataException("The MP4 caption fragment has no sample run.");
                        foreach (var run in runs)
                        {
                            var reader = new Reader(Payload(bytes, run));
                            var runFlags = reader.U32(); var count = reader.U32();
                            var version = runFlags >> 24; var fields = runFlags & 0xffffff;
                            if (version > 1 || (fields & ~0xf05U) != 0) throw new NotSupportedException("Unsupported MP4 caption sample-run flags.");
                            if (count > 100000 - samples || (flags & 0x10000) != 0 && count != 0) throw new InvalidDataException("The MP4 caption sample count is invalid or excessive.");
                            var dataOffset = (fields & 1) != 0 ? checked(baseOffset + unchecked((int)reader.U32())) :
                                nextData ?? (explicitBase ? baseOffset : throw new NotSupportedException("The first MP4 caption run requires a data offset."));
                            if ((fields & 4) != 0) reader.U32();
                            for (var sample = 0U; sample < count; sample++)
                            {
                                ct.ThrowIfCancellationRequested(); samples++;
                                var sampleDuration = (fields & 0x100) != 0 ? reader.U32() : defaultDuration;
                                var size = (fields & 0x200) != 0 ? reader.U32() : defaultSize;
                                if ((fields & 0x400) != 0) reader.U32();
                                long composition = 0;
                                if ((fields & 0x800) != 0) { var value = reader.U32(); composition = version == 0 ? value : unchecked((int)value); }
                                if (sampleDuration == 0 || size == 0 || size > MaximumInputBytes || dataOffset < 0 || dataOffset > bytes.Length - size ||
                                    !media.Any(box => dataOffset >= box.Data && dataOffset + size <= box.End)) throw new InvalidDataException("The MP4 caption sample points outside media data or has no duration.");
                                var sampleStart = ((decimal)decode + composition) / track.Timescale;
                                var sampleEnd = sampleStart + (decimal)sampleDuration / track.Timescale;
                                var start = sampleStart - (decimal)offsetSeconds;
                                var end = sampleEnd - (decimal)offsetSeconds;
                                start = Math.Max(0, start);
                                if (durationSeconds is { } limit) end = Math.Min(end, (decimal)limit);
                                if (end > start)
                                {
                                    if (ttml) ReadTtmlSample(bytes, checked((int)dataOffset), checked((int)size), sampleStart, sampleEnd, start, end,
                                        (decimal)offsetSeconds, cues, continuing, ref textBudget, ct);
                                    else ReadSample(bytes, checked((int)dataOffset), checked((int)size), start, end, cues, continuing, ref textBudget);
                                }
                                dataOffset = checked(dataOffset + size); decode = checked(decode + sampleDuration);
                            }
                            reader.End(); nextData = dataOffset;
                        }
                        previousDecodeEnd = decode;
                    }
                }
            }
            if (samples == 0) throw new InvalidDataException("The MP4 resource has no samples for its selected caption track.");
            var output = new StringBuilder(track.Configuration.TrimEnd() + "\n\n");
            foreach (var cue in cues.OrderBy(cue => cue.Start).ThenBy(cue => cue.End))
            {
                if (cue.Id.Length > 0) output.Append(cue.Id).Append('\n');
                output.Append(Time(cue.Start, false)).Append(" --> ").Append(Time(cue.End, true));
                if (cue.Settings.Length > 0) output.Append(' ').Append(cue.Settings);
                output.Append('\n').Append(cue.Text).Append("\n\n");
                if (output.Length > 2 * 1024 * 1024) throw new InvalidDataException("The converted WebVTT exceeds its size limit.");
            }
            if (Utf8.GetByteCount(output.ToString()) > 2 * 1024 * 1024) throw new InvalidDataException("The converted WebVTT exceeds its byte limit.");
            return output.ToString();
        }
        catch (OverflowException ex) { throw new InvalidDataException("The MP4 caption contains overflowing sample offsets or timestamps.", ex); }
    }

    private static Track ReadTrack(byte[] bytes, Box movie, bool ttml)
    {
        var movieBoxes = Children(bytes, movie); var tracks = new List<Track>();
        foreach (var trak in movieBoxes.Where(box => box.Type == "trak"))
        {
            var trackBoxes = Children(bytes, trak); var mdia = Children(bytes, One(trackBoxes, "mdia"));
            var sampleTable = Children(bytes, One(Children(bytes, One(mdia, "minf")), "stbl"));
            var stsd = One(sampleTable, "stsd"); var description = new Reader(Payload(bytes, stsd));
            if (description.U32() != 0) throw new NotSupportedException("Unsupported caption sample-description version.");
            var count = description.U32();
            var entries = Boxes(bytes, stsd.Data + 8, stsd.End - stsd.Data - 8);
            if (entries.Count != count) throw new InvalidDataException("The MP4 sample description count is inconsistent.");
            if (!entries.Any(box => box.Type == (ttml ? "stpp" : "wvtt"))) continue;
            if (entries.Count != 1) throw new NotSupportedException("Multiple MP4 caption sample descriptions remain unsupported.");
            var sampleEntry = entries[0];
            if (sampleEntry.End - sampleEntry.Data < 8) throw new InvalidDataException("The caption sample entry is truncated.");
            string configuration;
            if (ttml)
            {
                if (BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(sampleEntry.Data + 6, 2)) != 1)
                    throw new NotSupportedException("External TTML data references require additional native support.");
                var position = sampleEntry.Data + 8;
                var ns = CString(bytes, ref position, sampleEntry.End);
                _ = CString(bytes, ref position, sampleEntry.End); // Schema location is descriptive and is never fetched.
                var auxiliary = CString(bytes, ref position, sampleEntry.End);
                var namespaces = ns.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (namespaces.Length is 0 or > 32 || !namespaces.Any(value => value.Length > 0 && TtmlClock.IsDocumentNamespace(value)) || auxiliary.Length > 0)
                    throw new NotSupportedException("This MP4 TTML namespace or auxiliary-media layout is unsupported.");
                if (Boxes(bytes, position, sampleEntry.End - position).Any(box => box.Type == "sinf"))
                    throw new NotSupportedException("Protected MP4 TTML cannot be converted.");
                configuration = "WEBVTT";
            }
            else
            {
                var configBoxes = Boxes(bytes, sampleEntry.Data + 8, sampleEntry.End - sampleEntry.Data - 8);
                if (configBoxes.Any(box => box.Type == "sinf")) throw new NotSupportedException("Protected MP4 WebVTT cannot be converted.");
                configuration = Text(Payload(bytes, One(configBoxes, "vttC")), false);
                if (!configuration.StartsWith("WEBVTT", StringComparison.Ordinal) || configuration.Contains("-->")) throw new InvalidDataException("The MP4 WebVTT configuration is invalid.");
            }
            var tkhd = new Reader(Payload(bytes, One(trackBoxes, "tkhd"))); var version = tkhd.U32() >> 24;
            if (version > 1) throw new NotSupportedException("Unsupported MP4 track-header version.");
            tkhd.Skip(version == 0 ? 8 : 16); var id = tkhd.U32();
            var mdhd = new Reader(Payload(bytes, One(mdia, "mdhd"))); var mdhdVersion = mdhd.U32() >> 24;
            if (mdhdVersion > 1) throw new NotSupportedException("Unsupported MP4 media-header version.");
            mdhd.Skip(mdhdVersion == 0 ? 8 : 16); var timescale = mdhd.U32();
            if (id == 0 || timescale == 0) throw new InvalidDataException("The MP4 caption track or timescale is invalid.");
            if (trackBoxes.Any(box => box.Type == "edts"))
            {
                var edit = new Reader(Payload(bytes, One(Children(bytes, One(trackBoxes, "edts")), "elst")));
                var editVersion = edit.U32() >> 24; var editCount = edit.U32();
                if (editVersion > 1 || editCount > 1) throw new NotSupportedException("Complex MP4 caption edits remain unsupported.");
                if (editCount == 1)
                {
                    if (editVersion == 0) edit.U32(); else edit.U64();
                    var time = editVersion == 0 ? edit.U32() : edit.U64();
                    if (time != 0 || edit.U32() != 0x00010000) throw new NotSupportedException("Shifted MP4 caption edit lists remain unsupported.");
                }
                edit.End();
            }
            var mvex = One(movieBoxes, "mvex");
            var defaults = Children(bytes, mvex).Where(box => box.Type == "trex").Where(box => Payload(bytes, box).Length >= 8 && BinaryPrimitives.ReadUInt32BigEndian(Payload(bytes, box)[4..]) == id).ToArray();
            if (defaults.Length > 1) throw new InvalidDataException("The MP4 caption track has ambiguous fragment defaults.");
            uint duration = 0, size = 0;
            if (defaults.Length == 1)
            {
                var trex = new Reader(Payload(bytes, defaults[0]));
                if (trex.U32() != 0 || trex.U32() != id || trex.U32() != 1) throw new InvalidDataException("The caption fragment defaults are invalid.");
                duration = trex.U32(); size = trex.U32(); trex.U32(); trex.End();
            }
            // Some published files omit matching track defaults. Zero remains
            // unknown: every sample must then supply duration/size via tfhd/trun.
            // Defaults belonging to a different track are never borrowed.
            tracks.Add(new(id, timescale, duration, size, configuration));
        }
        return tracks.Count == 1 ? tracks[0] : throw new NotSupportedException("The MP4 resource must contain exactly one track of the requested caption codec.");
    }

    private static string CString(byte[] bytes, ref int position, int end)
    {
        var start = position;
        while (position < end && bytes[position] != 0)
            if (++position - start > 16384) throw new InvalidDataException("The TTML sample-entry string exceeds its limit.");
        if (position == end) throw new InvalidDataException("The TTML sample-entry string is unterminated.");
        var result = Text(bytes.AsSpan(start, position - start), true); position++;
        return result;
    }

    private static void ReadTtmlSample(byte[] bytes, int offset, int length, decimal sampleStart, decimal sampleEnd, decimal windowStart, decimal windowEnd,
        decimal presentationOffset, List<Cue> cues, Dictionary<(string, string, string, string), int> continuing, ref int textBudget, CancellationToken ct)
    {
        var document = TtmlReader.Read(Text(bytes.AsSpan(offset, length), false).TrimStart('\uFEFF'), ct, windowEnd: sampleEnd, roundTimes: false, allowEmpty: true);
        var occurrences = new Dictionary<(decimal Start, string Text), int>();
        foreach (var cue in document.Cues)
        {
            ct.ThrowIfCancellationRequested();
            var identity = (cue.Start, cue.Text);
            var occurrence = occurrences.GetValueOrDefault(identity); occurrences[identity] = occurrence + 1;
            // TTML document time already belongs to the track timeline. Intersect
            // with this sample before shifting by the DASH presentation offset.
            var start = Math.Max(windowStart, Math.Max(cue.Start, sampleStart) - presentationOffset);
            var end = Math.Min(windowEnd, Math.Min(cue.End, sampleEnd) - presentationOffset);
            if (end <= start) continue;
            start = decimal.Round(start, 3, MidpointRounding.AwayFromZero); end = decimal.Round(end, 3, MidpointRounding.AwayFromZero);
            if (end <= start) throw new NotSupportedException("An MP4 TTML cue is shorter than the output timing precision.");
            var original = cue.Start.ToString(CultureInfo.InvariantCulture) + ":" + occurrence.ToString(CultureInfo.InvariantCulture);
            var key = ("", "", cue.Text, original);
            if (continuing.TryGetValue(key, out var index) && cues[index].End == start) cues[index] = cues[index] with { End = end };
            else
            {
                if (cues.Count >= 100000) throw new InvalidDataException("The MP4 TTML cue count exceeds its limit.");
                textBudget = checked(textBudget + cue.Text.Length + original.Length + 64);
                if (textBudget > 2 * 1024 * 1024) throw new InvalidDataException("The decoded MP4 TTML exceeds its output limit.");
                continuing[key] = cues.Count; cues.Add(new(start, end, "", "", cue.Text, original));
            }
        }
    }

    private static void ReadSample(byte[] data, int offset, int length, decimal start, decimal end, List<Cue> cues, Dictionary<(string, string, string, string), int> continuing, ref int textBudget)
    {
        foreach (var box in Boxes(data, offset, length))
        {
            if (box.Type == "vtte") { if (box.End != box.Data) throw new InvalidDataException("The empty WebVTT sample has a payload."); continue; }
            if (box.Type != "vttc") throw new NotSupportedException("Unsupported MP4 WebVTT sample box: " + box.Type);
            var children = Children(data, box);
            var text = Text(Payload(data, One(children, "payl")), false);
            var id = OptionalText("iden"); var settings = OptionalText("sttg"); var original = OptionalText("ctim");
            if (id.Contains("-->", StringComparison.Ordinal)) throw new InvalidDataException("The WebVTT cue identifier contains a timing delimiter.");
            if (text.Contains("\n\n")) throw new NotSupportedException("Blank lines inside MP4 WebVTT cue payloads require conversion that is not implemented yet.");
            var key = (id, settings, text, original);
            if (continuing.TryGetValue(key, out var index) && cues[index].End == start) cues[index] = cues[index] with { End = end };
            else
            {
                if (cues.Count == 100000) throw new InvalidDataException("The MP4 caption cue count exceeds its limit.");
                textBudget = checked(textBudget + text.Length + id.Length + settings.Length + original.Length + 64);
                if (textBudget > 2 * 1024 * 1024) throw new InvalidDataException("The decoded MP4 caption text exceeds its size limit.");
                continuing[key] = cues.Count; cues.Add(new(start, end, id, settings, text, original));
            }
            string OptionalText(string type)
            {
                var values = children.Where(child => child.Type == type).ToArray();
                if (values.Length > 1) throw new InvalidDataException("The MP4 cue repeats a field.");
                return values.Length == 0 ? "" : Text(Payload(data, values[0]), true);
            }
        }
    }

    private static string Time(decimal seconds, bool end)
    {
        var milliseconds = checked((long)(end ? decimal.Ceiling(seconds * 1000) : decimal.Floor(seconds * 1000)));
        return string.Create(CultureInfo.InvariantCulture, $"{milliseconds / 3600000:D2}:{milliseconds / 60000 % 60:D2}:{milliseconds / 1000 % 60:D2}.{milliseconds % 1000:D3}");
    }
    private static string Text(ReadOnlySpan<byte> bytes, bool singleLine)
    {
        var value = Utf8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (value.Contains('\0') || singleLine && value.Contains('\n')) throw new InvalidDataException("The MP4 caption contains invalid text fields.");
        return value;
    }
    private static Box One(IReadOnlyList<Box> boxes, string type)
    { var matches = boxes.Where(box => box.Type == type).ToArray(); return matches.Length == 1 ? matches[0] : throw new InvalidDataException("Missing or ambiguous MP4 caption box: " + type); }
    private static ReadOnlySpan<byte> Payload(byte[] bytes, Box box) => bytes.AsSpan(box.Data, box.End - box.Data);
    private static List<Box> Children(byte[] bytes, Box box) => Boxes(bytes, box.Data, box.End - box.Data);
    private static List<Box> Boxes(byte[] bytes, int start, int length)
    {
        if (start < 0 || length < 0 || start > bytes.Length - length) throw new InvalidDataException("An MP4 caption box exceeds its resource.");
        var result = new List<Box>(); var end = start + length;
        while (start < end)
        {
            if (end - start < 8 || result.Count == 100000) throw new InvalidDataException("The MP4 caption box list is truncated or excessive.");
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(start)); var header = 8;
            var type = Encoding.ASCII.GetString(bytes.AsSpan(start + 4, 4));
            if (size == 1) { if (end - start < 16) throw new InvalidDataException("The MP4 caption extended box header is truncated."); size = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(start + 8)); header = 16; }
            else if (size == 0) size = (ulong)(end - start);
            if (size < (ulong)header || size > (ulong)(end - start)) throw new InvalidDataException("The MP4 caption box size is invalid.");
            result.Add(new(type, start, start + header, start + (int)size)); start += (int)size;
        }
        return result;
    }
    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private ReadOnlySpan<byte> _data = data;
        public uint U32() { Need(4); var result = BinaryPrimitives.ReadUInt32BigEndian(_data); _data = _data[4..]; return result; }
        public ulong U64() { Need(8); var result = BinaryPrimitives.ReadUInt64BigEndian(_data); _data = _data[8..]; return result; }
        public void Skip(int count) { Need(count); _data = _data[count..]; }
        public void End() { if (_data.Length != 0) throw new InvalidDataException("The MP4 caption field has trailing data."); }
        private void Need(int count) { if (_data.Length < count) throw new InvalidDataException("The MP4 caption field is truncated."); }
    }
}
