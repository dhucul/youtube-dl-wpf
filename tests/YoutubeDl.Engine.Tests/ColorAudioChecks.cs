using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Media;

internal static class ColorAudioChecks
{
    private static void Check(bool value,string message)=>WriteCleanupChecks.Check(value,message);
    private static LiveDashSelectionPolicy Policy()=>new(){NormalizeTransitions=true,AdaptiveQualityLadder=true,TransitionHeight=90};
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
        new[]{"video","audio","layout","rate","partial-rate","peak","peak-disabled","opus-rate","requires-normalize","legacy-json","all-targets","geometry","color","targets-disabled"}.Select(mode=>("Color audio validation "+mode,(Func<Task>)(()=>Validation(mode))))
        .Concat(new[]{"sdr","geometry","sar","rotate","pq","hlg","bt601","range","unknown","hdr-disabled","advanced","bad-rotation","bad-aspect","complex","pixel","tag-missing","peak","strip"}.Select(mode=>("Color audio video "+mode,(Func<Task>)(()=>Video(mode)))))
        .Concat(new[]{"mono","stereo","surround","resample","unknown","unconfigured","bounds","mismatch"}.Select(mode=>("Color audio layout "+mode,(Func<Task>)(()=>Audio(mode)))))
        .Concat(new[]{"layout","rate","policy","language"}.Select(mode=>("Color audio recovery intent "+mode,(Func<Task>)(()=>Recovery(mode)))));
    private static Task Validation(string mode)
    {
        var policy=Policy();policy=mode switch
        {
            "video"=>policy with{TransitionVideoCodec="mpeg2"},"audio"=>policy with{TransitionAudioCodec="mp3"},"layout"=>policy with{TransitionAudioLayout="unknown",TransitionAudioSampleRate=48000},"rate"=>policy with{TransitionAudioLayout="stereo",TransitionAudioSampleRate=12345},"partial-rate"=>policy with{TransitionAudioSampleRate=48000},"peak"=>policy with{TransitionColorPolicy="hdr-to-bt709"},"peak-disabled"=>policy with{HdrPeakNits=1000},"opus-rate"=>policy with{TransitionAudioCodec="opus",TransitionAudioLayout="stereo",TransitionAudioSampleRate=44100},"requires-normalize"=>new(){TransitionVideoCodec="hevc"},"targets-disabled"=>new(){NormalizeVideoGeometry=true},"color"=>policy with{TransitionColorPolicy="guess"},"all-targets"=>policy with{TransitionVideoCodec="av1",TransitionAudioCodec="opus",TransitionAudioLayout="5.1",TransitionAudioSampleRate=48000,NormalizeVideoGeometry=true,TransitionColorPolicy="hdr-to-bt709",HdrPeakNits=1000},"geometry"=>policy with{NormalizeVideoGeometry=true},_=>new()
        };
        var invalid=mode is not ("legacy-json" or "all-targets" or "geometry");var failed=false;
        try{policy.Validate();Check(!invalid,"Invalid conversion targets succeeded.");if(mode=="legacy-json")Check(JsonSerializer.Serialize(policy)=="{\"AudioLanguages\":[],\"AudioRoles\":[],\"CaptionRoles\":[],\"FollowQuality\":true,\"ConvertCaptionCodecs\":true}","Unused new targets changed compatible policy JSON.");}
        catch(ArgumentException)when(invalid){failed=true;}Check(failed==invalid,"Conversion target validation changed.");return Task.CompletedTask;
    }
    private static Task Video(string mode)
    {
        var first=new TransitionVideoFacts("yuv420p","bt709","bt709","bt709","tv","1:1",0);var facts=first;var policy=Policy();
        if(mode is "geometry" or "sar" or "rotate" or "bad-rotation" or "bad-aspect" or "complex")policy=policy with{NormalizeVideoGeometry=true};
        if(mode is "pq" or "hlg" or "peak" or "strip" or "hdr-disabled")facts=first with{Pixel="yuv420p10le",Transfer=mode=="hlg"?"arib-std-b67":"smpte2084",Primaries="bt2020",Matrix="bt2020nc"};
        if(mode is "pq" or "hlg" or "peak" or "strip")policy=policy with{TransitionColorPolicy="hdr-to-bt709",HdrPeakNits=1000};
        if(mode is "bt601" or "range" or "unknown" or "advanced" or "pixel" or "tag-missing")policy=policy with{TransitionColorPolicy="bt709"};
        if(mode=="bt601")facts=first with{Transfer="smpte170m",Primaries="smpte170m",Matrix="smpte170m"};if(mode=="range")facts=first with{Range="pc"};if(mode=="unknown")facts=first with{Transfer="unknown"};if(mode=="advanced")facts=first with{AdvancedHdr=true};if(mode=="pixel")facts=first with{Pixel="rgb24"};if(mode=="tag-missing")facts=first with{Range=""};if(mode=="sar")facts=first with{Aspect="2:1"};if(mode=="rotate")facts=first with{Rotation=90};if(mode=="bad-rotation")facts=first with{Rotation=45};if(mode=="bad-aspect")facts=first with{Aspect="100:1"};if(mode=="complex")facts=first with{SimpleDisplay=false};
        var invalid=mode is "unknown" or "hdr-disabled" or "advanced" or "bad-rotation" or "bad-aspect" or "complex" or "pixel" or "tag-missing";var failed=false;
        try{var filter=TransitionConversionPlan.VideoFilter(policy,facts,first,160);Check(!invalid&&filter.EndsWith("setsar=1,fps=30"),"Invalid transform succeeded or lost its output cadence/aspect.");if(mode=="sar")Check(filter.Contains("reset_sar=1"),"Aspect conversion did not use display aspect ratio.");if(mode=="peak")Check(filter.Contains("peak=10"),"Nominal HDR peak was not expressed relative to 100 nits.");if(mode=="strip")Check(filter.Contains("MASTERING_DISPLAY_METADATA")&&filter.Contains("CONTENT_LIGHT_LEVEL"),"HDR-to-SDR conversion retained stale static HDR side data.");}
        catch(NotSupportedException)when(invalid){failed=true;}Check(failed==invalid,"Color/geometry refusal changed: "+mode);return Task.CompletedTask;
    }
    private static Task Audio(string mode)
    {
        var first=new TransitionAudioFacts(1,44100,"mono",16);var input=new TransitionAudioFacts(6,48000,"5.1",24);var policy=Policy() with{TransitionAudioLayout=mode=="mono"?"mono":mode=="surround"?"5.1":"stereo",TransitionAudioSampleRate=48000};
        if(mode=="unknown")input=input with{Layout="unknown"};if(mode=="unconfigured")policy=Policy();if(mode=="bounds")input=input with{Rate=999999};if(mode=="mismatch")input=input with{Channels=2};var invalid=mode is "unknown" or "unconfigured" or "bounds" or "mismatch";var failed=false;
        try{var result=TransitionConversionPlan.AudioProfile(policy,input,first);Check(!invalid&&result.Rate==48000&&result.Channels==(mode=="mono"?1:mode=="surround"?6:2),"Audio output profile did not remain fixed across input layout/rate changes.");Check(TransitionConversionPlan.AudioFilter(policy,result).Contains("rematrix_maxval=1"),"Audio remapping did not bound rematrix gain.");}
        catch(NotSupportedException)when(invalid){failed=true;}Check(failed==invalid,"Audio layout refusal changed.");return Task.CompletedTask;
    }
    private static Task Recovery(string mode)
    {
        var url=new Uri("https://fixture.example/a.mpd");DashRepresentation Track(string id,string channels,string rate,string language="en")=>new(){Id=id,Url=url,MimeType="audio/mp4",AdaptationIdentity=id,Codecs="alac",Language=language,Roles="urn:mpeg:dash:role:2011=main",Channels=channels,SamplingRate=rate,Bandwidth=100000};
        var request=new NativeDownloadRequest{Url=url,OutputDirectory="unused",LiveDashSelection=Policy() with{TransitionAudioLayout="stereo",TransitionAudioSampleRate=48000}};var a=Track("a","1","44100");var b=Track("b",mode=="layout"?"6":"2","48000",mode=="language"?"fr":"en");var selection=new DashTrackSelection(a,request);
        if(mode=="language"){try{selection.Resolve("next",[b]);throw new Exception("Audio remapping changed the selected language.");}catch(InvalidDataException){}return Task.CompletedTask;}
        Check(selection.Key==new DashTrackSelection(b,request).Key,"A fixed audio target depended on the first source layout/rate.");if(mode=="policy")Check(selection.Key!=new DashTrackSelection(a,request with{LiveDashSelection=request.LiveDashSelection! with{TransitionAudioSampleRate=44100}}).Key,"Changed remapping intent reused a previous recording key.");else Check(selection.Resolve("next",[b]).Id=="b","An explicit fixed audio profile did not allow the next source layout/rate.");return Task.CompletedTask;
    }
}
