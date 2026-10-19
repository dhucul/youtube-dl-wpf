using System.Buffers.Binary;

namespace YoutubeDl.Engine.Media;

// One instance per coded source stream. Resolve references before presentation
// reordering or clipping; never carry this state across independent periods.
internal sealed class DolbyRpuSequence
{
    private sealed record Mapping(bool[] Signature,bool[] Data);
    private sealed record Block(int Level,byte[] Data);
    private readonly Dictionary<int,Mapping> mappings=[];
    private readonly bool allowCompression;
    private readonly bool extended;
    internal DolbyRpuSequence(bool allowCompression=true,bool extended=true){this.allowCompression=allowCompression;this.extended=extended;}
    private bool[]? signal;
    private int? signalId;
    private List<Block> staticV1=[],staticV2=[];
    internal bool NeedsSignalReconstruction {get;private set;}
    internal byte[] Expand(byte[] nal)
    {
        if(nal.Length is <10 or >65536||HevcMetadataTransport.Type(nal)!=62||nal[2]!=25)throw new InvalidDataException("Invalid sequential Dolby RPU wrapper.");var raw=HdrBits.Unescape(nal.AsSpan(3));if(raw.Length<6||raw[^1]!=0x80||BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(raw.Length-5))!=DolbySpatialRpu.Crc(raw.AsSpan(0,raw.Length-5)))throw new InvalidDataException("Sequential Dolby RPU checksum mismatch.");var bits=new HdrBits(raw[..^5]);
        if(bits.Read(6)!=2||bits.Read(11)!=18||bits.Read(4) is not (0 or 1)||bits.Read(4)!=0||bits.Read(1)!=1)throw new NotSupportedException("Sequential Dolby expansion requires explicit type-2 HEVC sequence metadata.");if(bits.Read(1)!=0)throw new NotSupportedException("Explicit Dolby chroma filters are unsupported.");var coefficient=bits.Read(2);if(coefficient is not (0 or 1))throw new NotSupportedException("Unsupported Dolby coefficient representation.");var denom=coefficient==0?bits.Ue():32;if(denom is <1 or >32)throw new InvalidDataException("Invalid Dolby coefficient precision.");bits.Skip(3);var bl=bits.Ue()+8;var el=bits.Ue()+8;var vdr=bits.Ue()+8;if(bl!=10||el!=10||vdr!=12||bits.Read(1)!=0)throw new NotSupportedException("Sequential expansion requires ten-bit pictures and twelve-bit Dolby signal precision.");var compressionAt=bits.Position;var compression=bits.Read(3);if(compression>1)throw new NotSupportedException("Unsupported Dolby DM compression method.");bits.Read(1);var residual=bits.Read(1)==0;if(bits.Read(1)!=1)throw new NotSupportedException("Explicit Dolby display metadata is required.");var dependencyAt=bits.Position;var dependent=bits.Read(1)!=0;var headerEnd=bits.Position;if(!allowCompression&&(compression!=0||dependent))throw new InvalidDataException("Dolby sequence dependencies require container compression signaling.");var signature=bits.Data.Take(compressionAt).Concat(bits.Data.Skip(compressionAt+3).Take(dependencyAt-compressionAt-3)).ToArray();bool[] mapping;int id;
        if(dependent)
        {
            id=bits.Ue();if(id>15||!mappings.TryGetValue(id,out var previous)||!previous.Signature.SequenceEqual(signature))throw new InvalidDataException("Missing or incompatible previous Dolby mapping.");mapping=previous.Data;
        }
        else
        {
            var start=bits.Position;id=bits.Ue();if(id>15||bits.Ue()!=0||bits.Ue()!=0)throw new NotSupportedException("Unsupported Dolby mapping identifier/domain.");var pieces=new int[3];for(var c=0;c<3;c++){pieces[c]=bits.Ue()+1;if(pieces[c]>8)throw new InvalidDataException("Too many Dolby reshaping pieces.");var pivot=0;for(var i=0;i<=pieces[c];i++){var delta=bits.Read(bl);pivot+=delta;if(pivot>1023||i>0&&delta==0)throw new InvalidDataException("Invalid Dolby reshaping pivots.");}}
            if(residual){if(bits.Read(3)!=0)throw new NotSupportedException("Unsupported Dolby NLQ method.");bits.Skip(bl*2);}if(bits.Ue()!=0||bits.Ue()!=0)throw new NotSupportedException("Partitioned Dolby reshaping is unsupported.");
            for(var c=0;c<3;c++)for(var i=0;i<pieces[c];i++){var method=bits.Ue();int count;if(method==0){var order=bits.Ue()+1;if(order is <1 or >2||order==1&&bits.Read(1)!=0)throw new NotSupportedException("Unsupported Dolby polynomial.");count=order+1;}else if(method==1){var order=bits.Read(2)+1;if(order>3)throw new InvalidDataException("Invalid Dolby MMR order.");count=1+order*7;}else throw new InvalidDataException("Invalid Dolby mapping method.");for(var n=0;n<count;n++)Coef();}if(residual)for(var c=0;c<3;c++){bits.Skip(el);for(var n=0;n<3;n++)Coef();}mapping=bits.Data.GetRange(start,bits.Position-start).ToArray();
        }
        if(!extended&&id!=0)throw new InvalidDataException("Limited Dolby compression requires mapping ID zero.");var mapBits=new HdrBits();mapBits.Data.AddRange(mapping);mapBits.Ue();var canonicalMapping=new HdrBits();canonicalMapping.Ue(0);canonicalMapping.Data.AddRange(mapBits.Data.Skip(mapBits.Position));
        var dmStart=bits.Position;var affected=bits.Ue();var current=bits.Ue();if(affected>15||affected!=current)throw new NotSupportedException("Mismatched Dolby display metadata identifiers.");var scene=bits.Ue();var dmHeader=new HdrBits();dmHeader.Ue(0);dmHeader.Ue(0);dmHeader.Ue(scene);bool[] nextSignal;
        if(compression==0){var at=bits.Position;bits.Skip(509);nextSignal=bits.Data.GetRange(at,509).ToArray();}
        else{if(signal is null||signalId!=current)throw new InvalidDataException("Compressed Dolby display metadata has no matching static signal state.");nextSignal=signal;}
        var v1=Blocks();var v2=bits.Data.Count-bits.Position>7?Blocks():[];while(bits.Position<bits.Data.Count)if(bits.Read(1)!=0)throw new InvalidDataException("Invalid Dolby sequence alignment.");
        if(compression!=0&&(v1.Any(b=>Static(b.Level))||v2.Any(b=>Static(b.Level))))throw new InvalidDataException("Compressed Dolby metadata illegally repeats static extension blocks.");var nextV1=compression==0?v1.Where(b=>Static(b.Level)).ToList():staticV1;var nextV2=compression==0?v2.Where(b=>Static(b.Level)).ToList():staticV2;
        var output=new HdrBits();output.Data.AddRange(bits.Data.Take(headerEnd));for(var i=0;i<3;i++)output.Data[compressionAt+i]=false;output.Data[dependencyAt]=false;output.Data.AddRange(canonicalMapping.Data);output.Data.AddRange(dmHeader.Data);output.Data.AddRange(nextSignal);Write(compression==0?v1:nextV1.Concat(v1).ToList());var second=compression==0?v2:nextV2.Concat(v2).ToList();if(second.Count>0)Write(second);output.Align();var data=output.Bytes();var tail=new byte[5];BinaryPrimitives.WriteUInt32BigEndian(tail,DolbySpatialRpu.Crc(data));tail[4]=0x80;var result=new byte[]{0x7c,1,25}.Concat(HdrBits.Escape(data.Concat(tail))).ToArray();if(result.Length>65536)throw new InvalidDataException("Expanded Dolby metadata exceeds its bound.");
        // Failed frames never publish partial sequence state.
        var signalBits=new HdrBits();signalBits.Data.AddRange(nextSignal);var non=Enumerable.Range(0,9).Select(_=>unchecked((short)signalBits.Read(16))).ToArray();var off=Enumerable.Range(0,3).Select(_=>((uint)signalBits.Read(16)<<16)|(uint)signalBits.Read(16)).ToArray();var lin=Enumerable.Range(0,9).Select(_=>unchecked((short)signalBits.Read(16))).ToArray();var custom=signalBits.Read(16)!=65535|signalBits.Read(16)!=0|signalBits.Read(16)!=0|signalBits.Read(16)!=0|signalBits.Read(16)!=0|signalBits.Read(5)!=12|signalBits.Read(2)!=0|signalBits.Read(2)!=0|signalBits.Read(2)!=1;custom|=!non.Select(v=>(int)v).SequenceEqual(DolbySignalDomain.CanonicalNonlinear)||!off.SequenceEqual(DolbySignalDomain.CanonicalOffset)||!lin.Select(v=>(int)v).SequenceEqual(DolbySignalDomain.CanonicalLinear);
        mappings[id]=new(signature,mapping);NeedsSignalReconstruction|=custom;signal=nextSignal;signalId=current;staticV1=nextV1;staticV2=nextV2;return !dependent&&compression==0&&id==0&&current==0?nal:result;
        void Coef(){if(coefficient==0)bits.Ue();bits.Skip(denom);}
        List<Block> Blocks(){var count=bits.Ue();if(count>64)throw new InvalidDataException("Too many Dolby extension blocks.");bits.AlignRead();var list=new List<Block>();for(var i=0;i<count;i++){var size=bits.Ue();var level=bits.Read(8);if(size is <1 or >1024)throw new InvalidDataException("Invalid Dolby extension block length: "+size+" at "+bits.Position+"; dm "+dmStart);var bytes=new byte[size];for(var n=0;n<size;n++)bytes[n]=(byte)bits.Read(8);list.Add(new(level,bytes));}if(list.GroupBy(b=>b.Level).Any(g=>g.Key!=2&&g.Key!=10&&g.Count()>1))throw new InvalidDataException("Duplicate Dolby extension block.");return list;}
        void Write(List<Block> values){output.Ue(values.Count);output.Align();foreach(var block in values){output.Ue(block.Data.Length);output.Put(block.Level,8);foreach(var value in block.Data)output.Put(value,8);}}
    }
    private static bool Static(int level)=>level is 6 or 10 or 32 or 254 or 255;
}

