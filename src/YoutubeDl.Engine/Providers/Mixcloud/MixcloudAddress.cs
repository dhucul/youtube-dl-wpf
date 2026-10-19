using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Mixcloud;

internal sealed record MixcloudAddress(string User, string? Slug, bool Playlist = false)
{
    internal string Key => "/" + Uri.EscapeDataString(User) + "/" + (Slug is null ? "" : (Playlist ? "playlists/" : "") + Uri.EscapeDataString(Slug) + "/");
    internal Uri Web => new("https://www.mixcloud.com" + Key);
    internal static bool Matches(Uri uri) { try { Parse(uri); return true; } catch (ArgumentException) { return false; } }
    internal static MixcloudAddress Parse(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Host is not ("mixcloud.com" or "www.mixcloud.com" or "api.mixcloud.com" or "player-widget.mixcloud.com")) throw new ArgumentException("A public Mixcloud HTTP address is required.");
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)) { var pair = part.Split('=', 2); if (!query.TryAdd(Uri.UnescapeDataString(pair[0]), pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "")) throw new ArgumentException("Repeated Mixcloud player options are ambiguous."); }
        if (query.Keys.Any(key => key is "access_token" or "token" or "since" or "until" or "offset" or "limit")) throw new ArgumentException("Private and manually paged Mixcloud requests need additional support.");
        if (uri.Host == "player-widget.mixcloud.com" || uri.AbsolutePath.TrimEnd('/') == "/widget/iframe")
        {
            if (!query.TryGetValue("feed", out var feed) || !feed.StartsWith('/') || feed.StartsWith("//", StringComparison.Ordinal)) throw new ArgumentException("The Mixcloud widget must identify one public feed path.");
            return Parse(new Uri("https://www.mixcloud.com" + feed));
        }
        var parts = uri.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
        if (!Name(parts[0]) || parts[0] is "developers" or "genres" or "live" or "discover" or "popular" or "new" or "upload" or "settings") throw new ArgumentException("Unsupported Mixcloud profile or route.");
        if (parts.Length == 1 || parts.Length == 2 && parts[1] is "uploads" or "cloudcasts") return new(parts[0], null);
        if (parts.Length == 2 && parts[1] is not ("tracks" or "favorites" or "reposts" or "playlists") && SlugValue(parts[1])) return new(parts[0], parts[1]);
        if (parts.Length == 3 && parts[1] == "playlists" && SlugValue(parts[2])) return new(parts[0], parts[2], true);
        throw new ArgumentException("Unsupported Mixcloud show or playlist route.");
    }
    private static bool Name(string value) => Regex.IsMatch(value, @"\A[A-Za-z0-9_-]{1,100}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static bool SlugValue(string value) => value.Length is > 0 and <= 500 && !value.Any(character => char.IsControl(character) || character is '/' or '?' or '#');
}
