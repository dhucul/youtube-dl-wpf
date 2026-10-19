namespace YoutubeDl.Engine.Download;

internal sealed record LiveWindow(DateTimeOffset Start,DateTimeOffset? RequestedEnd)
{
    private readonly object _gate=new();private DateTimeOffset? _observedEnd;
    internal DateTimeOffset? End {get {lock(_gate)return _observedEnd is { } ended&&(RequestedEnd is null||ended<RequestedEnd)?ended:RequestedEnd;}}
    internal void ReportEnd(DateTimeOffset end){lock(_gate)if(_observedEnd is null||end<_observedEnd)_observedEnd=end;}
    internal static LiveWindow Shared(HlsPlaylist video,HlsPlaylist audio,NativeDownloadRequest request)
    {
        ValidateTimeline(video);ValidateTimeline(audio);
        var v=video.MediaSegments;var a=audio.MediaSegments;
        if(request.LiveFromStart&&(!video.IsComplete&&(video.PlaylistType!="EVENT"||video.MediaSequence!=0)||!audio.IsComplete&&(audio.PlaylistType!="EVENT"||audio.MediaSequence!=0)))
            throw new NotSupportedException("Both live tracks must expose the beginning of their event.");
        var vi=request.LiveFromStart?0:Math.Max(0,v.Count-3);var ai=request.LiveFromStart?0:Math.Max(0,a.Count-3);
        var start=v[vi].ProgramDateTime!.Value>a[ai].ProgramDateTime!.Value?v[vi].ProgramDateTime!.Value:a[ai].ProgramDateTime!.Value;
        if(EndOf(v[^1])<=start||EndOf(a[^1])<=start)throw new InvalidDataException("The selected live tracks have no common available window.");
        var result=new LiveWindow(start,request.LiveDurationSeconds is { } duration?start.AddSeconds(duration):null);
        if(video.IsComplete)result.ReportEnd(EndOf(v[^1]));if(audio.IsComplete)result.ReportEnd(EndOf(a[^1]));return result;
    }
    internal static async Task<LiveWindow> RecoverSharedAsync(Http.MediaHttp http,HlsPlaylist video,HlsPlaylist audio,string destination,NativeDownloadRequest request,CancellationToken ct)
    {
        if(request.NoContinue||request.AdaptiveRecoveryScope is null)return Shared(video,audio,request);
        ValidateTimeline(video);ValidateTimeline(audio);
        var scope=AdaptiveFragmentCheckpoint.Hash(System.Text.Json.JsonSerializer.Serialize(new{request.AdaptiveRecoveryScope,Video=http.RecoveryIdentity(video.SourceUrl!),Audio=http.RecoveryIdentity(audio.SourceUrl!),request.LiveDurationSeconds,request.LiveFromStart}));
        var file=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!,".live-window-"+scope[..24]+".json");
        if(File.Exists(file))
        {
            if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0||new FileInfo(file).Length>4096)throw new IOException("The shared live recovery window is not a bounded regular receipt.");
            var saved=System.Text.Json.JsonSerializer.Deserialize<WindowReceipt>(await File.ReadAllTextAsync(file,ct).ConfigureAwait(false));
            if(saved is null||saved.Scope!=scope||saved.End<=saved.Start||saved.End is { } end&&request.LiveDurationSeconds is { } duration&&Math.Abs((end-saved.Start).TotalSeconds-duration)>0.000001)throw new InvalidDataException("The saved shared live window changed.");
            // Each recorder independently requires a fresh overlap and verifies the entire saved prefix.
            return new(saved.Start,saved.End);
        }
        var fresh=Shared(video,audio,request);
        var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{await AdaptiveFragmentCheckpoint.DurableWriteAsync(temporary,System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new WindowReceipt(scope,fresh.Start,fresh.RequestedEnd)),ct).ConfigureAwait(false);File.Move(temporary,file);}finally{try{File.Delete(temporary);}catch(IOException){}}
        return fresh;
    }
    private sealed record WindowReceipt(string Scope,DateTimeOffset Start,DateTimeOffset? End);
    internal static void ValidateTimeline(HlsPlaylist playlist)
    {
        if(playlist.MediaSegments.Count==0||playlist.MediaSegments.Any(segment=>segment.ProgramDateTime is null||segment.DurationSeconds is not >0))
            throw new NotSupportedException("Separate live tracks need explicit, consistent program-time mappings.");
        foreach(var pair in playlist.MediaSegments.Zip(playlist.MediaSegments.Skip(1)))
            if(Math.Abs((pair.Second.ProgramDateTime!.Value-EndOf(pair.First)).TotalSeconds)>0.05)
                throw new InvalidDataException("A live track's program timeline has a gap or inconsistent mapping.");
    }
    internal static DateTimeOffset EndOf(HlsSegment segment)=>segment.ProgramDateTime!.Value.AddSeconds(segment.DurationSeconds!.Value);
    internal static decimal Offset(HlsPlaylist video,HlsPlaylist audio)=>
        (video.MediaSegments[0].ProgramDateTime!.Value-audio.MediaSegments[0].ProgramDateTime!.Value).Ticks/(decimal)TimeSpan.TicksPerSecond;
    internal static double Coverage(HlsPlaylist video,HlsPlaylist audio,LiveWindow window)
    {
        ValidateTimeline(video);ValidateTimeline(audio);
        var videoEnd=EndOf(video.MediaSegments[^1]);var audioEnd=EndOf(audio.MediaSegments[^1]);var end=videoEnd<audioEnd?videoEnd:audioEnd;
        if(end<=window.Start||window.End is { } requested&&end<requested-TimeSpan.FromMilliseconds(50))throw new InvalidDataException("One live track ended before the requested shared recording window.");
        if(!video.MediaSegments.Select(segment=>segment.DiscontinuitySequence).Distinct().SequenceEqual(audio.MediaSegments.Select(segment=>segment.DiscontinuitySequence).Distinct()))
            throw new NotSupportedException("The selected live tracks do not expose matching discontinuity periods.");
        return(end-window.Start).TotalSeconds;
    }
}
