using System.Xml.Linq;

namespace YoutubeDl.Engine.Media;

internal static class HdrSpatialMetadata
{
    internal static bool Equivalent(HdrFrameMetadata expected,HdrFrameMetadata actual)=>expected.Static==actual.Static&&expected.Dolby==actual.Dolby&&WithoutGeometry(expected.Plus)==WithoutGeometry(actual.Plus);
    private static string? WithoutGeometry(string? text)
    {
        if(text is null)return null;var element=XElement.Parse(text);foreach(var field in element.Descendants("side_datum").Where(field=>(string?)field.Attribute("key") is { } key&&(key.Contains("corner_",StringComparison.Ordinal)||key.Contains("ellipse",StringComparison.Ordinal)||key=="rotation_angle")).ToArray())field.Remove();return element.ToString(SaveOptions.DisableFormatting);
    }
}
