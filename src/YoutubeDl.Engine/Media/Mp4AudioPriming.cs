using System.Buffers.Binary;
using System.Text;

namespace YoutubeDl.Engine.Media;

/// <summary>Retains an input's complete AAC priming trim when a positive delay makes its first packet nonnegative.</summary>
internal static class Mp4AudioPriming
{
    internal sealed record Plan(uint Samples,int SampleRate,decimal DelaySeconds);
    private readonly record struct Box(string Type,int Data,int End);
    internal static async Task ApplyAsync(string path,uint skippedSamples,int sampleRate,CancellationToken ct,decimal? declaredDelaySeconds=null)
    {
        if(skippedSamples==0)return;
        await using var file=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None,8192,FileOptions.Asynchronous);
        var header=new byte[16];long offset=-1;byte[]? movie=null;var count=0;var headerBytes=0;var trailing=false;var growth=0;
        while(file.Position<file.Length)
        {
            ct.ThrowIfCancellationRequested();if(++count>4096||file.Length-file.Position<8)throw new InvalidDataException("The live movie box inventory is invalid.");
            var start=file.Position;await file.ReadExactlyAsync(header.AsMemory(0,8),ct).ConfigureAwait(false);ulong size=BinaryPrimitives.ReadUInt32BigEndian(header);var bytes=8;
            if(size==1){await file.ReadExactlyAsync(header.AsMemory(8,8),ct).ConfigureAwait(false);size=BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));bytes=16;}if(size==0)size=(ulong)(file.Length-start);
            if(size<(ulong)bytes||size>(ulong)(file.Length-start))throw new InvalidDataException("The live movie contains a truncated box.");
            if(Encoding.ASCII.GetString(header,4,4)=="moov")
            {if(movie is not null||size-(ulong)bytes>8*1024*1024)throw new InvalidDataException("The live movie header is duplicate or excessive.");offset=file.Position;headerBytes=bytes;trailing=start+(long)size==file.Length;movie=new byte[(int)size-bytes];await file.ReadExactlyAsync(movie,ct).ConfigureAwait(false);}
            file.Position=start+(long)size;
        }
        if(movie is null)throw new InvalidDataException("The live movie has no movie header.");
        var top=Boxes(movie,0,movie.Length);var mvhd=top.Single(box=>box.Type=="mvhd");var movieScale=Scale(movie,mvhd);var changed=0;
        foreach(var track in top.Where(box=>box.Type=="trak"))
        {
            var children=Boxes(movie,track.Data,track.End);var media=children.Single(box=>box.Type=="mdia");var md=Boxes(movie,media.Data,media.End);var handler=md.Single(box=>box.Type=="hdlr");
            if(handler.Data+12>handler.End||Encoding.ASCII.GetString(movie,handler.Data+8,4)!="soun")continue;
            var scale=Scale(movie,md.Single(box=>box.Type=="mdhd"));if(scale!=(uint)sampleRate)throw new NotSupportedException("AAC priming uses an unsupported movie time base.");
            var edits=children.Single(box=>box.Type=="edts");var list=Boxes(movie,edits.Data,edits.End).Single(box=>box.Type=="elst");
            if(list.Data+8>list.End||movie[list.Data] is not (0 or 1))throw new NotSupportedException("The live audio edit list has an unsupported version.");
            var version=movie[list.Data];var step=version==1?20:12;var entries=BinaryPrimitives.ReadUInt32BigEndian(movie.AsSpan(list.Data+4));
            if(entries is not (1 or 2)||list.Data+8+entries*step!=list.End)throw new NotSupportedException("The delayed live audio has no supported priming edit layout.");
            var first=list.Data+8;var second=first+step;
            long Time(int position)=>version==1?BinaryPrimitives.ReadInt64BigEndian(movie.AsSpan(position+8)):BinaryPrimitives.ReadInt32BigEndian(movie.AsSpan(position+4));
            ulong Duration(int position)=>version==1?BinaryPrimitives.ReadUInt64BigEndian(movie.AsSpan(position)):BinaryPrimitives.ReadUInt32BigEndian(movie.AsSpan(position));
            var trim=checked((ulong)Math.Round(skippedSamples*(decimal)movieScale/sampleRate,MidpointRounding.AwayFromZero));
            ulong total;ulong delay;
            if(entries==1)
            {
                if(declaredDelaySeconds is not >0||Time(first)<0||Time(first)>skippedSamples||!trailing||headerBytes!=8)
                    throw new NotSupportedException("This short-delay priming correction requires a bounded trailing movie header.");
                total=Duration(first);delay=checked((ulong)decimal.Round(declaredDelaySeconds.Value*movieScale,0,MidpointRounding.AwayFromZero));
                var expanded=new byte[checked(movie.Length+step)];if(expanded.Length>8*1024*1024)throw new InvalidDataException("The corrected movie header exceeds its limit.");
                movie.AsSpan(0,first).CopyTo(expanded);movie.AsSpan(first).CopyTo(expanded.AsSpan(first+step));movie=expanded;growth=step;
                foreach(var container in new[]{track,edits,list})BinaryPrimitives.WriteUInt32BigEndian(movie.AsSpan(container.Data-8),checked(BinaryPrimitives.ReadUInt32BigEndian(movie.AsSpan(container.Data-8))+(uint)step));
                BinaryPrimitives.WriteUInt32BigEndian(movie.AsSpan(list.Data+4),2);
                if(version==1)BinaryPrimitives.WriteInt64BigEndian(movie.AsSpan(first+8),-1);else BinaryPrimitives.WriteInt32BigEndian(movie.AsSpan(first+4),-1);
                BinaryPrimitives.WriteUInt32BigEndian(movie.AsSpan(first+step-4),0x00010000);
            }
            else
            {
                if(Time(first)==-1&&Time(second)==skippedSamples&&declaredDelaySeconds is { } preserved&&
                    Duration(first)==checked((ulong)decimal.Round(preserved*movieScale,0,MidpointRounding.AwayFromZero)))return;
                if(Time(first)!=-1||Time(second)!=0)throw new NotSupportedException("The live audio edit list cannot safely restore source priming.");
                total=checked(Duration(first)+Duration(second));delay=declaredDelaySeconds is { } declared?
                    checked((ulong)decimal.Round(declared*movieScale,0,MidpointRounding.AwayFromZero)):checked(Duration(first)+trim);
            }
            if(total<=delay)throw new InvalidDataException("The delayed source priming exceeds the live audio duration.");
            WriteDuration(first,delay);WriteDuration(second,total-delay);
            if(version==1)BinaryPrimitives.WriteInt64BigEndian(movie.AsSpan(second+8),skippedSamples);else BinaryPrimitives.WriteInt32BigEndian(movie.AsSpan(second+4),checked((int)skippedSamples));changed++;
            void WriteDuration(int position,ulong value){if(version==1)BinaryPrimitives.WriteUInt64BigEndian(movie.AsSpan(position),value);else BinaryPrimitives.WriteUInt32BigEndian(movie.AsSpan(position),checked((uint)value));}
            break;
        }
        if(changed!=1)throw new InvalidDataException("The paired movie has no unique audio priming target.");
        if(growth>0){file.Position=offset-headerBytes;BinaryPrimitives.WriteUInt32BigEndian(header,checked((uint)(movie.Length+headerBytes)));await file.WriteAsync(header.AsMemory(0,4),ct).ConfigureAwait(false);}
        file.Position=offset;await file.WriteAsync(movie,ct).ConfigureAwait(false);if(growth>0)file.SetLength(offset+movie.Length);await file.FlushAsync(ct).ConfigureAwait(false);
    }
    internal static async Task<Plan?> ReadPlanAsync(string path,CancellationToken ct)
    {
        if(Path.GetExtension(path) is not (".mp4" or ".m4a" or ".mov"))return null;
        await using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,8192,FileOptions.Asynchronous);var header=new byte[16];byte[]? movie=null;var count=0;
        while(file.Position<file.Length)
        {
            ct.ThrowIfCancellationRequested();if(++count>4096||file.Length-file.Position<8)throw new InvalidDataException("The movie box inventory is invalid.");
            var start=file.Position;await file.ReadExactlyAsync(header.AsMemory(0,8),ct).ConfigureAwait(false);ulong size=BinaryPrimitives.ReadUInt32BigEndian(header);var bytes=8;
            if(size==1){await file.ReadExactlyAsync(header.AsMemory(8,8),ct).ConfigureAwait(false);size=BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));bytes=16;}if(size==0)size=(ulong)(file.Length-start);
            if(size<(ulong)bytes||size>(ulong)(file.Length-start))throw new InvalidDataException("The movie box is truncated.");
            if(Encoding.ASCII.GetString(header,4,4)=="moov"){if(movie is not null||size-(ulong)bytes>8*1024*1024)throw new InvalidDataException("The movie header is duplicate or excessive.");movie=new byte[(int)size-bytes];await file.ReadExactlyAsync(movie,ct).ConfigureAwait(false);}file.Position=start+(long)size;
        }
        if(movie is null)return null;var top=Boxes(movie,0,movie.Length);var movieScale=Scale(movie,top.Single(b=>b.Type=="mvhd"));
        foreach(var track in top.Where(b=>b.Type=="trak"))
        {
            var children=Boxes(movie,track.Data,track.End);var mdia=children.Single(b=>b.Type=="mdia");var media=Boxes(movie,mdia.Data,mdia.End);var handler=media.Single(b=>b.Type=="hdlr");
            if(handler.Data+12>handler.End||Encoding.ASCII.GetString(movie,handler.Data+8,4)!="soun")continue;
            var minf=media.Single(b=>b.Type=="minf");var stbl=Boxes(movie,minf.Data,minf.End).Single(b=>b.Type=="stbl");var stsd=Boxes(movie,stbl.Data,stbl.End).Single(b=>b.Type=="stsd");
            if(stsd.Data+8>stsd.End||!Boxes(movie,stsd.Data+8,stsd.End).Any(b=>b.Type=="mp4a"))continue;
            var edts=children.SingleOrDefault(b=>b.Type=="edts");if(edts.End==0)continue;var list=Boxes(movie,edts.Data,edts.End).Single(b=>b.Type=="elst");
            var version=movie[list.Data];if(version is not (0 or 1)||list.Data+8+(version==1?40:24)!=list.End||BinaryPrimitives.ReadUInt32BigEndian(movie.AsSpan(list.Data+4))!=2)continue;
            var first=list.Data+8;var second=first+(version==1?20:12);var empty=version==1?BinaryPrimitives.ReadInt64BigEndian(movie.AsSpan(first+8)):BinaryPrimitives.ReadInt32BigEndian(movie.AsSpan(first+4));
            var skipped=version==1?BinaryPrimitives.ReadInt64BigEndian(movie.AsSpan(second+8)):BinaryPrimitives.ReadInt32BigEndian(movie.AsSpan(second+4));
            var delay=version==1?BinaryPrimitives.ReadUInt64BigEndian(movie.AsSpan(first)):BinaryPrimitives.ReadUInt32BigEndian(movie.AsSpan(first));
            if(empty!=-1||skipped<=0||skipped>uint.MaxValue||delay==0)continue;return new(checked((uint)skipped),checked((int)Scale(movie,media.Single(b=>b.Type=="mdhd"))),delay/(decimal)movieScale);
        }
        return null;
    }
    private static uint Scale(byte[] data,Box box)
    {var offset=box.Data+(data[box.Data]==1?20:12);if(data[box.Data] is not (0 or 1)||offset+4>box.End)throw new InvalidDataException("The movie timing header is invalid.");var value=BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));return value>0?value:throw new InvalidDataException("The movie time scale is zero.");}
    private static List<Box> Boxes(byte[] data,int start,int end)
    {
        var result=new List<Box>();while(start<end){if(end-start<8||result.Count>=4096)throw new InvalidDataException("The movie box list is truncated or excessive.");var size=BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(start));if(size<8||size>(uint)(end-start))throw new InvalidDataException("The movie box size is invalid.");result.Add(new(Encoding.ASCII.GetString(data,start+4,4),start+8,start+(int)size));start+=(int)size;}return result;
    }
}
