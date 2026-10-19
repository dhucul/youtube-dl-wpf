using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace YoutubeDl.Engine.Media;

internal static class HevcMetadataTransport
{
    private const int MaximumUnit=32*1024*1024;
    internal static async IAsyncEnumerable<List<byte[]>> AccessUnits(string path,[EnumeratorCancellation]CancellationToken ct)
    {
        await using var stream=File.OpenRead(path);var buffer=new byte[65536];using var nal=new MemoryStream();var current=new List<byte[]>();var zeros=0;var count=0;var bytes=0;
        while(true)
        {
            var read=await stream.ReadAsync(buffer,ct).ConfigureAwait(false);
            if(read==0)break;
            for(var i=0;i<read;i++)
            {
                var value=buffer[i];
                if(value==0){if(++zeros>MaximumUnit)throw new InvalidDataException("An HEVC zero run exceeds its size limit.");continue;}
                if(value==1&&zeros>=2)
                {
                    if(nal.Length>0)
                    {
                        var data=nal.ToArray();nal.SetLength(0);if(data.Length<2||(data[0]&0x80)!=0||(data[1]&7)==0)throw new InvalidDataException("Invalid HEVC NAL header.");
                        var type=(data[0]>>1)&63;
                        if(type==35&&current.Any(item=>Type(item)<=31))
                        {if(++count>HdrMetadataTimeline.MaximumFrames)throw new InvalidDataException("Too many HEVC access units.");yield return current;current=[];bytes=0;}
                        if((bytes+=data.Length)>MaximumUnit)throw new InvalidDataException("An HEVC access unit exceeds its size limit.");current.Add(data);
                    }
                    zeros=0;continue;
                }
                while(zeros-->0)nal.WriteByte(0);zeros=0;nal.WriteByte(value);
                if(nal.Length>MaximumUnit)throw new InvalidDataException("An HEVC NAL exceeds its size limit.");
            }
        }
        // Annex-B trailing_zero_8bits are framing, not part of the final RBSP.
        if(nal.Length>0){var data=nal.ToArray();if(data.Length<2)throw new InvalidDataException("Truncated HEVC NAL.");current.Add(data);}
        if(current.Count>0){if(!current.Any(item=>Type(item)<=31))throw new InvalidDataException("HEVC trailer has no coded picture.");yield return current;}
    }
    internal static int Type(byte[] nal)=>(nal[0]>>1)&63;
    internal static IReadOnlyList<(int Type,byte[] Data)> Sei(byte[] nal)
    {
        if(Type(nal) is not (39 or 40))return [];
        var rbsp=Unescape(nal.AsSpan(2));var result=new List<(int,byte[])>();var index=0;
        while(index<rbsp.Length&&!(index==rbsp.Length-1&&rbsp[index]==0x80))
        {
            int Read(){var sum=0;byte value;do{if(index>=rbsp.Length||sum>65536)throw new InvalidDataException("Invalid HEVC SEI header.");value=rbsp[index++];sum+=value;}while(value==255);return sum;}
            var type=Read();var size=Read();if(size<0||size>65536||index+size>rbsp.Length)throw new InvalidDataException("Invalid HEVC SEI size.");result.Add((type,rbsp.AsSpan(index,size).ToArray()));index+=size;
        }
        if(index!=rbsp.Length-1||rbsp[index]!=0x80)throw new InvalidDataException("Invalid HEVC SEI trailing bits.");return result;
    }
    internal static bool HdrPlus(byte[] data)=>data.Length>=8&&data[0]==0xb5&&data[1]==0&&data[2]==0x3c&&data[3]==0&&data[4]==1&&data[5]==4;
    internal static void ValidatePlus(byte[] data)
    {if(!HdrPlus(data)||data.Length>4096||data[6]!=1||((data[7]>>6)&3)!=1)throw new NotSupportedException("HDR10+ re-encoding currently requires application version 1 and one full-frame window.");}
    internal static byte[] EncodeSei(IReadOnlyList<(int Type,byte[] Data)> messages,int type=39)
    {
        using var raw=new MemoryStream();foreach(var item in messages){Number(item.Type);Number(item.Data.Length);raw.Write(item.Data);}raw.WriteByte(0x80);
        using var result=new MemoryStream();result.WriteByte((byte)(type<<1));result.WriteByte(1);var zeros=0;foreach(var value in raw.ToArray()){if(zeros==2&&value<=3){result.WriteByte(3);zeros=0;}result.WriteByte(value);zeros=value==0?zeros+1:0;}return result.ToArray();
        void Number(int value){if(value is <0 or >65536)throw new InvalidDataException("SEI value exceeds its supported limit.");while(value>=255){raw.WriteByte(255);value-=255;}raw.WriteByte((byte)value);}
    }
    internal static IReadOnlyList<(int Type,byte[] Data)> Static(TransitionHdrMetadata metadata)
    {
        var result=new List<(int,byte[])>();
        if(metadata.MasterDisplay is { } master)
        {
            var values=System.Text.RegularExpressions.Regex.Matches(master,@"\d+",System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)).Select(value=>uint.Parse(value.Value,System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if(values.Length!=10)throw new InvalidDataException("Invalid canonical mastering profile.");var data=new byte[24];for(var i=0;i<8;i++)BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(i*2),checked((ushort)values[i]));BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16),values[8]);BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20),values[9]);result.Add((137,data));
        }
        if(metadata.ContentLight is { } light){var values=light.Split(',').Select(value=>ushort.Parse(value,System.Globalization.CultureInfo.InvariantCulture)).ToArray();var data=new byte[4];BinaryPrimitives.WriteUInt16BigEndian(data,values[0]);BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2),values[1]);result.Add((144,data));}
        return result;
    }
    internal static async Task PatchAsync(string input,string output,IReadOnlyList<HdrTimedFrame> frames,CancellationToken ct)
    {
        await using var destination=File.Create(output);var index=0;
        await foreach(var unit in AccessUnits(input,ct))
        {
            if(index>=frames.Count)throw new InvalidDataException("The encoded HDR timeline contains extra pictures.");var frame=frames[index++];var inserted=false;
            foreach(var nal in unit)
            {
                var type=Type(nal);if(type<=31&&!inserted){var messages=Static(frame.Metadata.Static).ToList();if(frame.PlusPayload is { } plus)messages.Add((4,plus));if(messages.Count>0)await Write(EncodeSei(messages));inserted=true;}
                if(type is 39 or 40){var kept=Sei(nal).Where(item=>item.Type is not (137 or 144)&&!(item.Type==4&&HdrPlus(item.Data))).ToArray();if(kept.Length>0)await Write(EncodeSei(kept,type));}
                else if(type!=62||frame.DolbyPayload is null)await Write(nal);
            }
            if(frame.DolbyPayload is { } dolby)await Write(dolby);
            if(!inserted)throw new InvalidDataException("An encoded HDR access unit lost its coded picture.");
        }
        if(index!=frames.Count)throw new InvalidDataException("The encoded HDR timeline lost pictures.");
        async Task Write(byte[] nal){await destination.WriteAsync(new byte[]{0,0,0,1},ct).ConfigureAwait(false);await destination.WriteAsync(nal,ct).ConfigureAwait(false);}
    }
    private static byte[] Unescape(ReadOnlySpan<byte> bytes)
    {
        using var result=new MemoryStream();var zeros=0;for(var i=0;i<bytes.Length;i++){var value=bytes[i];if(zeros==2&&value==3){if(i+1>=bytes.Length||bytes[i+1]>3)throw new InvalidDataException("Invalid HEVC emulation prevention.");zeros=0;continue;}result.WriteByte(value);zeros=value==0?zeros+1:0;}return result.ToArray();
    }
}
