using System.Net;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Discovery;

/// <summary>
/// Reads media URLs that a page put in script, JSON, or a capture log. This is
/// the in-process stand-in for watching a browser's media requests: the same
/// URL shapes are accepted, and encrypted player sessions are refused.
/// </summary>
public static partial class PageCapture
{
    public static CaptureResult Read(string text, Uri pageUrl)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new CaptureResult();
        if (DrmMarker().IsMatch(text))
        {
            return new CaptureResult
            {
                Refused = true,
                Reason = "This player uses encrypted media. The native engine does not remove DRM."
            };
        }

        var media = new Dictionary<string, MediaCandidate>(StringComparer.OrdinalIgnoreCase);
        var subtitles = new Dictionary<string, SubtitleTrack>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in QuotedUrl().Matches(text))
        {
            var raw = WebUtility.HtmlDecode(match.Groups["url"].Value)
                .Replace("\\/", "/", StringComparison.Ordinal)
                .Replace("\\u0026", "&", StringComparison.Ordinal);
            if (!Uri.TryCreate(pageUrl, raw, out var uri) || uri.Scheme is not ("http" or "https"))
                continue;
            var kind = PageMediaExtractor.Classify(uri);
            if (kind == MediaKind.Unknown)
            {
                if (IsSubtitle(uri) && !subtitles.ContainsKey(uri.AbsoluteUri))
                {
                    subtitles[uri.AbsoluteUri] = new SubtitleTrack
                    {
                        Url = uri,
                        Extension = Path.GetExtension(uri.AbsolutePath).TrimStart('.').ToLowerInvariant(),
                        Language = LanguageFrom(uri)
                    };
                }
                continue;
            }

            if (media.ContainsKey(uri.AbsoluteUri)) continue;
            media[uri.AbsoluteUri] = new MediaCandidate
            {
                Url = uri,
                Extension = PageMediaExtractor.ExtensionFrom(uri),
                Kind = kind,
                Source = "capture",
                Height = HeightFrom(uri)
            };
        }

        return new CaptureResult { Media = media.Values.ToList(), Subtitles = subtitles.Values.ToList() };
    }

    private static bool IsSubtitle(Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath).TrimStart('.');
        return extension.Equals("vtt", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals("srt", StringComparison.OrdinalIgnoreCase);
    }

    private static string LanguageFrom(Uri uri)
    {
        var name = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
        var parts = name.Split('.', '_', '-');
        return parts.Length > 1 && parts[^1].Length is >= 2 and <= 8 ? parts[^1] : "";
    }

    private static int? HeightFrom(Uri uri)
    {
        var match = HeightToken().Match(uri.AbsoluteUri);
        return match.Success && int.TryParse(match.Groups["height"].Value, out var height) ? height : null;
    }

    [GeneratedRegex("(?<url>https?:\\\\?/\\\\?/[^\\s\"'<>\\\\]+|https?://[^\\s\"'<>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuotedUrl();

    [GeneratedRegex("(?:height[=:]|\\b)(?<height>2160|1440|1080|720|480|360|240)p?\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeightToken();

    [GeneratedRegex("widevine|playready|clearkey|\"cenc\"|skd://", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DrmMarker();
}

public sealed class CaptureResult
{
    public IReadOnlyList<MediaCandidate> Media { get; init; } = [];
    public IReadOnlyList<SubtitleTrack> Subtitles { get; init; } = [];
    public bool Refused { get; init; }
    public string? Reason { get; init; }
}
