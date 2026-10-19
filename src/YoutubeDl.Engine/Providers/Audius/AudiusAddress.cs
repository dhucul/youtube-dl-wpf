using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Audius;

internal sealed record AudiusAddress(string Kind, string? Id = null, string? Handle = null, Uri? Web = null)
{
    internal static bool Matches(Uri uri) { try { Parse(uri); return true; } catch (ArgumentException) { return false; } }
    internal static AudiusAddress Parse(Uri uri)
    {
        if (!uri.IsAbsoluteUri) throw new ArgumentException("An absolute Audius address is required.");
        if (uri.Scheme == "audius") { var id = uri.OriginalString[7..]; if (!Identifier(id)) throw new ArgumentException("Use audius:TRACK_ID for a direct native track."); return new("track", id); }
        if (uri.Scheme is not ("http" or "https") || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) throw new ArgumentException("A public Audius HTTP address is required.");
        if (uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Any(pair => Uri.UnescapeDataString(pair.Split('=', 2)[0]) is "user_id" or "api_key" or "token" or "offset" or "limit" or "preview")) throw new ArgumentException("Authenticated manually paged and preview Audius requests need additional support.");
        var parts = uri.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
        if (uri.Host == "api.audius.co" && parts.Length == 3 && parts[0] == "v1" && parts[1] is "tracks" or "playlists" or "users" && Identifier(parts[2])) return new(parts[1] == "tracks" ? "track" : parts[1] == "playlists" ? "playlist" : "artist", parts[2]);
        if (uri.Host is not ("audius.co" or "www.audius.co") || !HandleValue(parts[0]) || parts[0] is "explore" or "trending" or "upload" or "settings" or "feed") throw new ArgumentException("Unsupported Audius website route.");
        var web = new UriBuilder(uri) { Scheme = "https", Host = "audius.co", Port = -1, Query = "", Fragment = "" }.Uri;
        if (parts.Length == 1) return new("artist", Handle: parts[0], Web: web);
        if (parts.Length == 2 && parts[1].Length is > 0 and <= 500) return new("track", Handle: parts[0], Web: web);
        if (parts.Length == 3 && parts[1] is "playlist" or "album" && parts[2].Length is > 0 and <= 500) return new("playlist", Handle: parts[0], Web: web);
        throw new ArgumentException("Unsupported Audius track artist or collection route.");
    }
    internal static bool Identifier(string value) => Regex.IsMatch(value, @"\A[A-Za-z0-9]{3,16}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static bool HandleValue(string value) => Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9_]{0,63}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static Uri Track(string id) => new("https://api.audius.co/v1/tracks/" + id);
}
