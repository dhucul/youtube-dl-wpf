using System.Globalization;
using System.Xml.Linq;

namespace YoutubeDl.Engine.Media;

// Only bounded, exactly representable HDR10 values can reach encoder arguments.
internal sealed record TransitionHdrMetadata(string? MasterDisplay=null,string? ContentLight=null)
{
    internal string EncoderOptions => (MasterDisplay is null?"":":master-display="+MasterDisplay)+(ContentLight is null?"":":max-cll="+ContentLight);
    internal static TransitionHdrMetadata Read(XElement frame)
    {
        string? master=null,light=null;
        foreach(var original in frame.Descendants("side_data"))
        {
            var side=new XElement(original);
            // ffprobe's XML writer emits side_datum key/value children (not JSON-style attributes).
            foreach(var datum in side.Elements("side_datum"))
            {
                var key=(string?)datum.Attribute("key");var value=(string?)datum.Attribute("value");
                if(key is null||value is null||key.Length>64||value.Length>256||side.Attribute(key) is not null)throw new InvalidDataException("Invalid or duplicate HDR side-data field.");
                side.SetAttributeValue(key,value);
            }
            var type=(string?)side.Attribute("side_data_type")??"";
            if(type.Contains("DOVI",StringComparison.OrdinalIgnoreCase)||type.Contains("Dolby",StringComparison.OrdinalIgnoreCase)||type.Contains("HDR",StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Dynamic HDR metadata requires a metadata-preserving transport; transition re-encoding is not supported.");
            if(type=="Mastering display metadata")
            {
                if(master is not null)throw new InvalidDataException("Duplicate mastering-display metadata.");
                long C(string key)=>Rational(side,key,50000,50000);
                var min=Rational(side,"min_luminance",10000,100000000);var max=Rational(side,"max_luminance",10000,100000000);
                if(max==0||min>max)throw new InvalidDataException("Invalid mastering-display luminance interval.");
                master=$"G({C("green_x")},{C("green_y")})B({C("blue_x")},{C("blue_y")})R({C("red_x")},{C("red_y")})WP({C("white_point_x")},{C("white_point_y")})L({max},{min})";
            }
            if(type=="Content light level metadata")
            {
                if(light is not null)throw new InvalidDataException("Duplicate content-light metadata.");
                var max=Number(side,"max_content");var average=Number(side,"max_average");
                if(max!=0&&average>max)throw new InvalidDataException("Invalid content-light interval.");
                light=max.ToString(CultureInfo.InvariantCulture)+","+average.ToString(CultureInfo.InvariantCulture);
            }
        }
        return new(master,light);
    }
    private static int Number(XElement side,string key)=>int.TryParse((string?)side.Attribute(key),NumberStyles.None,CultureInfo.InvariantCulture,out var result)&&result is >=0 and <=65535?result:throw new InvalidDataException("Invalid HDR content-light value.");
    private static long Rational(XElement side,string key,long scale,long maximum)
    {
        var parts=((string?)side.Attribute(key)??"").Split('/');
        if(parts.Length!=2||!long.TryParse(parts[0],NumberStyles.None,CultureInfo.InvariantCulture,out var n)||!long.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out var d)||d<=0||n>100000000||d>100000000)throw new InvalidDataException("Invalid HDR metadata rational.");
        var value=(decimal)n*scale/d;
        if(value<0||value>maximum||decimal.Truncate(value)!=value)throw new InvalidDataException("HDR metadata exceeds the supported exact encoder precision.");
        return (long)value;
    }
}
