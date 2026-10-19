using System.Globalization;

namespace YoutubeDl.Engine.Download;

public sealed class HlsPlaylist
{
    public bool IsMaster { get; init; }
    public IReadOnlyList<HlsVariant> Variants { get; init; } = [];
    public IReadOnlyList<HlsAudioRendition> AudioRenditions { get; init; } = [];
    public IReadOnlyList<HlsSubtitleRendition> SubtitleRenditions { get; init; } = [];
    public IReadOnlyList<Uri> Segments { get; init; } = [];
    public IReadOnlyList<HlsSegment> MediaSegments { get; init; } = [];
    public bool IsComplete { get; init; }
    public Uri? SourceUrl { get; init; }
    public double? TargetDurationSeconds { get; init; }
    public ulong MediaSequence { get; init; }
    public string? PlaylistType { get; init; }
    public IReadOnlyList<string> UnsupportedFeatures { get; init; } = [];
}
public sealed class HlsVariant
{
    public required Uri Url { get; init; }
    public int? Bandwidth { get; init; }
    public int? Height { get; init; }
    public string? AudioGroup { get; init; }
    public string? SubtitleGroup { get; init; }
    public string? ClosedCaptionGroup { get; init; }
    public string? VideoCodec { get; init; }
    public double? FramesPerSecond { get; init; }
}
public sealed record HlsAudioRendition(string Group, string Name, string? Language, bool Default, Uri? Url);
public sealed record HlsSubtitleRendition(string Group, string Name, string? Language, bool Default, bool Forced, Uri? Url, bool InBand = false);
public sealed record HlsByteRange(long Offset, long Length);
public sealed record HlsEncryption(Uri KeyUrl, byte[]? IV);
public sealed record HlsInitialization(Uri Url, HlsByteRange? Range, HlsEncryption? Encryption)
{
    internal byte[]? InlineData { get; init; }
}
public sealed record HlsSegment(Uri Url, ulong Sequence, HlsByteRange? Range, HlsEncryption? Encryption, HlsInitialization? Initialization, double? DurationSeconds = null, long DiscontinuitySequence = 0, DateTimeOffset? ProgramDateTime = null,
    decimal TrimStartSeconds=0,decimal TrimEndSeconds=0)
{
    [System.Text.Json.Serialization.JsonIgnore] internal string? RecoveryIdentity {get;init;}
    [System.Text.Json.Serialization.JsonIgnore] internal string? RecoveryTrackFacts {get;init;}
    [System.Text.Json.Serialization.JsonIgnore] internal string? RecoveryResourceIdentity {get;init;}
    [System.Text.Json.Serialization.JsonIgnore] internal string? RecoveryInitializationIdentity {get;init;}
    [System.Text.Json.Serialization.JsonIgnore] internal string? RecoveryBinding {get;init;}
}

