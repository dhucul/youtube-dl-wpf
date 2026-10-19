using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Jamendo;

internal sealed record JamendoAddress(string Id, bool Album)
{
    internal static bool Matches(Uri url) { try { Parse(url); return true; } catch (ArgumentException) { return false; } }
    internal static JamendoAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0 ||
            url.Host is not ("jamendo.com" or "www.jamendo.com")) throw new ArgumentException("Use a public Jamendo track or album address.");
        foreach (var pair in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Uri.UnescapeDataString(pair.Split('=', 2)[0]);
            if (name != "language" && !name.StartsWith("utm_", StringComparison.Ordinal) && name is not ("ref" or "from"))
                throw new ArgumentException("This Jamendo query requires additional native support.");
        }
        var parts = url.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
        if (parts.Length > 0 && parts[0] is "en" or "fr" or "de" or "es" or "it" or "pl" or "pt" or "ru") parts = parts[1..];
        if (parts.Length is not (2 or 3) || parts[0] is not ("track" or "album") || !Numeric(parts[1]) ||
            parts.Length == 3 && (parts[2].Length is 0 or > 300 || parts[2].Any(c => c is '/' or '\\' || char.IsControl(c))))
            throw new ArgumentException("This Jamendo route requires additional native support.");
        return new(parts[1], parts[0] == "album");
    }
    internal static bool Numeric(string id) => Regex.IsMatch(id, @"\A[1-9][0-9]{0,14}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal Uri Web => new("https://www.jamendo.com/" + (Album ? "album/" : "track/") + Id);
}
