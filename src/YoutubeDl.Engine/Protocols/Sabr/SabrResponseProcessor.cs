namespace YoutubeDl.Engine.Protocols.Sabr;

public static class SabrResponseProcessor
{
    public static async Task ReadAsync(Stream response, SabrControlState control, SabrSegmentAssembler assembler,
        SabrTrackSink sink, CancellationToken ct, Download.ByteRateLimiter? limiter = null)
    {
        control.BeginResponse();
        await foreach (var part in UmpReader.ReadAsync(response, ct: ct).ConfigureAwait(false))
        {
            control.Accept(part);
            if (limiter is not null) await limiter.ThrottleAsync(part.Payload.Length, ct).ConfigureAwait(false);
            var segment = assembler.Accept(part);
            if (segment is not null) await sink.AcceptAsync(segment, ct).ConfigureAwait(false);
        }
        assembler.EnsureDrained();
    }
}
