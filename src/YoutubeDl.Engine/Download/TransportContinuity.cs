namespace YoutubeDl.Engine.Download;

/// <summary>Joins independently numbered 188-byte TS packets without changing encoded payloads or clocks.</summary>
internal static class TransportContinuity
{
    // Some NRK muxers restart PID counters at complete PES starts. Validate the
    // entire received stream before changing only continuity nibbles. Other
    // counter gaps, transport-error indicators and malformed packets still fail.
    internal static async Task<int> NormalizePesResetsAsync(string path, CancellationToken ct)
    {
        await using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 188 * 256, FileOptions.Asynchronous))
        {
            if (input.Length == 0 || input.Length % 188 != 0) throw new InvalidDataException("The PES-reset transport is not packet aligned.");
            var previous = new byte[8192][]; var buffer = new byte[188 * 256];
            while (input.Position < input.Length)
            {
                ct.ThrowIfCancellationRequested(); var count = (int)Math.Min(buffer.Length, input.Length - input.Position); await input.ReadExactlyAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                for (var offset = 0; offset < count; offset += 188)
                {
                    var packet = buffer.AsSpan(offset, 188).ToArray(); var control = (packet[3] >> 4) & 3; var payload = (control & 1) != 0; var adaptation = (control & 2) != 0;
                    if (packet[0] != 0x47 || (packet[1] & 0x80) != 0 || control == 0 || adaptation && packet[4] > (payload ? 182 : 183)) throw new InvalidDataException("The PES-reset source contains a damaged transport packet.");
                    var pid = ((packet[1] & 31) << 8) | packet[2]; if (pid == 8191) continue;
                    var discontinuity = adaptation && packet[4] > 0 && (packet[5] & 0x80) != 0; var old = previous[pid]; var counter = packet[3] & 15;
                    if (old is not null && !discontinuity)
                    {
                        var duplicate = payload && old.AsSpan().SequenceEqual(packet); var expected = duplicate || !payload ? old[3] & 15 : ((old[3] & 15) + 1) & 15;
                        var start = 4 + (adaptation ? packet[4] + 1 : 0);
                        var pes = payload && (packet[1] & 0x40) != 0 && start + 3 <= 188 && packet[start] == 0 && packet[start + 1] == 0 && packet[start + 2] == 1;
                        if (counter != expected && !(counter == 0 && pes)) throw new InvalidDataException("The transport error is not an independent counter reset at a complete PES start.");
                    }
                    previous[pid] = packet;
                }
            }
        }
        return await NormalizeAsync(path, ct).ConfigureAwait(false);
    }
    private sealed class State
    {
        internal int Counter;
        internal byte[] Original = [];
    }
    internal static async Task<int> NormalizeAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 188 * 256, FileOptions.Asynchronous);
        if (stream.Length == 0 || stream.Length % 188 != 0) throw new InvalidDataException("The opted-in HLS transport stream is not packet aligned.");
        var states = new State?[8192]; var buffer = new byte[188 * 256]; var changed = 0;
        while (stream.Position < stream.Length)
        {
            ct.ThrowIfCancellationRequested(); var position = stream.Position; var count = (int)Math.Min(buffer.Length, stream.Length - position);
            await stream.ReadExactlyAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false); var blockChanged = false;
            for (var offset = 0; offset < count; offset += 188)
            {
                if (buffer[offset] != 0x47 || (buffer[offset + 1] & 0x80) != 0) throw new InvalidDataException("The HLS transport stream contains a sync or transport-error packet.");
                var pid = ((buffer[offset + 1] & 31) << 8) | buffer[offset + 2]; var control = (buffer[offset + 3] >> 4) & 3;
                if (control == 0) throw new InvalidDataException("The HLS transport packet has an invalid adaptation/payload mode.");
                if (pid == 8191) continue;
                var payload = (control & 1) != 0; var adaptation = (control & 2) != 0;
                if (adaptation && buffer[offset + 4] > (payload ? 182 : 183)) throw new InvalidDataException("The HLS transport adaptation field is excessive.");
                var discontinuity = adaptation && buffer[offset + 4] > 0 && (buffer[offset + 5] & 0x80) != 0;
                var original = buffer.AsSpan(offset, 188).ToArray(); var current = buffer[offset + 3] & 15; var state = states[pid];
                if (state is null || discontinuity) { states[pid] = new() { Counter = current, Original = payload ? original : [] }; continue; }
                var duplicate = payload && original.AsSpan().SequenceEqual(state.Original);
                var expected = duplicate || !payload ? state.Counter : (state.Counter + 1) & 15;
                if (current != expected) { buffer[offset + 3] = (byte)((buffer[offset + 3] & 0xf0) | expected); changed++; blockChanged = true; }
                state.Counter = expected; if (payload) state.Original = original;
            }
            if (blockChanged)
            {
                stream.Position = position; await stream.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
            }
            stream.Position = position + count;
        }
        await stream.FlushAsync(ct).ConfigureAwait(false); stream.Flush(true); return changed;
    }
}
