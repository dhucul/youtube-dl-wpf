using System.Globalization;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Rumble;

internal sealed record RumbleCollectionAddress(string Type, string Slug, string Tab, int Page, string Filters, Uri Url)
{
    internal static bool IsCollection(Uri url) => RumbleAddress.IsHost(url) && Regex.IsMatch(url.AbsolutePath, @"\A/(?:c|user)/[^/]+(?:/(?:videos|shorts|livestreams))?/?\z", RegexOptions.CultureInvariant);
    internal static RumbleCollectionAddress Parse(Uri url)
    {
        if (!RumbleAddress.IsHost(url)) throw new ArgumentException("Use a public Rumble channel or user listing.");
        var match = Regex.Match(url.AbsolutePath, @"\A/(?<kind>c|user)/(?<slug>[A-Za-z0-9_-]{1,128})(?:/(?<tab>videos|shorts|livestreams))?/?\z", RegexOptions.CultureInvariant);
        if (!match.Success) throw new NotSupportedException("This Rumble collection layout is not supported yet.");
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2); var key = Uri.UnescapeDataString(parts[0]); var value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "";
            if (key is "e9s" or "e9s_ext" or "src" || key.StartsWith("utm_", StringComparison.Ordinal)) continue;
            if (values.Count >= 32 || key.Length > 128 || value.Length > 2048 || values.TryGetValue(key, out var prior) && prior != value)
                throw new ArgumentException("Rumble collection query parameters are conflicting or excessive.");
            values[key] = value;
        }
        var page = 1;
        if (values.Remove("page", out var raw) && (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out page) || page is < 1 or > 10000))
            throw new ArgumentException("Rumble page numbers must be between 1 and 10000.");
        return new(match.Groups["kind"].Value == "c" ? "channel" : "user", match.Groups["slug"].Value, match.Groups["tab"].Value, page,
            string.Join('&', values.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))), url);
    }
    internal bool SameListing(RumbleCollectionAddress other) => Type == other.Type && Slug.Equals(other.Slug, StringComparison.OrdinalIgnoreCase) && Tab == other.Tab && Filters == other.Filters;
}
