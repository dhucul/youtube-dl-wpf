using System.Text.Json;
using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class LivePeriodSessionChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
        new[]{"closing","dropped","ended","raw-duration","offset","range","codec","origin","start","closed-end","backwards-publish","reused-period","canceled","staged","stale-stage","shared-recovery","time-sliding","number-reset"}
            .Select(mode=>("Live period/session graph "+mode,(Func<Task>)(()=>Graph(mode))))
        .Concat(new[]{"hls","dash","progressive","same-url","collision","failed","canceled","disposed","queued","bound"}.Select(mode=>("Live period/session lease "+mode,(Func<Task>)(()=>Lease(mode)))))
        .Concat(new[]{"wistia","jwplatform","brightcove","kaltura"}.Select(provider=>("Live period/session hosting "+provider,(Func<Task>)(()=>Hosting(provider)))));
    private static MediaDocument Document(Uri url,string text)=>new(url,text,null);
    private static void Check(bool value,string message)=>WriteCleanupChecks.Check(value,message);
    internal static PageMetadata Page(int generation,MediaKind kind=MediaKind.Dash,bool caption=false,bool collision=false,bool same=false)=>new(){Id="live-fixture",Extractor="fixture",IsLive=true,
        Media=[new(){Id="main",Url=new("https://cdn.example/"+(same||collision?0:generation)+(collision?"/captions.vtt":"/live."+(kind==MediaKind.Dash?"mpd":kind==MediaKind.Hls?"m3u8":"mp4"))),Kind=kind,Extension=kind==MediaKind.Dash?"mpd":kind==MediaKind.Hls?"m3u8":"mp4",HasVideo=true,HasAudio=true}],
        Subtitles=caption?[new(){Url=new("https://cdn.example/0/captions.vtt"),Language="en",ResourceIdentity="caption",Extension="vtt"}]:[]};
    internal static string Hls(int generation)=>"#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:1\n#EXT-X-PROGRAM-DATE-TIME:2026-01-01T00:00:00Z\n#EXTINF:2,\nhttps://cdn.example/"+generation+"/segment.ts\n";
    internal static string Dash(int generation,bool closed=false,bool dropped=false,bool ended=false,string mutation="")
    {
        var root=new XElement("MPD",new XAttribute("type",ended?"static":"dynamic"),new XAttribute("availabilityStartTime",mutation=="origin"?"2026-01-02T00:00:00Z":"2026-01-01T00:00:00Z"),new XAttribute("publishTime",mutation=="backwards-publish"?"2026-01-01T00:00:03Z":"2026-01-01T00:00:08Z"),new XAttribute("timeShiftBufferDepth","PT30S"),new XAttribute("minimumUpdatePeriod","PT0.25S"));
        if(ended)root.SetAttributeValue("mediaPresentationDuration","PT8S");
        if(!dropped)root.Add(Period("p0",mutation=="start"?1:0,0));if(closed||dropped)root.Add(Period(mutation=="reused-period"?"reused":"p1",mutation=="closed-end"?4:3,mutation=="offset"?4:3));return root.ToString();
        XElement Period(string id,int start,int offset)
        {
            var period=new XElement("Period",new XAttribute("id",id),new XAttribute("start","PT"+start+"S"));var adaptation=new XElement("AdaptationSet",new XAttribute("id","v"),new XAttribute("mimeType","video/mp4"));
            var representation=new XElement("Representation",new XAttribute("id","0"),new XAttribute("codecs",mutation=="codec"?"avc1.changed":"avc1.42c00a"),new XAttribute("height",72));
            var template=new XElement("SegmentTemplate",new XAttribute("timescale",1),new XAttribute("presentationTimeOffset",offset),new XAttribute("initialization","https://cdn.example/"+generation+"/"+id+"/init.mp4"),new XAttribute("media","https://cdn.example/"+generation+"/"+id+"/$Time$.m4s"));
            if(mutation=="range")
            {
                template.Name="SegmentList";template.Attribute("initialization")!.Remove();template.Attribute("media")!.Remove();template.Add(new XElement("Initialization",new XAttribute("sourceURL","https://cdn.example/"+generation+"/"+id+"/init.mp4")));for(var index=0;index<4;index++)template.Add(new XElement("SegmentURL",new XAttribute("media","https://cdn.example/"+generation+"/"+id+"/"+(index*2)+".m4s"),new XAttribute("mediaRange","0-99")));
            }
            template.Add(new XElement("SegmentTimeline",new XElement("S",new XAttribute("t",0),new XAttribute("d",mutation=="raw-duration"?3:2),new XAttribute("r",3))));representation.Add(template);adaptation.Add(representation);period.Add(adaptation);return period;
        }
    }
    private static async Task Graph(string mode)
    {
        var root=Page(0).Media[0].Url;var graph=new AdaptivePlaybackGraph();var closed=mode is "closed-end" or "reused-period" or "offset";
        string Snapshot(int generation)=>mode is "time-sliding" or "number-reset"?Dash(generation).Replace("<S t=\"0\"","<S t=\""+(generation==0?"0":"2")+"\"").Replace("$Time$",mode=="number-reset"?"$Number$":"$Time$"):Dash(generation,generation>0||closed,mutation:generation>0?mode:"");
        graph.Observe("format:main",MediaKind.Dash,root,root,Snapshot(0));
        var segment=new Uri("https://cdn.example/0/p0/2.m4s");var original=graph.Resolve(segment);using var cancellation=new CancellationTokenSource();var reject=mode is "raw-duration" or "offset" or "range" or "codec" or "origin" or "start" or "closed-end" or "backwards-publish" or "reused-period" or "canceled" or "stale-stage" or "number-reset";
        var recovery=graph.RecoveryIdentity(segment);
        var failed=false;
        try
        {
            var commit=await graph.PrepareRefreshAsync(new Dictionary<string,Uri>{{"format:main",Page(1).Media[0].Url}},(uri,_)=>Task.FromResult(Document(uri,mode=="shared-recovery"?Dash(1,true).Replace("/p1/","/p0/"):Snapshot(1))),cancellation.Token);
            Check(graph.Resolve(segment)==original,"Preparing renewal changed an active graph.");
            if(mode=="canceled")cancellation.Cancel();if(mode=="stale-stage")graph.Observe("format:main",MediaKind.Dash,root,root,Dash(0));
            if(mode=="staged")return;commit();Check(!reject,"Invalid DASH renewal was committed.");
            Check(graph.Resolve(segment).Url.AbsolutePath=="/1/p0/2.m4s"&&graph.Canonicalize(graph.Resolve(segment).Url)==segment,"Closing period lost its canonical full source.");
            Check(graph.RecoveryIdentity(segment)==recovery,"Closing or shared periods changed a saved resource binding.");
            if(mode is "dropped" or "ended")
            {
                var finish=await graph.PrepareRefreshAsync(new Dictionary<string,Uri>{{"format:main",Page(2).Media[0].Url}},(uri,_)=>Task.FromResult(Document(uri,Dash(2,true,true,mode=="ended"))),default);finish();
                try{graph.Resolve(segment);throw new InvalidOperationException("Retired period stayed renewable.");}catch(InvalidDataException){}
            }
        }
        catch(Exception e)when(reject&&e is InvalidDataException or NotSupportedException or OperationCanceledException){failed=true;}
        Check(failed==reject,"Graph failure contract changed: "+mode);if(reject)Check(graph.Resolve(segment)==original,"Rejected renewal changed usable fragment mappings.");
    }
    private static async Task Lease(string mode)
    {
        var kind=mode=="hls"?MediaKind.Hls:mode is "progressive" or "same-url" or "bound"?MediaKind.Progressive:MediaKind.Dash;var generation=0;var collide=mode=="collision";var same=mode is "same-url" or "bound";var fail=mode=="failed";var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var initial=Page(0,kind,collide,same:same);using var stop=new CancellationTokenSource();var lease=new RenewableMediaAuthorization(initial,async ct=>
        {generation++;if(mode is "disposed" or "queued" or "canceled"){entered.TrySetResult();await release.Task;}ct.ThrowIfCancellationRequested();if(fail&&generation==1)throw new IOException("Issuer outage");return Page(generation,kind,collide,collide&&generation==1,same);},readManifest:(url,_)=>Task.FromResult(Document(url,kind==MediaKind.Hls?Hls(generation):Dash(generation,true))));
        try
        {
            var url=initial.Media[0].Url;if(kind!=MediaKind.Progressive)await lease.ObserveManifestAsync(Document(url,kind==MediaKind.Hls?Hls(0):Dash(0)),default);
            var segment=new Uri(kind==MediaKind.Hls?"https://cdn.example/0/segment.ts":"https://cdn.example/0/p0/2.m4s");var old=kind==MediaKind.Progressive?await lease.AuthorizeAsync(url,default):await lease.AuthorizeAsync(segment,default);
            if(mode is "disposed" or "queued" or "canceled")
            {
                var pending=lease.RenewAsync(url,old.Generation,stop.Token);await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));var queued=mode=="queued"?lease.AuthorizeAsync(url,default):null;
                if(mode=="canceled")stop.Cancel();else lease.Dispose();release.TrySetResult();await Canceled(pending);if(queued is not null)await Canceled(queued);
                if(mode=="canceled")Check((await lease.AuthorizeAsync(segment,default))==old,"Canceled renewal changed the surviving graph.");return;
            }
            if(mode is "collision" or "failed")
            {
                try{await lease.RenewAsync(url,old.Generation,default);throw new InvalidOperationException("Invalid issuer generation accepted.");}catch(Exception e)when(e is InvalidDataException or IOException){}
                Check((await lease.AuthorizeAsync(segment,default))==old,"Rejected root URL installed its adaptive mappings.");
            }
            var count=mode=="bound"?256:24;var current=await lease.AuthorizeAsync(url,default);
            for(var index=0;index<count;index++)
            {
                var next=await lease.RenewAsync(url,current.Generation,default);var shared=await Task.WhenAll(Enumerable.Range(0,6).Select(_=>lease.RenewAsync(url,current.Generation,default)));Check(shared.All(value=>value==next),"Concurrent rejected generation refreshed repeatedly.");current=next;
            }
            if(mode=="bound"){try{await lease.RenewAsync(url,current.Generation,default);throw new InvalidOperationException("Playback renewal bound was ignored.");}catch(NotSupportedException){Check(generation==256,"Refresh bound contacted the issuer again.");}}
            if(kind!=MediaKind.Progressive)Check(lease.Canonicalize((await lease.AuthorizeAsync(segment,default)).Url)==segment,"Long session changed canonical media identity.");
        }
        finally{release.TrySetResult();lease.Dispose();}
    }
    private static async Task Canceled(Task task){try{await task.WaitAsync(TimeSpan.FromSeconds(3));throw new InvalidOperationException("Retired operation completed.");}catch(Exception e)when(e is OperationCanceledException or ObjectDisposedException){}}
    private static async Task Hosting(string provider)
    {
        var reads=0;using var http=AdaptiveAccountChecks.Http((_,_)=>throw new InvalidOperationException("Issuer fixture performed HTTP."));var request=new NativeDownloadRequest{Url=new("https://publisher.example/live"),OutputDirectory="."};
        using var session=new ProviderPlaybackSession(http,request,provider,(_,_)=>Task.FromResult(Page(Interlocked.Increment(ref reads),MediaKind.Progressive)));
        var initial=await session.ReadAsync(default);var lease=initial.PlaybackAuthorization!;var canonical=initial.Media[0].Url;var current=await lease.AuthorizeAsync(canonical,default);
        for(var index=0;index<24;index++){var next=await lease.RenewAsync(canonical,current.Generation,default);var shared=await Task.WhenAll(Enumerable.Range(0,6).Select(_=>lease.RenewAsync(canonical,current.Generation,default)));Check(shared.All(item=>item==next),"Hosting session did not share renewal.");current=next;}
        Check(reads==25&&(await session.ReadAsync(default)).Media[0].Url==canonical&&lease.Canonicalize(current.Url)==canonical,"Long hosting session changed registered identity.");
    }
}



