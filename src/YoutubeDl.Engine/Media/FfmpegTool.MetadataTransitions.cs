using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using YoutubeDl.Engine.Download;

namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    private sealed class HdrBudget
    {
        private readonly Dictionary<HdrFrameMetadata,HdrFrameMetadata> _known=[];private long _bytes;internal int SourceFrames;internal bool Broader;internal bool MeasuredPlus;
        internal HdrFrameMetadata Intern(HdrFrameMetadata value)
        {
            if(_known.TryGetValue(value,out var previous))return previous;
            if(_known.Count>=4096||(_bytes+=2L*((value.Plus?.Length??0)+(value.Dolby?.Length??0)+256))>64*1024*1024)throw new InvalidDataException("The HDR metadata inventory exceeds its supported size.");_known.Add(value,value);return value;
        }
    }
    private static async Task<List<HdrTimedFrame>> ReadHdrFramesAsync(string ffmpeg,string input,ProbedStream stream,HdrBudget budget,CancellationToken ct)
    {
        var start=new ProcessStartInfo(RequireProbe(ffmpeg,"inspect metadata-aware HDR frames")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"-v","error","-threads","1","-max_alloc","134217728","-protocol_whitelist","file,pipe","-f","mov","-enable_drefs","0","-use_absolute_path","0","-select_streams","v:0","-show_frames","-show_entries","frame=pkt_pos,best_effort_timestamp_time,duration_time,pix_fmt,color_range,color_space,color_primaries,color_transfer,interlaced_frame:frame_side_data","-of","xml",input})start.ArgumentList.Add(arg);
        ct.ThrowIfCancellationRequested();using var process=new Process{StartInfo=start};if(!process.Start())throw new InvalidOperationException("Could not inspect HDR metadata.");using var lifetime=ProcessLifetime.Attach(process);using var cancel=ct.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}});var errors=ReadTailAsync(process.StandardError);var frames=new List<HdrTimedFrame>();
        try
        {
            using var reader=XmlReader.Create(process.StandardOutput,new XmlReaderSettings{Async=true,DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,IgnoreWhitespace=true,MaxCharactersInDocument=512L*1024*1024});
            while(!reader.EOF)
            {
                ct.ThrowIfCancellationRequested();if(reader.NodeType!=XmlNodeType.Element||reader.LocalName!="frame"){await reader.ReadAsync().ConfigureAwait(false);continue;}
                if(++budget.SourceFrames>HdrMetadataTimeline.MaximumFrames)throw new InvalidDataException("Metadata-aware HDR exceeds its supported frame inventory.");
                var frame=(XElement)await XNode.ReadFromAsync(reader,ct).ConfigureAwait(false);
                static string Normalize(string? value)=>value is null or "unknown"?"":value;
                if((string?)frame.Attribute("pix_fmt")!=stream.PixelFormat||Normalize((string?)frame.Attribute("color_transfer"))!=Normalize(stream.ColorTransfer)||Normalize((string?)frame.Attribute("color_primaries"))!=Normalize(stream.ColorPrimaries)||Normalize((string?)frame.Attribute("color_space"))!=Normalize(stream.ColorSpace)||(string?)frame.Attribute("color_range")!=stream.ColorRange||(string?)frame.Attribute("interlaced_frame")!="0")throw new NotSupportedException("Metadata-aware HDR requires stable progressive color/sample facts within each period.");
                var time=HdrMetadataTimeline.Decimal(frame,"best_effort_timestamp_time");var duration=HdrMetadataTimeline.Decimal(frame,"duration_time");if(duration is <=0 or >1||frames.Count>0&&time<=frames[^1].Time)throw new InvalidDataException("The HDR frame clock is invalid.");var metadata=HdrFrameMetadata.Read(frame,budget.Broader);if(budget.MeasuredPlus&&metadata.Plus is not null)metadata=metadata with{Plus="<measured/>"};frames.Add(new(time,duration,budget.Intern(metadata),long.TryParse((string?)frame.Attribute("pkt_pos"),out var position)?position:-1));
            }
            await process.WaitForExitAsync(ct).ConfigureAwait(false);var diagnostic=await errors.ConfigureAwait(false);if(process.ExitCode!=0||diagnostic.Length>0||frames.Count==0)throw new InvalidDataException("HDR frame inspection failed: "+Trim(diagnostic));return frames;
        }
        catch{await ProcessLifetime.KillAndDrainAsync(process,null,errors).ConfigureAwait(false);ct.ThrowIfCancellationRequested();throw;}
    }
    private static async Task ReadHdrPayloadsAsync(string ffmpeg,string input,string raw,IReadOnlyList<HdrTimedFrame> frames,ProbedStream stream,bool plus,CancellationToken ct)
    {
        if(stream.Codec!="hevc"||plus&&stream.BFrames!=0)throw new NotSupportedException("HDR10+ payload retiming currently requires HEVC without reordered pictures.");
        await RunAsync(ffmpeg,["-y","-v","error","-protocol_whitelist","file,pipe","-enable_drefs","0","-use_absolute_path","0","-i",input,"-map","0:v:0","-c","copy","-bsf:v","hevc_mp4toannexb,hevc_metadata=aud=insert","-f","hevc",raw],ct,rejectDiagnostics:true).ConfigureAwait(false);
        var index=0;byte[]? current=null;var bytes=0;
        await foreach(var unit in HevcMetadataTransport.AccessUnits(raw,ct))
        {
            if(unit.Any(nal=>HevcMetadataTransport.Type(nal)==63||(nal[0]&1)!=0||(nal[1]>>3)!=0))throw new NotSupportedException("Dynamic HDR enhancement-layer NAL units are unsupported.");
            if(!plus){index++;continue;}
            if(index>=frames.Count)throw new InvalidDataException("The HDR10+ coded and decoded frame inventories differ.");var values=unit.SelectMany(HevcMetadataTransport.Sei).Where(item=>item.Type==4&&HevcMetadataTransport.HdrPlus(item.Data)).ToArray();
            if(values.Length>1)throw new NotSupportedException("Duplicate HDR10+ payloads in an access unit are unsupported.");if(values.Length==1){current=values[0].Data;HevcMetadataTransport.ValidatePlus(current);if((bytes+=current.Length)>32*1024*1024)throw new InvalidDataException("HDR10+ payload inventory exceeds its size limit.");}
            if(frames[index].Metadata.Plus is not null){if(current is null)throw new InvalidDataException("Decoded HDR10+ metadata has no corresponding coded payload.");frames[index].PlusPayload=current;}else if(values.Length>0)throw new InvalidDataException("The HDR10+ payload was not decoded completely.");index++;
        }
        if(index!=frames.Count)throw new InvalidDataException("The HDR10+ coded timeline is incomplete.");
    }
    private static async Task<IReadOnlyList<decimal>> JoinMetadataPeriodsAsync(string ffmpeg,IReadOnlyList<(string Path,double Duration)> periods,IReadOnlyList<MediaPeriodEdit> edits,string output,LiveDashSelectionPolicy policy,RetainedMediaCollector? retained,CancellationToken ct)
    {
        if(policy.TransitionColorPolicy=="hdr-reconstruct")return await JoinReconstructedHdrPeriodsAsync(ffmpeg,periods,edits,output,policy,retained,ct).ConfigureAwait(false);
        if(policy.TransitionColorPolicy is "hdr-spatial" or "hdr-transform")return await JoinSpatialHdrPeriodsAsync(ffmpeg,periods,edits,output,policy,retained,ct).ConfigureAwait(false);
        var directory=Path.GetDirectoryName(output)!;var origins=new List<decimal>();var expected=new List<HdrTimedFrame>();var budget=new HdrBudget();var inventory=new StringBuilder("ffconcat version 1.0\n");ProbedStream? first=null;TransitionDolbyConfiguration? dolby=null;var hadMaster=false;var hadLight=false;var hadPlus=false;
        var width=policy.TransitionHeight*16/9/2*2;
        for(var index=0;index<periods.Count;index++)
        {
            var streams=await ProbeStreamsAsync(ffmpeg,periods[index].Path,ct,"inspect HDR transition",localMediaOnly:true).ConfigureAwait(false);if(streams.Count!=1||streams[0].Kind!="video"||!streams[0].Container.Split(',').Contains("mov")||streams[0].Codec is not ("hevc" or "h264" or "av1" or "vp9"))throw new NotSupportedException("Metadata-aware HDR requires one supported clear MP4 video track.");var stream=streams[0];first??=stream;
            if(stream.Width is <=0 or >7680||stream.Height is <=0 or >4320||stream.StartTime is null)throw new InvalidDataException("Invalid HDR source dimensions or clock.");var edit=edits[index];if(edit.Leading<0||edit.Duration<=0||edit.Leading+edit.Duration>edit.RawDuration+0.000001m)throw new InvalidDataException("Invalid HDR clipping interval.");
            TransitionVideoFacts Facts(ProbedStream value)=>new(value.PixelFormat,value.ColorTransfer,value.ColorPrimaries,value.ColorSpace,value.ColorRange,value.SampleAspect,value.Rotation,value.SimpleDisplay,value.AdvancedHdr);
            var filter=TransitionConversionPlan.VideoFilter(policy,Facts(stream),Facts(first),width);var frames=await ReadHdrFramesAsync(ffmpeg,periods[index].Path,stream,budget,ct).ConfigureAwait(false);var hasDolby=frames.Any(frame=>frame.Metadata.Dolby is not null);var hasPlus=frames.Any(frame=>frame.Metadata.Plus is not null);
            if(hasDolby!=(stream.Dolby is not null)||hasDolby&&hasPlus||index>0&&hasDolby!=(dolby is not null))throw new NotSupportedException("Changing or incomplete Dolby Vision track signaling is unsupported.");
            if(hasDolby||hasPlus)
            {
                if(stream.Codec!="hevc"||stream.PixelFormat!="yuv420p10le"||stream.ColorRange!="tv"||stream.Width!=width||stream.Height!=policy.TransitionHeight||stream.Rotation!=0||stream.SampleAspect!="1:1"||!stream.SimpleDisplay)throw new NotSupportedException("Dynamic HDR re-encoding requires unchanged ten-bit limited-range HEVC geometry and square pixels.");filter="setsar=1,fps=30";
            }
            if(hasDolby){stream.Dolby!.Validate(stream.ColorTransfer);if(frames.Any(frame=>frame.Metadata.Dolby is null)||index>0&&stream.Dolby!=dolby)throw new NotSupportedException("Dolby Vision configuration or per-frame metadata changed incompatibly.");dolby=stream.Dolby;}
            if(hasPlus||hasDolby)await ReadHdrPayloadsAsync(ffmpeg,periods[index].Path,Path.Combine(directory,"metadata-source-"+index+".hevc"),frames,stream,hasPlus,ct).ConfigureAwait(false);
            var projected=HdrMetadataTimeline.Project(frames,edit);if(expected.Count+projected.Count>HdrMetadataTimeline.MaximumFrames)throw new InvalidDataException("The HDR output frame inventory exceeds its limit.");
            foreach(var frame in projected)
            {
                if(hadMaster&&frame.Metadata.Static.MasterDisplay is null||hadLight&&frame.Metadata.Static.ContentLight is null||hadPlus&&frame.Metadata.Plus is null)throw new NotSupportedException("Removing an already active HDR metadata family requires a supported reset sequence.");hadMaster|=frame.Metadata.Static.MasterDisplay is not null;hadLight|=frame.Metadata.Static.ContentLight is not null;hadPlus|=frame.Metadata.Plus is not null;
            }
            expected.AddRange(projected);origins.Add(stream.StartTime.Value);if(retained is not null)await retained.CaptureAsync(periods[index].Path,"part","mp4",null,ct).ConfigureAwait(false);
            var encoded=Path.Combine(directory,"metadata-encoded-"+index+".mp4");var raw=Path.Combine(directory,"metadata-encoded-"+index+".hevc");var patched=Path.Combine(directory,"metadata-patched-"+index+".hevc");var name="metadata-period-"+index+".mp4";var path=Path.Combine(directory,name);
            var args=new List<string>{"-y","-v","error","-xerror","-err_detect","explode","-filter_threads","1","-protocol_whitelist","file,pipe","-enable_drefs","0","-use_absolute_path","0","-i",periods[index].Path,"-map","0:v:0","-map_metadata","-1","-map_chapters","-1","-vf","setpts=PTS-STARTPTS,trim=start="+edit.Leading.ToString(CultureInfo.InvariantCulture)+":duration="+edit.Duration.ToString(CultureInfo.InvariantCulture)+",setpts=PTS-STARTPTS,"+filter,"-threads","1","-c:v","libx265","-preset","fast","-pix_fmt","yuv420p10le","-dolbyvision",hasDolby?"1":"0","-x265-params","lossless=1:pools=1:frame-threads=1:log-level=error:bframes=0:aud=1:repeat-headers=1"+(hasDolby?":vbv-maxrate=50000:vbv-bufsize=50000":"")+projected[0].Metadata.Static.EncoderOptions,"-tag:v","hvc1","-strict","unofficial"};args.AddRange(TransitionConversionPlan.VideoColor(policy,first.ColorTransfer));args.Add(encoded);await RunAsync(ffmpeg,args,ct,rejectDiagnostics:true).ConfigureAwait(false);
            await RunAsync(ffmpeg,["-y","-v","error","-i",encoded,"-map","0:v:0","-c","copy","-bsf:v","hevc_mp4toannexb,hevc_metadata=aud=insert","-f","hevc",raw],ct,rejectDiagnostics:true).ConfigureAwait(false);
            await HevcMetadataTransport.PatchAsync(raw,patched,projected,ct).ConfigureAwait(false);
            await RunAsync(ffmpeg,["-y","-v","error","-fflags","+genpts","-r","30","-f","hevc","-i",patched,"-map","0:v:0","-c","copy","-tag:v","hvc1","-strict","unofficial",path],ct,rejectDiagnostics:true).ConfigureAwait(false);
            if(hasDolby)await Mp4DolbyConfiguration.CopyAsync(encoded,path,ct).ConfigureAwait(false);
            await VerifyHdrTimelineAsync(ffmpeg,path,projected,policy,dolby,ct).ConfigureAwait(false);
            inventory.Append("file '").Append(name).Append("'\nduration ").Append(edit.Duration.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        var list=Path.Combine(directory,"metadata.ffconcat");await File.WriteAllTextAsync(list,inventory.ToString(),new UTF8Encoding(false),ct).ConfigureAwait(false);
        await RunAsync(ffmpeg,["-y","-v","error","-f","concat","-safe","1","-auto_convert","0","-i",list,"-map","0:v:0","-c","copy","-strict","unofficial",output],ct,rejectDiagnostics:true).ConfigureAwait(false);
        await VerifyLiveRecordingAsync(ffmpeg,output,"mov",(double)edits.Sum(edit=>edit.Duration),ct).ConfigureAwait(false);await VerifyHdrTimelineAsync(ffmpeg,output,expected,policy,dolby,ct).ConfigureAwait(false);return origins;
    }
    private static async Task VerifyHdrTimelineAsync(string ffmpeg,string path,IReadOnlyList<HdrTimedFrame> expected,LiveDashSelectionPolicy policy,TransitionDolbyConfiguration? dolby,CancellationToken ct)
    {
        var streams=await ProbeStreamsAsync(ffmpeg,path,ct,"verify HDR metadata output",localMediaOnly:true).ConfigureAwait(false);
        if(streams.Count!=1||streams[0] is not {Kind:"video",Codec:"hevc",PixelFormat:"yuv420p10le",ColorRange:"tv",ColorPrimaries:"bt2020",ColorSpace:"bt2020nc",Rotation:0,SampleAspect:"1:1"} stream||stream.Width!=policy.TransitionHeight*16/9/2*2||stream.Height!=policy.TransitionHeight||stream.Dolby!=dolby)throw new InvalidDataException("The HDR output lost its codec, color, geometry or Dolby configuration.");
        var frames=await ReadHdrFramesAsync(ffmpeg,path,stream,new(),ct).ConfigureAwait(false);if(frames.Count!=expected.Count)throw new InvalidDataException("The HDR output changed its frame timeline.");for(var i=0;i<frames.Count;i++)if(frames[i].Metadata!=expected[i].Metadata||Math.Abs(frames[i].Time-frames[0].Time-i/30m)>0.000002m)throw new InvalidDataException("HDR output frame metadata or timing differs at frame "+i+".");
    }
}
