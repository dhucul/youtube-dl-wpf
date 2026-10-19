using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Providers.Reddit;

internal sealed record RedditAddress(Uri Url, string Id, bool MediaResource, MediaKind? Kind)
{
    internal static bool IsHost(Uri url) => url.IsAbsoluteUri && url.Scheme is "http" or "https" && url.IsDefaultPort && url.UserInfo.Length == 0 &&
        url.Host is "reddit.com" or "www.reddit.com" or "old.reddit.com" or "new.reddit.com" or "m.reddit.com" or "redd.it" or "v.redd.it" or "redditmedia.com" or "www.redditmedia.com";
    internal static bool IsPostId(string? id) => id is not null && Regex.IsMatch(id, @"\A[a-z0-9]{3,12}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static RedditAddress Parse(Uri url)
    {
        if (!IsHost(url)) throw new ArgumentException("Use a supported Reddit post or supplied media address.");
        if (url.Fragment.Length > 0) throw new NotSupportedException("Reddit fragment-specific links require additional native support.");
        if (url.Host == "v.redd.it")
        {
            var parts = url.AbsolutePath.Trim('/').Split('/');
            if (parts.Length != 2 || !Regex.IsMatch(parts[0], @"\A[a-z0-9]{5,32}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                throw new NotSupportedException("Use the Reddit post link or an already supplied complete media/manifest URL.");
            var kind = PageMediaExtractor.Classify(url);
            if (kind == MediaKind.Unknown || kind == MediaKind.Progressive && PageMediaExtractor.ExtensionFrom(url) != "mp4")
                throw new NotSupportedException("This Reddit media resource requires additional native support.");
            return new(url, parts[0], true, kind);
        }
        var path = url.AbsolutePath.TrimEnd('/');
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) path = path[..^5].TrimEnd('/');
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? id = null;
        if (url.Host == "redd.it" && segments.Length == 1) id = segments[0];
        else if (segments.Length is 2 or 3 && segments[0] == "comments") id = segments[1];
        else if (segments.Length is 4 or 5 && segments[0] is "r" or "user" && segments[2] == "comments") id = segments[3];
        if (!IsPostId(id)) throw new NotSupportedException("Native Reddit support currently requires a post link; feeds, share tokens and comment videos need additional support.");
        foreach (var parameter in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var key = Uri.UnescapeDataString(parameter.Split('=', 2)[0]);
            if (key is not ("raw_json" or "limit" or "depth" or "context" or "sort" or "share" or "share_id" or "ref" or "ref_source" or "embed" or "rdt") && !key.StartsWith("utm_", StringComparison.Ordinal))
                throw new NotSupportedException("This Reddit post query requires additional native support: " + key);
        }
        return new(new("https://www.reddit.com/comments/" + id + "/"), id!, false, null);
    }
    internal Uri MetadataUrl => new(Url.AbsoluteUri.TrimEnd('/') + "/.json?raw_json=1&limit=0");
    internal static string AssetId(Uri url)
    {
        if (url.Scheme != "https" || url.Host != "v.redd.it" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0)
            throw new InvalidDataException("Reddit returned an invalid native media origin.");
        var id = url.AbsolutePath.Trim('/').Split('/')[0];
        if (!Regex.IsMatch(id, @"\A[a-z0-9]{5,32}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) throw new InvalidDataException("Reddit returned an invalid media identity.");
        return id;
    }
}
