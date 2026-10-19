using System.Buffers.Binary;
using System.Text;
using YoutubeDl.Engine.Media;

internal static class MovieChapterChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("MP4 chapter references bind only present track IDs including extended headers", () => Sync(References)),
        ("MP4 chapter references reject zero missing and duplicate track identities", () => Sync(Identities)),
        ("MP4 chapter header bounds reject malformed sizes versions and reference payloads", () => Sync(Bounds)),
        ("MP4 chapter file reading skips media bytes and rejects duplicate movie headers", Files)
    ];
    private static byte[] Box(string type, byte[] payload, bool extended = false)
    {
        var size = extended ? 16 : 8; var result = new byte[size + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(result, extended ? 1u : (uint)result.Length); Encoding.ASCII.GetBytes(type).CopyTo(result, 4);
        if (extended) BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(8), (ulong)result.Length);
        payload.CopyTo(result, size); return result;
    }
    private static byte[] Join(params byte[][] arrays) => arrays.SelectMany(array => array).ToArray();
    private static byte[] Track(uint id, uint? chapter = null, bool version1 = false, bool extended = false)
    {
        var header = new byte[version1 ? 24 : 16]; header[0] = version1 ? (byte)1 : (byte)0; BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(version1 ? 20 : 12), id);
        var reference = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(reference, chapter ?? 0);
        return Box("trak", Join(Box("tkhd", header, extended), chapter is null ? [] : Box("tref", Box("chap", reference))), extended);
    }
    private static void References()
    {
        foreach (var extended in new[] { false, true })
        {
            var references = Mp4ChapterReferences.Parse(Join(Track(1, 4, version1: true, extended: extended), Track(4, extended: extended)));
            if (!references.SetEquals([4u])) throw new InvalidOperationException("The chapter target was changed or inferred from track order.");
        }
        if (Mp4ChapterReferences.Parse(Join(Track(1), Track(4))).Count != 0) throw new InvalidOperationException("Unreferenced tracks were classified as chapters.");
    }
    private static void Identities()
    {
        foreach (var data in new[] { Join(Track(1, 0), Track(4)), Join(Track(1, 5), Track(4)), Join(Track(1, 4), Track(4), Track(4)), Track(0) }) Reject(() => Mp4ChapterReferences.Parse(data));
    }
    private static void Bounds()
    {
        var badHeader = new byte[16]; badHeader[0] = 2; var huge = Box("trak", []); BinaryPrimitives.WriteUInt32BigEndian(huge, uint.MaxValue);
        var validHeader = new byte[16]; BinaryPrimitives.WriteUInt32BigEndian(validHeader.AsSpan(12), 4);
        foreach (var data in new[] { huge, Box("trak", Box("tkhd", badHeader)), Box("trak", []), Box("trak", Join(Box("tkhd", new byte[3]), Box("tref", Box("chap", new byte[3])))),
            Join(Track(1, 4), Box("trak", Join(Box("tkhd", validHeader), Box("tref", Box("chap", new byte[3]))))), new byte[8 * 1024 * 1024 + 1] })
            Reject(() => Mp4ChapterReferences.Parse(data));
    }
    private static async Task Files()
    {
        var path = Path.Combine(Path.GetTempPath(), "native-chapter-" + Guid.NewGuid() + ".mp4");
        try
        {
            var movie = Box("moov", Join(Track(1, 4), Track(4)), extended: true);
            await File.WriteAllBytesAsync(path, Join(Box("ftyp", new byte[12]), Box("mdat", new byte[128 * 1024]), movie));
            if (!(await Mp4ChapterReferences.ReadAsync(path, default)).SetEquals([4u])) throw new InvalidOperationException("Trailing movie metadata was not found.");
            await File.WriteAllBytesAsync(path, Join(movie, movie));
            try { await Mp4ChapterReferences.ReadAsync(path, default); throw new InvalidOperationException("Duplicate movie headers were accepted."); } catch (InvalidDataException) { }
            using var stop = new CancellationTokenSource(); stop.Cancel();
            try { await Mp4ChapterReferences.ReadAsync(path, stop.Token); throw new InvalidOperationException("Pre-canceled movie inspection completed."); } catch (OperationCanceledException) { }
        }
        finally { File.Delete(path); }
    }
    private static Task Sync(Action test) { test(); return Task.CompletedTask; }
    private static void Reject(Action run) { try { run(); } catch (InvalidDataException) { return; } throw new InvalidOperationException("Malformed chapter metadata was accepted."); }
}
