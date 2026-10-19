using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Buffers.Binary;
using System.Security.Cryptography;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Media;

internal sealed class HlsLiveCaptionCapture:IDisposable
{
    private sealed record Captured(HlsSegment Descriptor,string Text,string? Header,string Facts);
    private sealed record Saved(ulong Sequence,double Duration,long Discontinuity,DateTimeOffset Time);
    private sealed class TrackState(SubtitleTrack track,Uri url,LiveCaptionResources resources)
    {
        internal readonly SubtitleTrack Track=track;internal readonly Uri Url=url;internal readonly LiveCaptionResources Resources=resources;
        internal readonly Dictionary<ulong,Captured> Captured=new();internal readonly Dictionary<(Uri,HlsByteRange?,Uri?,string),string> Headers=new();
        internal ulong? Next;internal DateTimeOffset? End;
        internal string? Destination;internal string Scope="";internal LiveCaptionCheckpoint? Journal;internal bool Opened;internal bool DelayApplied;
    }
    private readonly MediaHttp _http;private readonly List<SubtitleTrack> _tracks;private readonly List<TrackState> _selected;
    private DateTimeOffset? _start;
    private readonly NativeDownloadRequest _request;
    internal HlsLiveCaptionCapture(MediaHttp http,IReadOnlyList<SubtitleTrack> tracks,IEnumerable<(SubtitleTrack Track,Uri Url)> selected,NativeDownloadRequest request,ByteRateLimiter limiter,string? destination=null)
    {
        _request=request;_http=http;_tracks=tracks.ToList();var budget=new LiveCaptionBudget();_selected=selected.Select(pair=>new TrackState(pair.Track,pair.Url,new(http,request,limiter,budget))
        {Destination=request.NoContinue||request.AdaptiveRecoveryScope is null||destination is null?null:Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!,"captions.hls"),
            Scope=JsonSerializer.Serialize(new{request.AdaptiveRecoveryScope,Track=http.RecoveryIdentity(pair.Url),pair.Track.Language,pair.Track.Automatic,request.LiveDurationSeconds,request.LiveFromStart})}).ToList();
    }
    internal bool RequiresMediaClocks=>_selected.Count>0;
    internal bool HasRecovery=>_selected.Any(track=>track.Destination is not null&&LiveCaptionCheckpoint.Exists(track.Destination,track.Scope));
    internal async Task ObserveAsync(LiveCapturedSegment captured,CancellationToken ct)
    {
        if(_selected.Count==0)return;
        var media=captured.Segment;var start=media.ProgramDateTime??throw new NotSupportedException("Live HLS captions require program-time mappings on selected media and subtitle playlists.");
        _start??=start;var end=LiveWindow.EndOf(media);
        foreach(var track in _selected)
        {
            if (!track.DelayApplied) { await NativeSubtitleDelay.WaitAsync(_request, ct).ConfigureAwait(false); track.DelayApplied = true; }
            await RestoreAsync(track,ct).ConfigureAwait(false);
            var clock=Stopwatch.StartNew();
            while(track.End is null||track.End<end-TimeSpan.FromMilliseconds(50))
            {
                ct.ThrowIfCancellationRequested();var playlist=await new HlsDownloader(_http).ReadAsync(track.Url,ct).ConfigureAwait(false);
                if(playlist.IsMaster||playlist.UnsupportedFeatures.Count>0||playlist.MediaSegments.Count>10000)throw new NotSupportedException("The live HLS caption rendition has an unsupported layout.");
                if(playlist.MediaSegments.Count>0)LiveWindow.ValidateTimeline(playlist);
                track.Resources.RefreshKeys();
                foreach(var segment in playlist.MediaSegments)
                {
                    if(LiveWindow.EndOf(segment)<=_start)continue;
                    if(track.End is null&&segment.ProgramDateTime!.Value>_start.Value+TimeSpan.FromMilliseconds(50))throw new InvalidDataException("The live caption window has already dropped the beginning of this recording.");
                    if(track.Captured.TryGetValue(segment.Sequence,out var previous))
                    {if(previous.Facts!=Facts(segment))throw new InvalidDataException("An already captured live caption segment changed.");continue;}
                    if(track.Next is { } next&&segment.Sequence!=next)throw new InvalidDataException("The live caption window skipped a required segment.");
                    if(track.End is { } last&&Math.Abs((segment.ProgramDateTime!.Value-last).TotalSeconds)>0.05)throw new InvalidDataException("The live caption timeline has a gap.");
                    if(segment.ProgramDateTime!.Value>=end)break;
                    if(track.Captured.Count>=10000)throw new InvalidDataException("Live captions exceed their segment limit.");
                    string? header=null;
                    if(segment.Initialization is { } map)
                    {
                        var key=(map.Url,map.Range,map.Encryption?.KeyUrl,map.Encryption?.IV is { } iv?Convert.ToHexString(iv):"");
                        if(!track.Headers.TryGetValue(key,out header)){header=Utf8(await track.Resources.LoadAsync(map.Url,map.Range,map.Encryption,segment.Sequence,ct).ConfigureAwait(false));track.Headers.Add(key,header);}
                    }
                    var text=Utf8(await track.Resources.LoadAsync(segment.Url,segment.Range,segment.Encryption,segment.Sequence,ct).ConfigureAwait(false));
                    _=HlsWebVttReader.Join([new(text,0,(decimal)segment.DurationSeconds!.Value,header,ExplicitOffsetSeconds:0)],0,(decimal)segment.DurationSeconds.Value,ct);
                    track.Captured.Add(segment.Sequence,new(segment,text,header,Facts(segment)));track.Next=checked(segment.Sequence+1);track.End=LiveWindow.EndOf(segment);
                }
                if(track.End is { } covered&&covered>=end-TimeSpan.FromMilliseconds(50))break;
                if(playlist.IsComplete)throw new InvalidDataException("The ended caption rendition does not cover the recorded media.");
                if(clock.Elapsed>TimeSpan.FromSeconds(30))throw new TimeoutException("The live caption rendition stopped advancing.");
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp((playlist.TargetDurationSeconds??1)/2,0.25,1)),ct).ConfigureAwait(false);
            }
            if(track.Journal is not null)await track.Journal.SaveAsync(track.Captured.OrderBy(pair=>pair.Key).Select(pair=>LiveCaptionCheckpoint.RowHashed(AdaptiveFragmentCheckpoint.Hash("hls:"+pair.Key),pair.Value.Facts,
                JsonSerializer.Serialize(new Saved(pair.Key,pair.Value.Descriptor.DurationSeconds!.Value,pair.Value.Descriptor.DiscontinuitySequence,pair.Value.Descriptor.ProgramDateTime!.Value)),Pack(pair.Value.Text,pair.Value.Header))),ct).ConfigureAwait(false);
        }
    }
    private string Facts(HlsSegment s)=>AdaptiveFragmentCheckpoint.Hash(JsonSerializer.Serialize(new{s.Sequence,s.DurationSeconds,s.DiscontinuitySequence,s.ProgramDateTime,s.Range,
        Resource=_http.RecoveryIdentity(s.Url),Init=s.Initialization is null?null:_http.RecoveryIdentity(s.Initialization.Url),InitRange=s.Initialization?.Range,
        Key=s.Encryption is null?null:_http.RecoveryIdentity(s.Encryption.KeyUrl),IV=s.Encryption?.IV,InitKey=s.Initialization?.Encryption is { } key?_http.RecoveryIdentity(key.KeyUrl):null,InitIV=s.Initialization?.Encryption?.IV}));
    private async Task RestoreAsync(TrackState track,CancellationToken ct)
    {
        if(track.Opened)return;track.Opened=true;if(track.Destination is null)return;
        track.Journal=await LiveCaptionCheckpoint.OpenAsync(_http,track.Destination,track.Scope,ct).ConfigureAwait(false);
        if(track.Journal.Entries.Count==0)return;
        var saved=track.Journal.Entries.Select(row=>(Row:row,Data:JsonSerializer.Deserialize<Saved>(row.Description)??throw new InvalidDataException("The saved HLS caption descriptor is invalid."))).OrderBy(row=>row.Data.Sequence).ToArray();
        if(saved.Length>10000||saved.Any(row=>!double.IsFinite(row.Data.Duration)||row.Data.Duration is <=0 or >86400||row.Data.Discontinuity<0||row.Row.Identity!=AdaptiveFragmentCheckpoint.Hash("hls:"+row.Data.Sequence))||
            saved.Zip(saved.Skip(1)).Any(pair=>pair.Second.Data.Sequence!=pair.First.Data.Sequence+1||pair.Second.Data.Discontinuity<pair.First.Data.Discontinuity||Math.Abs((pair.Second.Data.Time-pair.First.Data.Time.AddSeconds(pair.First.Data.Duration)).TotalSeconds)>0.05))
            throw new InvalidDataException("The saved live caption timeline contains a gap.");
        var playlist=await new HlsDownloader(_http).ReadAsync(track.Url,ct).ConfigureAwait(false);
        if(playlist.IsMaster||playlist.UnsupportedFeatures.Count>0||playlist.MediaSegments.Count==0)throw new InvalidDataException("The fresh live caption rendition is unavailable.");
        LiveWindow.ValidateTimeline(playlist);track.Resources.RefreshKeys();
        var overlap=playlist.MediaSegments.Where(segment=>saved.Any(row=>row.Data.Sequence==segment.Sequence)).ToArray();
        var fresh=overlap.Length>0?overlap:[playlist.MediaSegments[0]];
        if(overlap.Length==0&&(fresh[0].Sequence!=saved[^1].Data.Sequence+1||Math.Abs((fresh[0].ProgramDateTime!.Value-saved[^1].Data.Time.AddSeconds(saved[^1].Data.Duration)).TotalSeconds)>0.05))
            throw new InvalidDataException("The live caption window has no matching overlap or contiguous continuation.");
        foreach(var segment in fresh)
        {
            var header=segment.Initialization is { } init?Utf8(await track.Resources.LoadAsync(init.Url,init.Range,init.Encryption,segment.Sequence,ct).ConfigureAwait(false)):null;
            var text=Utf8(await track.Resources.LoadAsync(segment.Url,segment.Range,segment.Encryption,segment.Sequence,ct).ConfigureAwait(false));
            _=HlsWebVttReader.Join([new(text,0,(decimal)segment.DurationSeconds!.Value,header,ExplicitOffsetSeconds:0)],0,(decimal)segment.DurationSeconds.Value,ct);
            if(saved.SingleOrDefault(row=>row.Data.Sequence==segment.Sequence) is {Row:not null} known&&(known.Row.Facts!=Facts(segment)||!CryptographicOperations.FixedTimeEquals(known.Row.Bytes,Pack(text,header))))
                throw new InvalidDataException("The fresh live captions changed their saved timing or payload.");
        }
        foreach(var item in saved)
        {
            var (text,header)=Unpack(item.Row.Bytes);track.Resources.RestoreBytes(item.Row.Bytes.Length);
            track.Captured.Add(item.Data.Sequence,new(new(new("https://native-live.invalid/caption/"+item.Row.Identity),item.Data.Sequence,null,null,null,item.Data.Duration,item.Data.Discontinuity,item.Data.Time),text,header,item.Row.Facts));
        }
        track.Next=checked(saved[^1].Data.Sequence+1);track.End=saved[^1].Data.Time.AddSeconds(saved[^1].Data.Duration);
    }
    private static byte[] Pack(string text,string? header)
    {
        var body=Encoding.UTF8.GetBytes(text);var head=header is null?[]:Encoding.UTF8.GetBytes(header);var bytes=new byte[checked(4+head.Length+body.Length)];BinaryPrimitives.WriteInt32BigEndian(bytes,header is null?-1:head.Length);head.CopyTo(bytes,4);body.CopyTo(bytes,4+head.Length);return bytes;
    }
    private static (string Text,string? Header) Unpack(byte[] bytes)
    {
        if(bytes.Length<4)throw new InvalidDataException("The saved live caption payload is incomplete.");var length=BinaryPrimitives.ReadInt32BigEndian(bytes);
        if(length< -1||length>bytes.Length-4)throw new InvalidDataException("The saved live caption header is invalid.");
        return(Utf8(bytes[(4+Math.Max(0,length))..]),length<0?null:Utf8(bytes[4..(4+length)]));
    }
    internal IReadOnlyList<SubtitleTrack> Finish(HlsPlaylist media,IReadOnlyList<decimal> clocks,DateTimeOffset? outputStart=null,DateTimeOffset? outputEnd=null,CancellationToken ct=default)
    {
        if(_selected.Count==0)return _tracks;
        var periods=media.MediaSegments.GroupBy(segment=>segment.DiscontinuitySequence).ToArray();
        if(clocks.Count!=periods.Length)throw new InvalidDataException("Live caption media clocks do not match recorded periods.");
        var zero=outputStart??media.MediaSegments[0].ProgramDateTime!.Value;var end=outputEnd??LiveWindow.EndOf(media.MediaSegments[^1]);var duration=(end-zero).Ticks/(decimal)TimeSpan.TicksPerSecond;
        foreach(var track in _selected)
        {
            var documents=new List<HlsWebVttSegment>();
            foreach(var segment in track.Captured.Values.OrderBy(item=>item.Descriptor.Sequence))
            {
                var descriptor=segment.Descriptor;var index=Array.FindIndex(periods,p=>p.Key==descriptor.DiscontinuitySequence);
                if(index<0){if(descriptor.ProgramDateTime>=end)continue;throw new NotSupportedException("Live media and caption discontinuity periods do not match.");}
                var first=periods[index].First();var offset=(first.ProgramDateTime!.Value-zero).Ticks/(decimal)TimeSpan.TicksPerSecond;
                var periodEnd=(LiveWindow.EndOf(periods[index].Last())-zero).Ticks/(decimal)TimeSpan.TicksPerSecond;
                documents.Add(new(segment.Text,Math.Max(0,(descriptor.ProgramDateTime!.Value-zero).Ticks/(decimal)TimeSpan.TicksPerSecond),(decimal)descriptor.DurationSeconds!.Value,segment.Header,clocks[index]+first.TrimStartSeconds-offset,ClipStartSeconds:Math.Max(0,offset),ClipEndSeconds:periodEnd));
            }
            var text=HlsWebVttReader.Join(documents,0,duration,ct);_tracks[_tracks.IndexOf(track.Track)]=new(){Url=track.Track.Url,Language=track.Track.Language,Automatic=track.Track.Automatic,Extension="vtt",Content=text};
        }
        return _tracks;
    }
    private static string Utf8(byte[] data)=>new UTF8Encoding(false,true).GetString(data);
    private static bool Same(HlsSegment a,HlsSegment b)=>a.Url==b.Url&&a.Range==b.Range&&a.Sequence==b.Sequence&&a.DurationSeconds==b.DurationSeconds&&a.ProgramDateTime==b.ProgramDateTime&&a.DiscontinuitySequence==b.DiscontinuitySequence&&a.Initialization?.Url==b.Initialization?.Url&&a.Initialization?.Range==b.Initialization?.Range&&a.Initialization?.Encryption?.KeyUrl==b.Initialization?.Encryption?.KeyUrl&&(a.Initialization?.Encryption?.IV??[]).SequenceEqual(b.Initialization?.Encryption?.IV??[])&&a.Encryption?.KeyUrl==b.Encryption?.KeyUrl&&(a.Encryption?.IV??[]).SequenceEqual(b.Encryption?.IV??[]);
    public void Dispose(){foreach(var track in _selected){track.Resources.Dispose();track.Journal?.Dispose();}}
}
