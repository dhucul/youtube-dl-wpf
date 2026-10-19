using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;

internal static class AdaptationRecoveryChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
        new[]{"video","audio","caption","prefer-group","ambiguous","role","viewpoint","accessibility","language","codec","channels","sampling","bandwidth","implicit","unknown-language","unknown-role","pinned","prefix-collision","canonical"}
            .Select(mode=>("Adaptation recovery selection "+mode,(Func<Task>)(()=>Selection(mode))))
        .Concat(new[]{"signed","dropped","binding","profile","time","private"}.Select(mode=>("Adaptation recovery stable projection "+mode,(Func<Task>)(()=>Projection(mode)))))
        .Concat(new[]{"wvtt","stpp","same-group","ambiguous","language","role","codec","initialization","within-period","restart","restart-binding","restart-bytes"}.Select(mode=>("Adaptation recovery captions "+mode,(Func<Task>)(()=>Captions(mode)))))
        .Concat(new[]{"from-start","ordinary-vod"}.Select(mode=>("Adaptation recovery ended safety "+mode,(Func<Task>)(()=>Ended(mode)))));
    internal static readonly Uri Url=new("https://cdn.example/live.mpd");
    private static void Check(bool value,string message)=>WriteCleanupChecks.Check(value,message);
    internal static string Manifest(string mode="video",int generation=0,bool dropped=false,bool changed=false,bool captions=false)
    {
        var root=new XElement("MPD",new XAttribute("type","dynamic"),new XAttribute("availabilityStartTime","2026-01-01T00:00:00Z"),new XAttribute("publishTime","2026-01-01T00:00:08.100Z"),new XAttribute("minimumUpdatePeriod","PT0.25S"),new XAttribute("timeShiftBufferDepth","PT30S"));
        if(!dropped)root.Add(Period("p0",0));root.Add(Period("p1",4));return root.ToString();
        XElement Period(string id,int start)
        {
            var period=new XElement("Period",new XAttribute("id",id),new XAttribute("start","PT"+start+"S"));period.Add(Adaptation(false));if(captions)period.Add(Adaptation(true));return period;
            XElement Adaptation(bool text)
            {
                var audio=mode=="audio";var kind=text||mode=="caption"?"text":audio?"audio":"video";var group=id=="p0"||mode=="same-group"?"original-"+kind:"changed-"+kind;
                if(mode=="prefer-group"&&id=="p1")group="original-"+kind;
                var adaptation=new XElement("AdaptationSet",new XAttribute("id",group),new XAttribute("mimeType",kind=="text"?"application/mp4":kind+"/mp4"),new XAttribute("contentType",kind),new XAttribute("lang",mode=="language"&&id=="p1"?"fr":mode=="unknown-language"?"und":"en"),new XAttribute("audioSamplingRate",mode=="sampling"&&id=="p1"?44100:48000));
                if(mode=="implicit"&&id=="p1")adaptation.Attribute("id")!.Remove();
                if(mode!="unknown-role")adaptation.Add(new XElement("Role",new XAttribute("schemeIdUri","urn:mpeg:dash:role:2011"),new XAttribute("value",mode=="role"&&id=="p1"?"commentary":kind=="text"?"subtitle":"main")));
                adaptation.Add(new XElement("AudioChannelConfiguration",new XAttribute("schemeIdUri","urn:mpeg:dash:23003:3:audio_channel_configuration:2011"),new XAttribute("value",mode=="channels"&&id=="p1"?6:2)));
                if(mode=="viewpoint"&&id=="p1")adaptation.Add(new XElement("Viewpoint",new XAttribute("schemeIdUri","urn:camera"),new XAttribute("value","other")));
                if(mode=="accessibility"&&id=="p1")adaptation.Add(new XElement("Accessibility",new XAttribute("schemeIdUri","urn:access"),new XAttribute("value","other")));
                var raw=id=="p0"?"first":changed&&id=="p1"?"mutated":"second";var codec=kind=="text"?mode=="stpp"?"stpp":"wvtt":kind=="audio"?"mp4a.40.2":"avc1.42c00a";
                if(mode=="codec"&&id=="p1")codec=kind=="text"?"stpp":"changed";
                XElement Representation(string name)=>new("Representation",new XAttribute("id",name),new XAttribute("codecs",codec),new XAttribute("width",128),new XAttribute("height",72),new XAttribute("bandwidth",mode=="bandwidth"&&id=="p1"?200000:100000),new XAttribute("frameRate",10),
                    new XElement("SegmentTemplate",new XAttribute("timescale",1),new XAttribute("duration",kind=="text"?1:2),new XAttribute("initialization","https://cdn.example/"+generation+"/"+id+"/"+kind+"-init.mp4"),new XAttribute("media","https://cdn.example/"+generation+"/"+id+"/"+kind+"-$Time$.m4s")));
                adaptation.Add(Representation(raw));
                if(mode=="ambiguous"&&id=="p1")adaptation.Add(Representation("duplicate"));
                if(mode=="prefer-group"&&id=="p1"){var extra=new XElement(adaptation);extra.SetAttributeValue("id","unrelated-"+kind);period.Add(extra);}
                return adaptation;
            }
        }
    }
    private static async Task Selection(string mode)
    {
        var manifest=DashManifestParser.Parse(Manifest(mode),Url);var text=mode=="caption";
        var first=text?manifest.Periods[0].Captions[0].Representation:manifest.Periods[0].Representations[0];var offered=text?manifest.Periods[1].Captions.Select(c=>c.Representation).ToArray():manifest.Periods[1].Representations;var selection=new DashTrackSelection(first);
        var invalid=mode is "ambiguous" or "role" or "viewpoint" or "accessibility" or "language" or "codec" or "channels" or "sampling" or "bandwidth" or "implicit" or "unknown-role";var failed=false;
        // Unknown language prevents audio/text reassociation; a video language is not its identity.
        if(mode=="unknown-language"){manifest=DashManifestParser.Parse(Manifest("audio").Replace("lang=\"en\"","lang=\"und\""),Url);first=manifest.Periods[0].Representations[0];offered=manifest.Periods[1].Representations;selection=new(first);invalid=true;}
        try
        {
            var result=selection.Resolve("p1",offered);Check(!invalid&&result.Id.EndsWith(":second"),"Changed adaptation did not preserve its unique selected profile.");
            if(mode=="pinned"){var later=DashManifestParser.Parse(Manifest(changed:true),Url);try{selection.Resolve("p1",later.Periods[1].Representations);throw new InvalidOperationException("A pinned adaptation changed within a period.");}catch(InvalidDataException){}}
            if(mode=="prefix-collision")
            {
                var a=new DashRepresentation{Id="v:x:0",AdaptationIdentity="v:x",Url=Url,MimeType="video/mp4",Codecs="avc1",Width=128,Height=72};var b=new DashRepresentation{Id="v:x:0",AdaptationIdentity="v",Url=Url,MimeType="video/mp4",Codecs="avc1",Width=128,Height=72};var pin=new DashTrackSelection(a);pin.Resolve("p",[a]);try{pin.Resolve("p",[b]);throw new InvalidOperationException("Ambiguous composite ID bypassed group pinning.");}catch(InvalidDataException){}
            }
            if(mode=="canonical")
            {
                using var http=AdaptiveAccountChecks.Http((_,_)=>Task.FromResult(AdaptiveAccountChecks.Text(Manifest())));var read=await new DashDownloader(http).ReadAsync(Url,default);Check(read.Periods[0].Representations[0].Roles==first.Roles&&read.Periods[0].Representations[0].Viewpoints==first.Viewpoints,"Canonical reading discarded track semantics.");
            }
        }
        catch(Exception error)when(invalid&&error is InvalidDataException or NotSupportedException){failed=true;}
        Check(failed==invalid,"Changed adaptation refusal changed: "+mode);
    }
    private static Task Projection(string mode)
    {
        using var http=new MediaHttp();var before=DashManifestParser.Parse(Manifest(),Url);var registry=new DashLiveRecorder.PeriodRegistry();registry.Observe(before);var first=new DashLiveRecorder.TrackProjection(http,before,before.Representations[0],registry).Project(before).MediaSegments.Where(segment=>segment.DiscontinuitySequence==1).ToArray();
        var xml=Manifest(generation:2,dropped:mode!="signed",changed:mode=="binding");if(mode=="profile")xml=xml.Replace("width=\"128\"","width=\"160\"");if(mode=="time")xml=xml.Replace("start=\"PT4S\"","start=\"PT5S\"");var after=DashManifestParser.Parse(xml,Url);var nextRegistry=new DashLiveRecorder.PeriodRegistry();nextRegistry.Observe(after);var next=new DashLiveRecorder.TrackProjection(http,after,after.Representations[0],nextRegistry).Project(after).MediaSegments.Where(segment=>segment.ProgramDateTime>=DateTimeOffset.Parse("2026-01-01T00:00:04Z")).ToArray();
        var same=mode is not ("profile" or "time");Check((first[0].RecoveryIdentity==next[0].RecoveryIdentity)==same,"Restart changed stable timeline identity unexpectedly.");if(same)Check(first[0].RecoveryResourceIdentity==next[0].RecoveryResourceIdentity&&first[0].RecoveryInitializationIdentity==next[0].RecoveryInitializationIdentity,"Restart depended on signed URLs or initial period references.");if(mode=="binding")Check(first[0].RecoveryBinding!=next[0].RecoveryBinding,"Restart lost its physical accepted binding.");if(mode=="private")Check(!JsonSerializer.Serialize(new[]{next[0].RecoveryIdentity,next[0].RecoveryResourceIdentity,next[0].RecoveryBinding}).Contains("https://"),"Recovery bindings included resource addresses.");return Task.CompletedTask;
    }
    private static async Task Captions(string mode)
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var changed=false;var changeInitialization=false;var generation=0;var reads=0;var isRestart=mode.StartsWith("restart",StringComparison.Ordinal);var baseMode=mode=="stpp"?"stpp":mode is "ambiguous" or "language" or "role" or "codec"?mode:mode=="same-group"?"same-group":"video";
            DashManifest Current()=>DashManifestParser.Parse(changeInitialization?Manifest(baseMode,generation,changed:changed,captions:true).Replace("text-init.mp4","changed-init.mp4"):Manifest(baseMode,generation,changed:changed,captions:true),Url);
            using var http=AdaptiveAccountChecks.Http((message,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(Bytes(message.RequestUri!))}));
            var request=new NativeDownloadRequest{Url=Url,OutputDirectory=directory,WriteSubs=true,SubLanguages="en",AdaptiveRecoveryScope=directory,LiveFromStart=true,LiveDurationSeconds=8,Retries=0};
            var invalid=mode is "ambiguous" or "language" or "role" or "codec" or "initialization" or "within-period" or "restart-binding" or "restart-bytes";var failed=false;
            var initial=Current();var media=new HlsSegment(Url,0,null,null,null,8,ProgramDateTime:initial.AvailabilityStartTime);var destination=Path.Combine(directory,"video.mp4");
            try
            {
                using(var capture=new DashLiveCaptionCapture(initial,[],request,http,new(null),_=>Task.FromResult(Current()),destination))
                {
                    await capture.ObserveAsync(new(media,"unused",null),default);
                    if(mode=="initialization"){changeInitialization=true;await capture.ObserveAsync(new(media,"unused",null),default);}
                    if(mode=="within-period"){changed=true;await capture.ObserveAsync(new(media,"unused",null),default);}
                    var output=capture.Finish(new(){MediaSegments=[media]});Check(output.Single().Content!.Contains("Continuing"),"Caption transition lost its text.");
                }
                if(isRestart)
                {
                    generation=2;changed=mode=="restart-binding";var restarted=Current();using var capture=new DashLiveCaptionCapture(restarted,[],request,http,new(null),_=>Task.FromResult(Current()),destination);await capture.ObserveAsync(new(media,"unused",null),default);Check(capture.Finish(new(){MediaSegments=[media]}).Single().Content!.Contains("Continuing"),"Caption restart lost saved text.");
                }
                Check(!invalid,"Invalid caption transition succeeded.");
            }
            catch(Exception error)when(invalid&&error is InvalidDataException or NotSupportedException){failed=true;}
            Check(failed==invalid,"Caption transition refusal changed: "+mode);Check(reads>=0,"Invalid read count.");
            byte[] Bytes(Uri uri)
            {
                reads++;var name=Path.GetFileName(uri.AbsolutePath);var ttml=baseMode=="stpp";var text=mode=="restart-bytes"&&generation>0?"Modified":"Continuing";
                var payload=ttml?System.Text.Encoding.UTF8.GetBytes("<tt xmlns=\"http://www.w3.org/ns/ttml\"><body><div><p begin=\"0s\" end=\"4s\">"+text+"</p></div></body></tt>"):DashCaptionChecks.Sample(text);
                var time=name.Contains("init")?0:ulong.Parse(Path.GetFileNameWithoutExtension(name).Split('-')[^1]);var movie=DashCaptionChecks.Movie([payload],out var offset,time*1000,ttml:ttml);return name.Contains("init")?movie[..offset]:movie[offset..];
            }
        }
        finally{Directory.Delete(directory,true);}
    }
    private static async Task Ended(string mode)
    {
        var xml=XDocument.Parse(Manifest(dropped:true));xml.Root!.SetAttributeValue("type","static");xml.Root.SetAttributeValue("mediaPresentationDuration","PT8S");
        if(mode=="ordinary-vod")
        {xml.Root.Attribute("availabilityStartTime")!.Remove();try{DashManifestParser.Parse(xml.ToString(),Url,true);throw new InvalidOperationException("Ordinary VOD was accepted as ended live projection.");}catch(InvalidDataException){}return;}
        var directory=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            using var http=AdaptiveAccountChecks.Http((_,_)=>throw new InvalidOperationException("Incomplete beginning requested media."));var manifest=DashManifestParser.Parse(xml.ToString(),Url,true);var periods=new DashLiveRecorder.PeriodRegistry();periods.Observe(manifest);var playlist=new DashLiveRecorder.TrackProjection(http,manifest,manifest.Representations[0],periods).Project(manifest);
            try{await new HlsLiveRecorder(http).DownloadAsync(playlist,Path.Combine(directory,"recording.mp4"),new(){Url=Url,OutputDirectory=directory,LiveFromStart=true,NoContinue=true,FfmpegPath=Path.Combine(directory,"missing.exe")},null,new(null),default);throw new InvalidOperationException("Ended incomplete window invented its beginning.");}catch(InvalidDataException){}
        }
        finally{Directory.Delete(directory,true);}
    }
}

