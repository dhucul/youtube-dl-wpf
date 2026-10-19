using System.Buffers.Binary;

namespace YoutubeDl.Engine.Media;

internal static class DolbySpatialRpu
{
    private sealed record Block(int Level,byte[] Bytes);
    private sealed record Parsed(HdrBits Bits,int PrefixEnd,List<Block> V1,List<Block> V2);
    internal static (int Left,int Right,int Top,int Bottom)? Area(byte[] nal)
    {
        var block=Parse(nal).V1.SingleOrDefault(block=>block.Level==5);if(block is null)return null;var bits=new HdrBits(block.Bytes);return(bits.Read(13),bits.Read(13),bits.Read(13),bits.Read(13));
    }
    internal static byte[] Map(byte[] nal,HdrSpatialGeometry geometry)
    {
        var old=Area(nal)??(0,0,0,0);return WithArea(nal,geometry.ActiveArea(old.Item1,old.Item2,old.Item3,old.Item4));
    }
    internal static string ContentIdentity(byte[] nal)
    {var value=Parse(nal);return string.Join("",value.Bits.Data.Take(value.PrefixEnd).Select(bit=>bit?'1':'0'))+"|"+string.Join(';',value.V1.Where(block=>block.Level!=5).Select(block=>block.Level+":"+Convert.ToHexString(block.Bytes)))+"|"+string.Join(';',value.V2.Select(block=>block.Level+":"+Convert.ToHexString(block.Bytes)));}
    internal static byte[] WithArea(byte[] nal,(int Left,int Right,int Top,int Bottom) area)
    {
        var parsed=Parse(nal);var active=new HdrBits();foreach(var value in new[]{area.Left,area.Right,area.Top,area.Bottom})active.Put(value,13);active.Align();
        var blocks=parsed.V1.ToList();var at=blocks.FindIndex(block=>block.Level==5);if(at>=0)blocks[at]=new(5,active.Bytes());else if(area!=(0,0,0,0))blocks.Add(new(5,active.Bytes()));else return nal;
        var result=new HdrBits();result.Data.AddRange(parsed.Bits.Data.Take(parsed.PrefixEnd));Write(blocks);if(parsed.V2.Count>0)Write(parsed.V2);result.Align();var bytes=result.Bytes();var crc=Crc(bytes);var data=new byte[bytes.Length+5];bytes.CopyTo(data,0);BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(bytes.Length),crc);data[^1]=0x80;var output=new byte[]{nal[0],nal[1],25}.Concat(HdrBits.Escape(data)).ToArray();
        if(Area(output)!=area)throw new InvalidDataException("The rewritten Dolby active area did not verify.");return output;
        void Write(IReadOnlyList<Block> values){result.Ue(values.Count);result.Align();foreach(var block in values){result.Ue(block.Bytes.Length);result.Put(block.Level,8);foreach(var value in block.Bytes)result.Put(value,8);}}
    }
    private static Parsed Parse(byte[] nal)
    {
        if(nal.Length is <10 or >65536||HevcMetadataTransport.Type(nal)!=62||nal[2]!=25)throw new InvalidDataException("Invalid canonical Dolby RPU wrapper.");var bytes=HdrBits.Unescape(nal.AsSpan(3));if(bytes.Length<6||bytes[^1]!=0x80||BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(bytes.Length-5))!=Crc(bytes.AsSpan(0,bytes.Length-5)))throw new InvalidDataException("The canonical Dolby RPU checksum is invalid.");
        var bits=new HdrBits(bytes[..^5]);if(bits.Read(6)!=2||bits.Read(11)!=18||bits.Read(4) is not (0 or 1))throw new NotSupportedException("Unsupported canonical Dolby RPU profile.");bits.Skip(4);if(bits.Read(1)!=1)throw new NotSupportedException("Canonical Dolby sequence information is required.");bits.Skip(1);var coefficient=bits.Read(2);if(coefficient!=0)throw new NotSupportedException("Dolby spatial remapping currently requires fixed-point reshaping coefficients.");var denominator=bits.Ue();if(denominator is <1 or >32)throw new NotSupportedException("Unsupported Dolby coefficient precision.");bits.Skip(3);var depth=bits.Ue()+8;bits.Ue();bits.Ue();if(depth!=10||bits.Read(1)!=0||bits.Read(3)!=0||bits.Read(1)!=0||bits.Read(1)!=1||bits.Read(1)!=1||bits.Read(1)!=0)throw new NotSupportedException("Dolby remapping requires complete independent non-residual, uncompressed frame metadata.");bits.Ue();bits.Ue();bits.Ue();var pieces=new int[3];for(var channel=0;channel<3;channel++){pieces[channel]=bits.Ue()+1;if(pieces[channel]>8)throw new InvalidDataException("Too many Dolby reshaping pieces.");bits.Skip((pieces[channel]+1)*depth);}bits.Ue();bits.Ue();
        for(var channel=0;channel<3;channel++)for(var part=0;part<pieces[channel];part++)
        {
            var mapping=bits.Ue();if(mapping==0){var order=bits.Ue()+1;if(order is <1 or >2)throw new InvalidDataException("Invalid Dolby polynomial order.");if(order==1&&bits.Read(1)!=0)throw new NotSupportedException("Interpolated Dolby coefficients are unsupported.");for(var k=0;k<=order;k++)Coef();}
            else if(mapping==1){var order=bits.Read(2)+1;if(order>3)throw new InvalidDataException("Invalid Dolby MMR order.");Coef();for(var k=0;k<order*7;k++)Coef();}else throw new InvalidDataException("Invalid Dolby mapping type.");
        }
        bits.Ue();bits.Ue();bits.Ue();bits.Skip(9*16+3*32+9*16+3*16+32+5+2+2+2+12+12+10);var prefix=bits.Position;var first=Read();var second=bits.Data.Count-bits.Position>7?Read():[];while(bits.Position<bits.Data.Count)if(bits.Read(1)!=0)throw new InvalidDataException("Invalid Dolby RPU padding.");return new(bits,prefix,first,second);
        void Coef(){bits.Ue();bits.Skip(denominator);}
        List<Block> Read(){var count=bits.Ue();if(count>64)throw new InvalidDataException("Too many Dolby extension blocks.");bits.AlignRead();var blocks=new List<Block>();for(var i=0;i<count;i++){var length=bits.Ue();var level=bits.Read(8);if(length is <1 or >1024)throw new InvalidDataException("Invalid Dolby extension length.");var block=new byte[length];for(var n=0;n<length;n++)block[n]=(byte)bits.Read(8);blocks.Add(new(level,block));}if(blocks.Count(block=>block.Level==5)>1)throw new InvalidDataException("Duplicate Dolby active-area metadata.");return blocks;}
    }
    internal static uint Crc(ReadOnlySpan<byte> bytes){var crc=uint.MaxValue;foreach(var value in bytes){crc^=(uint)value<<24;for(var bit=0;bit<8;bit++)crc=(crc&0x80000000)!=0?(crc<<1)^0x04c11db7:crc<<1;}return crc;}
}
