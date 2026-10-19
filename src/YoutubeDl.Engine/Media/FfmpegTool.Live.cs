using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    internal static async Task MergeLiveAsync(string ffmpeg,string video,string audio,string output,decimal offset,double duration,CancellationToken ct,bool experimentalAudio=false)
    {
        if(!double.IsFinite(duration)||duration<=0||Math.Abs(offset)>86400)throw new InvalidDataException("The paired recording has invalid timing.");
        var args=new List<string>{"-y","-v","error","-nostats","-copyts","-start_at_zero"};
        if(offset>0)args.AddRange(["-itsoffset",offset.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        args.AddRange(["-i",video]);if(offset<0)args.AddRange(["-itsoffset",(-offset).ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        args.AddRange(["-i",audio,"-map","0:v:0","-map","1:a:0","-c","copy"]);if(experimentalAudio)args.AddRange(["-strict","-2"]);var dolby=await PreserveDolbyMuxAsync(ffmpeg,video,output,args,ct).ConfigureAwait(false);args.AddRange(["-t",duration.ToString("0.#########",System.Globalization.CultureInfo.InvariantCulture),output]);
        await RunAsync(ffmpeg,args,ct,rejectDiagnostics:true).ConfigureAwait(false);
        await VerifyDolbyMuxAsync(ffmpeg,output,dolby,ct).ConfigureAwait(false);
        if(offset<0&&Path.GetExtension(output) is ".mp4" or ".m4a" or ".mov")
        {
            var streams=await ProbeStreamsAsync(ffmpeg,audio,ct,"preserve delayed audio priming").ConfigureAwait(false);var stream=streams.Single(item=>item.Kind=="audio");uint skip=0;
            if(stream.Codec=="aac")
            {var json=await ProbePrimingAsync(ffmpeg,audio,ct).ConfigureAwait(false);using var document=System.Text.Json.JsonDocument.Parse(json);foreach(var packet in document.RootElement.GetProperty("packets").EnumerateArray())if(packet.TryGetProperty("side_data_list",out var list))foreach(var data in list.EnumerateArray())if(data.TryGetProperty("skip_samples",out var samples))skip=samples.GetUInt32();}
            if(skip>0)await Mp4AudioPriming.ApplyAsync(output,skip,stream.SampleRate,ct,-offset).ConfigureAwait(false);
        }
    }
    private static async Task<string> ProbePrimingAsync(string ffmpeg,string audio,CancellationToken ct)
    {
        var start=new System.Diagnostics.ProcessStartInfo(RequireProbe(ffmpeg)){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        foreach(var argument in new[]{"-v","error","-select_streams","a:0","-show_packets","-read_intervals","%+#1","-show_entries","packet=side_data_list","-of","json",audio})start.ArgumentList.Add(argument);
        using var process=new System.Diagnostics.Process{StartInfo=start};if(!process.Start())throw new InvalidOperationException("Could not inspect audio priming.");
        var output=ReadTailAsync(process.StandardOutput);var errors=ReadTailAsync(process.StandardError);
        try{await process.WaitForExitAsync(ct).ConfigureAwait(false);}
        catch(OperationCanceledException){try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);await Task.WhenAll(output,errors).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);throw;}
        var json=await output.ConfigureAwait(false);var diagnostics=await errors.ConfigureAwait(false);if(process.ExitCode!=0||diagnostics.Length>0)throw new InvalidDataException("Could not inspect complete source audio priming.");return json;
    }
    internal static async Task VerifyLivePairAsync(string ffmpeg,string path,string extension,double duration,CancellationToken ct)
    {
        var demuxer=extension is "mkv" or "webm"?"matroska":extension=="ts"?"mpegts":"mov";
        var streams=await ProbeStreamsAsync(ffmpeg,path,ct,"verify paired live tracks",inputDemuxer:demuxer,countFrames:true,localMediaOnly:true).ConfigureAwait(false);
        if(streams.Count(stream=>stream.Kind=="video")!=1||streams.Count(stream=>stream.Kind=="audio")!=1)throw new InvalidDataException("The paired live recording did not retain both selected tracks.");
        var tolerance=(decimal)Math.Max(0.25,duration*0.005);
        if(streams.Where(stream=>stream.Kind is "video" or "audio").Any(stream=>stream.DurationSeconds is { } span&&span+tolerance<(decimal)duration))
            throw new InvalidDataException("A selected live track does not cover the complete shared recording interval.");
        await VerifyDownloadedMediaAsync(ffmpeg,path,new MediaCandidate{Url=new Uri(Path.GetFullPath(path)),Kind=MediaKind.Hls,HasVideo=true,HasAudio=true,
            VerificationDemuxer=demuxer,VerificationMinimumDurationSeconds=duration,VerificationMinimumDurationToleranceSeconds=Math.Max(0.25,duration*0.005)},ct).ConfigureAwait(false);
    }
    internal static async Task VerifyLiveRecordingAsync(string ffmpeg,string path,string demuxer,double duration,CancellationToken ct)
    {
        var streams=await ProbeStreamsAsync(ffmpeg,path,ct,"identify recorded live tracks",inputDemuxer:demuxer=="mpegts"?null:demuxer,countFrames:true,localMediaOnly:true).ConfigureAwait(false);
        // HLS without an initialization map can carry packed ADTS AAC. Permit
        // that exact local audio container; keep every other demuxer contract.
        if(demuxer=="mpegts")
        {
            if(streams.Count==1&&streams[0].Container=="aac"&&streams[0].Kind=="audio"&&streams[0].Codec=="aac")demuxer="aac";
            else if(streams.Any(stream=>stream.Container!="mpegts"))throw new InvalidDataException("The live HLS recording uses an unsupported nonfragmented container.");
        }
        await VerifyDownloadedMediaAsync(ffmpeg,path,new MediaCandidate {Url=new Uri(Path.GetFullPath(path)),Kind=MediaKind.Hls,
            HasAudio=streams.Any(stream=>stream.Kind=="audio"),HasVideo=streams.Any(stream=>stream.Kind=="video"),VerificationDemuxer=demuxer,
            VerificationMinimumDurationSeconds=duration,VerificationMinimumDurationToleranceSeconds=Math.Max(0.25,duration*0.005)},ct).ConfigureAwait(false);
    }
}

