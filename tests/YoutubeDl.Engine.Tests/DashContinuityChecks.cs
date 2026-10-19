using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

internal static class DashContinuityChecks
{
    private static readonly Uri Url=new("https://live.example/manifest.mpd");
    public static IEnumerable<(string Name,Func<Task> Test)> All=>
    [
        ("DASH continuity explicit periods retain presentation clocks and repeated representation IDs",()=>Sync(Periods)),
        ("DASH continuity implicit starts and next-period ends resolve consistently",()=>Sync(Implicit)),
        ("DASH continuity sliding availability removes expired periods and future media",()=>Sync(Availability)),
        ("DASH continuity announced periods may expose an empty pending timeline",()=>Sync(Pending)),
        ("DASH continuity gaps overlaps duplicate identities and unknown starts fail",()=>Sync(BadPeriods)),
        ("DASH continuity fixed Number and Time templates retain shifted media origins",()=>Sync(Shifted)),
        ("DASH continuity explicit segment lists map inherited timeline and byte ranges",()=>Sync(List)),
        ("DASH continuity fixed-duration segment lists preserve startNumber and offset",()=>Sync(FixedList)),
        ("DASH continuity mismatched list timeline counts and index modes fail",()=>Sync(BadLists)),
        ("DASH continuity large clocks repetitions and period limits are bounded",()=>Sync(Limits)),
        ("DASH continuity inherited low latency and boundary clipping remain explicit",()=>Sync(Unsupported)),
        ("DASH continuity projections retain period boundaries when old periods disappear",()=>Sync(Projection)),
        ("DASH continuity period timing mutation and reused IDs fail",()=>Sync(Mutation)),
        ("DASH continuity missing representations and changed codecs fail",()=>Sync(TrackMutation)),
        ("DASH continuity initialization may change between compatible periods only",()=>Sync(Initialization)),
        ("DASH continuity ended snapshots preserve the availability origin and periods",()=>Sync(Ended)),
        ("DASH continuity from-start requires the true presentation beginning",()=>Sync(FromStart))
    ];
    private static Task Sync(Action action){action();return Task.CompletedTask;}
    private static void Assert(bool value){if(!value)throw new InvalidOperationException("DASH continuity expectation failed.");}
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is InvalidDataException or NotSupportedException){return;}throw new InvalidOperationException("Invalid DASH continuity was accepted.");}
    internal static string Period(string id,int start,string? addressing=null)=>$"<Period id=\"{id}\" start=\"PT{start}S\"><AdaptationSet id=\"v\" mimeType=\"video/mp4\" codecs=\"avc1.42c00a\"><Representation id=\"0\" height=\"72\">{addressing??$"<SegmentTemplate timescale=\"1\" initialization=\"{id}/init.mp4\" media=\"{id}/$Time$.m4s\"><SegmentTimeline><S t=\"0\" d=\"2\" r=\"-1\"/></SegmentTimeline></SegmentTemplate>"}</Representation></AdaptationSet></Period>";
    internal static string Mpd(string periods,int published=8,int depth=30)=>$"<MPD type=\"dynamic\" availabilityStartTime=\"2026-01-01T00:00:00Z\" publishTime=\"2026-01-01T00:00:{published:00}Z\" timeShiftBufferDepth=\"PT{depth}S\">{periods}</MPD>";
    private static DashManifest Parse(string text)=>DashManifestParser.Parse(text,Url);
    private static string Two=>Mpd(Period("p0",0)+Period("p1",4));
    private static void Periods(){var p=Parse(Two);Assert(p.Periods.Count==2&&p.Periods[0].DurationSeconds==4&&p.Periods[1].Representations[0].Segments[0].PresentationStartSeconds==4&&p.Periods.All(x=>x.Representations[0].Id=="v:0"));}
    private static void Implicit(){var p=Parse(Two.Replace("start=\"PT0S\"","duration=\"PT4S\"").Replace("start=\"PT4S\"",""));Assert(p.Periods[1].StartSeconds==4);}
    private static void Availability(){var p=Parse(Mpd(Period("p0",0)+Period("p1",4)+Period("future",12),8,2));Assert(p.Representations[0].Segments.Count==1&&p.Representations[0].Segments[0].PresentationStartSeconds==6&&p.Periods[0].Representations[0].Segments.Count==0&&p.Periods[2].Representations[0].Segments.Count==0);}
    private static void Pending(){var p=Parse(Mpd(Period("p0",0)+Period("pending",8).Replace("<S t=\"0\" d=\"2\" r=\"-1\"/>","")));Assert(p.Periods[1].Representations[0].Segments.Count==0&&p.Representations[0].Segments.Count==4);}
    private static void BadPeriods(){foreach(var x in new[]{Two.Replace("start=\"PT0S\"","start=\"PT0S\" duration=\"PT3S\""),Two.Replace("start=\"PT4S\"","start=\"PT0S\""),Two.Replace("id=\"p1\"","id=\"p0\""),Two.Replace("start=\"PT4S\"",""),Two.Replace("start=\"PT0S\"","start=\"-PT1S\"")})Reject(()=>Parse(x));}
    private static void Shifted(){foreach(var variable in new[]{"$Number%03d$","$Time$"}){var a=$"<SegmentTemplate timescale=\"1\" presentationTimeOffset=\"100\" startNumber=\"9\" duration=\"2\" initialization=\"init.mp4\" media=\"{variable}.m4s\"/>";var p=Parse(Mpd(Period("p",4,a),8));var s=p.Representations[0].Segments;Assert(s.Count==2&&s[0].PresentationStartSeconds==4&&s[0].Number==9&&s[0].Url.AbsolutePath==(variable.Contains("Number")?"/009.m4s":"/100.m4s"));}}
    private const string TimedList="<SegmentList timescale=\"10\" presentationTimeOffset=\"100\"><Initialization sourceURL=\"all.mp4\" range=\"0-99\"/><SegmentTimeline><S t=\"100\" d=\"20\" r=\"1\"/></SegmentTimeline><SegmentURL media=\"all.mp4\" mediaRange=\"100-199\"/><SegmentURL media=\"all.mp4\" mediaRange=\"200-299\"/></SegmentList>";
    private static void List(){var x=Period("p",0,TimedList);x=x.Replace(TimedList,"").Replace("<Representation",TimedList+"<Representation");var r=Parse(Mpd(x,4)).Representations[0];Assert(r.InitializationRange==new HlsByteRange(0,100)&&r.Segments[1].Range==new HlsByteRange(200,100)&&r.Segments[1].PresentationStartSeconds==2);}
    private static void FixedList(){var a=TimedList.Replace("<SegmentTimeline><S t=\"100\" d=\"20\" r=\"1\"/></SegmentTimeline>","").Replace("timescale=\"10\"","timescale=\"10\" duration=\"20\" startNumber=\"20\"");var s=Parse(Mpd(Period("p",0,a),4)).Representations[0].Segments;Assert(s.Count==2&&s[1].Number==21&&s[1].PresentationStartSeconds==2);}
    private static void BadLists(){foreach(var a in new[]{TimedList.Replace("r=\"1\"","r=\"2\""),TimedList.Replace("r=\"1\"","r=\"0\""),TimedList.Replace("mediaRange=\"100-199\"","indexRange=\"100-199\""),TimedList.Replace("range=\"0-99\"","range=\"99-0\""),TimedList+"<SegmentTemplate/>"})Reject(()=>Parse(Mpd(Period("p",0,a))));}
    private static void Limits(){Reject(()=>Parse(Mpd(Period("p",0).Replace("d=\"2\" r=\"-1\"","d=\"9223372036854775807\" r=\"2\""))));Reject(()=>Parse(Mpd(string.Concat(Enumerable.Range(0,1001).Select(i=>Period("p"+i,i*2))))));}
    private static void Unsupported(){foreach(var x in new[]{Two.Replace("<AdaptationSet","<BaseURL availabilityTimeOffset=\"1\">https://live.example/</BaseURL><AdaptationSet"),Two.Replace("timescale=\"1\"","timescale=\"1\" eptDelta=\"1\""),Two.Replace("<SegmentTemplate","<SegmentBase/><SegmentTemplate")})Reject(()=>Parse(x));}
    private static void Projection(){using var http=new MediaHttp();var initial=Parse(Mpd(Period("p0",0),4));var registry=new DashLiveRecorder.PeriodRegistry();registry.Observe(initial);var project=new DashLiveRecorder.TrackProjection(http,initial,initial.Representations[0],registry);var a=project.Project(initial);var next=Parse(Two);registry.Observe(next);var b=project.Project(next);var dropped=Parse(Mpd(Period("p1",4)));registry.Observe(dropped);var c=project.Project(dropped);Assert(a.MediaSegments.Count==2&&b.MediaSegments.Count==4&&c.MediaSegments[0].Sequence==2&&c.MediaSegments[0].DiscontinuitySequence==1);}
    private static void Mutation(){var r=new DashLiveRecorder.PeriodRegistry();r.Observe(Parse(Two));Reject(()=>r.Observe(Parse(Two.Replace("start=\"PT4S\"","start=\"PT6S\""))));Reject(()=>r.Observe(Parse(Mpd(Period("replacement",2)))));}
    private static void TrackMutation(){using var http=new MediaHttp();foreach(var change in new[]{Two.Replace("id=\"0\" height", "id=\"missing\" height"),Two.Replace("avc1.42c00a","avc1.64000a")}){var p=Parse(Two);var r=new DashLiveRecorder.PeriodRegistry();r.Observe(p);var project=new DashLiveRecorder.TrackProjection(http,p,p.Representations[0],r);project.Project(p);Reject(()=>project.Project(Parse(change)));}}
    private static void Initialization(){using var http=new MediaHttp();var p=Parse(Two);var r=new DashLiveRecorder.PeriodRegistry();r.Observe(p);var project=new DashLiveRecorder.TrackProjection(http,p,p.Representations[0],r);Assert(project.Project(p).MediaSegments[0].Initialization!.Url!=project.Project(p).MediaSegments[^1].Initialization!.Url);Reject(()=>project.Project(Parse(Two.Replace("p1/init.mp4","p1/changed.mp4"))));}
    private static void Ended(){var p=DashManifestParser.Parse(Two.Replace("type=\"dynamic\"","type=\"static\" mediaPresentationDuration=\"PT8S\""),Url,true);Assert(!p.IsDynamic&&p.Periods[1].DurationSeconds==4);}
    private static void FromStart(){using var http=new MediaHttp();var p=Parse(Mpd(Period("p",4)));var r=new DashLiveRecorder.PeriodRegistry();r.Observe(p);var track=new DashLiveRecorder.TrackProjection(http,p,p.Representations[0],r).Project(p);Assert(track.PlaylistType is null&&track.MediaSequence!=0);}
}
