using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.IHeart;

internal sealed record IHeartAddress(string PodcastId, string? EpisodeId)
{
    internal Uri Web => EpisodeId is null ? Podcast(PodcastId) : Episode(PodcastId, EpisodeId);
    internal static Uri Podcast(string id) => new("https://www.iheart.com/podcast/" + id + "/");
    internal static Uri Episode(string podcast, string episode) => new("https://www.iheart.com/podcast/" + podcast + "/episode/" + episode + "/");
    internal static bool Matches(Uri url) { try { Parse(url); return true; } catch (ArgumentException) { return false; } }
    internal static IHeartAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || url.Host is not ("www.iheart.com" or "iheart.com") ||
            !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0)
            throw new ArgumentException("A public iHeart podcast or episode address is required.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = pair.Split('=', 2); var key = Uri.UnescapeDataString(pieces[0]); var value = pieces.Length == 2 ? Uri.UnescapeDataString(pieces[1]) : "";
            if (!keys.Add(key) || !(key.StartsWith("utm_", StringComparison.Ordinal) || key == "embed" && value is "true" or "false" || key == "theme" && value is "light" or "dark"))
                throw new ArgumentException("This iHeart query requires additional native support.");
        }
        var parts = url.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
        if (parts.Length is not (2 or 4) || parts[0] != "podcast" || parts.Length == 4 && parts[2] != "episode")
            throw new ArgumentException("This iHeart route requires additional native support.");
        return new(ReadId(parts[1]), parts.Length == 4 ? ReadId(parts[3]) : null);
    }
    private static string ReadId(string value)
    {
        var match = Regex.Match(value, @"\A(?:[A-Za-z0-9][A-Za-z0-9_-]{0,250}-)?(?<id>[1-9][0-9]{0,14})\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return match.Success ? match.Groups["id"].Value : throw new ArgumentException("The iHeart address has no supported numeric identity.");
    }
}
