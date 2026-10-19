using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Medal;

internal sealed record MedalAddress(string Id, Uri Page, string? Game)
{
    internal static bool Matches(Uri url) { try { _ = Parse(url); return true; } catch (ArgumentException) { return false; } }
    internal static MedalAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Host is not ("medal.tv" or "www.medal.tv")) throw new ArgumentException("A supported Medal clip URL is required.");
        var parts = url.AbsolutePath.Trim('/').Split('/'); string? game = null; string? id = null;
        if (parts.Length == 4 && parts[0] == "games" && parts[2] is "clips" or "clip") { game = parts[1]; id = parts[3]; }
        else if (parts.Length == 2 && parts[0] is "clips" or "clip") id = parts[1];
        else if (parts.Length == 5 && Regex.IsMatch(parts[0], @"\A[a-z]{2}(?:-[a-z]{2})?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) && parts[1] == "games" && parts[3] is "clips" or "clip") { game = parts[2]; id = parts[4]; }
        if (id is null || !Regex.IsMatch(id, @"\A[A-Za-z0-9_-]{3,40}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) || game is not null && !Regex.IsMatch(game, @"\A[a-z0-9][a-z0-9-]{0,149}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new ArgumentException("This Medal address is not a supported public clip identity.");
        return new(id, new UriBuilder(url) { Scheme = "https", Host = "medal.tv", Port = -1, Query = "", Fragment = "" }.Uri, game);
    }
}
