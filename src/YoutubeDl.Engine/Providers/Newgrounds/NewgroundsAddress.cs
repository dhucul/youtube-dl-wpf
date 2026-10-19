using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Newgrounds;

internal sealed record NewgroundsAddress(string Kind, string Id, string? User = null)
{
    internal static bool Matches(Uri uri) { try { Parse(uri); return true; } catch (ArgumentException) { return false; } }
    internal static NewgroundsAddress Parse(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("A public Newgrounds HTTP address is required.");
        if (uri.Query.Length != 0) throw new ArgumentException("Filtered, manually paged or access-specific Newgrounds URLs require additional native support.");
        var parts = uri.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
        if (uri.Host is "newgrounds.com" or "www.newgrounds.com")
        {
            if (parts.Length == 3 && Numeric(parts[2]) && parts[0] == "audio" && parts[1] == "listen") return new("audio", parts[2]);
            if (parts.Length == 3 && Numeric(parts[2]) && parts[0] == "portal" && parts[1] == "view") return new("video", parts[2]);
            if (parts.Length is 2 or 3 && parts[0] == "playlist" && Numeric(parts[1]) && (parts.Length == 2 || ValidSlug(parts[2]))) return new("playlist", parts[1]);
        }
        const string suffix = ".newgrounds.com"; var user = uri.Host.EndsWith(suffix, StringComparison.Ordinal) ? uri.Host[..^suffix.Length] : "";
        if (ValidUser(user) && user is not ("www" or "api" or "css" or "img" or "audio" or "uploads") && parts.Length == 1 && parts[0] is "audio" or "movies")
            return new("user-" + parts[0], user, user);
        throw new ArgumentException("This Newgrounds route requires additional native support.");
    }
    internal static bool Numeric(string value) => Regex.IsMatch(value, @"\A[1-9][0-9]{0,14}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static bool ValidUser(string value) => Regex.IsMatch(value, @"\A[a-z0-9][a-z0-9-]{0,62}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static bool ValidSlug(string value) => value.Length is > 0 and <= 300 && !value.Any(c => c is '/' or '\\' || char.IsControl(c));
    internal Uri Web => Kind switch { "audio" => new("https://www.newgrounds.com/audio/listen/" + Id), "video" => new("https://www.newgrounds.com/portal/view/" + Id),
        "playlist" => new("https://www.newgrounds.com/playlist/" + Id), _ => new("https://" + User + ".newgrounds.com/" + Kind[5..]) };
    public static bool IsMetadataAddress(Uri url, Uri root)
    {
        if (url.Scheme != "https" || url.Host != root.Host || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0) return false;
        if (root.Host != "www.newgrounds.com")
        {
            if (url.AbsolutePath != root.AbsolutePath) return false;
            return url.Query.Length == 0 || Regex.IsMatch(url.Query, @"\A\?page=[1-9][0-9]{0,2}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }
        if (url.AbsolutePath == root.AbsolutePath) return url.Query == root.Query;
        if (url.Query.Length != 0) return false;
        if (root.AbsolutePath.StartsWith("/playlist/", StringComparison.Ordinal))
            return url.AbsolutePath == "/visual-links-fetch" || Matches(url) && Parse(url) == Parse(root);
        if (root.AbsolutePath.StartsWith("/audio/listen/", StringComparison.Ordinal)) return url.AbsolutePath == root.AbsolutePath.Replace("/audio/listen/", "/audio/load/", StringComparison.Ordinal);
        if (root.AbsolutePath.StartsWith("/portal/view/", StringComparison.Ordinal)) return url.AbsolutePath == root.AbsolutePath.Replace("/portal/view/", "/portal/video/", StringComparison.Ordinal);
        return false;
    }
}
