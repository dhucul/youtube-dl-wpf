namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    private static async Task<string> BridgeMelAsync(string ffmpeg,string input,string directory,int index,ProbedStream source,CancellationToken ct)
    {
        if(source.Codec!="hevc"||source.Dolby is not {Profile:7,Compatibility:6,Rpu:1,Base:1,Enhancement:1,Major:1,Minor:0,Compression:"none" or ""})throw new NotSupportedException("MEL conversion requires a complete HDR10-compatible profile-7 HEVC source.");
        var owned=Path.Combine(directory,"mel-source-"+index+".mp4");var output=Path.Combine(directory,"mel-base-"+index+".mp4");
        await RunAsync(ffmpeg,["-y","-v","error","-protocol_whitelist","file,pipe","-enable_drefs","0","-use_absolute_path","0","-i",input,"-map","0:v:0","-c","copy","-strict","unofficial",owned],ct,rejectDiagnostics:true).ConfigureAwait(false);
        var samples=new List<Mp4MelBridge.Sample>();await InspectHdrPacketsAsync(ffmpeg,owned,(position,nals)=>{Mp4MelBridge.SampleBytes(nals);samples.Add(new(position,checked(nals.Sum(n=>n.Length+4))));},ct).ConfigureAwait(false);
        await Mp4MelBridge.RewriteAsync(owned,output,samples,ct).ConfigureAwait(false);var prepared=await ProbeStreamsAsync(ffmpeg,output,ct,"verify MEL bridge",localMediaOnly:true).ConfigureAwait(false);if(prepared.Count!=1||prepared[0].Dolby is not {Profile:8,Compatibility:1,Rpu:1,Base:1,Enhancement:0})throw new InvalidDataException("The MEL bridge lost its compatible single-layer configuration.");return output;
    }
}
