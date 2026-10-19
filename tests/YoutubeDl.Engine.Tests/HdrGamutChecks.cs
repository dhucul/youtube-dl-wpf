using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Media;

internal static class HdrGamutChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
        new[]{"pq","hlg","full","12bit","rgb709","rgb-p3","p3-yuv","2020-yuv","2020-output","unknown-range","rgb-limited","rgb-matrix","alpha","float","pq-wide","mixed-hdr","hdr-8bit","hdr-709","dynamic","no-intent"}.Select(mode=>("HDR gamut policy "+mode,(Func<Task>)(()=>Policy(mode))))
        .Concat(new[]{"master","light","both","equivalent","xml","xml-repeat","missing","rational","precision","bound","minmax","duplicate","light-bound","light-interval","hdr10plus","dovi","dolby","vivid","future-hdr"}.Select(mode=>("HDR gamut metadata "+mode,(Func<Task>)(()=>Metadata(mode)))))
        .Concat(new[]{"codec","missing-codec","peak","disabled","key"}.Select(mode=>("HDR gamut intent "+mode,(Func<Task>)(()=>Intent(mode)))));
    private static LiveDashSelectionPolicy Base=>new(){NormalizeTransitions=true,AdaptiveQualityLadder=true,TransitionHeight=90,NormalizeVideoGeometry=true,TransitionVideoCodec="hevc",TransitionColorPolicy="hdr-preserve"};
    private static Task Policy(string mode)
    {
        var policy=Base;var facts=new TransitionVideoFacts("yuv420p10le","smpte2084","bt2020","bt2020nc","tv","1:1",0);var first=facts;
        if(mode=="hlg")facts=first=facts with{Transfer="arib-std-b67"};if(mode=="full")facts=facts with{Range="pc"};if(mode=="12bit")facts=facts with{Pixel="yuv422p12le"};
        if(mode is "rgb709" or "rgb-p3" or "p3-yuv" or "2020-yuv" or "2020-output" or "unknown-range" or "rgb-limited" or "rgb-matrix" or "alpha" or "float" or "pq-wide")
        {
            policy=policy with{TransitionColorPolicy=mode=="2020-output"?"bt2020":"wide-to-bt709"};facts=new("gbrp","iec61966-2-1",mode=="rgb-p3"?"smpte432":"bt709","gbr","pc","1:1",0);
            if(mode is "p3-yuv" or "2020-yuv")facts=facts with{Pixel="yuv444p10le",Matrix="bt2020nc",Primaries=mode=="p3-yuv"?"smpte432":"bt2020",Transfer="bt709",Range="tv"};
            if(mode=="unknown-range")facts=facts with{Range=""};if(mode=="rgb-limited")facts=facts with{Range="tv"};if(mode=="rgb-matrix")facts=facts with{Matrix="bt709"};if(mode=="alpha")facts=facts with{Pixel="gbrap"};if(mode=="float")facts=facts with{Pixel="gbrpf32le",Transfer="linear"};if(mode=="pq-wide")facts=first;
        }
        if(mode=="mixed-hdr")facts=facts with{Transfer="arib-std-b67"};if(mode=="hdr-8bit")facts=facts with{Pixel="yuv420p"};if(mode=="hdr-709")facts=facts with{Primaries="bt709"};if(mode=="dynamic")facts=facts with{AdvancedHdr=true};if(mode=="no-intent")policy=policy with{TransitionColorPolicy=null};
        var invalid=mode is "unknown-range" or "rgb-limited" or "rgb-matrix" or "alpha" or "float" or "pq-wide" or "mixed-hdr" or "hdr-8bit" or "hdr-709" or "dynamic" or "no-intent";
        try{var filter=TransitionConversionPlan.VideoFilter(policy,facts,first,160);WriteCleanupChecks.Check(!invalid,"Unsafe color conversion succeeded.");WriteCleanupChecks.Check(filter.Contains("format="+TransitionConversionPlan.VideoPixel(policy)),"Explicit bit depth was not selected.");if(policy.TransitionColorPolicy=="hdr-preserve")WriteCleanupChecks.Check(!filter.Contains("tonemap")&&!filter.Contains("sidedata=mode=delete"),"HDR preservation tone mapped or deleted source metadata.");}
        catch(NotSupportedException)when(invalid){return Task.CompletedTask;}WriteCleanupChecks.Check(!invalid,"Expected unsupported color facts to fail.");return Task.CompletedTask;
    }
    internal static XElement Master()=>XElement.Parse("<side_data side_data_type='Mastering display metadata' red_x='34000/50000' red_y='16000/50000' green_x='13250/50000' green_y='34500/50000' blue_x='7500/50000' blue_y='3000/50000' white_point_x='15635/50000' white_point_y='16450/50000' min_luminance='50/10000' max_luminance='10000000/10000'/>");
    private static Task Metadata(string mode)
    {
        var master=Master();var light=XElement.Parse("<side_data side_data_type='Content light level metadata' max_content='1000' max_average='400'/>");var frame=new XElement("frame");
        if(mode!="light")frame.Add(master);if(mode is "light" or "both" or "light-bound" or "light-interval")frame.Add(light);
        if(mode=="missing")master.Attribute("red_x")!.Remove();if(mode=="rational")master.SetAttributeValue("red_x","1/0");if(mode=="precision")master.SetAttributeValue("red_x","1/3");if(mode=="bound")master.SetAttributeValue("red_x","50001/50000");if(mode=="minmax")master.SetAttributeValue("min_luminance","20000000/10000");if(mode=="duplicate")frame.Add(new XElement(master));if(mode=="light-bound")light.SetAttributeValue("max_content","65536");if(mode=="light-interval")light.SetAttributeValue("max_average","2000");
        if(mode is "hdr10plus" or "dovi" or "dolby" or "vivid" or "future-hdr")frame.Add(new XElement("side_data",new XAttribute("side_data_type",mode switch{"hdr10plus"=>"HDR Dynamic Metadata SMPTE2094-40 (HDR10+)","dovi"=>"DOVI configuration record","dolby"=>"Dolby Vision RPU Data","future-hdr"=>"Unknown HDR metadata",_=>"HDR Vivid Dynamic Metadata"})));
        if(mode is "xml" or "xml-repeat"){var original=new XElement(master);master.RemoveAttributes();master.Add(original.Attributes().Select(attribute=>new XElement("side_datum",new XAttribute("key",attribute.Name.LocalName),new XAttribute("value",attribute.Value))));}
        var invalid=mode is not ("master" or "light" or "both" or "equivalent" or "xml" or "xml-repeat");
        try{var result=TransitionHdrMetadata.Read(frame);WriteCleanupChecks.Check(!invalid,"Malformed HDR metadata succeeded.");if(mode=="equivalent"){master.SetAttributeValue("red_x","68/100");WriteCleanupChecks.Check(result==TransitionHdrMetadata.Read(frame),"Equivalent rational HDR metadata changed its identity.");}if(mode is "xml" or "xml-repeat")WriteCleanupChecks.Check(result.MasterDisplay is not null&&result==TransitionHdrMetadata.Read(frame)&&master.Attributes().Count()==0,"Actual ffprobe XML was ignored or mutated during inspection.");if(mode=="both")WriteCleanupChecks.Check(result.EncoderOptions.EndsWith(":max-cll=1000,400")&&result.EncoderOptions.Contains("R(34000,16000)"),"Static HDR values were not expressed exactly for HEVC.");}
        catch(Exception e)when(invalid&&e is InvalidDataException or NotSupportedException){return Task.CompletedTask;}WriteCleanupChecks.Check(!invalid,"Expected malformed/dynamic HDR metadata to fail.");return Task.CompletedTask;
    }
    private static Task Intent(string mode)
    {
        var policy=Base;policy=mode switch{"codec"=>policy with{TransitionVideoCodec="av1"},"missing-codec"=>policy with{TransitionVideoCodec=null},"peak"=>policy with{HdrPeakNits=1000},"disabled"=>policy with{NormalizeTransitions=false},_=>policy};
        if(mode=="key")
        {
            var track=new DashRepresentation{Id="video",Url=new("https://fixture.example/v"),MimeType="video/mp4",Codecs="hev1",Width=128,Height=72};var request=new NativeDownloadRequest{Url=track.Url,OutputDirectory="unused",LiveDashSelection=policy};
            WriteCleanupChecks.Check(new DashTrackSelection(track,request).Key!=new DashTrackSelection(track,request with{LiveDashSelection=policy with{TransitionColorPolicy="bt2020"}}).Key,"Changing HDR output intent reused an incompatible checkpoint.");return Task.CompletedTask;
        }
        try{policy.Validate();throw new Exception("Invalid HDR output intent succeeded.");}catch(ArgumentException){}return Task.CompletedTask;
    }
}
