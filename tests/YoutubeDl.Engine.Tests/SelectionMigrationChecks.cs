using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Media;

internal static class SelectionMigrationChecks
{
    private static readonly Uri Url=AdaptationRecoveryChecks.Url;
    private static void Check(bool value,string message)=>WriteCleanupChecks.Check(value,message);
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
        new[]{"language","regional","language-order","role","role-order","caption-role","missing-language","missing-role","unknown-scheme","duplicate","limit","invalid-token","null-list"}.Select(mode=>("Selection migration preferences "+mode,(Func<Task>)(()=>Preferences(mode))))
        .Concat(new[]{"best","worst","proactive","same-group","literal","selector","disabled","ambiguous","language","role","codec","dimensions","unknown-bandwidth","within-period","stable-key"}.Select(mode=>("Selection migration quality "+mode,(Func<Task>)(()=>Quality(mode)))))
        .Concat(new[]{"wvtt-stpp","stpp-wvtt","restart","restart-dropped","within-period","wrong-init","unsupported","disabled","role","missing-role"}.Select(mode=>("Selection migration captions "+mode,(Func<Task>)(()=>Captions(mode)))))
        .Concat(new[]{"timeline","captions"}.SelectMany(kind=>new[]{"legacy","upgrade","future","reader","writer","scope","digest","cancel"}.Select(mode=>("Selection migration schema "+kind+" "+mode,(Func<Task>)(()=>Schema(kind,mode))))));

