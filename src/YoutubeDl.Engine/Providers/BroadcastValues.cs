using System.Xml;
namespace YoutubeDl.Engine.Providers;
internal static class BroadcastValues
{
    internal static double Duration(string? text, bool allowZero = false)
    {
        if (text is not { Length: > 0 and <= 128 }) throw new InvalidDataException("The broadcaster omitted the complete duration.");
        try { var seconds = XmlConvert.ToTimeSpan(text).TotalSeconds; return seconds >= (allowZero ? 0 : double.Epsilon) && seconds <= 604800 ? seconds : throw new InvalidDataException("The broadcaster returned an unsupported full duration."); }
        catch (FormatException ex) { throw new InvalidDataException("The broadcaster returned an invalid duration.", ex); }
    }
}
