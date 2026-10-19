using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.YouTube;

internal sealed record YouTubeChannelAddress(Uri Url, string RootPath, string? ChannelId, string? Tab)
{
    internal static bool IsChannelId(string? value) => value is not null && Regex.IsMatch(value, @"\AUC[A-Za-z0-9_-]{22}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static bool IsMediaTab(string value) => value is "videos" or "shorts" or "streams";

    internal static bool TryParse(Uri url, out YouTubeChannelAddress? result)
    {
        result = null;
        if (!YouTubeAddress.IsYouTubeHost(url) || url.Host is not ("youtube.com" or "www.youtube.com" or "m.youtube.com")) return false;
        var parts = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        if (parts.Length == 0 || !(parts[0].StartsWith('@') || parts[0] is "channel" or "c" or "user")) return false;
        var count = parts[0].StartsWith('@') ? 1 : 2;
        if (parts.Length < count || parts.Length > count + 1 || url.Fragment.Length != 0) throw new NotSupportedException("This YouTube channel address requires additional native support.");
        var name = parts[count - 1];
        string? id = null;
        if (parts[0] == "channel")
        {
            if (!IsChannelId(name)) throw new ArgumentException("The YouTube channel ID is invalid.");
            id = name;
        }
        else
        {
            var slug = count == 1 ? name[1..] : name;
            if (slug.Length is < 1 or > 100 || !Regex.IsMatch(slug, @"\A[\p{L}\p{M}\p{N}_.\-·]+\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                throw new ArgumentException("The YouTube channel handle or alias is invalid.");
        }
        var tab = parts.Length == count + 1 ? parts[^1] : null;
        if (tab is not null && !IsMediaTab(tab)) throw new NotSupportedException("Native YouTube channel support currently covers Videos, Shorts and Live tabs.");
        foreach (var key in YouTubeQuery.Parse(url.Query).Keys)
            if (key is not ("hl" or "gl" or "feature" or "app" or "si" or "sub_confirmation"))
                throw new NotSupportedException("The YouTube channel query option " + key + " requires additional native support.");
        var root = "/" + string.Join('/', parts.Take(count).Select(part => Uri.EscapeDataString(part).Replace("%40", "@", StringComparison.Ordinal)));
        result = new(new Uri("https://www.youtube.com" + root + (tab is null ? "" : "/" + tab) + url.Query), root, id, tab);
        return true;
    }
}
