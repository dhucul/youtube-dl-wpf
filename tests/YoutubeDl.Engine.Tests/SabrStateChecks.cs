using YoutubeDl.Engine.Protocols;
using YoutubeDl.Engine.Protocols.Sabr;

internal static class SabrStateChecks
{
    private static readonly SabrFormatId Video = new(137, 100);
    private static readonly SabrFormatId Audio = new(140, 200);
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("SABR requests acknowledge only received initialization and exact buffered ranges", () => Sync(Wire)),
        ("Invalid, duplicate or unselected SABR state fails before request encoding", () => Sync(Invalid)),
        ("SABR track progress preserves independent initialization and rounded timelines", Progress),
        ("SABR timeline gaps, repeats and invalid times never alter staged bytes", Timeline),
        ("SABR full-duration metadata cannot make short received tracks complete", Duration),
        ("SABR tracks require separate empty staging streams", () => Sync(Staging))
    ];

    private static SabrRequest Request => new() { ClientVersion = "fixture", PlaybackConfiguration = [1], VideoFormats = [Video] };
    private static void Wire()
    {
        var first = ProtobufReader.Read(Request.Encode());
        Assert(first.All(field => field.Number is not (2 or 3)), "A fresh request claimed downloaded initialization or media.");
        var later = ProtobufReader.Read((Request with { InitializedFormats = [Video], BufferedRanges = [new(Video, 0, 2000, 1, 2)] }).Encode());
        Assert(later.Single(field => field.Number == 2).Bytes.Span.SequenceEqual(new byte[] { 8, 137, 1, 16, 100 }), "Initialization acknowledgment differs from the independent protobuf vector.");
        Assert(later.Single(field => field.Number == 3).Bytes.Span.SequenceEqual(new byte[] { 10, 5, 8, 137, 1, 16, 100, 16, 0, 24, 0xd0, 0x0f, 32, 1, 40, 2 }), "Buffered duration/segment indexes differ from the independent protobuf vector.");
    }

    private static void Invalid()
    {
        Throws<ArgumentException>(() => (Request with { InitializedFormats = [Audio] }).Encode());
        Throws<ArgumentException>(() => (Request with { InitializedFormats = [Video, Video] }).Encode());
        Throws<ArgumentException>(() => (Request with { AudioFormats = [Video] }).Encode());
        Throws<ArgumentException>(() => (Request with { BufferedRanges = [new(Video, 0, 1000, 1, 1)] }).Encode());
        foreach (var range in new[] { new SabrBufferedRange(Video, -1, 1000, 1, 1), new(Video, 0, 0, 1, 1), new(Video, 0, 1000, 0, 1), new(Video, 0, 1000, 2, 1), new(Video, long.MaxValue, 1, 1, 1), new(Video, 0, 1000, 1, uint.MaxValue), new(Audio, 0, 1000, 1, 1) })
            Throws<ArgumentException>(() => (Request with { InitializedFormats = [Video], BufferedRanges = [range] }).Encode());
        Throws<ArgumentException>(() => (Request with { InitializedFormats = [Video], BufferedRanges = [new(Video, 0, 1000, 1, 1), new(Video, 0, 1000, 1, 1)] }).Encode());
    }

    private static async Task Progress()
    {
        using var video = new MemoryStream(); using var audio = new MemoryStream();
        var sink = new SabrTrackSink(new Dictionary<SabrFormatId, Stream> { [Video] = video, [Audio] = audio });
        Assert(sink.InitializedFormats.Count == 0 && sink.BufferedRanges.Count == 0, "New tracks advertised data.");
        await sink.AcceptAsync(new(Video, 0, true, 0, 0, new byte[] { 1, 2 }), default);
        Assert(sink.InitializedFormats.SequenceEqual(new[] { Video }) && sink.BufferedRanges.Count == 0, "Initialization was applied to an unreceived track.");
        await sink.AcceptAsync(new(Video, 1, false, 0, 4034, new byte[] { 3 }), default);
        await sink.AcceptAsync(new(Video, 2, false, 4033, 4334, new byte[] { 4 }), default);
        Assert(sink.BufferedRanges.Single() == new SabrBufferedRange(Video, 0, 8367, 1, 2) && sink.TotalBytes == 4, "Rounded segment boundaries accumulated the wrong duration or bytes.");
        await sink.AcceptAsync(new(Audio, 0, true, 0, 0, new byte[] { 5 }), default);
        await sink.AcceptAsync(new(Audio, 1, false, 0, 9985, new byte[] { 6 }), default);
        Assert(sink.InitializedFormats.Count == 2 && sink.BufferedRanges.Single(range => range.Format == Audio).DurationMs == 9985 &&
            video.ToArray().AsSpan().SequenceEqual(new byte[] { 1, 2, 3, 4 }) && audio.ToArray().AsSpan().SequenceEqual(new byte[] { 5, 6 }), "Track data or progress was mixed.");
    }

    private static async Task Timeline()
    {
        using var output = new MemoryStream();
        var sink = new SabrTrackSink(new Dictionary<SabrFormatId, Stream> { [Video] = output });
        await sink.AcceptAsync(new(Video, 0, true, 0, 0, new byte[] { 1 }), default);
        await Expect<InvalidDataException>(() => sink.AcceptAsync(new(Video, 1, false, 1000, 1000, new byte[] { 2 }), default));
        await sink.AcceptAsync(new(Video, 1, false, 0, 1000, new byte[] { 2 }), default);
        foreach (var (start, duration) in new[] { (5000L, 1000L), (0, 2000), (1000, 0), (long.MaxValue, 1), (-1, 1000) })
            await Expect<InvalidDataException>(() => sink.AcceptAsync(new(Video, 2, false, start, duration, new byte[] { 3 }), default));
        Assert(output.Length == 2 && sink.BufferedRanges.Single().LastSegment == 1, "Invalid time ranges changed staged bytes or progress.");
        await sink.AcceptAsync(new(Video, 2, false, 999, 1000, new byte[] { 3 }), default);
        Assert(sink.BufferedRanges.Single().DurationMs == 1999, "One-millisecond rounding overlap was rejected or double-counted.");
    }

    private static async Task Duration()
    {
        using var output = new MemoryStream();
        var sink = new SabrTrackSink(new Dictionary<SabrFormatId, Stream> { [Video] = output });
        await sink.AcceptAsync(new(Video, 0, true, 0, 0, new byte[] { 1 }), default);
        await sink.AcceptAsync(new(Video, 1, false, 0, 1000, new byte[] { 2 }), default);
        var shortState = State(2000);
        Assert(!sink.IsComplete(shortState), "A full-duration header made a short track appear complete.");
        Throws<EndOfStreamException>(() => sink.EnsureComplete(shortState));
        Assert(sink.IsComplete(State(1000)) && sink.IsComplete(State(1001)), "Matching duration or one-millisecond rounding was rejected.");
        Throws<InvalidDataException>(() => State(ulong.MaxValue));
        var changed = Metadata(3000);
        Throws<InvalidDataException>(() => shortState.Accept(new(42, changed)));

        static SabrControlState State(ulong duration)
        { var state = new SabrControlState("fixture-video"); state.Accept(new(42, Metadata(duration))); return state; }
        static byte[] Metadata(ulong duration)
        {
            var metadata = new ProtobufWriter(); metadata.String(1, "fixture-video");
            metadata.Message(2, format => { format.UInt64(1, 137); format.UInt64(2, 100); }); metadata.UInt64(3, duration); metadata.UInt64(4, 1);
            return metadata.ToArray();
        }
    }

    private static void Staging()
    {
        using var previous = new MemoryStream(new byte[] { 9 });
        Throws<ArgumentException>(() => new SabrTrackSink(new Dictionary<SabrFormatId, Stream> { [Video] = previous }));
        Assert(previous.ToArray().Single() == 9, "Pre-existing content was changed.");
        using var shared = new MemoryStream();
        Throws<ArgumentException>(() => new SabrTrackSink(new Dictionary<SabrFormatId, Stream> { [Video] = shared, [Audio] = shared }));
    }

    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
