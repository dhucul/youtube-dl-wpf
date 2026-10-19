using System.Buffers.Binary;

namespace YoutubeDl.Engine.Protocols;

public sealed record ProtobufField(int Number, int WireType, ulong Integer, ReadOnlyMemory<byte> Bytes);

/// <summary>Minimal bounded protobuf wire reader for streaming control messages. No generated-code runtime is required.</summary>
public static class ProtobufReader
{
    public static IReadOnlyList<ProtobufField> Read(ReadOnlyMemory<byte> data, int maximumBytes = 8 * 1024 * 1024, int maximumFields = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFields);
        if (data.Length > maximumBytes) throw new InvalidDataException("The protobuf message exceeds the configured size limit.");
        var result = new List<ProtobufField>();
        var position = 0;
        while (position < data.Length)
        {
            if (result.Count == maximumFields) throw new InvalidDataException("The protobuf message contains too many fields.");
            var tag = ReadVarint(data.Span, ref position);
            var number = tag >> 3;
            if (number is 0 or > 0x1fffffff) throw new InvalidDataException("The protobuf field number is invalid.");
            var wire = (int)(tag & 7);
            ulong integer = 0;
            ReadOnlyMemory<byte> bytes = default;
            switch (wire)
            {
                case 0:
                    integer = ReadVarint(data.Span, ref position);
                    break;
                case 1:
                    Require(data.Length, position, 8);
                    integer = BinaryPrimitives.ReadUInt64LittleEndian(data.Span.Slice(position, 8));
                    position += 8;
                    break;
                case 2:
                    var length = ReadVarint(data.Span, ref position);
                    if (length > int.MaxValue) throw new InvalidDataException("The protobuf byte field is too large.");
                    Require(data.Length, position, (int)length);
                    bytes = data.Slice(position, (int)length);
                    position += (int)length;
                    break;
                case 5:
                    Require(data.Length, position, 4);
                    integer = BinaryPrimitives.ReadUInt32LittleEndian(data.Span.Slice(position, 4));
                    position += 4;
                    break;
                default:
                    throw new NotSupportedException($"Protobuf wire type {wire} is not supported by the native streaming decoder.");
            }
            result.Add(new((int)number, wire, integer, bytes));
        }
        return result;
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> data, ref int position)
    {
        ulong result = 0;
        for (var index = 0; index < 10; index++)
        {
            Require(data.Length, position, 1);
            var value = data[position++];
            if (index == 9 && value > 1) throw new InvalidDataException("The protobuf integer overflowed 64 bits.");
            result |= (ulong)(value & 0x7f) << (index * 7);
            if ((value & 0x80) == 0) return result;
        }
        throw new InvalidDataException("The protobuf integer was unterminated.");
    }

    private static void Require(int length, int position, int count)
    {
        if (count > length - position) throw new EndOfStreamException("The protobuf field was incomplete.");
    }
}
