using System.Globalization;
using System.Text;
using YoutubeDl.Engine.Download;

namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    internal static async Task<IReadOnlyList<decimal>> JoinConvertedPeriodsAsync(string ffmpeg,IReadOnlyList<(string Path,double Duration)> periods,IReadOnlyList<MediaPeriodEdit> edits,string output,LiveDashSelectionPolicy policy,RetainedMediaCollector? retained,CancellationToken ct)
    {
        policy.Validate();
        if(!policy.NormalizeTransitions||periods.Count!=edits.Count||periods.Count is 0 or >1000)throw new InvalidDataException("The converted DASH period inventory is invalid.");
        if(policy.TransitionColorPolicy is "hdr-metadata" or "hdr-spatial" or "hdr-transform" or "hdr-reconstruct")
        {
            var role=await ProbeStreamsAsync(ffmpeg,periods[0].Path,ct,"identify metadata-aware transition",localMediaOnly:true).ConfigureAwait(false);
            if(role.Count==1&&role[0].Kind=="video")return await JoinMetadataPeriodsAsync(ffmpeg,periods,edits,output,policy,retained,ct).ConfigureAwait(false);
        }
        var directory=Path.GetDirectoryName(output)!;var origins=new List<decimal>();var inventory=new StringBuilder("ffconcat version 1.0\n");ProbedStream? first=null;
        var width=(policy.TransitionHeight*16/9)/2*2;TransitionHdrMetadata? hdrMetadata=null;
        for(var index=0;index<periods.Count;index++)
        {
            ct.ThrowIfCancellationRequested();var edit=edits[index];
            if(edit.Leading<0||edit.Duration<=0||edit.RawDuration<=0||edit.Leading+edit.Duration>edit.RawDuration+0.000001m)throw new InvalidDataException("The transition clipping interval is invalid.");
            var streams=await ProbeStreamsAsync(ffmpeg,periods[index].Path,ct,"inspect DASH decoder transition",localMediaOnly:true).ConfigureAwait(false);
            if(streams.Count!=1||!streams[0].Container.Split(',').Contains("mov")||streams[0].IsCover||streams[0].Kind is not ("video" or "audio"))throw new NotSupportedException("Transition conversion requires one clear MP4 video or audio track per period.");
            var stream=streams[0];first??=stream;
            if(stream.Kind!=first.Kind||stream.StartTime is null)throw new InvalidDataException("The DASH transition changed media role or lost its clock.");
            var audio=stream.Kind=="audio";
            var audioProfile=(Channels:first.Channels,Rate:first.SampleRate,Layout:first.ChannelLayout);var videoFilter="";
            if(audio)
            {
                if(stream.Codec is not ("aac" or "alac" or "flac" or "opus" or "ac3" or "eac3"))throw new NotSupportedException("The audio transition codec is unsupported.");
                audioProfile=TransitionConversionPlan.AudioProfile(policy,new(stream.Channels,stream.SampleRate,stream.ChannelLayout,stream.BitsPerSample),new(first.Channels,first.SampleRate,first.ChannelLayout,first.BitsPerSample));
            }
            else
            {
                if(stream.Codec is not ("h264" or "hevc" or "vp9" or "av1")||stream.Width is <=0 or >7680||stream.Height is <=0 or >4320)throw new NotSupportedException("The video transition codec or dimensions are unsupported.");
                TransitionVideoFacts Facts(ProbedStream value)=>new(value.PixelFormat,value.ColorTransfer,value.ColorPrimaries,value.ColorSpace,value.ColorRange,value.SampleAspect,value.Rotation,value.SimpleDisplay,value.AdvancedHdr);
                videoFilter=TransitionConversionPlan.VideoFilter(policy,Facts(stream),Facts(first),width);
                var metadata=await InspectTransitionMetadataAsync(ffmpeg,periods[index].Path,stream,policy.TransitionColorPolicy is "hdr-preserve" or "bt2020" or "wide-to-bt709",ct).ConfigureAwait(false);
                if(policy.TransitionColorPolicy=="hdr-preserve")
                {
                    if(hdrMetadata is not null&&hdrMetadata!=metadata)throw new NotSupportedException("HDR10 mastering/content-light metadata changed across periods; a single static output profile cannot preserve it.");
                    hdrMetadata??=metadata;
                }
            }
            origins.Add(stream.StartTime.Value);
            if(retained is not null)await retained.CaptureAsync(periods[index].Path,"part","mp4",null,ct).ConfigureAwait(false);
            var name="converted-period-"+index+(audio?".m4a":".mkv");var path=Path.Combine(directory,name);var start=edit.Leading.ToString(CultureInfo.InvariantCulture);var duration=edit.Duration.ToString(CultureInfo.InvariantCulture);
            var args=new List<string>{"-y","-v","error","-xerror","-err_detect","explode","-filter_threads","1","-protocol_whitelist","file,pipe","-enable_drefs","0","-use_absolute_path","0","-i",periods[index].Path,"-map",audio?"0:a:0":"0:v:0","-threads","1"};
            if(audio)args.AddRange(["-af","asetpts=PTS-STARTPTS,atrim=start="+start+":duration="+duration+",asetpts=PTS-STARTPTS"+TransitionConversionPlan.AudioFilter(policy,audioProfile)+",atrim=duration="+duration+",asetpts=PTS-STARTPTS","-c:a","alac","-ar",audioProfile.Rate.ToString(CultureInfo.InvariantCulture),"-ac",audioProfile.Channels.ToString(CultureInfo.InvariantCulture)]);
            else
            {
                args.AddRange(["-vf","setpts=PTS-STARTPTS,trim=start="+start+":duration="+duration+",setpts=PTS-STARTPTS,"+videoFilter,"-c:v","ffv1","-pix_fmt",TransitionConversionPlan.VideoPixel(policy),"-metadata:s:v:0","rotate=0"]);
                if(policy.TransitionColorPolicy is not null){args.AddRange(["-map_metadata","-1"]);args.AddRange(TransitionConversionPlan.VideoColor(policy,first.ColorTransfer));}
            }
            args.Add(path);await RunAsync(ffmpeg,args,ct,rejectDiagnostics:true).ConfigureAwait(false);
            var converted=await ProbeStreamsAsync(ffmpeg,path,ct,"verify converted DASH period",countFrames:true,localMediaOnly:true).ConfigureAwait(false);
            if(converted.Count!=1||converted[0].Kind!=stream.Kind||audio&&(converted[0].Channels!=audioProfile.Channels||converted[0].SampleRate!=audioProfile.Rate)||!audio&&(converted[0].Width!=width||converted[0].Height!=policy.TransitionHeight||converted[0].Rotation!=0||converted[0].SampleAspect!="1:1"||!long.TryParse(converted[0].FrameCount,out var frames)||Math.Abs(frames-(double)edit.Duration*30)>1.01))
                throw new InvalidDataException("A converted DASH period lost its output profile or visible frame timeline.");
            inventory.Append("file '").Append(name).Append("'\nduration ").Append(duration).Append('\n');
        }
        var list=Path.Combine(directory,"converted.ffconcat");await File.WriteAllTextAsync(list,inventory.ToString(),new UTF8Encoding(false),ct).ConfigureAwait(false);
        var final=new List<string>{"-y","-v","error","-xerror","-f","concat","-safe","1","-i",list,"-map","0:0","-threads","1"};
        if(first!.Kind=="audio")final.AddRange(TransitionConversionPlan.AudioEncoder(policy.TransitionAudioCodec,policy.TransitionAudioLayout=="5.1"?6:policy.TransitionAudioLayout=="stereo"?2:policy.TransitionAudioLayout=="mono"?1:first.Channels));
        else
        {
            final.AddRange(TransitionConversionPlan.VideoEncoder(policy.TransitionVideoCodec));final.AddRange(["-pix_fmt",TransitionConversionPlan.VideoPixel(policy),"-metadata:s:v:0","rotate=0"]);
            if(hdrMetadata is not null){var option=final.IndexOf("-x265-params");final[option+1]+=hdrMetadata.EncoderOptions;}
            final.AddRange(TransitionConversionPlan.VideoColor(policy,first.ColorTransfer));
        }
        final.AddRange(["-t",edits.Sum(edit=>edit.Duration).ToString(CultureInfo.InvariantCulture),output]);await RunAsync(ffmpeg,final,ct,rejectDiagnostics:true).ConfigureAwait(false);
        await VerifyLiveRecordingAsync(ffmpeg,output,"mov",(double)edits.Sum(edit=>edit.Duration),ct).ConfigureAwait(false);
        var result=await ProbeStreamsAsync(ffmpeg,output,ct,"verify conversion target",localMediaOnly:true).ConfigureAwait(false);var expected=first.Kind=="audio"?policy.TransitionAudioCodec??"alac":policy.TransitionVideoCodec??"h264";
        var wide=policy.TransitionColorPolicy is "hdr-preserve" or "bt2020";var transfer=policy.TransitionColorPolicy=="hdr-preserve"?first.ColorTransfer:wide?"bt2020-10":"bt709";
        if(result.Count!=1||result[0].Codec!=expected||first.Kind=="video"&&(result[0].Rotation!=0||result[0].SampleAspect!="1:1"||result[0].PixelFormat!=TransitionConversionPlan.VideoPixel(policy)||policy.TransitionColorPolicy is not null&&(result[0].ColorTransfer!=transfer||result[0].ColorPrimaries!=(wide?"bt2020":"bt709")||result[0].ColorSpace!=(wide?"bt2020nc":"bt709")||result[0].ColorRange!="tv"||result[0].AdvancedHdr||policy.TransitionColorPolicy!="hdr-preserve"&&result[0].StaticHdr)))throw new InvalidDataException("The converted stream did not retain its declared codec/color/geometry target.");
        if(first.Kind=="video"&&policy.TransitionColorPolicy is not null)
        {
            var metadata=await InspectTransitionMetadataAsync(ffmpeg,output,result[0],true,ct).ConfigureAwait(false);
            if(metadata!=(hdrMetadata??new TransitionHdrMetadata()))throw new InvalidDataException("The converted output did not retain its declared static HDR metadata profile.");
        }
        return origins;
    }
}

