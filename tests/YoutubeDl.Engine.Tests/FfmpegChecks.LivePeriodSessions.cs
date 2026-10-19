using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;

internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> LivePeriodSessionMediaChecks(string ffmpeg)=>
        new[]{"hevc","hevc-10","vp9","vp9-10","av1","av1-10","flac-24","opus","geometry","pixel","codec","channels","bits-12","unsupported","cancel"}
            .Select(mode=>("Live period/session media clipped "+mode,(Func<Task>)(()=>ClippedCodec(ffmpeg,mode))))
        .Concat(new[]{"hevc-opus","vp9-flac"}.Select(mode=>("Live period/session media native "+mode,(Func<Task>)(()=>NativeRenewingClip(ffmpeg,mode)))))
        .Concat(new[]{"closing","ending","collision","issuer-failed","repeated"}.Select(mode=>("Live period/session media renewal "+mode,(Func<Task>)(()=>RenewingClip(ffmpeg,mode)))));
    private static string[] FrameHashes(string text)=>text.Split('\n').Where(line=>line.Trim().Length>0&&!line.StartsWith('#')).Select(line=>line.Split(',')[^1].Trim()).ToArray();
    private static async Task ClippedCodec(string ffmpeg,string mode)
    {
        using var files=new Files();var audio=mode is "flac-24" or "opus" or "channels";var first=Path.Combine(files.Directory,"first.mp4");var second=Path.Combine(files.Directory,"second.mp4");var output=Path.Combine(files.Directory,audio?"published.m4a":"published.mp4");
        var rejected=mode is "geometry" or "pixel" or "codec" or "channels" or "bits-12" or "unsupported" or "cancel";
        async Task Create(string path,bool changed)
        {
            var args=new List<string>{"-v","error","-y","-f","lavfi","-i",audio?"sine=frequency=590:sample_rate=48000":"testsrc2=size="+(mode=="geometry"&&changed?"160x90":"128x72")+":rate=10","-t","4"};
            if(audio)
            {args.AddRange(["-c:a",mode=="opus"?"libopus":"flac","-sample_fmt",mode=="opus"?"flt":"s32","-ac",mode=="channels"&&changed?"2":"1","-strict","-2"]);if(mode!="opus")args.AddRange(["-bits_per_raw_sample","24"]);}
            else
            {
                var encoder=mode.StartsWith("hevc")||mode=="bits-12"?"libx265":mode.StartsWith("vp9")?"libvpx-vp9":mode.StartsWith("av1")?"libaom-av1":mode=="unsupported"?"mpeg4":mode=="codec"&&changed?"libvpx-vp9":"libx264";
                args.AddRange(["-c:v",encoder,"-pix_fmt",mode=="bits-12"?"yuv420p12le":mode.EndsWith("-10")||mode=="pixel"&&changed?"yuv420p10le":"yuv420p"]);
                if(encoder=="libx265")args.AddRange(["-preset","ultrafast","-x265-params","log-level=error:pools=1:frame-threads=1"]);
                if(encoder=="libx264")args.AddRange(["-preset","ultrafast"]);
                if(encoder=="libvpx-vp9")args.AddRange(["-deadline","realtime","-cpu-used","8"]);
                if(encoder=="libaom-av1")args.AddRange(["-cpu-used","8","-row-mt","1"]);
            }
            args.Add(path);await Run(ffmpeg,args);
        }
        await Create(first,false);if(mode is "geometry" or "pixel" or "codec" or "channels")await Create(second,true);else File.Copy(first,second);
        await File.WriteAllTextAsync(output,"previous media");var hashes=new[]{first,second}.ToDictionary(path=>path,path=>SHA256.HashData(File.ReadAllBytes(path)));using var retained=new RetainedMediaCollector(files.Directory);using var cancellation=new CancellationTokenSource();if(mode=="cancel")cancellation.Cancel();var failed=false;
        try
        {
            await FfmpegTool.JoinClippedPeriodsAsync(ffmpeg,[(first,4),(second,4)],[new(4,0,1.5m),new(4,1.5m,2.5m)],output,retained,cancellation.Token);
            WriteCleanupChecks.Check(!rejected,"Unsupported clipping published media.");await Run(ffmpeg,["-v","error","-xerror","-i",output,"-map","0:0","-f","null","-"],true);
            using var details=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-count_frames","-show_entries","stream=codec_name,codec_type,nb_read_frames,pix_fmt:format=duration","-of","json",output]));
            WriteCleanupChecks.Check(details.RootElement.GetProperty("streams")[0].GetProperty("codec_name").GetString()==(audio?mode=="flac-24"?"alac":"aac":"h264"),"Clipped output uses an unexpected codec.");
            if(!audio)
            {
                var expected=FrameHashes(await Run(ffmpeg,["-v","error","-i",first,"-map","0:v:0","-fps_mode","passthrough","-f","framemd5","-"]));var actual=FrameHashes(await Run(ffmpeg,["-v","error","-i",output,"-map","0:v:0","-fps_mode","passthrough","-f","framemd5","-"]));
                WriteCleanupChecks.Check(expected.Length==40&&expected.SequenceEqual(actual),"Clipping changed decoded source pixels.");
            }
            if(mode=="flac-24")
            {
                var expected=Path.Combine(files.Directory,"expected.pcm");var actual=Path.Combine(files.Directory,"actual.pcm");await Run(ffmpeg,["-v","error","-y","-i",first,"-map","0:a:0","-c:a","pcm_s32le","-f","s32le",expected]);await Run(ffmpeg,["-v","error","-y","-i",output,"-map","0:a:0","-c:a","pcm_s32le","-f","s32le",actual]);
                WriteCleanupChecks.Check(File.ReadAllBytes(expected).SequenceEqual(File.ReadAllBytes(actual)),"FLAC clipping changed decoded source samples.");
            }
            WriteCleanupChecks.Check(retained.Items.Count==2&&retained.Items.All(item=>SHA256.HashData(File.ReadAllBytes(item.Path)).SequenceEqual(hashes[first])),"Clipping did not retain complete encoded source periods.");
        }
        catch(Exception error)when(rejected&&error is NotSupportedException or InvalidDataException or OperationCanceledException){failed=true;}
        WriteCleanupChecks.Check(failed==rejected,"Clipping failure contract changed.");if(rejected)WriteCleanupChecks.Check(await File.ReadAllTextAsync(output)=="previous media","Rejected clipping changed previous output.");
        foreach(var (path,hash) in hashes)WriteCleanupChecks.Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),"Clipping changed source period files.");
    }
    private static async Task NativeRenewingClip(string ffmpeg,string mode)
    {
        using var files=new Files();var fixtures=Path.Combine(files.Directory,"fixtures");Directory.CreateDirectory(fixtures);var source=Path.Combine(fixtures,"source.mpd");var arguments=new List<string>{"-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=10","-f","lavfi","-i","sine=frequency=590:sample_rate=48000","-t","4","-c:v",mode=="hevc-opus"?"libx265":"libvpx-vp9"};
        if(mode=="hevc-opus")arguments.AddRange(["-preset","ultrafast","-x265-params","log-level=error:pools=1:frame-threads=1"]);else arguments.AddRange(["-deadline","realtime","-cpu-used","8","-pix_fmt","yuv420p10le"]);
        arguments.AddRange(["-g","20","-c:a",mode=="hevc-opus"?"libopus":"flac","-strict","-2"]);if(mode=="vp9-flac")arguments.AddRange(["-sample_fmt","s32","-bits_per_raw_sample","24"]);arguments.AddRange(["-f","dash","-dash_segment_type","mp4","-seg_duration","2","-init_seg_name","init-$RepresentationID$.m4s","-media_seg_name","chunk-$RepresentationID$-$Number%05d$.m4s",source]);await Run(ffmpeg,arguments);
        var xml=XDocument.Parse(await File.ReadAllTextAsync(source));var initial=LivePeriodSessionChecks.Page(0);var generation=0;var polls=0;var callbacks=0;var reads=0;var rejected=false;
        using var lease=new RenewableMediaAuthorization(initial,_=>{generation++;return Task.FromResult(LivePeriodSessionChecks.Page(generation));},readManifest:(uri,_)=>Task.FromResult(new MediaDocument(uri,Manifest(1,generation),null)));
        var engine=new NativeEngine([new AdaptiveNoOverwriteChecks.Provider(initial with {PlaybackAuthorization=lease})],_=>AdaptiveAccountChecks.Http(async(message,ct)=>
        {
            reads++;WriteCleanupChecks.Check(message.Headers.Authorization is null,"Issuer authorization entered media headers.");
            if(message.RequestUri!.AbsolutePath.EndsWith("live.mpd"))
            {
                polls++;if(polls>1&&generation==0&&!rejected){rejected=true;return AdaptiveAccountChecks.Text("Expired",HttpStatusCode.Forbidden);}
                return AdaptiveAccountChecks.Text(Manifest(polls==1?0:1,generation));
            }
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(fixtures,Path.GetFileName(message.RequestUri.AbsolutePath)),ct))};
        }));
        var request=new NativeDownloadRequest{Url=new("https://issuer.example/"+Path.GetFileName(files.Directory)+"/"+mode+"/watch"),OutputDirectory=Path.Combine(files.Directory,"out"),FileName="recording",FfmpegPath=ffmpeg,KeepOriginal=true,AddMetadata=true,LiveFromStart=true,LiveDurationSeconds=4,Retries=0,FragmentRetries=0,DownloadArchiveFile=Path.Combine(files.Directory,"archive.txt")};
        var result=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);WriteCleanupChecks.Check(result.Succeeded&&generation==1&&rejected&&callbacks==result.OutputPaths.Count&&result.OutputPaths.Count>=6,"Native renewed overlap lost completion or retained sources.");
        foreach(var path in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",path,"-map","0:v?","-map","0:a?","-f","null","-"],true);
        var expected=FrameHashes(await Run(ffmpeg,["-v","error","-i",source,"-map","0:v:0","-fps_mode","passthrough","-f","framemd5","-"]));var actual=FrameHashes(await Run(ffmpeg,["-v","error","-i",result.OutputPath!,"-map","0:v:0","-fps_mode","passthrough","-f","framemd5","-"]));WriteCleanupChecks.Check(expected.Length==40&&expected.SequenceEqual(actual),"Native renewed period overlap changed decoded pixels.");
        var before=reads;var repeat=await engine.DownloadAsync(request with {FfmpegPath=Path.Combine(files.Directory,"unused.exe")},null,null,default,_=>callbacks++);WriteCleanupChecks.Check(repeat.Skipped&&reads==before&&callbacks==result.OutputPaths.Count,"Native overlap archive repeat requested media or callbacks.");
        string Manifest(int stage,int current)
        {
            var root=new XElement("MPD",new XAttribute("type",stage==0?"dynamic":"static"),new XAttribute("availabilityStartTime","2026-01-01T00:00:00Z"),new XAttribute("publishTime",stage==0?"2026-01-01T00:00:02.100Z":"2026-01-01T00:00:04.100Z"),new XAttribute("minimumUpdatePeriod","PT0.25S"),new XAttribute("timeShiftBufferDepth","PT30S"));if(stage>0)root.SetAttributeValue("mediaPresentationDuration","PT4S");root.Add(Period("p0",0));if(stage>0)root.Add(Period("p1",1.5m));return root.ToString();
            XElement Period(string id,decimal start)
            {
                var period=new XElement("Period",new XAttribute("id",id),new XAttribute("start","PT"+start.ToString(System.Globalization.CultureInfo.InvariantCulture)+"S"));
                foreach(var offered in xml.Descendants().Where(element=>element.Name.LocalName=="AdaptationSet"))
                {
                    var adaptation=new XElement(offered);var audio=(string?)adaptation.Attribute("contentType")=="audio";adaptation.SetAttributeValue("id",audio?"a":"v");
                    foreach(var template in adaptation.Descendants().Where(element=>element.Name.LocalName=="SegmentTemplate"))
                    {var scale=long.Parse((string)template.Attribute("timescale")!);template.SetAttributeValue("presentationTimeOffset",(long)(start*scale));template.SetAttributeValue("initialization","https://cdn.example/"+current+"/"+(string)template.Attribute("initialization")!);template.SetAttributeValue("media","https://cdn.example/"+current+"/"+(string)template.Attribute("media")!);}
                    period.Add(adaptation);
                }
                return period;
            }
        }
    }
    private static async Task RenewingClip(string ffmpeg,string mode)
    {
        using var files=new Files();var source=Path.Combine(files.Directory,"source.mpd");
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=10","-t","4","-c:v","libx264","-preset","ultrafast","-g","20","-bf","0","-f","dash","-dash_segment_type","mp4","-seg_duration","2","-init_seg_name","init-$RepresentationID$.m4s","-media_seg_name","chunk-$RepresentationID$-$Number%05d$.m4s",source]);
        var staticManifest=DashManifestParser.Parse(await File.ReadAllTextAsync(source),new("https://cdn.example/source.mpd"));var representation=staticManifest.Representations.Single();var initial=LivePeriodSessionChecks.Page(0,caption:mode=="collision");var generation=0;
        using var lease=new RenewableMediaAuthorization(initial,_=>{generation++;if(mode=="issuer-failed"&&generation==1)throw new IOException("Temporary issuer outage");return Task.FromResult(LivePeriodSessionChecks.Page(generation,caption:mode=="collision",collision:mode=="collision"&&generation==1));},readManifest:(uri,_)=>Task.FromResult(new MediaDocument(uri,Manifest(generation,true,mode=="ending"),null)));
        await lease.ObserveManifestAsync(new(initial.Media[0].Url,Manifest(0,false),null),default);var canonical=new Uri("https://cdn.example/0/chunk-0-00002.m4s");var old=await lease.AuthorizeAsync(canonical,default);
        if(mode is "collision" or "issuer-failed")
        {try{await lease.RenewAsync(canonical,old.Generation,default);throw new InvalidOperationException("Invalid renewal succeeded.");}catch(Exception e)when(e is InvalidDataException or IOException){}WriteCleanupChecks.Check(await lease.AuthorizeAsync(canonical,default)==old,"Failed renewal changed old fragment routing.");}
        var value=await lease.RenewAsync(canonical,old.Generation,default);if(mode=="repeated")for(var index=0;index<24;index++)value=await lease.RenewAsync(canonical,value.Generation,default);
        var bodies=0;using var owner=AdaptiveAccountChecks.Http(async(message,ct)=>{bodies++;WriteCleanupChecks.Check(message.Headers.Authorization is null,"Issuer secret entered media.");var name=Path.GetFileName(message.RequestUri!.AbsolutePath);return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(files.Directory,name),ct))};});using var playback=owner.WithAuthorization(lease);
        var next=DashManifestParser.Parse(Manifest(generation,true,mode=="ending"),initial.Media[0].Url,true);var registry=new DashLiveRecorder.PeriodRegistry();registry.Observe(next);var playlist=new DashLiveRecorder.TrackProjection(playback,next,next.Representations[0],registry).Project(next);var target=Path.Combine(files.Directory,"published.mp4");
        using var retained=new RetainedMediaCollector(files.Directory);await new HlsDownloader(playback){RetainedMedia=retained}.DownloadAsync(new HlsPlaylist{IsComplete=true,MediaSegments=playlist.MediaSegments,Segments=playlist.Segments},target,null,default,ffmpeg:ffmpeg);await Run(ffmpeg,["-v","error","-xerror","-i",target,"-f","null","-"],true);
        using var probe=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-count_frames","-show_entries","stream=nb_read_frames:format=duration","-of","json",target]));WriteCleanupChecks.Check(probe.RootElement.GetProperty("streams")[0].GetProperty("nb_read_frames").GetString()=="40"&&bodies>0,"Renewal clipping dropped/repeated frames.");
        string Manifest(int current,bool closed,bool ended=false)
        {
            var root=new XElement("MPD",new XAttribute("type",ended?"static":"dynamic"),new XAttribute("availabilityStartTime","2026-01-01T00:00:00Z"),new XAttribute("publishTime","2026-01-01T00:00:04.100Z"),new XAttribute("timeShiftBufferDepth","PT30S"));if(ended)root.SetAttributeValue("mediaPresentationDuration","PT4S");root.Add(Period("p0",0));if(closed)root.Add(Period("p1",3));return root.ToString();
            XElement Period(string id,int start)=>new("Period",new XAttribute("id",id),new XAttribute("start","PT"+start+"S"),new XElement("AdaptationSet",new XAttribute("id","v"),new XAttribute("mimeType","video/mp4"),new XElement("Representation",new XAttribute("id","0"),new XAttribute("codecs",representation.Codecs!),new XAttribute("height",72),new XElement("SegmentTemplate",new XAttribute("timescale",1),new XAttribute("presentationTimeOffset",start),new XAttribute("initialization","https://cdn.example/"+current+"/init-0.m4s"),new XAttribute("media","https://cdn.example/"+current+"/chunk-0-$Number%05d$.m4s"),new XElement("SegmentTimeline",new XElement("S",new XAttribute("t",0),new XAttribute("d",2),new XAttribute("r",1)))))));
        }
    }
}





