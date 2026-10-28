using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    private static async Task InspectHdrPacketsAsync(string ffmpeg,string input,Action<long,IReadOnlyList<byte[]>> inspect,CancellationToken ct,bool av1=false,DolbyRpuSequence? sequence=null)
    {
        var start=new ProcessStartInfo(RequireProbe(ffmpeg,"bind HDR metadata to coded pictures")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var value in new[]{"-v","error","-protocol_whitelist","file,pipe","-f","mov","-enable_drefs","0","-use_absolute_path","0","-select_streams","v:0","-show_packets","-show_entries","packet=pos,size","-of","xml",input})start.ArgumentList.Add(value);
        ct.ThrowIfCancellationRequested();using var process=new Process{StartInfo=start};if(!process.Start())throw new InvalidOperationException("Could not inspect coded HDR packets.");using var lifetime=ProcessLifetime.Attach(process);using var cancel=ct.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}});var error=ReadTailAsync(process.StandardError);
        try
        {
            await using var source=File.OpenRead(input);using var reader=XmlReader.Create(process.StandardOutput,new XmlReaderSettings{Async=true,DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,IgnoreWhitespace=true,MaxCharactersInDocument=32*1024*1024});var count=0;var positions=new HashSet<long>();
            while(await reader.ReadAsync().ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();if(reader.NodeType!=XmlNodeType.Element||reader.LocalName!="packet")continue;
                if(++count>HdrMetadataTimeline.MaximumFrames||!long.TryParse(reader.GetAttribute("pos"),NumberStyles.None,CultureInfo.InvariantCulture,out var position)||!int.TryParse(reader.GetAttribute("size"),NumberStyles.None,CultureInfo.InvariantCulture,out var size)||size is <=0 or >32*1024*1024||position<0||position>source.Length-size||!positions.Add(position))throw new InvalidDataException("Invalid or ambiguous coded HDR packet interval.");
                var bytes=new byte[size];source.Position=position;await source.ReadExactlyAsync(bytes,ct).ConfigureAwait(false);if(av1){inspect(position,Av1DolbyMetadata.Rpus(bytes,sequence));continue;}var nals=new List<byte[]>();var at=0;
                while(at<bytes.Length){if(bytes.Length-at<4)throw new InvalidDataException("Truncated HDR sample NAL length.");var length=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at));at+=4;if(length<2||length>bytes.Length-at)throw new NotSupportedException("HDR metadata transport requires complete four-byte-length HEVC samples.");var nal=bytes.AsSpan(at,(int)length).ToArray();if((nal[0]&0x80)!=0||(nal[1]&7)==0)throw new InvalidDataException("Invalid HEVC packet header.");nals.Add(nal);at+=(int)length;}
                inspect(position,nals);
            }
            await process.WaitForExitAsync(ct).ConfigureAwait(false);var diagnostic=await error.ConfigureAwait(false);if(process.ExitCode!=0||diagnostic.Length>0||count==0)throw new InvalidDataException("Coded HDR packet inspection failed: "+Trim(diagnostic));
        }
        catch{await ProcessLifetime.KillAndDrainAsync(process,null,error).ConfigureAwait(false);ct.ThrowIfCancellationRequested();throw;}
    }
    private static async Task BindSpatialPlusAsync(string ffmpeg,string input,IReadOnlyList<HdrTimedFrame> frames,HdrSpatialGeometry geometry,CancellationToken ct)
        =>await BindSpatialPlusAsync(ffmpeg,input,frames,payload=>HdrPlusSpatial.Map(payload,geometry),ct).ConfigureAwait(false);
    private static async Task BindSpatialPlusAsync(string ffmpeg,string input,IReadOnlyList<HdrTimedFrame> frames,Func<byte[],byte[]> map,CancellationToken ct)
    {
        var bindings=frames.ToDictionary(frame=>frame.PacketPosition);if(bindings.ContainsKey(-1))throw new NotSupportedException("HDR10+ reordered pictures require unambiguous coded packet positions.");var assigned=new HashSet<long>();byte[]? current=null;var bytes=0;
        await InspectHdrPacketsAsync(ffmpeg,input,(position,nals)=>
        {
            if(!bindings.TryGetValue(position,out var frame)||!assigned.Add(position))throw new InvalidDataException("The coded HDR packet has no unique decoded picture.");if(nals.Any(nal=>HevcMetadataTransport.Type(nal)==63||(nal[0]&1)!=0||(nal[1]>>3)!=0))throw new NotSupportedException("HDR enhancement-layer packets remain unsupported.");var plus=nals.SelectMany(HevcMetadataTransport.Sei).Where(item=>item.Type==4&&HevcMetadataTransport.HdrPlus(item.Data)).ToArray();if(plus.Length>1)throw new InvalidDataException("Duplicate HDR10+ payloads in one packet.");if(plus.Length==1){current=map(plus[0].Data);if((bytes+=current.Length)>32*1024*1024)throw new InvalidDataException("HDR10+ payload inventory is excessive.");}
            if(frame.Metadata.Plus is not null){if(current is null)throw new InvalidDataException("Decoded HDR10+ metadata has no coded payload state.");frame.PlusPayload=current;}else if(plus.Length>0)throw new InvalidDataException("An HDR10+ packet did not decode its declared metadata.");
        },ct).ConfigureAwait(false);if(assigned.Count!=frames.Count)throw new InvalidDataException("The reordered HDR10+ picture inventory is incomplete.");
    }
    private static async Task BindCanonicalDolbyAsync(string ffmpeg,string input,IReadOnlyList<HdrTimedFrame> frames,HdrSpatialGeometry geometry,CancellationToken ct)
        =>await BindCanonicalDolbyAsync(ffmpeg,input,frames,payload=>DolbySpatialRpu.Map(payload,geometry),ct).ConfigureAwait(false);
    private static async Task BindCanonicalDolbyAsync(string ffmpeg,string input,IReadOnlyList<HdrTimedFrame> frames,Func<byte[],byte[]> map,CancellationToken ct)
    {
        var bindings=frames.ToDictionary(frame=>frame.PacketPosition);var assigned=new HashSet<long>();var bytes=0;
        await InspectHdrPacketsAsync(ffmpeg,input,(position,nals)=>{if(!bindings.TryGetValue(position,out var frame)||!assigned.Add(position))throw new InvalidDataException("The canonical Dolby packet has no unique decoded picture.");var rp=nals.Where(nal=>HevcMetadataTransport.Type(nal)==62).ToArray();if(rp.Length!=1||nals.Any(nal=>HevcMetadataTransport.Type(nal)==63||(nal[0]&1)!=0||(nal[1]>>3)!=0))throw new InvalidDataException("The canonical Dolby picture has invalid layer metadata.");frame.DolbyPayload=map(rp[0]);if((bytes+=frame.DolbyPayload.Length)>32*1024*1024)throw new InvalidDataException("Dolby payload inventory is excessive.");},ct).ConfigureAwait(false);if(assigned.Count!=frames.Count)throw new InvalidDataException("The canonical Dolby timeline is incomplete.");
    }
}

