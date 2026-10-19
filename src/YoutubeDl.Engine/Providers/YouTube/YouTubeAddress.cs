using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.YouTube;

public sealed record YouTubeAddress(string? VideoId, string? PlaylistId)
{
    public Uri? WatchUrl => VideoId is null ? null : new($"https://www.youtube.com/watch?v={VideoId}");

    public static bool IsYouTubeHost(Uri url) => url.IsAbsoluteUri && url.Scheme is "http" or "https" &&
        string.IsNullOrEmpty(url.UserInfo) && url.IsDefaultPort &&
        (Domain(url.Host, "youtube.com") || Domain(url.Host, "youtube-nocookie.com") || url.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase));

    public static YouTubeAddress Parse(Uri url)
    {
        if (!IsYouTubeHost(url)) throw new ArgumentException("The address is not a supported YouTube URL.", nameof(url));
        var query = YouTubeQuery.Parse(url.Query);
        var path = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        string? video = null;
        if (url.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) && path.Length == 1) video = path[0];
        else if (path.Length == 1 && path[0] == "watch") query.TryGetValue("v", out video);
        else if (path.Length == 2 && path[0] is "shorts" or "embed" or "live" or "v") video = path[1];
        if (video is not null && !Regex.IsMatch(video, @"\A[A-Za-z0-9_-]{11}\z"))
            throw new ArgumentException("The YouTube video identifier must contain 11 letters, digits, underscores, or hyphens.", nameof(url));
        query.TryGetValue("list", out var playlist);
        if (playlist is not null && !Regex.IsMatch(playlist, @"\A[A-Za-z0-9_-]{1,128}\z"))
            throw new ArgumentException("The YouTube playlist identifier is invalid.", nameof(url));
        if (video is null && playlist is null)
            throw new NotSupportedException("Native YouTube channel, search, and collection URL support is pending.");
        return new(video, playlist);
    }

    private static bool Domain(string host, string domain) => host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
}

internal static class YouTubeQuery
{
    public static Dictionary<string, string> Parse(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=');
            var key = Decode(equals < 0 ? part : part[..equals]);
            var value = Decode(equals < 0 ? "" : part[(equals + 1)..]);
            if (!result.TryAdd(key, value)) throw new InvalidDataException("The YouTube address contains an ambiguous repeated parameter.");
        }
        return result;
    }

    public static Uri Set(Uri url, string name, string value)
    {
        var fields = Parse(url.Query);
        fields[name] = value;
        return new UriBuilder(url) { Query = string.Join("&", fields.Select(field =>
            Uri.EscapeDataString(field.Key) + "=" + Uri.EscapeDataString(field.Value))) }.Uri;
    }

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace("+", " ", StringComparison.Ordinal));
}
