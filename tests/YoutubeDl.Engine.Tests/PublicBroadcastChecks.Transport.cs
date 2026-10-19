using YoutubeDl.Engine.Download;
internal static partial class PublicBroadcastChecks
{
    private static IEnumerable<(string Name, Func<Task> Test)> TransportChecks => new[] { "pes", "clean", "gap", "pusi-without-pes", "adaptation", "tei", "sync", "unaligned", "cancel" }.Select(mode => ("Public broadcast NRK transport " + mode, (Func<Task>)(() => PesTransport(mode))));
    private static async Task PesTransport(string mode)
    {
        using var files = new ExecSponsorChecks.Files(); var path = Path.Combine(files.Directory, "received.ts");
        byte[] Packet(int counter, bool start) { var p = new byte[188]; p[0] = 0x47; p[1] = (byte)(1 | (start ? 64 : 0)); p[2] = 0; p[3] = (byte)(0x10 | counter); p[4] = 0; p[5] = 0; p[6] = 1; p[7] = 0xe0; for (var i = 8; i < p.Length; i++) p[i] = (byte)i; return p; }
        var source = Packet(7, true).Concat(Packet(mode == "clean" ? 8 : mode == "gap" ? 10 : 0, mode != "gap")).Concat(Packet(mode == "clean" ? 9 : 1, false)).ToArray();
        if (mode == "pusi-without-pes") source[188 + 6] = 9;
        if (mode == "adaptation") { source[188 + 3] |= 0x20; source[188 + 4] = 184; }
        if (mode == "tei") source[188 + 1] |= 0x80;
        if (mode == "sync") source[188] = 0;
        if (mode == "unaligned") source = source[..^1];
        await File.WriteAllBytesAsync(path, source);
        if (mode == "cancel") { using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); await ProviderFamiliesChecks.Expect<OperationCanceledException>(() => TransportContinuity.NormalizePesResetsAsync(path, cancellation.Token)); }
        else if (mode is "pes" or "clean")
        {
            var changed = await TransportContinuity.NormalizePesResetsAsync(path, default); var current = await File.ReadAllBytesAsync(path); Assert(changed == (mode == "pes" ? 2 : 0) && current.Length == source.Length, "The validated transport fix changed packet boundaries.");
            for (var i = 0; i < source.Length; i++) Assert(i % 188 == 3 ? (source[i] & 0xf0) == (current[i] & 0xf0) : source[i] == current[i], "Transport normalization changed a payload or clock byte.");
            return;
        }
        else await ProviderFamiliesChecks.Expect<InvalidDataException>(() => TransportContinuity.NormalizePesResetsAsync(path, default));
        Assert((await File.ReadAllBytesAsync(path)).AsEnumerable().SequenceEqual(source), "Refused/canceled transport validation altered the received source.");
    }
}
