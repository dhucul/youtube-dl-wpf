using System.Globalization;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Pbs;

internal sealed record PbsAddress(string? Id, string? Slug)
{
    internal Uri Page => Id is { } id ? new("https://player.pbs.org/portalplayer/" + id + "/") : new("https://www.pbs.org/video/" + Slug + "/");
    internal static bool Matches(Uri url) { try { _ = Parse(url); return true; } catch (ArgumentException) { return false; } }
    internal static PbsAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0) throw new ArgumentException("A supported PBS video address is required.");
        var parts = url.AbsolutePath.Trim('/').Split('/');
        if (url.Host == "player.pbs.org" && parts.Length == 2 && parts[0] is "portalplayer" or "partnerplayer" or "viralplayer" or "widget" && Numeric(parts[1])) return new(parts[1], null);
        if (url.Host == "player.pbs.org" && parts.Length == 3 && parts[0] == "widget" && parts[1] == "partnerplayer" && Numeric(parts[2])) return new(parts[2], null);
        if (url.Host is "www.pbs.org" or "pbs.org" or "video.pbs.org" && parts.Length == 2 && parts[0] == "video")
        {
            if (Numeric(parts[1])) return new(parts[1], null);
            if (url.Host != "video.pbs.org" && Regex.IsMatch(parts[1], @"\A[a-z0-9][a-z0-9_-]{0,249}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) return new(null, parts[1]);
        }
        throw new ArgumentException("This PBS page is not a supported public video or player link.");
    }
    internal static bool Numeric(string value) => Regex.IsMatch(value, @"\A[1-9][0-9]{0,18}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) &&
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);
}
