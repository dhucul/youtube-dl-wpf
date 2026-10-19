using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace YoutubeDl.Engine.Download;

public sealed class DashManifest
{
    public Uri? SourceUrl { get; init; }
    public bool IsDynamic { get; init; }
    internal bool LiveProjection { get; init; }
    public double MinimumUpdatePeriodSeconds { get; init; } = 2;
    public DateTimeOffset? AvailabilityStartTime { get; init; }
    public DateTimeOffset? PublishTime { get; init; }
    internal string PeriodIdentity { get; init; } = "0";
    internal decimal PeriodStartSeconds { get; init; }
    public IReadOnlyList<DashPeriod> Periods { get; init; } = [];
    public IReadOnlyList<DashRepresentation> Representations { get; init; } = [];
    public IReadOnlyList<Uri> Subtitles { get; init; } = [];
    public bool HasSegmentedSubtitles { get; init; }
    public IReadOnlyList<DashCaption> CaptionTracks { get; init; } = [];
}
public sealed record DashPeriod(string Id, decimal StartSeconds, decimal? DurationSeconds, IReadOnlyList<DashRepresentation> Representations)
{
    public IReadOnlyList<DashCaption> Captions { get; init; } = [];
}
public sealed class DashRepresentation
{
    public required string Id { get; init; }
    public required Uri Url { get; init; }
    public string MimeType { get; init; } = "";
    public int? Height { get; init; }
    public int? Width { get; init; }
    internal string? AdaptationIdentity {get;init;}
    internal string? Language {get;init;}
    internal string Roles {get;init;}="";
    internal string Channels {get;init;}="";
    internal string Viewpoints {get;init;}="";
    internal string Accessibility {get;init;}="";
    internal string? SamplingRate {get;init;}
    internal bool DefaultAudio {get;init;}
    public int? Bandwidth { get; init; }
    public string? Codecs { get; init; }
    public double? FramesPerSecond { get; init; }
    public Uri? Initialization { get; init; }
    internal byte[]? InlineInitialization { get; init; }
    public HlsByteRange? InitializationRange { get; init; }
    public IReadOnlyList<DashSegment> Segments { get; init; } = [];
    public DashSegmentBase? SegmentBase { get; init; }
    public string? UnavailableReason { get; init; }
    public bool IsAudio => MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);
    public bool IsVideo => MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
}
public sealed record DashSegment(Uri Url, HlsByteRange? Range, decimal? PresentationStartSeconds=null,double? DurationSeconds=null,long? Number=null,
    decimal TrimStartSeconds=0,decimal TrimEndSeconds=0)
{
    internal decimal? SourcePresentationStartSeconds {get;init;}
    internal double? SourceDurationSeconds {get;init;}
    internal string? SourceSlot {get;init;}
}
public sealed record DashSegmentBase(HlsByteRange IndexRange, uint? Timescale, double? PeriodDurationSeconds);
public sealed record DashCaption(string Language, string Extension, DashRepresentation Representation, double? DurationSeconds, double OffsetSeconds, string? UnavailableReason = null);

