namespace YoutubeDl.Engine.Protocols.Sabr;

public sealed record SabrTimeRange(long StartTicks, long DurationTicks, int Timescale)
{
    public long StartMs => ConvertTicks(StartTicks, Timescale, ceiling: false);
    public long EndMs => ConvertTicks(checked(StartTicks + DurationTicks), Timescale, ceiling: true);

    internal static SabrTimeRange Read(ReadOnlyMemory<byte> bytes, bool initialization = false)
    {
        var fields = ProtobufReader.Read(bytes, maximumBytes: 1024);
        var start = SabrSegmentAssembler.RequiredInteger(fields, 1);
        var duration = SabrSegmentAssembler.RequiredInteger(fields, 2);
        var scale = SabrSegmentAssembler.RequiredInteger(fields, 3);
        if (start > long.MaxValue || duration > long.MaxValue - start || scale is 0 or > int.MaxValue || !initialization && duration == 0)
            throw new InvalidDataException("The SABR tick-based media time range is invalid.");
        var result = new SabrTimeRange((long)start, (long)duration, (int)scale);
        _ = result.EndMs;
        return result;
    }

    internal static long ConvertTicks(long ticks, int timescale, bool ceiling)
    {
        if (ticks < 0 || timescale <= 0) throw new InvalidDataException("The SABR tick timestamp is invalid.");
        var value = ticks * 1000m / timescale;
        if (value > long.MaxValue) throw new InvalidDataException("The SABR tick timestamp exceeds the supported time range.");
        return (long)(ceiling ? decimal.Ceiling(value) : decimal.Floor(value));
    }
}
