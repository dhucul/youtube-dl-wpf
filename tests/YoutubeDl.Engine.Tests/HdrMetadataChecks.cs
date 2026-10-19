using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Media;

internal static class HdrMetadataChecks
{
    private static void Check(bool value,string message)=>WriteCleanupChecks.Check(value,message);
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
        new[]{"valid","auto-codec","wrong-codec","peak","disabled","identity","dvh1","dvhe"}.Select(mode=>("HDR metadata policy "+mode,(Func<Task>)(()=>Policy(mode))))
        .Concat(new[]{"ten","fifteen","sixty","origin","clip","tail","phase","coverage","fraction"}.Select(mode=>("HDR metadata timeline "+mode,(Func<Task>)(()=>Timeline(mode)))))
        .Concat(new[]{"8.1","8.4","profile","compatibility","enhancement","rpu","base","version","compression"}.Select(mode=>("HDR metadata Dolby configuration "+mode,(Func<Task>)(()=>Dolby(mode)))))
        .Concat(new[]{"static","plus","unknown","duplicate","incomplete","residual","bits","combined"}.Select(mode=>("HDR metadata frame "+mode,(Func<Task>)(()=>Frame(mode)))))
        .Concat(new[]{"static","roundtrip","escape","extended","truncated","trailing","bad-escape","window","version","provider"}.Select(mode=>("HDR metadata HEVC transport "+mode,(Func<Task>)(()=>Transport(mode)))));
    internal static LiveDashSelectionPolicy PolicyValue=>new(){NormalizeTransitions=true,AdaptiveQualityLadder=true,TransitionHeight=90,TransitionVideoCodec="hevc",TransitionColorPolicy="hdr-metadata",NormalizeVideoGeometry=true};
    private static Task Policy(string mode)
    {
        if(mode is "dvh1" or "dvhe"){Check(FormatFacts.CodecMatches(mode+".08.01","h265")&&MergeOutputFormats.CanPredict("mp4",mode+".08.01","aac"),"Dolby HEVC aliases lost explicit codec/container selection.");return Task.CompletedTask;}
        var policy=PolicyValue;policy=mode switch{"auto-codec"=>policy with{TransitionVideoCodec=null},"wrong-codec"=>policy with{TransitionVideoCodec="av1"},"peak"=>policy with{HdrPeakNits=1000},"disabled"=>policy with{NormalizeTransitions=false},_=>policy};
        if(mode=="identity"){var track=new DashRepresentation{Id="a",AdaptationIdentity="video",Url=new("https://fixture.example/video"),MimeType="video/mp4",Codecs="dvh1.08.01",Height=90,Width=160,FramesPerSecond=30};var request=new NativeDownloadRequest{Url=track.Url,OutputDirectory="unused",LiveDashSelection=policy};Check(DashTrackSelection.ConvertibleCodec(track)&&new DashTrackSelection(track,request).Key!=new DashTrackSelection(track,request with{LiveDashSelection=policy with{TransitionColorPolicy="hdr-preserve"}}).Key,"Dolby codec or changed metadata intent lost its recovery identity.");return Task.CompletedTask;}
        try{policy.Validate();Check(mode=="valid","Invalid metadata policy succeeded.");}catch(ArgumentException)when(mode!="valid"){return Task.CompletedTask;}Check(mode=="valid","Expected invalid metadata policy to fail.");return Task.CompletedTask;
    }
    private static Task Timeline(string mode)
    {
        var fps=mode=="fifteen"?15:mode=="sixty"?60:10;var origin=mode=="origin"?8m:0m;var a=new HdrFrameMetadata(new(ContentLight:"1000,400"),null,null);var b=a with{Static=new(ContentLight:"2000,500")};var frames=Enumerable.Range(0,fps*2).Select(i=>new HdrTimedFrame(origin+(decimal)i/fps,1m/fps,i<fps?a:b)).ToArray();var edit=new MediaPeriodEdit(2,mode=="clip"?1:mode=="phase"?0.01m:0,mode is "clip" or "tail"?1:mode=="coverage"?3:mode=="fraction"?1.01m:2);var invalid=mode is "phase" or "coverage" or "fraction";
        try{var result=HdrMetadataTimeline.Project(frames,edit);Check(!invalid&&result.Count==(mode is "clip" or "tail"?30:60),"Metadata retiming changed its output frame count.");Check(result[0].Metadata==(mode=="clip"?b:a)&&result[^1].Metadata==(mode=="tail"?a:b),"Clipping/retiming shifted static profile changes.");if(mode=="ten")Check(ReferenceEquals(result[0],result[2])&&!ReferenceEquals(result[2],result[3]),"Ten-fps source metadata was not duplicated with the selected picture.");}
        catch(NotSupportedException)when(invalid){return Task.CompletedTask;}Check(!invalid,"An unsupported metadata cut succeeded.");return Task.CompletedTask;
    }
    private static Task Dolby(string mode)
    {
        var value=new TransitionDolbyConfiguration(8,mode=="8.4"?4:1,1,1,0,1,0,"none");value=mode switch{"profile"=>value with{Profile=7},"compatibility"=>value with{Compatibility=0},"enhancement"=>value with{Enhancement=1},"rpu"=>value with{Rpu=0},"base"=>value with{Base=0},"version"=>value with{Major=2},"compression"=>value with{Compression="limited"},_=>value};var invalid=mode is not ("8.1" or "8.4");try{value.Validate(mode=="8.4"?"arib-std-b67":"smpte2084");Check(!invalid,"Unsupported Dolby signaling succeeded.");}catch(NotSupportedException)when(invalid){return Task.CompletedTask;}Check(!invalid,"Expected unsupported Dolby configuration to fail.");return Task.CompletedTask;
    }
    private static Task Frame(string mode)
    {
        var frame=new XElement("frame",HdrGamutChecks.Master());if(mode is "plus" or "duplicate" or "combined")frame.Add(new XElement("side_data",new XAttribute("side_data_type","HDR Dynamic Metadata SMPTE2094-40 (HDR10+)")));if(mode=="duplicate")frame.Add(new XElement(frame.Elements("side_data").Last()));if(mode=="unknown")frame.Add(new XElement("side_data",new XAttribute("side_data_type","Unknown HDR family")));
        if(mode is "incomplete" or "residual" or "bits" or "combined"){frame.Add(new XElement("side_data",new XAttribute("side_data_type","Dolby Vision RPU Data")));if(mode!="incomplete")frame.Add(new XElement("side_data",new XAttribute("side_data_type","Dolby Vision Metadata"),new XAttribute("rpu_type",2),new XAttribute("vdr_rpu_profile",1),new XAttribute("bl_bit_depth",mode=="bits"?12:10),new XAttribute("bl_video_full_range_flag",0),new XAttribute("disable_residual_flag",mode=="residual"?0:1),new XAttribute("spatial_resampling_filter_flag",0),new XAttribute("el_spatial_resampling_filter_flag",0),new XAttribute("nlq_method_idc",-1)));}
        var invalid=mode is not ("static" or "plus");try{var value=HdrFrameMetadata.Read(frame);Check(!invalid&&value.Static.MasterDisplay is not null&&(value.Plus is not null)==(mode=="plus"),"HDR frame parsing changed declared metadata.");}catch(NotSupportedException)when(invalid){return Task.CompletedTask;}catch(InvalidDataException)when(invalid){return Task.CompletedTask;}Check(!invalid,"Unsupported frame metadata succeeded.");return Task.CompletedTask;
    }
    private static Task Transport(string mode)
    {
        var invalid=mode is "truncated" or "trailing" or "bad-escape" or "window" or "version" or "provider";
        try
        {
            if(mode is "window" or "version" or "provider"){var data=new byte[]{0xb5,0,0x3c,0,1,4,1,0x40};if(mode=="window")data[7]=0x80;if(mode=="version")data[6]=2;if(mode=="provider")data[2]=0;HevcMetadataTransport.ValidatePlus(data);}
            else if(mode=="static"){var value=TransitionHdrMetadata.Read(new XElement("frame",HdrGamutChecks.Master()));var messages=HevcMetadataTransport.Static(value);Check(messages.Count==1&&messages[0].Type==137&&messages[0].Data.Length==24,"Canonical static metadata was not encoded as a mastering SEI.");}
            else
            {
                var body=mode=="escape"?new byte[]{0,0,0,0,0,1,0,0,2,0,0,3,0x80}:Enumerable.Range(0,600).Select(i=>(byte)i).ToArray();var type=mode=="extended"?128:4;var nal=HevcMetadataTransport.EncodeSei([(type,body)]);if(mode=="truncated")nal=nal[..^3];if(mode=="trailing")nal[^1]=0;if(mode=="bad-escape")nal=[78,1,4,4,0,0,3,5,0x80];var decoded=HevcMetadataTransport.Sei(nal);Check(decoded.Count==1&&decoded[0].Type==type&&decoded[0].Data.SequenceEqual(body),"HEVC metadata transport changed a payload.");
            }
            Check(!invalid,"Malformed metadata transport succeeded.");
        }
        catch(Exception e)when(invalid&&e is InvalidDataException or NotSupportedException){return Task.CompletedTask;}Check(!invalid,"Expected malformed transport to fail.");return Task.CompletedTask;
    }
}