/// <summary>Reads bounded static DASH segment addressing and progressive representations.</summary>
public static partial class DashManifestParser
{
    private const int MaximumSegments = 100000;
    public static DashManifest Parse(string xml, Uri manifestUrl, bool liveProjection = false)
    {
        if(xml.Length>8*1024*1024)throw new InvalidDataException("The DASH document exceeds its limit.");
        if(xml.AsSpan().TrimStart().StartsWith("{"))return VimeoJsonManifest.Parse(xml,manifestUrl,liveProjection);
        using var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8*1024*1024});
        var document=XDocument.Load(reader,LoadOptions.None);
        var root = document.Root ?? throw new InvalidDataException("The DASH manifest is empty.");
        if (root.Name.LocalName != "MPD") throw new InvalidDataException("The document is not a DASH manifest.");
        if (Attribute(root,"type")=="dynamic" || liveProjection)return ParseLive(root,manifestUrl);
        if (root.Descendants().Any(element => element.Name.LocalName == "ContentProtection") ||
            root.Elements().Count(element => element.Name.LocalName == "Period") > 1)
            throw new NotSupportedException("Encrypted, live, and multi-period DASH support is not implemented yet.");
        var representations = new List<DashRepresentation>();
        var subtitles = new List<Uri>();
        var segmentedSubtitles = false;
        var captions = new List<DashCaption>();
        foreach (var adaptation in root.Descendants().Where(element => element.Name.LocalName == "AdaptationSet"))
        {
            foreach (var representation in adaptation.Elements().Where(element => element.Name.LocalName == "Representation"))
            {
                var mime = Inherited(representation, "mimeType") ?? (Inherited(representation, "contentType") switch
                    { "audio" => "audio/mp4", "video" => "video/mp4", var other => other ?? "" });
                var id = Attribute(representation, "id") ?? representations.Count.ToString(CultureInfo.InvariantCulture);
                var bandwidth = Integer(Inherited(representation, "bandwidth"));
                var url = ResolveInherited(representation, manifestUrl);
                var mode = AddressingKind(representation);
                var template = mode == "SegmentTemplate" ? Addressing(representation, mode) : null;
                var list = mode == "SegmentList" ? Addressing(representation, mode) : null;
                var indexed = mode == "SegmentBase" ? Addressing(representation, mode) : null;
                DashSegmentBase? segmentBase = null;
                var codecs = Inherited(representation, "codecs");
                if (mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || mime.Contains("vtt", StringComparison.OrdinalIgnoreCase) || mime == "application/ttml+xml" ||
                    Inherited(representation, "contentType") == "text" || codecs is "wvtt" or "stpp" || codecs?.StartsWith("stpp.", StringComparison.Ordinal) == true)
                {
                    if (mode is not null) segmentedSubtitles = true;
                    else subtitles.Add(url);
                    if (captions.Count == 256) throw new InvalidDataException("The DASH manifest contains too many caption tracks.");
                    captions.Add(ReadCaption(representation, root, url, id, mime, codecs, mode, indexed));
                    continue;
                }
                Uri? initialization = null;
                HlsByteRange? initializationRange = null;
                IReadOnlyList<DashSegment> segments = [];
                if (template is not null)
                {
                    var init = Attribute(template, "initialization");
                    if (init is not null) initialization = Resolve(url, Expand(init, id, bandwidth, 0, 0));
                    segments = TemplateSegments(template, representation, root, url, id, bandwidth);
                }
                else if (list is not null)
                {
                    var init = Child(list, "Initialization");
                    if (init is not null)
                    {
                        initialization = Resolve(url, Attribute(init, "sourceURL") ?? "");
                        initializationRange = ReadRange(Attribute(init, "range"));
                    }
                    var urls = list.Elements().Where(element => element.Name.LocalName == "SegmentURL").ToArray();
                    if (urls.Length is 0 or > MaximumSegments) throw new InvalidDataException("The DASH segment list is empty or exceeds the configured limit.");
                    segments = urls.Select(element => new DashSegment(Resolve(url, Attribute(element, "media") ?? ""), ReadRange(Attribute(element, "mediaRange")))).ToArray();
                }
                else if (indexed is not null)
                {
                    if (mime is not ("video/mp4" or "audio/mp4"))
                    {
                        representations.Add(new() { Id = id, Url = url, MimeType = mime, Height = Integer(Inherited(representation, "height")), Bandwidth = bandwidth,
                            Codecs = codecs, FramesPerSecond = ReadFrameRate(Inherited(representation, "frameRate")), UnavailableReason = "Native indexed DASH currently requires an MP4 representation." });
                        continue;
                    }
                    var period = representation.Ancestors().FirstOrDefault(element => element.Name.LocalName == "Period");
                    if (Nonnegative(Attribute(indexed, "presentationTimeOffset") ?? "0") != 0 || Duration(Attribute(period, "start")) is > 0)
                        throw new NotSupportedException("Shifted or clipped indexed DASH periods remain unsupported.");
                    if (Child(indexed, "RepresentationIndex") is not null) throw new NotSupportedException("Separate DASH index resources remain unsupported.");
                    var range = ReadRange(Attribute(indexed, "indexRange")) ?? throw new NotSupportedException("Indexed DASH requires an explicit index range.");
                    if (range.Length > Mp4SegmentIndexParser.MaximumIndexBytes) throw new InvalidDataException("The DASH index exceeds its size limit.");
                    var init = Child(indexed, "Initialization") ?? throw new NotSupportedException("Indexed DASH requires an explicit initialization range.");
                    initialization = Resolve(url, Attribute(init, "sourceURL") ?? "");
                    initializationRange = ReadRange(Attribute(init, "range")) ?? throw new NotSupportedException("Indexed DASH requires an explicit initialization range.");
                    if (initialization != url || initializationRange.Offset != 0 || initializationRange.Length > range.Offset)
                        throw new NotSupportedException("Indexed DASH requires a nonoverlapping initialization prefix in the same resource.");
                    var timescale = Attribute(indexed, "timescale") is { } scale ? Positive(scale) : (long?)null;
                    if (timescale > uint.MaxValue) throw new InvalidDataException("The DASH timescale exceeds the index format.");
                    segmentBase = new(range, (uint?)timescale, Duration(Attribute(period, "duration")) ?? Duration(Attribute(root, "mediaPresentationDuration")));
                }
                else if (!representation.AncestorsAndSelf().Any(element => Child(element, "BaseURL") is not null)) continue;
                if (segments.Count > 0 && initialization is null) throw new NotSupportedException("The DASH representation is missing a supported initialization section.");
                if (mime.Contains("text", StringComparison.OrdinalIgnoreCase) || mime.Contains("vtt", StringComparison.OrdinalIgnoreCase))
                {
                    if (segments.Count == 0 && segmentBase is null) subtitles.Add(url);
                    continue;
                }
                representations.Add(new DashRepresentation
                {
                    Id = id, Url = url, MimeType = mime, Height = Integer(Inherited(representation, "height")), Bandwidth = bandwidth,
                    Codecs = Inherited(representation, "codecs"), FramesPerSecond = ReadFrameRate(Inherited(representation, "frameRate")),
                    Initialization = initialization, InitializationRange = initializationRange, Segments = segments, SegmentBase = segmentBase
                });
            }
        }
        if (representations.Count > 0 && representations.All(item => item.UnavailableReason is not null))
            throw new NotSupportedException(string.Join(" ", representations.Select(item => item.UnavailableReason).Distinct()));
        return new() { SourceUrl=manifestUrl,Representations = representations, Subtitles = subtitles, HasSegmentedSubtitles = segmentedSubtitles, CaptionTracks = captions };
    }

    private static DashCaption ReadCaption(XElement element, XElement root, Uri url, string id, string mime, string? codecs, string? mode, XElement? indexed)
    {
        var language = Inherited(element, "lang") ?? "und";
        var period = element.Ancestors().FirstOrDefault(parent => parent.Name.LocalName == "Period");
        var duration = Duration(Attribute(period, "duration")) ?? Duration(Attribute(root, "mediaPresentationDuration"));
        var baseRepresentation = new DashRepresentation { Id = id, Url = url, MimeType = mime, Codecs = codecs };
        if (mode is null && mime is "text/vtt" or "application/ttml+xml")
            return new(language, mime == "text/vtt" ? "vtt" : "ttml", baseRepresentation, duration, 0);
        if (codecs != "wvtt" && !YoutubeDl.Engine.Media.Mp4TtmlReader.IsSupportedCodec(codecs)) return new(language, "vtt", baseRepresentation, duration, 0, "Native conversion of this DASH caption codec remains pending.");
        if (mode is null) return new(language, "vtt", baseRepresentation, duration, 0);
        try
        {
            if (Duration(Attribute(period, "start")) is > 0)
                throw new NotSupportedException("Nonzero period starts for DASH captions remain unsupported.");
            if (indexed is null)
            {
                var addressing = Addressing(element, mode!);
                if (addressing is null) throw new NotSupportedException("The DASH caption addressing mode is unsupported.");
                Uri segmentInitialization; HlsByteRange? segmentInitializationRange = null; IReadOnlyList<DashSegment> segments;
                if (mode == "SegmentTemplate")
                {
                    var init = Attribute(addressing, "initialization") ?? throw new NotSupportedException("The DASH caption template has no initialization.");
                    segmentInitialization = Resolve(url, Expand(init, id, Integer(Inherited(element, "bandwidth")), 0, 0));
                    segments = TemplateSegments(addressing, element, root, url, id, Integer(Inherited(element, "bandwidth")));
                }
                else
                {
                    var init = Child(addressing, "Initialization") ?? throw new NotSupportedException("The DASH caption list has no initialization.");
                    segmentInitialization = Resolve(url, Attribute(init, "sourceURL") ?? ""); segmentInitializationRange = ReadRange(Attribute(init, "range"));
                    segments = addressing.Elements().Where(child => child.Name.LocalName == "SegmentURL")
                        .Select(child => new DashSegment(Resolve(url, Attribute(child, "media") ?? ""), ReadRange(Attribute(child, "mediaRange")))).ToArray();
                }
                if (segments.Count is 0 or > 10000) throw new InvalidDataException("The DASH caption segment count is empty or excessive.");
                var segmentOffset = Nonnegative(Attribute(addressing, "presentationTimeOffset") ?? "0") / (double)Positive(Attribute(addressing, "timescale") ?? "1");
                return new(language, "vtt", new DashRepresentation { Id = id, Url = url, MimeType = mime, Codecs = codecs, Initialization = segmentInitialization,
                    InitializationRange = segmentInitializationRange, Segments = segments }, duration, segmentOffset);
            }
            if (Child(indexed, "RepresentationIndex") is not null)
                throw new NotSupportedException("Separate indexes and nonzero period starts for DASH captions remain unsupported.");
            var range = ReadRange(Attribute(indexed, "indexRange")) ?? throw new NotSupportedException("The DASH caption requires an explicit index range.");
            if (range.Length > Mp4SegmentIndexParser.MaximumIndexBytes) throw new InvalidDataException("The DASH caption index exceeds its size limit.");
            var initialization = Child(indexed, "Initialization") ?? throw new NotSupportedException("The DASH caption requires initialization.");
            var initializationUrl = Resolve(url, Attribute(initialization, "sourceURL") ?? "");
            var initializationRange = ReadRange(Attribute(initialization, "range")) ?? throw new NotSupportedException("The DASH caption requires an initialization range.");
            if (initializationUrl != url || initializationRange.Offset != 0 || initializationRange.Length > range.Offset)
                throw new NotSupportedException("The DASH caption requires a nonoverlapping initialization prefix in the same resource.");
            var scale = Attribute(indexed, "timescale") is { } value ? Positive(value) : (long?)null;
            if (scale > uint.MaxValue) throw new InvalidDataException("The caption index timescale is excessive.");
            var offset = Nonnegative(Attribute(indexed, "presentationTimeOffset") ?? "0") / (double)(scale ?? 1);
            return new(language, "vtt", new DashRepresentation { Id = id, Url = url, MimeType = mime, Codecs = codecs,
                Initialization = initializationUrl, InitializationRange = initializationRange,
                SegmentBase = new(range, (uint?)scale, null) }, duration, offset);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        { return new(language, "vtt", baseRepresentation, duration, 0, ex.Message); }
    }

    private static double? ReadFrameRate(string? value)
    {
        if (value is null) return null;
        var parts = value.Split('/');
        if (parts.Length is < 1 or > 2 || !double.TryParse(parts[0], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var numerator)) return null;
        var denominator = 1d;
        if (parts.Length == 2 && (!double.TryParse(parts[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out denominator) || denominator <= 0)) return null;
        var result = numerator / denominator;
        return double.IsFinite(result) && result > 0 ? result : null;
    }

    private static IReadOnlyList<DashSegment> TemplateSegments(XElement template, XElement representation, XElement root, Uri baseUrl, string id, int? bandwidth)
    {
        var media = Attribute(template, "media") ?? throw new InvalidDataException("The DASH segment template is missing its media pattern.");
        var timescale = Positive(Attribute(template, "timescale") ?? "1");
        var startNumber = Nonnegative(Attribute(template, "startNumber") ?? "1");
        var offset = Nonnegative(Attribute(template, "presentationTimeOffset") ?? "0");
        var period = representation.Ancestors().FirstOrDefault(element => element.Name.LocalName == "Period");
        var duration = Duration(Attribute(period, "duration")) ?? Duration(Attribute(root, "mediaPresentationDuration"));
        var periodStart = Duration(Attribute(period, "start")) ?? 0;
        if (Attribute(period, "duration") is null && duration is not null) duration -= periodStart;
        var end = duration is > 0 ? checked(offset + (long)Math.Ceiling(duration.Value * timescale)) : (long?)null;
        var result = new List<DashSegment>();
        var timeline = Child(template, "SegmentTimeline");
        long current = 0;
        if (timeline is not null)
        {
            var entries = timeline.Elements().Where(element => element.Name.LocalName == "S").ToArray();
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                if (Attribute(entry, "t") is { } explicitTime) current = Nonnegative(explicitTime);
                var step = Positive(Attribute(entry, "d") ?? throw new InvalidDataException("A DASH timeline entry is missing its duration."));
                var repeatText = Attribute(entry, "r") ?? "0";
                if (!long.TryParse(repeatText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var repeat) || repeat < -1)
                    throw new InvalidDataException("The DASH timeline repetition is invalid.");
                long count;
                if (repeat >= 0) count = checked(repeat + 1);
                else
                {
                    var limit = index + 1 < entries.Length && Attribute(entries[index + 1], "t") is { } next ? Nonnegative(next) : end;
                    if (limit is null || limit <= current) throw new NotSupportedException("The DASH timeline has an unbounded negative repetition.");
                    count = checked((limit.Value - current + step - 1) / step);
                }
                if (count > MaximumSegments - result.Count) throw new InvalidDataException("The DASH timeline exceeds the configured segment limit.");
                for (long iteration = 0; iteration < count; iteration++)
                {
                    if (end is null || current < end)
                        result.Add(new(Resolve(baseUrl, Expand(media, id, bandwidth, checked(startNumber + result.Count), current)), null));
                    current = checked(current + step);
                }
            }
        }
        else
        {
            var step = Positive(Attribute(template, "duration") ?? throw new InvalidDataException("The DASH segment template has no duration or timeline."));
            if (end is null) throw new NotSupportedException("A finite duration is required for static DASH template downloads.");
            var count = checked((end.Value - offset + step - 1) / step);
            if (count > MaximumSegments) throw new InvalidDataException("The DASH template exceeds the configured segment limit.");
            for (long index = 0; index < count; index++) result.Add(new(Resolve(baseUrl, Expand(media, id, bandwidth, checked(startNumber + index), checked(index * step))), null));
        }
        if (result.Count == 0) throw new InvalidDataException("The DASH representation contains no media segments.");
        return result;
    }

    private static string Expand(string pattern, string id, int? bandwidth, long number, long time)
    {
        if (pattern.Length > 4096) throw new InvalidDataException("The DASH URL template is too long.");
        foreach (Match token in Regex.Matches(pattern.Replace("$$", "", StringComparison.Ordinal), @"\$[^$]*\$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            if (!Regex.IsMatch(token.Value, @"\A\$(?:RepresentationID|Bandwidth|Number|Time)(?:%0\d{1,2}d)?\$\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                throw new NotSupportedException("The DASH URL template contains an unsupported substitution.");
        return Regex.Replace(pattern, @"\$\$|\$(?<name>RepresentationID|Bandwidth|Number|Time)(?:%0(?<width>\d{1,2})d)?\$", match =>
        {
            if (match.Value == "$$") return "$";
            var name = match.Groups["name"].Value;
            if (name == "RepresentationID") return Uri.EscapeDataString(id);
            var value = name switch { "Number" => number, "Time" => time, _ => bandwidth ?? throw new InvalidDataException("The DASH template requires an unavailable bandwidth value.") };
            var width = match.Groups["width"].Success ? int.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture) : 0;
            if (width > 12) throw new InvalidDataException("The DASH template padding exceeds the configured limit.");
            return value.ToString(width == 0 ? "0" : "D" + width, CultureInfo.InvariantCulture);
        }, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
    private static XElement? Addressing(XElement element, string kind)
    {
        XElement? result = null;
        foreach (var ancestor in element.AncestorsAndSelf().Reverse())
        {
            if (ancestor.Elements().Any(child => child.Name.LocalName is "SegmentTemplate" or "SegmentList" or "SegmentBase" && child.Name.LocalName != kind)) result = null;
            var current = Child(ancestor, kind);
            if (current is null) continue;
            result ??= new XElement(kind);
            foreach (var attribute in current.Attributes()) result.SetAttributeValue(attribute.Name, attribute.Value);
            foreach (var group in current.Elements().GroupBy(child => child.Name.LocalName))
            {
                result.Elements().Where(existing => existing.Name.LocalName == group.Key).Remove();
                result.Add(group.Select(child => new XElement(child)));
            }
        }
        return result;
    }
    private static string? AddressingKind(XElement element)
    {
        string? nearest = null;
        foreach (var ancestor in element.AncestorsAndSelf())
        {
            var modes = ancestor.Elements().Where(child => child.Name.LocalName is "SegmentTemplate" or "SegmentList" or "SegmentBase").ToArray();
            if (modes.Length > 1) throw new InvalidDataException("The DASH manifest mixes segment addressing modes at one level.");
            if (modes.Length == 1) nearest ??= modes[0].Name.LocalName;
        }
        return nearest;
    }
    private static HlsByteRange? ReadRange(string? text)
    {
        if (text is null) return null;
        var parts = text.Split('-');
        if (parts.Length != 2) throw new InvalidDataException("The DASH byte range is invalid.");
        var from = Nonnegative(parts[0]); var to = Nonnegative(parts[1]);
        if (to < from) throw new InvalidDataException("The DASH byte range is reversed.");
        return new(from, checked(to - from + 1));
    }
    private static double? Duration(string? value)
    {
        if (value is null) return null;
        try { return XmlConvert.ToTimeSpan(value).TotalSeconds; }
        catch (FormatException ex) { throw new InvalidDataException("The DASH presentation duration is invalid.", ex); }
    }
    private static long Positive(string value) => Nonnegative(value) is > 0 and var number ? number : throw new InvalidDataException("The DASH duration, timescale, or starting number must be positive.");
    private static long Nonnegative(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : throw new InvalidDataException("The DASH integer is invalid.");
    private static int? Integer(string? value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : null;
    private static string? Attribute(XElement? element, string name) => element?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value;
    private static string? Inherited(XElement element, string name) => element.AncestorsAndSelf().Select(ancestor => Attribute(ancestor, name)).FirstOrDefault(value => value is not null);
    private static XElement? Child(XElement element, string name) => element.Elements().FirstOrDefault(child => child.Name.LocalName == name);
    private static Uri ResolveInherited(XElement element, Uri original)
    {
        var result = original;
        foreach (var ancestor in element.AncestorsAndSelf().Reverse())
            if (Child(ancestor, "BaseURL") is { } value) result = Resolve(result, value.Value.Trim());
        return result;
    }
    private static Uri Resolve(Uri baseUrl, string value) => Uri.TryCreate(baseUrl, value, out var url) && url.Scheme is "http" or "https" ? url
        : throw new InvalidDataException("The DASH manifest contains an invalid media address.");
}
