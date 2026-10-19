using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Audiomack;

internal sealed record AudiomackAddress(string Artist, string Slug, bool Album)
{
    internal static bool Matches(Uri uri) { try { Parse(uri); return true; } catch (ArgumentException) { return false; } }
    internal static AudiomackAddress Parse(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") || uri.Host is not ("audiomack.com" or "www.audiomack.com") ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) throw new ArgumentException("Use a public Audiomack track or album address.");
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Uri.UnescapeDataString(pair.Split('=', 2)[0]);
            if (name is not ("autoplay" or "background" or "ref" or "referrer") && !name.StartsWith("utm_", StringComparison.Ordinal))
                throw new ArgumentException("This Audiomack access or selection query requires additional native support.");
        }
        var parts = uri.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
        if (parts.Length > 0 && parts[0] == "embed") parts = parts[1..];
        if (parts.Length is not 3 || parts[1] is not ("song" or "album") || !SlugValue(parts[0]) || !SlugValue(parts[2])) throw new ArgumentException("This Audiomack route requires additional native support.");
        return new(parts[0], parts[2], parts[1] == "album");
    }
    internal static bool SlugValue(string value) => value.Length is > 0 and <= 300 && Regex.IsMatch(value, @"\A[a-z0-9][a-z0-9_-]*\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal Uri Web => new("https://audiomack.com/" + Artist + (Album ? "/album/" : "/song/") + Slug);
}
