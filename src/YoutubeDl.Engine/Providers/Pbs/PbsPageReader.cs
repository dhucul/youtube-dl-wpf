using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Pbs;

internal static class PbsPageReader
{
    internal sealed record Asset(string Id, string Cid, string Slug);
    private static IEnumerable<string> Scripts(string html)
    {
        var clean = Regex.Replace(html, @"<!--.*?-->|<(?:template|textarea)\b[^>]*>.*?</(?:template|textarea)\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));
        return Regex.Matches(clean, @"<script\b[^>]*>(?<body>.*?)</script\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500))
            .Select(match => match.Groups["body"].Value.Trim()).ToArray();
    }
    internal static Asset Selected(string html, string slug)
    {
        var wire = new StringBuilder(); var chunks = 0;
        foreach (var body in Scripts(html))
        {
            if (!body.StartsWith("self.__next_f.push([1,", StringComparison.Ordinal)) continue;
            var literal = Regex.Match(body, @"\Aself\.__next_f\.push\(\[1,(?<json>""(?:\\.|[^""\\])*"")\]\)\s*;?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));
            if (!literal.Success) throw new InvalidDataException("PBS returned an unsupported executable page-data declaration.");
            using var value = Json(literal.Groups["json"].Value); var text = value.RootElement.GetString()!;
            if (++chunks > 256 || wire.Length + text.Length > 8 * 1024 * 1024) throw new InvalidDataException("PBS returned excessive public page data.");
            wire.Append(text);
        }
        var found = new List<Asset>(); var count = 0;
        foreach (var line in wire.ToString().Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon < 1 || !Regex.IsMatch(line[..colon], @"\A[0-9a-f]+\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) || line.Length <= colon + 1 || line[colon + 1] is not ('{' or '[')) continue;
            using var document = Json(line[(colon + 1)..]); var stack = new Stack<JsonElement>(); stack.Push(document.RootElement);
            while (stack.TryPop(out var item))
            {
                if (++count > 100000) throw new InvalidDataException("PBS returned excessive page metadata fields.");
                if (item.ValueKind == JsonValueKind.Object)
                {
                    if (Text(item, "slug") == slug && Text(item, "item_type") == "video" && item.TryGetProperty("legacy_tp_media_id", out _))
                    {
                        var id = Long(item, "legacy_tp_media_id")?.ToString(System.Globalization.CultureInfo.InvariantCulture); var cid = Cid(Text(item, "cid"));
                        if (id is null || !PbsAddress.Numeric(id) || Text(item, "url") is not { } raw || !Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.Host != "content.services.pbs.org" ||
                            url.Scheme != "https" || !url.IsDefaultPort || url.AbsolutePath != "/v3/pbsorg/screens/video-assets/" + slug + "/" || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0)
                            throw new InvalidDataException("PBS's selected page record has conflicting video identities.");
                        found.Add(new(id, cid, slug));
                    }
                    foreach (var property in item.EnumerateObject()) stack.Push(property.Value);
                }
                else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) stack.Push(child);
            }
        }
        return found.Distinct().ToArray() is { Length: 1 } selected ? selected[0] : throw new NotSupportedException("PBS did not expose one identified requested public video. This page layout needs additional support.");
    }
    internal static JsonDocument Bridge(string html)
    {
        var found = new List<JsonDocument>();
        try
        {
            foreach (var body in Scripts(html))
            {
                if (!Regex.IsMatch(body, @"\bwindow\.videoBridge\s*=", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) continue;
                var bytes = Encoding.UTF8.GetBytes(body); var position = 0;
                while (position < bytes.Length)
                {
                    var remaining = Encoding.UTF8.GetString(bytes[position..]);
                    var declaration = Regex.Match(remaining, @"\A\s*window\.(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                    if (!declaration.Success) throw new InvalidDataException("PBS returned an executable or unsupported player-data declaration.");
                    position += Encoding.UTF8.GetByteCount(declaration.Value);
                    var reader = new Utf8JsonReader(bytes.AsSpan(position), new() { MaxDepth = 64 });
                    JsonDocument value; try { value = JsonDocument.ParseValue(ref reader); } catch (JsonException) { throw new InvalidDataException("PBS returned nonliteral player metadata."); }
                    position += (int)reader.BytesConsumed;
                    while (position < bytes.Length && char.IsWhiteSpace((char)bytes[position])) position++;
                    if (position >= bytes.Length || bytes[position++] != ';') { value.Dispose(); throw new InvalidDataException("PBS returned additional player-data arguments."); }
                    try { Validate(value.RootElement); } catch { value.Dispose(); throw; }
                    if (declaration.Groups["name"].Value == "videoBridge") found.Add(value); else value.Dispose();
                    while (position < bytes.Length && char.IsWhiteSpace((char)bytes[position])) position++;
                }
            }
            if (found.Count != 1) throw new NotSupportedException("PBS did not expose one supported public player record. It may be unavailable or need another native layout.");
            var result = found[0]; found.Clear(); return result;
        }
        finally { foreach (var value in found) value.Dispose(); }
    }
    internal static string Cid(string? raw) => Guid.TryParseExact(raw, "D", out var id) && id != Guid.Empty ? id.ToString("D") : throw new InvalidDataException("PBS returned an invalid content ID.");
    internal static JsonDocument Json(string text)
    {
        JsonDocument value; try { value = JsonDocument.Parse(text, new() { MaxDepth = 64 }); } catch (JsonException) { throw new InvalidDataException("PBS returned malformed public metadata."); }
        try { Validate(value.RootElement); return value; } catch { value.Dispose(); throw; }
    }
    private static void Validate(JsonElement root)
    {
        var stack = new Stack<JsonElement>(); stack.Push(root); var count = 0;
        while (stack.TryPop(out var item))
        {
            if (++count > 100000) throw new InvalidDataException("PBS returned excessive metadata fields.");
            if (item.ValueKind == JsonValueKind.Object) { var keys = new HashSet<string>(StringComparer.Ordinal); foreach (var property in item.EnumerateObject()) { if (!keys.Add(property.Name)) throw new InvalidDataException("PBS returned repeated metadata fields."); stack.Push(property.Value); } }
            else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) stack.Push(child);
        }
    }
}
