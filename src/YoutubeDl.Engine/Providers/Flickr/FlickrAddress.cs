using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Flickr;

internal sealed record FlickrAddress(string Id, Uri Page)
{
    internal static bool Matches(Uri url) => url.IsAbsoluteUri && url.Host is "flickr.com" or "www.flickr.com" or "m.flickr.com" or "embedr.flickr.com" or "flic.kr";
    internal static FlickrAddress Parse(Uri url)
    {
        if (!Matches(url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length > 0)
            throw new NotSupportedException("Use an HTTPS public Flickr video URL.");
        string? id = null;
        if (url.Host == "flic.kr")
        {
            const string alphabet = "123456789abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";
            var shortMatch = Regex.Match(url.AbsolutePath, @"\A/p/(?<id>[1-9A-HJ-NP-Za-km-z]{1,16})/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (shortMatch.Success)
            {
                BigInteger decoded = 0;
                foreach (var letter in shortMatch.Groups["id"].Value) decoded = decoded * 58 + alphabet.IndexOf(letter);
                if (decoded > 0 && decoded <= ulong.MaxValue) id = decoded.ToString(CultureInfo.InvariantCulture);
            }
        }
        else if (url.AbsolutePath == "/photo.gne" && url.Host != "embedr.flickr.com")
        {
            var ids = url.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).Where(pair => pair.Length == 2 && pair[0] == "id").ToArray();
            if (ids.Length == 1) id = Uri.UnescapeDataString(ids[0][1]);
        }
        else
        {
            var pattern = url.Host == "embedr.flickr.com" ? @"\A/photos/(?<id>[0-9]{1,20})/?\z" :
                @"\A/photos/[A-Za-z0-9@_-]{1,128}/(?<id>[0-9]{1,20})(?:/in/[A-Za-z0-9@_-]{1,160})?/?\z";
            var match = Regex.Match(url.AbsolutePath, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (match.Success) id = match.Groups["id"].Value;
        }
        if (id is null || !ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number == 0 || id != number.ToString(CultureInfo.InvariantCulture))
            throw new NotSupportedException("Flickr profiles, albums, galleries and this URL layout need additional native support.");
        return new(id, url.Host is "flic.kr" or "embedr.flickr.com" || url.AbsolutePath == "/photo.gne" ? new("https://www.flickr.com/photo.gne?id=" + id) :
            new("https://www.flickr.com" + url.AbsolutePath));
    }
}
