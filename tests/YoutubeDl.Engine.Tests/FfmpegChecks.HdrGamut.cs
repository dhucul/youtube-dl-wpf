using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;

internal static partial class FfmpegChecks
{
    private static readonly string[] HdrGamutModes=["pq","hlg","full","12bit","no-static","rgb709","rgb-p3","rgb2020","p3-yuv","2020-yuv","wide12","rgb2020-output","p3-output","clip-color","caption","recover","kill","corrupt","cancel","static-change","static-late","mixed-transfer","dynamic-late","dynamic-tone","rgb-missing","rgb-disabled"];
    internal static IEnumerable<(string Name,Func<Task> Test)> HdrGamutMediaChecks(string ffmpeg)=>HdrGamutModes.Select(mode=>("HDR gamut media "+mode,(Func<Task>)(()=>WpfLadderMedia(ffmpeg,"gamut-"+mode))))
        .Concat(new[]{("HDR gamut media dynamic copy",(Func<Task>)(()=>HdrGamutDynamicCopy(ffmpeg)))});
    private static bool HdrGamutHdr(string mode)=>mode is "pq" or "hlg" or "full" or "12bit" or "no-static" or "clip-color" or "caption" or "recover" or "kill" or "corrupt" or "cancel" or "static-change" or "static-late" or "mixed-transfer" or "dynamic-late" or "dynamic-tone";
    private static bool HdrGamutTen(string mode)=>HdrGamutHdr(mode)&&mode!="dynamic-tone"||mode is "rgb2020-output" or "p3-output";
    private static LiveDashSelectionPolicy HdrGamutPolicy(string mode)=>new()
    {
        NormalizeTransitions=true,AdaptiveQualityLadder=true,TransitionHeight=90,NormalizeVideoGeometry=true,TransitionVideoCodec="hevc",TransitionAudioCodec="flac",TransitionAudioLayout="stereo",TransitionAudioSampleRate=48000,
        TransitionColorPolicy=mode=="dynamic-tone"?"hdr-to-bt709":HdrGamutHdr(mode)?"hdr-preserve":HdrGamutTen(mode)?"bt2020":mode=="rgb-disabled"?"bt709":"wide-to-bt709",HdrPeakNits=mode=="dynamic-tone"?1000:0
    };
    private static async Task HdrGamutAssets(string ffmpeg,string folder,string period,string mode)
    {
        var later=period=="p1";var hdr=HdrGamutHdr(mode);var rgb=mode.StartsWith("rgb",StringComparison.Ordinal);var p3=mode is "rgb-p3" or "p3-yuv" or "p3-output";var wide=mode is "rgb2020" or "2020-yuv" or "wide12";var fps=later?15:10;
        var transfer=hdr?(mode=="hlg"||later&&mode=="mixed-transfer"?"arib-std-b67":"smpte2084"):rgb?"iec61966-2-1":wide?"bt2020-10":"bt709";
        var primaries=hdr||wide?"bt2020":p3?"smpte432":"bt709";var matrix=rgb?"0":hdr||wide?"bt2020nc":"bt709";var range=rgb||mode=="full"?"pc":"tv";var twelve=later&&mode is "12bit" or "wide12";var pixel=rgb?"rgb24":twelve?"yuv420p12le":hdr||wide||p3?"yuv420p10le":"yuv420p";
        var source=Path.Combine(folder,"coded.mp4");var signal="setparams=range="+(range=="pc"?"full":"limited")+":color_primaries="+primaries+":color_trc="+transfer+":colorspace="+matrix;
        var args=new List<string>{"-v","error","-y","-f","lavfi","-i",(rgb?"testsrc":"testsrc2")+"=size="+(later?"160x90":"128x72")+":rate="+fps,"-f","lavfi","-i","sine=frequency="+(later?880:440)+":sample_rate=48000","-t","4","-c:v",rgb?"libx264rgb":"libx265","-preset","ultrafast","-threads","1","-g",(fps*2).ToString(),"-bf","0","-pix_fmt",pixel};
        var missing=later&&mode=="rgb-missing";if(!missing)args.AddRange(["-vf",signal,"-color_primaries",primaries,"-color_trc",transfer,"-colorspace",matrix,"-color_range",range]);
        if(!rgb)
        {
            var options="lossless=1:pools=1:frame-threads=1:log-level=error:repeat-headers=1";
            if(hdr&&mode!="no-static")options+=":master-display=G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,50):max-cll="+(later&&mode=="static-change"?"2000,400":"1000,400");
            args.AddRange(["-x265-params",options]);
        }
        args.AddRange(["-c:a","alac",source]);await Run(ffmpeg,args);
        var video=source;
        if(later&&mode is "dynamic-late" or "dynamic-tone" or "static-late")
        {
            var raw=Path.Combine(folder,"coded.hevc");await Run(ffmpeg,["-v","error","-y","-i",source,"-map","0:v:0","-c","copy","-bsf:v","hevc_mp4toannexb","-f","hevc",raw]);var bytes=await File.ReadAllBytesAsync(raw);var seen=0;var offset=-1;
            for(var i=0;i+5<bytes.Length;i++)if(bytes[i]==0&&bytes[i+1]==0&&bytes[i+2]==1){var type=(bytes[i+3]>>1)&63;if(type is >=16 and <=21&&++seen==2){offset=i>0&&bytes[i-1]==0?i-1:i;break;}}
            WriteCleanupChecks.Check(offset>=0,"No later IDR for the dynamic-HDR fixture.");var patched=Path.Combine(folder,"dynamic.hevc");await File.WriteAllBytesAsync(patched,bytes[..offset].Concat(HdrGamutDynamicSei(mode=="static-late")).Concat(bytes[offset..]).ToArray());video=Path.Combine(folder,"dynamic.mp4");await Run(ffmpeg,["-v","error","-y","-fflags","+genpts","-r",fps.ToString(),"-i",patched,"-i",source,"-map","0:v:0","-map","1:a:0","-c","copy",video]);
            var probe=Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe");using var dynamic=JsonDocument.Parse(await Run(probe,["-v","error","-select_streams","v:0","-show_frames","-show_entries","frame=pts_time:frame_side_data=side_data_type","-of","json",video]));var frames=dynamic.RootElement.GetProperty("frames").EnumerateArray().ToArray();var firstDynamic=Array.FindIndex(frames,frame=>frame.ToString().Contains("HDR Dynamic",StringComparison.Ordinal));if(mode!="static-late")WriteCleanupChecks.Check(firstDynamic>=fps*2,"The HDR10+ fixture did not introduce decoded dynamic metadata after the first GOP.");else {using var levels=JsonDocument.Parse(await Run(probe,["-v","error","-select_streams","v:0","-show_frames","-show_entries","frame_side_data=side_data_type,max_content","-of","json",video]));var values=levels.RootElement.GetProperty("frames").EnumerateArray().SelectMany(frame=>frame.GetProperty("side_data_list").EnumerateArray()).Where(side=>side.TryGetProperty("max_content",out _)).Select(side=>side.GetProperty("max_content").GetInt32()).Distinct().ToArray();WriteCleanupChecks.Check(values.Contains(1000)&&values.Contains(2000),"The later-GOP static metadata change was not present in decoded frames.");}
        }
        await Run(ffmpeg,["-v","error","-y","-i",video,"-map","0:v:0","-map","0:a:0","-c","copy","-f","dash","-dash_segment_type","mp4","-seg_duration","2","-use_timeline","0","-init_seg_name","init-$RepresentationID$.m4s","-media_seg_name","chunk-$RepresentationID$-$Number%05d$.m4s",Path.Combine(folder,"source.mpd")]);
        foreach(var role in new[]{"v","a"})
        {
            var id=role=="v"?"0":"1";await using var output=File.Create(Path.Combine(folder,"reference-"+role+".mp4"));foreach(var path in new[]{Path.Combine(folder,"init-"+id+".m4s")}.Concat(Directory.GetFiles(folder,"chunk-"+id+"-*.m4s").Order(StringComparer.Ordinal).Take(2))){await using var input=File.OpenRead(path);await input.CopyToAsync(output);}
        }
    }
    // ITU-T T.35 HDR10+ one-window payload, deliberately inserted only at the second IDR.
    private static byte[] HdrGamutDynamicSei(bool staticChange=false)
    {
        var bits=new List<int>();void Put(int value,int count){for(var i=count-1;i>=0;i--)bits.Add((value>>i)&1);}
        Put(1,8);Put(1,2);Put(1000,27);Put(0,1);for(var i=0;i<3;i++)Put(100000,17);Put(50000,17);Put(0,4);Put(0,10);Put(0,1);Put(0,1);Put(0,1);while(bits.Count%8!=0)bits.Add(0);
        var payload=new List<byte>{0xb5,0,0x3c,0,1,4};for(var i=0;i<bits.Count;i+=8){var value=0;for(var j=0;j<8;j++)value=value*2+bits[i+j];payload.Add((byte)value);}
        var rbsp=staticChange?new List<byte>{144,4,0x07,0xd0,0x01,0x90,0x80}:new List<byte>{4,(byte)payload.Count};if(!staticChange){rbsp.AddRange(payload);rbsp.Add(0x80);}var result=new List<byte>{0,0,0,1,0x4e,1};var zeros=0;foreach(var value in rbsp){if(zeros>=2&&value<=3){result.Add(3);zeros=0;}result.Add(value);zeros=value==0?zeros+1:0;}return result.ToArray();
    }
    private static string HdrGamutReferenceVideo(string mode,string period)
    {
        var transfer=mode=="hlg"?"arib-std-b67":"smpte2084";var color=HdrGamutHdr(mode)?"zscale=p=bt2020:t="+transfer+":m=bt2020nc:r=limited:d=error_diffusion,format=yuv420p10le,":(mode.StartsWith("rgb",StringComparison.Ordinal)?"format=gbrp16le,":"")+(HdrGamutTen(mode)?"zscale=p=bt2020:t=bt2020-10:m=bt2020nc:r=limited:d=error_diffusion,format=yuv420p10le,":"zscale=p=bt709:t=bt709:m=bt709:r=limited:d=error_diffusion,format=yuv420p,");
        return color+"scale=160:90:force_original_aspect_ratio=decrease:force_divisible_by=2:reset_sar=1,pad=160:90:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30";
    }
    private static async Task HdrGamutVerify(string ffmpeg,string assets,NativeDownloadResult result,string mode,int duration)
    {
        var policy=HdrGamutPolicy(mode);await ColorAudioVerifyAudio(ffmpeg,assets,result,policy with{TransitionColorPolicy=null},duration);
        var probe=Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe");using var doc=JsonDocument.Parse(await Run(probe,["-v","error","-select_streams","v:0","-show_frames","-show_entries","frame=pix_fmt,color_range,color_transfer,color_primaries,color_space:frame_side_data=side_data_type,red_x,red_y,green_x,green_y,blue_x,blue_y,white_point_x,white_point_y,max_luminance,min_luminance,max_content,max_average","-of","json",result.OutputPath!]));
        var frames=doc.RootElement.GetProperty("frames").EnumerateArray().ToArray();WriteCleanupChecks.Check(frames.Length==duration*30,"HDR output lost decoded frames.");
        foreach(var frame in frames)
        {
            var ten=HdrGamutTen(mode);WriteCleanupChecks.Check(frame.GetProperty("pix_fmt").GetString()==(ten?"yuv420p10le":"yuv420p")&&frame.GetProperty("color_transfer").GetString()==(HdrGamutHdr(mode)?mode=="hlg"?"arib-std-b67":"smpte2084":ten?"bt2020-10":"bt709")&&frame.GetProperty("color_primaries").GetString()==(ten?"bt2020":"bt709")&&frame.GetProperty("color_space").GetString()==(ten?"bt2020nc":"bt709")&&frame.GetProperty("color_range").GetString()=="tv","HDR/wide-gamut output frame lost its declared profile.");
            var sides=frame.TryGetProperty("side_data_list",out var list)?list.EnumerateArray().ToArray():[];var staticExpected=HdrGamutHdr(mode)&&mode!="no-static";
            WriteCleanupChecks.Check(sides.Any(side=>side.GetProperty("side_data_type").GetString()=="Mastering display metadata")==staticExpected&&sides.Any(side=>side.GetProperty("side_data_type").GetString()=="Content light level metadata")==staticExpected,"HDR output dropped or invented static metadata.");
            if(staticExpected){var master=sides.Single(side=>side.GetProperty("side_data_type").GetString()=="Mastering display metadata");var light=sides.Single(side=>side.GetProperty("side_data_type").GetString()=="Content light level metadata");WriteCleanupChecks.Check(master.GetProperty("max_luminance").GetString()=="10000000/10000"&&master.GetProperty("red_x").GetString()=="34000/50000"&&light.GetProperty("max_content").GetInt32()==1000&&light.GetProperty("max_average").GetInt32()==400,"HDR output changed mastering/content-light values.");}
        }
    }
    private static async Task HdrGamutDynamicCopy(string ffmpeg)
    {
        using var files=new Files();var assets=Path.Combine(files.Directory,"assets");Directory.CreateDirectory(assets);await HdrGamutAssets(ffmpeg,assets,"p1","dynamic-late");var reads=0;var callbacks=0;
        var playlist="#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-VERSION:7\n#EXT-X-MEDIA-SEQUENCE:0\n#EXT-X-MAP:URI=\"init-0.m4s\"\n#EXTINF:2,\nchunk-0-00001.m4s\n#EXTINF:2,\nchunk-0-00002.m4s\n#EXT-X-ENDLIST\n";
        var page=new PageMetadata{Id="hdr-dynamic",Extractor="hdr-fixture",Title="HDR fixture",Media=[new(){Id="master",Url=new("https://hdr.example/media.m3u8"),Kind=MediaKind.Hls,Extension="mp4",HasVideo=true,HasAudio=false}]};
        var engine=new NativeEngine([new AdaptiveNoOverwriteChecks.Provider(page)],_=>AdaptiveAccountChecks.Http(async(message,ct)=>{if(message.RequestUri!.AbsolutePath.EndsWith(".m3u8",StringComparison.Ordinal))return AdaptiveAccountChecks.Text(playlist);reads++;return new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(assets,Path.GetFileName(message.RequestUri.AbsolutePath)),ct))};}));
        var request=new NativeDownloadRequest{Url=new("https://hdr.example/watch"),OutputDirectory=Path.Combine(files.Directory,"out"),FileName="dynamic",VideoOnly=true,FfmpegPath=ffmpeg,KeepOriginal=true,AddMetadata=true};var result=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);WriteCleanupChecks.Check(result.Succeeded&&callbacks==result.OutputPaths.Count,"Native HDR10+ stream-copy publication failed.");
        var probe=Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe");var reference=Path.Combine(assets,"reference-v.mp4");
        async Task<string[]> Packets(string path){using var doc=JsonDocument.Parse(await Run(probe,["-v","error","-select_streams","v:0","-show_packets","-show_data_hash","sha256","-show_entries","packet=data_hash","-of","json",path]));return doc.RootElement.GetProperty("packets").EnumerateArray().Select(row=>row.GetProperty("data_hash").GetString()!).ToArray();}
        async Task<string[]> Dynamic(string path){using var doc=JsonDocument.Parse(await Run(probe,["-v","error","-select_streams","v:0","-show_frames","-of","json",path]));return doc.RootElement.GetProperty("frames").EnumerateArray().Select((frame,index)=>(frame,index)).Where(row=>row.frame.TryGetProperty("side_data_list",out var sides)&&sides.EnumerateArray().Any(side=>side.GetProperty("side_data_type").GetString()!.Contains("HDR Dynamic",StringComparison.Ordinal))).Select(row=>row.index+":"+row.frame.GetProperty("side_data_list").EnumerateArray().Single(side=>side.GetProperty("side_data_type").GetString()!.Contains("HDR Dynamic",StringComparison.Ordinal)).ToString()).ToArray();}
        var packets=await Packets(reference);var metadata=await Dynamic(reference);WriteCleanupChecks.Check(metadata.Length>0,"The copy reference has no HDR10+ metadata.");
        foreach(var path in result.OutputPaths){await Run(ffmpeg,["-v","error","-xerror","-i",path,"-f","null","-"],true);var actualPackets=await Packets(path);var actualMetadata=await Dynamic(path);WriteCleanupChecks.Check(packets.SequenceEqual(actualPackets)&&metadata.SequenceEqual(actualMetadata),"Native stream-copy changed coded HDR packets or frame-indexed dynamic metadata.");}
        var before=reads;var count=callbacks;var repeat=await engine.DownloadAsync(request with{NoOverwrites=true,FfmpegPath=Path.Combine(files.Directory,"missing.exe")},null,null,default,_=>callbacks++);WriteCleanupChecks.Check(repeat.Skipped&&reads==before&&callbacks==count,"HDR no-overwrite repeat transferred media bodies or published callbacks.");
    }
}




