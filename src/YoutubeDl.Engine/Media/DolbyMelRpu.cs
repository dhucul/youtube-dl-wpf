using System.Buffers.Binary;

namespace YoutubeDl.Engine.Media;

// A MEL has a provably neutral NLQ contribution. FEL coefficients are never discarded.
internal static class DolbyMelRpu
{
    internal static byte[] ToSingleLayer(byte[] nal)
    {
        if(nal.Length is <10 or >65536||HevcMetadataTransport.Type(nal)!=62||nal[2]!=25)throw new InvalidDataException("Invalid Dolby enhancement RPU wrapper.");
        var bytes=HdrBits.Unescape(nal.AsSpan(3));if(bytes.Length<6||bytes[^1]!=0x80||BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(bytes.Length-5))!=DolbySpatialRpu.Crc(bytes.AsSpan(0,bytes.Length-5)))throw new InvalidDataException("Invalid Dolby enhancement RPU checksum.");
        var bits=new HdrBits(bytes[..^5]);if(bits.Read(6)!=2||bits.Read(11)!=18||bits.Read(4)!=1||bits.Read(4)!=0||bits.Read(1)!=1||bits.Read(1)!=0||bits.Read(2)!=0)throw new NotSupportedException("MEL conversion requires complete fixed-point profile-7 metadata.");
        var denominator=bits.Ue();if(denominator is <1 or >23)throw new NotSupportedException("Unsupported MEL coefficient precision.");bits.Read(2);if(bits.Read(1)!=0||bits.Ue()!=2||bits.Ue()!=2||bits.Ue()!=4||bits.Read(1)!=0||bits.Read(3)!=0)throw new NotSupportedException("Unsupported MEL bit-depth or spatial processing.");
        var elFlag=bits.Position;if(bits.Read(1)!=1||bits.Read(1)!=0||bits.Read(1)!=1||bits.Read(1)!=0)throw new NotSupportedException("MEL conversion requires independent uncompressed display metadata.");
        bits.Ue();bits.Ue();bits.Ue();var pieces=new int[3];for(var c=0;c<3;c++){pieces[c]=bits.Ue()+1;if(pieces[c]>8)throw new InvalidDataException("Too many MEL reshaping pieces.");bits.Skip((pieces[c]+1)*10);}
        var nlqHeader=bits.Position;if(bits.Read(3)!=0||bits.Read(10)!=0||bits.Read(10)!=1023)throw new NotSupportedException("Unsupported Dolby NLQ predictor.");var nlqHeaderEnd=bits.Position;
        if(bits.Ue()!=0||bits.Ue()!=0)throw new NotSupportedException("Partitioned Dolby enhancement mappings are unsupported.");
        for(var c=0;c<3;c++)for(var i=0;i<pieces[c];i++)
        {
            var mapping=bits.Ue();if(mapping==0){var order=bits.Ue()+1;if(order is <1 or >2)throw new InvalidDataException("Invalid MEL polynomial order.");if(order==1&&bits.Read(1)!=0)throw new NotSupportedException("Interpolated MEL mappings are unsupported.");for(var n=0;n<=order;n++)Coef();}
            else if(mapping==1){var order=bits.Read(2)+1;if(order>3)throw new InvalidDataException("Invalid MEL MMR order.");for(var n=0;n<1+order*7;n++)Coef();}else throw new InvalidDataException("Invalid MEL mapping type.");
        }
        var nlq=bits.Position;for(var c=0;c<3;c++)if(bits.Read(10)!=0||bits.Ue()!=1||!Zero(denominator)||bits.Ue()!=0||!Zero(denominator)||bits.Ue()!=0||!Zero(denominator))throw new NotSupportedException("Dolby Vision FEL residual reconstruction is unavailable; its enhancement layer cannot be discarded.");var nlqEnd=bits.Position;
        bits.Ue();bits.Ue();bits.Ue();bits.Skip(9*16+3*32+9*16+3*16+32+5+2+2+2+12+12+10);var prefixEnd=bits.Position;var v1=Blocks();var v2=bits.Data.Count-bits.Position>7?Blocks():[];while(bits.Position<bits.Data.Count)if(bits.Read(1)!=0)throw new InvalidDataException("Nonzero MEL trailing padding.");
        var result=new HdrBits();result.Data.AddRange(bits.Data.Take(nlqHeader));result.Data[elFlag]=false;result.Data[elFlag+1]=true;result.Data.AddRange(bits.Data.GetRange(nlqHeaderEnd,nlq-nlqHeaderEnd));result.Data.AddRange(bits.Data.GetRange(nlqEnd,prefixEnd-nlqEnd));Write(v1);if(v2.Count>0)Write(v2);result.Align();var data=result.Bytes();var tail=new byte[5];BinaryPrimitives.WriteUInt32BigEndian(tail,DolbySpatialRpu.Crc(data));tail[4]=0x80;var output=new byte[]{0x7c,1,25}.Concat(HdrBits.Escape(data.Concat(tail))).ToArray();DolbySpatialRpu.Area(output);return output;
        void Coef(){bits.Ue();bits.Skip(denominator);}
        bool Zero(int count){for(var i=0;i<count;i++)if(bits.Read(1)!=0)return false;return true;}
        List<(int Level,byte[] Data)> Blocks(){var count=bits.Ue();if(count>64)throw new InvalidDataException("Too many MEL extension blocks.");bits.AlignRead();var values=new List<(int,byte[])>();for(var i=0;i<count;i++){var length=bits.Ue();var level=bits.Read(8);if(length is <1 or >1024)throw new InvalidDataException("Invalid MEL extension length.");var block=new byte[length];for(var j=0;j<length;j++)block[j]=(byte)bits.Read(8);values.Add((level,block));}return values;}
        void Write(List<(int Level,byte[] Data)> values){result.Ue(values.Count);result.Align();foreach(var value in values){result.Ue(value.Data.Length);result.Put(value.Level,8);foreach(var b in value.Data)result.Put(b,8);}}
    }
}
