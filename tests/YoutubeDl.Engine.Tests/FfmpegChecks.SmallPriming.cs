using System.Globalization;
using System.Text.Json;
using YoutubeDl.Engine.Media;

internal static partial class FfmpegChecks
{
    private static async Task SmallDelayedPriming(string ffmpeg)
    {
        using var files=new Files();var video=Path.Combine(files.Directory,"video.mp4");var audio=Path.Combine(files.Directory,"audio.m4a");var output=Path.Combine(files.Directory,"delayed.mp4");
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=25","-t","1","-c:v","libx264","-preset","ultrafast",video]);
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","sine=frequency=700:sample_rate=48000","-t","1","-c:a","aac",audio]);
        await FfmpegTool.MergeLiveAsync(ffmpeg,video,audio,output,-0.01m,1.01,default);await Run(ffmpeg,["-v","error","-xerror","-i",output,"-f","null","-"],true);
        using var probe=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-select_streams","a:0","-show_entries","stream=start_time","-of","json",output]));
        var start=decimal.Parse(probe.RootElement.GetProperty("streams")[0].GetProperty("start_time").GetString()!,CultureInfo.InvariantCulture);
        if(Math.Abs(start-0.01m)>0.001m)throw new InvalidOperationException("A sub-priming audio delay lost its presentation clock.");
        var sourcePackets=await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-select_streams","a:0","-show_packets","-show_data_hash","sha256","-show_entries","packet=data_hash","-of","json",audio]);
        var mergedPackets=await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-select_streams","a:0","-show_packets","-show_data_hash","sha256","-show_entries","packet=data_hash","-of","json",output]);
        using var a=JsonDocument.Parse(sourcePackets);using var b=JsonDocument.Parse(mergedPackets);
        var expected=a.RootElement.GetProperty("packets").EnumerateArray().Select(p=>p.GetProperty("data_hash").GetString()).ToArray();var actual=b.RootElement.GetProperty("packets").EnumerateArray().Select(p=>p.GetProperty("data_hash").GetString()).ToArray();
        if(!expected.SequenceEqual(actual))throw new InvalidOperationException("Short-delay priming correction changed coded AAC packets.");
        var captions=Path.Combine(files.Directory,"captions.vtt");var embedded=Path.Combine(files.Directory,"embedded.mp4");await File.WriteAllTextAsync(captions,"WEBVTT\n\n00:00:00.000 --> 00:00:01.000\nCaption\n\n");
        await Run(ffmpeg,["-v","error","-y","-i",output,"-i",captions,"-map","0:v:0","-map","0:a:0","-map","1:0","-c:v","copy","-c:a","copy","-c:s","mov_text",embedded]);
        var plan=await Mp4AudioPriming.ReadPlanAsync(output,default)??throw new InvalidOperationException("Delayed priming plan was not retained.");await Mp4AudioPriming.ApplyAsync(embedded,plan.Samples,plan.SampleRate,default,plan.DelaySeconds);
        await Run(ffmpeg,["-v","error","-xerror","-i",embedded,"-f","null","-"],true);
        using var after=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-select_streams","a:0","-show_entries","stream=start_time","-of","json",embedded]));
        var embeddedStart=decimal.Parse(after.RootElement.GetProperty("streams")[0].GetProperty("start_time").GetString()!,CultureInfo.InvariantCulture);
        if(Math.Abs(embeddedStart-0.01m)>0.001m)throw new InvalidOperationException("Caption embedding changed the delayed audio clock.");
    }
}
