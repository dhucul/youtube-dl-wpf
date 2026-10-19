using System.Xml.Linq;

namespace YoutubeDl.Engine.Media;

internal readonly record struct TtmlStyle(bool Bold = false, bool Italic = false, bool Underline = false, bool Hidden = false, bool DisplayNone = false, bool Transparent = false, bool Preserve = false);

internal sealed class TtmlStyles
{
    private readonly Dictionary<string, XElement> _styles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XElement> _regions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _resolved = new(StringComparer.Ordinal);

    internal TtmlStyles(XElement root)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in root.DescendantsAndSelf())
        {
            var id = (string?)element.Attribute(XNamespace.Xml + "id") ?? (string?)element.Attribute("id");
            if (id is not null && (id.Length is 0 or > 256 || !ids.Add(id))) throw new InvalidDataException("TTML identifiers are empty, duplicated or excessive.");
            if (!TtmlClock.IsDocumentNamespace(element.Name.NamespaceName)) continue;
            if (element.Name.LocalName == "style" && id is not null) _styles.Add(id, element);
            if (element.Name.LocalName == "region" && id is not null)
            {
                if (element.Attributes().Any(attribute => attribute.Name.LocalName is "begin" or "end" or "dur" || attribute.Name.LocalName == "timeContainer" && attribute.Value != "par"))
                    throw new NotSupportedException("Timed TTML regions require additional native support.");
                _regions.Add(id, element);
            }
        }
        if (_styles.Count > 512 || _regions.Count > 256) throw new InvalidDataException("TTML contains too many styles or regions.");
    }

    internal TtmlStyle Apply(XElement element, TtmlStyle inherited)
    {
        var scopes = new List<Dictionary<string, string>>();
        if ((string?)element.Attribute("region") is { } region)
        {
            if (!_regions.TryGetValue(region, out var definition)) throw new InvalidDataException("TTML references an unknown region.");
            scopes.Add(Properties(definition, new HashSet<string>(StringComparer.Ordinal), 0));
        }
        scopes.Add(Properties(element, new HashSet<string>(StringComparer.Ordinal), 0));
        var result = inherited;
        foreach (var scope in scopes)
        foreach (var (property, value) in scope)
            result = property switch
            {
                "fontWeight" => result with { Bold = value switch { "bold" => true, "normal" => false, _ => throw new NotSupportedException("Unsupported TTML font weight.") } },
                "fontStyle" => result with { Italic = value switch { "italic" or "oblique" => true, "normal" => false, _ => throw new NotSupportedException("Unsupported TTML font style.") } },
                "textDecoration" => result with { Underline = Decoration(value, result.Underline) },
                "visibility" => result with { Hidden = value switch { "hidden" => true, "visible" => false, _ => throw new InvalidDataException("Invalid TTML visibility.") } },
                "display" => result with { DisplayNone = result.DisplayNone || value switch { "none" => true, "auto" => false, _ => throw new InvalidDataException("Invalid TTML display.") } },
                "color" => result with { Transparent = ColorTransparency(value) ?? result.Transparent },
                "opacity" => result with { DisplayNone = result.DisplayNone || Opacity(value) == 0 },
                _ => result
            };
        if ((string?)element.Attribute(XNamespace.Xml + "space") is { } space)
            result = result with { Preserve = space switch { "preserve" => true, "default" => false, _ => throw new InvalidDataException("Invalid TTML whitespace mode.") } };
        return result;
    }

    private Dictionary<string, string> Properties(XElement element, HashSet<string> seen, int depth)
    {
        if (depth > 32) throw new InvalidDataException("TTML style inheritance is too deeply nested.");
        if (element.Name.LocalName is "style" or "region" && element.Elements().Any(child => child.Name.LocalName != "metadata"))
            throw new NotSupportedException("Nested or animated TTML style definitions require additional support.");
        if (element.Attributes().Any(attribute => attribute.Name.LocalName is "backgroundImage" or "condition"))
            throw new NotSupportedException("Image or conditional TTML styles require additional support.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var references = ((string?)element.Attribute("style") ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (references.Length > 32) throw new InvalidDataException("TTML references too many styles on one element.");
        foreach (var reference in references)
        {
            if (!_styles.TryGetValue(reference, out var style) || !seen.Add(reference)) throw new InvalidDataException("TTML style references are unknown or recursive.");
            if (!_resolved.TryGetValue(reference, out var values)) _resolved[reference] = values = Properties(style, seen, depth + 1);
            Merge(result, values); seen.Remove(reference);
        }
        foreach (var attribute in element.Attributes().Where(attribute => TtmlClock.IsStyleNamespace(attribute.Name.NamespaceName))) result[attribute.Name.LocalName] = attribute.Value.Trim();
        return result;
    }
    private static decimal Opacity(string value) => decimal.TryParse(value, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var number) && number is >= 0 and <= 1
        ? number : throw new InvalidDataException("Invalid TTML opacity.");
    private static bool? ColorTransparency(string value)
    {
        if (value is "inherit" or "currentColor") return null;
        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.StartsWith('#'))
        {
            if (value.Length is not (7 or 9) || !value.AsSpan(1).ToArray().All(char.IsAsciiHexDigit)) throw new InvalidDataException("Invalid TTML hexadecimal color.");
            return value.Length == 9 && value.EndsWith("00", StringComparison.Ordinal);
        }
        if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var rgba = value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase);
            if (!(rgba || value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase)) || !value.EndsWith(')')) throw new InvalidDataException("Invalid TTML functional color.");
            var components = value[(rgba ? 5 : 4)..^1].Split(',');
            if (components.Length != (rgba ? 4 : 3) || components.Any(component => !byte.TryParse(component.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _)))
                throw new InvalidDataException("Invalid TTML color component.");
            return rgba && byte.Parse(components[^1].Trim(), System.Globalization.CultureInfo.InvariantCulture) == 0;
        }
        return false;
    }
    private static bool Decoration(string value, bool underline)
    {
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            underline = token switch { "none" or "noUnderline" => false, "underline" => true, "lineThrough" or "noLineThrough" or "overline" or "noOverline" => underline, _ => throw new NotSupportedException("Unsupported TTML text decoration.") };
        return underline;
    }
    private static void Merge(Dictionary<string, string> destination, IReadOnlyDictionary<string, string> source)
    { foreach (var (key, value) in source) destination[key] = value; }
}
