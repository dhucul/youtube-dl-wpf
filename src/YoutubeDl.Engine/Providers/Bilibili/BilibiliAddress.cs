using System.Globalization;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Bilibili;

internal sealed record BilibiliAddress(string? Bvid, long? Aid, int? Part, long? Cid)
{
    internal static bool IsBvid(string? value) => value is not null && Regex.IsMatch(value, @"\ABV[A-Za-z0-9]{10}\z", RegexOptions.CultureInvariant);
    internal static BilibiliAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0 ||
            url.Host is not ("bilibili.com" or "www.bilibili.com" or "m.bilibili.com" or "player.bilibili.com"))
            throw new ArgumentException("Use a Bilibili public video or player URL.");
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2); var key = Uri.UnescapeDataString(pair[0]);
            if (key is not ("bvid" or "aid" or "p" or "page" or "cid")) continue;
            var value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "";
            if (query.TryGetValue(key, out var previous) && previous != value) throw new ArgumentException("Bilibili URL has conflicting identity or part values.");
            query[key] = value;
        }
        string? bvid = query.GetValueOrDefault("bvid");
        long? aid = Number("aid"), cid = Number("cid");
        var part = Number("p"); var page = Number("page");
        if (part is not null && page is not null && part != page || (part ?? page) > int.MaxValue) throw new ArgumentException("Bilibili URL has an invalid part selection.");
        if (url.Host == "player.bilibili.com")
        {
            if (url.AbsolutePath != "/player.html") throw new NotSupportedException("This Bilibili player address is not supported yet.");
        }
        else
        {
            var match = Regex.Match(url.AbsolutePath, @"\A/(?:s/)?video/(?<id>BV[A-Za-z0-9]{10}|av[0-9]{1,19})/?\z", RegexOptions.CultureInvariant);
            if (!match.Success) throw new NotSupportedException("This Bilibili page type is not supported yet.");
            var id = match.Groups["id"].Value;
            if (id.StartsWith("BV", StringComparison.Ordinal))
            {
                if (bvid is not null && bvid != id) throw new ArgumentException("Bilibili URL has conflicting video identities.");
                bvid = id;
            }
            else
            {
                var pathAid = Positive(id[2..]);
                if (aid is not null && aid != pathAid) throw new ArgumentException("Bilibili URL has conflicting video identities.");
                aid = pathAid;
            }
        }
        if (bvid is not null && !IsBvid(bvid) || bvid is null && aid is null) throw new ArgumentException("Bilibili URL is missing a valid video identity.");
        return new(bvid, aid, (int?)(part ?? page), cid);

        long? Number(string key) => query.TryGetValue(key, out var value) ? Positive(value) : null;
        static long Positive(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number : throw new ArgumentException("Bilibili identifiers and part numbers must be positive integers.");
    }
}
