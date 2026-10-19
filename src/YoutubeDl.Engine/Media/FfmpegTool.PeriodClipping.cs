using System.Globalization;
using System.Text;
using YoutubeDl.Engine.Download;

namespace YoutubeDl.Engine.Media;

internal sealed record MediaPeriodEdit(decimal RawDuration,decimal Leading,decimal Duration);
public static partial class FfmpegTool
{
    internal static async Task<IReadOnlyList<decimal>> JoinClippedPeriodsAsync(string ffmpeg,IReadOnlyList<(string Path,double Duration)> periods,IReadOnlyList<MediaPeriodEdit> edits,string output,RetainedMediaCollector? retained,CancellationToken ct)
    {
        if(periods.Count!=edits.Count||periods.Count is 0 or >1000)throw new InvalidDataException("The clipped period inventory is invalid.");
        var first=await ProbeStreamsAsync(ffmpeg,periods[0].Path,ct,"inspect clipped media",localMediaOnly:true).ConfigureAwait(false);
        if(first.Count!=1||!first[0].Container.Split(',').Contains("mov")||first[0].Kind=="video"&&first[0].Codec is not ("h264" or "hevc" or "vp9" or "av1")||first[0].Kind=="audio"&&first[0].Codec is not ("aac" or "alac" or "flac" or "opus")||first[0].Kind is not ("audio" or "video"))throw new NotSupportedException("Decoded period clipping requires a supported single clear MP4 video/audio track.");
        var audio=first[0].Kind=="audio";var directory=Path.GetDirectoryName(output)!;var normalized=new List<string>();
        if(!audio&&first[0].PixelFormat is not ("yuv420p" or "yuv422p" or "yuv444p" or "yuvj420p" or "yuvj422p" or "yuvj444p" or "yuv420p10le" or "yuv422p10le" or "yuv444p10le")||audio&&first[0].Codec is "alac" or "flac"&&first[0].BitsPerSample>24)throw new NotSupportedException("The clipped source exceeds the supported lossless pixel/sample formats.");
        var list=new StringBuilder("ffconcat version 1.0\n");
        var origins=new List<decimal>();
        for(var index=0;index<periods.Count;index++)
        {
            ct.ThrowIfCancellationRequested();var edit=edits[index];if(edit.Leading<0||edit.Duration<=0||edit.Leading+edit.Duration>edit.RawDuration+0.000001m)throw new InvalidDataException("The clipping interval is invalid.");
            var streams=index==0?first:await ProbeStreamsAsync(ffmpeg,periods[index].Path,ct,"inspect clipped period continuity",localMediaOnly:true).ConfigureAwait(false);
            if(streams.Count!=1||streams[0].Kind!=first[0].Kind||streams[0].Codec!=first[0].Codec||streams[0].Width!=first[0].Width||streams[0].Height!=first[0].Height||streams[0].PixelFormat!=first[0].PixelFormat||streams[0].SampleRate!=first[0].SampleRate||streams[0].ChannelLayout!=first[0].ChannelLayout||streams[0].Channels!=first[0].Channels||streams[0].BitsPerSample!=first[0].BitsPerSample)throw new NotSupportedException("The clipped period changed its decoder or sample layout.");
            origins.Add(streams[0].StartTime??throw new InvalidDataException("The clipped source period has no usable media clock."));
            var name="clipped-period-"+index+(audio?".m4a":".mkv");var path=Path.Combine(directory,name);normalized.Add(path);
            if(retained is not null)await retained.CaptureAsync(periods[index].Path,"part","mp4",null,ct).ConfigureAwait(false);
            var start=edit.Leading.ToString(CultureInfo.InvariantCulture);var duration=edit.Duration.ToString(CultureInfo.InvariantCulture);
            var args=new List<string>{"-y","-v","error","-i",periods[index].Path,"-map",audio?"0:a:0":"0:v:0"};
            if(audio)args.AddRange(["-af","asetpts=PTS-STARTPTS,atrim=start="+start+":duration="+duration+",asetpts=PTS-STARTPTS","-c:a","alac"]);
            else args.AddRange(["-vf","setpts=PTS-STARTPTS,trim=start="+start+":duration="+duration+",setpts=PTS-STARTPTS","-c:v","ffv1","-pix_fmt",first[0].PixelFormat]);
            args.Add(path);await RunAsync(ffmpeg,args,ct,rejectDiagnostics:true).ConfigureAwait(false);
            list.Append("file '").Append(name).Append("'\nduration ").Append(duration).Append('\n');
        }
        var listPath=Path.Combine(directory,"clipped.ffconcat");await File.WriteAllTextAsync(listPath,list.ToString(),new UTF8Encoding(false),ct).ConfigureAwait(false);
        var final=new List<string>{"-y","-v","error","-f","concat","-safe","1","-i",listPath,"-map","0:0"};
        if(audio)final.AddRange(first[0].Codec is "alac" or "flac"?["-c:a","alac"]:["-c:a","aac","-b:a","256k"]);
        else final.AddRange(["-c:v","libx264","-preset","fast","-qp","0","-pix_fmt",first[0].PixelFormat]);
        final.AddRange(["-t",edits.Sum(e=>e.Duration).ToString(CultureInfo.InvariantCulture),output]);
        await RunAsync(ffmpeg,final,ct,rejectDiagnostics:true).ConfigureAwait(false);
        return origins;
    }
}
