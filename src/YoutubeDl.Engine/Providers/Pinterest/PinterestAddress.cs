using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Pinterest;

internal sealed record PinterestAddress(string? Id, Uri Page)
{
    private static readonly string[] Roots = ["pinterest.com", "pinterest.ca", "pinterest.co.uk", "pinterest.com.au", "pinterest.de", "pinterest.fr", "pinterest.es", "pinterest.it", "pinterest.jp"];
    internal static bool Matches(Uri url) => url.IsAbsoluteUri && (url.Host == "pin.it" || Roots.Any(root => url.Host == root || url.Host.EndsWith("." + root, StringComparison.Ordinal)));
    internal static PinterestAddress Parse(Uri url)
    {
        if (!Matches(url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length > 0)
            throw new NotSupportedException("Use an HTTPS public Pinterest pin URL.");
        if (url.Host == "pin.it")
        {
            if (!Regex.IsMatch(url.AbsolutePath, @"\A/[A-Za-z0-9_-]{1,80}/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                throw new NotSupportedException("This Pinterest short-link layout needs additional native support.");
            return new(null, url);
        }
        var match = Regex.Match(url.AbsolutePath, @"\A/pin/(?:[A-Za-z0-9%_-]{1,800}--)?(?<id>[1-9][0-9]{0,29})/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        string? id = match.Success ? match.Groups["id"].Value : null;
        if (url.Host == "assets.pinterest.com" && url.AbsolutePath == "/ext/embed.html")
        {
            var ids = url.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).Where(pair => pair.Length == 2 && pair[0] == "id").ToArray();
            if (ids.Length == 1 && Regex.IsMatch(ids[0][1], @"\A[1-9][0-9]{0,29}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) id = ids[0][1];
        }
        return id is not null ? new(id, new("https://www.pinterest.com/pin/" + id + "/")) :
            throw new NotSupportedException("Pinterest boards, profiles, collections and this URL layout need additional native support.");
    }
}
