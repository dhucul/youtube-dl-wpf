using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Discovery;

/// <summary>Reads literal public Next Flight data, including byte-counted text rows; executes no code.</summary>
internal static class NextFlightReader
{
    internal static IReadOnlyList<JsonElement> Read(string html)
    {
        var clean = Regex.Replace(html, @"<!--.*?-->|<(?:template|textarea)\b[^>]*>.*?</(?:template|textarea)\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));
        var wire = new StringBuilder(); var chunks = 0;
        foreach (Match script in Regex.Matches(clean, @"<script\b[^>]*>(?<body>.*?)</script\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500)))
        {
            var body = script.Groups["body"].Value.Trim(); if (!body.StartsWith("self.__next_f.push([1,", StringComparison.Ordinal)) continue;
            var literal = Regex.Match(body, @"\Aself\.__next_f\.push\(\[1,(?<json>""(?:\\.|[^""\\])*"")\]\)\s*;?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));
            if (!literal.Success) throw new InvalidDataException("The page has executable or unsupported Flight arguments.");
            string? value; try { value = JsonSerializer.Deserialize<string>(literal.Groups["json"].Value); } catch (JsonException) { throw new InvalidDataException("The page has a malformed literal Flight string."); }
            if (value is null || ++chunks > 512 || wire.Length + value.Length > 8 * 1024 * 1024) throw new InvalidDataException("The public page data exceeds its bound."); wire.Append(value);
        }
        return ParseWire(new UTF8Encoding(false, true).GetBytes(wire.ToString()));
    }
    internal static IReadOnlyList<JsonElement> ParseWire(byte[] wire)
    {
        if (wire.Length > 8 * 1024 * 1024) throw new InvalidDataException("The Flight stream exceeds its bound.");
        var result = new List<JsonElement>(); var position = 0; var count = 0; var utf8 = new UTF8Encoding(false, true);
        while (position < wire.Length)
        {
            if (wire[position] is 10 or 13) { position++; continue; }
            if (++count > 4096) throw new InvalidDataException("The Flight stream has too many rows.");
            if (wire[position] == ':' && position + 3 < wire.Length && wire[position + 1] == 'H' && wire[position + 2] == 'L')
            { while (position < wire.Length && wire[position] != 10) position++; continue; }
            var start = position; while (position < wire.Length && Hex(wire[position])) position++;
            if (position == start || position - start > 16 || position >= wire.Length || wire[position++] != ':') throw new InvalidDataException("The Flight stream has an invalid row identity.");
            if (position >= wire.Length) throw new InvalidDataException("The Flight row is truncated.");
            if (wire[position] == 'T')
            {
                start = ++position; while (position < wire.Length && Hex(wire[position])) position++;
                if (position == start || position - start > 8 || position >= wire.Length || wire[position] != ',' ||
                    !int.TryParse(Encoding.ASCII.GetString(wire, start, position - start), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var length) || length < 0 || length > wire.Length - (position + 1))
                    throw new InvalidDataException("A byte-counted Flight text row has an invalid or truncated length.");
                position++; string text; try { text = utf8.GetString(wire, position, length); } catch (DecoderFallbackException) { throw new InvalidDataException("A Flight text row ends inside invalid UTF-8 data."); }
                position += length; if (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('[')) result.Add(Json(text));
            }
            else
            {
                start = position; while (position < wire.Length && wire[position] != 10) position++;
                if (wire[start] is (byte)'{' or (byte)'[') result.Add(Json(utf8.GetString(wire, start, position - start).TrimEnd('\r')));
            }
        }
        return result;
    }
    private static bool Hex(byte value) => value is >= (byte)'0' and <= (byte)'9' or >= (byte)'a' and <= (byte)'f';
    private static JsonElement Json(string text)
    {
        JsonDocument document; try { document = JsonDocument.Parse(text, new() { MaxDepth = 64 }); } catch (JsonException) { throw new InvalidDataException("The Flight stream has malformed JSON data."); }
        using (document)
        {
            var stack = new Stack<JsonElement>(); stack.Push(document.RootElement); var count = 0;
            while (stack.TryPop(out var item))
            {
                if (++count > 100000) throw new InvalidDataException("The Flight JSON has too many fields.");
                if (item.ValueKind == JsonValueKind.Object) { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var property in item.EnumerateObject()) { if (!names.Add(property.Name)) throw new InvalidDataException("The Flight JSON has repeated fields."); stack.Push(property.Value); } }
                else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) stack.Push(child);
            }
            return document.RootElement.Clone();
        }
    }
}
