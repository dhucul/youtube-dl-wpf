using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Coub;

internal sealed record CoubAddress(string Id)
{
    internal Uri Page => new("https://coub.com/view/" + Id);
    internal static bool Matches(Uri url) { try { _ = Parse(url); return true; } catch (ArgumentException) { return false; } }
    internal static CoubAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Host is not ("coub.com" or "www.coub.com")) throw new ArgumentException("A supported Coub video address is required.");
        var match = Regex.Match(url.AbsolutePath, @"\A/(?:view|embed)/(?<id>[a-z0-9]{1,20})/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) throw new ArgumentException("This Coub address does not identify a supported public video.");
        return new(match.Groups["id"].Value);
    }
}
