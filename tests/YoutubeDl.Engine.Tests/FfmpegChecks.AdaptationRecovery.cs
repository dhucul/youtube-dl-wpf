using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> AdaptationRecoveryMediaChecks(string ffmpeg)=>
        new[]{"pair","video","audio","wvtt","stpp","ambiguous","viewpoint","accessibility","caption-role","caption-missing","caption-codec","restart","shared","drop","boundary","restart-media-id","restart-caption-id","restart-media-bytes","restart-caption-bytes","journal-corrupt","prefix-missing","restart-cancel","kill-wvtt","kill-stpp"}
            .Select(mode=>("Adaptation recovery media "+mode,(Func<Task>)(()=>AdaptationMedia(ffmpeg,mode))));
    internal static async Task<int> AdaptationWorker(string ffmpeg,string directory,string mode)
    {await AdaptationMedia(ffmpeg,mode,directory,true);return 1;}
    private static async Task AdaptationMedia(string ffmpeg,string mode,string? existing=null,bool worker=false)
    {
        using var files=new Files(existing,!worker);var directory=files.Directory;var assets=Path.Combine(directory,"fixtures");var mpd=Path.Combine(assets,"source.mpd");var restart=mode is not ("pair" or "video" or "audio" or "wvtt" or "stpp" or "ambiguous" or "viewpoint" or "accessibility" or "caption-role" or "caption-missing" or "caption-codec");var captions=mode is not ("pair" or "video" or "audio");var ttml=mode is "stpp" or "kill-stpp";var boundary=mode=="boundary";var shared=mode is "shared" or "kill-wvtt" or "kill-stpp";var invalid=mode is "ambiguous" or "viewpoint" or "accessibility" or "caption-role" or "caption-missing" or "caption-codec" or "restart-media-id" or "restart-caption-id" or "restart-media-bytes" or "restart-caption-bytes" or "journal-corrupt" or "prefix-missing" or "restart-cancel";
        var policyBatch=mode.StartsWith("selection-",StringComparison.Ordinal);var legacy=mode.StartsWith("selection-legacy",StringComparison.Ordinal);var conversion=mode.StartsWith("selection-convert",StringComparison.Ordinal);
        ttml|=mode is "selection-convert-reverse" or "selection-legacy-stpp";boundary|=mode=="selection-convert-boundary";shared|=legacy||mode is "selection-convert-shared" or "selection-convert-kill";invalid|=mode is "selection-convert-role" or "selection-convert-wrong-init" or "selection-legacy-future" or "selection-legacy-corrupt" or "selection-legacy-cancel" or "selection-legacy-missing";
        if(!worker)
        {
            Directory.CreateDirectory(assets);await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=10","-f","lavfi","-i","sine=frequency=650:sample_rate=48000","-t","4","-c:v","libx264","-preset","ultrafast","-g","20","-bf","0","-c:a","alac","-f","dash","-dash_segment_type","mp4","-seg_duration","2","-init_seg_name","init-$RepresentationID$.m4s","-media_seg_name","chunk-$RepresentationID$-$Number%05d$.m4s",mpd]);
            byte[]? initialization=null;for(var index=0;index<4;index++){var payload=ttml?Encoding.UTF8.GetBytes("<tt xmlns=\"http://www.w3.org/ns/ttml\"><body><div><p begin=\"0s\" end=\"4s\">Continuing caption.</p></div></body></tt>"):DashCaptionChecks.Sample("Continuing caption.");var movie=DashCaptionChecks.Movie([payload],out var offset,(ulong)index*1000,ttml:ttml);initialization??=movie[..offset];await File.WriteAllBytesAsync(Path.Combine(assets,"caption-"+index+".m4s"),movie[offset..]);}await File.WriteAllBytesAsync(Path.Combine(assets,"caption-init.mp4"),initialization!);
            if(conversion)foreach(var codec in new[]{"vtt","ttml"})for(var index=0;index<4;index++){var isTtml=codec=="ttml";var payload=isTtml?Encoding.UTF8.GetBytes("<tt xmlns=\"http://www.w3.org/ns/ttml\"><body><div><p begin=\"0s\" end=\"4s\">Continuing caption.</p></div></body></tt>"):DashCaptionChecks.Sample("Continuing caption.");var movie=DashCaptionChecks.Movie([payload],out var offset,(ulong)index*1000,ttml:isTtml);await File.WriteAllBytesAsync(Path.Combine(assets,codec+"-caption-"+index+".m4s"),movie[offset..]);if(index==0)await File.WriteAllBytesAsync(Path.Combine(assets,codec+"-caption-init.mp4"),movie[..offset]);}
        }
        var xml=XDocument.Parse(await File.ReadAllTextAsync(mpd));var videoId=(string)xml.Descendants().First(element=>element.Name.LocalName=="AdaptationSet"&&(string?)element.Attribute("contentType")=="video").Descendants().First(element=>element.Name.LocalName=="Representation").Attribute("id")!;var audioId=(string)xml.Descendants().First(element=>element.Name.LocalName=="AdaptationSet"&&(string?)element.Attribute("contentType")=="audio").Descendants().First(element=>element.Name.LocalName=="Representation").Attribute("id")!;
        var output=Path.Combine(directory,"out");Directory.CreateDirectory(output);var target=Path.Combine(output,mode=="audio"?"recording.m4a":"recording.mp4");var sidecar=Path.Combine(output,"recording.en.vtt");var archive=Path.Combine(directory,"archive.txt");if(!worker){await File.WriteAllTextAsync(target,"previous media");await File.WriteAllTextAsync(sidecar,"previous captions");await File.WriteAllTextAsync(archive,"previous archive\n");}
        var servedVideo=0;var servedAudio=0;var resumed=false;var callbacks=0;var polls=0;var oldReads=0;var bodies=0;var seedGeneration=0;var preferredAudioReads=0;
        NativeEngine Engine()
        {
            var generation=resumed?2:0;var page=new PageMetadata{Id="fixture",Extractor="adaptation-fixture",Title="Fixture",IsLive=true,Media=[new(){Id="main",Url=new("https://cdn.example/g"+generation+"/live.mpd?token=fixture"),Kind=MediaKind.Dash,Extension="mpd",HasVideo=true,HasAudio=true}]};
            var lease=new RenewableMediaAuthorization(page,_=>{seedGeneration++;return Task.FromResult(page);},readManifest:(uri,_)=>Task.FromResult(new MediaDocument(uri,Snapshot(generation,2),null)));
            return new([new AdaptiveNoOverwriteChecks.Provider(page with {PlaybackAuthorization=lease})],_=>AdaptiveAccountChecks.Http(async(message,ct)=>
            {
                var uri=message.RequestUri!;if(uri.AbsolutePath.EndsWith("live.mpd")){polls++;return AdaptiveAccountChecks.Text(Snapshot(generation,resumed||!restart||polls>1?2:1));}
                if(mode is "selection-language" or "selection-role"&&(Path.GetFileName(uri.AbsolutePath)=="init-"+audioId+".m4s"||Path.GetFileName(uri.AbsolutePath).StartsWith("chunk-"+audioId+"-",StringComparison.Ordinal))){WriteCleanupChecks.Check(uri.AbsolutePath.Contains("/preferred/"),"The explicit audio preference downloaded the default track.");preferredAudioReads++;}
                if(resumed&&uri.AbsolutePath.Contains("/p0/")){oldReads++;return AdaptiveAccountChecks.Text("Retired",HttpStatusCode.Gone);}
                var lastVideo=Path.GetFileName(uri.AbsolutePath)=="chunk-"+videoId+"-00002.m4s";var lastAudio=Path.GetFileName(uri.AbsolutePath)=="chunk-"+audioId+"-00002.m4s";
                var sharedTail=shared&&(lastVideo&&Interlocked.Increment(ref servedVideo)>1||lastAudio&&Interlocked.Increment(ref servedAudio)>1);
                if(restart&&!resumed&&(uri.AbsolutePath.Contains("/p1/")&&(lastVideo||lastAudio)||sharedTail))
                {
                    if(worker)
                    {
                        while(!CaptionGate(directory,true,2))await Task.Delay(20,ct);await File.WriteAllTextAsync(Path.Combine(directory,"adaptation-ready"),Environment.ProcessId.ToString(),ct);await Task.Delay(Timeout.Infinite,ct);
                    }
                    await Task.Delay(50,ct);return AdaptiveAccountChecks.Text("Interrupted",HttpStatusCode.ServiceUnavailable);
                }
                bodies++;var asset=Path.GetFileName(uri.AbsolutePath);if(mode=="selection-convert-wrong-init"&&asset=="ttml-caption-init.mp4")asset="vtt-caption-init.mp4";var bytes=await File.ReadAllBytesAsync(Path.Combine(assets,asset),ct);
                if(resumed&&(mode=="restart-caption-bytes"&&uri.AbsolutePath.Contains("caption-0.m4s")||mode=="restart-media-bytes"&&Path.GetFileName(uri.AbsolutePath)=="chunk-"+videoId+"-00001.m4s")){bytes=bytes.ToArray();bytes[^1]^=1;}
                return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
            }));
        }
        var request=new NativeDownloadRequest{Url=new("https://issuer.example/"+Path.GetFileName(directory)+"/watch"),OutputDirectory=output,FileName="recording",FfmpegPath=ffmpeg,KeepOriginal=true,AddMetadata=true,LiveFromStart=true,LiveDurationSeconds=boundary?7:8,WriteSubs=captions,EmbedSubs=captions,SubLanguages="en",VideoOnly=mode=="video",AudioOnly=mode=="audio",Retries=0,FragmentRetries=0,DownloadArchiveFile=archive};
        if(policyBatch&&!legacy)request=request with {Quality=mode=="selection-worst"?QualityPreference.Worst:QualityPreference.Best,LiveDashSelection=new(){AudioLanguages=mode=="selection-language"?["fr","en"]:[],AudioRoles=mode=="selection-role"?["commentary","main"]:[]}};
        if(worker){await Engine().DownloadAsync(request,null,null,default);throw new InvalidOperationException("Worker escaped its checkpoint gate.");}
        if(restart)
        {
            if(mode.StartsWith("kill",StringComparison.Ordinal)||legacy||mode=="selection-convert-kill")
            {
                var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};start.ArgumentList.Add(typeof(FfmpegChecks).Assembly.Location);if(legacy){start.ArgumentList.Add("--legacy-adaptation-worker");start.ArgumentList.Add(LegacyNativeAssembly!);}else start.ArgumentList.Add("--adaptation-worker");foreach(var argument in new[]{ffmpeg,directory,legacy?ttml?"kill-stpp":"kill-wvtt":mode})start.ArgumentList.Add(argument);using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
                try{for(var attempt=0;attempt<350&&!File.Exists(Path.Combine(directory,"adaptation-ready"))&&!process.HasExited;attempt++)await Task.Delay(20);WriteCleanupChecks.Check(File.Exists(Path.Combine(directory,"adaptation-ready")),"Adaptation worker failed before a durable media/caption prefix: "+(process.HasExited?await stderr:"timeout"));process.Kill(true);await process.WaitForExitAsync();}finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}await stdout;await stderr;}
                if(legacy)foreach(var file in Directory.GetFiles(directory,"*.json",SearchOption.AllDirectories).Where(path=>Path.GetFileName(path) is "timeline.json" or "captions.json")){using var saved=JsonDocument.Parse(await File.ReadAllTextAsync(file));WriteCleanupChecks.Check(saved.RootElement.GetProperty("Schema").GetInt32()==1,"Legacy worker did not create a schema-1 checkpoint.");}
            }
            else{try{await Engine().DownloadAsync(request,null,null,default,_=>callbacks++);throw new InvalidOperationException("Interrupted recording succeeded.");}catch(Exception error)when(error is IOException or HttpRequestException||invalid&&error is InvalidDataException or NotSupportedException){} }
            WriteCleanupChecks.Check(await File.ReadAllTextAsync(target)=="previous media"&&await File.ReadAllTextAsync(archive)=="previous archive\n"&&callbacks==0,"Interrupted session changed publication.");
            if(mode is "selection-best" or "selection-worst")foreach(var path in Directory.GetFiles(directory,"timeline.json",SearchOption.AllDirectories))
            {using var timeline=JsonDocument.Parse(await File.ReadAllTextAsync(path));foreach(var row in timeline.RootElement.GetProperty("Records").EnumerateArray()){using var metadata=JsonDocument.Parse(row.GetProperty("Metadata").GetString()!);if(metadata.RootElement.GetProperty("Kind").GetString()!="media")continue;using var binding=JsonDocument.Parse(metadata.RootElement.GetProperty("Binding").GetString()!);using var profile=JsonDocument.Parse(binding.RootElement.GetProperty("Profile").GetString()!);var expected=mode=="selection-worst"?100000:binding.RootElement.GetProperty("Period").GetString()=="p0"?200000:300000;WriteCleanupChecks.Check(profile.RootElement.GetProperty("Bandwidth").GetInt32()==expected,"The captured period did not follow the requested bitrate quality.");}}
            if(mode=="journal-corrupt"){var journal=Directory.GetFiles(directory,"captions.json",SearchOption.AllDirectories).Single();await File.WriteAllTextAsync(journal,"corrupt");}
            if(mode=="prefix-missing"){var prefix=Directory.GetFiles(directory,"timeline.json",SearchOption.AllDirectories).First();using var document=JsonDocument.Parse(await File.ReadAllTextAsync(prefix));var index=0;foreach(var row in document.RootElement.GetProperty("Records").EnumerateArray()){if(row.GetProperty("Metadata").GetString()!.Contains("\"Kind\":\"media\"")){File.Delete(Path.Combine(Path.GetDirectoryName(prefix)!,"fragment-"+index.ToString("D6")+".bin"));break;}index++;}}
            if(mode=="selection-legacy-future"){var path=Directory.GetFiles(directory,"timeline.json",SearchOption.AllDirectories).First();var saved=System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;saved["Schema"]=99;await File.WriteAllTextAsync(path,saved.ToJsonString());}
            if(mode=="selection-legacy-corrupt")await File.WriteAllTextAsync(Directory.GetFiles(directory,"captions.json",SearchOption.AllDirectories).Single(),"corrupt");
            if(mode=="selection-legacy-missing"){var path=Directory.GetFiles(directory,"timeline.json",SearchOption.AllDirectories).First();File.Delete(Path.Combine(Path.GetDirectoryName(path)!,"fragment-000001.bin"));}
            resumed=true;polls=0;bodies=0;
        }
        using var canceled=new CancellationTokenSource();if(mode is "restart-cancel" or "selection-legacy-cancel")canceled.Cancel();var failed=false;
        try
        {
            var result=await Engine().DownloadAsync(request,null,null,canceled.Token,_=>callbacks++);WriteCleanupChecks.Check(!invalid&&result.Succeeded&&callbacks==result.OutputPaths.Count,"Adaptation/caption recovery did not publish complete outputs.");
            foreach(var path in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",path,"-map","0:v?","-map","0:a?","-f","null","-"],true);
            if(mode!="audio"){var frames=FrameHashes(await Run(ffmpeg,["-v","error","-i",mpd,"-map","0:v:0","-fps_mode","passthrough","-f","framemd5","-"]));var expected=(boundary?frames.Take(30):frames).Concat(frames).ToArray();var actual=FrameHashes(await Run(ffmpeg,["-v","error","-i",result.OutputPath!,"-map","0:v:0","-fps_mode","passthrough","-f","framemd5","-"]));WriteCleanupChecks.Check(expected.SequenceEqual(actual),"Recovery lost or repeated decoded video frames.");}
            if(captions){var saved=await File.ReadAllTextAsync(sidecar);var embedded=await Run(ffmpeg,["-v","error","-i",result.OutputPath!,"-map","0:s:0","-f","webvtt","-"]);WriteCleanupChecks.Check(saved.Contains("Continuing caption.")&&embedded.Contains("Continuing caption."),"Recovery lost saved or embedded caption text.");}
            if(mode is "selection-language" or "selection-role")WriteCleanupChecks.Check(preferredAudioReads>0,"The preferred audio track was never transferred.");
            if(restart)WriteCleanupChecks.Check(oldReads==0,"Fresh session requested a retired period.");var before=bodies;var repeat=await Engine().DownloadAsync(request with {FfmpegPath=Path.Combine(directory,"unused.exe")},null,null,default,_=>callbacks++);WriteCleanupChecks.Check(repeat.Skipped&&bodies==before&&callbacks==result.OutputPaths.Count,"Recovery archive replay transferred data or callbacks.");
        }
        catch(Exception error)when(invalid&&error is IOException or InvalidDataException or NotSupportedException or OperationCanceledException or JsonException){failed=true;}
        WriteCleanupChecks.Check(failed==invalid,"Adaptation/caption failure contract changed: "+mode);if(invalid)WriteCleanupChecks.Check(await File.ReadAllTextAsync(target)=="previous media"&&await File.ReadAllTextAsync(sidecar)=="previous captions"&&await File.ReadAllTextAsync(archive)=="previous archive\n"&&callbacks==0,"Invalid recovery changed prior output/caption/archive state.");
        string Snapshot(int generation,int stage)
        {
            var root=new XElement("MPD",new XAttribute("type",resumed||!restart&&polls>1?"static":"dynamic"),new XAttribute("availabilityStartTime","2026-01-01T00:00:00Z"),new XAttribute("publishTime",stage<2?"2026-01-01T00:00:06.100Z":"2026-01-01T00:00:08.100Z"),new XAttribute("minimumUpdatePeriod","PT0.25S"),new XAttribute("timeShiftBufferDepth","PT30S"));if(resumed||!restart)root.SetAttributeValue("mediaPresentationDuration",boundary?"PT7S":"PT8S");if(!resumed)root.Add(Period("p0",0));root.Add(Period("p1",boundary?3:4));return root.ToString();
            XElement Period(string id,int start)
            {
                var period=new XElement("Period",new XAttribute("id",id),new XAttribute("start","PT"+start+"S"));if(id=="p0")period.SetAttributeValue("duration",boundary?"PT3S":"PT4S");
                foreach(var original in xml.Descendants().Where(element=>element.Name.LocalName=="AdaptationSet"))
                {
                    var adaptation=new XElement(original);var audio=(string?)adaptation.Attribute("contentType")=="audio";adaptation.SetAttributeValue("id",id+"-"+(audio?"audio":"video")+(resumed&&mode=="restart-media-id"?"-mutated":""));adaptation.SetAttributeValue("lang","en");adaptation.Add(new XElement("Role",new XAttribute("schemeIdUri","urn:mpeg:dash:role:2011"),new XAttribute("value","main")));
                    if(id=="p1"&&mode is "viewpoint" or "accessibility")adaptation.Add(new XElement(mode=="viewpoint"?"Viewpoint":"Accessibility",new XAttribute("schemeIdUri","urn:changed"),new XAttribute("value","other")));
                    foreach(var representation in adaptation.Elements().Where(element=>element.Name.LocalName=="Representation"))
                    {
                        var raw=(string)representation.Attribute("id")!;if(!audio)representation.SetAttributeValue("width",128);representation.SetAttributeValue("id",id+"-"+raw);if(audio)representation.SetAttributeValue("codecs","alac");
                        foreach(var template in representation.Descendants().Where(element=>element.Name.LocalName=="SegmentTemplate")){template.SetAttributeValue("initialization","https://cdn.example/g"+generation+"/"+(shared?"shared":id)+"/init-"+raw+".m4s");template.SetAttributeValue("media","https://cdn.example/g"+generation+"/"+(shared?"shared":id)+"/chunk-"+raw+"-$Number%05d$.m4s");}
                    }
                    if(mode is "selection-best" or "selection-worst"){foreach(var representation in adaptation.Elements().Where(element=>element.Name.LocalName=="Representation").ToArray()){representation.SetAttributeValue("bandwidth",100000);var copy=new XElement(representation);copy.SetAttributeValue("id",representation.Attribute("id")!.Value+"-quality");copy.SetAttributeValue("bandwidth",id=="p0"?200000:300000);adaptation.Add(copy);}}
                    if(audio&&mode is "selection-language" or "selection-role"){var alternative=new XElement(adaptation);alternative.SetAttributeValue("id",id+"-preferred-audio");alternative.SetAttributeValue("lang",mode=="selection-language"?"fr":"en");if(mode=="selection-role")alternative.Element("Role")!.SetAttributeValue("value","commentary");foreach(var rep in alternative.Elements().Where(element=>element.Name.LocalName=="Representation"))rep.SetAttributeValue("id",rep.Attribute("id")!.Value+"-preferred");foreach(var template in alternative.Descendants().Where(element=>element.Name.LocalName=="SegmentTemplate")){template.SetAttributeValue("initialization",template.Attribute("initialization")!.Value.Replace("/init-","/preferred/init-"));template.SetAttributeValue("media",template.Attribute("media")!.Value.Replace("/chunk-","/preferred/chunk-"));}period.Add(alternative);}
                    if(mode=="ambiguous"&&id=="p1"){var copy=new XElement(adaptation.Elements().First(element=>element.Name.LocalName=="Representation"));copy.SetAttributeValue("id","duplicate");adaptation.Add(copy);}period.Add(adaptation);
                }
                if(captions&&!(mode=="caption-missing"&&id=="p1"))
                {
                    var caption=new XElement("AdaptationSet",new XAttribute("id",id+"-captions"+(resumed&&mode=="restart-caption-id"?"-mutated":"")),new XAttribute("contentType","text"),new XAttribute("mimeType","application/mp4"),new XAttribute("lang","en"),new XElement("Role",new XAttribute("schemeIdUri","urn:mpeg:dash:role:2011"),new XAttribute("value",mode=="caption-role"&&id=="p1"?"commentary":"subtitle")),new XElement("Representation",new XAttribute("id",id+"-text"),new XAttribute("codecs",mode=="caption-codec"&&id=="p1"?"stpp":ttml?"stpp":"wvtt"),new XElement("SegmentTemplate",new XAttribute("timescale",1),new XAttribute("duration",1),new XAttribute("initialization","https://cdn.example/g"+generation+"/"+(shared?"shared":id)+"/caption-init.mp4"),new XAttribute("media","https://cdn.example/g"+generation+"/"+(shared?"shared":id)+"/caption-$Time$.m4s"))));period.Add(caption);
                    if(conversion){var codec=(id=="p1")!=ttml?"ttml":"vtt";caption.Element("Representation")!.SetAttributeValue("codecs",codec=="ttml"?"stpp":"wvtt");foreach(var template in caption.Descendants("SegmentTemplate")){template.SetAttributeValue("initialization",template.Attribute("initialization")!.Value.Replace("caption-init",codec+"-caption-init"));template.SetAttributeValue("media",template.Attribute("media")!.Value.Replace("caption-",codec+"-caption-"));}if(mode=="selection-convert-role"&&id=="p1")caption.Element("Role")!.SetAttributeValue("value","commentary");}
                }
                return period;
            }
        }
    }
}




