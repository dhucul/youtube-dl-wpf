namespace YoutubeDl.Engine.Download;

/// <summary>Normalizes only a single sync prefix after a complete MP3 frame inventory.</summary>
internal static class Mp3ClipBoundary
{
    private static readonly int[] SampleRates = [44100, 48000, 32000];
    private static readonly int[] Mpeg1Rates = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
    private static readonly int[] Mpeg2Rates = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
    internal static async Task<bool> NormalizeAsync(string path, CancellationToken ct)
    {
        var info = new FileInfo(path); if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("The MP3 clipping input cannot be a link.");
        if (info.Length is < 10 or > 68719476736) throw new InvalidDataException("The declared MP3 clip has an invalid size.");
        long end = 0; var frames = 0; var header = new byte[10];
        await using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous))
        {
            await input.ReadExactlyAsync(header, ct).ConfigureAwait(false);
            if (header[0] == 'I' && header[1] == 'D' && header[2] == '3')
            {
                if (header[3] is not (3 or 4) || header.Skip(6).Any(value => value > 127)) throw new InvalidDataException("The MP3 clip has an unsupported ID3 header.");
                end = 10 + (header[6] << 21 | header[7] << 14 | header[8] << 7 | header[9]) + (header[3] == 4 && (header[5] & 16) != 0 ? 10 : 0);
                if (end > 1024 * 1024 || end >= input.Length) throw new InvalidDataException("The MP3 clip has an invalid ID3 interval.");
            }
            int? signature = null;
            while (input.Length - end >= 4)
            {
                ct.ThrowIfCancellationRequested(); input.Position = end; await input.ReadExactlyAsync(header.AsMemory(0, 4), ct).ConfigureAwait(false);
                var bits = (uint)(header[0] << 24 | header[1] << 16 | header[2] << 8 | header[3]); var version = (int)(bits >> 19 & 3); var layer = bits >> 17 & 3; var bitrate = (int)(bits >> 12 & 15); var rate = (int)(bits >> 10 & 3);
                if (bits >> 21 != 0x7ff || version == 1 || layer != 1 || bitrate is 0 or 15 || rate == 3 || ++frames > 30000000) throw new InvalidDataException("The MP3 clip has an invalid or incomplete frame inventory.");
                // Version, sample rate and mono/stereo role must stay stable.
                var current = version * 16 + rate * 2 + ((bits >> 6 & 3) == 3 ? 1 : 0);
                if (signature is { } expected && expected != current) throw new InvalidDataException("The MP3 clip changes its audio profile."); signature = current;
                var sampleRate = SampleRates[rate] / (version == 3 ? 1 : version == 2 ? 2 : 4);
                var table = version == 3 ? Mpeg1Rates : Mpeg2Rates;
                var length = (version == 3 ? 144000 : 72000) * table[bitrate] / sampleRate + (int)(bits >> 9 & 1);
                if (length < 24 || length > input.Length - end) throw new InvalidDataException("The MP3 clip truncates an encoded audio frame."); end += length;
            }
            if (frames < 2) throw new InvalidDataException("The MP3 clip has no complete frame timeline.");
            if (input.Length == end) return false;
            input.Position = end; if (input.Length - end != 1 || input.ReadByte() != 0xff) throw new InvalidDataException("The MP3 clip has an unsupported trailing payload.");
        }
        var temporary = path + ".clip-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                var buffer = new byte[65536]; var remaining = end;
                while (remaining > 0) { var count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct).ConfigureAwait(false); if (count == 0) throw new EndOfStreamException("The MP3 clipping input changed during normalization."); await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false); remaining -= count; }
                if (input.Length != end + 1 || input.ReadByte() != 0xff) throw new InvalidDataException("The MP3 clipping boundary changed during normalization.");
                await output.FlushAsync(ct).ConfigureAwait(false); output.Flush(true);
            }
            ct.ThrowIfCancellationRequested(); File.SetLastWriteTimeUtc(temporary, info.LastWriteTimeUtc); File.Move(temporary, path, true); return true;
        }
        finally { try { File.Delete(temporary); } catch (IOException) { } }
    }
}
