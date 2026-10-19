using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Libsyn;

internal sealed record LibsynAddress(string? Id, bool Destination, Uri? Page = null, string? ShowId = null, string? TrackingDestination = null, bool OldestFirst = false)
{
    internal static bool Matches(Uri uri) { try { Parse(uri); return true; } catch (ArgumentException) { return false; } }
    internal static LibsynAddress Parse(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) throw new ArgumentException("A public Libsyn HTTP address is required.");
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair => Uri.UnescapeDataString(pair.Split('=', 2)[0]));
        if (query.Any(name => name is "token" or "key" or "page" or "category" or "cache" or "item_id")) throw new ArgumentException("Private filtered manually paged or cache-changing Libsyn requests need additional support.");
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (uri.Host is "play.libsyn.com" or "html5-player.libsyn.com")
        {
            if (parts.Length < 4 || (parts.Length - 4) % 2 != 0 || parts[0] != "embed" || parts[1] is not ("episode" or "destination") || parts[2] != "id" || !Numeric(parts[3])) throw new ArgumentException("Unsupported Libsyn player identity.");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in Enumerable.Range(0, (parts.Length - 4) / 2))
            {
                var name = parts[4 + pair * 2]; var value = Uri.UnescapeDataString(parts[5 + pair * 2]);
                if (name is not ("height" or "width" or "theme" or "size" or "thumbnail" or "direction" or "tdest_id" or "render-playlist" or "custom-color" or "hide-share" or "hide-menu" or "hide-follow" or "hide-subscribe" or "download" or "time-start" or "playlist-height") || value.Length > 128 || !values.TryAdd(name, value)) throw new ArgumentException("Unsupported or repeated Libsyn player options.");
            }
            if (values.TryGetValue("tdest_id", out var tracking) && !Numeric(tracking) || values.TryGetValue("direction", out var direction) && direction is not ("forward" or "backward")) throw new ArgumentException("Invalid Libsyn tracking or direction option.");
            return new(parts[3], parts[1] == "destination", TrackingDestination: values.GetValueOrDefault("tdest_id"), OldestFirst: values.GetValueOrDefault("direction") == "forward");
        }
        if (uri.Host == "sites.libsyn.com")
        {
            if (parts.Length is not (2 or 3) || !Numeric(parts[0]) || parts.Length == 3 && parts[1] != "website" || !Slug(parts[^1])) throw new ArgumentException("Unsupported Libsyn hosted episode route.");
            return new(null, false, new Uri("https://sites.libsyn.com/" + string.Join('/', parts)), parts[0]);
        }
        const string suffix = ".libsyn.com"; var label = uri.Host.EndsWith(suffix, StringComparison.Ordinal) ? uri.Host[..^suffix.Length] : "";
        if (!Slug(label) || label.Contains('.') || label is "www" or "assets" or "traffic" or "media" or "feeds" or "secure" or "directory" || parts.Length != 1 || !Slug(parts[0])) throw new ArgumentException("Unsupported Libsyn hosted website address.");
        return new(null, false, new Uri("https://" + uri.Host + "/" + parts[0]));
    }
    internal static bool Numeric(string value) => Regex.IsMatch(value, @"\A[1-9][0-9]{0,14}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static bool Slug(string value) => Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9_-]{0,249}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static Uri Episode(string id, string? tracking = null) => new("https://play.libsyn.com/embed/episode/id/" + id + (tracking is null ? "" : "/tdest_id/" + tracking));
}
