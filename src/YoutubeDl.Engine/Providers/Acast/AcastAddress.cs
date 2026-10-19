using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Acast;

internal sealed record AcastAddress(string Show, string? Episode)
{
    internal static bool Matches(Uri uri) { try { _ = Parse(uri); return true; } catch (ArgumentException) { return false; } }
    internal static AcastAddress Parse(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") || !uri.IsDefaultPort || uri.UserInfo.Length != 0) throw new ArgumentException("A supported Acast show or episode URL is required.");
        var parts = uri.AbsolutePath.Trim('/').Split('/'); string? show = null; string? episode = null;
        if (uri.Host == "shows.acast.com" && parts.Length is 1 or 3 && (parts.Length == 1 || parts[1] == "episodes")) { show = parts[0]; episode = parts.Length == 3 ? parts[2] : null; }
        else if (uri.Host == "play.acast.com" && parts.Length is 2 or 3 && parts[0] == "s") { show = parts[1]; episode = parts.Length == 3 ? parts[2] : null; }
        else if (uri.Host == "embed.acast.com" && parts.Length is 1 or 2) { show = parts[0]; episode = parts.Length == 2 ? parts[1] : null; }
        if (show is null || !Identifier(show) || episode is not null && !Identifier(episode)) throw new ArgumentException("This Acast address is not a supported public show/episode identity.");
        var names = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair => Uri.UnescapeDataString(pair.Split('=', 2)[0]));
        if (names.Any(name => name is "token" or "url" or "feed" or "from" or "limit")) throw new ArgumentException("Token/private-feed/paged Acast player requests need additional native support.");
        return new(show, episode);
    }
    internal static bool Identifier(string value) => Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9_-]{0,249}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static bool Mongo(string value) => Regex.IsMatch(value, @"\A[a-f0-9]{24}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static Uri Canonical(string show, string? episode) => new("https://shows.acast.com/" + show + (episode is null ? "" : "/episodes/" + episode));
}
