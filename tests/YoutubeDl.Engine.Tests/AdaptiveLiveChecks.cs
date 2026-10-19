using System.Net;
using System.Text.Json;
using System.Xml;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.PeerTube;

internal static class AdaptiveLiveChecks
{
    internal static readonly Uri MpdUrl=new("https://live.example/manifest.mpd");
    internal static string Mpd(string type="dynamic",string times="<S t=\"0\" d=\"2\" r=\"3\"/>",string videoId="v",string audioId="a")=>$"""
        <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="{type}" availabilityStartTime="2026-01-01T00:00:00Z" publishTime="2026-01-01T00:00:08Z" minimumUpdatePeriod="PT1S" timeShiftBufferDepth="PT8S">
        <Period id="event" start="PT0S"><AdaptationSet id="video" mimeType="video/mp4"><SegmentTemplate timescale="1" initialization="$RepresentationID$-init.mp4" media="$RepresentationID$-$Time$.m4s"><SegmentTimeline>{times}</SegmentTimeline></SegmentTemplate><Representation id="{videoId}" bandwidth="100000" codecs="avc1.42C01E" height="72"/></AdaptationSet>
        <AdaptationSet id="audio" mimeType="audio/mp4"><SegmentTemplate timescale="1" initialization="$RepresentationID$-init.mp4" media="$RepresentationID$-$Time$.m4s"><SegmentTimeline>{times}</SegmentTimeline></SegmentTemplate><Representation id="{audioId}" bandwidth="64000" codecs="mp4a.40.2"/></AdaptationSet></Period></MPD>
        """;
    public static IEnumerable<(string Name,Func<Task> Test)> All=>
    [
        ("Adaptive live HLS program clocks extrapolate within each timeline period",()=>Sync(HlsClock)),
        ("Adaptive live shared windows align different sequence counters by program time",()=>Sync(Window)),
        ("Adaptive live incompatible or missing program clocks fail before capture",()=>Sync(BadWindow)),
        ("Adaptive live DASH timelines retain exact addressing and complete availability",()=>Sync(DashTimeline)),
        ("Adaptive live DASH negative repeats remain bounded to complete segments",()=>Sync(DashRepeat)),
        ("Adaptive live DASH Number templates track their availability window",()=>Sync(DashNumber)),
        ("Adaptive live DASH representation identities include their adaptation scope",()=>Sync(DashIdentity)),
        ("Adaptive live DASH protection multi-period partial and DTD modes fail explicitly",()=>Sync(DashBoundaries)),
        ("Adaptive live private resource rejection renews once and removes token URLs",()=>Renew(false,false)),
        ("Adaptive live concurrent private rejection uses one shared renewal",()=>Renew(true,false)),
        ("Adaptive live private repeated rejection remains bounded",()=>Renew(false,true)),
        ("Adaptive live private expiry renews before the next resource request",Expiry),
        ("Adaptive live private redirect and foreign token scopes fail without leaks",TokenBoundaries),
        ("Adaptive live private cancellation prevents token and resource acquisition",TokenCancellation)
    ];
    private static Task Sync(Action a){a();return Task.CompletedTask;}
    private static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void HlsClock()
    {var p=HlsPlaylistParser.Parse("#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXTINF:2,\nzero.ts\n#EXT-X-PROGRAM-DATE-TIME:2026-01-01T00:00:02Z\n#EXTINF:2,\none.ts\n#EXTINF:2,\ntwo.ts",new("https://live.example/live.m3u8"));Assert(p.MediaSegments[0].ProgramDateTime==DateTimeOffset.Parse("2026-01-01T00:00:00Z")&&p.MediaSegments[2].ProgramDateTime==DateTimeOffset.Parse("2026-01-01T00:00:04Z"),"Program clocks were not extrapolated from the first anchor.");}
    private static HlsPlaylist Playlist(int sequence,int start)=>HlsPlaylistParser.Parse("#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:"+sequence+"\n#EXT-X-PROGRAM-DATE-TIME:2026-01-01T00:00:"+start.ToString("00")+"Z\n#EXTINF:2,\none.ts\n#EXTINF:2,\ntwo.ts",new("https://live.example/"+sequence+".m3u8"));
    private static void Window(){var p=LiveWindow.Shared(Playlist(100,0),Playlist(700,1),new(){Url=MpdUrl,OutputDirectory=".",LiveDurationSeconds=2});Assert(p.Start.Second==1&&p.End!.Value.Second==3,"Track sequence counters were mistaken for shared clocks.");}
    private static void BadWindow(){foreach(var audio in new[]{Playlist(500,20),HlsPlaylistParser.Parse("#EXTM3U\n#EXTINF:2,\none.ts",MpdUrl)}){try{LiveWindow.Shared(Playlist(0,0),audio,new(){Url=MpdUrl,OutputDirectory="."});throw new InvalidOperationException("Unrelated timelines accepted.");}catch(Exception e)when(e is NotSupportedException or InvalidDataException){}}}
    private static void DashTimeline(){var p=DashManifestParser.Parse(Mpd(),MpdUrl);var v=p.Representations[0];Assert(p.IsDynamic&&v.Segments.Count==4&&v.Segments[^1].Url.AbsolutePath=="/v-6.m4s"&&v.Segments[^1].PresentationStartSeconds==6,"DASH addressing/timeline changed.");}
    private static void DashRepeat(){var p=DashManifestParser.Parse(Mpd(times:"<S t=\"0\" d=\"3\" r=\"-1\"/>"),MpdUrl);Assert(p.Representations[0].Segments.Count==2&&p.Representations[0].Segments[^1].PresentationStartSeconds==3,"Negative repetition included an incomplete future segment.");}
    private static void DashNumber(){var xml=Mpd().Replace("timeShiftBufferDepth=\"PT8S\"","timeShiftBufferDepth=\"PT4S\"").Replace("media=\"$RepresentationID$-$Time$.m4s\"","duration=\"2\" media=\"$RepresentationID$-$Number%03d$.m4s\"");xml=System.Text.RegularExpressions.Regex.Replace(xml,"<SegmentTimeline>.*?</SegmentTimeline>","");var p=DashManifestParser.Parse(xml,MpdUrl);Assert(p.Representations[0].Segments.Count==2&&p.Representations[0].Segments[0].Url.AbsolutePath=="/v-003.m4s","Number/window addressing changed.");}
    private static void DashIdentity(){var p=DashManifestParser.Parse(Mpd(videoId:"0",audioId:"0"),MpdUrl);Assert(p.Representations[0].Id!=p.Representations[1].Id,"Representation identities collided across adaptations.");}
    private static void DashBoundaries(){foreach(var xml in new[]{Mpd().Replace("<Period id=", "<Period/><Period id="),Mpd().Replace("<Period id=","<ContentProtection/><Period id="),Mpd().Replace("timescale=\"1\"","availabilityTimeComplete=\"false\" timescale=\"1\""),"<!DOCTYPE MPD [<!ENTITY x 'value'>]>"+Mpd(),Mpd().Replace("availabilityStartTime=\"2026-01-01T00:00:00Z\"","")}){try{DashManifestParser.Parse(xml,MpdUrl);throw new InvalidOperationException("Unsupported live mode accepted.");}catch(Exception e)when(e is NotSupportedException or InvalidDataException or XmlException){}}}
    private static PageMetadata Page()=>new(){Id=AuthenticationBatchChecks.Id,Media=[new(){Url=new("https://account.example/static/video.mp4"),Kind=MediaKind.Progressive,HasVideo=true}]};
    private static async Task Renew(bool concurrent,bool deny)
    {
        var tokens=0;var reads=0;var rejected=0;var barrier=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);using var owner=AuthenticationBatchChecks.Http(async(message,ct)=>
        {
            if(message.Method==HttpMethod.Post){var n=Interlocked.Increment(ref tokens);return AuthenticationBatchChecks.Json(JsonSerializer.Serialize(new{files=new{token="t"+n,expires=DateTimeOffset.UtcNow.AddMinutes(2)}}));}
            if(concurrent&&message.RequestUri!.Query=="?videoFileToken=t1"){if(Interlocked.Increment(ref rejected)==8)barrier.TrySetResult();await barrier.Task.WaitAsync(ct);};Interlocked.Increment(ref reads);var ok=!deny&&message.RequestUri!.Query=="?videoFileToken=t2";return AuthenticationBatchChecks.Json("private-reflected-value",ok?HttpStatusCode.OK:HttpStatusCode.Forbidden);
        });
        var session=owner.SessionState("test",()=>new PeerTubeSession(owner,new("https://account.example/"),new(){VideoPassword="fixture-value"}));
        var authorization=await session.PlaybackAuthorizationAsync(Page(),default);using var media=owner.WithAuthorization(authorization);
        async Task Send(){using var request=new HttpRequestMessage(HttpMethod.Get,Page().Media[0].Url);using var response=await media.SendAsync(request,default);Assert(request.RequestUri!.Query==""&&(int)response.StatusCode==(deny?403:200),"Bounded token renewal/canonical URL changed.");}
        if(concurrent)await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Send()));else await Send();
        Assert(tokens==2&&reads<=(concurrent?16:2),"Rejected generation caused duplicate or unbounded refresh.");
    }
    private static async Task Expiry()
    {
        var tokens=0;using var owner=AuthenticationBatchChecks.Http((_,_)=>Task.FromResult(AuthenticationBatchChecks.Json(JsonSerializer.Serialize(new{files=new{token="t"+(++tokens),expires=DateTimeOffset.UtcNow.AddMilliseconds(tokens==1?100:60000)}}))));
        var session=new PeerTubeSession(owner,new("https://account.example/"),new());using(session){var auth=await session.PlaybackAuthorizationAsync(Page(),default);await Task.Delay(150);var lease=await auth.AuthorizeAsync(Page().Media[0].Url,default);Assert(tokens==2&&lease.Url.Query=="?videoFileToken=t2","Expired file token was reused.");}
    }
    private static async Task TokenBoundaries()
    {
        using var owner=AuthenticationBatchChecks.Http((message,_)=>{if(message.Method==HttpMethod.Post)return Task.FromResult(AuthenticationBatchChecks.Json(JsonSerializer.Serialize(new{files=new{token="secret",expires=DateTimeOffset.UtcNow.AddMinutes(2)}})));var r=AuthenticationBatchChecks.Json("secret",HttpStatusCode.Redirect);r.Headers.Location=new("https://foreign.example/");return Task.FromResult(r);});
        var session=owner.SessionState("test",()=>new PeerTubeSession(owner,new("https://account.example/"),new()));var auth=await session.PlaybackAuthorizationAsync(Page(),default);
        Assert(!auth.AppliesTo(new("https://foreign.example/static/video.mp4")),"Token scope crossed origins.");
        try{await auth.AuthorizeAsync(new("https://account.example/static/video.mp4?videoFileToken=foreign"),default);throw new InvalidOperationException("Foreign token was replaced.");}catch(InvalidDataException){}
        using var media=owner.WithAuthorization(auth);try{using var response=await media.SendAsync(new(HttpMethod.Get,Page().Media[0].Url),default);throw new InvalidOperationException("Private redirect followed.");}catch(InvalidDataException e){Assert(!e.Message.Contains("secret"),"Token leaked in diagnostics.");}
    }
    private static async Task TokenCancellation()
    {
        using var cts=new CancellationTokenSource();cts.Cancel();using var owner=AuthenticationBatchChecks.Http((_,_)=>throw new InvalidOperationException("Cancelled token request reached HTTP."));using var session=new PeerTubeSession(owner,new("https://account.example/"),new());
        try{await session.PlaybackAuthorizationAsync(Page(),cts.Token);throw new InvalidOperationException("Token cancellation ignored.");}catch(OperationCanceledException){}
    }
}
