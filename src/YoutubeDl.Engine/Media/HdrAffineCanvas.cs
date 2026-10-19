using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Media;

internal sealed record HdrCrop(int Left,int Top,int Right,int Bottom)
{
    internal static HdrCrop Parse(string? text)
    {
        if(text is null)return new(0,0,0,0);if(text.Length>64)throw new ArgumentException("Crop margins exceed their bound.");var parts=text.Split(',');if(parts.Length!=4||parts.Any(p=>!int.TryParse(p,NumberStyles.None,CultureInfo.InvariantCulture,out var n)||n is <0 or >7680))throw new ArgumentException("Crop requires four nonnegative pixel margins: left,top,right,bottom.");return new(int.Parse(parts[0],CultureInfo.InvariantCulture),int.Parse(parts[1],CultureInfo.InvariantCulture),int.Parse(parts[2],CultureInfo.InvariantCulture),int.Parse(parts[3],CultureInfo.InvariantCulture));
    }
}
internal sealed record HdrAffineCanvas(int SourceWidth,int SourceHeight,int Width,int Height,HdrCrop Crop,double A,double B,double C,double D,double X,double Y,double P=0,double Q=0,double W=1)
{
    internal static HdrAffineCanvas Create(int sw,int sh,int width,int height,string aspect,string? matrix,string? crop)
    {
        if(sw is <=0 or >7680||sh is <=0 or >4320||width is <=0 or >3840||height is <=0 or >2160||width%2!=0||height%2!=0)throw new NotSupportedException("HDR affine dimensions exceed their bounds.");var cut=HdrCrop.Parse(crop);if(cut.Left+cut.Right>=sw||cut.Top+cut.Bottom>=sh)throw new NotSupportedException("Crop removes the entire source picture.");
        var p=aspect.Split(':');if(p.Length!=2||!int.TryParse(p[0],out var n)||!int.TryParse(p[1],out var d)||n<=0||d<=0||(double)n/d is <0.25 or >4)throw new NotSupportedException("Invalid HDR pixel aspect.");var sar=(double)n/d;double a=sar,b=0,c=0,e=1,tx=0,ty=0,pp=0,qq=0,ww=1;
        if(!string.IsNullOrWhiteSpace(matrix))
        {
            if(matrix.Length>1024)throw new InvalidDataException("Oversized HDR display matrix.");var rows=Regex.Matches(matrix,@"(?m)^\s*[0-9a-fA-F]{8}:\s*(-?\d+)\s+(-?\d+)\s+(-?\d+)",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));if(rows.Count!=3)throw new InvalidDataException("Invalid HDR display matrix.");var v=rows.SelectMany(r=>r.Groups.Cast<Group>().Skip(1).Select(g=>long.TryParse(g.Value,out var value)?value:long.MaxValue)).ToArray();if(v.Any(value=>value<int.MinValue||value>int.MaxValue))throw new InvalidDataException("HDR matrix component exceeds signed fixed-point precision.");a=v[0]/65536d*sar;b=v[3]/65536d;c=v[1]/65536d*sar;e=v[4]/65536d;tx=v[6]/65536d;ty=v[7]/65536d;pp=v[2]/1073741824d*sar;qq=v[5]/1073741824d;ww=v[8]/1073741824d;
        }
        if(new[]{a,b,c,e}.Any(v=>!double.IsFinite(v)||Math.Abs(v)>32)||Math.Abs(a*(e*ww-ty*qq)-b*(c*ww-ty*pp)+tx*(c*qq-e*pp))<0.0001)throw new NotSupportedException("HDR affine transform is singular or exceeds its scale bound.");
        var edges=new[]{(cut.Left,cut.Top),(sw-cut.Right,cut.Top),(cut.Left,sh-cut.Bottom),(sw-cut.Right,sh-cut.Bottom)};var depths=edges.Select(p=>pp*p.Item1+qq*p.Item2+ww).ToArray();if(depths.Any(z=>!double.IsFinite(z)||z<0.0625||z>16)||depths.Max()/depths.Min()>16)throw new NotSupportedException("HDR perspective crosses a horizon or exceeds its conditioning bound.");var corners=edges.Select(p=>(X:(a*p.Item1+b*p.Item2+tx)/(pp*p.Item1+qq*p.Item2+ww),Y:(c*p.Item1+e*p.Item2+ty)/(pp*p.Item1+qq*p.Item2+ww))).ToArray();var minx=corners.Min(p=>p.X);var miny=corners.Min(p=>p.Y);var dx=corners.Max(p=>p.X)-minx;var dy=corners.Max(p=>p.Y)-miny;var fit=Math.Min(width/dx,height/dy);if(!double.IsFinite(fit)||dx<0.0001||dy<0.0001||Math.Max(dx,dy)>1000000)throw new NotSupportedException("HDR perspective canvas exceeds its bound.");var ox=(width-dx*fit)/2-minx*fit;var oy=(height-dy*fit)/2-miny*fit;return new(sw,sh,width,height,cut,a*fit+ox*pp,b*fit+ox*qq,c*fit+oy*pp,e*fit+oy*qq,tx*fit+ox*ww,ty*fit+oy*ww,pp,qq,ww);
    }
    internal bool AxisAligned=>P==0&&Q==0&&(Math.Abs(A)<1e-8&&Math.Abs(D)<1e-8||Math.Abs(B)<1e-8&&Math.Abs(C)<1e-8);
    internal (double X,double Y) Point(double x,double y)=>((A*x+B*y+X)/(P*x+Q*y+W),(C*x+D*y+Y)/(P*x+Q*y+W));
    internal (int Left,int Right,int Top,int Bottom) ActiveArea(int left,int right,int top,int bottom)
    {
        if(left<0||right<0||top<0||bottom<0||left+right>=SourceWidth||top+bottom>=SourceHeight)throw new InvalidDataException("Invalid Dolby active area.");if(!AxisAligned&&(left!=0||right!=0||top!=0||bottom!=0))throw new NotSupportedException("An inset Dolby area cannot represent this oblique/sheared transform.");
        var x0=Math.Max(left,Crop.Left);var x1=Math.Min(SourceWidth-right,SourceWidth-Crop.Right);var y0=Math.Max(top,Crop.Top);var y1=Math.Min(SourceHeight-bottom,SourceHeight-Crop.Bottom);if(x0>=x1||y0>=y1)throw new NotSupportedException("Crop removes the Dolby active area.");var p=new[]{Point(x0,y0),Point(x1,y0),Point(x0,y1),Point(x1,y1)};int Round(double n,int bound)=>Math.Clamp((int)Math.Round(n,MidpointRounding.AwayFromZero),0,bound);var result=(Round(p.Min(v=>v.X),Width),Width-Round(p.Max(v=>v.X),Width),Round(p.Min(v=>v.Y),Height),Height-Round(p.Max(v=>v.Y),Height));if(result.Item1+result.Item2>=Width||result.Item3+result.Item4>=Height)throw new NotSupportedException("Transformed Dolby area collapsed.");return result;
    }
    internal void Render(ReadOnlySpan<byte> source,Span<byte> output,CancellationToken ct=default)
    {
        var pixels=checked(SourceWidth*SourceHeight);var target=checked(Width*Height);if(source.Length!=pixels*6||output.Length!=target*6)throw new InvalidDataException("Invalid affine raster buffer.");var ia=D*W-Y*Q;var ib=X*Q-B*W;var ix=B*Y-X*D;var ic=Y*P-C*W;var id=A*W-X*P;var iy=X*C-A*Y;var ip=C*Q-D*P;var iq=B*P-A*Q;var iw=A*D-B*C;
        for(var y=0;y<Height;y++)for(var x=0;x<Width;x++)
        {
            if(x==0&&(y&15)==0)ct.ThrowIfCancellationRequested();var px=x+0.5;var py=y+0.5;var z=ip*px+iq*py+iw;var sx=(ia*px+ib*py+ix)/z;var sy=(ic*px+id*py+iy)/z;var inside=double.IsFinite(sx)&&double.IsFinite(sy)&&sx>=Crop.Left&&sx<SourceWidth-Crop.Right&&sy>=Crop.Top&&sy<SourceHeight-Crop.Bottom;if(!inside){sx=Crop.Left;sy=Crop.Top;}
            var fx=Math.Clamp(sx-0.5,Crop.Left,SourceWidth-Crop.Right-1);var fy=Math.Clamp(sy-0.5,Crop.Top,SourceHeight-Crop.Bottom-1);var x0=(int)Math.Floor(fx);var y0=(int)Math.Floor(fy);var x1=Math.Min(x0+1,SourceWidth-Crop.Right-1);var y1=Math.Min(y0+1,SourceHeight-Crop.Bottom-1);var wx=fx-x0;var wy=fy-y0;
            for(var channel=0;channel<3;channel++)
            {
                double value=channel==0?64:512;if(inside){var start=channel*pixels;var v00=BinaryPrimitives.ReadUInt16LittleEndian(source.Slice((start+y0*SourceWidth+x0)*2));var v01=BinaryPrimitives.ReadUInt16LittleEndian(source.Slice((start+y0*SourceWidth+x1)*2));var v10=BinaryPrimitives.ReadUInt16LittleEndian(source.Slice((start+y1*SourceWidth+x0)*2));var v11=BinaryPrimitives.ReadUInt16LittleEndian(source.Slice((start+y1*SourceWidth+x1)*2));if((v00|v01|v10|v11)>1023)throw new InvalidDataException("HDR affine samples exceed ten bits.");value=(v00*(1-wx)+v01*wx)*(1-wy)+(v10*(1-wx)+v11*wx)*wy;}BinaryPrimitives.WriteUInt16LittleEndian(output.Slice((channel*target+y*Width+x)*2),(ushort)Math.Clamp(Math.Round(value,MidpointRounding.AwayFromZero),0,1023));
            }
        }
    }
}
