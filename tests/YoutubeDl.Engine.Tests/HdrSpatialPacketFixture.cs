using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Media;

internal static class HdrSpatialPacketFixture
{
    internal static async Task Rewrite(string input,string output,string packetJson,Func<int,byte[]> metadata)
    {
        var source=await File.ReadAllBytesAsync(input);using var doc=JsonDocument.Parse(packetJson);var packets=doc.RootElement.GetProperty("packets").EnumerateArray().Select(packet=>(Pos:int.Parse(packet.GetProperty("pos").GetString()!),Size:int.Parse(packet.GetProperty("size").GetString()!))).OrderBy(packet=>packet.Pos).ToArray();var replacements=new List<byte[]>();
        for(var i=0;i<packets.Length;i++){var body=HevcMetadataTransport.EncodeSei([(4,metadata(i))]);var bytes=new byte[4+body.Length+packets[i].Size];BinaryPrimitives.WriteUInt32BigEndian(bytes,(uint)body.Length);body.CopyTo(bytes,4);source.AsSpan(packets[i].Pos,packets[i].Size).CopyTo(bytes.AsSpan(body.Length+4));replacements.Add(bytes);}
        using var result=new MemoryStream();var at=0;for(var i=0;i<packets.Length;i++){result.Write(source,at,packets[i].Pos-at);result.Write(replacements[i]);at=packets[i].Pos+packets[i].Size;}result.Write(source,at,source.Length-at);var target=result.ToArray();var delta=target.Length-source.Length;var sourceTop=Boxes(source,0,source.Length).ToArray();var mdat=sourceTop.Single(box=>box.Type=="mdat");if(sourceTop.Single(box=>box.Type=="moov").Start<mdat.End)throw new Exception("The fixture movie index must follow media data.");BinaryPrimitives.WriteUInt32BigEndian(target.AsSpan(mdat.Start),(uint)(mdat.Size+delta));Walk(0,target.Length,false);await File.WriteAllBytesAsync(output,target);
        void Walk(int start,int end,bool video)
        {
            foreach(var box in Boxes(target,start,end))
            {
                var selected=video;if(box.Type=="trak")selected=FindVideo(box.Start+8,box.End);
                if(box.Type is "moov" or "trak" or "mdia" or "minf" or "stbl")Walk(box.Start+8,box.End,selected);
                if(box.Type=="stsz"&&selected){if(BinaryPrimitives.ReadUInt32BigEndian(target.AsSpan(box.Start+12))!=0||BinaryPrimitives.ReadUInt32BigEndian(target.AsSpan(box.Start+16))!=packets.Length)throw new Exception("Unexpected fixture sample-size layout.");for(var i=0;i<packets.Length;i++)BinaryPrimitives.WriteUInt32BigEndian(target.AsSpan(box.Start+20+i*4),(uint)replacements[i].Length);}
                if(box.Type=="stco"){var count=BinaryPrimitives.ReadUInt32BigEndian(target.AsSpan(box.Start+12));for(var i=0;i<count;i++){var p=box.Start+16+i*4;var value=BinaryPrimitives.ReadUInt32BigEndian(target.AsSpan(p));var shift=0;for(var n=0;n<packets.Length&&packets[n].Pos<value;n++)shift+=replacements[n].Length-packets[n].Size;BinaryPrimitives.WriteUInt32BigEndian(target.AsSpan(p),checked((uint)(value+shift)));}}
                if(box.Type=="co64")throw new Exception("Unexpected large fixture offsets.");
            }
        }
        bool FindVideo(int start,int end){foreach(var box in Boxes(target,start,end)){if(box.Type=="hdlr"&&Encoding.ASCII.GetString(target,box.Start+16,4)=="vide")return true;if(box.Type is "mdia" or "minf" or "stbl"&&FindVideo(box.Start+8,box.End))return true;}return false;}
    }
    private sealed record Box(int Start,int Size,string Type){internal int End=>Start+Size;}
    private static IEnumerable<Box> Boxes(byte[] data,int start,int end){while(start<end){var size=(int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(start));if(size<8||size>end-start)throw new Exception("Invalid owned fixture box.");yield return new(start,size,Encoding.ASCII.GetString(data,start+4,4));start+=size;}}
}
