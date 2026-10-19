using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Providers.Vimeo;

public sealed record VimeoAddress(string VideoId, string? UnlistedHash)
{
    internal sealed record FileLink(string VideoId, MediaKind Kind);
    public Uri ConfigurationUrl => new($"https://player.vimeo.com/video/{VideoId}/config" +
        (UnlistedHash is null ? "" : "?h=" + Uri.EscapeDataString(UnlistedHash)));
    public Uri PlayerUrl => new($"https://player.vimeo.com/video/{VideoId}" +
        (UnlistedHash is null ? "" : "?h=" + Uri.EscapeDataString(UnlistedHash)));

    public static bool IsVimeoHost(Uri url) => url.IsAbsoluteUri && url.Scheme is "http" or "https" &&
        string.IsNullOrEmpty(url.UserInfo) && url.IsDefaultPort &&
        (url.Host.Equals("vimeo.com", StringComparison.OrdinalIgnoreCase) || url.Host.EndsWith(".vimeo.com", StringComparison.OrdinalIgnoreCase));

    public static VimeoAddress Parse(Uri url)
    {
        if (!IsVimeoHost(url)) throw new ArgumentException("The address is not a supported Vimeo URL.", nameof(url));
        var pattern = url.Host.Equals("player.vimeo.com", StringComparison.OrdinalIgnoreCase)
            ? @"\A/video/(?<id>[0-9]{1,15})/?\z"
            : @"\A/(?:(?:channels/[^/]+/|groups/[^/]+/videos/|(?:album|showcase)/[0-9]+/video/))?(?<id>[0-9]{1,15})(?:/(?<hash>[A-Za-z0-9]{1,64}))?/?\z";
        var match = Regex.Match(url.AbsolutePath, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success || match.Groups["id"].Value.Trim('0').Length == 0)
            throw new NotSupportedException("This Vimeo address does not identify a supported video; collection and channel enumeration remain pending.");
        string? hash = match.Groups["hash"].Success ? match.Groups["hash"].Value : null;
        foreach (var field in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = field.Split('=', 2);
            if (Uri.UnescapeDataString(pair[0]) == "h")
            {
                var value = Uri.UnescapeDataString(pair.Length == 2 ? pair[1] : "");
                if (hash is not null && hash != value) throw new ArgumentException("The Vimeo URL contains conflicting unlisted-video identifiers.");
                hash = value;
            }
        }
        if (hash is not null && !Regex.IsMatch(hash, @"\A[A-Za-z0-9]{1,64}\z")) throw new ArgumentException("The Vimeo unlisted-video identifier is invalid.");
        return new(match.Groups["id"].Value.TrimStart('0'), hash);
    }
    internal static Uri? ReadEmbed(IReadOnlyDictionary<string, string> attributes)
    {
        attributes.TryGetValue("data-vimeo-id", out var id);
        attributes.TryGetValue("data-vimeo-url", out var value);
        if (id is null && value is null) return null;
        if (id is not null && (!Regex.IsMatch(id, @"\A[0-9]{1,15}\z") || id.Trim('0').Length == 0))
            throw new InvalidDataException("The Vimeo embed has an invalid video identity.");
        if (value is null) return new VimeoAddress(id!.TrimStart('0'), null).PlayerUrl;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || !IsVimeoHost(url)) throw new InvalidDataException("The Vimeo embed has an invalid player URL.");
        var address = Parse(url);
        if (id is not null && id.TrimStart('0') != address.VideoId) throw new InvalidDataException("The Vimeo embed has conflicting video identities.");
        return address.PlayerUrl;
    }
    internal static FileLink? ParseFileLink(Uri url)
    {
        if (!IsVimeoHost(url) || url.Host != "player.vimeo.com") return null;
        var match = Regex.Match(url.AbsolutePath,
            @"\A/(?:progressive_redirect/playback/(?<id>[0-9]{1,15})/(?:container/[A-Za-z0-9-]{1,64}/[A-Za-z0-9_-]{1,128}|rendition/[A-Za-z0-9_-]{1,32}/file\.mp4)|play/c/(?<id>[0-9]{1,15})/(?<type>hls\.m3u8|dash\.mpd)|external/(?<id>[0-9]{1,15})(?:\.(?:sd|hd))?\.(?<type>mp4|m3u8|mpd))\z",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) return null;
        if (match.Groups["id"].Value.Trim('0').Length == 0 || url.Scheme != "https" || url.Fragment.Length != 0)
            throw new ArgumentException("Use a valid HTTPS Vimeo file link.");
        var type = match.Groups["type"].Value;
        return new(match.Groups["id"].Value.TrimStart('0'), type.EndsWith("m3u8", StringComparison.Ordinal) ? MediaKind.Hls : type.EndsWith("mpd", StringComparison.Ordinal) ? MediaKind.Dash : MediaKind.Progressive);
    }
}
