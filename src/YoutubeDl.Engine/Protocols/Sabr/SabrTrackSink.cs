namespace YoutubeDl.Engine.Protocols.Sabr;

/// <summary>Writes ordered initialization/media segments into caller-owned staging streams.</summary>
public sealed class SabrTrackSink
{
    private sealed class Track(Stream output)
    {
        public Stream Output { get; } = output;
        public bool Initialized;
        public uint LastSegment;
        public long Bytes;
        public long StartMs;
        public long EndMs;
        public SabrTimeRange? TimeRange;
    }
    private readonly Dictionary<SabrFormatId, Track> _tracks;
    private readonly long _maximumTrackBytes;
    internal bool WriteFailed { get; private set; }
    internal Func<SabrSegment,CancellationToken,Task>? ValidateCheckpoint {get;init;}
    public SabrTrackSink(IReadOnlyDictionary<SabrFormatId, Stream> outputs, long maximumTrackBytes = 100L * 1024 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTrackBytes);
        if (outputs.Count is 0 or > 16 || outputs.Values.Any(output => !output.CanWrite || output.CanSeek && (output.Length != 0 || output.Position != 0)) ||
            outputs.Values.Distinct(ReferenceEqualityComparer.Instance).Count() != outputs.Count)
            throw new ArgumentException("Supply separate, empty writable staging streams for the selected tracks.");
        _tracks = outputs.ToDictionary(pair => pair.Key, pair => new Track(pair.Value));
        _maximumTrackBytes = maximumTrackBytes;
    }
    public async Task AcceptAsync(SabrSegment segment, CancellationToken ct)
    {
        if (!_tracks.TryGetValue(segment.Format, out var track)) return;
        if (segment.Initialization)
        {
            if (track.Initialized) throw new InvalidDataException("A SABR track received conflicting initialization segments.");
        }
        else if (!track.Initialized || segment.Number != track.LastSegment + 1)
            throw new InvalidDataException("A SABR track is missing initialization or contains a media sequence gap.");
        if (!segment.Initialization && (segment.StartMs < 0 || segment.DurationMs <= 0 || segment.DurationMs > long.MaxValue - segment.StartMs ||
            segment.Number > int.MaxValue || track.LastSegment == 0 && segment.StartMs != 0 ||
            track.LastSegment > 0 && (segment.StartMs < track.EndMs - 1 || segment.StartMs > track.EndMs && segment.StartMs - track.EndMs > 1 || segment.StartMs + segment.DurationMs <= track.EndMs)))
            throw new InvalidDataException("The SABR track has an invalid, stalled, or discontinuous media timeline.");
        if (segment.Data.Length > _maximumTrackBytes - track.Bytes) throw new InvalidDataException("The SABR track exceeds the configured size limit.");
        if (!segment.Initialization && segment.TimeRange is { } time)
        {
            if (time.StartTicks < 0 || time.DurationTicks <= 0 || time.DurationTicks > long.MaxValue - time.StartTicks || time.Timescale <= 0 ||
                time.StartMs != segment.StartMs || time.EndMs != segment.StartMs + segment.DurationMs)
                throw new InvalidDataException("The SABR segment's precise timeline is invalid.");
            if (track.TimeRange is { } prior && (prior.Timescale != time.Timescale || prior.StartTicks + prior.DurationTicks != time.StartTicks))
                throw new InvalidDataException("The SABR precise timeline has a gap or changed timescale.");
        }
        if(ValidateCheckpoint is not null)await ValidateCheckpoint(segment,ct).ConfigureAwait(false);
        try { await track.Output.WriteAsync(segment.Data, ct).ConfigureAwait(false); }
        catch { WriteFailed = true; throw; }
        track.Bytes += segment.Data.Length;
        if (segment.Initialization) track.Initialized = true;
        else
        {
            track.TimeRange = track.LastSegment == 0 ? segment.TimeRange : track.TimeRange is { } old && segment.TimeRange is { } current
                ? new(old.StartTicks, current.StartTicks + current.DurationTicks - old.StartTicks, old.Timescale) : null;
            if (track.LastSegment == 0) track.StartMs = segment.StartMs;
            track.EndMs = segment.StartMs + segment.DurationMs;
            track.LastSegment = segment.Number;
        }
    }
    public long TotalBytes => _tracks.Values.Sum(track => track.Bytes);
    internal uint LastSegment(SabrFormatId format)=>_tracks[format].LastSegment;
    public IReadOnlyList<SabrFormatId> SelectedFormats => _tracks.Keys.ToArray();
    public IReadOnlyList<SabrFormatId> InitializedFormats => _tracks.Where(pair => pair.Value.Initialized).Select(pair => pair.Key).ToArray();
    public IReadOnlyList<SabrBufferedRange> BufferedRanges => _tracks.Where(pair => pair.Value.LastSegment > 0)
        .Select(pair => new SabrBufferedRange(pair.Key, pair.Value.StartMs, pair.Value.EndMs - pair.Value.StartMs, 1, pair.Value.LastSegment, pair.Value.TimeRange)).ToArray();
    public bool IsComplete(SabrControlState control) => _tracks.All(pair => pair.Value.Initialized && pair.Value.LastSegment > 0 &&
        control.LastSegments.TryGetValue(pair.Key, out var last) && pair.Value.LastSegment == last &&
        (!control.EndTimesMs.TryGetValue(pair.Key, out var end) || Math.Abs(pair.Value.EndMs - end) <= 1));
    public void EnsureComplete(SabrControlState control)
    {
        if (!IsComplete(control)) throw new EndOfStreamException("The SABR response did not complete every selected media track.");
    }
}
