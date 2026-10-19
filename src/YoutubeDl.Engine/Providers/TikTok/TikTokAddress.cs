using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.TikTok;

internal sealed record TikTokAddress(string? Id, Uri Url)
{
    internal static bool IsHost(Uri url) => url.IsAbsoluteUri && url.Host.ToLowerInvariant() is "tiktok.com" or "www.tiktok.com" or "m.tiktok.com" or "vm.tiktok.com" or "vt.tiktok.com";
    internal static bool IsId(string? id) => id is not null && Regex.IsMatch(id, @"\A[1-9][0-9]{9,19}\z");
    internal static Uri Canonical(string id) => new("https://www.tiktok.com/@_/video/" + id);
    internal static TikTokAddress Parse(Uri url)
    {
        if (!IsHost(url) || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0)
            throw new ArgumentException("Use a TikTok video, player, or share URL.");
        var path = url.AbsolutePath;
        if (url.Host is "vm.tiktok.com" or "vt.tiktok.com" || path.StartsWith("/t/", StringComparison.Ordinal))
        {
            if (!Regex.IsMatch(path, @"\A/(?:t/)?[A-Za-z0-9_-]{1,100}/?\z")) throw new ArgumentException("The TikTok share URL is invalid.");
            return new(null, new UriBuilder(url) { Scheme = "https", Port = -1, Fragment = "" }.Uri);
        }
        var match = Regex.Match(path, @"\A/(?:@[^/]{1,100}/video/|embed/(?:v2/)?|player/v1/|share/video/|v/)(?<id>[1-9][0-9]{9,19})(?:\.html)?/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) throw new NotSupportedException("Native TikTok photo posts, profiles, live streams and collections remain pending.");
        return new(match.Groups["id"].Value, Canonical(match.Groups["id"].Value));
    }
}
