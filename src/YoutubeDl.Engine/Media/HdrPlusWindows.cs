namespace YoutubeDl.Engine.Media;

// Processing-window statistics use inclusive rectangular pixel coordinates. The
// ellipse selects tone-mapped pixels; it does not weight the scene distribution.
internal sealed record HdrPlusWindow(int X0,int Y0,int X1,int Y1,int CenterX=0,int CenterY=0,int Angle=0,int Inner=1,int Outer=1,int Minor=1,int Overlap=0)
{
    internal void Validate(int width,int height)
    {
        if(X0<0||Y0<0||X0>X1||Y0>Y1||X1>=width||Y1>=height||CenterX<0||CenterX>=width||CenterY<0||CenterY>=height||Angle is <0 or >180||Inner<1||Outer<Inner||Minor<1||Minor>Outer)throw new InvalidDataException("Invalid HDR10+ processing window.");
    }
    internal HdrPlusWindow Map(HdrAffineCanvas g,string? authoring=null)
    {
        if(authoring=="ellipse-envelope")return HdrSelectorEnvelope.Map(this,g);if(authoring is not null)throw new NotSupportedException("Unknown HDR10+ selector authoring policy.");
        Validate(g.SourceWidth,g.SourceHeight);if(!g.AxisAligned)throw new NotSupportedException("An inset HDR10+ processing rectangle cannot represent this projective/oblique transform.");
        if(X0<g.Crop.Left||Y0<g.Crop.Top||X1>=g.SourceWidth-g.Crop.Right||Y1>=g.SourceHeight-g.Crop.Bottom)throw new NotSupportedException("Cropping an inset HDR10+ selector requires a new authoring decision.");
        (double X,double Y) Pixel(int x,int y){var p=g.Point(x+0.5,y+0.5);return(p.X-0.5,p.Y-0.5);}
        var corners=new[]{Pixel(X0,Y0),Pixel(X1,Y0),Pixel(X0,Y1),Pixel(X1,Y1)};var center=Pixel(CenterX,CenterY);var t=Angle*Math.PI/180;var ux=(g.A*Math.Cos(t)+g.B*Math.Sin(t))/g.W;var uy=(g.C*Math.Cos(t)+g.D*Math.Sin(t))/g.W;var vx=(-g.A*Math.Sin(t)+g.B*Math.Cos(t))/g.W;var vy=(-g.C*Math.Sin(t)+g.D*Math.Cos(t))/g.W;
        var xx=Outer*(double)Outer*ux*ux+Minor*(double)Minor*vx*vx;var yy=Outer*(double)Outer*uy*uy+Minor*(double)Minor*vy*vy;var xy=Outer*(double)Outer*ux*uy+Minor*(double)Minor*vx*vy;var delta=Math.Sqrt(Math.Max(0,(xx-yy)*(xx-yy)+4*xy*xy));var major=Math.Sqrt(Math.Max(0,(xx+yy+delta)/2));var minor=Math.Sqrt(Math.Max(0,(xx+yy-delta)/2));var angle=(Math.Atan2(2*xy,xx-yy)*90/Math.PI+180)%180;
        int R(double v)=>checked((int)Math.Round(v,MidpointRounding.AwayFromZero));
        var mapped=new HdrPlusWindow(Math.Clamp(R(corners.Min(p=>p.X)),0,g.Width-1),Math.Clamp(R(corners.Min(p=>p.Y)),0,g.Height-1),Math.Clamp(R(corners.Max(p=>p.X)),0,g.Width-1),Math.Clamp(R(corners.Max(p=>p.Y)),0,g.Height-1),R(center.X),R(center.Y),R(angle)%180,R(major*Inner/Outer),R(major),R(minor),Overlap);mapped.Validate(g.Width,g.Height);if(mapped.Outer>65535)throw new NotSupportedException("Transformed HDR10+ ellipse exceeds coded precision.");return mapped;
    }
}
