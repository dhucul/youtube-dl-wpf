using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Buffers.Binary;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;

namespace YoutubeDl.Engine.Download;

internal sealed record HlsLiveResult(HlsPlaylist Playlist,HlsDownloadTiming Timing);
internal sealed record LiveCapturedSegment(HlsSegment Segment,string Path,string? InitializationPath,bool Recovered=false);

/// <summary>Captures each newly advertised segment before a moving window can discard it.</summary>
internal sealed class HlsLiveRecorder(MediaHttp http)
{
    private sealed record SavedMedia(string Kind,ulong Sequence,double Duration,long Discontinuity,DateTimeOffset? Time,decimal TrimStart,decimal TrimEnd,string? Initialization,string? Binding=null);
    internal TimeSpan? StallTimeout { get; init; }
    internal LiveWindow? Window { get; init; }
    internal Func<CancellationToken,Task<HlsPlaylist>>? Refresh { get; init; }
    internal Func<LiveCapturedSegment,CancellationToken,Task>? Observe { get; init; }
    internal async Task<HlsLiveResult> DownloadAsync(HlsPlaylist initial,string destination,NativeDownloadRequest request,
        IProgress<DownloadProgress>? progress,ByteRateLimiter limiter,CancellationToken ct)
    {
        Validate(initial,false);
        var source=initial.SourceUrl??throw new InvalidDataException("The live playlist has no refresh address.");
        var plain=false;
        string Identity(HlsSegment segment)=>segment.RecoveryIdentity??"hls:"+segment.Sequence;
        string Facts(HlsSegment segment)=>System.Text.Json.JsonSerializer.Serialize(new{segment.DurationSeconds,segment.DiscontinuitySequence,segment.ProgramDateTime,segment.Range,segment.TrimStartSeconds,segment.TrimEndSeconds,
            Resource=segment.RecoveryResourceIdentity??http.RecoveryIdentity(segment.Url),Init=segment.Initialization is null?null:segment.RecoveryInitializationIdentity??http.RecoveryIdentity(segment.Initialization.Url),InitRange=segment.Initialization?.Range,Key=segment.Encryption is null?null:http.RecoveryIdentity(segment.Encryption.KeyUrl),IV=segment.Encryption?.IV})+(plain&&segment.RecoveryTrackFacts is { } track?":track:"+track:"");
        var scope=System.Text.Json.JsonSerializer.Serialize(new{request.AdaptiveRecoveryScope,Track=http.RecoveryIdentity(source),request.LiveDurationSeconds,request.LiveFromStart,Window?.Start,Window?.RequestedEnd});
        if(initial.MediaSegments.Any(segment=>segment.RecoveryResourceIdentity is not null))scope+=":dash-selection-v2";
        using var recovery=!request.NoContinue&&request.AdaptiveRecoveryScope is not null?await TimelineCheckpoint.OpenAsync(http,destination,scope,ct).ConfigureAwait(false):null;
        plain=recovery is not null&&recovery.Records.All(row=>row.Metadata is not null);
        var saved=plain?recovery!.Records.Where(row=>row.Metadata!="{\"Kind\":\"init\"}").Select(row=>(Row:row,Media:JsonSerializer.Deserialize<SavedMedia>(row.Metadata!)??throw new InvalidDataException("The saved live descriptor is invalid."))).ToArray():[];
        long sequenceOffset=0,discontinuityOffset=0;
        HlsPlaylist Normalize(HlsPlaylist playlist)
        {
            if(sequenceOffset==0&&discontinuityOffset==0)return playlist;
            var segments=playlist.MediaSegments.Select(segment=>segment with {Sequence=checked((ulong)(checked((long)segment.Sequence)+sequenceOffset)),DiscontinuitySequence=checked(segment.DiscontinuitySequence+discontinuityOffset)}).ToArray();
            return new(){SourceUrl=playlist.SourceUrl,MediaSegments=segments,Segments=segments.Select(segment=>segment.Url).ToArray(),IsComplete=playlist.IsComplete,TargetDurationSeconds=playlist.TargetDurationSeconds,MediaSequence=playlist.MediaSequence,PlaylistType=playlist.PlaylistType,UnsupportedFeatures=playlist.UnsupportedFeatures,IsMaster=playlist.IsMaster};
        }
        var overlap=saved.Length==0?null:initial.MediaSegments.FirstOrDefault(segment=>saved.Any(row=>row.Row.Identity==AdaptiveFragmentCheckpoint.Hash(Identity(segment))));
        if(saved.Length>0)
        {
            if(overlap is null)throw new InvalidDataException("The live window has no fresh overlap with the saved recording.");
            var anchor=saved.Single(row=>row.Row.Identity==AdaptiveFragmentCheckpoint.Hash(Identity(overlap))).Media;
            if(overlap.RecoveryIdentity is not null){sequenceOffset=checked((long)anchor.Sequence-(long)overlap.Sequence);discontinuityOffset=anchor.Discontinuity-overlap.DiscontinuitySequence;}
            initial=Normalize(initial);overlap=initial.MediaSegments.Single(segment=>AdaptiveFragmentCheckpoint.Hash(Identity(segment))==saved.Single(row=>row.Media==anchor).Row.Identity);
            if(saved.Any(row=>row.Media.Kind!="media"||!double.IsFinite(row.Media.Duration)||row.Media.Duration is <=0 or >86400||row.Media.TrimStart<0||row.Media.TrimEnd<0)||
                saved.Zip(saved.Skip(1)).Any(pair=>pair.Second.Media.Sequence!=pair.First.Media.Sequence+1||pair.Second.Media.Discontinuity<pair.First.Media.Discontinuity||
                    pair.First.Media.Time is { } time&&pair.Second.Media.Time is { } nextTime&&Math.Abs((nextTime-time.AddSeconds(pair.First.Media.Duration)).TotalSeconds)>0.05))
                throw new InvalidDataException("The saved live prefix contains a sequence or timeline gap.");
        }
        else
        {
            Validate(initial,request.LiveFromStart);
            if(request.LiveFromStart&&initial.MediaSegments.Any(segment=>segment.RecoveryResourceIdentity is not null)&&(initial.PlaylistType!="EVENT"||initial.MediaSequence!=0))
                throw new InvalidDataException("The ended DASH window has no saved prefix or exposed presentation beginning.");
            recovery?.RequireVisible(initial.MediaSegments.SelectMany(segment=>new[]{Identity(segment),"init:"+Identity(segment)}));
        }
        FfmpegTool.RequireProbe(request.FfmpegPath,"verify a complete live recording");
        var staging=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!,".hls-live-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var captured=new List<HlsSegment>();var descriptors=new Dictionary<ulong,HlsSegment>();
        var resources=new Dictionary<Uri,string>();var maps=new Dictionary<string,HlsInitialization>(StringComparer.Ordinal);
        var canonicalKeys=new Dictionary<string,HlsEncryption>(StringComparer.Ordinal);
        var initializationReceipts=new Dictionary<Uri,string>();var warmed=new Dictionary<string,Uri>();
        var restoredMaps=new Dictionary<string,HlsInitialization>();
        var downloader=new HlsDownloader(http){NoContinue=true,NoPart=request.NoPart,BufferSize=request.BufferSize,NoResizeBuffer=request.NoResizeBuffer,HttpChunkSize=request.HttpChunkSize};
        var current=initial;double duration=0;long downloaded=0;
        // Normal recording starts near the live edge; EVENT/from-start explicitly preserves the exposed beginning.
        ulong? next=InitialSequence(initial,Window,request.LiveFromStart);
        if(saved.Length>0)next=overlap!.Sequence;
        else if(recovery is {Records.Count:>0})next=initial.MediaSegments.First(segment=>AdaptiveFragmentCheckpoint.Hash(Identity(segment))==recovery.Records[0].Identity||AdaptiveFragmentCheckpoint.Hash("init:"+Identity(segment))==recovery.Records[0].Identity).Sequence;
        var stale=Stopwatch.StartNew();var clock=Stopwatch.StartNew();
        var stall=StallTimeout??TimeSpan.FromSeconds(Math.Clamp((initial.TargetDurationSeconds??10)*6,30,300));
        try
        {
            if(saved.Length>0)
            {
                // Verify an actually available segment before any dropped data is imported.
                var anchor=ClipWindowStart(overlap!,Window);var row=saved.Single(item=>item.Row.Identity==AdaptiveFragmentCheckpoint.Hash(Identity(anchor)));
                if(row.Media.Binding!=anchor.RecoveryBinding)throw new InvalidDataException("The fresh live overlap changed its accepted track binding.");
                if(anchor.Initialization is { } init)
                {
                    if(row.Media.Initialization is null)throw new InvalidDataException("The fresh live initialization changed.");
                    var initRow=recovery!.Records.SingleOrDefault(item=>item.Identity==row.Media.Initialization)??throw new InvalidDataException("The saved live initialization is missing.");
                    var path=Path.Combine(staging,"anchor-init");await downloader.CaptureResourceAsync(init.Url,init.Range,path,limiter,request.FragmentRetries??request.Retries,ct).ConfigureAwait(false);
                    await DecryptAsync(path,init.Encryption,anchor.Sequence).ConfigureAwait(false);
                    await VerifyDigestAsync(path,initRow.Digest).ConfigureAwait(false);File.Delete(path);
                }
                var mediaPath=Path.Combine(staging,"anchor-media");await downloader.CaptureResourceAsync(anchor.Url,anchor.Range,mediaPath,limiter,request.FragmentRetries??request.Retries,ct).ConfigureAwait(false);
                await DecryptAsync(mediaPath,anchor.Encryption,anchor.Sequence).ConfigureAwait(false);
                if(AdaptiveFragmentCheckpoint.Hash(Facts(anchor))!=row.Row.Facts)throw new InvalidDataException("The fresh live overlap changed its identity or timing.");
                await VerifyDigestAsync(mediaPath,row.Row.Digest).ConfigureAwait(false);
                var local=new Uri("https://native-live.invalid/resource/"+resources.Count);resources.Add(local,mediaPath);warmed.Add(Identity(anchor),local);
                foreach(var item in saved.TakeWhile(item=>item.Media.Sequence<anchor.Sequence))
                {
                    HlsInitialization? map=null;
                    if(item.Media.Initialization is { } initIdentity&&!restoredMaps.TryGetValue(initIdentity,out map))
                    {
                        var initRow=recovery!.Records.SingleOrDefault(record=>record.Identity==initIdentity)??throw new InvalidDataException("The saved live initialization is missing.");
                        var initUri=await ImportAsync(initRow).ConfigureAwait(false);restoredMaps.Add(initIdentity,map=new(initUri,null,null));
                    }
                    var uri=await ImportAsync(item.Row).ConfigureAwait(false);var descriptor=item.Media;
                    captured.Add(new HlsSegment(uri,descriptor.Sequence,null,null,map,descriptor.Duration,descriptor.Discontinuity,descriptor.Time,descriptor.TrimStart,descriptor.TrimEnd));
                    if(Observe is not null)await Observe(new(captured[^1],resources[uri],map is null?null:resources[map.Url],true),ct).ConfigureAwait(false);
                    duration+=descriptor.Duration;
                }
            }
            while(true)
            {
                ct.ThrowIfCancellationRequested();Validate(current,false);
                if(current.SourceUrl!=source)throw new InvalidDataException("The live playlist changed its refresh address.");
                var keys=new Dictionary<HlsEncryption,HlsEncryption>(ReferenceEqualityComparer.Instance);
                foreach(var advertised in current.MediaSegments)
                {
                    var segment=ClipWindowStart(advertised,Window);
                    if(descriptors.TryGetValue(segment.Sequence,out var previous))
                    {
                        if(!SameDescriptor(previous,segment))
                        {
                            var revised=previous with {DurationSeconds=segment.DurationSeconds,TrimEndSeconds=segment.TrimEndSeconds};
                            if(captured.Count==0||captured[^1].Sequence!=segment.Sequence||segment.TrimEndSeconds<=previous.TrimEndSeconds||segment.DurationSeconds>=previous.DurationSeconds||
                                Math.Abs((decimal)previous.DurationSeconds!.Value+previous.TrimEndSeconds-(decimal)segment.DurationSeconds!.Value-segment.TrimEndSeconds)>0.000001m||!SameDescriptor(revised,segment))
                                throw new InvalidDataException("The live playlist changed an already captured segment.");
                            if(recovery is not null)
                            {
                                var priorRow=recovery.Records.Single(row=>row.Identity==AdaptiveFragmentCheckpoint.Hash(Identity(previous)));
                                var refinedMetadata=plain?JsonSerializer.Serialize((JsonSerializer.Deserialize<SavedMedia>(priorRow.Metadata!)??throw new InvalidDataException("The saved live boundary is invalid.")) with {Duration=segment.DurationSeconds!.Value,TrimEnd=segment.TrimEndSeconds}):null;
                                await recovery.RefineAsync(Identity(previous),Facts(previous),Facts(segment),refinedMetadata,ct).ConfigureAwait(false);
                            }
                            duration-=previous.DurationSeconds.Value-segment.DurationSeconds!.Value;captured[^1]=captured[^1] with {DurationSeconds=segment.DurationSeconds,TrimEndSeconds=segment.TrimEndSeconds};descriptors[segment.Sequence]=segment;
                        }
                        continue;
                    }
                    if(next is not null&&segment.Sequence<next.Value)continue;
                    if(next is not null&&segment.Sequence!=next.Value)throw new InvalidDataException("The live playlist advanced past a missing segment; the recording is incomplete.");
                    if(Window is { } currentWindow&&segment.ProgramDateTime is { }&&LiveWindow.EndOf(segment)<=currentWindow.Start)continue;
                    if(captured.Count>=100000)throw new InvalidDataException("The live recording exceeds its segment limit.");
                    if(captured.Count>0&&captured[^1].ProgramDateTime is not null&&segment.ProgramDateTime is not null&&Math.Abs((segment.ProgramDateTime.Value-LiveWindow.EndOf(captured[^1])).TotalSeconds)>0.05)throw new InvalidDataException($"The live presentation skipped a required media interval: previous end={LiveWindow.EndOf(captured[^1]):O}, next start={segment.ProgramDateTime:O}.");
                    var encryption=plain?null:await KeyAsync(segment.Encryption).ConfigureAwait(false);HlsInitialization? map=null;
                    if(segment.Initialization is { } initialization)
                    {
                        var mapEncryption=plain?null:await KeyAsync(initialization.Encryption).ConfigureAwait(false);
                        var mapKey=initialization.Url.AbsoluteUri+"|"+initialization.Range+"|"+mapEncryption?.KeyUrl+"|"+(initialization.Encryption?.IV is { } iv?Convert.ToHexString(iv):"");
                        if(!maps.TryGetValue(mapKey,out map))
                        {
                            var savedMap=saved.SingleOrDefault(item=>item.Row.Identity==AdaptiveFragmentCheckpoint.Hash(Identity(segment))).Media?.Initialization;
                            if(savedMap is not null&&restoredMaps.TryGetValue(savedMap,out var restored))
                            {maps[mapKey]=map=restored;initializationReceipts[map.Url]=savedMap;}
                            else
                            {
                                var mapUri=await CaptureAsync(initialization.Url,initialization.Range,"init:"+Identity(segment),Facts(segment)+":init",initialization.Encryption,segment.Sequence,plain?"{\"Kind\":\"init\"}":null).ConfigureAwait(false);
                                maps[mapKey]=map=new(mapUri,null,mapEncryption);initializationReceipts[mapUri]=AdaptiveFragmentCheckpoint.Hash("init:"+Identity(segment));
                            }
                        }
                    }
                    var metadata=plain?JsonSerializer.Serialize(new SavedMedia("media",segment.Sequence,segment.DurationSeconds!.Value,segment.DiscontinuitySequence,segment.ProgramDateTime,segment.TrimStartSeconds,segment.TrimEndSeconds,map is null?null:initializationReceipts[map.Url],segment.RecoveryBinding)):null;
                    var url=warmed.Remove(Identity(segment),out var warm)?warm:await CaptureAsync(segment.Url,segment.Range,Identity(segment),Facts(segment),segment.Encryption,segment.Sequence,metadata).ConfigureAwait(false);
                    captured.Add(segment with {Url=url,Range=null,Encryption=encryption,Initialization=map});descriptors[segment.Sequence]=segment;
                    if(Observe is not null)await Observe(new(plain?segment with {Encryption=null,Initialization=segment.Initialization is null?null:segment.Initialization with {Encryption=null}}:segment,resources[url],map is null?null:resources[map.Url]),ct).ConfigureAwait(false);
                    next=checked(segment.Sequence+1);duration+=segment.DurationSeconds!.Value;stale.Restart();
                    if(Window is {End:{ } end}?LiveWindow.EndOf(segment)>=end:request.LiveDurationSeconds is { } limit&&duration>=limit)break;
                }
                if((Window is {End:{ } endAt}?captured.Count>0&&LiveWindow.EndOf(captured[^1])>=endAt:request.LiveDurationSeconds is { } bound&&duration>=bound)||current.IsComplete)break;
                if(stale.Elapsed>=stall)throw new TimeoutException("The live playlist stopped advancing; no completed recording was published.");
                if(clock.Elapsed>=TimeSpan.FromHours(24))throw new TimeoutException("The live recording reached its maximum session time.");
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp((current.TargetDurationSeconds??2)/2,0.25,10)),ct).ConfigureAwait(false);
                current=Normalize(Refresh is null?await downloader.ReadAsync(source,ct).ConfigureAwait(false):await Refresh(ct).ConfigureAwait(false));

                async Task<HlsEncryption?> KeyAsync(HlsEncryption? key)
                {
                    if(key is null)return null;if(keys.TryGetValue(key,out var cached))return cached;
                    var bytes=await http.GetBytesAsync(key.KeyUrl,16,ct).ConfigureAwait(false);
                    try
                    {
                        if(bytes.Length!=16)throw new InvalidDataException("The live HLS key does not contain exactly128 bits.");
                        var signature=key.KeyUrl.AbsoluteUri+"|"+Convert.ToHexString(SHA256.HashData(bytes))+"|"+(key.IV is { } iv?Convert.ToHexString(iv):"");
                        if(canonicalKeys.TryGetValue(signature,out cached)){keys[key]=cached;return cached;}
                        var url=new Uri("https://native-live.invalid/resource/"+resources.Count);var path=Path.Combine(staging,"resource-"+resources.Count);
                        await File.WriteAllBytesAsync(path,bytes,ct).ConfigureAwait(false);resources.Add(url,path);
                        cached=new(url,key.IV?.ToArray());keys[key]=cached;canonicalKeys[signature]=cached;return cached;
                    }
                    finally { CryptographicOperations.ZeroMemory(bytes); }
                }
            }
            if(captured.Count==0)throw new InvalidDataException("The ended live event contained no captured media.");
            if(current.IsComplete&&Window is not null)Window.ReportEnd(LiveWindow.EndOf(captured[^1]));
            var recording=new HlsPlaylist {SourceUrl=source,IsComplete=true,MediaSegments=captured,Segments=captured.Select(segment=>segment.Url).ToArray(),TargetDurationSeconds=current.TargetDurationSeconds};
            var temporary=Path.Combine(staging,captured[0].Initialization is null?"recording.ts":"recording.mp4");
            using var cache=new MediaHttp(new CacheHandler(resources),new(){AllowAutoRedirect=false});
            var timing=await new HlsDownloader(cache){RetainedMedia=request.RetainedMedia,TransitionPolicy=initial.MediaSegments.Any(segment=>segment.RecoveryResourceIdentity is not null)?request.LiveDashSelection:null}.DownloadAsync(recording,temporary,null,ct,retries:0,ffmpeg:request.FfmpegPath,concurrentFragments:request.ConcurrentFragments).ConfigureAwait(false);
            await FfmpegTool.VerifyLiveRecordingAsync(request.FfmpegPath!,temporary,captured[0].Initialization is null?"mpegts":"mov",duration,ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();File.Move(temporary,destination,overwrite:true);return new(recording,timing);

            async Task<Uri> ImportAsync(TimelineCheckpoint.Record record)
            {
                var local=new Uri("https://native-live.invalid/resource/"+resources.Count);var path=Path.Combine(staging,"resource-"+resources.Count);
                await recovery!.CopySavedAsync(record,path,ct).ConfigureAwait(false);resources.Add(local,path);downloaded=checked(downloaded+new FileInfo(path).Length);return local;
            }
            async Task VerifyDigestAsync(string path,string digest)
            {
                await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous);
                if(Convert.ToHexString(await SHA256.HashDataAsync(stream,ct).ConfigureAwait(false))!=digest)throw new InvalidDataException("The fresh live overlap changed its media.");
            }
            async Task DecryptAsync(string path,HlsEncryption? encryption,ulong sequence)
            {
                if(!plain||encryption is null)return;
                var key=await http.GetBytesAsync(encryption.KeyUrl,16,ct).ConfigureAwait(false);
                var temporary=path+".clear";
                try
                {
                    if(key.Length!=16)throw new InvalidDataException("The live HLS key does not contain exactly128 bits.");
                    var iv=encryption.IV?.ToArray()??new byte[16];if(encryption.IV is null)BinaryPrimitives.WriteUInt64BigEndian(iv.AsSpan(8),sequence);
                    using var aes=Aes.Create();aes.Key=key;aes.IV=iv;aes.Mode=CipherMode.CBC;aes.Padding=PaddingMode.PKCS7;
                    await using(var input=File.OpenRead(path))
                    await using(var decrypted=new CryptoStream(input,aes.CreateDecryptor(),CryptoStreamMode.Read))
                    await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous))
                    {await decrypted.CopyToAsync(output,ct).ConfigureAwait(false);await output.FlushAsync(ct).ConfigureAwait(false);output.Flush(true);}
                    File.Move(temporary,path,true);
                }
                finally{CryptographicOperations.ZeroMemory(key);try{File.Delete(temporary);}catch(IOException){}}
            }
            async Task<Uri> CaptureAsync(Uri url,HlsByteRange? range,string identity,string facts,HlsEncryption? encryption,ulong sequence,string? metadata)
            {
                var local=new Uri("https://native-live.invalid/resource/"+resources.Count);var path=Path.Combine(staging,"resource-"+resources.Count);
                if(recovery is null)await downloader.CaptureResourceAsync(url,range,path,limiter,request.FragmentRetries??request.Retries,ct).ConfigureAwait(false);
                else
                {
                    async Task<IndexedDashDownloader.ResourceVersion?> Fetch(string target,CancellationToken token)
                    {
                        IndexedDashDownloader.ResourceVersion? version;
                        if(range is { } requested){await using var output=new FileStream(target,FileMode.Create,FileAccess.ReadWrite,FileShare.None,65536,FileOptions.Asynchronous);version=await IndexedDashDownloader.CopyRangeAsync(http,url,requested,output,null,limiter,request.FragmentRetries??request.Retries,512L*1024*1024,token,request.BufferSize,request.NoResizeBuffer,request.HttpChunkSize).ConfigureAwait(false);await output.FlushAsync(token).ConfigureAwait(false);output.Flush(true);}
                        else{var transfer=new FileDownloader(http){NoContinue=request.NoContinue,NoPart=request.NoPart,BufferSize=request.BufferSize,NoResizeBuffer=request.NoResizeBuffer,HttpChunkSize=request.HttpChunkSize,MaximumBytes=512L*1024*1024};await transfer.DownloadAsync(url,target,limiter,null,token,request.FragmentRetries??request.Retries).ConfigureAwait(false);version=transfer.CompletedVersion;}
                        await DecryptAsync(target,encryption,sequence).ConfigureAwait(false);return plain&&encryption is not null?null:version;
                    }
                    await recovery.CaptureAsync(identity,facts,url,range,null,path,Fetch,ct,metadata).ConfigureAwait(false);
                }
                resources.Add(local,path);downloaded=checked(downloaded+new FileInfo(path).Length);progress?.Report(new(){DownloadedBytes=downloaded});return local;
            }
        }
        finally
        {
            foreach(var path in resources.Values){try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}}
            try{Directory.Delete(staging,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    private static void Validate(HlsPlaylist playlist,bool fromStart)
    {
        if(playlist.IsMaster||playlist.UnsupportedFeatures.Count>0||playlist.MediaSegments.Any(segment=>segment.DurationSeconds is not >0))throw new NotSupportedException("The live HLS playlist has an unsupported or incomplete media descriptor.");
        if(!playlist.IsComplete&&playlist.TargetDurationSeconds is not >0)throw new InvalidDataException("Live HLS requires a finite target duration.");
        if(playlist.MediaSegments.Any(segment=>segment.DurationSeconds>86400 || !playlist.IsComplete&&segment.DurationSeconds>playlist.TargetDurationSeconds+0.5))
            throw new InvalidDataException("The live HLS segment timing exceeds its declared target or recording limits.");
        if(fromStart&&!playlist.IsComplete&&(playlist.PlaylistType!="EVENT"||playlist.MediaSequence!=0))throw new NotSupportedException("This live window does not expose the beginning of its event.");
        if(playlist.MediaSegments.Zip(playlist.MediaSegments.Skip(1)).Any(pair=>pair.Second.Sequence!=pair.First.Sequence+1||pair.Second.DiscontinuitySequence<pair.First.DiscontinuitySequence))throw new InvalidDataException("The live playlist has inconsistent sequence or timeline ordering.");
    }
    internal static ulong? InitialSequence(HlsPlaylist initial,LiveWindow? window,bool fromStart)=>window is not null?
        initial.MediaSegments.FirstOrDefault(segment=>segment.ProgramDateTime is not null&&LiveWindow.EndOf(segment)>window.Start+TimeSpan.FromMicroseconds(1))?.Sequence:
        !initial.IsComplete&&!fromStart&&initial.MediaSegments.Count>0?initial.MediaSegments[Math.Max(0,initial.MediaSegments.Count-3)].Sequence:null;
    internal static HlsSegment ClipWindowStart(HlsSegment segment,LiveWindow? window)
    {
        if(window is null||segment.Initialization is null||segment.ProgramDateTime is not { } start||start>=window.Start)return segment;
        var leading=(window.Start-start).Ticks/(decimal)TimeSpan.TicksPerSecond;
        if(leading<=0.05m||LiveWindow.EndOf(segment)<=window.Start)return segment;
        return segment with {ProgramDateTime=window.Start,DurationSeconds=(double)((decimal)segment.DurationSeconds!.Value-leading),TrimStartSeconds=segment.TrimStartSeconds+leading};
    }
    private static bool SameDescriptor(HlsSegment a,HlsSegment b)=>(a.ProgramDateTime is null&&b.ProgramDateTime is null||a.ProgramDateTime is not null&&b.ProgramDateTime is not null&&Math.Abs((a.ProgramDateTime.Value-b.ProgramDateTime.Value).TotalSeconds)<=0.05)&&a.Url==b.Url&&a.Range==b.Range&&a.DurationSeconds==b.DurationSeconds&&a.DiscontinuitySequence==b.DiscontinuitySequence&&
        a.TrimStartSeconds==b.TrimStartSeconds&&a.TrimEndSeconds==b.TrimEndSeconds&&a.RecoveryBinding==b.RecoveryBinding&&SameKey(a.Encryption,b.Encryption)&&a.Initialization?.Url==b.Initialization?.Url&&a.Initialization?.Range==b.Initialization?.Range&&SameKey(a.Initialization?.Encryption,b.Initialization?.Encryption);
    private static bool SameKey(HlsEncryption? a,HlsEncryption? b)=>a is null&&b is null||a is not null&&b is not null&&a.KeyUrl==b.KeyUrl&&(a.IV??[]).SequenceEqual(b.IV??[]);
    private sealed class CacheHandler(IReadOnlyDictionary<Uri,string> resources):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();if(request.Method!=HttpMethod.Get||request.RequestUri is null||!resources.TryGetValue(request.RequestUri,out var path))throw new InvalidDataException("The captured HLS replay requested an unrecorded resource.");
            var response=new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new StreamContent(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous|FileOptions.SequentialScan))};
            response.Content.Headers.ContentLength=new FileInfo(path).Length;return Task.FromResult(response);
        }
    }
}
