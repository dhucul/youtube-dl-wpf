using System.Buffers.Binary;

namespace YoutubeDl.Engine.Media;

internal sealed record HdrPixelStatistics(int[] MaxScl,int Average,int Bright,uint[] Bins)
{
    internal int Quantile(int percent)
    {
        if(percent is <0 or >100)throw new InvalidDataException("Invalid HDR percentile.");var total=Bins.Aggregate(0UL,(sum,n)=>sum+n);if(total==0)throw new InvalidDataException("Empty HDR histogram.");var rank=Math.Max(1UL,(total*(ulong)percent+99)/100);ulong sum=0;for(var i=0;i<Bins.Length;i++){sum+=Bins[i];if(sum>=rank)return i;}throw new InvalidDataException("Incomplete HDR histogram.");
    }
}
internal static class HdrPixelHistogram
{
    // RGB is measured after final ten-bit 4:2:0 encoding, in linear BT.2020 at 10,000-nit normalization.
    internal static HdrPixelStatistics Measure(ReadOnlySpan<byte> gbrFloat,int pixels,CancellationToken ct=default)
    {
        if(pixels is <=0 or >3840*2160||gbrFloat.Length!=checked(pixels*12))throw new InvalidDataException("Invalid HDR histogram raster.");return Measure(gbrFloat,pixels,1,new(0,0,pixels-1,0),ct);
    }
    internal static HdrPixelStatistics Measure(ReadOnlySpan<byte> gbrFloat,int width,int height,HdrPlusWindow window,CancellationToken ct=default)
    {
        var pixels=checked(width*height);if(width<=0||height<=0||pixels>3840*2160||gbrFloat.Length!=checked(pixels*12)||window.X0<0||window.Y0<0||window.X1>=width||window.Y1>=height||window.X0>window.X1||window.Y0>window.Y1)throw new InvalidDataException("Invalid HDR histogram window.");var selected=checked((window.X1-window.X0+1)*(window.Y1-window.Y0+1));var bins=new uint[100001];var max=new double[3];double sum=0;var peak=0;
        for(var y=window.Y0;y<=window.Y1;y++)for(var x=window.X0;x<=window.X1;x++)
        {
            var i=y*width+x;if((x-window.X0&16383)==0)ct.ThrowIfCancellationRequested();var g=Read(gbrFloat,i);var b=Read(gbrFloat,pixels+i);var r=Read(gbrFloat,2*pixels+i);max[0]=Math.Max(max[0],r);max[1]=Math.Max(max[1],g);max[2]=Math.Max(max[2],b);var v=Math.Max(r,Math.Max(g,b));sum+=v;var bin=Q(v);bins[bin]++;peak=Math.Max(peak,bin);
        }
        return new(max.Select(Q).ToArray(),Q(sum/selected),(int)Math.Round(1000d*bins[peak]/selected,MidpointRounding.AwayFromZero),bins);
    }
    private static double Read(ReadOnlySpan<byte> data,int i){var value=BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(i*4)));if(!float.IsFinite(value))throw new InvalidDataException("Nonfinite HDR decoded sample.");return Math.Clamp((double)value,0,1);}
    private static int Q(double v)=>(int)Math.Clamp(Math.Round(v*100000,MidpointRounding.AwayFromZero),0,100000);
}
internal static class HdrPlusHistogram
{
    internal static IReadOnlyList<HdrPlusWindow> Windows(byte[] payload,int width,int height)
    {
        var windows=new List<HdrPlusWindow>{new(0,0,width-1,height-1)};Rewrite(payload,null,w=>{w.Validate(width,height);windows.Add(w);return w;});return windows;
    }
    internal static byte[] Map(byte[] payload,HdrAffineCanvas canvas,string? authoring=null)=>Rewrite(payload,null,w=>w.Map(canvas,authoring));
    internal static byte[] Regenerate(byte[] payload,HdrPixelStatistics stats)=>Regenerate(payload,new[]{stats});
    internal static byte[] Regenerate(byte[] payload,IReadOnlyList<HdrPixelStatistics> stats)=>Rewrite(payload,stats,null);
    private static byte[] Rewrite(byte[] payload,IReadOnlyList<HdrPixelStatistics>? stats,Func<HdrPlusWindow,HdrPlusWindow>? map)
    {
        if(!HevcMetadataTransport.HdrPlus(payload)||payload.Length>4096||payload[6]!=1)throw new NotSupportedException("Exact HDR statistics require HDR10+ application version 1.");var input=new HdrBits(payload);var output=new HdrBits();Copy(56);var windows=input.Read(2);if(windows is <1 or >3||stats is not null&&stats.Count!=windows)throw new InvalidDataException("HDR10+ window/statistic inventories differ.");output.Put(windows,2);
        for(var i=1;i<windows;i++)
        {
            var w=new HdrPlusWindow(input.Read(16),input.Read(16),input.Read(16),input.Read(16),input.Read(16),input.Read(16),input.Read(8),input.Read(16),input.Read(16),input.Read(16),input.Read(1));var m=map is null?w:map(w);foreach(var v in new[]{m.X0,m.Y0,m.X1,m.Y1,m.CenterX,m.CenterY})output.Put(v,16);output.Put(m.Angle,8);foreach(var v in new[]{m.Inner,m.Outer,m.Minor})output.Put(v,16);output.Put(m.Overlap,1);
        }
        Copy(27);Grid();
        for(var window=0;window<windows;window++)
        {
            var start=input.Position;input.Skip(3*17+17);var count=input.Read(4);var percentages=new List<int>();var previous=-1;var values=new List<int>();for(var i=0;i<count;i++){var p=input.Read(7);var v=input.Read(17);if(p<=previous||p>100||v>100000)throw new InvalidDataException("Invalid HDR percentile inventory.");percentages.Add(p);values.Add(v);previous=p;}input.Skip(10);
            var reserved=count==9&&percentages[1]==5&&percentages[2]==10;if(reserved&&(values[1]!=0||values[2]!=255))throw new NotSupportedException("Extended HDR percentile semantics require a separate declared metadata application.");if(stats is null){output.Data.AddRange(input.Data.GetRange(start,input.Position-start));continue;}
            var measured=stats[window];if(measured.MaxScl.Length!=3||measured.MaxScl.Any(v=>v is <0 or >100000)||measured.Average is <0 or >100000||measured.Bright is <0 or >1000)throw new InvalidDataException("Invalid measured HDR statistics.");if(count==0)percentages.AddRange([1,25,50,75,90,95,99,100]);foreach(var v in measured.MaxScl)output.Put(v,17);output.Put(measured.Average,17);output.Put(percentages.Count,4);for(var i=0;i<percentages.Count;i++){var p=percentages[i];output.Put(p,7);output.Put(reserved&&i==1?0:reserved&&i==2?255:measured.Quantile(p),17);}output.Put(measured.Bright,10);
        }
        Grid();for(var i=0;i<windows;i++){var tone=input.Read(1);output.Put(tone,1);if(tone!=0){Copy(24);var anchors=input.Read(4);output.Put(anchors,4);Copy(anchors*10);}var saturation=input.Read(1);output.Put(saturation,1);if(saturation!=0)Copy(6);}if(input.Data.Count-input.Position>7)throw new InvalidDataException("Unexpected HDR payload suffix.");while(input.Position<input.Data.Count)if(input.Read(1)!=0)throw new InvalidDataException("Nonzero HDR alignment bits.");output.Align();return output.Bytes();
        void Copy(int n){var at=input.Position;input.Skip(n);output.Data.AddRange(input.Data.GetRange(at,n));}
        void Grid(){var flag=input.Read(1);output.Put(flag,1);if(flag==0)return;var rows=input.Read(5);var columns=input.Read(5);if(rows is <2 or >25||columns is <2 or >25)throw new InvalidDataException("Invalid display peak grid.");output.Put(rows,5);output.Put(columns,5);Copy(rows*columns*4);}
    }
}