    private static Task Preferences(string mode)
    {
        DashRepresentation Track(string id,string language,string role,string scheme="urn:mpeg:dash:role:2011")=>new(){Id=id,Url=Url,MimeType="audio/mp4",Language=language,Roles=scheme+"="+role};
        var offered=new[]{Track("en","en","main"),Track("fr","fr","main"),Track("en-us","en-US","commentary"),Track("alt","en","commentary")};
        var policy=mode switch
        {
            "language"=>new LiveDashSelectionPolicy{AudioLanguages=["fr"]},
            "regional"=>new(){AudioLanguages=["en-US","en"]},
            "language-order"=>new(){AudioLanguages=["en","fr"]},
            "role" or "unknown-scheme"=>new(){AudioRoles=["commentary"]},
            "role-order"=>new(){AudioRoles=["main","commentary"]},
            "caption-role"=>new(){CaptionRoles=["commentary"]},
            "missing-language"=>new(){AudioLanguages=["de"]},
            "missing-role"=>new(){AudioRoles=["dub"]},
            "duplicate"=>new(){AudioLanguages=["en","EN"]},
            "limit"=>new(){AudioLanguages=Enumerable.Range(0,17).Select(i=>"lang-"+i).ToArray()},
            "invalid-token"=>new(){AudioRoles=["main\ncommentary"]},
            _=>new(){AudioLanguages=null!}
        };
        var invalid=mode is "missing-language" or "missing-role" or "unknown-scheme" or "duplicate" or "limit" or "invalid-token" or "null-list";var failed=false;
        if(mode=="unknown-scheme")offered=[Track("foreign","en","commentary","urn:unrelated")];
        try{var result=policy.Prefer(offered,mode=="caption-role");Check(!invalid,"Invalid preferences succeeded.");Check(mode switch{"language"=>result.Single().Id=="fr","regional"=>result.Single().Id=="en-us","language-order"=>result.All(item=>item.Language=="en"),"role" or "caption-role"=>result.All(item=>item.Roles.EndsWith("=commentary")),_=>result.All(item=>item.Roles.EndsWith("=main"))},"Preference ordering selected the wrong language or role.");}
        catch(Exception error)when(invalid&&error is ArgumentException or NotSupportedException){failed=true;}
        Check(failed==invalid,"Preference refusal changed.");return Task.CompletedTask;
    }
    private static Task Quality(string mode)
    {
        var first=DashManifestParser.Parse(AdaptationRecoveryChecks.Manifest(),Url).Representations[0];
        DashRepresentation Track(string id,int? bandwidth=200000,string? language=null,string? role=null,string? codec=null,int width=128)=>new(){Id=id,Url=Url,MimeType=first.MimeType,Width=width,Height=first.Height,Bandwidth=bandwidth,Codecs=codec??first.Codecs,FramesPerSecond=first.FramesPerSecond,Language=language??first.Language,Roles=role??first.Roles,Channels=first.Channels,SamplingRate=first.SamplingRate,AdaptationIdentity=mode=="same-group"?first.AdaptationIdentity:"new-group"};
        var request=new NativeDownloadRequest{Url=Url,OutputDirectory="unused",Quality=mode=="worst"?QualityPreference.Worst:QualityPreference.Best,FormatId=mode=="literal"?first.Id:null,FormatSelector=mode=="selector"?"bestvideo":"",LiveDashSelection=new(){FollowQuality=mode!="disabled"}};
        var selection=new DashTrackSelection(first,request);var offered=new[]{Track("low",50000),Track("high")};
        if(mode=="proactive")offered=[Track(first.Id,100000),Track("high")];
        if(mode=="same-group")offered=[Track("high"),new(){Id="other",Url=Url,MimeType=first.MimeType,Width=128,Height=72,Codecs=first.Codecs,Language=first.Language,Roles=first.Roles,Channels=first.Channels,SamplingRate=first.SamplingRate,FramesPerSecond=10,Bandwidth=500000,AdaptationIdentity="other"}];
        if(mode=="ambiguous")offered=[Track("a"),Track("b")];
        if(mode is "language" or "role" or "codec" or "dimensions" or "unknown-bandwidth")offered=[Track("bad",mode=="unknown-bandwidth"?null:200000,mode=="language"?"fr":null,mode=="role"?"other":null,mode=="codec"?"hev1":null,mode=="dimensions"?160:128)];
        var invalid=mode is "literal" or "selector" or "disabled" or "ambiguous" or "language" or "role" or "codec" or "dimensions" or "unknown-bandwidth";var failed=false;
        try
        {
            var chosen=selection.Resolve("next",offered);Check(!invalid&&chosen.Id==(mode=="worst"?"low":"high"),"Quality policy selected the wrong successor.");
            if(mode=="within-period"){try{selection.Resolve("next",[Track("high",300000)]);throw new Exception("Pinned profile changed.");}catch(InvalidDataException){}}
            if(mode=="stable-key"){var next=new DashTrackSelection(Track("other",50000),request);Check(next.Key==selection.Key,"Quality intent depended on the initial bitrate or physical ID.");}
        }
        catch(Exception error)when(invalid&&error is InvalidDataException or NotSupportedException){failed=true;}
        Check(failed==invalid,"Quality refusal changed.");return Task.CompletedTask;
    }
    private static async Task Captions(string mode)
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);var resumed=false;var mutate=false;
        try
        {
            var reverse=mode=="stpp-wvtt";var request=new NativeDownloadRequest{Url=Url,OutputDirectory=directory,WriteSubs=true,SubLanguages="en",AdaptiveRecoveryScope=directory,LiveFromStart=true,LiveDurationSeconds=8,LiveDashSelection=new(){ConvertCaptionCodecs=mode!="disabled",CaptionRoles=mode=="missing-role"?["forced"]:[]}};
            string Xml()
            {
                var root=XDocument.Parse(AdaptationRecoveryChecks.Manifest(captions:true,generation:resumed?2:0,dropped:resumed&&mode=="restart-dropped"));
                foreach(var period in root.Root!.Elements())foreach(var rep in period.Descendants("Representation").Where(item=>item.Attribute("codecs")?.Value=="wvtt"))
                {
                    var later=period.Attribute("id")!.Value=="p1";var ttml=reverse?!later:later;rep.SetAttributeValue("codecs",mode=="unsupported"&&later?"unknown":ttml?"stpp":"wvtt");
                    if(mutate&&later)rep.SetAttributeValue("codecs","wvtt");
                    var template=rep.Element("SegmentTemplate")!;template.SetAttributeValue("initialization",template.Attribute("initialization")!.Value.Replace("text-init",ttml?"ttml-init":"vtt-init"));template.SetAttributeValue("media",template.Attribute("media")!.Value.Replace("text-",ttml?"ttml-":"vtt-"));
                    if(mode=="role"&&later)rep.Parent!.Element("Role")!.SetAttributeValue("value","commentary");
                }
                return root.ToString();
            }
            using var http=AdaptiveAccountChecks.Http((message,_)=>
            {
                var name=Path.GetFileName(message.RequestUri!.AbsolutePath);var ttml=name.StartsWith("ttml",StringComparison.Ordinal)&&mode!="wrong-init";var payload=ttml?Encoding.UTF8.GetBytes("<tt xmlns=\"http://www.w3.org/ns/ttml\"><body><div><p begin=\"0s\" end=\"4s\">TTML payload.</p></div></body></tt>"):DashCaptionChecks.Sample("WebVTT payload.");
                var time=name.Contains("init")?0:ulong.Parse(Path.GetFileNameWithoutExtension(name).Split('-')[^1]);var movie=DashCaptionChecks.Movie([payload],out var offset,time*1000,ttml:ttml);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(name.Contains("init")?movie[..offset]:movie[offset..])});
            });
            DashManifest Current()=>DashManifestParser.Parse(Xml(),Url);var media=new HlsSegment(Url,0,null,null,null,8,ProgramDateTime:DateTimeOffset.Parse("2026-01-01T00:00:00Z"));var destination=Path.Combine(directory,"video.mp4");var invalid=mode is "within-period" or "wrong-init" or "unsupported" or "disabled" or "role" or "missing-role";var failed=false;
            try
            {
                using(var capture=new DashLiveCaptionCapture(Current(),[],request,http,new(null),_=>Task.FromResult(Current()),destination))
                {await capture.ObserveAsync(new(media,"unused",null),default);if(mode=="within-period"){mutate=true;await capture.ObserveAsync(new(media,"unused",null),default);}var content=capture.Finish(new(){MediaSegments=[media]}).Single().Content!;Check(content.Contains("TTML payload.")&&content.Contains("WebVTT payload."),"Codec conversion lost one period's text.");}
                if(mode.StartsWith("restart",StringComparison.Ordinal)){resumed=true;using var capture=new DashLiveCaptionCapture(Current(),[],request,http,new(null),_=>Task.FromResult(Current()),destination);await capture.ObserveAsync(new(media,"unused",null),default);var text=capture.Finish(new(){MediaSegments=[media]}).Single().Content!;Check(text.Contains("TTML payload.")&&text.Contains("WebVTT payload."),"Mixed-codec recovery lost a retired caption period.");}
                Check(!invalid,"Invalid codec transition succeeded.");
            }
            catch(Exception error)when(invalid&&error is InvalidDataException or NotSupportedException){failed=true;}
            Check(failed==invalid,"Caption conversion refusal changed: "+mode);
        }
        finally{Directory.Delete(directory,true);}
    }
    private static async Task Schema(string kind,string mode)
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            using var http=new YoutubeDl.Engine.Http.MediaHttp();var destination=Path.Combine(directory,"recording");const string scope="compatible-0.168";var row=LiveCaptionCheckpoint.Row("id","facts","description",[1,2,3]);
            if(kind=="captions"){using var saved=await LiveCaptionCheckpoint.OpenAsync(http,destination,scope,default);await saved.SaveAsync([row],default);}
            else{using var saved=await TimelineCheckpoint.OpenAsync(http,destination,scope,default);await saved.CaptureAsync("id","facts",Url,null,[1,2,3],Path.Combine(directory,"copy"),async(path,ct)=>{await File.WriteAllBytesAsync(path,[1,2,3],ct);return null;},default,"{\"Kind\":\"media\"}");}
            var path=Directory.GetFiles(directory,kind=="captions"?"captions.json":"timeline.json",SearchOption.AllDirectories).Single();var document=JsonNode.Parse(await File.ReadAllTextAsync(path))!;document["Schema"]=1;document.AsObject().Remove("MinimumReaderSchema");document.AsObject().Remove("WriterVersion");
            if(mode=="future")document["Schema"]=99;if(mode is "reader" or "writer"){document["Schema"]=2;document["MinimumReaderSchema"]=mode=="reader"?3:2;document["WriterVersion"]=mode=="writer"?"invalid":"0.172.0";}if(mode=="scope")document["Scope"]=new string('0',64);if(mode=="digest")document[kind=="captions"?"Entries":"Records"]![0]![kind=="captions"?"Digest":"MetadataDigest"]=new string('0',64);
            await File.WriteAllTextAsync(path,document.ToJsonString());var before=await File.ReadAllBytesAsync(path);var invalid=mode is "future" or "reader" or "writer" or "scope" or "digest" or "cancel";var failed=false;using var cancellation=new CancellationTokenSource();if(mode=="cancel")cancellation.Cancel();
            try
            {
                if(kind=="captions"){using var opened=await LiveCaptionCheckpoint.OpenAsync(http,destination,scope,cancellation.Token);var restored=opened.Entries.Single();Check(restored.Identity==row.Identity&&restored.Facts==row.Facts&&restored.Description==row.Description&&restored.Digest==row.Digest&&restored.Bytes.SequenceEqual(row.Bytes),"Legacy captions changed their validated entries.");if(mode=="upgrade")await opened.SaveAsync(opened.Entries,default);}
                else{using var opened=await TimelineCheckpoint.OpenAsync(http,destination,scope,cancellation.Token);await opened.CopySavedAsync(opened.Records.Single(),Path.Combine(directory,"restored"),default);Check((await File.ReadAllBytesAsync(Path.Combine(directory,"restored"))).SequenceEqual(new byte[]{1,2,3}),"Legacy media bytes changed.");if(mode=="upgrade")await opened.CaptureAsync("next","facts",Url,null,[4,5,6],Path.Combine(directory,"next"),async(path,ct)=>{await File.WriteAllBytesAsync(path,[4,5,6],ct);return null;},default,"{}");}
                Check(!invalid,"Unsupported checkpoint was accepted.");
            }
            catch(Exception error)when(invalid&&error is InvalidDataException or OperationCanceledException){failed=true;}
            Check(failed==invalid,"Schema compatibility refusal changed.");if(mode=="upgrade")Check(JsonNode.Parse(await File.ReadAllTextAsync(path))!["Schema"]!.GetValue<int>()==2,"Compatible checkpoint was not upgraded on a successful write.");else{var after=await File.ReadAllBytesAsync(path);Check(before.SequenceEqual(after),"Reading or refusing an older checkpoint rewrote it.");}
        }
        finally{Directory.Delete(directory,true);}
    }
}
