using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Megaphone;

internal sealed record MegaphoneAddress(string? EpisodeId, string? Podcast, int? LatestEpisodes = null)
{
    internal static bool Matches(Uri uri) { try { Parse(uri); return true; } catch (ArgumentException) { return false; } }
    internal static MegaphoneAddress Parse(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) throw new ArgumentException("A public Megaphone HTTP address is required.");
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)) { var pair = raw.Split('=', 2); if (!query.TryAdd(Uri.UnescapeDataString(pair[0]), pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "")) throw new ArgumentException("Repeated Megaphone URL options are ambiguous."); }
        if (query.Keys.Any(key => key is "token" or "key" or "page" or "clip" or "private")) throw new ArgumentException("Private manually paged and clip Megaphone workflows need additional support.");
        var parts = uri.AbsolutePath.Trim('/').Split('/'); string? episode = null; string? podcast = null;
        if (uri.Host is "player.megaphone.fm" or "player-origin.megaphone.fm" or "megaphone.link")
        {
            if (parts.Length == 1 && Uid(parts[0])) episode = parts[0];
            else if (uri.Host != "megaphone.link" && parts.Length == 3 && parts[0] == "playlist" && parts[1] == "episode" && Uid(parts[2])) episode = parts[2];
            else if (uri.Host != "megaphone.link" && parts.Length == 2 && parts[0] == "playlist" && Name(parts[1])) podcast = parts[1];
            else throw new ArgumentException("Unsupported Megaphone player route.");
            if (query.ContainsKey("e") || query.ContainsKey("p") || query.ContainsKey("selected")) throw new ArgumentException("Conflicting Megaphone route and query identities.");
        }
        else if (uri.Host == "playlist.megaphone.fm" && uri.AbsolutePath is "" or "/")
        {
            episode = query.GetValueOrDefault("e"); podcast = query.GetValueOrDefault("p");
            if ((episode is null) == (podcast is null) || episode is not null && !Uid(episode) || podcast is not null && !Name(podcast)) throw new ArgumentException("A Megaphone player must identify exactly one episode or podcast.");
        }
        else if (uri.Host == "cms.megaphone.fm" && parts.Length == 2 && parts[0] == "channel" && Name(parts[1]))
        { podcast = parts[1]; episode = query.GetValueOrDefault("selected"); if (episode is not null && !Uid(episode) || query.ContainsKey("p") || query.ContainsKey("e")) throw new ArgumentException("Invalid Megaphone channel selection."); }
        else throw new ArgumentException("Unsupported Megaphone host or route.");
        int? latest = null;
        if (query.TryGetValue("episodes", out var supplied)) { if (podcast is null || !int.TryParse(supplied, out var count) || count is < 1 or > 10000) throw new ArgumentException("Invalid Megaphone latest-episode inventory scope."); latest = count; }
        return new(episode?.ToUpperInvariant(), podcast, latest);
    }
    internal static bool Uid(string value) => Regex.IsMatch(value, @"\A[A-Za-z]{2,12}[0-9]{10}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static bool Name(string value) => Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9_-]{0,249}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static Uri Episode(string id) => new("https://player.megaphone.fm/" + id);
}
