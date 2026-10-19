using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Download;

internal sealed class DashLiveRecorder(MediaHttp http)
{
    internal async Task<(string Path,bool Merged,IReadOnlyList<SubtitleTrack> Subtitles)> DownloadAsync(DashManifest manifest,string destination,NativeDownloadRequest request,ByteRateLimiter limiter,
        IProgress<DownloadProgress>? progress,CancellationToken ct,IReadOnlyList<SubtitleTrack>? existingSubtitles=null)
    {
        request.LiveDashSelection?.Validate();
        var representations=manifest.Representations;
        if(!request.VideoOnly&&request.LiveDashSelection is { } policy&&representations.Any(item=>item.IsAudio))
            representations=representations.Where(item=>!item.IsAudio).Concat(policy.Prefer(representations.Where(item=>item.IsAudio).ToArray())).ToArray();
        var candidates=DashDownloader.ToCandidates(new(){Representations=representations});
        if(candidates.Count==0)throw new InvalidDataException("Live DASH exposes no complete supported media segments.");
        if(!request.VideoOnly&&manifest.Representations.Any(item=>item.IsAudio)&&!candidates.Any(item=>item.IsAudioOnly)||!request.AudioOnly&&manifest.Representations.Any(item=>item.IsVideo)&&!candidates.Any(item=>item.HasVideo==true))
            throw new InvalidDataException("A required live DASH audio/video track has no complete available segment.");
        var plan=MediaDownloadPlan.Create(new PageMetadata{Media=candidates},request with {VideoOnly=request.VideoOnly||!request.AudioOnly&&!candidates.Any(item=>item.IsAudioOnly)});
        var primary=manifest.Representations.Single(item=>item.Id==plan.Primary.Id);var other=plan.Audio is { } audio?manifest.Representations.Single(item=>item.Id==audio.Id):null;
        var periods=new PeriodRegistry();periods.Observe(manifest);
        using var refresh=new SnapshotSession(http,manifest,periods);
        using var captions=new DashLiveCaptionCapture(manifest,existingSubtitles??[],request,http,limiter,refresh.ReadAsync,destination);
        var first=new TrackProjection(http,manifest,primary,periods,request);var firstPlaylist=first.Project(manifest);
        async Task<HlsPlaylist> NextFirst(CancellationToken token)=>first.Project(await refresh.ReadAsync(token).ConfigureAwait(false));
        if(other is null)
        {
            destination=Path.ChangeExtension(destination,primary.IsAudio?"m4a":"mp4");
            var recording=await new HlsLiveRecorder(http){Refresh=NextFirst,Observe=captions.ObserveAsync}.DownloadAsync(firstPlaylist,destination,request,progress,limiter,ct).ConfigureAwait(false);
            return(destination,false,captions.Finish(recording.Playlist,ct:ct));
        }
        var second=new TrackProjection(http,manifest,other,periods,request);var secondPlaylist=second.Project(manifest);
        async Task<HlsPlaylist> NextSecond(CancellationToken token)=>second.Project(await refresh.ReadAsync(token).ConfigureAwait(false));
        var paired=await PairedLiveRecorder.DownloadAsync(http,firstPlaylist,secondPlaylist,destination,request,limiter,progress,ct,NextFirst,NextSecond,captions.ObserveAsync).ConfigureAwait(false);
        var zero=paired.Video.MediaSegments[0].ProgramDateTime!.Value<paired.Audio.MediaSegments[0].ProgramDateTime!.Value?paired.Video.MediaSegments[0].ProgramDateTime!.Value:paired.Audio.MediaSegments[0].ProgramDateTime!.Value;
        return(paired.Path,true,captions.Finish(paired.Video,zero,paired.End,ct));
    }
    internal sealed class PeriodRegistry
    {
        private readonly Dictionary<string,(decimal Start,decimal? Duration,long Sequence)> _known=new(StringComparer.Ordinal);
        private readonly object _gate=new();private long _next;private decimal? _latest;
        internal void Observe(DashManifest manifest)
        {
            lock(_gate)foreach(var period in manifest.Periods)
            {
                if(_known.TryGetValue(period.Id,out var known))
                {
                    if(known.Start!=period.StartSeconds||known.Duration is { } duration&&period.DurationSeconds!=duration)
                        throw new InvalidDataException("The live DASH period changed previously declared timing.");
                    _known[period.Id]=(known.Start,period.DurationSeconds,known.Sequence);
                }
                else
                {
                    if(_known.Count>=1000||_latest is { } latest&&period.StartSeconds<=latest)
                        throw new InvalidDataException("The live DASH period identity moved backwards or exceeds its limit.");
                    _known.Add(period.Id,(period.StartSeconds,period.DurationSeconds,_next++));_latest=period.StartSeconds;
                }
            }
        }
        internal long Sequence(string id){lock(_gate)return _known[id].Sequence;}
        internal decimal? End(long sequence){lock(_gate){var period=_known.Values.Single(item=>item.Sequence==sequence);return period.Duration is { } duration?period.Start+duration:null;}}
    }
    private sealed class SnapshotSession(MediaHttp http,DashManifest initial,PeriodRegistry periods):IDisposable
    {
        private readonly SemaphoreSlim _gate=new(1,1);private readonly DashManifest _initial=initial;private DashManifest _current=initial;private long _last=Environment.TickCount64;
        internal async Task<DashManifest> ReadAsync(CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if(_last!=0&&Environment.TickCount64-_last<_current.MinimumUpdatePeriodSeconds*1000)return _current;
                var next=await new DashDownloader(http).ReadAsync(_initial.SourceUrl!,ct,liveProjection:true).ConfigureAwait(false);
                if(next.SourceUrl!=_initial.SourceUrl||next.AvailabilityStartTime!=_initial.AvailabilityStartTime)
                    throw new NotSupportedException("The live DASH presentation changed its availability origin.");
                if(next.PublishTime<_current.PublishTime)throw new InvalidDataException("The live DASH publish clock moved backwards.");
                periods.Observe(next);
                _current=next;_last=Environment.TickCount64;return next;
            }
            finally{_gate.Release();}
        }
        public void Dispose()=>_gate.Dispose();
    }
    internal sealed class TrackProjection(MediaHttp http,DashManifest initial,DashRepresentation selected,PeriodRegistry periods,NativeDownloadRequest? request=null)
    {
        private readonly Dictionary<(string Period,decimal Time),ulong> _identities=new();
        private readonly Dictionary<string,(Uri Initialization,HlsByteRange? Range)> _initializations=new(StringComparer.Ordinal);
        private readonly DashTrackSelection _selection=new(selected,request);
        private ulong _next;private decimal? _first;
        private IReadOnlyList<HlsSegment> _previousWindow=[];
        internal HlsPlaylist Project(DashManifest manifest)
        {
            var segments=new List<HlsSegment>();
            foreach(var period in manifest.Periods)
            {
                if(!period.Representations.Any(item=>item.Segments.Count>0))continue;
                var representation=_selection.Resolve(period.Id,period.Representations);
                var init=http.CanonicalizePlaybackUri(representation.Initialization!);
                if(_initializations.TryGetValue(period.Id,out var known)&&(known.Initialization!=init||known.Range!=representation.InitializationRange))
                    throw new NotSupportedException("The live DASH track changed initialization within a period.");
                _initializations[period.Id]=(init,representation.InitializationRange);
                var initialization=new HlsInitialization(init,representation.InitializationRange,null);
                foreach(var segment in representation.Segments)
                {
                if(segment.PresentationStartSeconds is not { } time||segment.DurationSeconds is not >0)throw new InvalidDataException("The live DASH segment has no complete presentation timing.");
                if(_first is not null&&time<_first)continue;
                if(!_identities.TryGetValue((period.Id,time),out var sequence))
                {if(_identities.Count>=100000)throw new InvalidDataException("The live DASH recording exceeds its segment limit.");_identities.Add((period.Id,time),sequence=_next++);_first??=time;}
                DateTimeOffset programTime;
                try{programTime=initial.AvailabilityStartTime!.Value.AddSeconds((double)time);}catch(ArgumentOutOfRangeException){throw new InvalidDataException("The live DASH clock exceeds its supported range.");}
                var identity=System.Text.Json.JsonSerializer.Serialize(new{Period=period.Id,period.StartSeconds,Selection=_selection.Key,Time=time});
                var binding=request?.LiveDashSelection is null?System.Text.Json.JsonSerializer.Serialize(new{Period=period.Id,representation.Id,representation.AdaptationIdentity}):System.Text.Json.JsonSerializer.Serialize(new{Period=period.Id,representation.Id,representation.AdaptationIdentity,Profile=DashTrackSelection.Profile(representation)});
                segments.Add(new(http.CanonicalizePlaybackUri(segment.Url),sequence,segment.Range,null,initialization,segment.DurationSeconds,periods.Sequence(period.Id),programTime,segment.TrimStartSeconds,segment.TrimEndSeconds){RecoveryIdentity=identity,RecoveryTrackFacts=request?.LiveDashSelection is null?DashTrackSelection.Profile(selected):_selection.Key,RecoveryResourceIdentity=AdaptiveFragmentCheckpoint.Hash("dash-source-v2:"+identity),RecoveryInitializationIdentity=AdaptiveFragmentCheckpoint.Hash("dash-init-v2:"+period.Id+":"+_selection.Key),RecoveryBinding=binding});
                }
            }
            var currentPeriods=manifest.Periods.Select(p=>periods.Sequence(p.Id)).ToHashSet();
            foreach(var previous in _previousWindow.Where(segment=>!currentPeriods.Contains(segment.DiscontinuitySequence)))
            {
                if(periods.End(previous.DiscontinuitySequence) is not { } periodEnd)continue;
                var boundary=initial.AvailabilityStartTime!.Value.AddSeconds((double)periodEnd);
                if(previous.ProgramDateTime>=boundary||LiveWindow.EndOf(previous)<=boundary)continue;
                var duration=(boundary-previous.ProgramDateTime!.Value).Ticks/(decimal)TimeSpan.TicksPerSecond;
                segments.Add(previous with {DurationSeconds=(double)duration,TrimEndSeconds=previous.TrimEndSeconds+(decimal)previous.DurationSeconds!.Value-duration});
            }
            segments.Sort((a,b)=>a.Sequence.CompareTo(b.Sequence));_previousWindow=segments.ToArray();
            if(segments.Count==0)throw new InvalidDataException("The selected live DASH window contains no available segments.");
            LiveWindow.ValidateTimeline(new(){MediaSegments=segments});
            var begins=segments[0].ProgramDateTime==initial.AvailabilityStartTime;
            return new(){SourceUrl=initial.SourceUrl,IsComplete=!manifest.IsDynamic,MediaSegments=segments,Segments=segments.Select(item=>item.Url).ToArray(),
                MediaSequence=begins?0:segments[0].Sequence+1,PlaylistType=begins?"EVENT":null,TargetDurationSeconds=Math.Max(1,Math.Ceiling(segments.Max(item=>item.DurationSeconds!.Value)))};
        }
    }
}


