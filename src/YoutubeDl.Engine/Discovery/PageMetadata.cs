using System.Net;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Discovery;

public sealed record PageMetadata
{
    public string Extractor { get; init; } = "generic";
    public string? Id { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public Download.DownloadArchiveIdentity? ArchiveIdentity { get; init; }
    public double? DurationSeconds { get; init; }
    public DateOnly? UploadDate { get; init; }
    public DateTimeOffset? UploadTimestamp { get; init; }
    public long? ViewCount { get; init; }
    public int? AgeLimit { get; init; }
    public IReadOnlyDictionary<string, object?> MetadataFields { get; init; } = new Dictionary<string, object?>();
    [System.Text.Json.Serialization.JsonIgnore]
    internal IReadOnlyDictionary<string, string?> ParsedMetadata { get; init; } = new Dictionary<string, string?>();
    public bool IsLive { get; init; }
    public Uri? PlayerScriptUrl { get; init; }
    public string? PlaylistId { get; init; }
    public bool IsPlaylist { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal bool RevalidateCollectionMembership {get;init;}
    // A single clear metadata-only declaration may use its page's selected player.
    [System.Text.Json.Serialization.JsonIgnore]
    internal bool AllowPagePlayer { get; init; }
    public IReadOnlyList<NativePlaylistEntry> Entries { get; init; } = [];
    public IReadOnlyList<NativeFormat> ReportedFormats { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string Title { get; init; } = "";
    public string? Description { get; init; }
    public string? ThumbnailUrl { get; init; }
    public IReadOnlyList<ThumbnailCandidate> Thumbnails { get; init; } = [];
    public string? Uploader { get; init; }
    public IReadOnlyList<MediaCandidate> Media { get; init; } = [];
    public IReadOnlyList<SubtitleTrack> Subtitles { get; init; } = [];
    public IReadOnlyList<ChapterMarker> Chapters { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    internal string? ChapterRefusalReason { get; init; }
    public IReadOnlyList<Uri> EmbeddedPages { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    internal Download.DownloadArchiveIdentity? DelegatedArchiveIdentity {get;init;}
    public Uri? SourcePageUrl { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal bool AnonymousPlayback { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal bool PublicServicePlayback { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal Http.IPlaybackAuthorization? PlaybackAuthorization { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal IReadOnlyList<Http.ScopedMediaQuery> PlaybackQueries { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public Providers.YouTube.YouTubePlaybackCookies? PlaybackCookies { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyDictionary<string, string> PlaybackHeaders { get; init; } = new Dictionary<string, string>();
    public bool DrmRefused { get; init; }
    public string? RefusalReason { get; init; }
}

public static partial class PageMetadataReader
{
    public static PageMetadata Read(string html, Uri pageUrl)
    {
        var title = First(
            ReadMeta(html, "og:title"),
            ReadMeta(html, "twitter:title"),
            ReadTitle(html),
            pageUrl.Host);
        var thumbnail = First(ReadMeta(html, "og:image"), ReadMeta(html, "twitter:image"));
        return new PageMetadata
        {
            Title = WebUtility.HtmlDecode(title ?? pageUrl.Host),
            Description = Decode(ReadMeta(html, "og:description") ?? ReadMeta(html, "description")),
            ThumbnailUrl = Resolve(thumbnail, pageUrl),
            Uploader = Decode(ReadMeta(html, "og:site_name")),
            Media = PageMediaExtractor.Extract(html, pageUrl)
        };
    }

    private static string? ReadTitle(string html)
    {
        var match = TitleTag().Match(html);
        return match.Success ? match.Groups["title"].Value : null;
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

    private static string? Resolve(string? raw, Uri pageUrl)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return Uri.TryCreate(pageUrl, WebUtility.HtmlDecode(raw), out var uri) &&
               uri.Scheme is "http" or "https"
            ? uri.AbsoluteUri
            : null;
    }

    private static string? Decode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : WebUtility.HtmlDecode(value).Trim();

    private static string? First(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    [GeneratedRegex("<title[^>]*>(?<title>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex TitleTag();

    [GeneratedRegex("<meta[^>]+(?:property|name)=[\"'](?<key>[^\"']+)[\"'][^>]+content=[\"'](?<content>[^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetaTag();
}
