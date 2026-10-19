using System.Buffers.Binary;
using System.Text;

namespace YoutubeDl.Engine.Media;

internal static class Mp4MelBridge
{
    internal sealed record Sample(long Position,int Size);
    private sealed record Box(int Start,int Size,string Type){internal int End=>Start+Size;}
    internal static byte[] SampleBytes(IReadOnlyList<byte[]> nals,Func<byte[],byte[]>? convert=null,bool singleLayer=false)
    {
        var rpus=nals.Where(n=>HevcMetadataTransport.Type(n)==62).ToArray();var enhancement=nals.Where(n=>HevcMetadataTransport.Type(n)==63).ToArray();
        if(rpus.Length!=1||(!singleLayer&&enhancement.Length==0)||(singleLayer&&enhancement.Length!=0)||enhancement.Any(n=>n.Length<4||(n[2]&0x80)!=0||(n[3]&7)==0)||nals.Any(n=>(n[0]&1)!=0||(n[1]>>3)!=0))throw new NotSupportedException("Profile-7 MEL requires a complete base/enhancement/RPU access unit in each MP4 sample.");
        var converted=convert is null?DolbyMelRpu.ToSingleLayer(rpus[0]):convert(rpus[0]);using var result=new MemoryStream();var length=new byte[4];
        foreach(var nal in nals){if(HevcMetadataTransport.Type(nal)==63)continue;var data=HevcMetadataTransport.Type(nal)==62?converted:nal;BinaryPrimitives.WriteUInt32BigEndian(length,(uint)data.Length);result.Write(length);result.Write(data);}return result.ToArray();
    }
    internal static async Task RewriteAsync(string input,string output,IReadOnlyList<Sample> samples,CancellationToken ct,Func<byte[],byte[]>? convert=null,bool singleLayer=false)
    {
        if(samples.Count is 0 or >HdrMetadataTimeline.MaximumFrames)throw new InvalidDataException("Invalid MEL sample inventory.");
        await using var source=File.OpenRead(input);var header=new byte[8];long moov=-1,mdat=-1,mdatEnd=-1;byte[]? movie=null;
        while(source.Position<source.Length)
        {
            var at=source.Position;await source.ReadExactlyAsync(header,ct).ConfigureAwait(false);var size=BinaryPrimitives.ReadUInt32BigEndian(header);var type=Encoding.ASCII.GetString(header,4,4);if(size<8||size>source.Length-at)throw new NotSupportedException("MEL editing requires bounded standard local MP4 boxes.");
            if(type=="mdat"){if(mdat>=0)throw new NotSupportedException("Multiple MEL media-data boxes are unsupported.");mdat=at;mdatEnd=at+size;}
            if(type=="moov"){if(mdat<0||size>16*1024*1024||at+size!=source.Length)throw new NotSupportedException("MEL editing requires a bounded final MP4 movie index.");moov=at;movie=new byte[size];header.CopyTo(movie,0);await source.ReadExactlyAsync(movie.AsMemory(8),ct).ConfigureAwait(false);}
            source.Position=at+size;
        }
        if(movie is null||moov<mdatEnd)throw new InvalidDataException("Missing MEL movie index.");var sorted=samples.OrderBy(s=>s.Position).ToArray();var newSizes=new int[sorted.Length];var shifts=new long[sorted.Length+1];var positions=sorted.Select(s=>s.Position).ToArray();var pending=output+".mel-pending";
        try
        {
            await using(var destination=File.Create(pending))
            {
                source.Position=0;
                for(var i=0;i<sorted.Length;i++)
                {
                    var sample=sorted[i];if(sample.Size is <=0 or >32*1024*1024||sample.Position<mdat+8||sample.Position<source.Position||sample.Position>mdatEnd-sample.Size)throw new InvalidDataException("Invalid MEL packet interval.");await Copy(sample.Position-source.Position,destination).ConfigureAwait(false);var data=new byte[sample.Size];await source.ReadExactlyAsync(data,ct).ConfigureAwait(false);var nals=new List<byte[]>();var at=0;while(at<data.Length){if(data.Length-at<4)throw new InvalidDataException("Truncated MEL sample.");var length=BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));at+=4;if(length<2||length>data.Length-at)throw new InvalidDataException("Invalid MEL NAL length.");nals.Add(data.AsSpan(at,(int)length).ToArray());at+=(int)length;}
                    var replacement=SampleBytes(nals,convert,singleLayer);newSizes[i]=replacement.Length;shifts[i+1]=checked(shifts[i]+replacement.Length-sample.Size);await destination.WriteAsync(replacement,ct).ConfigureAwait(false);
                }
                await Copy(moov-source.Position,destination).ConfigureAwait(false);var sampleTables=0;var configs=0;Walk(0,movie.Length);if(sampleTables!=1||configs!=1)throw new InvalidDataException("MEL source must contain one video sample table and one Dolby record.");await destination.WriteAsync(movie,ct).ConfigureAwait(false);destination.Position=mdat;BinaryPrimitives.WriteUInt32BigEndian(header,checked((uint)(mdatEnd-mdat+shifts[^1])));await destination.WriteAsync(header.AsMemory(0,4),ct).ConfigureAwait(false);
                void Walk(int start,int end)
                {
                    foreach(var box in Boxes(movie,start,end))
                    {
                        if(box.Type is "moov" or "trak" or "mdia" or "minf" or "stbl")Walk(box.Start+8,box.End);
                        if(box.Type=="stsd"){if(box.Size<16)throw new InvalidDataException("Truncated MEL sample description.");Walk(box.Start+16,box.End);}
                        if(box.Type is "hvc1" or "hev1"){if(box.Size<86)throw new InvalidDataException("Truncated MEL video entry.");Walk(box.Start+86,box.End);}
                        if(box.Type=="stsz")
                        {
                            if(++sampleTables!=1||box.Size<20||BinaryPrimitives.ReadUInt32BigEndian(movie.AsSpan(box.Start+12))!=0||BinaryPrimitives.ReadUInt32BigEndian(movie.AsSpan(box.Start+16))!=sorted.Length||box.Size!=20+sorted.Length*4)throw new NotSupportedException("Unsupported MEL sample-size table.");for(var i=0;i<sorted.Length;i++)BinaryPrimitives.WriteUInt32BigEndian(movie.AsSpan(box.Start+20+i*4),(uint)newSizes[i]);
                        }
                        if(box.Type is "stco" or "co64")
                        {
                            var stride=box.Type=="stco"?4:8;if(box.Size<16)throw new InvalidDataException("Truncated MEL chunk offsets.");var count=BinaryPrimitives.ReadUInt32BigEndian(movie.AsSpan(box.Start+12));if(count>sorted.Length||box.Size!=16+(long)count*stride)throw new InvalidDataException("Invalid MEL chunk-offset inventory.");
                            for(var i=0;i<count;i++){var at=box.Start+16+i*stride;var offset=stride==4?BinaryPrimitives.ReadUInt32BigEndian(movie.AsSpan(at)):BinaryPrimitives.ReadUInt64BigEndian(movie.AsSpan(at));if(offset>long.MaxValue)throw new InvalidDataException("Oversized MEL offset.");var index=Array.BinarySearch(positions,(long)offset);if(index<0)throw new InvalidDataException("MEL chunk does not begin at a verified sample.");var value=checked((ulong)((long)offset+shifts[index]));if(stride==4)BinaryPrimitives.WriteUInt32BigEndian(movie.AsSpan(at),checked((uint)value));else BinaryPrimitives.WriteUInt64BigEndian(movie.AsSpan(at),value);}
                        }
                        if(box.Type is "dvcC" or "dvvC")
                        {if(++configs!=1||box.Size!=32)throw new NotSupportedException("Dolby bridge requires one bounded configuration.");if(singleLayer){if((movie[box.Start+10]>>1)!=8||(movie[box.Start+11]&7)!=5||(movie[box.Start+12]>>4) is not (1 or 4))throw new NotSupportedException("Dolby normalization requires a complete HDR10-compatible profile-8 configuration.");movie[box.Start+12]&=0xf3;}else{if((movie[box.Start+10]>>1)!=7||(movie[box.Start+11]&7)!=7||(movie[box.Start+12]>>4)!=6)throw new NotSupportedException("MEL bridge requires the complete HDR10-compatible profile-7 configuration.");movie[box.Start+10]=(byte)((8<<1)|(movie[box.Start+10]&1));movie[box.Start+11]&=0xfd;movie[box.Start+12]=(byte)((movie[box.Start+12]&15)|0x10);}}
                    }
                }
            }
            ct.ThrowIfCancellationRequested();File.Move(pending,output,true);
        }
        finally{if(File.Exists(pending))File.Delete(pending);}
        async Task Copy(long count,Stream destination){if(count<0)throw new InvalidDataException("Overlapping MEL packet intervals.");var buffer=new byte[65536];while(count>0){var size=(int)Math.Min(buffer.Length,count);await source.ReadExactlyAsync(buffer.AsMemory(0,size),ct).ConfigureAwait(false);await destination.WriteAsync(buffer.AsMemory(0,size),ct).ConfigureAwait(false);count-=size;}}
    }
    private static IEnumerable<Box> Boxes(byte[] data,int start,int end){while(start<end){if(end-start<8)throw new InvalidDataException("Truncated MEL box.");var size=BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(start));if(size<8||size>end-start)throw new InvalidDataException("Invalid MEL box bounds.");yield return new(start,(int)size,Encoding.ASCII.GetString(data,start+4,4));start+=(int)size;}}
}
