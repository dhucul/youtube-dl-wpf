using System.Text.RegularExpressions;
using YoutubeDl.Engine.Providers.Pbs;

namespace YoutubeDl.Engine.Providers.PbsKids;

internal sealed record PbsKidsAddress(string Mode, string Slug, string Id, string? Context = null)
{
    internal Uri Page => new("https://pbskids.org/videos/" + Mode + "/" + (Context is null ? "" : Context + "/") + Slug + "/" + Id);
    internal static bool Matches(Uri url) { try { _ = Parse(url); return true; } catch (ArgumentException) { return false; } }
    internal static PbsKidsAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Host is not ("pbskids.org" or "www.pbskids.org"))
            throw new ArgumentException("A supported PBS Kids video or collection address is required.");
        var parts = url.AbsolutePath.Trim('/').Split('/');
        if (parts.Length is not (4 or 5) || parts[0] != "videos" || parts[1] is not ("watch" or "playlist") || !PbsAddress.Numeric(parts[^1]) || parts.Skip(2).Take(parts.Length - 3).Any(part => !SlugValid(part)))
            throw new ArgumentException("This PBS Kids address is not a supported current video/collection identity. Other aliases need additional support.");
        return new(parts[1], parts[^2], parts[^1], parts.Length == 5 ? parts[2] : null);
    }
    internal static bool SlugValid(string value) => Regex.IsMatch(value, @"\A[a-z0-9][a-z0-9_-]{0,249}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}
