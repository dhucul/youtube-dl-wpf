using System.Text;
using YoutubeDl.Engine.Protocols;
using YoutubeDl.Engine.Protocols.Sabr;

internal static class SabrChecks
{
    private static readonly SabrFormatId Format = new(137, 100, "track=en");
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Native protobuf requests match independent wire vectors", () => Sync(Writer)),
        ("SABR playback requests include selected tracks and session data", () => Sync(Request)),
        ("Interleaved SABR media is emitted only after exact completion", () => Sync(Assembly)),
        ("Truncated, excessive, and changed SABR segments are rejected", () => Sync(BadSegments)),
        ("SABR control policies preserve session context and validate redirects", () => Sync(Control)),
        ("Unfinished or unordered SABR tracks cannot be published", TrackOrdering),
        ("The native UMP response pipeline assembles complete staged tracks", Pipeline)
    ];
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }

    private static void Writer()
    {
        var writer = new ProtobufWriter();
        writer.UInt64(1, 150);
        writer.String(2, "abc");
        writer.Float(3, 1);
        Assert(writer.ToArray().AsSpan().SequenceEqual(new byte[] { 8, 0x96, 1, 18, 3, 97, 98, 99, 29, 0, 0, 0x80, 0x3f }), "Wire output differs from the independent protobuf vector.");
        Throws<ArgumentOutOfRangeException>(() => writer.UInt64(0, 1));
        Throws<InvalidDataException>(() => new ProtobufWriter(4).String(1, "longer"));
    }

    private static void Request()
    {
        var request = new SabrRequest
        {
            ClientVersion = "fixture-version", PlaybackConfiguration = [1, 2], VideoFormats = [Format],
            AudioFormats = [new(140, 200)], ProofToken = [3, 4], PlaybackCookie = [5], Contexts = new Dictionary<uint, byte[]> { [7] = [8] }, PlaybackTimeMs = 1000
        };
        var fields = ProtobufReader.Read(request.Encode());
        Assert(fields.All(field => field.Number != 2) && fields.Single(field => field.Number == 4).Integer == 1000, "The first request advertised initialization that has not been received.");
        var selected = ProtobufReader.Read(fields.Single(field => field.Number == 17).Bytes);
        Assert(selected.Single(field => field.Number == 1).Integer == 137 && selected.Single(field => field.Number == 2).Integer == 100, "Selected video format changed.");
        var context = ProtobufReader.Read(fields.Single(field => field.Number == 19).Bytes);
        Assert(context.Single(field => field.Number == 2).Bytes.Span.SequenceEqual(new byte[] { 3, 4 }) && context.Single(field => field.Number == 3).Bytes.Span.SequenceEqual(new byte[] { 5 }), "Session bytes changed.");
        Throws<ArgumentException>(() => new SabrRequest { ClientVersion = "fixture", PlaybackConfiguration = [1] }.Encode());
    }

    private static void Assembly()
    {
        using var assembler = new SabrSegmentAssembler("fixture-video");
        Assert(assembler.Accept(Header(1, 1, false, 3)) is null, "A header emitted media.");
        assembler.Accept(Header(2, 2, false, 2));
        assembler.Accept(new(21, new byte[] { 1, 65 }));
        assembler.Accept(new(21, new byte[] { 2, 88, 89 }));
        assembler.Accept(new(21, new byte[] { 1, 66, 67 }));
        var second = assembler.Accept(new(22, new byte[] { 2 }));
        var first = assembler.Accept(new(22, new byte[] { 1 }));
        Assert(Encoding.ASCII.GetString(first!.Data.Span) == "ABC" && Encoding.ASCII.GetString(second!.Data.Span) == "XY", "Interleaved segment bytes were mixed.");
        assembler.EnsureDrained();
        assembler.Accept(Header(3, 1, false, 3));
        assembler.Accept(new(21, new byte[] { 3, 65, 66, 67 }));
        Assert(assembler.Accept(new(22, new byte[] { 3 })) is null, "A retransmission was emitted twice.");
    }

    private static void BadSegments()
    {
        using var assembler = new SabrSegmentAssembler("fixture-video", maximumSegmentBytes: 8, maximumBufferedBytes: 8);
        Throws<InvalidDataException>(() => assembler.Accept(new(21, new byte[] { 1, 65 })));
        Throws<InvalidDataException>(() => assembler.Accept(Header(1, 1, false, 9)));
        assembler.Accept(Header(1, 1, false, 3));
        Throws<InvalidDataException>(() => assembler.Accept(new(21, new byte[] { 1, 1, 2, 3, 4 })));
        assembler.Accept(new(21, new byte[] { 1, 1 }));
        Throws<InvalidDataException>(() => assembler.Accept(new(22, new byte[] { 1 })));
        Throws<EndOfStreamException>(assembler.EnsureDrained);
        using var changed = new SabrSegmentAssembler("fixture-video");
        foreach (var value in new byte[] { 1, 2 })
        {
            changed.Accept(Header(1, 1, false, 1));
            changed.Accept(new(21, new byte[] { 1, value }));
            if (value == 1) changed.Accept(new(22, new byte[] { 1 }));
            else Throws<InvalidDataException>(() => changed.Accept(new(22, new byte[] { 1 })));
        }
        Throws<InvalidDataException>(() => changed.Accept(Header(2, 2, false, 1, video: "different-video")));
    }

    private static void Control()
    {
        var control = new SabrControlState("fixture-video");
        var context = new ProtobufWriter(); context.UInt64(1, 7); context.Bytes(3, [1, 2]); context.UInt64(4, 1);
        control.Accept(new(57, context.ToArray()));
        Assert(control.OutgoingContexts[7].AsSpan().SequenceEqual(new byte[] { 1, 2 }), "Context update missing.");
        var keep = new ProtobufWriter(); keep.UInt64(1, 7); keep.Bytes(3, [3]); keep.UInt64(5, 2);
        control.Accept(new(57, keep.ToArray()));
        Assert(control.OutgoingContexts[7][0] == 1, "Keep-existing context was overwritten.");
        var policy = new ProtobufWriter(); policy.UInt64(4, 100); policy.Bytes(7, [5, 6]); policy.String(8, "fixture-video");
        control.Accept(new(35, policy.ToArray()));
        Assert(control.BackoffMilliseconds == 100 && control.PlaybackCookie[0] == 5, "Retry policy/session cookie missing.");
        var redirect = new ProtobufWriter(); redirect.String(1, "https://r2.googlevideo.com/videoplayback?fixture=redirect");
        control.Accept(new(43, redirect.ToArray()));
        Assert(control.Redirect!.Host == "r2.googlevideo.com", "Streaming redirect missing.");
        var unsafeRedirect = new ProtobufWriter(); unsafeRedirect.String(1, "https://googlevideo.com.evil.example/media");
        Throws<InvalidDataException>(() => control.Accept(new(43, unsafeRedirect.ToArray())));
        var protection = new ProtobufWriter(); protection.UInt64(1, 3);
        Throws<NotSupportedException>(() => control.Accept(new(58, protection.ToArray())));
        var wrongVideo = new ProtobufWriter(); wrongVideo.String(8, "different-video");
        Throws<InvalidDataException>(() => control.Accept(new(35, wrongVideo.ToArray())));
    }

    private static async Task TrackOrdering()
    {
        using var output = new MemoryStream();
        var sink = new SabrTrackSink(new Dictionary<SabrFormatId, Stream> { [Format] = output });
        var state = new SabrControlState("fixture-video");
        Throws<EndOfStreamException>(() => sink.EnsureComplete(state));
        await Expect<InvalidDataException>(() => sink.AcceptAsync(new(Format, 1, false, 0, 1, new byte[] { 1 }), default));
        await sink.AcceptAsync(new(Format, 0, true, 0, 0, new byte[] { 1 }), default);
        await Expect<InvalidDataException>(() => sink.AcceptAsync(new(Format, 2, false, 1, 1, new byte[] { 2 }), default));
        await sink.AcceptAsync(new(Format, 1, false, 0, 1, new byte[] { 2 }), default);
        state.Accept(Initialization(1));
        sink.EnsureComplete(state);
        Assert(output.ToArray().AsSpan().SequenceEqual(new byte[] { 1, 2 }), "Ordered track bytes differ.");
    }

    private static async Task Pipeline()
    {
        using var output = new MemoryStream();
        using var response = new MemoryStream();
        WritePart(response, Initialization(1));
        WritePart(response, Header(1, 0, true, 2));
        WritePart(response, new(21, new byte[] { 1, 1, 2 }));
        WritePart(response, new(22, new byte[] { 1 }));
        WritePart(response, Header(2, 1, false, 3));
        WritePart(response, new(21, new byte[] { 2, 3, 4, 5 }));
        WritePart(response, new(22, new byte[] { 2 }));
        response.Position = 0;
        var state = new SabrControlState("fixture-video");
        using var assembler = new SabrSegmentAssembler("fixture-video");
        var sink = new SabrTrackSink(new Dictionary<SabrFormatId, Stream> { [Format] = output });
        await SabrResponseProcessor.ReadAsync(response, state, assembler, sink, default);
        sink.EnsureComplete(state);
        Assert(output.ToArray().AsSpan().SequenceEqual(new byte[] { 1, 2, 3, 4, 5 }), "The response pipeline altered staged media.");
    }

    private static UmpPart Header(byte id, uint number, bool initialization, int length, string video = "fixture-video")
    {
        var writer = new ProtobufWriter();
        writer.UInt64(1, id); writer.String(2, video); writer.UInt64(3, Format.Itag); writer.UInt64(4, Format.LastModified); writer.String(5, Format.ExtraTags);
        writer.UInt64(8, initialization ? 1UL : 0); writer.UInt64(9, number); writer.UInt64(11, 0); writer.UInt64(12, initialization ? 0UL : 1000); writer.UInt64(14, (ulong)length);
        return new(20, writer.ToArray());
    }
    private static UmpPart Initialization(uint last)
    {
        var writer = new ProtobufWriter(); writer.String(1, "fixture-video");
        writer.Message(2, format => { format.UInt64(1, Format.Itag); format.UInt64(2, Format.LastModified); format.String(3, Format.ExtraTags); });
        writer.UInt64(4, last);
        return new(42, writer.ToArray());
    }
    private static void WritePart(Stream stream, UmpPart part)
    {
        if (part.Type >= 128 || part.Payload.Length >= 128) throw new InvalidOperationException("This independent fixture uses one-byte UMP lengths.");
        stream.WriteByte((byte)part.Type); stream.WriteByte((byte)part.Payload.Length); stream.Write(part.Payload.Span);
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private static async Task Expect<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
}
