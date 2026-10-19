using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace YoutubeDl.Engine.Media;

internal sealed class TtmlClock
{
    internal const decimal MaximumSeconds = 10_000_000_000m;
    private readonly decimal _frameRate, _subFrameRate, _tickRate;
    private readonly int _nominalFrameRate;
    private static readonly Regex Clock = new(@"\A(?<h>[0-9]{2,9}):(?<m>[0-5][0-9]):(?<s>[0-5][0-9])(?:\.(?<fraction>[0-9]{1,12})|:(?<f>[0-9]{2,9})(?:\.(?<sub>[0-9]{1,9}))?)?\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Offset = new(@"\A(?<value>[0-9]+(?:\.[0-9]+)?)(?<unit>ms|h|m|s|f|t)\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));

    internal TtmlClock(XElement root)
    {
        if (Parameter(root, "timeBase") is { } timeBase && timeBase != "media") throw new NotSupportedException("Native TTML conversion currently requires the media time base.");
        if (Parameter(root, "dropMode") is { } drop && drop != "nonDrop") throw new NotSupportedException("TTML drop-frame timing requires additional native support.");
        var explicitFrameRate = Parameter(root, "frameRate");
        _nominalFrameRate = Integer(explicitFrameRate, 30, 1000);
        _subFrameRate = Integer(Parameter(root, "subFrameRate"), 1, 1_000_000);
        _frameRate = _nominalFrameRate;
        if (Parameter(root, "frameRateMultiplier") is { } multiplier)
        {
            var terms = multiplier.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length != 2) throw new InvalidDataException("The TTML frame-rate multiplier is invalid.");
            _frameRate *= (decimal)Integer(terms[0], 0, 1_000_000) / Integer(terms[1], 0, 1_000_000);
        }
        _tickRate = Parameter(root, "tickRate") is { } ticks ? Integer(ticks, 0, 1_000_000_000) : explicitFrameRate is null ? 1 : _frameRate * _subFrameRate;
    }

    internal decimal Parse(string value)
    {
        value = value.Trim();
        if (value.Length is 0 or > 80) throw new InvalidDataException("The TTML time expression is empty or excessive.");
        decimal result;
        try
        {
            var clock = Clock.Match(value);
            if (clock.Success)
            {
                result = Decimal(clock.Groups["h"].Value) * 3600 + Decimal(clock.Groups["m"].Value) * 60 + Decimal(clock.Groups["s"].Value);
                if (clock.Groups["fraction"].Success) result += Decimal("0." + clock.Groups["fraction"].Value);
                if (clock.Groups["f"].Success)
                {
                    var frames = Decimal(clock.Groups["f"].Value);
                    var subframes = clock.Groups["sub"].Success ? Decimal(clock.Groups["sub"].Value) : 0;
                    if (frames >= _nominalFrameRate || subframes >= _subFrameRate) throw new InvalidDataException("The TTML frame or subframe component exceeds its rate.");
                    result += (frames + subframes / _subFrameRate) / _frameRate;
                }
            }
            else
            {
                var offset = Offset.Match(value);
                if (!offset.Success) throw new NotSupportedException("The TTML time expression is not a supported media clock or offset.");
                var amount = Decimal(offset.Groups["value"].Value);
                result = offset.Groups["unit"].Value switch { "h" => amount * 3600, "m" => amount * 60, "ms" => amount / 1000, "f" => amount / _frameRate, "t" => amount / _tickRate, _ => amount };
            }
        }
        catch (OverflowException ex) { throw new InvalidDataException("The TTML time expression exceeds its numeric limit.", ex); }
        if (result < 0 || result > MaximumSeconds) throw new InvalidDataException("The TTML timestamp exceeds its supported range.");
        return result;
    }

    internal static string Stamp(decimal seconds, char separator)
    {
        var milliseconds = checked((long)decimal.Round(seconds * 1000, 0, MidpointRounding.AwayFromZero));
        return string.Create(CultureInfo.InvariantCulture, $"{milliseconds / 3600000:00}:{milliseconds / 60000 % 60:00}:{milliseconds / 1000 % 60:00}{separator}{milliseconds % 1000:000}");
    }
    internal static bool IsDocumentNamespace(string value) => value is "http://www.w3.org/ns/ttml" or "http://www.w3.org/2006/10/ttaf1" or "http://www.w3.org/2006/04/ttaf1" or "";
    internal static bool IsStyleNamespace(string value) => value is "http://www.w3.org/ns/ttml#styling" or "http://www.w3.org/2006/10/ttaf1#styling" or "http://www.w3.org/2006/04/ttaf1#styling";
    private static string? Parameter(XElement root, string name)
    {
        var attributes = root.Attributes().Where(attribute => attribute.Name.LocalName == name && !attribute.IsNamespaceDeclaration).ToArray();
        if (attributes.Length == 0) return null;
        if (attributes.Length != 1 || !(attributes[0].Name.NamespaceName is "http://www.w3.org/ns/ttml#parameter" or "http://www.w3.org/2006/10/ttaf1#parameter" or "http://www.w3.org/2006/04/ttaf1#parameter" ||
            root.Name.NamespaceName == "" && attributes[0].Name.NamespaceName == "")) throw new InvalidDataException("The TTML timing parameter has an unsupported or ambiguous namespace.");
        return attributes[0].Value.Trim();
    }
    private static int Integer(string? value, int fallback, int maximum)
    {
        if (value is null) return fallback;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result <= 0 || result > maximum) throw new InvalidDataException("The TTML timing rate is invalid.");
        return result;
    }
    private static decimal Decimal(string value) => decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ? number : throw new InvalidDataException("The TTML timestamp is invalid.");
}
