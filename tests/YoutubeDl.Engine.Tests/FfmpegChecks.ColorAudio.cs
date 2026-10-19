using System.Buffers.Binary;
using System.Text.Json;
using YoutubeDl.Engine;

internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> ColorAudioMediaChecks(string ffmpeg)=>new[]{"h264","hevc","vp9","av1","aac","flac","opus","aac-surround","opus-surround","mono-stereo","surround-stereo","stereo-surround","resample","bt709","bt601","full-range","sdr10","pq","hlg","sar-wide","sar-narrow","rotate90","rotate180","rotate270","clip-color","caption","recover","kill","corrupt","unknown-color","hdr-disabled","geometry-disabled","audio-disabled","bad-rotation","cancel","eac3"}
        .Select(mode=>("Color audio media "+mode,(Func<Task>)(()=>WpfLadderMedia(ffmpeg,"convert-"+mode))));
    private static bool ColorAudioHdr(string mode)=>mode is "pq" or "hlg" or "clip-color" or "caption" or "recover" or "kill" or "corrupt" or "cancel" or "hdr-disabled";
    private static bool ColorAudioColor(string mode)=>ColorAudioHdr(mode)||mode is "bt709" or "bt601" or "full-range" or "sdr10" or "unknown-color";
    private static LiveDashSelectionPolicy ColorAudioPolicy(string mode)=>new()
    {
        NormalizeTransitions=true,AdaptiveQualityLadder=true,TransitionHeight=90,
        TransitionVideoCodec=mode is "hevc" or "vp9" or "av1" or "h264"?mode:mode is "recover" or "kill"?"hevc":null,
        TransitionAudioCodec=mode is "aac" or "flac" or "opus"?mode:mode=="aac-surround"?"aac":mode=="opus-surround"?"opus":mode is "recover" or "kill"?"flac":null,
        TransitionAudioLayout=mode=="audio-disabled"?null:mode is "stereo-surround" or "aac-surround" or "opus-surround"?"5.1":"stereo",TransitionAudioSampleRate=mode=="audio-disabled"?0:48000,
        NormalizeVideoGeometry=mode!="geometry-disabled",TransitionColorPolicy=mode=="hdr-disabled"?null:ColorAudioHdr(mode)?"hdr-to-bt709":ColorAudioColor(mode)?"bt709":null,HdrPeakNits=ColorAudioHdr(mode)&&mode!="hdr-disabled"?1000:0
    };
    private static async Task ColorAudioAssets(string ffmpeg,string folder,string period,string mode)
    {
        var later=period=="p1";var surround=later&&(mode is "surround-stereo" or "eac3" or "caption" or "recover" or "kill" or "corrupt" or "cancel" or "audio-disabled");var stereo=mode=="stereo-surround"?!later:later&&!surround;var channels=surround?6:stereo?2:1;var layout=surround?"5.1":stereo?"stereo":"mono";var rate=later?48000:44100;
        var expressions=Enumerable.Range(0,channels).Select(index=>"0.05*sin(2*PI*"+((later?600:300)+index*100)+"*t)");var audio="aevalsrc="+string.Join('|',expressions)+":s="+rate+":c="+layout;var hdr=later&&ColorAudioHdr(mode);var ten=hdr||later&&mode=="sdr10";var codec=hdr?"libx265":"libx264";var fps=later?15:10;
        var args=new List<string>{"-v","error","-y","-f","lavfi","-i","testsrc2=size="+(later?"160x90":"128x72")+":rate="+fps,"-f","lavfi","-i",audio,"-t","4","-c:v",codec,"-threads","1","-g",(fps*2).ToString(),"-bf","0","-preset","ultrafast","-pix_fmt",ten?"yuv420p10le":"yuv420p"};
        if(hdr)args.AddRange(["-x265-params","pools=1:frame-threads=1:log-level=error:master-display=G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,50):max-cll=1000,400","-color_primaries","bt2020","-color_trc",mode=="hlg"?"arib-std-b67":"smpte2084","-colorspace","bt2020nc","-color_range","tv"]);
        else if(ColorAudioColor(mode)&&!(later&&mode=="unknown-color")){var s=mode=="bt601"&&later?"smpte170m":"bt709";args.AddRange(["-color_primaries",s,"-color_trc",s,"-colorspace",s,"-color_range",mode=="full-range"&&later?"pc":"tv"]);}
        var filters=new List<string>();
        if(hdr)filters.Add("setparams=range=limited:color_primaries=bt2020:color_trc="+(mode=="hlg"?"arib-std-b67":"smpte2084")+":colorspace=bt2020nc");
        else if(ColorAudioColor(mode)&&!(later&&mode=="unknown-color")){var signal=mode=="bt601"&&later?"smpte170m":"bt709";filters.Add("setparams=range="+(mode=="full-range"&&later?"full":"limited")+":color_primaries="+signal+":color_trc="+signal+":colorspace="+signal);}
        if(later&&mode is "sar-wide" or "sar-narrow" or "geometry-disabled")filters.Add("setsar="+(mode=="sar-narrow"?"1/2":"2/1"));if(filters.Count>0)args.AddRange(["-vf",string.Join(',',filters)]);
        args.AddRange(["-c:a",later&&mode=="eac3"?"eac3":"alac","-strict","-2","-f","dash","-dash_segment_type","mp4","-seg_duration","2","-use_timeline","0","-init_seg_name","init-$RepresentationID$.m4s","-media_seg_name","chunk-$RepresentationID$-$Number%05d$.m4s",Path.Combine(folder,"source.mpd")]);await Run(ffmpeg,args);
        if(later&&mode is "rotate90" or "rotate180" or "rotate270" or "bad-rotation")
        {
            var path=Path.Combine(folder,"init-0.m4s");var bytes=await File.ReadAllBytesAsync(path);var marker=bytes.AsSpan().IndexOf("tkhd"u8);WriteCleanupChecks.Check(marker>=0,"Generated video has no track header.");var data=marker+4;var matrix=data+(bytes[data]==1?52:40);var angle=mode=="bad-rotation"?45:int.Parse(mode[6..]);var a=(int)Math.Round(Math.Cos(angle*Math.PI/180)*65536);var b=(int)Math.Round(Math.Sin(angle*Math.PI/180)*65536);var values=new[]{a,-b,0,b,a,0,0,0,1073741824};for(var i=0;i<9;i++)BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(matrix+i*4,4),values[i]);await File.WriteAllBytesAsync(path,bytes);
        }
        foreach(var role in new[]{"v","a"})
        {
            var id=role=="v"?"0":"1";await using var output=File.Create(Path.Combine(folder,"reference-"+role+".mp4"));foreach(var path in new[]{Path.Combine(folder,"init-"+id+".m4s")}.Concat(Directory.GetFiles(folder,"chunk-"+id+"-*.m4s").Order(StringComparer.Ordinal).Take(2))){await using var source=File.OpenRead(path);await source.CopyToAsync(output);}
        }
    }
    private static string ColorAudioReferenceVideo(string mode,string period)
    {
        var color="";
        if(ColorAudioColor(mode))
        {
            color=period=="p1"&&ColorAudioHdr(mode)?"zscale=t=linear:npl=100,format=gbrpf32le,tonemap=tonemap=mobius:param=0.3:desat=2:peak=10,zscale=p=bt709:t=bt709:m=bt709:r=limited:d=error_diffusion,format=yuv420p,":"zscale=p=bt709:t=bt709:m=bt709:r=limited:d=error_diffusion,format=yuv420p,";
        }
        return color+"scale=160:90:force_original_aspect_ratio=decrease:force_divisible_by=2:reset_sar=1,pad=160:90:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30";
    }
    private static async Task ColorAudioVerifyAudio(string ffmpeg,string assets,NativeDownloadResult result,LiveDashSelectionPolicy policy,int duration)
    {
        var component=result.OutputPaths.Single(path=>Path.GetFileName(path)=="recording.audio.m4a");var lossless=policy.TransitionAudioCodec is null or "alac" or "flac";var reference=component;
        if(lossless)
        {
            var normalized=new List<string>();var layout=policy.TransitionAudioLayout!;
            foreach(var period in new[]{"p0","p1"})
            {
                var length=period=="p0"&&duration==7?3:4;var path=Path.Combine(assets,"reference-"+period+".m4a");await Run(ffmpeg,["-v","error","-y","-i",Path.Combine(assets,period,"high","reference-a.mp4"),"-map","0:a:0","-af","asetpts=PTS-STARTPTS,atrim=duration="+length+",asetpts=PTS-STARTPTS,aresample=48000:out_chlayout="+layout+":rematrix_maxval=1,atrim=duration="+length+",asetpts=PTS-STARTPTS","-c:a","alac",path]);normalized.Add(path);
            }
            reference=Path.Combine(assets,"reference-audio.m4a");await Run(ffmpeg,["-v","error","-y","-i",normalized[0],"-i",normalized[1],"-filter_complex","[0:a][1:a]concat=n=2:v=0:a=1[out]","-map","[out]","-c:a","alac",reference]);
        }
        async Task<string> Digest(string path)=>await Run(ffmpeg,["-v","error","-i",path,"-map","0:a:0","-af","atrim=duration="+duration,"-c:a","pcm_f64le","-f","hash","-"]);
        var expected=await Digest(reference);var actual=await Digest(result.OutputPath!);WriteCleanupChecks.Check(expected.Trim()==actual.Trim(),"Fixed-layout conversion or publication changed the reference decoded audio samples.");
        var probe=Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe");using var doc=JsonDocument.Parse(await Run(probe,["-v","error","-show_entries","stream=codec_type,codec_name,channels,channel_layout,sample_rate,color_transfer,color_primaries,color_space,color_range,sample_aspect_ratio,width,height:stream_side_data=side_data_type,rotation","-of","json",result.OutputPath!]));var streams=doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();var video=streams.Single(stream=>stream.GetProperty("codec_type").GetString()=="video");var audio=streams.Single(stream=>stream.GetProperty("codec_type").GetString()=="audio");
        WriteCleanupChecks.Check(video.GetProperty("codec_name").GetString()==(policy.TransitionVideoCodec??"h264")&&video.GetProperty("sample_aspect_ratio").GetString()=="1:1"&&audio.GetProperty("codec_name").GetString()==(policy.TransitionAudioCodec??"alac")&&audio.GetProperty("channels").GetInt32()==(policy.TransitionAudioLayout=="5.1"?6:2)&&audio.GetProperty("sample_rate").GetString()=="48000","Published codec/layout/aspect target differs from the explicit policy.");
        if(policy.TransitionColorPolicy is not null)WriteCleanupChecks.Check(video.GetProperty("color_transfer").GetString()=="bt709"&&video.GetProperty("color_primaries").GetString()=="bt709"&&video.GetProperty("color_space").GetString()=="bt709"&&video.GetProperty("color_range").GetString()=="tv","Tone/color conversion did not publish BT.709 limited-range SDR tags.");
    }
}
