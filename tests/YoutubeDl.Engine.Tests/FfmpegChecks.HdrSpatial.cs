using System.Buffers.Binary;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Media;

internal static partial class FfmpegChecks
{
    private static readonly string[] SpatialModes=["plus-reorder","plus-two","plus-three","plus-scale","plus-reorder-scale","plus-down","plus-late","clip-color","dolby-scale","dolby-pad","dolby-active","dolby-hlg","dolby-cmv40","dolby5","dolby5-pad","dolby10","dolby10-hlg","caption","recover","kill","corrupt","cancel","legacy","invalid-window","plus-pad","nonuniform","rotation","incompatible","enhancement","profile7","missing-rpu","missing-master"];
    internal static IEnumerable<(string Name,Func<Task> Test)> HdrSpatialMediaChecks(string ffmpeg)=>SpatialModes.Select(mode=>("HDR spatial media "+mode,(Func<Task>)(()=>WpfLadderMedia(ffmpeg,"spatial-"+mode))));
    private static bool SpatialDolby(string mode)=>mode.StartsWith("dolby",StringComparison.Ordinal)||mode is "recover" or "kill" or "corrupt" or "cancel" or "incompatible" or "enhancement" or "profile7" or "missing-rpu" or "missing-master";
    private static bool SpatialFull(string mode)=>mode.StartsWith("dolby5",StringComparison.Ordinal);
    private static bool SpatialAv1(string mode)=>mode.StartsWith("dolby10",StringComparison.Ordinal)||mode=="missing-master";
    private static (int W,int H) SpatialSize(string mode,bool later)=>mode is "dolby-pad" or "dolby5-pad" or "plus-pad"?(128,96):mode=="nonuniform"?(126,96):mode=="plus-down"?(320,180):(later?128:64,later?72:36);
    private static LiveDashSelectionPolicy SpatialPolicy(string mode)=>HdrSpatialChecks.Policy with{TransitionColorPolicy=mode=="legacy"?"hdr-metadata":"hdr-spatial",TransitionAudioCodec="flac",TransitionAudioLayout="stereo",TransitionAudioSampleRate=48000};
    private static async Task SpatialAssets(string ffmpeg,string folder,string period,string mode)
    {
        var later=period=="p1";var dolby=SpatialDolby(mode);var full=SpatialFull(mode);var av1=SpatialAv1(mode);var hlg=mode is "dolby-hlg" or "dolby10-hlg";var (w,h)=SpatialSize(mode,later);var fps=later?15:10;var reorder=mode.Contains("reorder",StringComparison.Ordinal);var baseFile=Path.Combine(folder,"base.mp4");var profile=new TransitionHdrMetadata("G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,50)","1000,400");var signal=full?"range=full:color_primaries=unknown:color_trc=unknown:colorspace=ipt-c2":"range=limited:color_primaries=bt2020:color_trc="+(hlg?"arib-std-b67":"smpte2084")+":colorspace=bt2020nc";
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size="+w+"x"+h+":rate="+fps,"-f","lavfi","-i","sine=frequency="+(later?880:440)+":sample_rate=48000","-t","4","-vf","format=yuv420p10le,setparams="+signal,"-c:v","libx265","-threads","1","-preset","ultrafast","-pix_fmt","yuv420p10le","-g",(fps*2).ToString(),"-x265-params","lossless=1:pools=1:frame-threads=1:log-level=error:aud=1:repeat-headers=1:bframes="+(reorder?2:0)+(mode=="missing-master"?"":profile.EncoderOptions),"-c:a","alac",baseFile]);
        var raw=Path.Combine(folder,"base.hevc");await Run(ffmpeg,["-v","error","-y","-i",baseFile,"-map","0:v","-c","copy","-bsf:v","hevc_mp4toannexb,hevc_metadata=aud=insert","-f","hevc",raw]);var injected=Path.Combine(folder,"injected.hevc");await using(var destination=File.Create(injected))
        {
            var index=0;await foreach(var unit in HevcMetadataTransport.AccessUnits(raw,default))
            {
                var inserted=false;foreach(var nal in unit)
                {
                    if(HevcMetadataTransport.Type(nal)<=31&&!inserted){inserted=true;if(!dolby&&(mode!="plus-late"||later||index>=fps*2)){var windows=mode=="plus-three"?3:mode is "plus-two" or "plus-scale" or "plus-reorder-scale" or "plus-reorder" or "caption"?2:1;var payload=HdrSpatialChecks.PlusPayload(windows,w,h);if(index%3==1)payload[10]^=1;if(mode=="invalid-window"){var bits=new HdrBits(HdrSpatialChecks.PlusPayload(2,w,h));var bad=new HdrBits();bad.Put(w+10,16);for(var bit=0;bit<16;bit++)bits.Data[58+bit]=bad.Data[bit];payload=bits.Bytes();}await Write(HevcMetadataTransport.EncodeSei([(4,payload)]));}}await Write(nal);
                }
                if(dolby)
                {
                    var fixture=full?SpatialDolbyFixtures.Profile5:hlg?DolbyMetadataFixtures.Profile84:mode=="dolby-cmv40"||mode is "recover" or "kill" or "corrupt" or "cancel"&&index>=fps*2?DolbyMetadataFixtures.Cmv40:DolbyMetadataFixtures.Profile8;var rpu=HdrSpatialChecks.Nal(fixture);rpu=DolbySpatialRpu.WithArea(rpu,mode=="dolby-active"?(w/8,w/8,h/6,h/6):(0,0,0,0));await Write(rpu);
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
        await Run(ffmpeg,["-v","error","-y","-i",source,"-map","0:v","-map","0:a","-c","copy","-strict","unofficial","-f","dash","-dash_segment_type","mp4","-seg_duration","2","-use_timeline","0","-init_seg_name","init-$RepresentationID$.m4s","-media_seg_name","chunk-$RepresentationID$-$Number%05d$.m4s",Path.Combine(folder,"source.mpd")]);
        if(mode=="rotation"){var init=Path.Combine(folder,"init-0.m4s");var bytes=await File.ReadAllBytesAsync(init);var at=bytes.AsSpan().IndexOf("tkhd"u8)+4;var matrix=at+(bytes[at]==1?52:40);var values=new[]{0,-65536,0,65536,0,0,0,0,1073741824};for(var i=0;i<9;i++)BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(matrix+i*4),values[i]);await File.WriteAllBytesAsync(init,bytes);}
        if(mode=="missing-rpu"){var init=Path.Combine(folder,"init-0.m4s");var bytes=await File.ReadAllBytesAsync(init);var at=bytes.AsSpan().IndexOf("dvvC"u8);bytes[at+7]&=0xfb;await File.WriteAllBytesAsync(init,bytes);}
        foreach(var role in new[]{"v","a"}){var id=role=="v"?"0":"1";await using var output=File.Create(Path.Combine(folder,"reference-"+role+".mp4"));foreach(var path in new[]{Path.Combine(folder,"init-"+id+".m4s")}.Concat(Directory.GetFiles(folder,"chunk-"+id+"-*.m4s").Order(StringComparer.Ordinal).Take(2))){await using var input=File.OpenRead(path);await input.CopyToAsync(output);}}
    }
    private static string SpatialReferenceVideo(string mode,string period)
    {
        var (w,h)=SpatialSize(mode,period=="p1");var scale=Math.Min(160d/w,90d/h);var sw=(int)(w*scale)/2*2;var sh=(int)(h*scale)/2*2;var full=SpatialFull(mode);return(full?"setparams=colorspace=bt709,":"")+"scale="+sw+":"+sh+":flags=bicubic:in_range="+(full?"full":"limited")+":out_range="+(full?"full":"limited")+",pad=160:90:"+((160-sw)/2)+":"+((90-sh)/2)+",setsar=1,fps=30"+(full?",setparams=colorspace=ipt-c2":"");
    }
    private static async Task SpatialVerify(string ffmpeg,string assets,NativeDownloadResult result,string mode,int duration)
    {
        await ColorAudioVerifyAudio(ffmpeg,assets,result,SpatialPolicy(mode) with{TransitionColorPolicy=null},duration);var probe=Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe");var dolby=SpatialDolby(mode);var expectedPlus=new List<byte[]?>();var expectedRpu=new List<(string Identity,(int,int,int,int)? Area)>();
        foreach(var period in new[]{"p0","p1"})
        {
            var source=Path.Combine(assets,period,"high","reference-v.mp4");var frames=await SpatialFrameInfo(probe,source);var packets=await SpatialPacketMetadata(probe,source,SpatialAv1(mode));var geometry=HdrSpatialGeometry.Create(SpatialSize(mode,period=="p1").W,SpatialSize(mode,period=="p1").H,160,90,!dolby);var length=period=="p0"&&duration==7?3:4;
            for(var i=0;i<length*30;i++){var index=(int)Math.Floor(i*frames.Length/4d/30);var data=packets[frames[index].Position];if(dolby){var area=DolbySpatialRpu.Area(data.Rpu!)??(0,0,0,0);expectedRpu.Add((DolbySpatialRpu.ContentIdentity(data.Rpu!),geometry.ActiveArea(area.Item1,area.Item2,area.Item3,area.Item4)));}else expectedPlus.Add(data.Plus is null?null:HdrPlusSpatial.Map(data.Plus,geometry));}
        }
        foreach(var path in result.OutputPaths.Where(path=>Path.GetFileName(path) is "recording.mp4" or "recording.original.mp4" or "recording.video.mp4"))
        {
            var frames=await SpatialFrameInfo(probe,path);var packets=await SpatialPacketMetadata(probe,path,false);WriteCleanupChecks.Check(frames.Length==duration*30,"Spatial HDR publication lost pictures.");for(var i=0;i<frames.Length;i++){var data=packets[frames[i].Position];if(dolby)WriteCleanupChecks.Check(data.Rpu is not null&&DolbySpatialRpu.ContentIdentity(data.Rpu)==expectedRpu[i].Identity&&(DolbySpatialRpu.Area(data.Rpu)??(0,0,0,0))==expectedRpu[i].Area,"Spatial Dolby remapping changed non-geometric RPU content or active-area offsets.");else WriteCleanupChecks.Check((data.Plus is null)==(expectedPlus[i] is null)&& (data.Plus is null||data.Plus.SequenceEqual(expectedPlus[i]!)),"Reordered HDR10+ geometry metadata shifted to a different picture.");}
            if(dolby){using var document=JsonDocument.Parse(await Run(probe,["-v","error","-select_streams","v:0","-show_streams","-of","json",path]));var cfg=document.RootElement.GetProperty("streams")[0].GetProperty("side_data_list").EnumerateArray().Single(value=>value.GetProperty("side_data_type").GetString()=="DOVI configuration record");WriteCleanupChecks.Check(cfg.GetProperty("dv_profile").GetInt32()==(SpatialFull(mode)?5:8),"Broader Dolby output lost its correct profile signaling.");}
        }
    }
    internal static async Task<(long Position,decimal Time)[]> SpatialFrameInfo(string probe,string path){using var doc=JsonDocument.Parse(await Run(probe,["-v","error","-select_streams","v:0","-show_frames","-show_entries","frame=pkt_pos,best_effort_timestamp_time","-of","json",path]));return doc.RootElement.GetProperty("frames").EnumerateArray().Select(frame=>(long.Parse(frame.GetProperty("pkt_pos").GetString()!),decimal.Parse(frame.GetProperty("best_effort_timestamp_time").GetString()!,System.Globalization.CultureInfo.InvariantCulture))).ToArray();}
    internal static async Task<Dictionary<long,(byte[]? Plus,byte[]? Rpu)>> SpatialPacketMetadata(string probe,string path,bool av1)
    {
        using var doc=JsonDocument.Parse(await Run(probe,["-v","error","-select_streams","v:0","-show_packets","-show_entries","packet=pos,size","-of","json",path]));var result=new Dictionary<long,(byte[]?,byte[]?)>();byte[]? current=null;await using var source=File.OpenRead(path);
        foreach(var packet in doc.RootElement.GetProperty("packets").EnumerateArray()){var pos=long.Parse(packet.GetProperty("pos").GetString()!);var bytes=new byte[int.Parse(packet.GetProperty("size").GetString()!)];source.Position=pos;await source.ReadExactlyAsync(bytes);byte[]? rpu=null;if(av1)rpu=Av1DolbyMetadata.Rpus(bytes).Single();else{var at=0;while(at<bytes.Length){var size=(int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at));at+=4;var nal=bytes.AsSpan(at,size).ToArray();at+=size;if(HevcMetadataTransport.Type(nal)==62)rpu=nal;foreach(var item in HevcMetadataTransport.Sei(nal))if(item.Type==4&&HevcMetadataTransport.HdrPlus(item.Data))current=item.Data;}}result.Add(pos,(current,rpu));}
        return result;
    }
}


