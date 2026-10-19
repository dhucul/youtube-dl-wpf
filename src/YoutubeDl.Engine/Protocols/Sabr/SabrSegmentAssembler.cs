using System.Security.Cryptography;
using System.Text;

namespace YoutubeDl.Engine.Protocols.Sabr;

public sealed record SabrSegment(SabrFormatId Format, uint Number, bool Initialization, long StartMs,
    long DurationMs, ReadOnlyMemory<byte> Data, SabrTimeRange? TimeRange = null);

/// <summary>Associates UMP media payloads with their headers and emits only complete validated segments.</summary>
public sealed class SabrSegmentAssembler : IDisposable
{
    private sealed record Pending(SabrFormatId Format, uint Number, bool Initialization, long StartMs,
        long DurationMs, int ExpectedLength, MemoryStream Buffer, SabrTimeRange? TimeRange);
    private readonly Dictionary<byte, Pending> _pending = [];
    private sealed record Completed(byte[] Digest,long StartMs,long DurationMs,SabrTimeRange? TimeRange);
    private readonly Dictionary<(SabrFormatId Format, uint Number, bool Initialization), Completed> _completed = [];
    private readonly string _videoId;
    private readonly int _maximumSegmentBytes;
    private readonly int _maximumBufferedBytes;
    private readonly int _maximumCompletedSegments;
    private int _bufferedBytes;
    public int PendingSegments => _pending.Count;

