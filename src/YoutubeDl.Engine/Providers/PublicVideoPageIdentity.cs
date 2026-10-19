using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;

namespace YoutubeDl.Engine.Providers;

/// <summary>Canonical video-page identities stay independent of short-lived delivery addresses.</summary>
internal static class PublicVideoPageIdentity
{
    internal static DownloadArchiveIdentity? Read(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0) return null;
        var site = Host("xvideos.com") ? "xvideos" : Host("xnxx.com") ? "xnxx" : null;
        if (site is null) return null;
        var pattern = site == "xvideos" ? @"\A/(?<id>video(?:\.[A-Za-z0-9]{1,64}|[0-9]{1,20}))(?:/[^\r\n]*)?\z" : @"\A/(?<id>video(?:-[A-Za-z0-9]{1,64}|[0-9]{1,20}))(?:/[^\r\n]*)?\z";
        var match = Regex.Match(url.AbsolutePath, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return match.Success ? new("native:" + site, match.Groups["id"].Value) : null;
        bool Host(string host) => url.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || url.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase);
    }

    internal static PageMetadata Bind(Uri requested, PageMetadata page)
    {
        var identity = Read(requested);
        if (identity is null || page.IsPlaylist || page.DrmRefused || page.Media.Count == 0) return page;
        if (page.SourcePageUrl is null || Read(page.SourcePageUrl) != identity)
            throw new InvalidDataException("The public video page redirected to a different selected video.");
        // Anonymous JSON-LD objects otherwise derive their IDs from contentUrl,
        // which can change on every page read as its CDN signature expires.
        return page with { Id = identity.Id, ArchiveIdentity = identity };
    }
}
