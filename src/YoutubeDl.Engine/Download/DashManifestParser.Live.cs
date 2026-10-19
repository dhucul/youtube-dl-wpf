using System.Globalization;
using System.Xml.Linq;

namespace YoutubeDl.Engine.Download;

public static partial class DashManifestParser
{
    internal static DashManifest ParseLive(XElement root, Uri manifestUrl)
    {
        try { return ParseLivePeriods(root, manifestUrl); }
        catch (OverflowException ex) { throw new InvalidDataException("The live DASH timing exceeds its supported range.", ex); }
    }

    private static DashManifest ParseLivePeriods(XElement root, Uri manifestUrl)
    {
        if (root.Descendants().Any(element => element.Name.LocalName is "ContentProtection" or "SegmentBase" ||
            element.Attribute("{http://www.w3.org/1999/xlink}href") is not null))
            throw new NotSupportedException("This live DASH protection, indexed or remote addressing mode requires additional support.");
        var elements = root.Elements().Where(element => element.Name.LocalName == "Period").Take(1001).ToArray();
        if (elements.Length is 0 or > 1000) throw new InvalidDataException("The live DASH period count is empty or excessive.");
        var ast = Date(root, "availabilityStartTime") ?? throw new InvalidDataException("Live DASH needs its availability origin.");
        var publish = Date(root, "publishTime") ?? DateTimeOffset.UtcNow;
        var elapsed = (publish - ast).Ticks / (decimal)TimeSpan.TicksPerSecond;
        if (elapsed < 0) throw new InvalidDataException("The live DASH availability clock is invalid.");
        var depth = LiveDuration(root, "timeShiftBufferDepth") ?? 30;
        var update = LiveDuration(root, "minimumUpdatePeriod") ?? 2;
        if (depth <= 0 || depth > 86400 || update < 0 || update > 300)
            throw new InvalidDataException("The live DASH window or refresh interval exceeds its limits.");
        var starts = new decimal[elements.Length]; var durations = new decimal?[elements.Length];
        for (var index = 0; index < elements.Length; index++)
        {
            starts[index] = LiveDuration(elements[index], "start") ?? (index == 0 ? 0 :
                durations[index - 1] is { } previous ? starts[index - 1] + previous :
                throw new InvalidDataException("The live DASH period has no determinable start."));
            durations[index] = LiveDuration(elements[index], "duration");
            if (starts[index] < 0 || durations[index] is <= 0)
                throw new InvalidDataException("The live DASH period timing is invalid.");
        }
        for (var index = 0; index < elements.Length; index++)
        {
            var end = index + 1 < elements.Length ? starts[index + 1] : LiveDuration(root, "mediaPresentationDuration");
            if (end is { } boundary)
            {
                if (boundary <= starts[index] || durations[index] is { } duration && starts[index] + duration != boundary)
                    throw new NotSupportedException("The live DASH periods overlap or leave a presentation gap.");
                durations[index] ??= boundary - starts[index];
            }
        }
        var periods = new List<DashPeriod>(); var lower = Math.Max(0, elapsed - depth); var totalRepresentations = 0; var totalSegments = 0;
        for (var index = 0; index < elements.Length; index++)
        {
            var element = elements[index]; var start = starts[index]; var duration = durations[index];
            var id = Attribute(element, "id") ?? "@" + start.ToString(CultureInfo.InvariantCulture);
            var upper = elapsed;
            var representations = new List<DashRepresentation>(); var captions = new List<DashCaption>(); var adaptationIndex = 0;
            foreach (var adaptation in element.Elements().Where(child => child.Name.LocalName == "AdaptationSet"))
            {
                var adaptationId = Attribute(adaptation, "id") ?? adaptationIndex.ToString(CultureInfo.InvariantCulture); adaptationIndex++;
                foreach (var representation in adaptation.Elements().Where(child => child.Name.LocalName == "Representation"))
                {
                    if (representations.Count >= 512 || ++totalRepresentations > 2048) throw new InvalidDataException("Live DASH contains too many representations.");
                    var rawId = Attribute(representation, "id") ?? throw new InvalidDataException("Live DASH needs stable representation identities.");
                    var mime = Inherited(representation, "mimeType") ?? (Inherited(representation, "contentType") switch
                        { "audio" => "audio/mp4", "video" => "video/mp4", _ => "" });
                    var codecs = Inherited(representation, "codecs");
                    var isCaption = Inherited(representation,"contentType")=="text" || mime.StartsWith("text/",StringComparison.Ordinal) || mime=="application/ttml+xml" || codecs=="wvtt" || YoutubeDl.Engine.Media.Mp4TtmlReader.IsSupportedCodec(codecs);
                    if (!isCaption && mime is not ("video/mp4" or "audio/mp4")) continue;
                    if (isCaption && codecs!="wvtt" && !YoutubeDl.Engine.Media.Mp4TtmlReader.IsSupportedCodec(codecs))
                    {captions.Add(new(Inherited(representation,"lang")??"und","vtt",new(){Id=adaptationId+":"+rawId,Url=ResolveInherited(representation,manifestUrl),MimeType=mime,Codecs=codecs},null,0,"Live DASH requires supported fragmented wvtt/stpp captions."));continue;}
                    var kind = AddressingKind(representation);
                    if (kind is not ("SegmentTemplate" or "SegmentList"))
                        throw new NotSupportedException("Live DASH requires a segment template or timed segment list.");
                    var addressing = Addressing(representation, kind)!;
                    if (Inherited(representation, "availabilityTimeOffset") is { } inheritedOffset && inheritedOffset != "0" ||
                        Inherited(representation, "availabilityTimeComplete") == "false" ||
                        Attribute(addressing, "availabilityTimeOffset") is { } ato && ato != "0" ||
                        Attribute(addressing, "availabilityTimeComplete") == "false" ||
                        representation.AncestorsAndSelf().SelectMany(parent => parent.Elements().Where(child => child.Name.LocalName == "BaseURL"))
                            .Any(child => Attribute(child, "availabilityTimeOffset") is { } value && value != "0" || Attribute(child, "availabilityTimeComplete") == "false"))
                        throw new NotSupportedException("Low-latency incomplete DASH segments require additional support.");
                    if (Attribute(addressing, "eptDelta") is { } delta && delta != "0")
                        throw new NotSupportedException("Live DASH segments with shifted boundary overlap require additional support.");
                    var bandwidth = Integer(Inherited(representation, "bandwidth")); var baseUrl = ResolveInherited(representation, manifestUrl);
                    Uri initialization; HlsByteRange? initializationRange = null;
                    XElement[]? urls = null; string? media = null;
                    if (kind == "SegmentTemplate")
                    {
                        media = Attribute(addressing, "media") ?? throw new InvalidDataException("Live DASH has no media URL template.");
                        var init = Attribute(addressing, "initialization") ?? throw new NotSupportedException("Live DASH requires explicit initialization.");
                        initialization = Resolve(baseUrl, Expand(init, rawId, bandwidth, 0, 0));
                    }
                    else
                    {
                        var init = Child(addressing, "Initialization") ?? throw new NotSupportedException("Live DASH requires explicit list initialization.");
                        initialization = Resolve(baseUrl, Attribute(init, "sourceURL") ?? ""); initializationRange = ReadRange(Attribute(init, "range"));
                        urls = addressing.Elements().Where(child => child.Name.LocalName == "SegmentURL").Take(MaximumSegments + 1).ToArray();
                        if (urls.Length is 0 or > MaximumSegments || urls.Any(child => Attribute(child, "index") is not null || Attribute(child, "indexRange") is not null))
                            throw new NotSupportedException("The live DASH list is empty, excessive or requires separate indexes.");
                    }
                    var segments = LiveSegments(addressing, start, duration, Math.Max(lower, start), upper, urls?.Length,
                        (ordinal, number, time) => (urls is null ? new DashSegment(Resolve(baseUrl, Expand(media!, rawId, bandwidth, number, time)), null) :
                            new DashSegment(Resolve(baseUrl, Attribute(urls[checked((int)ordinal)], "media") ?? ""), ReadRange(Attribute(urls[checked((int)ordinal)], "mediaRange")))) with
                            {SourceSlot=urls is null&&media!.Contains("$Number",StringComparison.Ordinal)?"number:"+number.ToString(CultureInfo.InvariantCulture):"time:"+time.ToString(CultureInfo.InvariantCulture)+":"+(Attribute(addressing,"timescale")??"1")});
                    totalSegments = checked(totalSegments + segments.Count);
                    if (totalSegments > 200000) throw new InvalidDataException("The live DASH presentation exceeds its aggregate segment limit.");
                    var descriptor = new DashRepresentation { Id = adaptationId + ":" + rawId, Url = baseUrl, MimeType = mime,
                        Width=Integer(Inherited(representation,"width")),AdaptationIdentity=Attribute(adaptation,"id"),Language=Inherited(representation,"lang")?.Trim().ToLowerInvariant(),
                        Roles=TrackDescriptors(representation,"Role"),Channels=TrackDescriptors(representation,"AudioChannelConfiguration"),Viewpoints=TrackDescriptors(representation,"Viewpoint"),Accessibility=TrackDescriptors(representation,"Accessibility"),SamplingRate=Inherited(representation,"audioSamplingRate")?.Trim(),
                        DefaultAudio=adaptation.Elements().Any(child=>child.Name.LocalName=="Role"&&Attribute(child,"schemeIdUri")=="urn:mpeg:dash:role:2011"&&Attribute(child,"value")=="main"),
                        Height = Integer(Inherited(representation, "height")), Bandwidth = bandwidth, Codecs = Inherited(representation, "codecs"),
                        FramesPerSecond = ReadFrameRate(Inherited(representation, "frameRate")), Initialization = initialization,
                        InitializationRange = initializationRange, Segments = segments,
                        UnavailableReason = segments.Count == 0 ? "The live representation exposes no complete available segment." : null };
                    if(isCaption)captions.Add(new(Inherited(representation,"lang")??"und","vtt",descriptor,duration is { } span?(double)span:null,
                        Nonnegative(Attribute(addressing,"presentationTimeOffset")??"0")/(double)Positive(Attribute(addressing,"timescale")??"1")));
                    else representations.Add(descriptor);
                }
            }
            if (representations.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != representations.Count)
                throw new InvalidDataException("Live DASH has ambiguous adaptation/representation identities.");
            if(captions.Count>256||captions.Select(item=>item.Representation.Id).Distinct(StringComparer.Ordinal).Count()!=captions.Count)throw new InvalidDataException("The live DASH caption inventory is excessive or ambiguous.");
            periods.Add(new(id, start, duration, representations){Captions=captions});
        }
        if (periods.Select(period => period.Id).Distinct(StringComparer.Ordinal).Count() != periods.Count)
            throw new InvalidDataException("Live DASH repeats a period identity.");
        var first = periods.FirstOrDefault(period => period.Representations.Any(item => item.Segments.Count > 0)) ?? periods[0];
        return new() { SourceUrl = manifestUrl, IsDynamic = Attribute(root, "type") == "dynamic", LiveProjection = true,
            AvailabilityStartTime = ast, PublishTime = publish, MinimumUpdatePeriodSeconds = (double)Math.Max(0.25m, update),
            PeriodIdentity = first.Id, PeriodStartSeconds = first.StartSeconds, Representations = first.Representations, Periods = periods,CaptionTracks=first.Captions,HasSegmentedSubtitles=periods.Any(p=>p.Captions.Count>0) };
    }

