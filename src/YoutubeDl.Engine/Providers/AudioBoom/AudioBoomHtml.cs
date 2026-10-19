using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.AudioBoom;

internal static class AudioBoomHtml
{
    internal static string Clean(string html) => Regex.Replace(html, @"<!--.*?-->|<(?:script|template|textarea)\b[^>]*>.*?</(?:script|template|textarea)\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromMilliseconds(500));
    internal static Dictionary<string, string> Attributes(string raw)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match attribute in Regex.Matches(raw, @"(?<name>[A-Za-z0-9_:-]+)\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)')", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)))
            if (!values.TryAdd(attribute.Groups["name"].Value, WebUtility.HtmlDecode(attribute.Groups["double"].Success ? attribute.Groups["double"].Value : attribute.Groups["single"].Value))) throw new InvalidDataException("Audioboom returned repeated HTML identity or player attributes.");
        return values;
    }
    internal static IEnumerable<Dictionary<string, string>> Tags(string html, string tag) => Regex.Matches(html, "<" + tag + @"\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500)).Cast<Match>().Select(match => Attributes(match.Groups["attrs"].Value));
    internal static Uri Canonical(string html)
    {
        var links = Tags(html, "link").Where(item => item.GetValueOrDefault("rel") == "canonical").Select(item => item.GetValueOrDefault("href")).Distinct(StringComparer.Ordinal).ToArray();
        if (links.Length != 1 || !Uri.TryCreate(links[0], UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "audioboom.com") throw new InvalidDataException("Audioboom omitted one canonical public identity.");
        _ = AudioBoomAddress.Parse(uri); return uri;
    }
    internal static JsonDocument Player(string html, bool embed)
    {
        var values = embed ? Tags(html, "main").Where(item => item.GetValueOrDefault("data-jsclass") == "ReactBoomEmbedPlayerV5").Select(item => item.GetValueOrDefault("data-player-data")).ToArray() :
            Tags(html, "ab-react").Where(item => item.GetValueOrDefault("data-react-class") == "V5DetailPagePlayer").Select(item => item.GetValueOrDefault("data-react-props")).ToArray();
        if (values.Length != 1 || values[0] is not { Length: > 0 } raw || raw.Length > 2 * 1024 * 1024) throw new NotSupportedException("Audioboom did not expose one supported literal public episode player.");
        JsonDocument document; try { document = JsonDocument.Parse(raw, new() { MaxDepth = 48 }); } catch (JsonException) { throw new InvalidDataException("Audioboom returned malformed literal player data."); }
        try
        {
            var stack = new Stack<JsonElement>(); stack.Push(document.RootElement); var count = 0;
            while (stack.TryPop(out var item))
            {
                if (++count > 100000) throw new InvalidDataException("Audioboom returned excessive player fields.");
                if (item.ValueKind == JsonValueKind.Object) { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var field in item.EnumerateObject()) { if (!names.Add(field.Name)) throw new InvalidDataException("Audioboom returned repeated player fields."); stack.Push(field.Value); } }
                else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) stack.Push(child);
            }
            return document;
        }
        catch { document.Dispose(); throw; }
    }
    internal static int Count(string html)
    {
        var values = Regex.Matches(html, @"<div\b(?<attrs>[^>]*)>\s*(?<count>[0-9][0-9,]*) episodes?\s*</div>", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500)).Cast<Match>()
            .Where(match => Attributes(match.Groups["attrs"].Value).GetValueOrDefault("class")?.Split(' ').Contains("mbm") == true).Select(match => match.Groups["count"].Value.Replace(",", "")).ToArray();
        return values.Length == 1 && int.TryParse(values[0], out var count) ? count : throw new NotSupportedException("Audioboom omitted a supported explicit complete-channel count.");
    }
}
