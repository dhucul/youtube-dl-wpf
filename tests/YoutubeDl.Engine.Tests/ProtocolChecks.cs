using YoutubeDl.Engine.Protocols;

internal static class ProtocolChecks
{
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("UMP parts survive byte-by-byte network fragmentation", Fragmentation),
        ("UMP extended integers preserve unsigned values", ExtendedIntegers),
        ("UMP truncation and size limits fail before publication", Limits),
        ("Protobuf streaming messages preserve repeated and unknown fields", Fields),
        ("Protobuf truncation, overflow, and field limits are rejected", Malformed)
    ];

    private static async Task Fragmentation()
    {
        // Independently specified frames: media header 20, media 21, end 22.
        await using var stream = new FragmentedStream([20, 2, 8, 1, 21, 4, 1, 65, 66, 67, 22, 1, 1]);
        var parts = await Collect(stream);
        Assert(parts.Count == 3 && parts[0].Type == 20 && parts[1].Type == 21 && parts[2].Type == 22, "Frames were lost across reads.");
        Assert(parts[1].Payload.Span.SequenceEqual(new byte[] { 1, 65, 66, 67 }), "Media bytes changed.");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Expect<OperationCanceledException>(() => Collect(new FragmentedStream([20, 0]), canceled.Token));
    }

    private static async Task ExtendedIntegers()
    {
        // UMP integer boundaries: 128, 16,384, 2,097,152, uint.MaxValue; all have empty payloads.
        await using var stream = new FragmentedStream([0x80, 0x02, 0, 0xc0, 0, 0x02, 0, 0xe0, 0, 0, 0x02, 0, 0xf0, 0xff, 0xff, 0xff, 0xff, 0]);
        var parts = await Collect(stream);
        Assert(parts.Select(part => part.Type).SequenceEqual(new uint[] { 128, 16384, 2097152, uint.MaxValue }), "Extended UMP values were interpreted as protobuf varints or signed integers.");
    }

    private static async Task Limits()
    {
        foreach (var data in new byte[][] { [20], [20, 3, 1], [0x80], [0xf0, 1, 2] })
            await Expect<EndOfStreamException>(() => Collect(new FragmentedStream(data)));
        await Expect<InvalidDataException>(async () =>
        {
            await foreach (var _ in UmpReader.ReadAsync(new FragmentedStream([20, 100]), maximumPartBytes: 32)) { }
        });
        await Expect<InvalidDataException>(async () =>
        {
            await foreach (var _ in UmpReader.ReadAsync(new FragmentedStream([20, 0, 21, 0]), maximumParts: 1)) { }
        });
    }

    private static Task Fields()
    {
        // field 1 varint 150, repeated field 1 value 1, field 2 UTF-8 "abc", field 3 fixed32, field 4 fixed64.
        var fields = ProtobufReader.Read(new byte[] { 8, 0x96, 1, 8, 1, 18, 3, 97, 98, 99, 29, 1, 2, 3, 4, 33, 1, 0, 0, 0, 0, 0, 0, 0 });
        Assert(fields.Count == 5 && fields[0].Integer == 150 && fields[1].Number == 1 && fields[1].Integer == 1, "Repeated integer fields changed.");
        Assert(System.Text.Encoding.UTF8.GetString(fields[2].Bytes.Span) == "abc" && fields[3].Integer == 0x04030201 && fields[4].Integer == 1, "Length-delimited or fixed-width fields changed.");
        return Task.CompletedTask;
    }

    private static async Task Malformed()
    {
        await Expect<EndOfStreamException>(() => Task.Run(() => ProtobufReader.Read(new byte[] { 18, 3, 1 })));
        await Expect<EndOfStreamException>(() => Task.Run(() => ProtobufReader.Read(new byte[] { 8, 0x80 })));
        await Expect<InvalidDataException>(() => Task.Run(() => ProtobufReader.Read(new byte[] { 0 })));
        await Expect<InvalidDataException>(() => Task.Run(() => ProtobufReader.Read(new byte[] { 8, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 2 })));
        await Expect<NotSupportedException>(() => Task.Run(() => ProtobufReader.Read(new byte[] { 11 })));
        await Expect<InvalidDataException>(() => Task.Run(() => ProtobufReader.Read(new byte[] { 8, 1, 8, 2 }, maximumFields: 1)));
    }

    private static async Task<List<UmpPart>> Collect(Stream stream, CancellationToken ct = default)
    {
        var parts = new List<UmpPart>();
        await foreach (var part in UmpReader.ReadAsync(stream, ct: ct)) parts.Add(part);
        return parts;
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> operation) where T : Exception
    {
        try { await operation(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], ct);
    }
}
