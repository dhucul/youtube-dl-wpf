using System.Buffers.Binary;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Media;

internal static partial class FfmpegChecks
{
    private static readonly string[] MetadataModes=["static-period","static-late","master-period","master-late","no-static","plus","plus-change","plus-late","plus-retime","clip-color","dolby","dolby-dvh1","dolby-dvhe","dolby-alac","dolby-cmv40","dolby-change","dolby-hlg","dolby-bframes","dv-clip","caption","recover","kill","corrupt","cancel","dv-geometry","dv-profile","dv-enhancement","plus-bframes","plus-window","plus-version","legacy"];
    internal static IEnumerable<(string Name,Func<Task> Test)> HdrMetadataMediaChecks(string ffmpeg)=>MetadataModes.Select(mode=>("HDR metadata media "+mode,(Func<Task>)(()=>WpfLadderMedia(ffmpeg,"metadata-"+mode))))
        .Concat(new[]{("HDR metadata media remux",(Func<Task>)(()=>MetadataRemux(ffmpeg)))});
    private static bool MetadataDolby(string mode)=>mode.StartsWith("dolby",StringComparison.Ordinal)||mode.StartsWith("dv-",StringComparison.Ordinal)||mode is "recover" or "kill" or "corrupt" or "cancel";
    private static bool MetadataPlus(string mode)=>mode.StartsWith("plus",StringComparison.Ordinal)||mode is "clip-color" or "caption" or "legacy";
    private static LiveDashSelectionPolicy MetadataPolicy(string mode)=>HdrMetadataChecks.PolicyValue with{TransitionColorPolicy=mode=="legacy"?"hdr-preserve":"hdr-metadata",TransitionAudioCodec=mode=="dolby-alac"?"alac":"flac",TransitionAudioLayout="stereo",TransitionAudioSampleRate=48000};
    private static async Task MetadataAssets(string ffmpeg,string folder,string period,string mode)
    {
        var later=period=="p1";var dolby=MetadataDolby(mode);var plus=MetadataPlus(mode);var hlg=mode=="dolby-hlg";var fps=later?15:10;var size=mode=="dv-geometry"&&!later?"128x72":dolby||plus?"160x90":later?"160x90":"128x72";var baseFile=Path.Combine(folder,"base.mp4");var transfer=hlg?"arib-std-b67":"smpte2084";var staticProfile=new TransitionHdrMetadata("G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,50)","1000,400");
        if(later&&mode=="static-period")staticProfile=staticProfile with{ContentLight="2000,500"};if(later&&mode=="master-period")staticProfile=staticProfile with{MasterDisplay="G(13250,34500)B(7500,3000)R(33000,16000)WP(15635,16450)L(20000000,50)"};
        var args=new List<string>{"-v","error","-y","-f","lavfi","-i","testsrc2=size="+size+":rate="+fps,"-f","lavfi","-i","sine=frequency="+(later?880:440)+":sample_rate=48000","-t","4","-vf","setparams=range=limited:color_primaries=bt2020:color_trc="+transfer+":colorspace=bt2020nc","-c:v","libx265","-threads","1","-preset","ultrafast","-pix_fmt","yuv420p10le","-g",(fps*2).ToString(),"-x265-params","lossless=1:pools=1:frame-threads=1:log-level=error:aud=1:repeat-headers=1:bframes="+(mode is "plus-bframes" or "dolby-bframes"?"2":"0")+(mode=="no-static"?"":staticProfile.EncoderOptions),"-c:a","alac",baseFile};await Run(ffmpeg,args);
        var raw=Path.Combine(folder,"base.hevc");await Run(ffmpeg,["-v","error","-y","-i",baseFile,"-map","0:v:0","-c","copy","-bsf:v","hevc_mp4toannexb,hevc_metadata=aud=insert","-f","hevc",raw]);var patched=Path.Combine(folder,"source.hevc");await using(var output=File.Create(patched))
        {
            var index=0;await foreach(var unit in HevcMetadataTransport.AccessUnits(raw,default))
            {
                var inserted=false;foreach(var nal in unit)
                {
                    if(!inserted&&HevcMetadataTransport.Type(nal)<=31)
                    {
                        inserted=true;var messages=new List<(int Type,byte[] Data)>();if(later&&index>=fps*2&&mode is "static-late" or "master-late" or "recover" or "kill" or "corrupt" or "cancel")messages.AddRange(HevcMetadataTransport.Static(mode=="master-late"?staticProfile with{MasterDisplay="G(13250,34500)B(7500,3000)R(33000,16000)WP(15635,16450)L(20000000,50)"}:staticProfile with{ContentLight="2000,500"}));
                        if(plus&&(mode!="plus-late"||later||index>=fps*2))messages.Add((4,MetadataPlusPayload(mode,index>=fps*2&&mode is "plus-change" or "caption"?2000:1000)));if(messages.Count>0)await Write(HevcMetadataTransport.EncodeSei(messages));
                    }
                    await Write(nal);
                }
                if(dolby){var rpu=hlg?DolbyMetadataFixtures.Profile84:mode=="dolby-cmv40"||mode is "dolby-change" or "recover" or "kill" or "corrupt" or "cancel"&&index>=fps*2?DolbyMetadataFixtures.Cmv40:DolbyMetadataFixtures.Profile8;await Write(new byte[]{0x7c,1}.Concat(rpu[4..]).ToArray());}index++;
            }
            async Task Write(byte[] nal){await output.WriteAsync(new byte[]{0,0,0,1});await output.WriteAsync(nal);}
        }
        var source=Path.Combine(folder,"coded.mp4");var mux=new List<string>{"-v","error","-y","-r",fps.ToString(),"-f","hevc","-i",patched,"-i",baseFile,"-map","0:v:0","-map","1:a:0"};
        if(dolby)mux.AddRange(["-c:v","libx265","-preset","ultrafast","-threads","1","-pix_fmt","yuv420p10le","-dolbyvision","1","-g",(fps*2).ToString(),"-x265-params","lossless=1:pools=1:frame-threads=1:log-level=error:bframes="+(mode=="dolby-bframes"?"2":"0")+":aud=1:repeat-headers=1:vbv-maxrate=50000:vbv-bufsize=50000"+staticProfile.EncoderOptions]);else mux.AddRange(["-c:v","copy"]);mux.AddRange(["-c:a","copy","-strict","unofficial","-tag:v",mode=="dolby-dvh1"?"dvh1":"hvc1",source]);await Run(ffmpeg,mux);
        if(mode is "dv-profile" or "dv-enhancement")
        {
            var data=await File.ReadAllBytesAsync(source);var offset=data.AsSpan().IndexOf("dvvC"u8);WriteCleanupChecks.Check(offset>=0,"The generated Dolby fixture has no configuration record.");if(mode=="dv-profile")data[offset+6]=(byte)((7<<1)|(data[offset+6]&1));else data[offset+7]|=2;await File.WriteAllBytesAsync(source,data);
        }
        // Re-introduce per-frame static changes after DOVI source encoding, which has a static encoder profile.
        if(dolby&&later&&mode is "recover" or "kill" or "corrupt" or "cancel")
        {
            var doviRaw=Path.Combine(folder,"dovi.hevc");await Run(ffmpeg,["-v","error","-y","-i",source,"-map","0:v:0","-c","copy","-bsf:v","hevc_mp4toannexb,hevc_metadata=aud=insert","-f","hevc",doviRaw]);var changed=Path.Combine(folder,"dovi-changed.hevc");var selected=Enumerable.Range(0,fps*4).Select(i=>new HdrTimedFrame((decimal)i/fps,1m/fps,new(i>=fps*2?staticProfile with{ContentLight="2000,500"}:staticProfile,null,null))).ToArray();await HevcMetadataTransport.PatchAsync(doviRaw,changed,selected,default);var temp=Path.Combine(folder,"dovi-changed.mp4");await Run(ffmpeg,["-v","error","-y","-r",fps.ToString(),"-f","hevc","-i",changed,"-i",baseFile,"-map","0:v","-map","1:a","-c","copy","-strict","unofficial",temp]);
            // The single-video template helper is applied before adding the fixture's audio track.
            var video=Path.Combine(folder,"dovi-video.mp4");await Run(ffmpeg,["-v","error","-y","-r",fps.ToString(),"-f","hevc","-i",changed,"-c","copy",video]);var template=Path.Combine(folder,"dovi-template.mp4");await Run(ffmpeg,["-v","error","-y","-i",source,"-map","0:v:0","-c","copy","-strict","unofficial",template]);await Mp4DolbyConfiguration.CopyAsync(template,video,default);await Run(ffmpeg,["-v","error","-y","-i",video,"-i",baseFile,"-map","0:v","-map","1:a","-c","copy","-strict","unofficial",temp]);File.Move(temp,source,true);
        }
        await Run(ffmpeg,["-v","error","-y","-i",source,"-map","0:v:0","-map","0:a:0","-c","copy","-strict","unofficial","-f","dash","-dash_segment_type","mp4","-seg_duration","2","-use_timeline","0","-init_seg_name","init-$RepresentationID$.m4s","-media_seg_name","chunk-$RepresentationID$-$Number%05d$.m4s",Path.Combine(folder,"source.mpd")]);
        if(mode=="dolby-dvhe")
        {
            // The bundled muxer does not emit the older dvhe tag; retain real HEVC/DOVI data and label its sample entry explicitly.
            var init=Path.Combine(folder,"init-0.m4s");var data=await File.ReadAllBytesAsync(init);var start=0;var found=false;
            while(start<data.Length){var at=data.AsSpan(start).IndexOf("hvc1"u8);if(at<0)break;at+=start;if(at>=4&&BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at-4)) is >=86 and <100000){"dvhe"u8.CopyTo(data.AsSpan(at));found=true;break;}start=at+4;}
            WriteCleanupChecks.Check(found,"The generated HEVC sample entry is missing.");await File.WriteAllBytesAsync(init,data);var mpd=System.Xml.Linq.XDocument.Load(Path.Combine(folder,"source.mpd"));var video=mpd.Descendants().Single(element=>element.Name.LocalName=="AdaptationSet"&&(string?)element.Attribute("contentType")=="video").Descendants().Single(element=>element.Name.LocalName=="Representation");video.SetAttributeValue("codecs","dvhe.08.01");mpd.Save(Path.Combine(folder,"source.mpd"));
        }
        foreach(var role in new[]{"v","a"}){var id=role=="v"?"0":"1";await using var target=File.Create(Path.Combine(folder,"reference-"+role+".mp4"));foreach(var path in new[]{Path.Combine(folder,"init-"+id+".m4s")}.Concat(Directory.GetFiles(folder,"chunk-"+id+"-*.m4s").Order(StringComparer.Ordinal).Take(2))){await using var input=File.OpenRead(path);await input.CopyToAsync(target);}}
    }
    private static byte[] MetadataPlusPayload(string mode,int luminance)
    {
        var bits=new List<int>();void Put(int value,int count){for(var i=count-1;i>=0;i--)bits.Add((value>>i)&1);}var windows=mode=="plus-window"?2:1;Put(mode=="plus-version"?2:1,8);Put(windows,2);
        if(windows==2){foreach(var value in new[]{0,0,160,90,80,45})Put(value,16);Put(0,8);foreach(var value in new[]{80,80,45})Put(value,16);Put(1,1);}
        Put(luminance,27);Put(0,1);for(var window=0;window<windows;window++){for(var i=0;i<3;i++)Put(100000,17);Put(50000,17);Put(0,4);Put(0,10);}Put(0,1);for(var window=0;window<windows;window++){Put(0,1);Put(0,1);}while(bits.Count%8!=0)bits.Add(0);var result=new List<byte>{0xb5,0,0x3c,0,1,4};for(var i=0;i<bits.Count;i+=8){var value=0;for(var j=0;j<8;j++)value=value*2+bits[i+j];result.Add((byte)value);}return result.ToArray();
    }
    private static string MetadataReferenceVideo(string mode)=>MetadataDolby(mode)||MetadataPlus(mode)?"setsar=1,fps=30":"zscale=p=bt2020:t=smpte2084:m=bt2020nc:r=limited:d=error_diffusion,format=yuv420p10le,scale=160:90:force_original_aspect_ratio=decrease:force_divisible_by=2:reset_sar=1,pad=160:90:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30";
    private static async Task MetadataVerify(string ffmpeg,string assets,NativeDownloadResult result,string mode,int duration)
    {
        await ColorAudioVerifyAudio(ffmpeg,assets,result,MetadataPolicy(mode) with{TransitionColorPolicy=null},duration);var probe=Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe");
        async Task<JsonElement[]> Frames(string path){using var doc=JsonDocument.Parse(await Run(probe,["-v","error","-select_streams","v:0","-show_frames","-of","json",path]));return doc.RootElement.GetProperty("frames").EnumerateArray().Select(frame=>frame.Clone()).ToArray();}
        static string Signature(JsonElement frame)=>!frame.TryGetProperty("side_data_list",out _)?"":string.Join('\n',frame.GetProperty("side_data_list").EnumerateArray().Where(side=>side.GetProperty("side_data_type").GetString() is "Mastering display metadata" or "Content light level metadata" or "Dolby Vision Metadata"||side.GetProperty("side_data_type").GetString()!.Contains("HDR Dynamic",StringComparison.Ordinal)).Select(side=>side.ToString()));
        var expected=new List<string>();foreach(var period in new[]{"p0","p1"}){var frames=await Frames(Path.Combine(assets,period,"high","reference-v.mp4"));var length=period=="p0"&&duration==7?3:4;for(var i=0;i<length*30;i++){var index=(int)Math.Floor(i*(frames.Length/4d)/30);expected.Add(Signature(frames[Math.Min(index,frames.Length-1)]));}}
        foreach(var path in result.OutputPaths.Where(path=>Path.GetFileName(path) is "recording.mp4" or "recording.original.mp4" or "recording.video.mp4"))
        {
            var actual=await Frames(path);WriteCleanupChecks.Check(actual.Length==duration*30&&expected.SequenceEqual(actual.Select(Signature)),"Native publication changed the frame-indexed static/dynamic HDR metadata.");if(MetadataDolby(mode)){using var doc=JsonDocument.Parse(await Run(probe,["-v","error","-select_streams","v:0","-show_streams","-of","json",path]));var configuration=doc.RootElement.GetProperty("streams")[0].GetProperty("side_data_list").EnumerateArray().Single(side=>side.GetProperty("side_data_type").GetString()=="DOVI configuration record");WriteCleanupChecks.Check(configuration.GetProperty("dv_profile").GetInt32()==8&&configuration.GetProperty("el_present_flag").GetInt32()==0&&configuration.GetProperty("dv_bl_signal_compatibility_id").GetInt32()==(mode=="dolby-hlg"?4:1),"Publication lost the Dolby Vision track configuration.");}
        }
    }
    private static async Task MetadataRemux(string ffmpeg)
    {
        using var files=new Files();var assets=Path.Combine(files.Directory,"assets");Directory.CreateDirectory(assets);await MetadataAssets(ffmpeg,assets,"p0","dolby");var source=Path.Combine(assets,"coded.mp4");var extracted=Path.Combine(files.Directory,"video.mp4");var remuxed=Path.Combine(files.Directory,"remux.mp4");var embedded=Path.Combine(files.Directory,"embed.mp4");var merged=Path.Combine(files.Directory,"merge.mp4");
        await FfmpegTool.ExtractVideoAsync(ffmpeg,source,extracted,default);await FfmpegTool.RemuxAsync(ffmpeg,extracted,remuxed,default);await FfmpegTool.EmbedAsync(ffmpeg,extracted,embedded,[],null,new Dictionary<string,string>{{"title","Dolby metadata copy"}},default);await FfmpegTool.MergeAsync(ffmpeg,extracted,source,merged,default);
        var probe=Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe");async Task<string> Packets(string path)=>await Run(probe,["-v","error","-select_streams","v:0","-show_packets","-show_data_hash","sha256","-show_entries","packet=data_hash","-of","json",path]);var expected=await Packets(source);
        foreach(var path in new[]{extracted,remuxed,embedded,merged}){await Run(ffmpeg,["-v","error","-xerror","-i",path,"-f","null","-"],true);var actual=await Packets(path);WriteCleanupChecks.Check(actual==expected,"Dolby remux changed coded video packets.");using var document=JsonDocument.Parse(await Run(probe,["-v","error","-select_streams","v:0","-show_streams","-of","json",path]));var record=document.RootElement.GetProperty("streams")[0].GetProperty("side_data_list").EnumerateArray().Single(side=>side.GetProperty("side_data_type").GetString()=="DOVI configuration record");WriteCleanupChecks.Check(record.GetProperty("dv_profile").GetInt32()==8&&record.GetProperty("dv_bl_signal_compatibility_id").GetInt32()==1,"Dolby remux lost its configuration record.");}
    }
}


