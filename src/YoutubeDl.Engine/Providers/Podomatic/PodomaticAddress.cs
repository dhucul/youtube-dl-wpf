using System.Globalization;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Podomatic;

internal sealed record PodomaticAddress(string? EpisodeId, string? PodcastId, string? Name = null, string? Stamp = null, bool Feed = false)
{
    internal static bool Matches(Uri uri) { try { Parse(uri); return true; } catch (ArgumentException) { return false; } }
    internal static PodomaticAddress Parse(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) throw new ArgumentException("A public Podomatic HTTP address is required.");
        if (uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Any(pair => Uri.UnescapeDataString(pair.Split('=', 2)[0]) is "token" or "key" or "page" or "per_page" or "private")) throw new ArgumentException("Private and manually paged Podomatic requests need additional support.");
        var parts = uri.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
        if (uri.Host is "www.podomatic.com" or "podomatic.com")
        {
            if (parts.Length == 4 && parts[0] == "embed" && parts[1] == "html5" && parts[2] is "episode" or "podcast" && Numeric(parts[3])) return parts[2] == "episode" ? new(parts[3], null) : new(null, parts[3]);
            if (parts.Length is 2 or 4 && parts[0] == "podcasts" && ValidName(parts[1]) && (parts.Length == 2 || parts[2] == "episodes" && ValidStamp(parts[3]))) return new(null, null, parts[1], parts.Length == 4 ? parts[3] : null);
        }
        else
        {
            const string suffix = ".podomatic.com"; var name = uri.Host.EndsWith(suffix, StringComparison.Ordinal) ? uri.Host[..^suffix.Length] : "";
            if (!ValidName(name) || name is "www" or "streams" or "assets" or "media" or "api") throw new ArgumentException("Unsupported Podomatic website host.");
            if (uri.AbsolutePath == "/rss2.xml") return new(null, null, name, Feed: true);
            if (uri.AbsolutePath is "" or "/") return new(null, null, name);
            if (parts.Length == 2 && parts[0] == "entry" && ValidStamp(parts[1])) return new(null, null, name, parts[1]);
        }
        throw new ArgumentException("Unsupported Podomatic route.");
    }
    internal static bool Numeric(string value) => Regex.IsMatch(value, @"\A[1-9][0-9]{0,14}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static bool ValidName(string value) => Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9_-]{0,62}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static bool ValidStamp(string value) => DateTimeOffset.TryParseExact(value.Replace('_', ':'), "yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    internal static Uri Episode(string id) => new("https://www.podomatic.com/embed/html5/episode/" + id);
}
