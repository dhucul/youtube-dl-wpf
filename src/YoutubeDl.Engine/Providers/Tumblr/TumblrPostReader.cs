using System.Net;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Tumblr;

internal static class TumblrPostReader
{
    internal static string Article(string html, string id)
    {
        var clean = Regex.Replace(html, @"<!--.*?-->|<(?:script|template|textarea)\b[^>]*>.*?</(?:script|template|textarea)\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromMilliseconds(500));
        var tags = Regex.Matches(clean, @"<(?<close>/)?article\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        var found = new List<string>();
        for (var index = 0; index < tags.Count; index++)
        {
            var tag = tags[index]; if (tag.Groups["close"].Success || Attributes(tag.Groups["attrs"].Value).GetValueOrDefault("data-post-id") != id) continue;
            var depth = 1; var next = index + 1;
            for (; next < tags.Count && depth > 0; next++) depth += tags[next].Groups["close"].Success ? -1 : 1;
            if (depth != 0) throw new InvalidDataException("Tumblr returned an incomplete requested post article.");
            var end = tags[next - 1].Index; found.Add(clean[(tag.Index + tag.Length)..end]);
        }
        return found.Count == 1 ? found[0] : throw new NotSupportedException("Tumblr did not expose one identified public post article. This theme/account layout needs additional support.");
    }
    internal static Dictionary<string, string> Attributes(string value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(value, @"(?<name>[A-Za-z0-9_:-]+)\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)')", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)))
        {
            var name = match.Groups["name"].Value; var text = WebUtility.HtmlDecode(match.Groups["double"].Success ? match.Groups["double"].Value : match.Groups["single"].Value);
            if (!result.TryAdd(name, text) && result[name] != text && name.ToLowerInvariant() is "data-post-id" or "data-npf" or "src" or "href")
                throw new InvalidDataException("Tumblr returned conflicting post element identities/resources.");
        }
        return result;
    }
    internal static IReadOnlyList<string> Blocks(string article)
    {
        var values = new List<string>();
        foreach (Match match in Regex.Matches(article, @"<[A-Za-z][A-Za-z0-9:-]*\b(?<attrs>[^>]*)>", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500)))
            if (Attributes(match.Groups["attrs"].Value).TryGetValue("data-npf", out var value))
            { if (value.Length > 1024 * 1024 || values.Count >= 1000) throw new InvalidDataException("Tumblr returned excessive post block data."); values.Add(value); }
        return values;
    }
}
