using System.Globalization;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Nfb;

internal sealed record NfbSeriesAddress(string Slug, int? Season, string? Episode, Uri Url)
{
    internal static bool IsSeries(Uri url) => NfbAddress.IsHost(url) && url.AbsolutePath.StartsWith("/series/", StringComparison.Ordinal);
    internal static NfbSeriesAddress Parse(Uri url)
    {
        if (!NfbAddress.IsHost(url)) throw new ArgumentException("Use an NFB or ONF series URL.");
        var match = Regex.Match(url.AbsolutePath, @"\A/series/(?<slug>[A-Za-z0-9_-]{1,200})(?:/season(?<season>[0-9]{1,6})(?:/(?<episode>[A-Za-z0-9_-]{1,200}))?)?/?\z", RegexOptions.CultureInvariant);
        if (!match.Success) throw new NotSupportedException("This NFB series address is not supported yet.");
        int? season = match.Groups["season"].Success ? int.Parse(match.Groups["season"].Value, CultureInfo.InvariantCulture) : null;
        if (season is <= 0) throw new ArgumentException("NFB season numbers must be positive.");
        return new(match.Groups["slug"].Value, season, match.Groups["episode"].Success ? match.Groups["episode"].Value : null, url);
    }
}
