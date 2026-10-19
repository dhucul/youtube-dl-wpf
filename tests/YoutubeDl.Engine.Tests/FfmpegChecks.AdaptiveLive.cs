using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

internal static partial class FfmpegChecks
{
    private static async Task AdaptiveLiveMedia(string ffmpeg,string mode,bool recover=false,bool dropped=false)
    {
        using var files=new Files();var original=Path.Combine(files.Directory,"original.mpd");
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=12","-f","lavfi","-i","sine=frequency=590:sample_rate=48000","-t","8","-map","0:v:0","-map","1:a:0","-c:v","libx264","-preset","ultrafast","-g","24","-c:a","aac","-f","dash","-seg_duration","2","-use_template","1","-use_timeline","1","-init_seg_name","init-$RepresentationID$.m4s","-media_seg_name","chunk-$RepresentationID$-$Number%05d$.m4s",original]);
        var xml=await File.ReadAllTextAsync(original);var parsed=DashManifestParser.Parse(xml,new("https://live.example/manifest.mpd"));var video=parsed.Representations.Single(item=>item.IsVideo);var audio=parsed.Representations.Single(item=>item.IsAudio);
        var origin=DateTimeOffset.Parse("2026-01-01T00:00:00Z");var snapshots=new[]{Snapshot(4.1,false),Snapshot(6.1,false),Snapshot(8.1,true)};
        var vp=0;var ap=0;var mp=0;var tokens=0;var mediaRequests=0;var namespacePath=mode=="private"?"/static/"+AuthenticationBatchChecks.Id+"/":"/";var host=mode=="private"?"account.example":"live.example";
        var url=new Uri("https://"+host+namespacePath+(mode.StartsWith("dash")?"manifest.mpd":"master.m3u8"));
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));if(mode=="cancel")timeout.CancelAfter(500);
        var replay=false;var engine=new NativeEngine(httpFactory:_=>AuthenticationBatchChecks.Http(async(message,ct)=>
        {
            var path=message.RequestUri!.AbsolutePath;var name=Path.GetFileName(path);
            if(mode=="private"&&path.StartsWith("/api/v1/"))
            {
                if(path.EndsWith("/token")){var token="t"+Interlocked.Increment(ref tokens);return AuthenticationBatchChecks.Json(JsonSerializer.Serialize(new{files=new{token,expires=DateTimeOffset.UtcNow.AddMinutes(2)}}));}
                if(path.EndsWith(AuthenticationBatchChecks.Id))return AuthenticationBatchChecks.Json(JsonSerializer.Serialize(new{uuid=AuthenticationBatchChecks.Id,name="Private live fixture",isLive=true,privacy=new{id=5},files=Array.Empty<object>(),streamingPlaylists=new[]{new{type=1,playlistUrl=url.AbsoluteUri}}}));
                return AuthenticationBatchChecks.Json("{}",HttpStatusCode.NotFound);
            }
            if(mode=="private")
            {
                if(message.Headers.Authorization is not null||message.Headers.Contains("x-peertube-video-password"))throw new InvalidOperationException("Account credentials entered a private media request.");
                var required=mediaRequests>=3?"?videoFileToken=t2":"?videoFileToken=t1";
                if(message.RequestUri.Query!=required)return AuthenticationBatchChecks.Json("fixture-value",HttpStatusCode.Forbidden);
            }
            if(name=="manifest.mpd")return AuthenticationBatchChecks.Json(snapshots[Math.Min(Interlocked.Increment(ref mp)-1,2)]);
            if(name=="master.m3u8")return AuthenticationBatchChecks.Json("#EXTM3U\n#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"a\",NAME=\"Audio\",DEFAULT=YES,URI=\"audio.m3u8\"\n#EXT-X-STREAM-INF:BANDWIDTH=100000,RESOLUTION=128x72,CODECS=\"avc1.42c01e,mp4a.40.2\",AUDIO=\"a\"\nvideo.m3u8\n");
            if(name is "video.m3u8" or "audio.m3u8")
            {var isVideo=name=="video.m3u8";var count=isVideo?Interlocked.Increment(ref vp):Interlocked.Increment(ref ap);return AuthenticationBatchChecks.Json(Hls(isVideo?video:audio,count,isVideo));}
            if(mode=="cancel"&&name.StartsWith("chunk-"+audio.Id+"-"))await Task.Delay(Timeout.Infinite,ct);
            var mediaOrdinal=Interlocked.Increment(ref mediaRequests);if(recover&&!replay&&mediaOrdinal==(dropped?8:4))return AuthenticationBatchChecks.Json("interrupted",HttpStatusCode.ServiceUnavailable);
            if(dropped&&replay&&name.EndsWith("00001.m4s"))throw new InvalidOperationException("A paired retry fetched a dropped segment.");
            var bytes=await File.ReadAllBytesAsync(Path.Combine(files.Directory,name),ct);return new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
        }));
        var destination=Path.Combine(files.Directory,"published.mp4");var archive=Path.Combine(files.Directory,"archive.txt");await File.WriteAllTextAsync(destination,"prior output");await File.WriteAllTextAsync(archive,"prior archive\n");
        var request=new NativeDownloadRequest{Url=mode=="private"?new("https://account.example/videos/watch/"+AuthenticationBatchChecks.Id):url,OutputDirectory=files.Directory,OutputTemplate="published.%(ext)s",FfmpegPath=ffmpeg,
            LiveDurationSeconds=mode=="ended"?null:6,KeepOriginal=true,DownloadArchiveFile=archive,Authentication=mode=="private"?new(){VideoPassword="fixture-value"}:new()};var callbacks=0;
        if(recover)
        {
            request=request with {Retries=0,FragmentRetries=0};
            try{var failed=await engine.DownloadAsync(request,null,null,timeout.Token,_=>callbacks++);if(failed.Succeeded)throw new InvalidOperationException("Interruption did not stop capture.");}catch(IOException){}catch(HttpRequestException){}catch(OperationCanceledException){}
            if(callbacks!=0||await File.ReadAllTextAsync(destination)!="prior output"||await File.ReadAllTextAsync(archive)!="prior archive\n")throw new InvalidOperationException("Interrupted capture published success.");
            replay=true;vp=0;ap=0;mp=0;mediaRequests=0;
        }
        try
        {
            var result=await engine.DownloadAsync(request,null,null,timeout.Token,_=>callbacks++);
            if(mode is "gap" or "cancel")throw new InvalidOperationException("Failed paired recording published success.");
            if(!result.Succeeded||result.OutputPaths.Count<3||callbacks!=result.OutputPaths.Count)throw new InvalidOperationException("Paired live publication lost retained tracks or callbacks.");
            foreach(var path in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",path,"-f","null","-"],true);
            var probe=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-count_frames","-show_entries","stream=codec_type,start_time,nb_read_frames","-of","json",destination]));
            var streams=probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();if(streams.Length!=2)throw new InvalidOperationException("Paired live output lost an audio/video role.");
            var vframes=int.Parse(streams.Single(item=>item.GetProperty("codec_type").GetString()=="video").GetProperty("nb_read_frames").GetString()!);
            if(vframes<(mode=="ended"?90:70))throw new InvalidOperationException("Paired live video is shorter than the shared capture window.");
            if(mode=="offset")
            {var vt=decimal.Parse(streams.Single(item=>item.GetProperty("codec_type").GetString()=="video").GetProperty("start_time").GetString()!,System.Globalization.CultureInfo.InvariantCulture);var at=decimal.Parse(streams.Single(item=>item.GetProperty("codec_type").GetString()=="audio").GetProperty("start_time").GetString()!,System.Globalization.CultureInfo.InvariantCulture);if(Math.Abs(at-vt-0.04m)>0.003m)throw new InvalidOperationException("Declared program-time offset was not preserved: video="+vt+", audio="+at+" frames="+await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-select_streams","a:0","-show_frames","-read_intervals","%+0.1","-show_entries","frame=pts_time,nb_samples","-of","json",destination]));}
            var reads=mediaRequests;var repeat=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);if(!repeat.Skipped||mediaRequests!=reads||callbacks!=result.OutputPaths.Count)throw new InvalidOperationException("Paired live archive repeated playback or callbacks.");
            if(mode=="private"&&tokens!=2)throw new InvalidOperationException("Private live recording did not use one bounded token renewal.");
        }
        catch(Exception ex) when(mode is "gap" or "cancel"&&ex is InvalidDataException or OperationCanceledException or HttpRequestException)
        {if(await File.ReadAllTextAsync(destination)!="prior output"||await File.ReadAllTextAsync(archive)!="prior archive\n"||callbacks!=0)throw new InvalidOperationException("Failed paired recording changed prior output or archive.");}
        if(Directory.EnumerateDirectories(files.Directory,".hls-live-*").Any()||Directory.EnumerateFiles(files.Directory,".live-pair-*").Any())throw new InvalidOperationException("Paired live capture left owned resources behind.");
        string Hls(DashRepresentation representation,int poll,bool isVideo)
        {
            var all=Segments(representation.Id);var count=poll==1?2:poll==2?3:4;var take=all.Take(count).ToArray();var skip=dropped&&replay?1:0;var text="#EXTM3U\n#EXT-X-TARGETDURATION:3\n#EXT-X-MEDIA-SEQUENCE:"+((isVideo?100:700)+skip)+"\n#EXT-X-MAP:URI=\""+Path.GetFileName(representation.Initialization!.AbsolutePath)+"\"\n";
            var time=origin.AddSeconds(!isVideo&&mode=="offset"?0.04:0);
            foreach(var item in take)
            {if(item.Index<skip||mode=="gap"&&!isVideo&&item.Index==2) {time=time.AddSeconds(item.Duration);continue;}text+="#EXT-X-PROGRAM-DATE-TIME:"+time.ToString("O")+"\n#EXTINF:"+item.Duration.ToString("0.########",System.Globalization.CultureInfo.InvariantCulture)+",\n"+Path.GetFileName(item.Url.AbsolutePath)+"\n";time=time.AddSeconds(item.Duration);}
            return text+(poll>=3?"#EXT-X-ENDLIST\n":"");
        }
        string Snapshot(double published,bool ended)
        {var doc=XDocument.Parse(xml);var root=doc.Root!;root.SetAttributeValue("type",ended?"static":"dynamic");root.SetAttributeValue("availabilityStartTime",origin.ToString("O"));root.SetAttributeValue("publishTime",origin.AddSeconds(published).ToString("O"));root.SetAttributeValue("timeShiftBufferDepth","PT30S");root.SetAttributeValue("minimumUpdatePeriod","PT0.5S");if(!ended){root.Attribute("mediaPresentationDuration")?.Remove();foreach(var period in root.Elements().Where(e=>e.Name.LocalName=="Period"))period.Attribute("duration")?.Remove();}return doc.ToString();}
        (Uri Url,double Duration,int Index)[] Segments(string id)
        {var doc=XDocument.Parse(xml);var rep=doc.Descendants().Single(e=>e.Name.LocalName=="Representation"&&(string?)e.Attribute("id")==id);var template=rep.Descendants().Single(e=>e.Name.LocalName=="SegmentTemplate");var scale=long.Parse((string?)template.Attribute("timescale")??"1");var values=new List<double>();foreach(var s in template.Descendants().Where(e=>e.Name.LocalName=="S")){var repeat=int.Parse((string?)s.Attribute("r")??"0");for(var i=0;i<=repeat;i++)values.Add(long.Parse((string)s.Attribute("d")!)/(double)scale);}return parsed.Representations.Single(r=>r.Id==id).Segments.Zip(values,(segment,duration)=>(segment.Url,duration)).Select((item,index)=>(item.Url,item.duration,index)).ToArray();}
    }
}


