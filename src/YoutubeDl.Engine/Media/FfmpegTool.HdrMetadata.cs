using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;

namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    // Streaming inspection checks every decoded frame, including metadata introduced after the first GOP.
    // Memory is bounded independently of recording length; cancellation owns and terminates ffprobe.
    private static async Task<TransitionHdrMetadata> InspectTransitionMetadataAsync(string ffmpeg,string input,ProbedStream stream,bool preserve,CancellationToken ct)
    {
        var start=new ProcessStartInfo(RequireProbe(ffmpeg,"inspect HDR frame metadata")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var value in new[]{"-v","error","-threads","1","-max_alloc","134217728","-protocol_whitelist","file,pipe","-format_whitelist","mov","-f","mov","-enable_drefs","0","-use_absolute_path","0","-select_streams","v:0","-show_frames","-show_entries","frame=pix_fmt,color_range,color_space,color_primaries,color_transfer:frame_side_data=side_data_type,red_x,red_y,green_x,green_y,blue_x,blue_y,white_point_x,white_point_y,min_luminance,max_luminance,max_content,max_average","-of","xml",input})start.ArgumentList.Add(value);
        ct.ThrowIfCancellationRequested();using var process=new Process{StartInfo=start};if(!process.Start())throw new InvalidOperationException("Could not start HDR inspection.");
        using var lifetime=ProcessLifetime.Attach(process);
        using var cancel=ct.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}});
        var error=ReadTailAsync(process.StandardError);TransitionHdrMetadata? first=null;var count=0;
        try
        {
            using var reader=XmlReader.Create(process.StandardOutput,new XmlReaderSettings{Async=true,DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,IgnoreWhitespace=true,MaxCharactersInDocument=512L*1024*1024});
            while(!reader.EOF)
            {
                ct.ThrowIfCancellationRequested();
                if(reader.NodeType!=XmlNodeType.Element||reader.LocalName!="frame"){await reader.ReadAsync().ConfigureAwait(false);continue;}
                if(++count>2000000)throw new InvalidDataException("HDR inspection exceeded the supported period frame count.");
                var frame=(XElement)await XNode.ReadFromAsync(reader,ct).ConfigureAwait(false);var metadata=TransitionHdrMetadata.Read(frame);
                if(preserve)
                {
                    if((string?)frame.Attribute("pix_fmt")!=stream.PixelFormat||(string?)frame.Attribute("color_transfer")!=stream.ColorTransfer||(string?)frame.Attribute("color_primaries")!=stream.ColorPrimaries||(string?)frame.Attribute("color_space")!=stream.ColorSpace||(string?)frame.Attribute("color_range")!=stream.ColorRange)
                        throw new NotSupportedException("HDR/color facts changed within a source period.");
                    if(first is not null&&first!=metadata)throw new NotSupportedException("Changing HDR10 metadata requires a per-frame metadata-preserving encoder.");
                }
                first??=metadata;
            }
            await process.WaitForExitAsync(ct).ConfigureAwait(false);var diagnostic=await error.ConfigureAwait(false);
            if(process.ExitCode!=0||!string.IsNullOrWhiteSpace(diagnostic)||count==0)throw new InvalidDataException("Could not inspect every source video frame: "+Trim(diagnostic));
            return first!;
        }
        catch
        {
            try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);await error.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);ct.ThrowIfCancellationRequested();throw;
        }
    }
}

