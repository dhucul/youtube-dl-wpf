using System.Buffers.Binary;
using System.Text;
using YoutubeDl.Engine.Media;

internal static class MovieAncillaryChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All => [
        ("MP4 legacy ancillary handlers bind actual track IDs rather than stream order", () => Run(Identities)),
        ("MP4 legacy streaming classification excludes only verified hint and system descriptors", () => Run(Classification)),
        ("MP4 legacy handler parsing rejects duplicate missing truncated and oversized records", () => Run(Bounds)),
        ("MP4 legacy file inspection reads trailing headers and honors cancellation", Files)
    ];
    private static byte[] Box(string type, params byte[][] data)
    {
        var body = data.SelectMany(bytes => bytes).ToArray(); var result = new byte[8 + body.Length]; BinaryPrimitives.WriteUInt32BigEndian(result, (uint)result.Length); Encoding.ASCII.GetBytes(type).CopyTo(result, 4); body.CopyTo(result, 8); return result;
    }
    private static byte[] Track(uint id, string handler, bool version1 = false)
    {
        var header = new byte[version1 ? 24 : 16]; header[0] = version1 ? (byte)1 : (byte)0; BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(version1 ? 20 : 12), id);
        var media = new byte[12]; Encoding.ASCII.GetBytes(handler).CopyTo(media, 8); return Box("trak", Box("tkhd", header), Box("mdia", Box("hdlr", media)));
    }
    private static void Identities()
    {
        var data = new[] { Track(41, "hint"), Track(7, "vide", true), Track(15, "odsm"), Track(13, "sdsm") }.SelectMany(bytes => bytes).ToArray();
        var types = Mp4ChapterReferences.ParseTrackHandlers(data); if (types[7] != "vide" || types[41] != "hint" || types[15] != "odsm" || types[13] != "sdsm") throw new InvalidOperationException("Handler identity was inferred from track ordering.");
    }
    private static void Classification()
    {
        foreach (var tuple in new[] { ("rtp ", "hint"), ("mp4s", "odsm"), ("mp4s", "sdsm") }) if (!Mp4ChapterReferences.IsLegacyAncillary(tuple.Item1, tuple.Item2)) throw new InvalidOperationException("A known streaming/system track was missed.");
        foreach (var tuple in new[] { ("tmcd", "tmcd"), ("text", "text"), ("mp4s", "meta"), ("rtp ", "vide"), ("mp4a", "soun"), ("avc1", "vide") })
            if (Mp4ChapterReferences.IsLegacyAncillary(tuple.Item1, tuple.Item2)) throw new InvalidOperationException("Unknown data or playback metadata was classified for removal.");
    }
    private static void Bounds()
    {
        var header = new byte[16]; BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), 1);
        foreach (var data in new[] { Track(0, "hint"), Track(1, "hint").Concat(Track(1, "vide")).ToArray(), Box("trak", Box("tkhd", header)),
            Box("trak", Box("tkhd", header), Box("mdia", Box("hdlr", new byte[3]))), Box("trak", Box("tkhd", header), Box("mdia", Box("hdlr", new byte[12]), Box("hdlr", new byte[12]))), new byte[8 * 1024 * 1024 + 1] })
            Reject(() => Mp4ChapterReferences.ParseTrackHandlers(data));
    }
    private static async Task Files()
    {
        var path = Path.Combine(Path.GetTempPath(), "native-handlers-" + Guid.NewGuid() + ".mp4");
        try
        {
            var data = Box("moov", Track(81, "hint"), Track(72, "vide")); await File.WriteAllBytesAsync(path, Box("ftyp", new byte[12]).Concat(Box("mdat", new byte[32000])).Concat(data).ToArray());
            var handlers = Mp4ChapterReferences.ParseTrackHandlers(await Mp4ChapterReferences.ReadMovieAsync(path, default)); if (handlers[81] != "hint" || handlers[72] != "vide") throw new InvalidOperationException("Trailing movie header was not read.");
            using var stop = new CancellationTokenSource(); stop.Cancel(); try { await Mp4ChapterReferences.ReadMovieAsync(path, stop.Token); throw new InvalidOperationException("Canceled inspection continued."); } catch (OperationCanceledException) { }
        }
        finally { File.Delete(path); }
    }
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new InvalidOperationException("Invalid track handlers were accepted."); }
    private static Task Run(Action action) { action(); return Task.CompletedTask; }
}
