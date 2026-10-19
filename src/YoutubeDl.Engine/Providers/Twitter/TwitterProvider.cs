using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Twitter;

public sealed class TwitterProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("twitter", "X/Twitter public post videos and media inventories", 200);
    public bool CanHandle(Uri url) => TwitterAddress.Matches(url);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = TwitterAddress.Parse(request.Url);
        using var anonymous = http.CreateAnonymousSession();
        using var session = anonymous.WithHeaders(new Dictionary<string, string> { ["Referer"] = "https://platform.twitter.com/", ["User-Agent"] = string.IsNullOrWhiteSpace(request.UserAgent) ? MediaHttp.DefaultUserAgent : request.UserAgent });
        var endpoint = new Uri("https://cdn.syndication.twimg.com/tweet-result?id=" + address.Id + "&lang=en&token=" + TwitterEmbedToken.Create(address.Id));
        var document = await session.GetDocumentAsync(endpoint, 4 * 1024 * 1024, ct).ConfigureAwait(false);
        if (document.Url != endpoint) throw new InvalidDataException("X redirected its public post metadata to another resource.");
        using var json = JsonDocument.Parse(document.Text, new() { MaxDepth = 48 }); var root = json.RootElement;
        if (Text(root, "__typename") == "TweetTombstone" || Text(root, "id_str") is null) throw new NotSupportedException("This X/Twitter post is unavailable, protected or unsupported by the public embed.");
        if (Text(root, "id_str") != address.Id) throw new InvalidDataException("X returned another post identity.");
        var user = Object(root, "user"); var title = Text(root, "text") ?? "X post " + address.Id;
        if (title.Length > 16384) throw new InvalidDataException("The public post text exceeds its supported size.");
        var details = Array(root, "mediaDetails"); if (details.Length > 32) throw new InvalidDataException("The post contains too many media positions.");
        var videos = details.Select((item, index) => (Item: item, Index: index + 1)).Where(pair => Text(pair.Item, "type") is "video" or "animated_gif").ToArray();
        if (address.MediaIndex is { } requested)
        {
            if (requested > details.Length || Text(details[requested - 1], "type") is not ("video" or "animated_gif")) throw new NotSupportedException("The selected X/Twitter media position is not an available native video.");
            return Video(details[requested - 1], requested, details.Length, root, user, title, address);
        }
        if (videos.Length == 1) return Video(videos[0].Item, videos[0].Index, details.Length, root, user, title, address);
        if (videos.Length > 1)
        {
            if (details.Length > request.MaximumPlaylistEntries) throw new InvalidDataException("The post media inventory exceeds the configured entry limit; it was not truncated.");
            var entries = new List<NativePlaylistEntry>();
            for (var index = 0; index < details.Length; index++)
            {
                var available = Text(details[index], "type") is "video" or "animated_gif"; var position = index + 1;
                var url = new Uri(address.Page.AbsoluteUri + "/video/" + position);
                PageMetadata? resolved = null;
                if (available)
                {
                    try { resolved = Video(details[index], position, details.Length, root, user, title, address); }
                    catch (NotSupportedException) { available = false; }
                }
                entries.Add(new(position, address.Id + "-" + position, title, url, Number(Object(details[index], "video_info"), "duration_millis") / 1000,
                    Image(Text(details[index], "media_url_https")), available, resolved,
                    ExpectedArchiveIdentity: new("twitter:media", address.Id + "-" + position)));
            }
            return new() { Id = address.Id, PlaylistId = address.Id, Extractor = Info.Id, Title = title, Description = title, Uploader = Text(user, "name"),
                IsPlaylist = true, Entries = entries, SourcePageUrl = address.Page,
                Warnings = entries.Any(entry => !entry.Available) ? ["Photos and unavailable or unsupported videos retain their original media positions."] : [] };
        }
        var quoted = Object(root, "quoted_tweet");
        if (Text(quoted, "id_str") is { } quotedId && ulong.TryParse(quotedId, NumberStyles.None, CultureInfo.InvariantCulture, out var quoteNumber) && quoteNumber > 0 && quotedId != address.Id &&
            Array(quoted, "mediaDetails").Any(item => Text(item, "type") is "video" or "animated_gif"))
            return new() { Id = address.Id, Extractor = Info.Id, Title = title, Description = title, EmbeddedPages = [new("https://x.com/i/status/" + quotedId)], SourcePageUrl = address.Page };
        return new() { Id = address.Id, Extractor = Info.Id, Title = title, Description = title, Uploader = Text(user, "name"), SourcePageUrl = address.Page,
            Warnings = ["This public post does not expose a supported native video. Photos, quoted/retweet media, external cards or account playback may need further support."] };
    }
    private static PageMetadata Video(JsonElement item, int position, int total, JsonElement root, JsonElement user, string title, TwitterAddress address)
    {
        var availability = Text(Object(item, "ext_media_availability"), "status");
        if (availability is not null && !availability.Equals("Available", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("The requested post video is not available to the public embed.");
        var info = Object(item, "video_info"); var variants = Array(info, "variants"); if (variants.Length > 64) throw new InvalidDataException("X returned too many video variants.");
        var duration = Number(info, "duration_millis") / 1000; var type = Text(item, "type"); var media = new List<MediaCandidate>(); string? mediaIdentity = null;
        foreach (var variant in variants)
        {
            var mime = Text(variant, "content_type"); if (mime is not ("video/mp4" or "application/x-mpegURL")) continue;
            var url = Resource(Text(variant, "url")); var match = Regex.Match(url.AbsolutePath, @"\A/(?<family>amplify_video|ext_tw_video)/(?<id>[0-9]+)/", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            var gif = type == "animated_gif" ? Regex.Match(url.AbsolutePath, @"\A/tweet_video/(?<asset>[A-Za-z0-9_-]+)\.mp4\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) : Match.Empty;
            if (!match.Success && !gif.Success) throw new NotSupportedException("This X/Twitter media URL family needs additional native support.");
            var identity = match.Success ? match.Groups["family"].Value + "/" + match.Groups["id"].Value : "tweet_video/" + gif.Groups["asset"].Value;
            if (mediaIdentity is not null && identity != mediaIdentity) throw new InvalidDataException("X video variants point to different media identities.");
            mediaIdentity = identity; var progressive = mime == "video/mp4";
            var dimensions = progressive ? Regex.Match(url.AbsolutePath, @"/vid/(?:[A-Za-z0-9._-]+/)?(?<width>[0-9]+)x(?<height>[0-9]+)/", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) : Match.Empty;
            int? width = dimensions.Success ? int.Parse(dimensions.Groups["width"].Value, CultureInfo.InvariantCulture) : type == "animated_gif" ? Integer(Object(item, "original_info"), "width") : null;
            int? height = dimensions.Success ? int.Parse(dimensions.Groups["height"].Value, CultureInfo.InvariantCulture) : type == "animated_gif" ? Integer(Object(item, "original_info"), "height") : null;
            media.Add(new() { Id = progressive ? "mp4-" + (height?.ToString(CultureInfo.InvariantCulture) ?? "unknown") + "-" + media.Count : "hls", Url = url, Kind = progressive ? MediaKind.Progressive : MediaKind.Hls,
                Extension = progressive ? "mp4" : "ts", MimeType = mime, Width = width, Height = height, Bandwidth = Number(variant, "bitrate"), HasVideo = true, HasAudio = type == "animated_gif" ? false : null,
                IsCompleteMedia = true, Source = "twitter", VerificationDemuxer = progressive ? "mov" : "mpegts", VerificationDurationSeconds = duration });
        }
        if (media.Count == 0) throw new NotSupportedException("The public post supplied no supported complete video variants.");
        // X assigns its poster a separate media identity. Bind the poster to the
        // already-verified post item and declared image origin, not the video ID.
        var poster = Image(Text(item, "media_url_https"));
        var logicalId = total > 1 ? address.Id + "-" + position : address.Id;
        return new() { Id = logicalId, Extractor = "twitter", ArchiveIdentity = new(total > 1 ? "twitter:media" : "twitter", logicalId), Title = title, Description = title,
            Uploader = Text(user, "name"), DurationSeconds = duration, ThumbnailUrl = poster, Media = media, SourcePageUrl = address.Page, AnonymousPlayback = true };
    }
    private static Uri Resource(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == "https" && url.Host == "video.twimg.com" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 ?
        url : throw new InvalidDataException("X returned an unsupported video origin.");
    private static string? Image(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == "https" && url.Host == "pbs.twimg.com" && url.UserInfo.Length == 0 ? url.AbsoluteUri : null;
}