public static class HlsPlaylistParser
{
    public static HlsPlaylist Parse(string text, Uri playlistUrl)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0 || lines[0].TrimStart('\uFEFF') != "#EXTM3U") throw new InvalidDataException("The playlist is not an HLS manifest.");
        var variants = new List<HlsVariant>();
        var renditions = new List<HlsAudioRendition>();
        var subtitles = new List<HlsSubtitleRendition>();
        var segments = new List<HlsSegment>();
        var unsupported = new HashSet<string>(StringComparer.Ordinal);
        HlsEncryption? encryption = null;
        HlsInitialization? initialization = null;
        string? pendingRange = null;
        HlsByteRange? previousRange = null;
        Uri? previousUrl = null;
        ulong sequence = 0;
        long discontinuity = 0;
        var discontinuityDeclared = false;
        var discontinuityMarked = false;
        int? bandwidth = null;
        int? height = null;
        string? audioGroup = null;
        string? subtitleGroup = null;
        string? closedCaptionGroup = null;
        double? pendingDuration = null; DateTimeOffset? pendingProgramTime=null;
        string? videoCodec = null;
        double? frameRate = null;
        var pendingVariant = false;
        var master = false;
        var complete = false;
        double? targetDuration=null;string? playlistType=null;ulong mediaSequence=0;var sequenceDeclared=false;
        foreach (var line in lines)
        {
            if (line == "#EXT-X-ENDLIST") complete = true;
            else if(line.StartsWith("#EXT-X-TARGETDURATION:",StringComparison.Ordinal))
            { if(targetDuration is not null||!double.TryParse(line[22..],NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var target)||!double.IsFinite(target)||target<0||target>86400)throw new InvalidDataException("The HLS target duration is invalid.");targetDuration=target; }
            else if(line.StartsWith("#EXT-X-PLAYLIST-TYPE:",StringComparison.Ordinal))
            { if(playlistType is not null||line[21..] is not ("EVENT" or "VOD"))throw new InvalidDataException("The HLS playlist type is invalid.");playlistType=line[21..]; }
            else if(line.StartsWith("#EXT-X-SKIP:",StringComparison.Ordinal)) unsupported.Add("delta live playlists");
            else if(line.StartsWith("#EXT-X-PROGRAM-DATE-TIME:",StringComparison.Ordinal))
            {var value=line[25..];if(pendingProgramTime is not null || !System.Text.RegularExpressions.Regex.IsMatch(value,@"(?:Z|[+-][0-9]{2}:?[0-9]{2})$",System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)) || !DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.None,out var time))throw new InvalidDataException("The HLS program-time mapping is invalid.");pendingProgramTime=time;}
            else if (line.StartsWith("#EXTINF:", StringComparison.Ordinal))
            {
                if (pendingDuration is not null || !double.TryParse(line[8..].Split(',')[0], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var duration) || !double.IsFinite(duration) || duration <= 0)
                    throw new InvalidDataException("The HLS segment duration is missing, repeated, or invalid.");
                pendingDuration = duration;
            }
            else if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE:", StringComparison.Ordinal))
            {
                if (sequenceDeclared || segments.Count > 0 || !ulong.TryParse(line[22..], NumberStyles.None, CultureInfo.InvariantCulture, out sequence))
                    throw new InvalidDataException("The HLS media sequence is invalid.");
                sequenceDeclared=true;mediaSequence=sequence;
            }
            else if (line.StartsWith("#EXT-X-KEY:", StringComparison.Ordinal))
            {
                var attributes = Attributes(line);
                var method = attributes.GetValueOrDefault("METHOD");
                if (method == "NONE") encryption = null;
                else if (method == "AES-128" && attributes.GetValueOrDefault("KEYFORMAT", "identity") == "identity")
                    encryption = new(Resolve(playlistUrl, Required(attributes, "URI")), ReadIV(attributes.GetValueOrDefault("IV")));
                else { unsupported.Add("sample encryption or key format"); encryption = null; }
            }
            else if (line.StartsWith("#EXT-X-MAP:", StringComparison.Ordinal))
            {
                var attributes = Attributes(line);
                var url = Resolve(playlistUrl, Required(attributes, "URI"));
                var range = attributes.TryGetValue("BYTERANGE", out var mapRange)
                    ? ReadRange(mapRange, url, initialization?.Url, initialization?.Range) : null;
                if (encryption is not null && encryption.IV is null)
                    throw new InvalidDataException("An encrypted HLS initialization section requires an explicit IV.");
                initialization = new(url, range, encryption);
            }
            else if (line.StartsWith("#EXT-X-BYTERANGE:", StringComparison.Ordinal))
            {
                if (pendingRange is not null) throw new InvalidDataException("An HLS byte range is missing its segment.");
                pendingRange = line[17..];
            }
            else if (line.StartsWith("#EXT-X-DISCONTINUITY-SEQUENCE:", StringComparison.Ordinal))
            {
                if (segments.Count > 0 || discontinuityDeclared || discontinuityMarked || !long.TryParse(line[30..], NumberStyles.None, CultureInfo.InvariantCulture, out discontinuity))
                    throw new InvalidDataException("The HLS discontinuity sequence is invalid.");
                discontinuityDeclared = true;
            }
            else if (line == "#EXT-X-DISCONTINUITY")
            {
                if (discontinuity == long.MaxValue) throw new InvalidDataException("The HLS discontinuity sequence overflowed.");
                discontinuityMarked = true;
                discontinuity++;
            }
            else if (line == "#EXT-X-GAP") unsupported.Add("unavailable segments");
            else if (line.StartsWith("#EXT-X-MEDIA:", StringComparison.Ordinal))
            {
                var attributes = Attributes(line);
                if (attributes.GetValueOrDefault("TYPE") == "AUDIO")
                    renditions.Add(new(Required(attributes, "GROUP-ID"), Required(attributes, "NAME"), attributes.GetValueOrDefault("LANGUAGE"),
                        attributes.GetValueOrDefault("DEFAULT") == "YES", attributes.TryGetValue("URI", out var audioUrl) ? Resolve(playlistUrl, audioUrl) : null));
                else if (attributes.GetValueOrDefault("TYPE") is "SUBTITLES" or "CLOSED-CAPTIONS")
                {
                    var inBand = attributes["TYPE"] == "CLOSED-CAPTIONS";
                    if (inBand) Required(attributes, "INSTREAM-ID");
                    subtitles.Add(new(Required(attributes, "GROUP-ID"), Required(attributes, "NAME"), attributes.GetValueOrDefault("LANGUAGE"),
                        attributes.GetValueOrDefault("DEFAULT") == "YES", attributes.GetValueOrDefault("FORCED") == "YES",
                        inBand ? null : Resolve(playlistUrl, Required(attributes, "URI")), inBand));
                    if (subtitles.Count > 256) throw new InvalidDataException("The HLS master contains too many subtitle renditions.");
                }
            }
            else if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal))
            {
                if (segments.Count > 0) throw new InvalidDataException("The HLS playlist mixes variants and media segments.");
                if (pendingVariant) throw new InvalidDataException("An HLS variant is missing its address.");
                master = pendingVariant = true;
                var attributes = Attributes(line);
                bandwidth = Integer(attributes.GetValueOrDefault("BANDWIDTH"));
                audioGroup = attributes.GetValueOrDefault("AUDIO");
                subtitleGroup = attributes.GetValueOrDefault("SUBTITLES");
                closedCaptionGroup = attributes.GetValueOrDefault("CLOSED-CAPTIONS");
                if (closedCaptionGroup == "NONE") closedCaptionGroup = null;
                videoCodec = attributes.GetValueOrDefault("CODECS")?.Split(',', StringSplitOptions.TrimEntries).FirstOrDefault(codec =>
                    codec.StartsWith("avc", StringComparison.OrdinalIgnoreCase) || codec.StartsWith("hvc", StringComparison.OrdinalIgnoreCase) ||
                    codec.StartsWith("hev", StringComparison.OrdinalIgnoreCase) || codec.StartsWith("vp", StringComparison.OrdinalIgnoreCase) || codec.StartsWith("av01", StringComparison.OrdinalIgnoreCase));
                frameRate = double.TryParse(attributes.GetValueOrDefault("FRAME-RATE"), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var fps) && double.IsFinite(fps) && fps > 0 ? fps : null;
                var resolution = attributes.GetValueOrDefault("RESOLUTION")?.Split('x');
                height = resolution?.Length == 2 ? Integer(resolution[1]) : null;
            }
            if (line.StartsWith('#')) continue;
            var mediaUrl = Resolve(playlistUrl, line);
            if (pendingVariant)
            {
                variants.Add(new HlsVariant { Url = mediaUrl, Bandwidth = bandwidth, Height = height, AudioGroup = audioGroup, SubtitleGroup = subtitleGroup, ClosedCaptionGroup = closedCaptionGroup, VideoCodec = videoCodec, FramesPerSecond = frameRate });
                pendingVariant = false;
            }
            else
            {
                if (master) throw new InvalidDataException("The HLS playlist mixes variants and media segments.");
                var range = pendingRange is null ? null : ReadRange(pendingRange, mediaUrl, previousUrl, previousRange);
                if (segments.Count >= 100000) throw new InvalidDataException("The HLS playlist contains too many segments.");
                segments.Add(new(mediaUrl, sequence, range, encryption, initialization, pendingDuration, discontinuity,pendingProgramTime));
                pendingProgramTime=null;
                pendingDuration = null;
                if (sequence == ulong.MaxValue) throw new InvalidDataException("The HLS media sequence overflowed.");
                sequence++;
                previousUrl = mediaUrl;
                previousRange = range;
                pendingRange = null;
            }
        }
        if (pendingVariant || pendingRange is not null || pendingDuration is not null) throw new InvalidDataException("The HLS playlist ends with an incomplete segment descriptor.");
        if (renditions.Count > 0 && !master) unsupported.Add("audio rendition declarations in a media playlist");
        if (subtitles.Count > 0 && !master) unsupported.Add("subtitle rendition declarations in a media playlist");
        var positions=segments.Select((segment,index)=>(segment.Sequence,index)).ToDictionary(item=>item.Sequence,item=>item.index);
        foreach(var group in segments.GroupBy(segment=>segment.DiscontinuitySequence))
        {
            var items=group.ToArray();var first=Array.FindIndex(items,item=>item.ProgramDateTime is not null);if(first<0)continue;
            var time=items[first].ProgramDateTime!.Value;
            for(var index=first-1;index>=0;index--){if(items[index].DurationSeconds is not >0)break;time=time.AddSeconds(-items[index].DurationSeconds!.Value);items[index]=items[index] with {ProgramDateTime=time};}
            DateTimeOffset? previousEnd=null;
            foreach(var item in items)
            {var current=item.ProgramDateTime??previousEnd;if(current is not null){var replacement=item with {ProgramDateTime=current};segments[positions[item.Sequence]]=replacement;previousEnd=item.DurationSeconds is >0?current.Value.AddSeconds(item.DurationSeconds.Value):null;}}
        }
        return new HlsPlaylist { IsMaster = master, Variants = variants, AudioRenditions = renditions, SubtitleRenditions = subtitles, Segments = segments.Select(segment => segment.Url).ToArray(), MediaSegments = segments,
            IsComplete = complete, SourceUrl=playlistUrl,TargetDurationSeconds=targetDuration,MediaSequence=mediaSequence,PlaylistType=playlistType,UnsupportedFeatures = unsupported.ToArray() };
    }

    private static HlsByteRange ReadRange(string value, Uri url, Uri? previousUrl, HlsByteRange? previous)
    {
        var parts = value.Split('@');
        if (parts.Length > 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var length) || length <= 0)
            throw new InvalidDataException("The HLS byte-range length is invalid.");
        long offset;
        if (parts.Length == 2)
        {
            if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out offset)) throw new InvalidDataException("The HLS byte-range offset is invalid.");
        }
        else
        {
            if (previous is null || previousUrl != url) throw new InvalidDataException("An implicit HLS byte range has no preceding range for the same resource.");
            offset = checked(previous.Offset + previous.Length);
        }
        _ = checked(offset + length - 1);
        return new(offset, length);
    }
    private static byte[]? ReadIV(string? value)
    {
        if (value is null) return null;
        if (!value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || value.Length is <= 2 or > 34)
            throw new InvalidDataException("The HLS AES initialization vector is invalid.");
        try { return Convert.FromHexString(value[2..].PadLeft(32, '0')); }
        catch (FormatException ex) { throw new InvalidDataException("The HLS AES initialization vector is invalid.", ex); }
    }
    private static Uri Resolve(Uri baseUrl, string value) => Uri.TryCreate(baseUrl, value, out var url) && url.Scheme is "http" or "https"
        ? url : throw new InvalidDataException("The HLS playlist contains an invalid media address.");
    private static int? Integer(string? value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : null;
    private static string Required(Dictionary<string, string> attributes, string name) => attributes.TryGetValue(name, out var value)
        ? value : throw new InvalidDataException("The HLS tag is missing " + name + ".");
    private static Dictionary<string, string> Attributes(string line)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var start = line.IndexOf(':') + 1;
        var quoted = false;
        for (var index = start; index <= line.Length; index++)
        {
            if (index < line.Length && line[index] == '"') quoted = !quoted;
            if (index < line.Length && (line[index] != ',' || quoted)) continue;
            if (quoted) throw new InvalidDataException("The HLS attribute list contains an unterminated quote.");
            var field = line[start..index].Trim();
            var equals = field.IndexOf('=');
            if (equals <= 0 || !result.TryAdd(field[..equals], field[(equals + 1)..].Trim('"')))
                throw new InvalidDataException("The HLS attribute list is malformed or repeats a field.");
            start = index + 1;
        }
        return result;
    }
}
