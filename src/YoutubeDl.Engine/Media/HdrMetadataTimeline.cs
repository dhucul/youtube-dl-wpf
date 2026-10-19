using System.Globalization;
using System.Xml.Linq;

namespace YoutubeDl.Engine.Media;

internal sealed record TransitionDolbyConfiguration(int Profile,int Compatibility,int Rpu,int Base,int Enhancement,int Major,int Minor,string Compression)
{
    internal void Validate(string transfer,bool broader=false)
    {
        if((broader?Profile is not (5 or 8 or 10):Profile!=8)||Compatibility!=(Profile==5?0:transfer=="smpte2084"?1:4)||Rpu!=1||Base!=1||Enhancement!=0||Major!=1||Minor!=0||Compression is not ("none" or ""))
            throw new NotSupportedException("Metadata re-encoding currently requires uncompressed single-layer Dolby Vision 8.1 or 8.4 with a compatible base layer.");
    }
}
internal sealed record HdrFrameMetadata(TransitionHdrMetadata Static,string? Plus,string? Dolby)
{
    internal static HdrFrameMetadata Read(XElement frame,bool broader=false)
    {
        var statics=new XElement("frame");string? plus=null,dolby=null;var rpu=false;
        foreach(var side in frame.Descendants("side_data"))
        {
            var type=Field(side,"side_data_type")??(string?)side.Attribute("type")??"";
            if(type is "Mastering display metadata" or "Content light level metadata")statics.Add(new XElement(side));
            else if(type.Contains("HDR Dynamic Metadata SMPTE2094-40",StringComparison.Ordinal))
            {if(plus is not null)throw new InvalidDataException("Duplicate HDR10+ frame metadata.");plus=Canonical(side);}
            else if(type=="Dolby Vision RPU Data")rpu=true;
            else if(type=="Dolby Vision Metadata")
            {
                if(dolby is not null||Field(side,"rpu_type")!="2"||Field(side,"vdr_rpu_profile") is not ("1")&&!(broader&&Field(side,"vdr_rpu_profile")=="0")||Field(side,"bl_bit_depth")!="10"||Field(side,"bl_video_full_range_flag")!="0"&&!(broader&&Field(side,"vdr_rpu_profile")=="0"&&Field(side,"bl_video_full_range_flag")=="1")||Field(side,"disable_residual_flag")!="1"||Field(side,"spatial_resampling_filter_flag")!="0"||Field(side,"el_spatial_resampling_filter_flag")!="0"||Field(side,"nlq_method_idc")!="-1")
                    throw new NotSupportedException("The Dolby Vision frame requires unsupported base-layer, residual or spatial processing.");
                dolby=Canonical(side);
            }
            else if(type.Contains("HDR",StringComparison.OrdinalIgnoreCase)||type.Contains("DOVI",StringComparison.OrdinalIgnoreCase)||type.Contains("Dolby",StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("The source contains an unsupported HDR metadata family.");
        }
        if(rpu!=(dolby is not null)||plus is not null&&dolby is not null)throw new NotSupportedException("Incomplete or combined dynamic HDR frame metadata is unsupported.");
        return new(TransitionHdrMetadata.Read(statics),plus,dolby);
    }
    internal static string? Field(XElement side,string key)=>(string?)side.Attribute(key)??side.Elements("side_datum").Where(item=>(string?)item.Attribute("key")==key).Select(item=>(string?)item.Attribute("value")).SingleOrDefault();
    private static string Canonical(XElement side)
    {
        var text=side.ToString(SaveOptions.DisableFormatting);
        if(text.Length>65536)throw new InvalidDataException("A dynamic HDR metadata frame exceeds its supported size.");
        return text;
    }
}
internal sealed class HdrTimedFrame(decimal time,decimal duration,HdrFrameMetadata metadata,long packetPosition=-1)
{
    internal decimal Time {get;}=time;
    internal decimal Duration {get;}=duration;
    internal HdrFrameMetadata Metadata {get;}=metadata;
    internal long PacketPosition {get;}=packetPosition;
    internal byte[]? PlusPayload {get;set;}
    internal byte[]? DolbyPayload {get;set;}
}
internal static class HdrMetadataTimeline
{
    internal const int MaximumFrames=120000;
    internal static IReadOnlyList<HdrTimedFrame> Project(IReadOnlyList<HdrTimedFrame> source,MediaPeriodEdit edit)
    {
        if(source.Count is 0 or >MaximumFrames)throw new InvalidDataException("Invalid HDR source frame inventory.");
        var origin=source[0].Time;var selected=source.Where(frame=>frame.Time-origin>=edit.Leading&&frame.Time-origin<edit.Leading+edit.Duration).ToArray();
        if(selected.Length==0||Math.Abs(selected[0].Time-origin-edit.Leading)>0.000002m||source[^1].Time+source[^1].Duration-origin+0.000002m<edit.Leading+edit.Duration)
            throw new NotSupportedException("Metadata-aware clipping requires complete source coverage and a cut on a source frame boundary.");
        var length=decimal.Round(edit.Duration*30,0,MidpointRounding.AwayFromZero);
        if(Math.Abs(edit.Duration*30-length)>0.0001m||length is <=0 or >MaximumFrames)throw new NotSupportedException("Metadata-aware output requires a bounded whole number of 30-fps frames.");
        var result=new List<HdrTimedFrame>((int)length);var index=0;
        for(var output=0;output<(int)length;output++)
        {
            while(index+1<selected.Length&&Tick(selected[index+1].Time-selected[0].Time)<=output)index++;
            result.Add(selected[index]);
        }
        return result;
    }
    private static decimal Tick(decimal time)=>decimal.Round(time*30,0,MidpointRounding.AwayFromZero);
    internal static decimal Decimal(XElement frame,string key)=>decimal.TryParse((string?)frame.Attribute(key),NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var number)?number:throw new InvalidDataException("Missing HDR frame clock: "+key);
}
