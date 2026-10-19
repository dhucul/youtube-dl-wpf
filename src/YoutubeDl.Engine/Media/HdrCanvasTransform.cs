using System.Globalization;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Media;

// Dolby active areas use pixel edges; HDR10+ windows/centers use inclusive pixel positions.
internal sealed record HdrCanvasTransform(int SourceWidth,int SourceHeight,int Width,int Height,int AspectWidth,int RotatedWidth,int RotatedHeight,int ScaledWidth,int ScaledHeight,int Left,int Top,double Degrees)
{
    internal bool AxisAligned=>Math.Abs(Degrees/90-Math.Round(Degrees/90))<0.00001;
    internal double Radians=>-Degrees*Math.PI/180;
    internal double ContentFraction=>Math.Clamp((double)AspectWidth*SourceHeight/RotatedWidth/RotatedHeight*ScaledWidth*ScaledHeight/Width/Height,0,1);
    internal static HdrCanvasTransform Create(int sw,int sh,int width,int height,string aspect,double rotation,string? matrix=null)
    {
        if(sw is <=0 or >7680||sh is <=0 or >4320||width is <=0 or >3840||height is <=0 or >2160||width%2!=0||height%2!=0||!double.IsFinite(rotation)||Math.Abs(rotation)>360)throw new NotSupportedException("HDR transform dimensions or angle exceed their bounds.");
        var parts=aspect.Split(':');if(parts.Length!=2||!int.TryParse(parts[0],out var n)||!int.TryParse(parts[1],out var d)||n<=0||d<=0||(double)n/d is <0.25 or >4)throw new NotSupportedException("HDR transforms require a bounded declared pixel aspect ratio.");
        if(!string.IsNullOrWhiteSpace(matrix))
        {
            var rows=Regex.Matches(matrix,@"(?m)^\s*[0-9a-fA-F]{8}:\s*(-?\d+)\s+(-?\d+)\s+(-?\d+)",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
            if(rows.Count!=3)throw new NotSupportedException("Invalid HDR display matrix.");var v=rows.SelectMany(row=>row.Groups.Cast<Group>().Skip(1).Select(group=>long.Parse(group.Value,CultureInfo.InvariantCulture))).ToArray();
            var a=v[0]/65536d;var b=v[1]/65536d;var c=v[3]/65536d;var e=v[4]/65536d;
            if(v[2]!=0||v[5]!=0||v[6]!=0||v[7]!=0||v[8]!=1073741824||Math.Abs(a*a+b*b-1)>0.0001||Math.Abs(c*c+e*e-1)>0.0001||Math.Abs(a*c+b*e)>0.0001||a*e-b*c<0.9999)throw new NotSupportedException("HDR display shear, reflection, translation and perspective require another raster transform.");
            var actual=Math.Atan2(c,a)*180/Math.PI;
            // ffprobe exports the matrix angle as an integer, truncating e.g. 29.9999 to 29.
            if(Math.Abs(Math.IEEERemainder(actual-rotation,360))>1.01)throw new InvalidDataException("HDR display angle and matrix disagree.");rotation=actual;
        }
        var aw=EvenCeiling((double)sw*n/d);if(aw>30720)throw new NotSupportedException("Anamorphic HDR canvas exceeds its bound.");var radians=-rotation*Math.PI/180;var cos=Math.Abs(Math.Cos(radians));var sin=Math.Abs(Math.Sin(radians));var rw=EvenCeiling(aw*cos+sh*sin);var rh=EvenCeiling(aw*sin+sh*cos);if(rw>32768||rh>32768)throw new NotSupportedException("Rotated HDR canvas exceeds its bound.");
        var ratio=Math.Min((double)width/rw,(double)height/rh);var w=Math.Max(2,(int)Math.Floor(rw*ratio+0.000001)/2*2);var h=Math.Max(2,(int)Math.Floor(rh*ratio+0.000001)/2*2);
        return new(sw,sh,width,height,aw,rw,rh,w,h,(width-w)/4*2,(height-h)/4*2,rotation);
    }
    private static int EvenCeiling(double v)=>(int)Math.Ceiling(v/2-0.000001)*2;
    internal (double X,double Y) Point(double x,double y)
    {
        if(!double.IsFinite(x)||!double.IsFinite(y)||x<0||y<0||x>SourceWidth||y>SourceHeight)throw new InvalidDataException("HDR coordinates exceed the source canvas.");
        var px=x*AspectWidth/SourceWidth-AspectWidth/2d;var py=y-SourceHeight/2d;var c=Math.Cos(Radians);var s=Math.Sin(Radians);
        return((px*c-py*s+RotatedWidth/2d)*ScaledWidth/RotatedWidth+Left,(px*s+py*c+RotatedHeight/2d)*ScaledHeight/RotatedHeight+Top);
    }
    internal (int X0,int Y0,int X1,int Y1) Rectangle(int x0,int y0,int x1,int y1,bool exact)
    {
        if(x0>x1||y0>y1)throw new InvalidDataException("Inverted HDR rectangle.");if(exact&&!AxisAligned)throw new NotSupportedException("A rotated HDR10+ rectangular window cannot be represented exactly by ST 2094-40.");
        var p=new[]{Point(x0,y0),Point(x0,y1),Point(x1,y0),Point(x1,y1)};return(Round(p.Min(v=>v.X)),Round(p.Min(v=>v.Y)),Round(p.Max(v=>v.X)),Round(p.Max(v=>v.Y)));
    }
    internal (int X,int Y) Pixel(int x,int y)
    {
        if(x<0||y<0||x>=SourceWidth||y>=SourceHeight)throw new InvalidDataException("HDR10+ pixel coordinates exceed the source picture.");var p=Point(x+0.5,y+0.5);return(Round(p.X-0.5),Round(p.Y-0.5));
    }
    internal (int X0,int Y0,int X1,int Y1) PixelRectangle(int x0,int y0,int x1,int y1)
    {
        if(x0>x1||y0>y1)throw new InvalidDataException("Inverted HDR10+ window.");if(!AxisAligned)throw new NotSupportedException("A rotated HDR10+ rectangular window cannot be represented exactly by ST 2094-40.");var p=new[]{Pixel(x0,y0),Pixel(x0,y1),Pixel(x1,y0),Pixel(x1,y1)};return(p.Min(v=>v.X),p.Min(v=>v.Y),p.Max(v=>v.X),p.Max(v=>v.Y));
    }
    internal (int Left,int Right,int Top,int Bottom) ActiveArea(int left,int right,int top,int bottom)
    {
        if(left<0||right<0||top<0||bottom<0||left+right>=SourceWidth||top+bottom>=SourceHeight)throw new InvalidDataException("Dolby active area exceeds its canvas.");
        // Level 5 is rectangular. Non-axis rotations can only retain an enclosing active area.
        if(!AxisAligned&&(left!=0||right!=0||top!=0||bottom!=0))throw new NotSupportedException("An inset Dolby active area cannot be rotated to a nonrectangular level-5 window.");
        var r=Rectangle(left,top,SourceWidth-right,SourceHeight-bottom,false);if(r.X0>=r.X1||r.Y0>=r.Y1)throw new InvalidDataException("Transformed Dolby active area collapsed.");return(r.X0,Width-r.X1,r.Y0,Height-r.Y1);
    }
    internal (int Angle,int Inner,int Outer,int Minor) Ellipse(int angle,int inner,int outer,int minor)
    {
        if(angle is <0 or >180||inner<0||inner>outer||minor<0||minor>outer||outer>Math.Max(SourceWidth,SourceHeight))throw new InvalidDataException("Invalid HDR ellipse.");
        var t=angle*Math.PI/180;var c=Math.Cos(Radians);var s=Math.Sin(Radians);var sx=(double)AspectWidth/SourceWidth;var ox=(double)ScaledWidth/RotatedWidth;var oy=(double)ScaledHeight/RotatedHeight;
        var ux=(c*sx*Math.Cos(t)-s*Math.Sin(t))*ox;var uy=(s*sx*Math.Cos(t)+c*Math.Sin(t))*oy;var vx=(-c*sx*Math.Sin(t)-s*Math.Cos(t))*ox;var vy=(-s*sx*Math.Sin(t)+c*Math.Cos(t))*oy;
        var xx=outer*(double)outer*ux*ux+minor*(double)minor*vx*vx;var yy=outer*(double)outer*uy*uy+minor*(double)minor*vy*vy;var xy=outer*(double)outer*ux*uy+minor*(double)minor*vx*vy;var delta=Math.Sqrt(Math.Max(0,(xx-yy)*(xx-yy)+4*xy*xy));var major=Math.Sqrt(Math.Max(0,(xx+yy+delta)/2));var small=Math.Sqrt(Math.Max(0,(xx+yy-delta)/2));var degrees=Math.Atan2(2*xy,xx-yy)*90/Math.PI;degrees=(degrees%180+180)%180;
        return(Round(degrees)%180,outer==0?0:Round(major*inner/outer),Round(major),Round(small));
    }
    internal string Filter(bool full)
    {
        var range=full?"full":"limited";var filters=new List<string>();if(AspectWidth!=SourceWidth)filters.Add("scale="+AspectWidth+":"+SourceHeight+":flags=bicubic:in_range="+range+":out_range="+range);
        var quarter=((int)Math.Round(Degrees/90)%4+4)%4;if(AxisAligned){if(quarter==1)filters.Add("transpose=cclock");if(quarter==2)filters.Add("hflip,vflip");if(quarter==3)filters.Add("transpose=clock");}
        else filters.Add("rotate="+Radians.ToString("R",CultureInfo.InvariantCulture)+":ow="+RotatedWidth+":oh="+RotatedHeight+":c=black:bilinear=1");
        filters.Add("scale="+ScaledWidth+":"+ScaledHeight+":flags=bicubic:in_range="+range+":out_range="+range);filters.Add("pad="+Width+":"+Height+":"+Left+":"+Top+":color=black");filters.Add("setsar=1");return string.Join(',',filters);
    }
    internal static int Round(double value)=>checked((int)Math.Round(value,MidpointRounding.AwayFromZero));
}
