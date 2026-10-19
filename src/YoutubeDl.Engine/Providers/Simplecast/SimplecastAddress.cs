using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Simplecast;

internal sealed record SimplecastAddress(string? Id, bool Podcast, Uri? Site = null, string? Slug = null)
{
    internal static bool Matches(Uri uri) { try { Parse(uri); return true; } catch (ArgumentException) { return false; } }
    internal static SimplecastAddress Parse(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") || !uri.IsDefaultPort || uri.UserInfo.Length != 0) throw new ArgumentException("A public Simplecast HTTP address is required.");
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)) { var pair = part.Split('=', 2); if (!query.TryAdd(Uri.UnescapeDataString(pair[0]), pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "")) throw new ArgumentException("Simplecast repeated URL parameters are ambiguous."); }
        if (query.Keys.Any(key => key is "token" or "key" or "private" or "page" or "limit" or "offset" or "season" or "status")) throw new ArgumentException("Private filtered or manually paged Simplecast requests need additional support.");
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (uri.Host == "player.simplecast.com" && parts.Length == 1 && Uuid(parts[0]))
        {
            if (uri.Fragment.Length != 0 && !Regex.IsMatch(uri.Fragment, @"\A#[a-fA-F0-9]{3}(?:[a-fA-F0-9]{3})?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) throw new ArgumentException("Unsupported Simplecast player fragment.");
            if (query.TryGetValue("show", out var show) && show != "true") throw new ArgumentException("Unsupported Simplecast show-player mode.");
            return new(parts[0].ToLowerInvariant(), query.ContainsKey("show"));
        }
        if (uri.Fragment.Length != 0 || query.ContainsKey("show")) throw new ArgumentException("Unsupported Simplecast route fragment or player mode.");
        if (uri.Host == "api.simplecast.com" && parts.Length == 2 && parts[0] is "episodes" or "podcasts" && Uuid(parts[1]) && query.Count == 0) return new(parts[1].ToLowerInvariant(), parts[0] == "podcasts");
        const string suffix = ".simplecast.com";
        var label = uri.Host.EndsWith(suffix, StringComparison.Ordinal) ? uri.Host[..^suffix.Length] : "";
        if (!SlugValue(label) || label.Contains('.') || label is "api" or "player" or "www" or "embed" or "help" or "blog" or "apidocs") throw new ArgumentException("Unsupported Simplecast website host.");
        var site = new Uri("https://" + uri.Host + "/");
        if (uri.AbsolutePath is "" or "/") return new(null, true, site);
        if (parts.Length == 2 && parts[0] == "episodes" && SlugValue(parts[1])) return new(null, false, site, parts[1]);
        throw new ArgumentException("Unsupported Simplecast website route.");
    }
    internal static bool Uuid(string value) => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty;
    internal static bool SlugValue(string value) => Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9_-]{0,249}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static Uri Episode(string id) => new("https://api.simplecast.com/episodes/" + id);
}
