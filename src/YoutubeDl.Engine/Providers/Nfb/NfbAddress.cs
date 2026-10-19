using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Nfb;

internal sealed record NfbAddress(string Slug, bool Embed, Uri Url)
{
    internal static bool IsHost(Uri url) => url.IsAbsoluteUri && url.Scheme is "http" or "https" && url.IsDefaultPort && url.UserInfo.Length == 0 &&
        url.Host is "nfb.ca" or "www.nfb.ca" or "onf.ca" or "www.onf.ca";
    internal static NfbAddress Parse(Uri url)
    {
        if (!IsHost(url)) throw new ArgumentException("Use an NFB or ONF film or player URL.");
        var match = Regex.Match(url.AbsolutePath, @"\A/film/(?<slug>[A-Za-z0-9_-]{1,200})(?<embed>/embed(?:/player)?)?/?\z", RegexOptions.CultureInvariant);
        if (!match.Success) throw new NotSupportedException("This NFB/ONF page type requires additional native support.");
        return new(match.Groups["slug"].Value, match.Groups["embed"].Success, url);
    }
    internal Uri Film => new("https://" + (Url.Host.EndsWith("onf.ca", StringComparison.Ordinal) ? "www.onf.ca" : "www.nfb.ca") + "/film/" + Slug + "/");
    internal Uri Player => new(Film, "embed/player/");
}
