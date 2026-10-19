using System.Buffers.Binary;
using System.Text;

namespace YoutubeDl.Engine.Media;

// Restore the encoder's exact configuration record after local elementary-stream metadata editing.
// The owned FFmpeg outputs place moov last, so media chunk offsets do not move.
internal static class Mp4DolbyConfiguration
{
    private sealed record Box(int Start,int Size,string Type){internal int End=>Start+Size;}
    internal static async Task CopyAsync(string template,string output,CancellationToken ct,int width=0,int height=0,int profile=0,int compatibility=0,bool uncompressed=false)
    {
        var original=await Movie(template,ct).ConfigureAwait(false);var chain=Entry(original.Bytes);var entry=chain[^1];var configs=Boxes(original.Bytes,entry.Start+86,entry.End).Where(box=>box.Type is "dvcC" or "dvvC").ToArray();
        if(configs.Length!=1||configs[0].Size!=32)throw new InvalidDataException("The Dolby encoder did not declare exactly one bounded configuration record.");var config=original.Bytes.AsSpan(configs[0].Start,32).ToArray();
        if(profile!=0){if(profile!=8)throw new NotSupportedException("Unsupported Dolby bridge profile.");config[10]=(byte)((config[10]&1)|(profile<<1));}
        if(compatibility!=0){if(compatibility is not (1 or 4))throw new NotSupportedException("Unsupported Dolby output compatibility.");config[12]=(byte)((config[12]&15)|(compatibility<<4));}if(uncompressed)config[12]&=0xf3;
        if(width>0&&height>0)
        {
            var limits=new(long Pixels,int Width)[]{(1280L*720*24,1280),(1280L*720*30,1280),(1920L*1080*24,1920),(1920L*1080*30,2560),(1920L*1080*60,3840),(3840L*2160*24,3840),(3840L*2160*30,3840),(3840L*2160*48,3840),(3840L*2160*60,3840)};var level=Array.FindIndex(limits,limit=>width<=limit.Width&&(long)width*height*30<=limit.Pixels)+1;if(level<=0)throw new NotSupportedException("The spatial Dolby output exceeds supported level signaling.");config[10]=(byte)((config[10]&0xfe)|(level>>5));config[11]=(byte)((config[11]&7)|((level&31)<<3));
        }
        var target=await Movie(output,ct).ConfigureAwait(false);var parents=Entry(target.Bytes);entry=parents[^1];
        if(Boxes(target.Bytes,entry.Start+86,entry.End).Any(box=>box.Type is "dvcC" or "dvvC"))throw new InvalidDataException("The edited Dolby output already declares a conflicting configuration.");
        var movie=new byte[target.Bytes.Length+32];target.Bytes.AsSpan(0,entry.End).CopyTo(movie);config.CopyTo(movie.AsSpan(entry.End));target.Bytes.AsSpan(entry.End).CopyTo(movie.AsSpan(entry.End+32));
        foreach(var parent in parents)BinaryPrimitives.WriteUInt32BigEndian(movie.AsSpan(parent.Start),checked((uint)(parent.Size+32)));
        var pending=output+".dolby-pending";
        try
        {
            await using(var source=File.OpenRead(output))await using(var destination=File.Create(pending))
            {
                var remaining=target.Offset;var buffer=new byte[65536];while(remaining>0){var read=await source.ReadAsync(buffer.AsMemory(0,(int)Math.Min(buffer.Length,remaining)),ct).ConfigureAwait(false);if(read==0)throw new EndOfStreamException();await destination.WriteAsync(buffer.AsMemory(0,read),ct).ConfigureAwait(false);remaining-=read;}
                await destination.WriteAsync(movie,ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();File.Move(pending,output,true);
        }
        finally{if(File.Exists(pending))File.Delete(pending);}
    }
    private static List<Box> Entry(byte[] bytes)
    {
        var movie=Boxes(bytes,0,bytes.Length).Single(box=>box.Type=="moov");var result=new List<Box>{movie};var current=movie;
        foreach(var type in new[]{"trak","mdia","minf","stbl","stsd"}){current=Boxes(bytes,current.Start+8,current.End).Single(box=>box.Type==type);result.Add(current);}
        var entries=Boxes(bytes,current.Start+16,current.End).ToArray();if(entries.Length!=1||entries[0].Type is not ("hvc1" or "hev1" or "av01")||entries[0].Size<86)throw new InvalidDataException("The Dolby movie does not contain one supported video sample entry.");result.Add(entries[0]);return result;
    }
    private static IEnumerable<Box> Boxes(byte[] bytes,int start,int end)
    {
        if(start<0||end>bytes.Length||start>end)throw new InvalidDataException("Invalid movie box interval.");
        while(start<end){if(end-start<8)throw new InvalidDataException("Truncated movie box.");var size=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(start));if(size<8||size>end-start)throw new InvalidDataException("Unsupported movie box size.");var box=new Box(start,(int)size,Encoding.ASCII.GetString(bytes,start+4,4));yield return box;start=box.End;}
    }
    private static async Task<(long Offset,byte[] Bytes)> Movie(string path,CancellationToken ct)
    {
        await using var source=File.OpenRead(path);var header=new byte[16];var media=false;
        while(source.Position<source.Length)
        {
            var offset=source.Position;await source.ReadExactlyAsync(header.AsMemory(0,8),ct).ConfigureAwait(false);var length=(long)BinaryPrimitives.ReadUInt32BigEndian(header);var type=Encoding.ASCII.GetString(header,4,4);var headerLength=8;
            if(length==1){await source.ReadExactlyAsync(header.AsMemory(8,8),ct).ConfigureAwait(false);var extended=BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));if(extended>long.MaxValue)throw new InvalidDataException("Oversized movie box.");length=(long)extended;headerLength=16;}
            if(length==0)length=source.Length-offset;if(length<headerLength||length>source.Length-offset)throw new InvalidDataException("Invalid top-level movie box.");
            if(type=="mdat")media=true;
            if(type=="moov")
            {
                if(!media||headerLength!=8||length>16*1024*1024||offset+length!=source.Length)throw new NotSupportedException("Dolby metadata editing requires a bounded final movie index after local media data.");var bytes=new byte[(int)length];header.AsSpan(0,8).CopyTo(bytes);await source.ReadExactlyAsync(bytes.AsMemory(8),ct).ConfigureAwait(false);return(offset,bytes);
            }
            source.Position=offset+length;
        }
        throw new InvalidDataException("Missing movie index.");
    }
}
