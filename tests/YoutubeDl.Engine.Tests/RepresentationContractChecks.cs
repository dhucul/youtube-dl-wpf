using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.YouTube;

internal static class RepresentationContractChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
        new[]{"rename","dropped","pinned","ambiguous","group","implicit-group","width","height","codec","bandwidth","language","role","channels","sampling","same-id-role","unknown-codec","exact-preferred","role-order","candidate-facts"}
            .Select(mode=>("Representation contract DASH "+mode,(Func<Task>)(()=>Projection(mode))))
        .Concat(new[]{"unused-missing","unused-changed","unused-expired","unused-caption","active-caption","used-missing","used-changed","restore","audio-language","audio-default","ending-hls","ending-dash","ending-incomplete","ending-open","ending-unobserved","ending-progressive","ending-master","revive","ending-failed","ending-concurrent"}.Select(mode=>("Representation contract session "+mode,(Func<Task>)(()=>Session(mode)))))
        .Concat(new[]{"wistia","jwplatform","brightcove","kaltura"}.Select(provider=>("Representation contract hosting "+provider,(Func<Task>)(()=>Hosting(provider)))))
        .Concat([("Representation contract YouTube observed ending",(Func<Task>)YouTubeEnding)]);
    private static readonly Uri Url=new("https://cdn.example/live.mpd");
    private static void Check(bool condition,string message)=>WriteCleanupChecks.Check(condition,message);
    internal static string Period(string id,int start,string raw="0",string mode="")
    {
        var audio=mode is "channels" or "sampling";var adaptation=new XElement("AdaptationSet",new XAttribute("id",mode=="group"&&id=="p1"?"other":"v"),new XAttribute("mimeType",audio?"audio/mp4":"video/mp4"),new XAttribute("lang",mode=="language"&&id=="p1"?"fr":"en"),new XAttribute("audioSamplingRate",mode=="sampling"&&id=="p1"?44100:48000));
        if(mode=="implicit-group")adaptation.Attribute("id")!.Remove();
        var roles=new[]{new XElement("Role",new XAttribute("schemeIdUri","urn:mpeg:dash:role:2011"),new XAttribute("value",mode is "role" or "same-id-role"&&id=="p1"?"commentary":"main")),new XElement("Role",new XAttribute("schemeIdUri","urn:example:role"),new XAttribute("value","stable"))};
        foreach(var role in mode=="role-order"&&id=="p1"?roles.Reverse():roles)adaptation.Add(role);
        adaptation.Add(new XElement("AudioChannelConfiguration",new XAttribute("schemeIdUri","urn:mpeg:dash:23003:3:audio_channel_configuration:2011"),new XAttribute("value",mode=="channels"&&id=="p1"?6:2)));
        XElement Representation(string value)=>new("Representation",new XAttribute("id",value),new XAttribute("height",mode=="height"&&id=="p1"?90:72),new XAttribute("width",mode=="width"&&id=="p1"?160:128),new XAttribute("bandwidth",mode=="bandwidth"&&id=="p1"?200000:100000),
            mode=="unknown-codec"?null:new XAttribute("codecs",mode=="codec"&&id=="p1"?"avc1.other":audio?"mp4a.40.2":"avc1.42c00a"),
            new XElement("SegmentTemplate",new XAttribute("timescale",1),new XAttribute("duration",2),new XAttribute("initialization",id+"/init.mp4"),new XAttribute("media",id+"/$RepresentationID$-$Number$.m4s")));
        adaptation.Add(Representation(raw));if(mode=="ambiguous"&&id=="p1")adaptation.Add(Representation("alternate"));if(mode=="exact-preferred"&&id=="p1")adaptation.Add(Representation("0"));return new XElement("Period",new XAttribute("id",id),new XAttribute("start","PT"+start+"S"),adaptation).ToString();
    }
    private static Task Projection(string mode)
    {
        using var http=new MediaHttp();var xml=DashContinuityChecks.Mpd(Period("p0",0,mode:mode)+Period("p1",4,mode=="same-id-role"?"0":"renamed",mode));var manifest=DashManifestParser.Parse(xml,Url);var registry=new DashLiveRecorder.PeriodRegistry();registry.Observe(manifest);var projection=new DashLiveRecorder.TrackProjection(http,manifest,manifest.Representations[0],registry);
        var invalid=mode is "ambiguous" or "implicit-group" or "width" or "height" or "codec" or "bandwidth" or "language" or "role" or "channels" or "sampling" or "same-id-role" or "unknown-codec";var failed=false;
        try
        {
            var result=projection.Project(manifest);Check(!invalid&&result.MediaSegments.Count==4,"Invalid representation transition succeeded.");
            if(mode=="pinned")
            {try{projection.Project(DashManifestParser.Parse(xml.Replace("id=\"renamed\"","id=\"later\""),Url));throw new InvalidOperationException("Representation changed within an accepted period.");}catch(InvalidDataException){}}
            if(mode=="dropped")
            {var next=DashManifestParser.Parse(DashContinuityChecks.Mpd(Period("p1",4,"renamed")),Url);registry.Observe(next);Check(projection.Project(next).MediaSegments[0].DiscontinuitySequence==1,"Dropped period lost the pinned successor.");}
            if(mode=="exact-preferred")Check(result.MediaSegments[^1].RecoveryBinding!.Contains("v:0"),"Exact representation did not take precedence.");
            if(mode=="candidate-facts"){var candidate=DashDownloader.ToCandidates(manifest)[0];Check(candidate.Width==128,"DASH width was not offered to selection.");}
        }
        catch(Exception error)when(invalid&&error is InvalidDataException or NotSupportedException){failed=true;}
        Check(failed==invalid,"Representation refusal contract changed: "+mode);return Task.CompletedTask;
    }
    internal static PageMetadata Page(int generation,MediaKind kind=MediaKind.Progressive,bool live=false,bool alternate=true,bool caption=true,int height=72,bool expired=false,string? language=null,bool defaultAudio=false)=>new(){Id="fixture",Extractor="contracts",IsLive=live,
        Media=new[]{new MediaCandidate{Id="main",Url=new("https://cdn.example/"+generation+"/main."+(kind==MediaKind.Hls?"m3u8":kind==MediaKind.Dash?"mpd":"mp4")),Kind=kind,Extension=kind==MediaKind.Hls?"m3u8":kind==MediaKind.Dash?"mpd":"mp4",Height=72,HasVideo=true,HasAudio=true,AudioLanguage=language,IsDefaultAudio=defaultAudio}}
            .Concat(alternate?[new MediaCandidate{Id="alternate",Url=new("https://cdn.example/"+generation+"/alternate.mp4"),Kind=MediaKind.Progressive,Extension="mp4",Height=height,HasVideo=true,HasAudio=true,ResourceExpires=expired?DateTimeOffset.UtcNow.AddSeconds(-1):null}]:[]).ToArray(),
        Subtitles=caption?[new(){Url=new("https://cdn.example/"+generation+"/caption.vtt"),Language="en",Extension="vtt",ResourceIdentity="caption"}]:[]};
    internal static string Manifest(MediaKind kind,int generation,bool ended)=>kind==MediaKind.Hls?LivePeriodSessionChecks.Hls(generation)+(ended?"#EXT-X-ENDLIST\n":""):LivePeriodSessionChecks.Dash(generation,true,ended:ended);
    private static async Task Session(string mode)
    {
        var ending=mode.StartsWith("ending",StringComparison.Ordinal)||mode=="revive";var kind=mode is "ending-dash" or "ending-incomplete"?MediaKind.Dash:ending&&mode!="ending-progressive"?MediaKind.Hls:MediaKind.Progressive;var initial=Page(0,kind,ending);var generation=0;var fail=mode=="ending-failed";
        using var lease=new RenewableMediaAuthorization(initial,_=>
        {
            generation++;if(fail&&generation==1)throw new IOException("Issuer unavailable");var next=Page(generation,kind,ending?mode=="revive"&&generation>1:false,
                alternate:mode is not ("unused-missing" or "used-missing")&&(mode!="restore"||generation>1),caption:mode is not ("unused-caption" or "active-caption"),
                height:mode is "unused-changed" or "used-changed"?90:72,expired:mode=="unused-expired",language:mode=="audio-language"?"fr":null,defaultAudio:mode=="audio-default");return Task.FromResult(next);
        },readManifest:(uri,_)=>Task.FromResult(new MediaDocument(uri,mode=="ending-master"?"#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1000\nvideo.m3u8\n":mode=="ending-incomplete"?Manifest(kind,generation,true).Replace("mediaPresentationDuration=\"PT8S\"","mediaPresentationDuration=\"PT10S\""):Manifest(kind,generation,mode!="ending-open"),null)));
        var main=initial.Media[0].Url;var alternate=initial.Media[1].Url;var caption=initial.Subtitles[0].Url;
        if(ending&&kind!=MediaKind.Progressive&&mode!="ending-unobserved")await lease.ObserveManifestAsync(new(main,mode=="ending-master"?"#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1000\nvideo.m3u8\n":Manifest(kind,0,false),null),default);
        var before=await lease.AuthorizeAsync(main,default);if(mode is "used-missing" or "used-changed")await lease.AuthorizeAsync(alternate,default);if(mode=="active-caption")await lease.AuthorizeAsync(caption,default);
        var invalid=mode is "used-missing" or "used-changed" or "active-caption" or "audio-language" or "audio-default" or "ending-incomplete" or "ending-open" or "ending-unobserved" or "ending-progressive" or "ending-master" or "ending-failed";var rejected=false;
        try
        {
            var next=await lease.RenewAsync(main,before.Generation,default);Check(!invalid,"Invalid session generation accepted.");
            if(mode=="ending-concurrent"){var shared=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>lease.RenewAsync(main,before.Generation,default)));Check(shared.All(value=>value==next)&&generation==1,"Concurrent ending refreshed repeatedly.");}
            if(mode is "unused-missing" or "unused-changed" or "unused-expired" or "unused-caption" or "restore")
            {
                var retired=mode=="unused-caption"?caption:alternate;Check(lease.AppliesTo(retired),"Retired alias escaped authorization handling.");try{await lease.AuthorizeAsync(retired,default);throw new InvalidOperationException("Retired resource stayed usable.");}catch(InvalidDataException){}
                if(mode=="restore"){await lease.RenewAsync(main,next.Generation,default);Check((await lease.AuthorizeAsync(retired,default)).Url.AbsolutePath=="/2/alternate.mp4","Matching unused resource did not restore.");}
            }
            if(ending)Check(!lease.IsLive,"Ended metadata was not committed.");
            if(mode=="revive"){try{await lease.RenewAsync(main,next.Generation,default);throw new InvalidOperationException("An ended session revived.");}catch(InvalidDataException){Check(!lease.IsLive,"Revival changed accepted ending state.");}}
        }
        catch(Exception error)when(invalid&&error is InvalidDataException or IOException){rejected=true;}
        Check(rejected==invalid,"Session refusal contract changed: "+mode);if(invalid)Check((await lease.AuthorizeAsync(main,default))==before&&lease.IsLive==ending,"Rejected generation replaced usable state.");
    }
    private static async Task Hosting(string provider)
    {
        var generation=0;using var owner=AdaptiveAccountChecks.Http((_,_)=>throw new InvalidOperationException("Unexpected network request"));using var session=new ProviderPlaybackSession(owner,new(){Url=new("https://publisher.example/watch"),OutputDirectory="."},provider,(_,_)=>Task.FromResult(Page(generation++ ,alternate:generation==1)));
        var page=await session.ReadAsync(default);var lease=page.PlaybackAuthorization!;var first=await lease.AuthorizeAsync(page.Media[0].Url,default);var next=await lease.RenewAsync(page.Media[0].Url,first.Generation,default);Check(next.Generation>first.Generation,"Hosting format churn stopped selected playback.");
        try{await lease.AuthorizeAsync(page.Media[1].Url,default);throw new InvalidOperationException("Missing hosting alternative bypassed retirement.");}catch(InvalidDataException){}
    }
    private static async Task YouTubeEnding()
    {
        var initial=Page(0,MediaKind.Hls,true,false,false);using var session=new YouTubeVideoSession(_=>Task.FromResult(Page(1,MediaKind.Hls,false,false,false)),(uri,_)=>Task.FromResult(new MediaDocument(uri,Manifest(MediaKind.Hls,1,true),null)));
        var page=session.Bind(initial,true);var lease=page.PlaybackAuthorization!;await lease.ObserveManifestAsync(new(initial.Media[0].Url,Manifest(MediaKind.Hls,0,false),null),default);var first=await lease.AuthorizeAsync(initial.Media[0].Url,default);var ended=await lease.RenewAsync(initial.Media[0].Url,first.Generation,default);Check(ended.Generation>first.Generation,"YouTube media helper rejected a proven event ending.");
    }
}


