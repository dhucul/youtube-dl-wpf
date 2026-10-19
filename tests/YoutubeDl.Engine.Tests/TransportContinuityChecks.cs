using YoutubeDl.Engine.Download;

internal static class TransportContinuityChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("TS continuity alignment changes only counters while preserving payload PCR and all other header bits", Payload),
        ("TS continuity alignment preserves duplicates adaptation-only packets and declared discontinuities", Semantics),
        ("TS continuity alignment rejects misaligned sync error and adaptation layouts before publication", Invalid)
    ];
    private static byte[] Packet(int pid, int counter, byte value, int mode = 1, bool discontinuity = false)
    {
        var bytes = Enumerable.Repeat(value, 188).ToArray(); bytes[0] = 0x47; bytes[1] = (byte)((pid >> 8) & 31); bytes[2] = (byte)pid; bytes[3] = (byte)((mode << 4) | counter);
        if ((mode & 2) != 0) { bytes[4] = mode == 2 ? (byte)183 : (byte)7; bytes[5] = discontinuity ? (byte)0x80 : (byte)0x10; }
        return bytes;
    }
    private static async Task Payload()
    {
        var original = Join(Packet(256, 8, 1), Packet(257, 4, 2), Packet(256, 0, 3), Packet(257, 0, 4, 3), Packet(256, 1, 5));
        await Run(original, async (path, bytes) =>
        {
            if (await TransportContinuity.NormalizeAsync(path, default) != 3) throw new InvalidOperationException("Independent segment counters were not aligned.");
            var after = await File.ReadAllBytesAsync(path);
            for (var index = 0; index < bytes.Length; index++) if (index % 188 == 3 ? (bytes[index] & 0xf0) != (after[index] & 0xf0) : bytes[index] != after[index])
                throw new InvalidOperationException("Counter alignment modified compressed payload, clock or non-counter metadata.");
            if ((after[188 * 2 + 3] & 15) != 9 || (after[188 * 3 + 3] & 15) != 5 || (after[188 * 4 + 3] & 15) != 10 || await TransportContinuity.NormalizeAsync(path, default) != 0)
                throw new InvalidOperationException("Counter sequences were incorrect or alignment was not idempotent.");
        });
    }
    private static async Task Semantics()
    {
        var first = Packet(256, 3, 1); var same = first.ToArray(); var noPayload = Packet(256, 0, 2, 2); var reset = Packet(256, 12, 3, 3, true);
        await Run(Join(first, noPayload, same, reset, Packet(256, 0, 4), Packet(8191, 0, 9)), async (path, bytes) =>
        {
            await TransportContinuity.NormalizeAsync(path, default); var after = await File.ReadAllBytesAsync(path);
            var counters = Enumerable.Range(0, 6).Select(index => after[index * 188 + 3] & 15).ToArray();
            if (!counters.SequenceEqual([3, 3, 3, 12, 13, 0])) throw new InvalidOperationException("Duplicate, adaptation, discontinuity or null-PID semantics changed.");
        });
    }
    private static async Task Invalid()
    {
        var sync = Packet(256, 0, 1); sync[0] = 0; var error = Packet(256, 0, 1); error[1] |= 0x80; var adaptation = Packet(256, 0, 1, 3); adaptation[4] = 183;
        foreach (var bytes in new[] { new byte[187], sync, error, adaptation, Packet(256, 0, 1, 0) })
            await Run(bytes, async (path, _) => { try { await TransportContinuity.NormalizeAsync(path, default); throw new InvalidOperationException("Invalid transport packets were accepted."); } catch (InvalidDataException) { } });
    }
    private static byte[] Join(params byte[][] packets) => packets.SelectMany(packet => packet).ToArray();
    private static async Task Run(byte[] bytes, Func<string, byte[], Task> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "native-ts-" + Guid.NewGuid() + ".ts");
        try { await File.WriteAllBytesAsync(path, bytes); await check(path, bytes); }
        finally { File.Delete(path); }
    }
}
