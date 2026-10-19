using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.ApplePodcasts;

internal sealed record ApplePodcastAddress(string ShowId, string? EpisodeId, string Country)
{
    internal Uri Canonical => new("https://podcasts.apple.com/" + Country + "/podcast/id" + ShowId + (EpisodeId is null ? "" : "?i=" + EpisodeId));
    internal static bool IsHost(Uri url) => url.IsAbsoluteUri && url.Host.ToLowerInvariant() is "podcasts.apple.com" or "embed.podcasts.apple.com" or "itunes.apple.com";
    internal static ApplePodcastAddress Parse(Uri url)
    {
        if (!IsHost(url) || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.AbsolutePath.Length > 2048)
            throw new ArgumentException("Use an Apple Podcasts show or episode URL.");
        var match = Regex.Match(url.AbsolutePath, @"\A/(?:(?<country>[A-Za-z]{2})/)?podcast/(?:[^/]+/)?id(?<show>[1-9][0-9]{0,18})/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) throw new NotSupportedException("Native Apple Podcasts support for this channel, search, or link shape remains pending.");
        string? episode = null;
        foreach (var part in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (Uri.UnescapeDataString(pair[0]) != "i") continue;
            var value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "";
            if (episode is not null || !Regex.IsMatch(value, @"\A[1-9][0-9]{0,18}\z")) throw new ArgumentException("The Apple episode identifier is invalid or repeated.");
            episode = value;
        }
        return new(match.Groups["show"].Value, episode, match.Groups["country"].Success ? match.Groups["country"].Value.ToLowerInvariant() : "us");
    }
}
