using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

internal static class WpfLadderChecks
{
    private static readonly Uri Url=new("https://ladder.example/live.mpd");
    private static void Check(bool value,string message)=>WriteCleanupChecks.Check(value,message);
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
        new[]{"height","odd","ceiling","ladder","ignored-height","tokens","legacy-json","default"}.Select(mode=>("WPF ladders validation "+mode,(Func<Task>)(()=>Validation(mode))))
        .Concat(new[]{"best","worst","height","fps","codec","prefer-free","decoder","disabled","dimensions","language","role","channels","sampling","viewpoint","accessibility","unknown","implicit","ambiguous","pinned","stable"}.Select(mode=>("WPF ladders selection "+mode,(Func<Task>)(()=>Selection(mode)))))
        .Concat(new[]{"keys","bindings","bounds","private"}.Select(mode=>("WPF ladders projection "+mode,(Func<Task>)(()=>Projection(mode)))));
    private static NativeDownloadRequest Request()=>new(){Url=Url,OutputDirectory="unused",LiveDashSelection=new(){NormalizeTransitions=true,AdaptiveQualityLadder=true,TransitionHeight=90}};
    private static DashRepresentation Track(string id="first",string? group="video",int height=720,int bandwidth=100000,string codec="avc1.42c01e",double fps=30,string language="en",string roles="urn:mpeg:dash:role:2011=main",string channels="",string sampling="",string viewpoint="",string access="")=>new(){Id=id,AdaptationIdentity=group,Url=Url,MimeType="video/mp4",Codecs=codec,Width=height*16/9,Height=height,Bandwidth=bandwidth,FramesPerSecond=fps,Language=language,Roles=roles,Channels=channels,SamplingRate=sampling,Viewpoints=viewpoint,Accessibility=access,Initialization=new("https://ladder.example/init.mp4"),Segments=[new(new("https://ladder.example/segment.m4s"),null,0,2){SourcePresentationStartSeconds=0,SourceDurationSeconds=2,SourceSlot="0"}]};
    private static Task Validation(string mode)
    {
        var policy=mode switch
        {
            "height"=>new LiveDashSelectionPolicy{NormalizeTransitions=true},
            "odd"=>new(){NormalizeTransitions=true,TransitionHeight=721},
            "ceiling"=>new(){NormalizeTransitions=true,TransitionHeight=2162},
            "ladder"=>new(){AdaptiveQualityLadder=true},
            "ignored-height"=>new(){TransitionHeight=720},
            "tokens"=>new(){AudioRoles=["main","MAIN"]},
            _=>new()
        };
        var invalid=mode is not ("legacy-json" or "default");var failed=false;
        try{policy.Validate();Check(!invalid,"Invalid transition settings succeeded.");if(mode=="legacy-json"){var json=JsonSerializer.Serialize(policy);Check(json=="{\"AudioLanguages\":[],\"AudioRoles\":[],\"CaptionRoles\":[],\"FollowQuality\":true,\"ConvertCaptionCodecs\":true}","Default extensions changed the compatible 0.170.0 policy serialization.");}}
        catch(ArgumentException)when(invalid){failed=true;}Check(failed==invalid,"Transition validation changed.");return Task.CompletedTask;
    }
    private static Task Selection(string mode)
    {
        var first=Track();var request=Request();if(mode=="worst")request=request with {Quality=QualityPreference.Worst};if(mode=="height")request=request with {MaximumHeight=720};if(mode=="fps")request=request with {MaximumFrameRate=30};if(mode=="codec")request=request with {VideoCodec="h264"};if(mode=="prefer-free")request=request with {PreferFreeFormats=true};
        if(mode=="disabled")request=request with {LiveDashSelection=new()};if(mode is "decoder" or "dimensions")request=request with {LiveDashSelection=new(){NormalizeTransitions=true,TransitionHeight=90}};
        var offered=new[]{Track("low","next",360,50000),Track("high","next",1080,200000,"hev1.1.6.L93",60)};
        if(mode=="prefer-free")offered=[Track("free","next",1080,100000,"vp09.00.10.08"),Track("high","next",1080,200000)];
        if(mode=="decoder")offered=[Track("decoder","next",720,100000,"av01.0.05M.08")];
        if(mode=="dimensions")offered=[Track("changed","next",1080)];
        if(mode=="language")offered=[Track("bad","next",language:"fr")];if(mode=="role")offered=[Track("bad","next",roles:"urn:mpeg:dash:role:2011=commentary")];if(mode=="channels")offered=[Track("bad","next",channels:"6")];if(mode=="sampling")offered=[Track("bad","next",sampling:"44100")];if(mode=="viewpoint")offered=[Track("bad","next",viewpoint:"other")];if(mode=="accessibility")offered=[Track("bad","next",access:"other")];if(mode=="unknown")offered=[Track("bad","next",codec:"unknown")];if(mode=="implicit")offered=[Track("bad",null)];if(mode=="ambiguous")offered=[Track("a","next"),Track("b","next")];
        var invalid=mode is "disabled" or "dimensions" or "language" or "role" or "channels" or "sampling" or "viewpoint" or "accessibility" or "unknown" or "implicit" or "ambiguous";var failed=false;var selection=new DashTrackSelection(first,request);
        try
        {
            var result=selection.Resolve("p1",offered);Check(!invalid&&result.Id==(mode is "height" or "fps" or "codec" or "worst"?"low":mode=="prefer-free"?"free":mode=="decoder"?"decoder":"high"),"Adaptive ladder selected the wrong representation.");
            if(mode=="pinned"){try{selection.Resolve("p1",[Track("high","next",720,200000,"hev1.1.6.L93",60)]);throw new Exception("Pinned period changed dimensions.");}catch(InvalidDataException){}}
            if(mode=="stable")Check(new DashTrackSelection(result,request).Key==selection.Key,"Converted selection depended on the initial decoder/dimensions.");
        }
        catch(Exception error)when(invalid&&error is InvalidDataException or NotSupportedException){failed=true;}Check(failed==invalid,"Adaptive ladder refusal changed: "+mode);return Task.CompletedTask;
    }
    private static Task Projection(string mode)
    {
        using var http=new MediaHttp();var request=Request();var origin=DateTimeOffset.Parse("2026-01-01T00:00:00Z");DashManifest Manifest(DashRepresentation representation)=>new(){SourceUrl=Url,IsDynamic=true,AvailabilityStartTime=origin,Representations=[representation],Periods=[new("p",0,2,[representation])]};
        var a=Manifest(Track());var b=Manifest(Track(height:360,codec:"vp09.00.10.08"));HlsSegment Project(DashManifest value,NativeDownloadRequest options){var periods=new DashLiveRecorder.PeriodRegistry();periods.Observe(value);return new DashLiveRecorder.TrackProjection(http,value,value.Representations[0],periods,options).Project(value).MediaSegments.Single();}
        var first=Project(a,request);var next=Project(b,request);Check(first.RecoveryIdentity==next.RecoveryIdentity&&first.RecoveryResourceIdentity==next.RecoveryResourceIdentity,"Converted recovery identity depended on the initial rung.");
        if(mode=="bindings")Check(first.RecoveryBinding!=next.RecoveryBinding,"Converted recovery lost the actual accepted decoder/profile binding.");
        if(mode=="bounds")Check(Project(a,request with {MaximumHeight=1080}).RecoveryIdentity!=first.RecoveryIdentity,"Changed ladder constraints reused the prior recovery intent.");
        if(mode=="private")Check(!JsonSerializer.Serialize(new[]{first.RecoveryIdentity,first.RecoveryBinding,first.RecoveryResourceIdentity}).Contains("https://"),"Converted checkpoint descriptors included delivery addresses.");return Task.CompletedTask;
    }
}