    public SabrSegmentAssembler(string videoId, int maximumSegmentBytes = 32 * 1024 * 1024,
        int maximumBufferedBytes = 64 * 1024 * 1024, int maximumCompletedSegments = 100000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSegmentBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBufferedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCompletedSegments);
        _videoId = videoId;
        _maximumSegmentBytes = maximumSegmentBytes;
        _maximumBufferedBytes = maximumBufferedBytes;
        _maximumCompletedSegments = maximumCompletedSegments;
    }

    public SabrSegment? Accept(UmpPart part)
    {
        if (part.Type == 20) { Header(part.Payload); return null; }
        if (part.Type is not (21 or 22)) return null;
        if (part.Payload.Length == 0) throw new InvalidDataException("A SABR media part is missing its header identifier.");
        var id = part.Payload.Span[0];
        if (!_pending.TryGetValue(id, out var pending)) throw new InvalidDataException("SABR media arrived without a matching header.");
        if (part.Type == 21)
        {
            var data = part.Payload[1..];
            if (pending.Buffer.Length + data.Length > pending.ExpectedLength || _bufferedBytes + data.Length > _maximumBufferedBytes)
                throw new InvalidDataException("The SABR segment exceeded its declared length or buffering limit.");
            pending.Buffer.Write(data.Span);
            _bufferedBytes += data.Length;
            return null;
        }
        if (part.Payload.Length != 1 || pending.Buffer.Length != pending.ExpectedLength)
            throw new InvalidDataException("The SABR segment ended before all declared media bytes arrived.");
        var key = (pending.Format, pending.Number, pending.Initialization);
        var bytes = pending.Buffer.ToArray();
        var digest = SHA256.HashData(bytes);
        _pending.Remove(id);
        _bufferedBytes -= bytes.Length;
        pending.Buffer.Dispose();
        if (_completed.TryGetValue(key, out var previous))
        {
            if (!CryptographicOperations.FixedTimeEquals(previous.Digest, digest)||previous.StartMs!=pending.StartMs||previous.DurationMs!=pending.DurationMs||previous.TimeRange!=pending.TimeRange)
                throw new InvalidDataException("A retransmitted SABR segment changed its contents.");
            return null;
        }
        if (_completed.Count == _maximumCompletedSegments) throw new InvalidDataException("The SABR segment count exceeded the configured limit.");
        _completed[key] = new(digest,pending.StartMs,pending.DurationMs,pending.TimeRange);
        return new(pending.Format, pending.Number, pending.Initialization, pending.StartMs, pending.DurationMs, bytes, pending.TimeRange);
    }

    public void EnsureDrained()
    {
        if (_pending.Count != 0) throw new EndOfStreamException("The SABR response ended with unfinished media segments.");
    }
    internal void RememberRestored(SabrSegment segment)
    {
        EnsureDrained();var key=(segment.Format,segment.Number,segment.Initialization);var digest=SHA256.HashData(segment.Data.Span);
        if(_completed.TryGetValue(key,out var previous)&&(!CryptographicOperations.FixedTimeEquals(previous.Digest,digest)||previous.StartMs!=segment.StartMs||previous.DurationMs!=segment.DurationMs||previous.TimeRange!=segment.TimeRange))throw new InvalidDataException("The restored SABR segment conflicts with fresh media.");
        if(!_completed.ContainsKey(key)&&_completed.Count>=_maximumCompletedSegments)throw new InvalidDataException("The restored SABR prefix exceeds its segment limit.");
        _completed[key]=new(digest,segment.StartMs,segment.DurationMs,segment.TimeRange);
    }
    public void DiscardPending()
    {
        foreach (var pending in _pending.Values) pending.Buffer.Dispose();
        _pending.Clear(); _bufferedBytes = 0;
    }

    private void Header(ReadOnlyMemory<byte> data)
    {
        var fields = ProtobufReader.Read(data, maximumBytes: 65536);
        var header = RequiredInteger(fields, 1);
        if (header > byte.MaxValue || _pending.ContainsKey((byte)header)) throw new InvalidDataException("The SABR media header identifier is invalid or already in use.");
        var video = OptionalBytes(fields, 2);
        if (!video.IsEmpty && Encoding.UTF8.GetString(video.Span) != _videoId) throw new InvalidDataException("The SABR segment belongs to a different video.");
        var formatFields = OptionalBytes(fields, 13);
        var nested = formatFields.IsEmpty ? fields : ProtobufReader.Read(formatFields, maximumBytes: 65536);
        var itag = RequiredInteger(nested, formatFields.IsEmpty ? 3 : 1);
        var modified = OptionalInteger(nested, formatFields.IsEmpty ? 4 : 2);
        var tags = Encoding.UTF8.GetString(OptionalBytes(nested, formatFields.IsEmpty ? 5 : 3).Span);
        var length = RequiredInteger(fields, 14);
        var number = OptionalInteger(fields, 9);
        if (itag is 0 or > uint.MaxValue || number > uint.MaxValue || length > (ulong)_maximumSegmentBytes)
            throw new InvalidDataException("The SABR segment format, sequence, or length is invalid.");
        if (OptionalInteger(fields, 7) != 0) throw new NotSupportedException("Compressed SABR media segments are not supported yet.");
        var start = OptionalInteger(fields, 11);
        var duration = OptionalInteger(fields, 12);
        if (start > long.MaxValue || duration > long.MaxValue) throw new InvalidDataException("The SABR segment time is invalid.");
        var initialization = OptionalInteger(fields, 8) != 0;
        var rawTime = OptionalBytes(fields, 15);
        SabrTimeRange? time = null;
        if (!rawTime.IsEmpty)
        {
            time = SabrTimeRange.Read(rawTime, initialization);
            if (fields.Any(field => field.Number == 11) && Math.Abs((decimal)start - time.StartMs) > 1 ||
                fields.Any(field => field.Number == 12) && Math.Abs((decimal)duration - (time.EndMs - time.StartMs)) > 1)
                throw new InvalidDataException("The SABR millisecond and tick timelines disagree.");
            start = (ulong)time.StartMs; duration = (ulong)(time.EndMs - time.StartMs);
        }
        if (_pending.Count >= 256) throw new InvalidDataException("Too many SABR segments are pending.");
        _pending[(byte)header] = new(new((uint)itag, modified, tags), (uint)number, initialization,
            (long)start, (long)duration, (int)length, new MemoryStream(), time);
    }

    internal static ulong RequiredInteger(IReadOnlyList<ProtobufField> fields, int number)
    {
        var matches = fields.Where(field => field.Number == number).ToArray();
        if (matches.Length != 1 || matches[0].WireType != 0) throw new InvalidDataException("A SABR control field is missing, repeated, or has the wrong type.");
        return matches[0].Integer;
    }
    internal static ulong OptionalInteger(IReadOnlyList<ProtobufField> fields, int number) => fields.Any(field => field.Number == number) ? RequiredInteger(fields, number) : 0;
    internal static ReadOnlyMemory<byte> OptionalBytes(IReadOnlyList<ProtobufField> fields, int number)
    {
        var matches = fields.Where(field => field.Number == number).ToArray();
        if (matches.Length == 0) return default;
        if (matches.Length != 1 || matches[0].WireType != 2) throw new InvalidDataException("A SABR byte field is repeated or has the wrong type.");
        return matches[0].Bytes;
    }
    public void Dispose()
    {
        foreach (var pending in _pending.Values) pending.Buffer.Dispose();
        _pending.Clear();
        _completed.Clear();
        _bufferedBytes = 0;
    }
}
