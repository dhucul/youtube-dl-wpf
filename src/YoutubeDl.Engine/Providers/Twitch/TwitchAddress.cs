using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Twitch;

internal sealed record TwitchAddress(string? Clip, string? Channel, string Period)
{
    internal static bool IsHost(Uri url) => url.IsAbsoluteUri && url.Host.ToLowerInvariant() is "twitch.tv" or "www.twitch.tv" or "m.twitch.tv" or "clips.twitch.tv" or "player.twitch.tv";
    internal static bool IsSlug(string? value) => value is not null && Regex.IsMatch(value, @"\A[A-Za-z0-9_-]{1,200}\z");
    internal static TwitchAddress Parse(Uri url)
    {
        if (!IsHost(url) || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0)
            throw new ArgumentException("Use a supported Twitch clip or channel clips URL.");
        var path = url.AbsolutePath.Trim('/').Split('/');
        if (url.Host == "clips.twitch.tv")
        {
            var slug = path is ["embed"] ? Query(url, "clip") : path.Length is 1 or 2 ? path[^1] : null;
            if (!IsSlug(slug) || slug == "embed") throw new ArgumentException("The Twitch clip URL has no valid clip identifier.");
            return new(slug, null, "ALL_TIME");
        }
        if (path.Length == 3 && path[1] == "clip" && IsLogin(path[0]) && IsSlug(path[2])) return new(path[2], null, "ALL_TIME");
        if (path.Length == 2 && path[1] == "clips" && IsLogin(path[0]))
        {
            var period = Query(url, "range") switch
            { null or "all" => "ALL_TIME", "24hr" => "LAST_DAY", "7d" => "LAST_WEEK", "30d" => "LAST_MONTH", _ => throw new NotSupportedException("This Twitch clip range is not supported.") };
            if (Query(url, "filter") is { } filter && filter != "clips") throw new NotSupportedException("This Twitch clip filter is not supported.");
            return new(null, path[0].ToLowerInvariant(), period);
        }
        throw new NotSupportedException("Native Twitch live streams, VODs and other collection URLs remain pending.");
    }
    private static bool IsLogin(string value) => Regex.IsMatch(value, @"\A[A-Za-z0-9_]{1,25}\z");
    private static string? Query(Uri url, string key)
    {
        var values = url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair => pair.Split('=', 2))
            .Where(pair => Uri.UnescapeDataString(pair[0]) == key).Select(pair => Uri.UnescapeDataString(pair.Length == 2 ? pair[1] : "")).ToArray();
        if (values.Length > 1) throw new ArgumentException("The Twitch URL repeats its " + key + " parameter.");
        return values.SingleOrDefault();
    }
}
