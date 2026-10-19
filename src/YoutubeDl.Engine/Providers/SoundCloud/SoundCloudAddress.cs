using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.SoundCloud;

internal sealed record SoundCloudAddress(Uri Url, bool Playlist, string? ApiId = null, bool ShortLink = false, string? Collection = null)
{
    internal static bool IsHost(Uri url) => url.IsAbsoluteUri && url.Host.ToLowerInvariant() is
        "soundcloud.com" or "www.soundcloud.com" or "m.soundcloud.com" or "on.soundcloud.com" or "w.soundcloud.com" or "api.soundcloud.com" or "api-v2.soundcloud.com";

    internal static SoundCloudAddress Parse(Uri url, int depth = 0)
    {
        if (!IsHost(url) || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0 || depth > 1)
            throw new ArgumentException("Use a SoundCloud track, set, public API resource, or embedded player URL.");
        var host = url.Host.ToLowerInvariant();
        var path = url.AbsolutePath.Trim('/');
        if (host == "w.soundcloud.com")
        {
            if (path != "player" || !Query(url).TryGetValue("url", out var nested) || !Uri.TryCreate(nested, UriKind.Absolute, out var target))
                throw new ArgumentException("The SoundCloud player does not contain a valid media address.");
            return Parse(target, depth + 1);
        }
        if (host == "on.soundcloud.com")
        {
            if (!Regex.IsMatch(path, @"\A[A-Za-z0-9_-]{1,200}\z")) throw new ArgumentException("The SoundCloud share link is invalid.");
            return new(new UriBuilder(url) { Scheme = "https", Fragment = "" }.Uri, false, ShortLink: true);
        }
        var parts = path.Split('/');
        if (host is "soundcloud.com" or "www.soundcloud.com" or "m.soundcloud.com")
        {
            if (path is "search" or "search/sounds")
            {
                var query = Query(url);
                if (url.Fragment.Length != 0 || query.Count != 1 || !query.TryGetValue("q", out var term) || string.IsNullOrWhiteSpace(term) || term.Length > 512 || term.Any(char.IsControl))
                    throw new NotSupportedException("Use a SoundCloud sounds search with one bounded q parameter and no filters.");
                return new(WithQuery(new Uri("https://soundcloud.com/search/sounds"), ("q", term)), false, Collection: "search");
            }
            if (parts.All(part => Regex.IsMatch(part, @"\A[A-Za-z0-9_-]{1,200}\z")) &&
                parts[0] is not ("discover" or "search" or "you" or "upload" or "stations" or "charts" or "stream" or "terms-of-use" or "pages" or "settings"))
            {
                if (parts.Length == 2 && parts[1] is "sets" or "albums" or "likes" or "reposts" or "popular")
                    throw new NotSupportedException("This SoundCloud profile feed remains pending. Use its uploads or an individual set.");
                if (parts.Length == 1 || parts.Length == 2 && parts[1] == "tracks")
                    return new(new Uri("https://soundcloud.com/" + parts[0]), false, Collection: "user");
                if (parts.Length == 3 && parts[1] != "sets" && parts[2] == "recommended")
                    return new(new Uri("https://soundcloud.com/" + string.Join('/', parts[..2])), false, Collection: "related");
            }
        }
        if (host is "api.soundcloud.com" or "api-v2.soundcloud.com" && parts.Length == 2 && parts[0] == "users" && Regex.IsMatch(parts[1], @"\A[1-9][0-9]{0,18}\z"))
            return new(new Uri("https://api-v2.soundcloud.com/users/" + parts[1]), false, parts[1], Collection: "user");
        if (host is "api.soundcloud.com" or "api-v2.soundcloud.com")
        {
            if (parts.Length != 2 || parts[0] is not ("tracks" or "playlists") || !Regex.IsMatch(parts[1], @"\A[1-9][0-9]{0,18}\z"))
                throw new NotSupportedException("This SoundCloud API resource is not a track or playlist.");
            return new(new Uri("https://api-v2.soundcloud.com/" + path + url.Query), parts[0] == "playlists", parts[1]);
        }
        if (parts.Any(part => !Regex.IsMatch(part, @"\A[A-Za-z0-9_-]{1,200}\z")) ||
            parts[0] is "discover" or "search" or "you" or "upload" or "stations" or "charts" or "stream" ||
            parts.Length != 2 && !(parts.Length == 3 && parts[1] == "sets"))
            throw new NotSupportedException("Native SoundCloud support for this collection, station, or private-link shape remains pending.");
        return new(new UriBuilder(url) { Host = "soundcloud.com", Scheme = "https", Fragment = "" }.Uri, parts.Length == 3);
    }

    internal static Dictionary<string, string> Query(Uri url)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            var value = Uri.UnescapeDataString((parts.Length == 2 ? parts[1] : "").Replace('+', ' '));
            if (!result.TryAdd(key, value)) throw new ArgumentException("The SoundCloud URL repeats a query parameter.");
        }
        return result;
    }
    internal static Uri WithQuery(Uri url, params (string Key, string? Value)[] additions)
    {
        var query = Query(url);
        foreach (var (key, value) in additions) if (!string.IsNullOrEmpty(value)) query[key] = value;
        return new UriBuilder(url) { Query = string.Join("&", query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))) }.Uri;
    }
}
