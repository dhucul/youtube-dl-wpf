using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Media;

internal static class LiveCaptionClippingChecks
{
    private static readonly Uri Url=new("https://live.example/manifest.mpd");
    public static IEnumerable<(string Name,Func<Task> Test)> All=>
    [
        ("Live captions and clipping partial DASH periods retain complete overlapping sources",()=>Sync(Clip)),
        ("Live captions and clipping availability waits for the complete boundary resource",()=>Sync(Availability)),
        ("Live captions and clipping tracks retain closures observed by another worker",()=>Sync(SharedClosure)),
        ("Live captions and clipping cue windows end at the common playback boundary",()=>Sync(CueWindow)),
        ("Live captions and clipping rounded touching windows do not retain an extra segment",()=>Sync(Touching)),
        ("Live captions and clipping shared windows trim whole-segment preroll consistently",()=>Sync(WindowStart)),
        ("Live captions and clipping shifted captions inherit addressing and language",()=>Sync(Captions)),
        ("Live captions and clipping subtitle codecs remain explicit and bounded",()=>Sync(Unsupported)),
        ("Live captions and clipping repeated cue text survives period clock resets",()=>Sync(Clocks)),
        ("Live captions and clipping negative period offsets clip the recorded beginning",()=>Sync(Offset)),
        ("Live captions and clipping HLS live windows capture and deduplicate selected text",()=>Hls("valid")),
        ("Live captions and clipping unselected HLS tracks issue no requests",()=>Hls("unselected")),
        ("Live captions and clipping missing HLS clocks reject before resource reads",()=>Hls("clock")),
        ("Live captions and clipping dropped HLS captions never publish a partial document",()=>Hls("gap")),
        ("Live captions and clipping cancellation stops caption acquisition",()=>Hls("cancel")),
        ("Live captions and clipping changed HLS segment identities fail",()=>Hls("changed"))
        ,("Live captions and clipping selected tracks share one bounded byte budget",Budget)
    ];
    private static Task Sync(Action action){action();return Task.CompletedTask;}
    private static void Assert(bool value){if(!value)throw new InvalidOperationException("Live caption/clipping expectation failed.");}
    private static string Period(string id,int start,int offset)=>DashContinuityChecks.Period(id,start,$"<SegmentTemplate timescale=\"1\" presentationTimeOffset=\"{offset}\" initialization=\"init.mp4\" media=\"$Time$.m4s\"><SegmentTimeline><S t=\"0\" d=\"2\" r=\"3\"/></SegmentTimeline></SegmentTemplate>");
    private static void Clip(){var p=DashManifestParser.Parse(DashContinuityChecks.Mpd(Period("p0",0,0)+Period("p1",3,3)),Url);var a=p.Periods[0].Representations[0].Segments;var b=p.Periods[1].Representations[0].Segments;Assert(a.Count==2&&a[^1].DurationSeconds==1&&a[^1].TrimEndSeconds==1&&b[0].PresentationStartSeconds==3&&b[0].DurationSeconds==1&&b[0].TrimStartSeconds==1&&b[0].Url==a[^1].Url);}
    private static void Availability(){var p=DashManifestParser.Parse(DashContinuityChecks.Mpd(Period("p0",0,0)+Period("p1",3,3),3),Url);Assert(p.Periods[0].Representations[0].Segments.Count==1&&p.Periods[1].Representations[0].Segments.Count==0);}
    private static void SharedClosure(){using var http=new YoutubeDl.Engine.Http.MediaHttp();var initial=DashManifestParser.Parse(DashContinuityChecks.Mpd(Period("p0",0,0),4),Url);var registry=new DashLiveRecorder.PeriodRegistry();registry.Observe(initial);var projection=new DashLiveRecorder.TrackProjection(http,initial,initial.Representations[0],registry);projection.Project(initial);registry.Observe(DashManifestParser.Parse(DashContinuityChecks.Mpd(Period("p0",0,0)+Period("p1",3,3),6),Url));var dropped=DashManifestParser.Parse(DashContinuityChecks.Mpd(Period("p1",3,3)).Replace("type=\"dynamic\"","type=\"static\" mediaPresentationDuration=\"PT8S\""),Url,true);registry.Observe(dropped);var revised=projection.Project(dropped);Assert(revised.MediaSegments[0].Sequence==1&&revised.MediaSegments[0].DurationSeconds==1&&revised.MediaSegments[0].TrimEndSeconds==1&&revised.MediaSegments[1].ProgramDateTime==DateTimeOffset.Parse("2026-01-01T00:00:03Z"));}
    private static void CueWindow(){var text="WEBVTT\n\n00:00:00.000 --> 00:00:05.000\nBounded\n\n";var result=HlsWebVttReader.Join([new(text,0,5,ExplicitOffsetSeconds:0,ClipStartSeconds:1,ClipEndSeconds:3)],0,5);Assert(result.Contains("00:00:01.000 --> 00:00:03.000"));}
    private static void Touching(){var origin=DateTimeOffset.Parse("2026-01-01T00:00:00Z");var a=new HlsSegment(Url,100,null,null,null,2.0000001,ProgramDateTime:origin);var b=new HlsSegment(Url,101,null,null,null,2,ProgramDateTime:origin.AddSeconds(2));Assert(HlsLiveRecorder.InitialSequence(new(){MediaSegments=[a,b]},new(origin.AddSeconds(2),origin.AddSeconds(4)),false)==101);}
    private static void WindowStart(){var origin=DateTimeOffset.Parse("2026-01-01T00:00:00Z");var source=new HlsSegment(Url,100,null,null,new(Url,null,null),2,ProgramDateTime:origin.AddSeconds(0.01));var clipped=HlsLiveRecorder.ClipWindowStart(source,new(origin.AddSeconds(2),origin.AddSeconds(4)));Assert(clipped.ProgramDateTime==origin.AddSeconds(2)&&clipped.TrimStartSeconds==1.99m&&Math.Abs(clipped.DurationSeconds!.Value-0.01)<0.000001);}
    private static string CaptionXml(string codec="wvtt")=>DashContinuityChecks.Mpd(Period("p",0,0).Replace("</Period>",$"<AdaptationSet id=\"c\" contentType=\"text\" lang=\"en\"><SegmentTemplate timescale=\"1000\" presentationTimeOffset=\"2000\" initialization=\"ci.mp4\" media=\"c-$Time$.m4s\"><SegmentTimeline><S t=\"2000\" d=\"1000\" r=\"7\"/></SegmentTimeline></SegmentTemplate><Representation id=\"c\" mimeType=\"application/mp4\" codecs=\"{codec}\"/></AdaptationSet></Period>"));
    private static void Captions(){foreach(var codec in new[]{"wvtt","stpp","stpp.ttml.im1t"}){var p=DashManifestParser.Parse(CaptionXml(codec),Url);Assert(p.Representations.Count==1&&p.CaptionTracks.Count==1&&p.CaptionTracks[0].Language=="en"&&p.CaptionTracks[0].OffsetSeconds==2&&p.CaptionTracks[0].Representation.Segments[0].PresentationStartSeconds==0);}}
    private static void Unsupported(){var p=DashManifestParser.Parse(CaptionXml("unsupported"),Url);Assert(p.CaptionTracks.Single().UnavailableReason is not null);}
    private static void Clocks(){var text="WEBVTT\n\ncue\n00:00:00.000 --> 00:00:02.000\nSame cue\n\n";var v=HlsWebVttReader.Join([new(text,0,2,ExplicitOffsetSeconds:0),new(text,2,2,ExplicitOffsetSeconds:2)],0,4);Assert(v.Contains("00:00:00.000 --> 00:00:04.000")&&v.Split("Same cue").Length==2);}
    private static void Offset(){var text="WEBVTT\n\n00:00:01.000 --> 00:00:04.000\nClipped\n\n";var v=HlsWebVttReader.Join([new(text,0,4,ExplicitOffsetSeconds:-2)],0,1.5m);Assert(v.Contains("00:00:00.000 --> 00:00:01.500"));}
    private static async Task Hls(string mode)
    {
        var reads=0;var polls=0;var track=new SubtitleTrack{Url=new("https://live.example/c.m3u8"),Language="en"};
        using var http=AuthenticationBatchChecks.Http((message,_)=>
        {
            if(message.RequestUri!.AbsolutePath=="/c.m3u8")
            {
                polls++;var playlist="#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:700\n#EXT-X-PROGRAM-DATE-TIME:2026-01-01T00:00:"+(mode=="gap"?"03":"00")+"Z\n#EXTINF:2,\nc1.vtt\n#EXTINF:2,\n"+(mode=="changed"&&polls>1?"changed.vtt":"c2.vtt")+"\n#EXT-X-ENDLIST";
                return Task.FromResult(AuthenticationBatchChecks.Json(playlist));
            }
            reads++;return Task.FromResult(AuthenticationBatchChecks.Json("WEBVTT\nX-TIMESTAMP-MAP=LOCAL:00:00:00.000,MPEGTS:900000\n\ncue\n00:00:00.500 --> 00:00:03.000\nNative live\n\n"));
        });
        var request=new NativeDownloadRequest{Url=Url,OutputDirectory=".",WriteSubs=true};using var capture=new HlsLiveCaptionCapture(http,[track],mode=="unselected"?[]:[(track,track.Url)],request,new(null));
        var playlist=HlsPlaylistParser.Parse("#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-PROGRAM-DATE-TIME:2026-01-01T00:00:00Z\n#EXTINF:2,\nv1.ts\n#EXTINF:2,\nv2.ts\n#EXT-X-ENDLIST",Url);
        using var cancellation=new CancellationTokenSource();if(mode=="cancel")cancellation.Cancel();
        try
        {
            var first=playlist.MediaSegments[0];if(mode=="clock")first=first with {ProgramDateTime=null};await capture.ObserveAsync(new(first,"unused",null),cancellation.Token);
            await capture.ObserveAsync(new(playlist.MediaSegments[1],"unused",null),cancellation.Token);
            if(mode=="changed")await capture.ObserveAsync(new(playlist.MediaSegments[1] with {DurationSeconds=3},"unused",null),default);
            if(mode is "clock" or "gap" or "cancel" or "changed")throw new InvalidOperationException("Invalid live captions accepted.");
            var result=capture.Finish(playlist,[10]);Assert(mode=="unselected"?reads==0&&polls==0:reads==2&&result.Single().Content!.Contains("00:00:00.500 --> 00:00:03.000"));
        }
        catch(Exception e)when(mode is "clock" or "gap" or "cancel" or "changed"&&e is NotSupportedException or InvalidDataException or OperationCanceledException)
        {if(mode is "clock" or "cancel")Assert(reads==0&&polls==0);}
    }
    private static async Task Budget(){using var http=AuthenticationBatchChecks.Http((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[8])}));var shared=new LiveCaptionBudget(12);var request=new NativeDownloadRequest{Url=Url,OutputDirectory="."};using var a=new LiveCaptionResources(http,request,new(null),shared);using var b=new LiveCaptionResources(http,request,new(null),shared);await a.LoadAsync(Url,null,null,0,default);try{await b.LoadAsync(Url,null,null,0,default);throw new InvalidOperationException("Aggregate caption budget was ignored.");}catch(InvalidDataException){Assert(shared.Remaining==4);}}
}
