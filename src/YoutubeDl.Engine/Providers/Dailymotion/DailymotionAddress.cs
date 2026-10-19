using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Dailymotion;

public sealed record DailymotionAddress(string? VideoId, string? PlaylistId)
{
    public static bool IsHost(Uri url) => url.IsAbsoluteUri && url.Scheme is "http" or "https" && url.IsDefaultPort && string.IsNullOrEmpty(url.UserInfo) &&
        (url.Host.Equals("dai.ly", StringComparison.OrdinalIgnoreCase) || url.Host.Equals("dailymotion.com", StringComparison.OrdinalIgnoreCase) || url.Host.EndsWith(".dailymotion.com", StringComparison.OrdinalIgnoreCase));
    public static DailymotionAddress Parse(Uri url)
    {
        if (!IsHost(url)) throw new ArgumentException("This is not a supported Dailymotion address.");
        var parts = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? video = null; string? playlist = null;
        if (url.Host == "dai.ly" && parts.Length == 1) video = parts[0];
        else if (parts.Length == 2 && parts[0] == "video") video = parts[1].Split('_', 2)[0];
        else if (parts.Length == 3 && parts[0] == "embed" && parts[1] == "video") video = parts[2];
        else if (parts.Length == 2 && parts[0] == "playlist") playlist = parts[1];
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2); var key = Uri.UnescapeDataString(pair[0]);
            if (key is not ("video" or "playlist")) continue;
            if (!query.TryAdd(key, Uri.UnescapeDataString(pair.Length == 2 ? pair[1] : ""))) throw new ArgumentException("The Dailymotion address contains duplicate media identifiers.");
        }
        if (parts.Length == 2 && parts[0] == "player" && parts[1].EndsWith(".html", StringComparison.Ordinal)) video = query.GetValueOrDefault("video");
        if (query.TryGetValue("playlist", out var context))
        { if (playlist is not null && playlist != context) throw new ArgumentException("The Dailymotion playlist identifiers conflict."); playlist = context; }
        if (video is null && playlist is null) throw new NotSupportedException("Native Dailymotion profile/search/live page enumeration remains pending.");
        foreach (var id in new[] { video, playlist }) if (id is not null && !IsId(id)) throw new ArgumentException("The Dailymotion media identifier is invalid.");
        return new(video, playlist);
    }
    internal static bool IsId(string id) => Regex.IsMatch(id, @"\A[A-Za-z0-9]{1,40}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}
