using System.Globalization;

namespace YoutubeDl.Engine.Media;

internal sealed record HdrSpatialGeometry(int SourceWidth,int SourceHeight,int Width,int Height,int ScaledWidth,int ScaledHeight,int Left,int Top)
{
    internal bool Identity=>SourceWidth==Width&&SourceHeight==Height&&Left==0&&Top==0;
    internal bool Padding=>Left!=0||Top!=0||ScaledWidth!=Width||ScaledHeight!=Height;
    internal decimal Scale=>(decimal)ScaledWidth/SourceWidth;
    internal static HdrSpatialGeometry Create(int sourceWidth,int sourceHeight,int width,int height,bool plus)
    {
        if(sourceWidth is <=0 or >7680||sourceHeight is <=0 or >4320||width is <=0 or >3840||height is <=0 or >2160)throw new NotSupportedException("HDR geometry exceeds its supported bounds.");
        var ratio=Math.Min((decimal)width/sourceWidth,(decimal)height/sourceHeight);var sw=(int)decimal.Truncate(sourceWidth*ratio)/2*2;var sh=(int)decimal.Truncate(sourceHeight*ratio)/2*2;
        if(sw<=0||sh<=0||(long)sw*sourceHeight!=(long)sh*sourceWidth||(width-sw)%4!=0||(height-sh)%4!=0)throw new NotSupportedException("Dynamic HDR geometry requires an exact uniform scale and chroma-aligned centered padding.");
        var geometry=new HdrSpatialGeometry(sourceWidth,sourceHeight,width,height,sw,sh,(width-sw)/2,(height-sh)/2);if(plus&&geometry.Padding)throw new NotSupportedException("HDR10+ padding requires additional full-frame histogram treatment.");return geometry;
    }
    internal int X(int value)=>Coordinate(value,SourceWidth,Left);
    internal int Y(int value)=>Coordinate(value,SourceHeight,Top);
    internal int Radius(int value){if(value<0||value>Math.Max(SourceWidth,SourceHeight))throw new InvalidDataException("HDR ellipse radius exceeds its source canvas.");return checked((int)decimal.Round(value*Scale,0,MidpointRounding.AwayFromZero));}
    internal (int Left,int Right,int Top,int Bottom) ActiveArea(int left,int right,int top,int bottom)
    {
        if(left<0||right<0||top<0||bottom<0||left+right>=SourceWidth||top+bottom>=SourceHeight)throw new InvalidDataException("The Dolby active area is outside its source canvas.");
        var result=(X(left),Width-Left-ScaledWidth+Radius(right),Y(top),Height-Top-ScaledHeight+Radius(bottom));if(result.Item1+result.Item2>=Width||result.Item3+result.Item4>=Height)throw new InvalidDataException("The scaled Dolby active area collapsed.");return result;
    }
    internal string Filter(bool fullRange)=>"scale="+ScaledWidth+":"+ScaledHeight+":flags=bicubic:in_range="+(fullRange?"full":"limited")+":out_range="+(fullRange?"full":"limited")+",pad="+Width+":"+Height+":"+Left+":"+Top+",setsar=1";
    private int Coordinate(int value,int maximum,int offset){if(value<0||value>maximum)throw new InvalidDataException("HDR window coordinate exceeds its source canvas.");return checked((int)decimal.Round(value*Scale,0,MidpointRounding.AwayFromZero)+offset);}
}
internal sealed class HdrBits
{
    internal readonly List<bool> Data;
    internal int Position;
    internal HdrBits(byte[] bytes)=>Data=bytes.SelectMany(value=>Enumerable.Range(0,8).Select(bit=>(value&(1<<(7-bit)))!=0)).ToList();
    internal HdrBits()=>Data=[];
    internal int Read(int count){if(count is <0 or >31||Position+count>Data.Count)throw new InvalidDataException("Truncated HDR bit field.");var value=0;for(var i=0;i<count;i++)value=(value<<1)|(Data[Position++]?1:0);return value;}
    internal void Skip(int count){if(count<0||Position+count>Data.Count)throw new InvalidDataException("Truncated HDR bit interval.");Position+=count;}
    internal int Ue(){var zeros=0;while(Read(1)==0)if(++zeros>16)throw new InvalidDataException("Oversized HDR variable-length field.");return ((1<<zeros)-1)+Read(zeros);}
    internal void Put(int value,int count){if(count is <0 or >31||value<0||(long)value>=(1L<<count))throw new InvalidDataException("HDR bit field exceeds its precision.");for(var i=count-1;i>=0;i--)Data.Add((value&(1<<i))!=0);}
    internal void Ue(int value){if(value is <0 or >65535)throw new InvalidDataException("HDR variable-length value exceeds its supported bound.");var number=value+1;var length=0;for(var n=number;n>0;n>>=1)length++;Put(0,length-1);Put(number,length);}
    internal void Align(){while(Data.Count%8!=0)Data.Add(false);}
    internal void AlignRead(){while(Position%8!=0)if(Read(1)!=0)throw new InvalidDataException("Nonzero HDR alignment padding.");}
    internal byte[] Bytes(){if(Data.Count%8!=0)throw new InvalidDataException("Unaligned HDR data.");var bytes=new byte[Data.Count/8];for(var i=0;i<Data.Count;i++)if(Data[i])bytes[i/8]|=(byte)(1<<(7-i%8));return bytes;}
    internal static byte[] Unescape(ReadOnlySpan<byte> bytes){var result=new List<byte>();var zeros=0;for(var i=0;i<bytes.Length;i++){var value=bytes[i];if(zeros==2&&value==3){if(i+1>=bytes.Length||bytes[i+1]>3)throw new InvalidDataException("Invalid HDR emulation prevention.");zeros=0;continue;}result.Add(value);zeros=value==0?zeros+1:0;}return result.ToArray();}
    internal static byte[] Escape(IEnumerable<byte> bytes){var result=new List<byte>();var zeros=0;foreach(var value in bytes){if(zeros==2&&value<=3){result.Add(3);zeros=0;}result.Add(value);zeros=value==0?zeros+1:0;}return result.ToArray();}
}
