using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Omny;

internal sealed record OmnyAddress(string? Program, string? Clip, string? Playlist, string? Organization = null, bool Ids = false)
{
    internal static bool Matches(Uri uri) { try { Parse(uri); return true; } catch (ArgumentException) { return false; } }
    internal static bool Uuid(string value) => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty;
    internal static OmnyAddress Parse(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("A public Omny HTTP address is required.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var key = Uri.UnescapeDataString(pair.Split('=',2)[0]);
            if (!seen.Add(key) || !(key.StartsWith("utm_", StringComparison.Ordinal) || key is "style" or "size" or "media" or "source"))
                throw new ArgumentException("This Omny query requires additional native support.");
            if (key == "media" && pair.Split('=',2).Last() is not ("Audio" or "Video")) throw new ArgumentException("Unknown Omny player media mode.");
        }
        var parts = uri.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToList();
        if (parts.Count > 0 && parts[^1] == "embed") parts.RemoveAt(parts.Count - 1);
        if (parts.Count > 0 && parts[^1].EndsWith(".json", StringComparison.Ordinal)) parts[^1] = parts[^1][..^5];
        if (uri.Host is "omny.fm" or "www.omny.fm")
        {
            if (parts.Count is >= 2 and <= 5 && parts[0] == "shows" && Name(parts[1]))
            {
                if (parts.Count == 2) return new(parts[1],null,null);
                if (parts.Count == 3 && Name(parts[2]) && parts[2] is not ("playlists" or "clips")) return new(parts[1],parts[2],null);
                if (parts.Count is 4 or 5 && parts[2] == "playlists" && Name(parts[3]) && (parts.Count == 4 || Name(parts[4]))) return new(parts[1],parts.Count == 5 ? parts[4] : null,parts[3]);
            }
        }
        if (uri.Host == "api.omny.fm")
        {
            if (parts.Count > 0 && parts[0] == "api") parts.RemoveAt(0);
            if (parts.Count == 4 && parts[0] == "orgs" && Uuid(parts[1]) && Uuid(parts[3]))
                return parts[2] switch { "clips" => new(null,parts[3].ToLowerInvariant(),null,parts[1].ToLowerInvariant(),true),
                    "playlists" => new(null,null,parts[3].ToLowerInvariant(),parts[1].ToLowerInvariant(),true), _ => throw new ArgumentException("Unsupported Omny API address.") };
        }
        throw new ArgumentException("This Omny route requires additional native support.");
    }
    private static bool Name(string value) => Regex.IsMatch(value,@"\A[A-Za-z0-9][A-Za-z0-9_-]{0,249}\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    internal static Uri ClipId(string org,string clip) => new("https://api.omny.fm/orgs/"+org+"/clips/"+clip);
}
