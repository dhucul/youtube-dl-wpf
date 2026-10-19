namespace YoutubeDl.Engine.Protocols.Sabr;

public sealed record SabrFormatId(uint Itag, ulong LastModified, string ExtraTags = "");
public sealed record SabrBufferedRange(SabrFormatId Format, long StartMs, long DurationMs, uint FirstSegment, uint LastSegment, SabrTimeRange? TimeRange = null);

public sealed record SabrRequest
{
    public required string ClientVersion { get; init; }
    public uint ClientName { get; init; } = 1;
    public required byte[] PlaybackConfiguration { get; init; }
    public IReadOnlyList<SabrFormatId> VideoFormats { get; init; } = [];
    public IReadOnlyList<SabrFormatId> AudioFormats { get; init; } = [];
    /// <summary>Formats whose initialization bytes have already been received, not requests for initialization.</summary>
    public IReadOnlyList<SabrFormatId> InitializedFormats { get; init; } = [];
    public IReadOnlyList<SabrBufferedRange> BufferedRanges { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[] ProofToken { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[] ClientInfo { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[] ClientAbrState { get; init; } = [];
    public SabrAudioTrack? AudioTrack { get; init; }
    public byte[] PlaybackCookie { get; init; } = [];
    public IReadOnlyDictionary<uint, byte[]> Contexts { get; init; } = new Dictionary<uint, byte[]>();
    public long PlaybackTimeMs { get; init; }
    public long ElapsedTimeMs { get; init; }
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;

    public byte[] Encode()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(PlaybackTimeMs);
        ArgumentOutOfRangeException.ThrowIfNegative(ElapsedTimeMs);
        if (ProofToken.Length > 16384) throw new ArgumentException("The SABR proof exceeds its supported size.");
        if (ClientInfo.Length > 0)
        {
            var fields = ProtobufReader.Read(ClientInfo, maximumBytes: 16384, maximumFields: 128);
            var names = fields.Where(field => field.Number == 16).ToArray(); var versions = fields.Where(field => field.Number == 17).ToArray();
            if (names.Length != 1 || names[0].WireType != 0 || names[0].Integer != ClientName || versions.Length != 1 || versions[0].WireType != 2 ||
                !versions[0].Bytes.Span.SequenceEqual(System.Text.Encoding.UTF8.GetBytes(ClientVersion)))
                throw new ArgumentException("The SABR client information does not match its declared client identity.");
        }
        if (Width <= 0 || Height <= 0 || string.IsNullOrWhiteSpace(ClientVersion) || ClientVersion.Length > 128 ||
            PlaybackConfiguration.Length == 0 || VideoFormats.Count + AudioFormats.Count == 0 || VideoFormats.Count + AudioFormats.Count > 16 || Contexts.Count > 64)
            throw new ArgumentException("The SABR playback request has invalid configuration, dimensions, or selected formats.");
        var selected = AudioFormats.Concat(VideoFormats).ToHashSet();
        if (selected.Count != AudioFormats.Count + VideoFormats.Count || InitializedFormats.Count > selected.Count ||
            InitializedFormats.Distinct().Count() != InitializedFormats.Count || InitializedFormats.Any(format => !selected.Contains(format)) ||
            BufferedRanges.Count > selected.Count || BufferedRanges.Select(range => range.Format).Distinct().Count() != BufferedRanges.Count)
            throw new ArgumentException("The SABR playback request has ambiguous or unselected format state.");
        foreach (var range in BufferedRanges)
        {
            if (!InitializedFormats.Contains(range.Format) || range.StartMs < 0 || range.DurationMs <= 0 ||
                range.DurationMs > long.MaxValue - range.StartMs || range.FirstSegment == 0 || range.LastSegment < range.FirstSegment || range.LastSegment > int.MaxValue)
                throw new ArgumentException("The SABR buffered range is invalid or has no received initialization.");
            if (range.TimeRange is { } time && (time.StartTicks < 0 || time.DurationTicks <= 0 || time.DurationTicks > long.MaxValue - time.StartTicks || time.Timescale <= 0 ||
                time.StartMs != range.StartMs || time.EndMs != range.StartMs + range.DurationMs)) throw new ArgumentException("The SABR precise buffered timeline is inconsistent.");
        }
        var writer = new ProtobufWriter();
        writer.Message(1, state =>
        {
            if (ClientAbrState.Length > 0)
            {
                var fields = ProtobufReader.Read(ClientAbrState, maximumBytes: 16384, maximumFields: 256);
                var mutable = AudioTrack is null ? new[] { 16, 18, 19, 28, 36, 40 } : new[] { 16, 18, 19, 28, 36, 40, 46, 69, 76 };
                foreach (var number in mutable)
                {
                    var offered = fields.Where(field => field.Number == number).ToArray();
                    if (offered.Length > 1 || offered.Any(field => field.WireType != (number == 69 ? 2 : 0))) throw new InvalidDataException("The observed playback state has ambiguous mutable fields.");
                }
                foreach (var field in fields.Where(field => !mutable.Contains(field.Number))) state.Field(field);
                state.UInt64(16, (ulong)Height); state.UInt64(18, (ulong)Width); state.UInt64(19, (ulong)Height);
                state.UInt64(28, (ulong)PlaybackTimeMs); state.UInt64(36, (ulong)ElapsedTimeMs);
                state.UInt64(40, (ulong)((AudioFormats.Count > 0 ? 1 : 0) | (VideoFormats.Count > 0 ? 2 : 0)));
                WriteAudioSelection(state);
                return;
            }
            state.UInt64(16, (ulong)Height);
            state.UInt64(18, (ulong)Width);
            state.UInt64(19, (ulong)Height);
            state.UInt64(22, 0);
            state.UInt64(23, 8_000_000);
            state.UInt64(28, (ulong)PlaybackTimeMs);
            state.UInt64(34, 1);
            state.Float(35, 1);
            state.UInt64(36, (ulong)ElapsedTimeMs);
            state.UInt64(40, (ulong)((AudioFormats.Count > 0 ? 1 : 0) | (VideoFormats.Count > 0 ? 2 : 0)));
            WriteAudioSelection(state);
        });
        foreach (var format in InitializedFormats) writer.Message(2, nested => WriteFormat(nested, format));
        foreach (var range in BufferedRanges) writer.Message(3, nested =>
        {
            nested.Message(1, format => WriteFormat(format, range.Format));
            nested.UInt64(2, (ulong)range.StartMs);
            nested.UInt64(3, (ulong)range.DurationMs);
            nested.UInt64(4, range.FirstSegment);
            nested.UInt64(5, range.LastSegment);
            if (range.TimeRange is { } time) nested.Message(6, clock =>
            { clock.UInt64(1, (ulong)time.StartTicks); clock.UInt64(2, (ulong)time.DurationTicks); clock.UInt64(3, (ulong)time.Timescale); });
        });
        writer.UInt64(4, (ulong)PlaybackTimeMs);
        writer.Bytes(5, PlaybackConfiguration);
        foreach (var format in AudioFormats) writer.Message(16, nested => WriteFormat(nested, format));
        foreach (var format in VideoFormats) writer.Message(17, nested => WriteFormat(nested, format));
        writer.Message(19, context =>
        {
            if (ClientInfo.Length > 0) context.Bytes(1, ClientInfo);
            else context.Message(1, client => { client.UInt64(16, ClientName); client.String(17, ClientVersion); });
            if (ProofToken.Length > 0) context.Bytes(2, ProofToken);
            if (PlaybackCookie.Length > 0) context.Bytes(3, PlaybackCookie);
            foreach (var (type, value) in Contexts)
                context.Message(5, nested => { nested.UInt64(1, type); nested.Bytes(2, value); });
        });
        return writer.ToArray();
    }

    private void WriteAudioSelection(ProtobufWriter state)
    {
        if (AudioTrack is null) return;
        if (AudioFormats.Count != 1 || AudioTrack.TrackId is { } id && (id.Length > 256 || id.Any(char.IsControl))) throw new ArgumentException("The selected SABR audio track is invalid.");
        state.UInt64(46, AudioTrack.DrcEnabled && !AudioTrack.VoiceBoost ? 1UL : 0UL);
        if (!string.IsNullOrEmpty(AudioTrack.TrackId)) state.String(69, AudioTrack.TrackId);
        state.UInt64(76, AudioTrack.VoiceBoost ? 1UL : 0UL);
    }

    private static void WriteFormat(ProtobufWriter writer, SabrFormatId format)
    {
        if (format.Itag == 0 || format.ExtraTags.Length > 4096) throw new ArgumentException("The selected SABR format is invalid.");
        writer.UInt64(1, format.Itag);
        writer.UInt64(2, format.LastModified);
        if (format.ExtraTags.Length > 0) writer.String(3, format.ExtraTags);
    }
}
