using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Providers;

/// <summary>Discovers HTML media and podcast feeds; complex sites need dedicated providers.</summary>
public sealed class GenericPageProvider : IMediaProvider
{
    private static readonly string[] DedicatedDomains = ["youtube.com", "youtu.be", "youtube-nocookie.com", "vimeo.com", "twitch.tv", "tiktok.com", "bilibili.com", "rumble.com", "iheart.com", "omny.fm", "jamendo.com", "newgrounds.com", "audiomack.com", "bandlab.com", "hearthis.at", "hearthis.app", "reverbnation.com",
        "player.megaphone.fm", "player-origin.megaphone.fm", "playlist.megaphone.fm", "cms.megaphone.fm", "megaphone.link", "podomatic.com", "audius.co", "mixcloud.com", "vocaroo.com", "voca.ro", "clyp.it"];
    public MediaProviderInfo Info { get; } = new("generic", "HTML media and podcast feeds", -100);
    public NativeAuthenticationCapabilities AuthenticationCapabilities=>NativeAuthenticationCapabilities.Hosting;

    public bool CanHandle(Uri url) => !DedicatedDomains.Any(domain =>
        url.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) || url.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => PublicVideoPageIdentity.Read(request.Url);

    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        if (PornhubPlaybackSession.CanHandle(request.Url))
            return await http.SessionState("playback:public-player:" + request.Url.AbsoluteUri,
                () => new PornhubPlaybackSession(http, request, ParseDocumentAsync)).ReadAsync(ct).ConfigureAwait(false);
        // These player pages issue expiring addresses. Cached HTML can contain
        // an obsolete CDN signature even while its advertised expiry is future.
        var freshPlayer = new[] { "pornhub.com", "xhamster.com" }.Any(host => request.Url.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || request.Url.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
        IReadOnlyDictionary<string, string>? pageHeaders = freshPlayer ? new Dictionary<string, string> { ["Cache-Control"] = "no-cache", ["Pragma"] = "no-cache" } : null;
        // A normal navigation has no self-referrer. The automatically supplied
        // page referrer selects a stale playback route on these publishers.
        // Explicit caller referrers and the later CDN referrer remain intact.
        using var navigation = freshPlayer && string.IsNullOrWhiteSpace(request.Referer) ? http.WithHeaders(new Dictionary<string, string> { ["Referer"] = "" }) : null;
        var document = await (navigation ?? http).GetDocumentAsync(request.Url, 5 * 1024 * 1024, ct, headers: pageHeaders, mediaHeadersOnly: true).ConfigureAwait(false);
        return PublicVideoPageIdentity.Bind(request.Url, await ParseDocumentAsync(request, http, document, ct).ConfigureAwait(false));
    }

    private static async Task<PageMetadata> ParseDocumentAsync(NativeDownloadRequest request, MediaHttp http, MediaDocument document, CancellationToken ct)
    {
        var kind = PageMediaExtractor.Classify(document.Url, document.ContentType);
        if (kind != MediaKind.Unknown)
        {
            var extension = PageMediaExtractor.ExtensionFrom(document.Url, document.ContentType);
            var audio = document.ContentType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true;
            return new() { Extractor = "generic:redirect", Title = Path.GetFileNameWithoutExtension(document.Url.AbsolutePath),
                Media = [new() { Url = document.Url, Kind = kind, Extension = extension, MimeType = document.ContentType, HasVideo = audio ? false : null, HasAudio = audio ? true : null, Source = "redirect" }] };
        }
        var html = document.Text;
        if (Feeds.MediaFeedParser.IsFeed(html, document.ContentType))
            return await Feeds.MediaFeedReader.ReadAsync(document, request, http, ct).ConfigureAwait(false);
        var structured = StructuredMediaReader.Read(html, document.Url, request.MaximumPlaylistEntries, ct);
        html = structured.RemainingHtml;
        var page = PageMetadataReader.Read(html, document.Url);
        if (structured.Media is { } metadata)
        {
            if (metadata.AllowPagePlayer && CommonPlayerReader.Read(html, document.Url, page with { DurationSeconds = metadata.DurationSeconds }, ct) is { } selected)
                return selected with { Title = string.IsNullOrWhiteSpace(metadata.Title) ? selected.Title : metadata.Title,
                    Description = metadata.Description ?? selected.Description, ThumbnailUrl = metadata.ThumbnailUrl ?? selected.ThumbnailUrl,
                    Uploader = metadata.Uploader ?? selected.Uploader, DurationSeconds = metadata.DurationSeconds, UploadDate = metadata.UploadDate,
                    ViewCount = metadata.ViewCount, MetadataFields = metadata.MetadataFields, Thumbnails = metadata.Thumbnails, Subtitles = metadata.Subtitles };
            return metadata with { Title = string.IsNullOrWhiteSpace(metadata.Title) ? page.Title : metadata.Title,
                Description = metadata.Description ?? page.Description, ThumbnailUrl = metadata.ThumbnailUrl ?? page.ThumbnailUrl,
                Uploader = metadata.Uploader ?? page.Uploader };
        }
        var player = CommonPlayerReader.Read(html, document.Url, page, ct);
        if (player is not null) return player;
        var captured = PageCapture.Read(html + "\n" + (request.CaptureText ?? ""), document.Url);
        return new PageMetadata
        {
            Extractor = "generic",
            Title = page.Title,
            Description = page.Description,
            ThumbnailUrl = page.ThumbnailUrl,
            Uploader = page.Uploader,
            Media = page.Media.Concat(captured.Media).GroupBy(item => item.Url.AbsoluteUri, StringComparer.Ordinal)
                .Select(group => group.First()).ToList(),
            Subtitles = captured.Subtitles,
            Chapters = page.Chapters,
            EmbeddedPages = (document.Url != request.Url ? new[] { document.Url } : []).Concat(EmbeddedMediaReader.Read(html, document.Url)).ToArray(),
            SourcePageUrl = document.Url,
            DrmRefused = captured.Refused && page.Media.Count == 0,
            RefusalReason = captured.Reason
        };
    }
}
