namespace YoutubeDl.Engine.Media;

internal static class HdrPlusTransform
{
    internal static byte[] Map(byte[] payload,HdrCanvasTransform geometry)
    {
        if(!HevcMetadataTransport.HdrPlus(payload)||payload.Length>4096||payload[6]!=1)throw new NotSupportedException("HDR10+ transforms require application version 1.");
        var input=new HdrBits(payload);var output=new HdrBits();Copy(56);var windows=input.Read(2);if(windows is <1 or >3)throw new InvalidDataException("Invalid HDR10+ window count.");output.Put(windows,2);
        for(var i=1;i<windows;i++)
        {
            var x0=input.Read(16);var y0=input.Read(16);var x1=input.Read(16);var y1=input.Read(16);var cx=input.Read(16);var cy=input.Read(16);var angle=input.Read(8);var inner=input.Read(16);var outer=input.Read(16);var minor=input.Read(16);var overlap=input.Read(1);
            if(inner==0||outer==0||minor==0)throw new InvalidDataException("HDR10+ ellipse axes must be positive.");var r=geometry.PixelRectangle(x0,y0,x1,y1);var center=geometry.Pixel(cx,cy);var ellipse=geometry.Ellipse(angle,inner,outer,minor);
            if(ellipse.Inner==0||ellipse.Outer==0||ellipse.Minor==0)throw new NotSupportedException("The transformed HDR10+ ellipse is smaller than its positive integer precision.");
            foreach(var v in new[]{r.X0,r.Y0,r.X1,r.Y1,HdrCanvasTransform.Round(center.X),HdrCanvasTransform.Round(center.Y)})output.Put(v,16);output.Put(ellipse.Angle,8);foreach(var v in new[]{ellipse.Inner,ellipse.Outer,ellipse.Minor})output.Put(v,16);output.Put(overlap,1);
        }
        Copy(27);Grid();
        for(var window=0;window<windows;window++)
        {
            for(var c=0;c<3;c++){var max=input.Read(17);if(max>100000)throw new InvalidDataException("Invalid HDR10+ component maximum.");output.Put(max,17);}var average=input.Read(17);var count=input.Read(4);var points=new List<(int Percent,int Value)>();var previous=-1;var luminance=-1;
            for(var i=0;i<count;i++){var percent=input.Read(7);var value=input.Read(17);if(percent>100||percent<=previous||value<luminance||value>100000)throw new InvalidDataException("Invalid HDR10+ luminance distribution.");points.Add((percent,value));previous=percent;luminance=value;}
            var bright=input.Read(10);if(average>100000||bright>1000)throw new InvalidDataException("Invalid HDR10+ full-frame statistics.");
            if(window==0&&geometry.ContentFraction<0.999999999)
            {
                var fraction=geometry.ContentFraction;average=HdrCanvasTransform.Round(average*fraction);bright=HdrCanvasTransform.Round(bright*fraction);
                // Reweight the supplied CDF knots, without inventing values between them.
                // Percentage fields quantize the black mixture to whole percentage points.
                var mapped=new SortedDictionary<int,int>();var black=HdrCanvasTransform.Round(100*(1-fraction));if(black>0)mapped[black]=0;
                foreach(var point in points){var p=HdrCanvasTransform.Round(100*(1-fraction)+point.Percent*fraction);mapped[p]=point.Value;}
                if(mapped.Count>15)throw new NotSupportedException("Padded HDR10+ distribution needs more than fifteen representable percentile knots.");points=mapped.Select(p=>(p.Key,p.Value)).ToList();
            }
            output.Put(average,17);output.Put(points.Count,4);foreach(var p in points){output.Put(p.Percent,7);output.Put(p.Value,17);}output.Put(bright,10);
        }
        Grid();
        for(var i=0;i<windows;i++){var tone=input.Read(1);output.Put(tone,1);if(tone!=0){Copy(24);var count=input.Read(4);output.Put(count,4);Copy(count*10);}var saturation=input.Read(1);output.Put(saturation,1);if(saturation!=0)Copy(6);}
        if(input.Data.Count-input.Position>7)throw new InvalidDataException("Unexpected HDR10+ trailing data.");while(input.Position<input.Data.Count)if(input.Read(1)!=0)throw new InvalidDataException("Nonzero HDR10+ alignment bits.");output.Align();return output.Bytes();
        void Copy(int count){var at=input.Position;input.Skip(count);output.Data.AddRange(input.Data.GetRange(at,count));}
        void Grid()
        {
            var flag=input.Read(1);output.Put(flag,1);if(flag==0)return;
            // These grids describe the target/mastering displays, not picture-space windows.
            var rows=input.Read(5);var columns=input.Read(5);if(rows is <2 or >25||columns is <2 or >25)throw new InvalidDataException("Invalid HDR10+ display peak-grid dimensions.");output.Put(rows,5);output.Put(columns,5);Copy(rows*columns*4);
        }
    }
}
