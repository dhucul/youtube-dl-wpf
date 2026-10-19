using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Media;

internal static class HdrMelPacketFixture
{
    internal static async Task Rewrite(string input,string output,string packetJson,string mode,byte[]? rpuOverride=null,string? elFile=null,string? elJson=null)
    {
        var source=await File.ReadAllBytesAsync(input);using var doc=JsonDocument.Parse(packetJson);var packets=doc.RootElement.GetProperty("packets").EnumerateArray().Select(packet=>(Pos:int.Parse(packet.GetProperty("pos").GetString()!),Size:int.Parse(packet.GetProperty("size").GetString()!))).OrderBy(packet=>packet.Pos).ToArray();var replacements=new List<byte[]>();byte[]? elBytes=elFile is null?null:await File.ReadAllBytesAsync(elFile);using var elDoc=elJson is null?null:JsonDocument.Parse(elJson);var elPackets=elDoc?.RootElement.GetProperty("packets").EnumerateArray().ToArray();
        for(var i=0;i<packets.Length;i++)
        {
            using var sample=new MemoryStream();var data=source.AsSpan(packets[i].Pos,packets[i].Size).ToArray();var nals=new List<byte[]>();var cursor=0;while(cursor<data.Length){var size=(int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(cursor));cursor+=4;nals.Add(data.AsSpan(cursor,size).ToArray());cursor+=size;}
            foreach(var nal in nals.Where(n=>HevcMetadataTransport.Type(n)!=62))Write(nal);
            var enhancement=nals;if(elBytes is not null){var ep=elPackets![i];var pos=int.Parse(ep.GetProperty("pos").GetString()!);var length=int.Parse(ep.GetProperty("size").GetString()!);enhancement=[];while(length>0){var n=(int)BinaryPrimitives.ReadUInt32BigEndian(elBytes.AsSpan(pos));enhancement.Add(elBytes.AsSpan(pos+4,n).ToArray());pos+=n+4;length-=n+4;}}
            if(mode!="missing-layer"&&!mode.StartsWith("single-",StringComparison.Ordinal))foreach(var nal in enhancement.Where(n=>HevcMetadataTransport.Type(n)<41&&HevcMetadataTransport.Type(n)!=35))Write(new byte[]{126,1}.Concat(nal).ToArray());
            var rpu=HdrTransformChecks.Mel;if(mode=="fel")rpu=HdrTransformChecks.Fel;if(rpuOverride is not null)rpu=rpuOverride;if(mode.StartsWith("single-",StringComparison.Ordinal))rpu=mode is "single-extended-ids" or "single-limited-ids"?i==0?HdrSelectorFixtures.FullId15:HdrSelectorFixtures.RefId15:mode=="single-custom-single"?HdrDomainFixtures.CustomSingle:mode=="single-custom-linear"?HdrDomainFixtures.CustomLinear:i==0?HdrDomainFixtures.Independent:mode=="single-dependent"?HdrDomainFixtures.Dependent:mode=="single-missing-reference"?HdrDomainFixtures.MissingReference:HdrDomainFixtures.Compressed;Write(rpu);replacements.Add(sample.ToArray());
            void Write(byte[] nal){var length=new byte[4];BinaryPrimitives.WriteUInt32BigEndian(length,(uint)nal.Length);sample.Write(length);sample.Write(nal);}
        }
        using var result=new MemoryStream();var at=0;for(var i=0;i<packets.Length;i++){result.Write(source,at,packets[i].Pos-at);result.Write(replacements[i]);at=packets[i].Pos+packets[i].Size;}result.Write(source,at,source.Length-at);var target=result.ToArray();var delta=target.Length-source.Length;var sourceTop=Boxes(source,0,source.Length).ToArray();var mdat=sourceTop.Single(box=>box.Type=="mdat");if(sourceTop.Single(box=>box.Type=="moov").Start<mdat.End)throw new Exception("The fixture movie index must follow media data.");BinaryPrimitives.WriteUInt32BigEndian(target.AsSpan(mdat.Start),(uint)(mdat.Size+delta));Walk(0,target.Length,false);var cfg=target.AsSpan().IndexOf("dvvC"u8);if(cfg<0)cfg=target.AsSpan().IndexOf("dvcC"u8);if(cfg<0)throw new Exception("Missing fixture Dolby configuration.");if(mode.StartsWith("single-",StringComparison.Ordinal)){if(mode is "single-extended-ids" or "single-limited-ids")target[cfg+8]|=(byte)(mode=="single-extended-ids"?12:4);if(mode is "single-compressed" or "single-dependent" or "single-missing-reference")target[cfg+8]|=4;}else{target[cfg+6]=(byte)((7<<1)|(target[cfg+6]&1));target[cfg+7]|=2;target[cfg+8]=(byte)((target[cfg+8]&15)|0x60);}await File.WriteAllBytesAsync(output,target);
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
