using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace YoutubeDl.Engine.Protocols;

public sealed record UmpPart(uint Type, ReadOnlyMemory<byte> Payload);

/// <summary>Reads bounded UMP frames from streaming responses, including fragmented network reads.</summary>
public static class UmpReader
{
    public static async IAsyncEnumerable<UmpPart> ReadAsync(Stream stream, int maximumPartBytes = 8 * 1024 * 1024,
        int maximumParts = 10000, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPartBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumParts);
        var header = new byte[5];
        var count = 0;
        while (true)
        {
            var type = await ReadIntegerAsync(stream, header, allowEnd: true, ct).ConfigureAwait(false);
            if (type is null) yield break;
            if (++count > maximumParts) throw new InvalidDataException("The streaming response contains too many UMP parts.");
            var length = await ReadIntegerAsync(stream, header, allowEnd: false, ct).ConfigureAwait(false);
            if (length > maximumPartBytes) throw new InvalidDataException("The UMP part exceeds the configured size limit.");
            var payload = new byte[(int)length!.Value];
            await stream.ReadExactlyAsync(payload, ct).ConfigureAwait(false);
            yield return new(type.Value, payload);
        }
    }

    private static async Task<uint?> ReadIntegerAsync(Stream stream, byte[] buffer, bool allowEnd, CancellationToken ct)
    {
        var read = await stream.ReadAsync(buffer.AsMemory(0, 1), ct).ConfigureAwait(false);
        if (read == 0)
        {
            if (allowEnd) return null;
            throw new EndOfStreamException("The UMP frame header was incomplete.");
        }
        var length = 1;
        while (length < 5 && (buffer[0] & (0x80 >> (length - 1))) != 0) length++;
        if (length > 1) await stream.ReadExactlyAsync(buffer.AsMemory(1, length - 1), ct).ConfigureAwait(false);
        if (length == 5) return BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(1, 4));
        var leadingBits = 8 - length;
        var value = (uint)(buffer[0] & ((1 << leadingBits) - 1));
        for (var index = 1; index < length; index++) value |= (uint)buffer[index] << (leadingBits + (index - 1) * 8);
        return value;
    }
}
