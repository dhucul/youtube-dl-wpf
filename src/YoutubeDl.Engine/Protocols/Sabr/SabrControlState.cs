using System.Text;

namespace YoutubeDl.Engine.Protocols.Sabr;

public sealed class SabrControlState
{
    private readonly string _videoId;
    private readonly bool _allowStartupPlayback;
    private bool _seeded;
    private readonly Dictionary<uint, byte[]> _contexts = [];
    private readonly HashSet<uint> _sendContexts = [];
    private readonly Dictionary<SabrFormatId, uint> _lastSegments = [];
    private readonly Dictionary<SabrFormatId, long> _endTimes = [];
    public byte[] PlaybackCookie { get; private set; } = [];
    public int BackoffMilliseconds { get; private set; }
    public Uri? Redirect { get; private set; }
    public bool RequiresPlayerReload { get; private set; }
    public int? ProtectionStatus { get; private set; }
    public IReadOnlyDictionary<SabrFormatId, uint> LastSegments => _lastSegments;
    public IReadOnlyDictionary<SabrFormatId, long> EndTimesMs => _endTimes;
    public IReadOnlyDictionary<uint, byte[]> OutgoingContexts => _contexts.Where(pair => _sendContexts.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
    public SabrControlState(string videoId, bool allowStartupPlayback = false) { _videoId = videoId; _allowStartupPlayback = allowStartupPlayback; }
    internal void SeedSession(byte[] cookie, IReadOnlyDictionary<uint, byte[]> contexts)
    {
        if (_seeded) return;
        if (cookie.Length > 16384 || contexts.Count > 64 || contexts.Values.Any(value => value.Length > 16384) || contexts.Values.Sum(value => (long)value.Length) > 65536) throw new InvalidDataException("The initial playback context exceeds its limit.");
        PlaybackCookie = cookie.ToArray();
        foreach (var pair in contexts) { _contexts.Add(pair.Key, pair.Value.ToArray()); _sendContexts.Add(pair.Key); }
        _seeded = true;
    }

    public void Accept(UmpPart part)
    {
        if (part.Type is not (35 or 42 or 43 or 44 or 46 or 57 or 58 or 59)) return;
        var fields = ProtobufReader.Read(part.Payload, maximumBytes: 65536);
        switch (part.Type)
        {
            case 35:
                CheckVideo(SabrSegmentAssembler.OptionalBytes(fields, 8));
                var backoff = SabrSegmentAssembler.OptionalInteger(fields, 4);
                if (backoff > 60000) throw new InvalidDataException("The SABR retry delay exceeds the configured limit.");
                BackoffMilliseconds = (int)backoff;
                var cookie = SabrSegmentAssembler.OptionalBytes(fields, 7);
                if (!cookie.IsEmpty) PlaybackCookie = cookie.ToArray();
                break;
            case 42:
                CheckVideo(SabrSegmentAssembler.OptionalBytes(fields, 1));
                var format = ReadFormat(SabrSegmentAssembler.OptionalBytes(fields, 2));
                var last = SabrSegmentAssembler.RequiredInteger(fields, 4);
                if (last > uint.MaxValue) throw new InvalidDataException("The SABR final segment identifier is invalid.");
                if (_lastSegments.TryGetValue(format, out var previous) && previous != (uint)last)
                    throw new InvalidDataException("SABR changed the declared end of a selected track.");
                var end = SabrSegmentAssembler.OptionalInteger(fields, 3);
                if (fields.Any(field => field.Number is 9 or 10))
                {
                    var ticks = SabrSegmentAssembler.RequiredInteger(fields, 9);
                    var scale = SabrSegmentAssembler.RequiredInteger(fields, 10);
                    if (ticks > long.MaxValue || scale is 0 or > int.MaxValue) throw new InvalidDataException("The SABR track duration ticks are invalid.");
                    var fromTicks = SabrTimeRange.ConvertTicks((long)ticks, (int)scale, ceiling: true);
                    if (fields.Any(field => field.Number == 3) && Math.Abs((decimal)end - fromTicks) > 1)
                        throw new InvalidDataException("The SABR declared duration units disagree.");
                    end = (ulong)fromTicks;
                }
                if (fields.Any(field => field.Number is 3 or 9 or 10) && (end == 0 || end > long.MaxValue ||
                    _endTimes.TryGetValue(format, out var previousEnd) && previousEnd != (long)end))
                    throw new InvalidDataException("SABR returned an invalid or changed track duration.");
                _lastSegments[format] = (uint)last;
                if (end > 0) _endTimes[format] = (long)end;
                break;
            case 43:
                var raw = Text(SabrSegmentAssembler.OptionalBytes(fields, 1));
                if (!Uri.TryCreate(raw, UriKind.Absolute, out var redirect) || redirect.Scheme != "https" || !redirect.IsDefaultPort ||
                    !string.IsNullOrEmpty(redirect.UserInfo) || redirect.Fragment.Length != 0 || !redirect.Host.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SABR returned an invalid streaming redirect.");
                Redirect = redirect;
                break;
            case 44:
                var code = SabrSegmentAssembler.OptionalInteger(fields, 2);
                throw new InvalidOperationException($"YouTube streaming returned a SABR error (code {code}).");
            case 46:
                RequiresPlayerReload = true;
                break;
            case 57:
                var type = SabrSegmentAssembler.RequiredInteger(fields, 1);
                var value = SabrSegmentAssembler.OptionalBytes(fields, 3);
                if (type > uint.MaxValue || value.Length > 16384 || !_contexts.ContainsKey((uint)type) && _contexts.Count >= 64)
                    throw new InvalidDataException("The SABR playback context exceeds the configured limits.");
                if (!_contexts.ContainsKey((uint)type) || SabrSegmentAssembler.OptionalInteger(fields, 5) != 2)
                    _contexts[(uint)type] = value.ToArray();
                if (SabrSegmentAssembler.OptionalInteger(fields, 4) != 0) _sendContexts.Add((uint)type);
                break;
            case 58:
                var status = SabrSegmentAssembler.OptionalInteger(fields, 1);
                if (status > 3) throw new NotSupportedException("YouTube returned an unsupported playback authorization status.");
                ProtectionStatus = (int)status;
                if (status == 3 || status == 2 && !_allowStartupPlayback) throw new NotSupportedException("YouTube requires a playback authorization challenge that the native session has not completed.");
                break;
            case 59:
                foreach (var field in fields)
                {
                    if (field.Number is not (1 or 2 or 3)) continue;
                    if (field.WireType != 0 || field.Integer > uint.MaxValue) throw new InvalidDataException("The SABR context policy is invalid.");
                    var context = (uint)field.Integer;
                    if (field.Number == 1) _sendContexts.Add(context);
                    else _sendContexts.Remove(context);
                    if (field.Number == 3) _contexts.Remove(context);
                }
                break;
        }
    }

    public void BeginResponse() { Redirect = null; BackoffMilliseconds = 0; RequiresPlayerReload = false; }
    internal void ResetSession(){_contexts.Clear();_sendContexts.Clear();PlaybackCookie=[];Redirect=null;BackoffMilliseconds=0;RequiresPlayerReload=false;ProtectionStatus=null;_seeded=false;}

    internal static SabrFormatId ReadFormat(ReadOnlyMemory<byte> data)
    {
        var fields = ProtobufReader.Read(data, maximumBytes: 65536);
        var itag = SabrSegmentAssembler.RequiredInteger(fields, 1);
        if (itag is 0 or > uint.MaxValue) throw new InvalidDataException("The SABR format identifier is invalid.");
        return new((uint)itag, SabrSegmentAssembler.OptionalInteger(fields, 2), Text(SabrSegmentAssembler.OptionalBytes(fields, 3)));
    }
    private void CheckVideo(ReadOnlyMemory<byte> value)
    {
        if (!value.IsEmpty && Text(value) != _videoId) throw new InvalidDataException("The SABR response belongs to a different video.");
    }
    private static string Text(ReadOnlyMemory<byte> bytes) => new UTF8Encoding(false, true).GetString(bytes.Span);
}
