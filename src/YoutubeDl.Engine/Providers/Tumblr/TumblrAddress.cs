using System.Globalization;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Tumblr;

internal sealed record TumblrAddress(string Blog, string Id, Uri Page)
{
    internal static bool Matches(Uri url) => url.IsAbsoluteUri && (url.Host == "tumblr.com" || url.Host.EndsWith(".tumblr.com", StringComparison.Ordinal)) &&
        url.Host is not ("api.tumblr.com" or "assets.tumblr.com" or "static.tumblr.com") && !url.Host.EndsWith(".media.tumblr.com", StringComparison.Ordinal);
    internal static TumblrAddress Parse(Uri url)
    {
        if (!Matches(url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length > 0) throw new NotSupportedException("Use an HTTPS public Tumblr post URL.");
        string? blog = null; string? id = null;
        if (url.Host is "www.tumblr.com" or "tumblr.com")
        {
            var match = Regex.Match(url.AbsolutePath, @"\A/(?:video/(?<blog>[A-Za-z0-9_-]{1,64})/(?<id>[0-9]{1,20})(?:/[0-9]+)?|blog/view/(?<blog>[A-Za-z0-9_-]{1,64})/(?<id>[0-9]{1,20})|(?<blog>[A-Za-z0-9_-]{1,64})/(?<id>[0-9]{1,20}))(?:/[^/]*)?/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (match.Success) { blog = match.Groups["blog"].Value; id = match.Groups["id"].Value; }
        }
        else
        {
            var match = Regex.Match(url.AbsolutePath, @"\A/post/(?<id>[0-9]{1,20})(?:/[^/]*)?/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            var name = url.Host[..^".tumblr.com".Length];
            if (match.Success && Regex.IsMatch(name, @"\A[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) { blog = name; id = match.Groups["id"].Value; }
        }
        if (blog is null || id is null || !ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number == 0 || number.ToString(CultureInfo.InvariantCulture) != id)
            throw new NotSupportedException("Tumblr blog feeds, custom domains and this post URL layout need additional native support.");
        blog = blog.ToLowerInvariant(); return new(blog, id, new("https://" + blog + ".tumblr.com/post/" + id));
    }
}
