using System.Diagnostics;
using System.Text.Json;
using System.Globalization;
using System.Security.Cryptography;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Media;

internal sealed class DashLiveCaptionCapture:IDisposable
{
    private sealed record Saved(string Kind,string Period,decimal Start,decimal? PeriodDuration,double Offset,string Initialization,decimal Time=0,double? Duration=null,decimal TrimStart=0,decimal TrimEnd=0,long RangeOffset=0,bool Ranged=false,string? Track=null,string? Group=null,string? Codec=null);
    private sealed class PeriodData(DashPeriod period,DashCaption caption)
    {
        internal DashPeriod Period=period;internal readonly DashCaption Caption=caption;internal byte[]? Initialization;
        internal string InitializationFacts="";
        internal readonly SortedDictionary<decimal,(DashSegment Descriptor,byte[] Bytes,string Facts)> Segments=new();
    }
    private sealed class TrackData(SubtitleTrack track,DashCaption selected,LiveCaptionResources resources,NativeDownloadRequest request)
    {
        internal bool DelayApplied;
        internal readonly SubtitleTrack Track=track;internal readonly DashCaption Selected=selected;internal readonly LiveCaptionResources Resources=resources;
        internal readonly Dictionary<string,PeriodData> Periods=new(StringComparer.Ordinal);
        internal readonly DashTrackSelection Selection=new(selected.Representation,request,true);
        internal readonly bool Convert=request.LiveDashSelection?.ConvertCaptionCodecs==true;
        internal string? Destination;internal string Scope="";internal bool Opened;internal LiveCaptionCheckpoint? Journal;
    }
    private readonly NativeDownloadRequest _request;
    private readonly Func<CancellationToken,Task<DashManifest>> _refresh;private readonly DateTimeOffset _origin;private readonly MediaHttp _http;
    private readonly List<SubtitleTrack> _tracks;private readonly List<TrackData> _selected=new();private decimal? _start;
    internal DashLiveCaptionCapture(DashManifest initial,IReadOnlyList<SubtitleTrack> existing,NativeDownloadRequest request,MediaHttp http,ByteRateLimiter limiter,Func<CancellationToken,Task<DashManifest>> refresh,string? destination=null)
    {
        _request=request;_origin=initial.AvailabilityStartTime!.Value;_refresh=refresh;_http=http;_tracks=existing.ToList();var descriptors=new Dictionary<SubtitleTrack,DashCaption>();
        var offered=initial.CaptionTracks;
        if(request.LiveDashSelection is {CaptionRoles.Count:>0} policy)
        {
            var wanted=SubtitleSelection.Select(offered.Select(caption=>new SubtitleTrack{Url=caption.Representation.Url,Language=caption.Language,Extension=caption.Extension}).ToArray(),request).Select(track=>track.Language).ToHashSet(StringComparer.OrdinalIgnoreCase);
            offered=offered.GroupBy(caption=>caption.Language,StringComparer.OrdinalIgnoreCase).SelectMany(group=>{if(!wanted.Contains(group.Key))return group;var preferred=policy.Prefer(group.Select(caption=>caption.Representation).ToArray(),true);return group.Where(caption=>preferred.Contains(caption.Representation));}).ToArray();
        }
        foreach(var caption in offered){var track=new SubtitleTrack{Url=http.CanonicalizePlaybackUri(caption.Representation.Initialization??caption.Representation.Url),Language=caption.Language,Extension=caption.Extension};_tracks.Add(track);descriptors.Add(track,caption);}
        var selected=SubtitleSelection.Select(_tracks,request);SubtitleFormats.ValidateSelection(request,selected);
        var budget=new LiveCaptionBudget();
        foreach(var track in selected)if(descriptors.TryGetValue(track,out var caption))
        {if(caption.UnavailableReason is { } reason)throw new NotSupportedException(reason);_selected.Add(new(track,caption,new(http,request,limiter,budget),request)
        {Destination=request.NoContinue||request.AdaptiveRecoveryScope is null||destination is null?null:Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!,"captions.dash"),
            Scope=request.LiveDashSelection is null?JsonSerializer.Serialize(new{request.AdaptiveRecoveryScope,Manifest=http.RecoveryIdentity(initial.SourceUrl!),Origin=_origin,Selection=AdaptiveFragmentCheckpoint.Hash(DashTrackSelection.Profile(caption.Representation)),caption.Language,caption.Extension,request.LiveDurationSeconds,request.LiveFromStart,Version=2}):JsonSerializer.Serialize(new{request.AdaptiveRecoveryScope,Manifest=http.RecoveryIdentity(initial.SourceUrl!),Origin=_origin,Selection=new DashTrackSelection(caption.Representation,request,true).Key,caption.Language,caption.Extension,request.LiveDurationSeconds,request.LiveFromStart,Version=3})});}
    }
    internal async Task ObserveAsync(LiveCapturedSegment media,CancellationToken ct)
    {
        if(_selected.Count==0)return;
        var start=(media.Segment.ProgramDateTime!.Value-_origin).Ticks/(decimal)TimeSpan.TicksPerSecond;
        _start??=start;var end=start+(decimal)media.Segment.DurationSeconds!.Value;var clock=Stopwatch.StartNew();
        while(true)
        {
            var manifest=await _refresh(ct).ConfigureAwait(false);var complete=true;
            foreach(var track in _selected)
            {
                if (!track.DelayApplied) { await NativeSubtitleDelay.WaitAsync(_request, ct).ConfigureAwait(false); track.DelayApplied = true; }
                await RestoreAsync(track,manifest,ct).ConfigureAwait(false);
                foreach(var period in manifest.Periods)
                {
                    if(period.StartSeconds>=end||period.DurationSeconds is { } length&&period.StartSeconds+length<=_start)continue;
                    var caption=Resolve(track,period);
                    if(caption.UnavailableReason is { } reason)throw new NotSupportedException(reason);
                    if(caption.Language!=track.Selected.Language||!CompatibleCodec(track,caption.Representation.Codecs))throw new NotSupportedException("The live DASH caption language or unsupported codec changed across periods.");
                    var key=PeriodKey(period);var initializationFacts=InitializationFacts(caption);
                    if(!track.Periods.TryGetValue(key,out var state)){state=new(period,caption){InitializationFacts=initializationFacts};track.Periods.Add(key,state);}
                    if(state.Period.StartSeconds!=period.StartSeconds||state.Period.DurationSeconds is { } knownLength&&period.DurationSeconds!=knownLength)throw new InvalidDataException("A live caption period changed its saved boundary.");
                    state.Period=period;
                    if(state.InitializationFacts!=initializationFacts||state.Caption.Representation.Initialization!=caption.Representation.Initialization||state.Caption.Representation.InitializationRange!=caption.Representation.InitializationRange)
                        throw new InvalidDataException("A live caption changed its initialization or sample origin within a period.");
                    foreach(var advertised in caption.Representation.Segments)
                    {
                        var segment=advertised with {Url=_http.CanonicalizePlaybackUri(advertised.Url)};
                        var time=segment.PresentationStartSeconds!.Value;var segmentEnd=time+(decimal)segment.DurationSeconds!.Value;
                        if(segmentEnd<=_start||time>=end)continue;
                        if(state.Segments.TryGetValue(time,out var known))
                        {
                            if(known.Facts!=SegmentFacts(period,caption,segment)||segment.DurationSeconds>known.Descriptor.DurationSeconds||segment.TrimEndSeconds<known.Descriptor.TrimEndSeconds)
                                throw new InvalidDataException("An already captured DASH caption segment changed.");
                            state.Segments[time]=(segment,known.Bytes,known.Facts);continue;
                        }
                        if(state.Segments.Count>=10000)throw new InvalidDataException("Live DASH captions exceed their segment limit.");
                        if(state.Initialization is null)
                        {
                            state.Initialization=await track.Resources.LoadAsync(caption.Representation.Initialization!,caption.Representation.InitializationRange,null,0,ct).ConfigureAwait(false);
                            Mp4WebVttReader.ValidateInitialization(state.Initialization,Mp4TtmlReader.IsSupportedCodec(caption.Representation.Codecs),ct);
                        }
                        var bytes=await track.Resources.LoadAsync(segment.Url,segment.Range,null,0,ct).ConfigureAwait(false);ValidatePayload(state.Initialization,caption,segment,bytes,ct);
                        state.Segments.Add(time,(segment,bytes,SegmentFacts(period,caption,segment)));
                    }
                }
                decimal coverage=_start.Value;
                foreach(var segment in track.Periods.Values.SelectMany(p=>p.Segments.Values).OrderBy(s=>s.Descriptor.PresentationStartSeconds))
                {
                    var first=segment.Descriptor.PresentationStartSeconds!.Value;var last=first+(decimal)segment.Descriptor.DurationSeconds!.Value;
                    if(first>coverage+0.05m)throw new InvalidDataException("The live DASH caption window skipped a required interval.");coverage=Math.Max(coverage,last);
                }
                if(coverage<end-0.05m)complete=false;
                if(track.Journal is not null)await track.Journal.SaveAsync(Snapshot(track),ct).ConfigureAwait(false);
            }
            if(complete)return;
            if(!manifest.IsDynamic)throw new InvalidDataException("The ended DASH captions do not cover recorded media.");
            if(clock.Elapsed>TimeSpan.FromSeconds(30))throw new TimeoutException("The live DASH captions stopped advancing.");
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(manifest.MinimumUpdatePeriodSeconds,0.25,1)),ct).ConfigureAwait(false);
        }
    }
    private static DashCaption Resolve(TrackData track,DashPeriod period)
    {
        var selected=track.Selection.Resolve(PeriodKey(period),period.Captions.Select(caption=>caption.Representation).ToArray());
        return period.Captions.Single(caption=>ReferenceEquals(caption.Representation,selected));
    }
    private static string PeriodKey(DashPeriod period)=>AdaptiveFragmentCheckpoint.Hash(period.Id);
    private static bool CompatibleCodec(TrackData track,string? codec)=>codec==track.Selected.Representation.Codecs||track.Convert&&DashTrackSelection.CaptionCodec(codec)&&DashTrackSelection.CaptionCodec(track.Selected.Representation.Codecs);
    private string InitializationFacts(DashCaption caption)=>AdaptiveFragmentCheckpoint.Hash(JsonSerializer.Serialize(new{Profile=DashTrackSelection.Profile(caption.Representation),caption.Representation.InitializationRange,caption.OffsetSeconds}));
    private string SegmentFacts(DashPeriod period,DashCaption caption,DashSegment segment)=>AdaptiveFragmentCheckpoint.Hash(JsonSerializer.Serialize(new{Period=PeriodKey(period),period.StartSeconds,segment.PresentationStartSeconds,Total=segment.SourceDurationSeconds??segment.DurationSeconds,segment.TrimStartSeconds,Resource=AdaptiveFragmentCheckpoint.Hash(JsonSerializer.Serialize(new{Period=PeriodKey(period),Profile=DashTrackSelection.Profile(caption.Representation),segment.SourceSlot,Source=segment.SourcePresentationStartSeconds??segment.PresentationStartSeconds})),segment.Range,caption.OffsetSeconds}));
    private static void ValidatePayload(byte[] initialization,DashCaption caption,DashSegment segment,byte[] bytes,CancellationToken ct)
    {
        if(Mp4TtmlReader.IsSupportedCodec(caption.Representation.Codecs))_=Mp4TtmlReader.ReadSegments(initialization,[new(bytes,segment.Range?.Offset??0)],offsetSeconds:caption.OffsetSeconds,ct:ct);
        else _=Mp4WebVttReader.ReadSegments(initialization,[new(bytes,segment.Range?.Offset??0)],offsetSeconds:caption.OffsetSeconds,ct:ct);
    }
    private static IEnumerable<LiveCaptionCheckpoint.Entry> Snapshot(TrackData track)
    {
        foreach(var pair in track.Periods.OrderBy(pair=>pair.Value.Period.StartSeconds))
        {
            var state=pair.Value;if(state.Initialization is null)continue;
            var period=new Saved("period",pair.Key,state.Period.StartSeconds,state.Period.DurationSeconds,state.Caption.OffsetSeconds,state.InitializationFacts,Track:state.Caption.Representation.Id,Group:state.Caption.Representation.AdaptationIdentity,Codec:state.Caption.Representation.Codecs);
            yield return LiveCaptionCheckpoint.RowHashed(AdaptiveFragmentCheckpoint.Hash("period:"+pair.Key),state.InitializationFacts,JsonSerializer.Serialize(period),state.Initialization);
            foreach(var segment in state.Segments)
            {
                var saved=period with {Kind="segment",Time=segment.Key,Duration=segment.Value.Descriptor.DurationSeconds,TrimStart=segment.Value.Descriptor.TrimStartSeconds,TrimEnd=segment.Value.Descriptor.TrimEndSeconds,RangeOffset=segment.Value.Descriptor.Range?.Offset??0,Ranged=segment.Value.Descriptor.Range is not null};
                yield return LiveCaptionCheckpoint.RowHashed(AdaptiveFragmentCheckpoint.Hash("dash:"+pair.Key+":"+segment.Key.ToString(CultureInfo.InvariantCulture)),segment.Value.Facts,JsonSerializer.Serialize(saved),segment.Value.Bytes);
            }
        }
    }
    private async Task RestoreAsync(TrackData track,DashManifest manifest,CancellationToken ct)
    {
        if(track.Opened)return;track.Opened=true;if(track.Destination is null)return;
        track.Journal=await LiveCaptionCheckpoint.OpenAsync(_http,track.Destination,track.Scope,ct).ConfigureAwait(false);if(track.Journal.Entries.Count==0)return;
        var rows=track.Journal.Entries.Select(row=>(Row:row,Data:JsonSerializer.Deserialize<Saved>(row.Description)??throw new InvalidDataException("The saved DASH caption descriptor is invalid."))).ToArray();
        if(rows.Any(row=>row.Data.Kind is not ("period" or "segment")||row.Data.Start<0||row.Data.PeriodDuration is <=0||row.Data.Offset<0||!double.IsFinite(row.Data.Offset)||row.Data.TrimStart<0||row.Data.TrimEnd<0||row.Data.RangeOffset<0))throw new InvalidDataException("The saved DASH caption bounds are invalid.");
        var segments=rows.Where(row=>row.Data.Kind=="segment").OrderBy(row=>row.Data.Time).ToArray();
        if(segments.Length==0||segments.Length>10000||segments.Any(row=>row.Data.Duration is not >0||!double.IsFinite(row.Data.Duration.Value)||row.Data.Time<row.Data.Start))throw new InvalidDataException("The saved DASH caption timeline is empty or invalid.");
        decimal coverage=segments[0].Data.Time;
        foreach(var row in segments){if(row.Data.Time>coverage+0.05m)throw new InvalidDataException("The saved DASH captions have a gap.");coverage=Math.Max(coverage,row.Data.Time+(decimal)row.Data.Duration!.Value);}
        foreach(var row in rows.Where(row=>row.Data.Kind=="period")){if(row.Data.Track is null)throw new InvalidDataException("The saved DASH captions have no accepted track binding.");track.Selection.Pin(row.Data.Period,row.Data.Track,row.Data.Group);}
        var fresh=new List<(DashPeriod Period,DashCaption Caption,DashSegment Segment)>();var overlap=new List<(DashPeriod Period,DashCaption Caption,DashSegment Segment)>();
        foreach(var period in manifest.Periods)
        {
            var caption=Resolve(track,period);
            if(caption.UnavailableReason is { } reason)throw new NotSupportedException(reason);
            if(caption.Language!=track.Selected.Language||caption.Extension!=track.Selected.Extension||!CompatibleCodec(track,caption.Representation.Codecs))throw new InvalidDataException("The fresh DASH caption format changed.");
            foreach(var segment in caption.Representation.Segments)
            {
                fresh.Add((period,caption,segment));
                if(segments.Any(row=>row.Data.Period==PeriodKey(period)&&row.Data.Time==segment.PresentationStartSeconds))overlap.Add((period,caption,segment));
            }
        }
        var validate=overlap.Count>0?overlap:fresh.Where(item=>item.Segment.PresentationStartSeconds>=coverage-0.05m).OrderBy(item=>item.Segment.PresentationStartSeconds).Take(1).ToList();
        if(validate.Count==0||overlap.Count==0&&Math.Abs(validate[0].Segment.PresentationStartSeconds!.Value-coverage)>0.05m)throw new InvalidDataException("The current DASH captions have no overlap or contiguous continuation.");
        var initializations=new Dictionary<string,byte[]>();
        foreach(var item in validate)
        {
            var key=PeriodKey(item.Period);
            if(!initializations.TryGetValue(key,out var initialization))
            {
                initialization=await track.Resources.LoadAsync(item.Caption.Representation.Initialization!,item.Caption.Representation.InitializationRange,null,0,ct).ConfigureAwait(false);
                Mp4WebVttReader.ValidateInitialization(initialization,Mp4TtmlReader.IsSupportedCodec(item.Caption.Representation.Codecs),ct);initializations.Add(key,initialization);
                var periodRow=rows.SingleOrDefault(row=>row.Data.Kind=="period"&&row.Data.Period==key);
                if(periodRow.Row is not null&&(periodRow.Data.Start!=item.Period.StartSeconds||periodRow.Data.PeriodDuration is { } length&&length!=item.Period.DurationSeconds||periodRow.Row.Facts!=InitializationFacts(item.Caption)||!CryptographicOperations.FixedTimeEquals(periodRow.Row.Bytes,initialization)))
                    throw new InvalidDataException("The fresh caption initialization or period boundary changed.");
            }
            var bytes=await track.Resources.LoadAsync(item.Segment.Url,item.Segment.Range,null,0,ct).ConfigureAwait(false);ValidatePayload(initialization,item.Caption,item.Segment,bytes,ct);
            var known=segments.SingleOrDefault(row=>row.Data.Period==key&&row.Data.Time==item.Segment.PresentationStartSeconds);
            if(known.Row is not null&&(known.Row.Facts!=SegmentFacts(item.Period,item.Caption,item.Segment)||!CryptographicOperations.FixedTimeEquals(known.Row.Bytes,bytes)||item.Segment.DurationSeconds>known.Data.Duration||item.Segment.TrimEndSeconds<known.Data.TrimEnd))
                throw new InvalidDataException("The fresh DASH caption segment changed its saved payload or timing.");
        }
        foreach(var row in rows.Where(row=>row.Data.Kind=="period"))
        {
            if(row.Row.Identity!=AdaptiveFragmentCheckpoint.Hash("period:"+row.Data.Period)||row.Row.Facts!=row.Data.Initialization)throw new InvalidDataException("The saved caption period identity is invalid.");
            var visible=manifest.Periods.SingleOrDefault(period=>PeriodKey(period)==row.Data.Period);
            var codec=row.Data.Codec??track.Selected.Representation.Codecs;
            if(!CompatibleCodec(track,codec)||!DashTrackSelection.CaptionCodec(codec))throw new InvalidDataException("The saved DASH caption codec is incompatible with the selected track.");
            var caption=visible is null?track.Selected with {OffsetSeconds=row.Data.Offset,Representation=new(){Id=row.Data.Track!,AdaptationIdentity=row.Data.Group,Url=track.Selected.Representation.Url,Codecs=codec}}:Resolve(track,visible);
            if(caption.Representation.Codecs!=codec)throw new InvalidDataException("The saved DASH caption period changed its codec.");
            var period=new DashPeriod(row.Data.Period,row.Data.Start,row.Data.PeriodDuration,[]);
            Mp4WebVttReader.ValidateInitialization(row.Row.Bytes,Mp4TtmlReader.IsSupportedCodec(caption.Representation.Codecs),ct);track.Resources.RestoreBytes(row.Row.Bytes.Length);
            track.Periods.Add(row.Data.Period,new(period,caption){Initialization=row.Row.Bytes,InitializationFacts=row.Data.Initialization});
        }
        foreach(var row in segments)
        {
            if(!track.Periods.TryGetValue(row.Data.Period,out var state)||row.Data.Start!=state.Period.StartSeconds||row.Data.Offset!=state.Caption.OffsetSeconds||row.Data.Codec is { } codec&&codec!=state.Caption.Representation.Codecs||row.Row.Identity!=AdaptiveFragmentCheckpoint.Hash("dash:"+row.Data.Period+":"+row.Data.Time.ToString(CultureInfo.InvariantCulture)))
                throw new InvalidDataException("The saved DASH caption segment has no matching period.");
            var descriptor=new DashSegment(new("https://native-live.invalid/caption/"+row.Row.Identity),row.Data.Ranged?new(row.Data.RangeOffset,row.Row.Bytes.Length):null,row.Data.Time,row.Data.Duration,TrimStartSeconds:row.Data.TrimStart,TrimEndSeconds:row.Data.TrimEnd);
            ValidatePayload(state.Initialization!,state.Caption,descriptor,row.Row.Bytes,ct);track.Resources.RestoreBytes(row.Row.Bytes.Length);state.Segments.Add(row.Data.Time,(descriptor,row.Row.Bytes,row.Row.Facts));
        }
    }
    internal IReadOnlyList<SubtitleTrack> Finish(HlsPlaylist media,DateTimeOffset? outputStart=null,DateTimeOffset? outputEnd=null,CancellationToken ct=default)
    {
        if(_selected.Count==0)return _tracks;
        var zero=((outputStart??media.MediaSegments[0].ProgramDateTime!.Value)-_origin).Ticks/(decimal)TimeSpan.TicksPerSecond;
        var end=((outputEnd??LiveWindow.EndOf(media.MediaSegments[^1]))-_origin).Ticks/(decimal)TimeSpan.TicksPerSecond;var duration=end-zero;
        foreach(var track in _selected)
        {
            var documents=new List<HlsWebVttSegment>();
            foreach(var state in track.Periods.Values.OrderBy(p=>p.Period.StartSeconds))
            {
                var period=state.Period;var visibleEnd=period.DurationSeconds is { } length?Math.Min(end,period.StartSeconds+length):end;
                if(visibleEnd<=period.StartSeconds||state.Initialization is null)continue;
                var parts=state.Segments.Values.Select(s=>new Mp4WebVttSegment(s.Bytes,s.Descriptor.Range?.Offset??0)).ToArray();
                var text=Mp4TtmlReader.IsSupportedCodec(state.Caption.Representation.Codecs)?Mp4TtmlReader.ReadSegments(state.Initialization,parts,(double)(visibleEnd-period.StartSeconds),state.Caption.OffsetSeconds,ct):
                    Mp4WebVttReader.ReadSegments(state.Initialization,parts,(double)(visibleEnd-period.StartSeconds),state.Caption.OffsetSeconds,ct);
                var offset=period.StartSeconds-zero;documents.Add(new(text,Math.Max(0,offset),visibleEnd-period.StartSeconds,ExplicitOffsetSeconds:offset));
            }
            var converted=HlsWebVttReader.Join(documents,0,duration,ct);_tracks[_tracks.IndexOf(track.Track)]=new(){Url=track.Track.Url,Language=track.Track.Language,Extension="vtt",Content=converted};
        }
        return _tracks;
    }
    public void Dispose(){foreach(var track in _selected){track.Resources.Dispose();track.Journal?.Dispose();}}
}

