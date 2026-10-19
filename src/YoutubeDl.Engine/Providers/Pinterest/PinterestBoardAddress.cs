using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Pinterest;

internal sealed record PinterestBoardAddress(string Username, string Slug, Uri Page)
{
    internal static bool TryParse(Uri url, out PinterestBoardAddress? address)
    {
        address = null;
        if (!PinterestAddress.Matches(url) || url.Host is "pin.it" or "assets.pinterest.com") return false;
        var match = Regex.Match(url.AbsolutePath, @"\A/(?<user>[A-Za-z0-9_]{1,64})/(?<slug>[A-Za-z0-9_-]{1,150})/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success || match.Groups["slug"].Value.StartsWith('_') || match.Groups["user"].Value is "pin" or "settings" or "search" or "ideas" or "explore" or "login" or "signup" or "business" or "ext") return false;
        if (url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length > 0) throw new NotSupportedException("Use an HTTPS public Pinterest board URL.");
        foreach (var part in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Uri.UnescapeDataString(part.Split('=', 2)[0]);
            if (name is not ("rs" or "lp" or "source") && !name.StartsWith("utm_", StringComparison.Ordinal))
                throw new NotSupportedException("This Pinterest board query/filter needs additional native support.");
        }
        var username = match.Groups["user"].Value.ToLowerInvariant(); var slug = match.Groups["slug"].Value;
        address = new(username, slug, new("https://www.pinterest.com/" + username + "/" + slug + "/")); return true;
    }
}
