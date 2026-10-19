using System.Buffers.Binary;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Media;

internal static partial class FfmpegChecks
{
    private static readonly string[] TransformModes=["plus-pad","plus-two-pad","plus-three-pad","plus-anamorphic","plus-quarter","plus-oblique","plus-fractional","dolby-pad","dolby-anamorphic","dolby-quarter","dolby-oblique","mel","mel-pad","mel-quarter","caption","recover","kill","corrupt","cancel","oblique-window","inset-oblique","fel","missing-layer","bad-matrix","bad-cdf"];
    internal static IEnumerable<(string Name,Func<Task> Test)> HdrTransformMediaChecks(string ffmpeg)=>TransformModes.Select(mode=>("HDR transform media "+mode,(Func<Task>)(()=>WpfLadderMedia(ffmpeg,"transform-"+mode))));
    private static bool TransformDolby(string mode)=>mode.StartsWith("av1-",StringComparison.Ordinal)||mode.StartsWith("single-",StringComparison.Ordinal)||mode.StartsWith("dolby",StringComparison.Ordinal)||mode.StartsWith("mel",StringComparison.Ordinal)||mode is "fel" or "missing-layer" or "inset-oblique" or "recover" or "kill" or "corrupt" or "cancel" or "incompatible" or "enhancement" or "profile7" or "missing-rpu" or "missing-master";
    private static bool TransformFull(string mode)=>mode.StartsWith("dolby5",StringComparison.Ordinal);
    private static bool TransformAv1(string mode)=>mode.StartsWith("dolby10",StringComparison.Ordinal)||mode.StartsWith("av1-",StringComparison.Ordinal)||mode=="missing-master";
    private static (int W,int H) TransformSize(string mode,bool later)=>mode.StartsWith("selector-",StringComparison.Ordinal)||mode.Contains("pad",StringComparison.Ordinal)||mode=="caption"?(128,96):mode=="plus-fractional"?(126,96):(later?128:64,later?72:36);
    private static double TransformAngle(string mode){var degrees=mode.Contains("quarter",StringComparison.Ordinal)?90:mode.Contains("oblique",StringComparison.Ordinal)?30:0;var r=degrees*Math.PI/180;return Math.Atan2(Math.Round(Math.Sin(r)*65536),Math.Round(Math.Cos(r)*65536))*180/Math.PI;}
    private static string TransformAspect(string mode)=>mode.Contains("anamorphic",StringComparison.Ordinal)?"2:1":"1:1";
    private static HdrCanvasTransform TransformGeometry(string mode,string period){var (w,h)=TransformSize(mode,period=="p1");return HdrCanvasTransform.Create(w,h,160,90,TransformAspect(mode),TransformAngle(mode));}

