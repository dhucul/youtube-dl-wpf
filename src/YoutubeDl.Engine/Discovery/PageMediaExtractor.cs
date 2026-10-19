using System.Net;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Discovery;

/// <summary>
/// Finds playable media in ordinary HTML: video/source tags, Open Graph,
/// and embedded links to media files or HLS/DASH manifests. Typed JSON-LD is
/// processed separately by the generic provider.
/// Players that only appear after script execution need a later capture step.
/// </summary>
public static partial class PageMediaExtractor
{
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "m4v", "webm", "mkv", "mov", "m4a", "mp3", "aac", "opus", "ogg", "flac", "wav",
        "m3u8", "mpd"
    };

    public static IReadOnlyList<MediaCandidate> Extract(string html, Uri pageUrl)
    {
        if (string.IsNullOrWhiteSpace(html)) return [];

        var found = new Dictionary<string, MediaCandidate>(StringComparer.OrdinalIgnoreCase);
        Add(found, ReadMeta(html, "og:video"), pageUrl, "open-graph");
        Add(found, ReadMeta(html, "og:video:url"), pageUrl, "open-graph");
        Add(found, ReadMeta(html, "og:video:secure_url"), pageUrl, "open-graph");
        Add(found, ReadMeta(html, "twitter:player:stream"), pageUrl, "twitter");

        foreach (Match match in SourceUrl().Matches(html))
            Add(found, WebUtility.HtmlDecode(match.Groups["url"].Value), pageUrl, "video-tag");

        foreach (Match match in AbsoluteMediaUrl().Matches(html))
            Add(found, WebUtility.HtmlDecode(match.Value), pageUrl, "embedded-url");

        return found.Values
            .OrderByDescending(static candidate => candidate.Rank)
            .ToList();
    }

    public static MediaKind Classify(Uri url, string? mimeType = null)
    {
        if (mimeType?.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) == true ||
            url.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            return MediaKind.Hls;
        if (mimeType?.Contains("dash+xml", StringComparison.OrdinalIgnoreCase) == true ||
            url.AbsolutePath.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase))
            return MediaKind.Dash;
        if (mimeType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true ||
            mimeType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true ||
            HasMediaExtension(url))
            return MediaKind.Progressive;
        return MediaKind.Unknown;
    }

    public static string? ExtensionFrom(Uri url, string? mimeType = null)
    {
        var pathExtension = Path.GetExtension(url.AbsolutePath).TrimStart('.');
        if (MediaExtensions.Contains(pathExtension)) return pathExtension.ToLowerInvariant();
        return mimeType?.Split(';')[0].Trim().ToLowerInvariant() switch
        {
            "video/mp4" => "mp4",
            "video/webm" => "webm",
            "video/quicktime" => "mov",
            "audio/mp4" or "audio/m4a" or "audio/x-m4a" => "m4a",
            "audio/mpeg" => "mp3",
            "audio/aac" or "audio/aacp" or "audio/x-aac" => "aac",
            "audio/ogg" => "ogg",
            "audio/opus" => "opus",
            "audio/flac" or "audio/x-flac" => "flac",
            "audio/wav" or "audio/x-wav" or "audio/vnd.wave" => "wav",
            "application/vnd.apple.mpegurl" or "application/x-mpegurl" or "audio/mpegurl" or "audio/x-mpegurl" => "m3u8",
            "application/dash+xml" => "mpd",
            _ => null
        };
    }

    private static void Add(Dictionary<string, MediaCandidate> found, string? raw, Uri pageUrl, string source)
    {
        if (!TryCreate(raw, pageUrl, out var url)) return;
        var kind = Classify(url);
        if (kind == MediaKind.Unknown || found.ContainsKey(url.AbsoluteUri)) return;
        var extension = ExtensionFrom(url);
        found[url.AbsoluteUri] = new MediaCandidate
        {
            Url = url,
            Extension = extension,
            Kind = kind,
            Source = source,
            MimeType = extension is "m4a" or "mp3" or "aac" or "opus" or "ogg" or "flac" or "wav"
                ? "audio/" + extension
                : null
        };
    }

    private static bool TryCreate(string? raw, Uri pageUrl, out Uri url)
    {
        url = null!;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var decoded = WebUtility.HtmlDecode(raw).Trim().Trim('"', '\'');
        if (decoded.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ||
            decoded.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!Uri.TryCreate(pageUrl, decoded, out var created) || created.Scheme is not ("http" or "https"))
            return false;
        url = created;
        return Classify(created) != MediaKind.Unknown;
    }

    private static string? ReadMeta(string html, string property)
    {
        foreach (Match match in MetaTag().Matches(html))
        {
            if (match.Groups["key"].Value.Equals(property, StringComparison.OrdinalIgnoreCase))
                return match.Groups["content"].Value;
        }

        return null;
    }

    private static bool HasMediaExtension(Uri url) =>
        MediaExtensions.Contains(Path.GetExtension(url.AbsolutePath).TrimStart('.'));

    [GeneratedRegex("<meta[^>]+(?:property|name)=[\"'](?<key>[^\"']+)[\"'][^>]+content=[\"'](?<content>[^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetaTag();

    [GeneratedRegex("(?:<source[^>]+src=|<video[^>]+src=)[\"'](?<url>[^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SourceUrl();

    [GeneratedRegex("https?://[^\\s\"'<>]+\\.(?:mp4|m4v|webm|mkv|mov|m4a|mp3|m3u8|mpd)(?:\\?[^\\s\"'<>]*)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AbsoluteMediaUrl();

}