    private static string TrackDescriptors(XElement representation,string name)
    {
        var descriptors=representation.AncestorsAndSelf().TakeWhile(element=>element.Name.LocalName!="Period").SelectMany(element=>element.Elements().Where(child=>child.Name.LocalName==name))
            .Select(element=>(Attribute(element,"schemeIdUri")??"")+"="+(Attribute(element,"value")??"")).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(17).ToArray();
        if(descriptors.Length>16||descriptors.Sum(value=>value.Length)>4096)throw new InvalidDataException("The live DASH track descriptors exceed their limit.");
        return string.Join("\n",descriptors);
    }

    private static IReadOnlyList<DashSegment> LiveSegments(XElement addressing, decimal periodStart, decimal? periodDuration,
        decimal lower, decimal upper, int? listCount, Func<long, long, long, DashSegment> resource)
    {
        var scale = Positive(Attribute(addressing, "timescale") ?? "1"); var offset = Nonnegative(Attribute(addressing, "presentationTimeOffset") ?? "0");
        var number = Nonnegative(Attribute(addressing, "startNumber") ?? "1");
        var available = offset + Math.Max(0, upper - periodStart) * scale;
        var earliest = offset + Math.Max(0, lower - periodStart) * scale;
        var end = periodDuration is { } duration ? offset + duration * scale : (decimal?)null;
        var segments = new List<DashSegment>(); long current = 0, ordinal = 0;
        if (Child(addressing, "SegmentTimeline") is { } timeline)
        {
            var entries = timeline.Elements().Where(element => element.Name.LocalName == "S").Take(MaximumSegments + 1).ToArray();
            if (entries.Length > MaximumSegments) throw new InvalidDataException("The live DASH timeline is excessive.");
            if (entries.Length == 0 && listCount is null or 0) return segments;
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index]; if (Attribute(entry, "t") is { } time) current = Nonnegative(time);
                var step = Positive(Attribute(entry, "d") ?? throw new InvalidDataException("A live DASH segment is missing its duration."));
                if (step / (double)scale > 86400) throw new InvalidDataException("The live DASH segment duration exceeds its limit.");
                if (!long.TryParse(Attribute(entry, "r") ?? "0", NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var repeat) || repeat < -1)
                    throw new InvalidDataException("The live DASH repetition is invalid.");
                var bound = index + 1 < entries.Length && Attribute(entries[index + 1], "t") is { } next ? Nonnegative(next) : end ?? available;
                var count = repeat >= 0 ? checked(repeat + 1) : listCount is { } urls && index + 1 == entries.Length ? urls - ordinal :
                    Math.Max(0, checked((long)decimal.Ceiling((bound - current) / step)));
                if (count < 0 || listCount is { } maximum && ordinal + count > maximum) throw new InvalidDataException("The live DASH list and timeline counts differ.");
                Add(current, step, count); current = checked(current + checked(count * step)); ordinal = checked(ordinal + count);
            }
            if (listCount is { } expected && ordinal != expected) throw new InvalidDataException("The live DASH list and timeline counts differ.");
        }
        else
        {
            var step = Positive(Attribute(addressing, "duration") ?? throw new InvalidDataException("Live DASH has no segment duration or timeline."));
            if (step / (double)scale > 86400) throw new InvalidDataException("The live DASH segment duration exceeds its limit.");
            Add(offset, step, listCount ?? Math.Max(0, checked((long)decimal.Floor((available - offset) / step))));
        }
        return segments;

        void Add(long time, long step, long count)
        {
            var first = Math.Max(0, checked((long)decimal.Floor((earliest - time) / step)));
            var last = Math.Min(count, Math.Max(0, checked((long)decimal.Floor((available - time) / step))));
            if (last - first > MaximumSegments - segments.Count) throw new InvalidDataException("The live DASH window contains too many segments.");
            for (var k = first; k < last; k++)
            {
                var start = checked(time + checked(k * step)); var finish = checked(start + step);
                if (finish <= earliest || upper <= periodStart || end is { } boundaryEnd&&start>=boundaryEnd) continue;
                var visibleStart=Math.Max((decimal)start,offset);var visibleEnd=end is { } boundary?Math.Min(finish,boundary):finish;
                if(visibleEnd<=visibleStart)continue;
                var n = checked(number + checked(ordinal + k));
                segments.Add(resource(checked(ordinal + k), n, start) with
                    { PresentationStartSeconds = periodStart + (visibleStart - offset) / scale, DurationSeconds = (double)((visibleEnd-visibleStart)/scale), Number = n,
                        TrimStartSeconds=(visibleStart-start)/scale,TrimEndSeconds=(finish-visibleEnd)/scale,
                        SourcePresentationStartSeconds=periodStart+(start-offset)/scale,SourceDurationSeconds=(double)((finish-start)/scale) });
            }
        }
    }
    private static decimal? LiveDuration(XElement element, string name) => Duration(Attribute(element, name)) is { } value && double.IsFinite(value) ? (decimal)value : null;
    private static DateTimeOffset? Date(XElement element, string name)
    {
        var value = Attribute(element, name); if (value is null) return null;
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)) throw new InvalidDataException("The live DASH date is invalid.");
        return time;
    }
}
