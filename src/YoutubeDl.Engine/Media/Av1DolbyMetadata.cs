namespace YoutubeDl.Engine.Media;

internal static class Av1DolbyMetadata
{
    internal static IReadOnlyList<byte[]> Rpus(byte[] packet,DolbyRpuSequence? sequence=null)
    {
        var result=new List<byte[]>();var at=0;
        while(at<packet.Length)
        {
            var header=packet[at++];if((header&0x81)!=0||(header&2)==0)throw new NotSupportedException("AV1 Dolby transport requires sized, valid OBU headers.");var type=(header>>3)&15;if((header&4)!=0){if(at>=packet.Length||packet[at++]!=0)throw new NotSupportedException("AV1 temporal/spatial enhancement metadata is unsupported.");}var length=Leb();if(length>packet.Length-at)throw new InvalidDataException("Truncated AV1 OBU.");var end=at+length;
            if(type==5)
            {
                var metadata=Leb();if(metadata==4&&end-at>=7&&packet[at]==0xb5&&packet[at+1]==0&&packet[at+2]==0x3b)
                {
                    var bits=new HdrBits(packet.AsSpan(at,end-at).ToArray());bits.Skip(24);if(bits.Read(16)!=0||bits.Read(16)!=0x800||bits.Read(27)!=0x01be6841)throw new NotSupportedException("Unsupported AV1 Dolby EMDF wrapper.");var size=bits.Read(8);if(bits.Read(1)==1){size=((size+1)<<8)|bits.Read(8);if(bits.Read(1)!=0)throw new InvalidDataException("Invalid AV1 Dolby size delimiter.");}if(size is <6 or >65536)throw new InvalidDataException("Invalid AV1 Dolby payload size.");var raw=new byte[size];for(var i=0;i<size;i++)raw[i]=(byte)bits.Read(8);var nal=new byte[]{0x7c,1,25}.Concat(HdrBits.Escape(raw)).ToArray();if(bits.Read(17)!=0x400)throw new InvalidDataException("Invalid AV1 Dolby protection footer.");if(sequence is not null)nal=sequence.Expand(nal);_ = DolbySpatialRpu.Area(nal);result.Add(nal);
                }
            }
            at=end;
        }
        return result;
        int Leb(){var value=0;var shift=0;for(var i=0;i<5;i++){if(at>=packet.Length)throw new InvalidDataException("Truncated AV1 size field.");var next=packet[at++];if(shift==28&&(next&0x7f)>7)throw new InvalidDataException("Oversized AV1 size.");value|=(next&0x7f)<<shift;if((next&0x80)==0)return value;shift+=7;}throw new InvalidDataException("Oversized AV1 size field.");}
    }
}