    private static LiveDashSelectionPolicy TransformPolicy(string mode)=>HdrTransformChecks.Policy with{TransitionColorPolicy="hdr-transform",TransitionAudioCodec="flac",TransitionAudioLayout="stereo",TransitionAudioSampleRate=48000};
    private static Task TransformAssets(string ffmpeg,string folder,string period,string mode)=>TransformAssetsCore(ffmpeg,folder,period,mode,null);
    private static async Task TransformAssetsCore(string ffmpeg,string folder,string period,string mode,byte[]? enhancementRpu,bool half=false)
    {
        var later=period=="p1";var dolby=TransformDolby(mode);var full=TransformFull(mode);var av1=TransformAv1(mode);var hlg=mode is "dolby-hlg" or "dolby10-hlg" or "av1-hlg-compressed" or "source-hlg";var (w,h)=TransformSize(mode,later);var fps=later?15:10;var reorder=mode.Contains("reorder",StringComparison.Ordinal);var baseFile=Path.Combine(folder,"base.mp4");var profile=new TransitionHdrMetadata("G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,50)","1000,400");var signal=full?"range=full:color_primaries=unknown:color_trc=unknown:colorspace=ipt-c2":"range=limited:color_primaries=bt2020:color_trc="+(hlg?"arib-std-b67":mode.StartsWith("source-",StringComparison.Ordinal)?mode switch{"source-gamma22"=>"bt470m","source-gamma28"=>"bt470bg","source-missing-peak"=>"linear",_=>mode[7..]}:"smpte2084")+":colorspace=bt2020nc";
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i",(mode is "black" or "white"?"color="+mode+":size=":"testsrc2=size=")+w+"x"+h+":rate="+fps,"-f","lavfi","-i","sine=frequency="+(later?880:440)+":sample_rate=48000","-t","4","-vf","format=yuv420p10le,setparams="+signal,"-c:v","libx265","-threads","1","-preset","ultrafast","-pix_fmt","yuv420p10le","-g",(fps*2).ToString(),"-x265-params","lossless=1:pools=1:frame-threads=1:log-level=error:aud=1:repeat-headers=1:bframes="+(reorder?2:0)+(mode is "missing-master" or "source-missing-peak"?"":profile.EncoderOptions),"-c:a","alac",baseFile]);
        var raw=Path.Combine(folder,"base.hevc");await Run(ffmpeg,["-v","error","-y","-i",baseFile,"-map","0:v","-c","copy","-bsf:v","hevc_mp4toannexb,hevc_metadata=aud=insert","-f","hevc",raw]);var injected=Path.Combine(folder,"injected.hevc");await using(var destination=File.Create(injected))
        {
            var index=0;await foreach(var unit in HevcMetadataTransport.AccessUnits(raw,default))
            {
                var inserted=false;foreach(var nal in unit)
                {
                    if(HevcMetadataTransport.Type(nal)<=31&&!inserted){inserted=true;if(!mode.StartsWith("source-",StringComparison.Ordinal)&&!dolby&&(mode!="plus-late"||later||index>=fps*2)){var windows=mode=="plus-three-pad"?3:mode.StartsWith("selector-",StringComparison.Ordinal)?2:mode is "plus-two-pad" or "plus-anamorphic" or "plus-quarter" or "oblique-window" or "caption"?2:1;var payload=mode.StartsWith("selector-",StringComparison.Ordinal)?HdrSelectorChecks.WindowPayload(mode=="selector-projective-hard",mode=="selector-clipped"):windows==1?HdrTransformChecks.Payload(width:w,height:h,bad:mode=="bad-cdf"):HdrSpatialChecks.PlusPayload(windows,w,h);if(mode=="invalid-window"){var bits=new HdrBits(HdrSpatialChecks.PlusPayload(2,w,h));var bad=new HdrBits();bad.Put(w+10,16);for(var bit=0;bit<16;bit++)bits.Data[58+bit]=bad.Data[bit];payload=bits.Bytes();}await Write(HevcMetadataTransport.EncodeSei([(4,payload)]));}}await Write(nal);
                }
                if(dolby)
                {
                    var fixture=full?SpatialDolbyFixtures.Profile5:hlg?DolbyMetadataFixtures.Profile84:mode=="dolby-cmv40"?DolbyMetadataFixtures.Cmv40:DolbyMetadataFixtures.Profile8;var rpu=HdrSpatialChecks.Nal(fixture);rpu=DolbySpatialRpu.WithArea(rpu,mode=="inset-oblique"?(w/8,w/8,h/6,h/6):(0,0,0,0));await Write(rpu);
                }
                index++;
            }
            async Task Write(byte[] nal){await destination.WriteAsync(new byte[]{0,0,0,1});await destination.WriteAsync(nal);}
        }
        var source=Path.Combine(folder,"coded.mp4");var args=new List<string>{"-v","error","-y","-r",fps.ToString(),"-f","hevc","-i",injected,"-i",baseFile,"-map","0:v","-map","1:a"};
        if(dolby){args.AddRange(["-c:v",av1?"libaom-av1":"libx265","-threads","1","-pix_fmt","yuv420p10le","-dolbyvision","1","-g",(fps*2).ToString()]);if(av1)args.AddRange(["-cpu-used","8","-crf","0","-b:v","0","-lag-in-frames","0"]);else args.AddRange(["-x265-params","lossless=1:pools=1:frame-threads=1:log-level=error:aud=1:bframes=0:repeat-headers=1:vbv-maxrate=50000:vbv-bufsize=50000"+profile.EncoderOptions]);}else args.AddRange(["-c:v","copy"]);args.AddRange(["-c:a","copy","-strict","unofficial",source]);await Run(ffmpeg,args);
        if(reorder)
        {
            var probe=Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe");var packetJson=await Run(probe,["-v","error","-select_streams","v:0","-show_packets","-show_entries","packet=pos,size","-of","json",baseFile]);await HdrSpatialPacketFixture.Rewrite(baseFile,source,packetJson,index=>{var payload=HdrSpatialChecks.PlusPayload(2,w,h);if(index%3==1)payload[10]^=1;return payload;});
        }
        if(mode is "profile7" or "enhancement" or "incompatible")
        {var bytes=await File.ReadAllBytesAsync(source);var offset=bytes.AsSpan().IndexOf("dvvC"u8);WriteCleanupChecks.Check(offset>=0,"Dolby source configuration is missing.");if(mode=="profile7")bytes[offset+6]=(byte)((7<<1)|(bytes[offset+6]&1));if(mode=="enhancement")bytes[offset+7]|=2;if(mode=="incompatible")bytes[offset+8]=(byte)((bytes[offset+8]&15)|0x40);await File.WriteAllBytesAsync(source,bytes);}
        if(mode.StartsWith("single-",StringComparison.Ordinal)||mode.StartsWith("mel",StringComparison.Ordinal)||mode is "fel" or "missing-layer" or "recover" or "kill" or "corrupt" or "cancel")
        {
            var json=await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-select_streams","v:0","-show_packets","-show_entries","packet=pos,size","-of","json",source]);
            var multi=Path.Combine(folder,"multilayer.mp4");string? elFile=null;string? elJson=null;if(half){elFile=Path.Combine(folder,"half-el.mp4");await Run(ffmpeg,["-v","error","-y","-i",baseFile,"-map","0:v:0","-vf","scale="+(w/2)+":"+(h/2),"-c:v","libx265","-threads","1","-x265-params","lossless=1:pools=1:frame-threads=1:log-level=error:aud=1:repeat-headers=1:bframes=0",elFile]);elJson=await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-select_streams","v:0","-show_packets","-show_entries","packet=pos,size","-of","json",elFile]);}
            await HdrMelPacketFixture.Rewrite(source,multi,json,mode,enhancementRpu,elFile,elJson);source=multi;
        }
        if(mode.StartsWith("av1-",StringComparison.Ordinal)){var compressed=Path.Combine(folder,"compressed-av1.mp4");await Run(ffmpeg,["-v","error","-y","-i",source,"-map","0:v","-map","0:a","-c","copy","-bsf:v","dovi_rpu=compression="+(mode=="av1-extended"?"extended":"limited"),"-strict","unofficial",compressed]);source=compressed;}
        await Run(ffmpeg,["-v","error","-y","-i",source,"-map","0:v","-map","0:a","-c","copy","-strict","unofficial","-f","dash","-dash_segment_type","mp4","-seg_duration","2","-use_timeline","0","-init_seg_name","init-$RepresentationID$.m4s","-media_seg_name","chunk-$RepresentationID$-$Number%05d$.m4s",Path.Combine(folder,"source.mpd")]);
        {
            var init=Path.Combine(folder,"init-0.m4s");var bytes=await File.ReadAllBytesAsync(init);var at=bytes.AsSpan().IndexOf("tkhd"u8)+4;var matrix=at+(bytes[at]==1?52:40);var radians=TransformAngle(mode)*Math.PI/180;var c=(int)Math.Round(Math.Cos(radians)*65536);var s=(int)Math.Round(Math.Sin(radians)*65536);var values=new[]{c,-s,0,s,c,0,0,0,1073741824};if(mode=="bad-matrix")values[3]=32768;for(var i=0;i<9;i++)BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(matrix+i*4),values[i]);
            if(TransformAspect(mode)!="1:1"){var pasp=bytes.AsSpan().IndexOf("pasp"u8);WriteCleanupChecks.Check(pasp>=0,"Fixture pixel aspect is missing.");BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(pasp+4),2);BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(pasp+8),1);}
            await File.WriteAllBytesAsync(init,bytes);
        }
        if(mode=="missing-rpu"){var init=Path.Combine(folder,"init-0.m4s");var bytes=await File.ReadAllBytesAsync(init);var at=bytes.AsSpan().IndexOf("dvvC"u8);bytes[at+7]&=0xfb;await File.WriteAllBytesAsync(init,bytes);}
        foreach(var role in new[]{"v","a"}){var id=role=="v"?"0":"1";await using var output=File.Create(Path.Combine(folder,"reference-"+role+".mp4"));foreach(var path in new[]{Path.Combine(folder,"init-"+id+".m4s")}.Concat(Directory.GetFiles(folder,"chunk-"+id+"-*.m4s").Order(StringComparer.Ordinal).Take(2))){await using var input=File.OpenRead(path);await input.CopyToAsync(output);}}
    }
    private static string TransformReferenceVideo(string mode,string period)=>TransformGeometry(mode,period).Filter(false)+",fps=30";
    private static async Task TransformVerify(string ffmpeg,string assets,NativeDownloadResult result,string mode,int duration)
    {
        await ColorAudioVerifyAudio(ffmpeg,assets,result,TransformPolicy(mode) with{TransitionColorPolicy=null},duration);var probe=Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe");var dolby=TransformDolby(mode);var expectedPlus=new List<byte[]?>();var expectedRpu=new List<(string Identity,(int,int,int,int)? Area)>();
        foreach(var period in new[]{"p0","p1"})
        {
            var source=Path.Combine(assets,period,"high","reference-v.mp4");var frames=await TransformFrameInfo(probe,source);var packets=await TransformPacketMetadata(probe,source,TransformAv1(mode));var geometry=TransformGeometry(mode,period);var length=period=="p0"&&duration==7?3:4;
            for(var i=0;i<length*30;i++){var index=(int)Math.Floor(i*frames.Length/4d/30);var data=packets[frames[index].Position];if(dolby){var rpu=mode.StartsWith("mel",StringComparison.Ordinal)||mode is "recover" or "kill" or "corrupt" or "cancel"?DolbyMelRpu.ToSingleLayer(data.Rpu!):data.Rpu!;var area=DolbySpatialRpu.Area(rpu)??(0,0,0,0);expectedRpu.Add((DolbySpatialRpu.ContentIdentity(rpu),geometry.ActiveArea(area.Item1,area.Item2,area.Item3,area.Item4)));}else expectedPlus.Add(data.Plus is null?null:HdrPlusTransform.Map(data.Plus,geometry));}
        }
        foreach(var path in result.OutputPaths.Where(path=>Path.GetFileName(path) is "recording.mp4" or "recording.original.mp4" or "recording.video.mp4"))
        {
            var frames=await TransformFrameInfo(probe,path);var packets=await TransformPacketMetadata(probe,path,false);WriteCleanupChecks.Check(frames.Length==duration*30,"Transform HDR publication lost pictures.");for(var i=0;i<frames.Length;i++){var data=packets[frames[i].Position];if(dolby)WriteCleanupChecks.Check(data.Rpu is not null&&DolbySpatialRpu.ContentIdentity(data.Rpu)==expectedRpu[i].Identity&&(DolbySpatialRpu.Area(data.Rpu)??(0,0,0,0))==expectedRpu[i].Area,"Transform Dolby remapping changed non-geometric RPU content or active-area offsets.");else WriteCleanupChecks.Check((data.Plus is null)==(expectedPlus[i] is null)&& (data.Plus is null||data.Plus.SequenceEqual(expectedPlus[i]!)),"Reordered HDR10+ geometry metadata shifted to a different picture.");}
            if(dolby){using var document=JsonDocument.Parse(await Run(probe,["-v","error","-select_streams","v:0","-show_streams","-of","json",path]));var cfg=document.RootElement.GetProperty("streams")[0].GetProperty("side_data_list").EnumerateArray().Single(value=>value.GetProperty("side_data_type").GetString()=="DOVI configuration record");WriteCleanupChecks.Check(cfg.GetProperty("dv_profile").GetInt32()==(TransformFull(mode)?5:8),"Broader Dolby output lost its correct profile signaling.");}
        }
    }
    internal static async Task<(long Position,decimal Time)[]> TransformFrameInfo(string probe,string path){using var doc=JsonDocument.Parse(await Run(probe,["-v","error","-select_streams","v:0","-show_frames","-show_entries","frame=pkt_pos,best_effort_timestamp_time","-of","json",path]));return doc.RootElement.GetProperty("frames").EnumerateArray().Select(frame=>(long.Parse(frame.GetProperty("pkt_pos").GetString()!),decimal.Parse(frame.GetProperty("best_effort_timestamp_time").GetString()!,System.Globalization.CultureInfo.InvariantCulture))).ToArray();}
    internal static async Task<Dictionary<long,(byte[]? Plus,byte[]? Rpu)>> TransformPacketMetadata(string probe,string path,bool av1)
    {
        using var doc=JsonDocument.Parse(await Run(probe,["-v","error","-select_streams","v:0","-show_packets","-show_entries","packet=pos,size","-of","json",path]));var result=new Dictionary<long,(byte[]?,byte[]?)>();byte[]? current=null;await using var source=File.OpenRead(path);
        foreach(var packet in doc.RootElement.GetProperty("packets").EnumerateArray()){var pos=long.Parse(packet.GetProperty("pos").GetString()!);var bytes=new byte[int.Parse(packet.GetProperty("size").GetString()!)];source.Position=pos;await source.ReadExactlyAsync(bytes);byte[]? rpu=null;if(av1)rpu=Av1DolbyMetadata.Rpus(bytes).Single();else{var at=0;while(at<bytes.Length){var size=(int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at));at+=4;var nal=bytes.AsSpan(at,size).ToArray();at+=size;if(HevcMetadataTransport.Type(nal)==62)rpu=nal;foreach(var item in HevcMetadataTransport.Sei(nal))if(item.Type==4&&HevcMetadataTransport.HdrPlus(item.Data))current=item.Data;}}result.Add(pos,(current,rpu));}
        return result;
    }
}


