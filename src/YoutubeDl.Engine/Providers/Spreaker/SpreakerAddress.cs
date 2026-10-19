using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Spreaker;

internal sealed record SpreakerAddress(string Identity, bool IsShow)
{
    internal static bool Matches(Uri uri) { try { Parse(uri); return true; } catch (ArgumentException) { return false; } }
    internal static SpreakerAddress Parse(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("A public Spreaker HTTP address is required.");
        var query = Query(uri); var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (query.Keys.Any(key => key is "episode_key" or "show_key" or "token" or "user_id" or "last_id" or "filter")) throw new ArgumentException("Private or manually paged Spreaker addresses need additional support.");
        if (uri.Host == "widget.spreaker.com" && parts is ["player"])
        {
            var episode = query.GetValueOrDefault("episode_id"); var show = query.GetValueOrDefault("show_id");
            if ((episode is null) == (show is null) || !Numeric(episode ?? show!)) throw new ArgumentException("The Spreaker player must identify exactly one episode or show.");
            if (query.TryGetValue("playlist", out var playlist) && playlist is not ("false" or "true" or "show")) throw new ArgumentException("This Spreaker player collection needs additional support.");
            return new(episode ?? show!, show is not null);
        }
        if (uri.Host == "api.spreaker.com" && parts.Length == 3 && parts[0] == "v2" && parts[1] is "episodes" or "shows" && Numeric(parts[2]) && query.Count == 0) return new(parts[2], parts[1] == "shows");
        if (uri.Host is not ("spreaker.com" or "www.spreaker.com") || parts.Length != 2 || parts[0] is not ("episode" or "show" or "podcast")) throw new ArgumentException("Unsupported Spreaker route.");
        if (query.ContainsKey("episode_id") || query.ContainsKey("show_id")) throw new ArgumentException("Conflicting Spreaker route and query identity.");
        var isShow = parts[0] != "episode"; var identity = parts[1]; var marker = identity.LastIndexOf("--", StringComparison.Ordinal);
        if (marker >= 0) { if (!Slug(identity[..marker])) throw new ArgumentException("Invalid Spreaker public alias."); identity = identity[(marker + 2)..]; }
        if (!Numeric(identity) && !(isShow && Slug(identity))) throw new ArgumentException("Spreaker episode IDs must be numeric.");
        return new(identity, isShow);
    }
    internal static bool Numeric(string value) => Regex.IsMatch(value, @"\A[1-9][0-9]{0,14}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static bool Slug(string value) => Regex.IsMatch(value, @"\A[a-zA-Z0-9][a-zA-Z0-9_-]{0,249}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static Uri Canonical(string id, bool show = false) => new("https://www.spreaker.com/" + (show ? "show/" : "episode/") + id);
    internal static Dictionary<string, string> Query(Uri uri)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2); var key = Uri.UnescapeDataString(pair[0]); var value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "";
            if (!result.TryAdd(key, value)) throw new ArgumentException("Repeated Spreaker query parameters are ambiguous.");
        }
        return result;
    }
}
