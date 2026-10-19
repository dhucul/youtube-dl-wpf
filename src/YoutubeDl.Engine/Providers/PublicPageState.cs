using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers;

internal static class PublicPageState
{
    internal static JsonElement Next(string html)
    {
        var matches = Regex.Matches(html, @"<script\b(?=[^>]{0,8192}\bid\s*=\s*['""]__NEXT_DATA__['""])[^>]*>(?<json>[\s\S]*?)</script\s*>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (matches.Count != 1) throw new InvalidDataException("The selected page omitted its unique public JSON state.");
        return Parse(matches[0].Groups["json"].Value);
    }
    internal static JsonElement Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > 6 * 1024 * 1024) throw new InvalidDataException("The public JSON state exceeds its limit.");
        using var json = JsonDocument.Parse(text, new() { MaxDepth = 64 }); return json.RootElement.Clone();
    }
    internal static JsonElement Svt(string html)
    {
        var matches = Regex.Matches(html, @"<script\b[^>]*>\s*window\.URQL_DATA\s*=\s*(?<json>\{[\s\S]*?\})\s*;?\s*</script\s*>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (matches.Count != 1) throw new InvalidDataException("SVT omitted its unique public query cache.");
        var cache = Parse(matches[0].Groups["json"].Value); if (cache.ValueKind != JsonValueKind.Object || cache.EnumerateObject().Count() > 32) throw new InvalidDataException("SVT returned an oversized query inventory.");
        var selected = new List<JsonElement>();
        foreach (var entry in cache.EnumerateObject())
        {
            var value = entry.Value;
            if (ProviderJson.True(value, "hasNext")) throw new InvalidDataException("SVT returned an incomplete streaming query result.");
            if (ProviderJson.Text(value, "data") is not { } raw) continue;
            var data = Parse(raw); var page = ProviderJson.Object(data, "detailsPageByPath"); if (page.ValueKind == JsonValueKind.Object) selected.Add(page);
        }
        return selected.Count == 1 ? selected[0] : throw new InvalidDataException("SVT returned an ambiguous selected page.");
    }
}
