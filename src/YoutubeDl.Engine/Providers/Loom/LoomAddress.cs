using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Loom;

internal sealed record LoomAddress(string Id, Uri Page)
{
    internal static bool Matches(Uri url) => url.IsAbsoluteUri && (url.Host == "loom.com" || url.Host == "www.loom.com") &&
        Regex.IsMatch(url.AbsolutePath, @"\A/(?:share|embed|community)/", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static LoomAddress Parse(Uri url)
    {
        if (!Matches(url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length > 0) throw new NotSupportedException("Use an HTTPS Loom share or embedded video URL.");
        var match = Regex.Match(url.AbsolutePath, @"\A/(?<kind>share|embed|community)/(?<id>[a-fA-F0-9]{32})(?:-pg)?/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) throw new NotSupportedException("Loom folders and this address layout need additional native support.");
        var id = match.Groups["id"].Value.ToLowerInvariant(); return new(id, new("https://www.loom.com/share/" + id));
    }
}
