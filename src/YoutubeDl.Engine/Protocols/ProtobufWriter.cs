using System.Buffers.Binary;
using System.Text;

namespace YoutubeDl.Engine.Protocols;

public sealed class ProtobufWriter
{
    private readonly MemoryStream _stream = new();
    private readonly int _maximumBytes;
    public ProtobufWriter(int maximumBytes = 65536)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        _maximumBytes = maximumBytes;
    }
    public void UInt64(int field, ulong value)
    {
        Tag(field, 0);
        Varint(value);
    }
    public void Float(int field, float value)
    {
        Tag(field, 5);
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        Append(bytes);
    }
    public void String(int field, string value) => Bytes(field, Encoding.UTF8.GetBytes(value));
    public void Bytes(int field, ReadOnlySpan<byte> value)
    {
        Tag(field, 2);
        Varint((ulong)value.Length);
        Append(value);
    }
    public void Message(int field, Action<ProtobufWriter> build)
    {
        var nested = new ProtobufWriter(_maximumBytes);
        build(nested);
        Bytes(field, nested.ToArray());
    }
    public byte[] ToArray() => _stream.ToArray();
    internal void Field(ProtobufField value)
    {
        if (value.WireType == 0) { UInt64(value.Number, value.Integer); return; }
        if (value.WireType == 2) { Bytes(value.Number, value.Bytes.Span); return; }
        if (value.WireType is not (1 or 5)) throw new NotSupportedException("The protobuf field uses an unsupported wire type.");
        Tag(value.Number, value.WireType);
        Span<byte> bytes = stackalloc byte[8];
        if (value.WireType == 1) { BinaryPrimitives.WriteUInt64LittleEndian(bytes, value.Integer); Append(bytes); }
        else { BinaryPrimitives.WriteUInt32LittleEndian(bytes, checked((uint)value.Integer)); Append(bytes[..4]); }
    }
    private void Tag(int field, int wire)
    {
        if (field is <= 0 or > 0x1fffffff) throw new ArgumentOutOfRangeException(nameof(field));
        Varint(((ulong)field << 3) | (uint)wire);
    }
    private void Varint(ulong value)
    {
        Span<byte> bytes = stackalloc byte[10];
        var length = 0;
        do
        {
            var current = (byte)(value & 0x7f);
            value >>= 7;
            bytes[length++] = value == 0 ? current : (byte)(current | 0x80);
        } while (value != 0);
        Append(bytes[..length]);
    }
    private void Append(ReadOnlySpan<byte> value)
    {
        if (_stream.Length + value.Length > _maximumBytes) throw new InvalidDataException("The protobuf request exceeds the configured size limit.");
        _stream.Write(value);
    }
}
