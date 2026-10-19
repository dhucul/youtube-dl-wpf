namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    private static async Task<TransitionDolbyConfiguration?> PreserveDolbyMuxAsync(string ffmpeg,string input,string output,List<string> args,CancellationToken ct)
    {
        if(Path.GetExtension(output).ToLowerInvariant() is not (".mp4" or ".m4v" or ".mov" or ".mkv"))return null;
        var streams=await ProbeStreamsAsync(ffmpeg,input,ct,"preserve Dolby Vision track signaling",localMediaOnly:true).ConfigureAwait(false);var configurations=streams.Where(stream=>stream.Kind=="video"&&!stream.IsCover&&stream.Dolby is not null).Select(stream=>stream.Dolby!).ToArray();if(configurations.Length==0)return null;if(configurations.Length!=1)throw new NotSupportedException("Dolby Vision remuxing requires one selected video track.");
        if(Path.GetExtension(output).ToLowerInvariant() is ".mp4" or ".m4v" or ".mov")
        {var strict=args.LastIndexOf("-strict");if(strict<0||args[strict+1] is not ("-2" or "experimental" or "-1" or "unofficial"))args.AddRange(["-strict","unofficial"]);}
        return configurations[0];
    }
    private static async Task VerifyDolbyMuxAsync(string ffmpeg,string output,TransitionDolbyConfiguration? expected,CancellationToken ct)
    {
        if(expected is null)return;var streams=await ProbeStreamsAsync(ffmpeg,output,ct,"verify Dolby Vision track signaling",localMediaOnly:true).ConfigureAwait(false);if(streams.Count(stream=>stream.Kind=="video"&&!stream.IsCover&&stream.Dolby==expected)!=1)throw new InvalidDataException("Media processing lost the selected Dolby Vision configuration.");
    }
}
