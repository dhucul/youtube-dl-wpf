using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> RepresentationContractMediaChecks(string ffmpeg)=>
        new[]{"literal-selector","literal-id","pair","video","audio","clip","late-closure","dropped","within-period","ambiguous","language","role","width","decoder","cancel","used-missing","unused-changed","ending-open","wistia","jwplatform","brightcove","kaltura"}
            .Select(mode=>("Representation contract media "+mode,(Func<Task>)(()=>RepresentationMedia(ffmpeg,mode))));
    private static async Task RepresentationMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var assets=Path.Combine(files.Directory,"fixtures");Directory.CreateDirectory(assets);var mpd=Path.Combine(assets,"source.mpd");
        async Task Generate(string destination,int width)=>await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size="+width+"x72:rate=10","-f","lavfi","-i","sine=frequency=590:sample_rate=48000","-t","4","-c:v","libx264","-preset","ultrafast","-g","20","-bf","0","-c:a","aac","-f","dash","-dash_segment_type","mp4","-seg_duration","2","-init_seg_name","init-$RepresentationID$.m4s","-media_seg_name","chunk-$RepresentationID$-$Number%05d$.m4s",destination]);
        await Generate(mpd,128);var xml=XDocument.Parse(await File.ReadAllTextAsync(mpd));
        if(mode=="decoder"){var changed=Path.Combine(assets,"changed");Directory.CreateDirectory(changed);await Generate(Path.Combine(changed,"source.mpd"),160);}
        var generation=0;var polls=0;var callbacks=0;var media=0;var rejected=false;var provider=mode is "wistia" or "jwplatform" or "brightcove" or "kaltura"?mode:"jwplatform";var initial=RepresentationContractChecks.Page(0,MediaKind.Dash,true);
        var invalid=mode is "late-closure" or "within-period" or "ambiguous" or "language" or "role" or "width" or "decoder" or "cancel" or "used-missing" or "ending-open";
        int Stage()=>mode=="within-period"?(polls==1?0:polls<5?1:2):generation<=1?0:2;
        MediaHttp Http()=>AdaptiveAccountChecks.Http(async(message,ct)=>
        {
            if(message.RequestUri!.AbsolutePath.EndsWith("main.mpd"))
            {
                polls++;if(generation==1&&polls>1&&!rejected){rejected=true;return AdaptiveAccountChecks.Text("Expired",HttpStatusCode.Forbidden);}
                return AdaptiveAccountChecks.Text(Snapshot(Stage()));
            }
            media++;if(mode=="cancel"&&message.RequestUri.AbsolutePath.Contains("/p1/"))await Task.Delay(Timeout.Infinite,ct);
            var directory=mode=="decoder"&&message.RequestUri.AbsolutePath.Contains("/p1/")?Path.Combine(assets,"changed"):assets;
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(directory,Path.GetFileName(message.RequestUri.AbsolutePath)),ct))};
        });
        using var controlled=Http();using var session=new ProviderPlaybackSession(controlled,new(){Url=new("https://issuer.example/"+Path.GetFileName(files.Directory)+"/watch"),OutputDirectory=files.Directory},provider,(_,_)=>
        {
            var next=generation++==0?initial:RepresentationContractChecks.Page(generation-1,MediaKind.Dash,mode=="within-period",alternate:mode=="unused-changed",caption:false,height:mode=="unused-changed"?90:72);
            if(mode=="used-missing"&&generation>1)next=next with {Media=[]};return Task.FromResult(next);
        });
        var page=await session.ReadAsync(default);var lease=page.PlaybackAuthorization!;
        if(mode=="used-missing")await lease.AuthorizeAsync(initial.Media[1].Url,default);
        var engine=new NativeEngine([new AdaptiveNoOverwriteChecks.Provider(mode is "literal-selector" or "literal-id"?page:page with {Media=[page.Media[0]]})],_=>Http());var output=Path.Combine(files.Directory,"out");Directory.CreateDirectory(output);var prior=Path.Combine(output,mode=="audio"?"recording.m4a":"recording.mp4");var archive=Path.Combine(files.Directory,"archive.txt");await File.WriteAllTextAsync(prior,"previous media");await File.WriteAllTextAsync(archive,"previous archive\n");
        using var stop=new CancellationTokenSource();if(mode=="cancel")stop.CancelAfter(700);
        var request=new NativeDownloadRequest{Url=new("https://issuer.example/"+Path.GetFileName(files.Directory)+"/watch"),OutputDirectory=output,FileName="recording",FfmpegPath=ffmpeg,FormatSelector=mode=="literal-selector"?"main":"",FormatId=mode=="literal-id"?"main":null,KeepOriginal=true,AddMetadata=true,LiveFromStart=true,LiveDurationSeconds=mode is "clip" or "late-closure"?7:8,VideoOnly=mode=="video",AudioOnly=mode=="audio",Retries=0,FragmentRetries=0,DownloadArchiveFile=archive};var failed=false;
        try
        {
            var result=await engine.DownloadAsync(request,null,null,stop.Token,_=>callbacks++);WriteCleanupChecks.Check(!invalid&&result.Succeeded&&generation==2&&rejected&&callbacks==result.OutputPaths.Count,"Native representation/session transition lost completion.");
            foreach(var path in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",path,"-map","0:v?","-map","0:a?","-f","null","-"],true);
            using var probe=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-count_frames","-show_entries","stream=codec_type,nb_read_frames:format=duration","-of","json",result.OutputPath!]));var streams=probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            WriteCleanupChecks.Check(streams.Length==(mode is "video" or "audio"?1:2),"Renamed periods changed the selected track roles.");
            if(mode!="audio")
            {
                var expected=FrameHashes(await Run(ffmpeg,["-v","error","-i",mpd,"-map","0:v:0","-fps_mode","passthrough","-f","framemd5","-"]));if(mode=="clip")expected=expected.Take(30).Concat(expected).ToArray();else expected=expected.Concat(expected).ToArray();
                var actual=FrameHashes(await Run(ffmpeg,["-v","error","-i",result.OutputPath!,"-map","0:v:0","-fps_mode","passthrough","-f","framemd5","-"]));WriteCleanupChecks.Check(expected.Length==(mode=="clip"?70:80)&&expected.SequenceEqual(actual),"Representation transition changed/dropped/repeated decoded video frames.");
            }
            if(mode!="within-period")WriteCleanupChecks.Check(!((YoutubeDl.Engine.Http.RenewableMediaAuthorization)lease).IsLive,"Complete adaptive graph did not accept issuer ending metadata.");
            var hashes=result.OutputPaths.ToDictionary(path=>path,path=>SHA256.HashData(File.ReadAllBytes(path)));var before=media;var repeat=await engine.DownloadAsync(request with {FfmpegPath=Path.Combine(files.Directory,"unused.exe")},null,null,default,_=>callbacks++);WriteCleanupChecks.Check(repeat.Skipped&&media==before&&callbacks==result.OutputPaths.Count,"Representation transition archive repeated media or callbacks.");foreach(var (path,hash) in hashes)WriteCleanupChecks.Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),"Archive repeat changed media.");
        }
        catch(Exception error)when(invalid&&error is IOException or InvalidDataException or NotSupportedException or OperationCanceledException){failed=true;}
        WriteCleanupChecks.Check(failed==invalid,"Native representation refusal contract changed: "+mode);if(invalid)WriteCleanupChecks.Check(await File.ReadAllTextAsync(prior)=="previous media"&&await File.ReadAllTextAsync(archive)=="previous archive\n"&&callbacks==0,"Failed transition changed media/archive/callback state.");
        string Snapshot(int stage)
        {
            var complete=stage>=2&&mode!="ending-open";var root=new XElement("MPD",new XAttribute("type",complete?"static":"dynamic"),new XAttribute("availabilityStartTime","2026-01-01T00:00:00Z"),new XAttribute("publishTime",stage==0?(mode=="clip"?"2026-01-01T00:00:03.100Z":"2026-01-01T00:00:04.100Z"):stage==1?"2026-01-01T00:00:06.100Z":"2026-01-01T00:00:08.100Z"),new XAttribute("minimumUpdatePeriod","PT0.25S"),new XAttribute("timeShiftBufferDepth","PT30S"));if(complete)root.SetAttributeValue("mediaPresentationDuration",mode is "clip" or "late-closure"?"PT7S":"PT8S");if(!(mode=="dropped"&&stage>0))root.Add(Period("p0",0));if(stage>0)root.Add(Period("p1",mode is "clip" or "late-closure"?3:4));return root.ToString();
            XElement Period(string id,int start)
            {
                var period=new XElement("Period",new XAttribute("id",id),new XAttribute("start","PT"+start+"S"));if(id=="p0"&&mode is not ("clip" or "late-closure"))period.SetAttributeValue("duration","PT4S");
                foreach(var offered in xml.Descendants().Where(element=>element.Name.LocalName=="AdaptationSet"))
                {
                    var adaptation=new XElement(offered);var audio=(string?)adaptation.Attribute("contentType")=="audio";adaptation.SetAttributeValue("id",audio?"a":"v");adaptation.SetAttributeValue("lang",mode=="language"&&id=="p1"?"fr":"en");adaptation.Add(new XElement("Role",new XAttribute("schemeIdUri","urn:mpeg:dash:role:2011"),new XAttribute("value",mode=="role"&&id=="p1"?"commentary":"main")));
                    foreach(var representation in adaptation.Elements().Where(element=>element.Name.LocalName=="Representation"))
                    {
                        var originalId=(string)representation.Attribute("id")!;if(!audio)representation.SetAttributeValue("width",mode=="width"&&id=="p1"?160:128);
                        if(id=="p1")representation.SetAttributeValue("id",mode=="within-period"&&stage>=2?"changed-again":audio?"renamed-a":"renamed-v");
                        foreach(var template in representation.Descendants().Where(element=>element.Name.LocalName=="SegmentTemplate")){template.SetAttributeValue("initialization","https://cdn.example/"+(generation-1)+"/"+id+"/init-"+originalId+".m4s");template.SetAttributeValue("media","https://cdn.example/"+(generation-1)+"/"+id+"/chunk-"+originalId+"-$Number%05d$.m4s");}
                    }
                    if(mode=="ambiguous"&&id=="p1"){var duplicate=new XElement(adaptation.Elements().First(element=>element.Name.LocalName=="Representation"));duplicate.SetAttributeValue("id","ambiguous");adaptation.Add(duplicate);}
                    period.Add(adaptation);
                }
                return period;
            }
        }
    }
}






