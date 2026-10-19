namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    private static async Task<(string Path,bool CustomSignal)> ExpandDolbySequenceAsync(string ffmpeg,string input,string directory,int index,ProbedStream source,CancellationToken ct)
    {
        if(source.Dolby is not {Profile:8,Compatibility:1 or 4,Rpu:1,Base:1,Enhancement:0,Major:1,Minor:0,Compression:"none" or "" or "limited" or "extended"})throw new NotSupportedException("Dolby sequence expansion requires clear HDR10-compatible profile-8 metadata.");
        if(source.Dolby.Compatibility!=(source.ColorTransfer=="smpte2084"?1:source.ColorTransfer=="arib-std-b67"?4:-1))throw new NotSupportedException("Dolby base transfer and compatibility disagree.");
        var owned=Path.Combine(directory,"dolby-sequence-owned-"+index+".mp4");var output=Path.Combine(directory,"dolby-sequence-expanded-"+index+".mp4");await RunAsync(ffmpeg,["-y","-v","error","-protocol_whitelist","file,pipe","-enable_drefs","0","-use_absolute_path","0","-i",input,"-map","0:v:0","-c","copy","-strict","unofficial",owned],ct,rejectDiagnostics:true).ConfigureAwait(false);
        var sequence=new DolbyRpuSequence(source.Dolby.Compression is "limited" or "extended",source.Dolby.Compression=="extended");var samples=new List<Mp4MelBridge.Sample>();var expanded=new List<byte[]>();var unique=new Dictionary<string,byte[]>();var bytes=0;
        await InspectHdrPacketsAsync(ffmpeg,owned,(position,nals)=>
        {
            ct.ThrowIfCancellationRequested();var rpu=nals.Where(n=>HevcMetadataTransport.Type(n)==62).ToArray();if(rpu.Length!=1||nals.Any(n=>HevcMetadataTransport.Type(n)==63)||samples.Count>0&&position<=samples[^1].Position)throw new InvalidDataException("Dolby expansion requires one ordered RPU per single-layer coded picture.");var full=sequence.Expand(rpu[0]);var key=Convert.ToHexString(full);if(!unique.TryGetValue(key,out var shared)){if(unique.Count>=4096||(bytes+=full.Length)>32*1024*1024)throw new InvalidDataException("Expanded Dolby metadata exceeds its inventory bound.");unique[key]=shared=full;}expanded.Add(shared);samples.Add(new(position,nals.Sum(n=>n.Length+4)));
        },ct).ConfigureAwait(false);
        var at=0;await Mp4MelBridge.RewriteAsync(owned,output,samples,ct,_=>expanded[at++],singleLayer:true).ConfigureAwait(false);if(at!=expanded.Count)throw new InvalidDataException("Dolby expansion did not consume its coded sample inventory.");return(output,sequence.NeedsSignalReconstruction);
    }
}
