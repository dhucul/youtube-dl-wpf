using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.AudioBoom;

internal sealed record AudioBoomAddress(string Identity, bool IsChannel, bool Embed, bool Alias = false)
{
    internal static bool Matches(Uri uri) { try { Parse(uri); return true; } catch (ArgumentException) { return false; } }
    internal static AudioBoomAddress Parse(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Host is not ("audioboom.com" or "www.audioboom.com" or "embeds.audioboom.com")) throw new ArgumentException("A public Audioboom address is required.");
        var parts = uri.AbsolutePath.Trim('/').Split('/'); var embed = parts.Length == 3 && parts[2] == "embed";
        if (parts.Length != 2 && !embed || parts[0] is not ("posts" or "channels" or "channel")) throw new ArgumentException("This Audioboom route needs additional support.");
        foreach (var query in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        { var key = Uri.UnescapeDataString(query.Split('=', 2)[0]); if (key is "token" or "key" or "private" or "page" or "search" or "playlist") throw new ArgumentException("Private filtered or manually paged Audioboom requests need additional support."); }
        if (parts[0] == "channel") { if (embed || !Slug(parts[1])) throw new ArgumentException("Invalid Audioboom channel alias."); return new(parts[1], true, false, true); }
        var match = Regex.Match(parts[1], @"\A(?<id>[1-9][0-9]{0,14})(?:-[A-Za-z0-9][A-Za-z0-9_-]{0,249})?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) throw new ArgumentException("Audioboom requires an identified public post or channel.");
        return new(match.Groups["id"].Value, parts[0] == "channels", embed);
    }
    internal static bool Slug(string value) => Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9_-]{0,249}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal Uri Page => new("https://audioboom.com/" + (Alias ? "channel/" : IsChannel ? "channels/" : "posts/") + Identity + (Embed ? "/embed" : ""));
    internal static Uri Episode(string id) => new("https://audioboom.com/posts/" + id);
}
