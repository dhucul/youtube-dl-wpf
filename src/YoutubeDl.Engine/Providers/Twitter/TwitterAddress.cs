using System.Globalization;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Twitter;

internal sealed record TwitterAddress(string Id, int? MediaIndex, Uri Page)
{
    internal static bool Matches(Uri url) => url.IsAbsoluteUri && (url.Host is "x.com" or "www.x.com" or "twitter.com" or "www.twitter.com" or "mobile.twitter.com" ||
        url.Host == "platform.twitter.com" && url.AbsolutePath == "/embed/Tweet.html");
    internal static TwitterAddress Parse(Uri url)
    {
        if (!Matches(url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length > 0) throw new NotSupportedException("Use an HTTPS public X/Twitter post URL.");
        string? id = null; int? index = null;
        if (url.Host == "platform.twitter.com")
        {
            var ids = url.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).Where(pair => pair.Length == 2 && pair[0] == "id").ToArray();
            if (ids.Length == 1) id = Uri.UnescapeDataString(ids[0][1]);
        }
        else
        {
            var match = Regex.Match(url.AbsolutePath, @"\A/(?:[A-Za-z0-9_]{1,15}/status(?:es)?|i/web/status|i/status)/(?<id>[0-9]{1,20})(?:/(?:video|photo)/(?<index>[1-9][0-9]?))?/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (match.Success) { id = match.Groups["id"].Value; if (match.Groups["index"].Success) index = int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture); }
        }
        if (id is null || !ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number == 0)
            throw new NotSupportedException("X/Twitter profiles, spaces, broadcasts, cards and this URL layout need additional native support.");
        id = number.ToString(CultureInfo.InvariantCulture); return new(id, index, new("https://x.com/i/status/" + id));
    }
}
