using System.Net;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Reddit;

public sealed class RedditProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("reddit", "Reddit post videos and supplied media resources", 200);
    public bool CanHandle(Uri url) => RedditAddress.IsHost(url);
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)
    {
        var address = RedditAddress.Parse(request.Url);
        return address.MediaResource ? new("reddit:video", address.Id) : null;
    }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = RedditAddress.Parse(request.Url);
        if (address.MediaResource)
            return new() { Id = address.Id, Title = address.Id, Extractor = "reddit:video", SourcePageUrl = address.Url,
                Media = [new() { Id = address.Kind!.Value.ToString().ToLowerInvariant(), Url = address.Url, Kind = address.Kind.Value,
                    Extension = PageMediaExtractor.ExtensionFrom(address.Url), Source = Info.Id }] };
        using var session = http.WithHeaders(new Dictionary<string, string> { ["User-Agent"] = string.IsNullOrWhiteSpace(request.UserAgent) ? "YoutubeDlNative/" + NativeEngine.Version : request.UserAgent });
        var document = await session.GetDocumentAsync(address.MetadataUrl, 4 * 1024 * 1024, ct).ConfigureAwait(false);
        if (document.Url != address.MetadataUrl) throw new InvalidDataException("Reddit redirected the post metadata to a different resource.");
        ct.ThrowIfCancellationRequested();
        using var json = JsonDocument.Parse(document.Text);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() is < 1 or > 2 || Text(root[0], "kind") != "Listing")
            throw new InvalidDataException("Reddit did not return a supported post listing.");
        var children = Array(Object(root[0], "data"), "children");
        if (children.Length != 1 || Text(children[0], "kind") != "t3") throw new InvalidDataException("Reddit returned an ambiguous post listing.");
        var post = Object(children[0], "data"); ValidatePost(post, address.Id);
        var source = post;
        var visited = new HashSet<string>(StringComparer.Ordinal) { address.Id };
        for (var depth = 0; Video(source).ValueKind == JsonValueKind.Undefined && Array(source, "crosspost_parent_list") is { Length: > 0 } parents; depth++)
        {
            if (parents.Length != 1 || depth >= 5 || Text(source, "crosspost_parent") is not { } parentName || !parentName.StartsWith("t3_", StringComparison.Ordinal))
                throw new NotSupportedException("The Reddit crosspost does not expose one supported source post.");
            var parentId = parentName[3..];
            if (!visited.Add(parentId)) throw new InvalidDataException("The Reddit crosspost source is recursive.");
            source = parents[0]; ValidatePost(source, parentId);
        }
        if (True(post, "is_gallery") || True(source, "is_gallery")) throw new NotSupportedException("Reddit gallery and mixed-media posts require additional native support.");
        var video = Video(source);
        var media = new List<MediaCandidate>(); var embeds = new List<Uri>(); var warnings = new List<string>();
        if (video.ValueKind != JsonValueKind.Undefined)
        {
            if (Text(video, "transcoding_status") is { } status && status != "completed") throw new NotSupportedException("Reddit has not completed processing this video.");
            if (True(video, "is_live") || True(source, "is_live")) throw new NotSupportedException("Reddit live recording requires additional native support.");
            string? asset = null;
            Add("hls", "hls_url", MediaKind.Hls, "m3u8");
            Add("dash", "dash_url", MediaKind.Dash, "mpd");
            var gif = True(video, "is_gif");
            if (Text(video, "fallback_url") is { Length: > 0 } fallback)
            {
                var url = MediaUrl(fallback); VerifyAsset(url);
                if (PageMediaExtractor.ExtensionFrom(url) != "mp4") throw new InvalidDataException("Reddit returned an unsupported fallback resource.");
                // A fallback MP4 does not establish the presence of Reddit's separate audio.
                // Only the server's explicit GIF-like flag makes it a complete silent video.
                media.Add(new() { Id = "fallback", Url = url, Kind = MediaKind.Progressive, Extension = "mp4", HasVideo = true, HasAudio = false,
                    IsCompleteMedia = gif, IsFallback = media.Count > 0, Width = PositiveInteger(video, "width"), Height = PositiveInteger(video, "height"),
                    Bandwidth = Number(video, "bitrate_kbps") is > 0 and var bitrate ? bitrate * 1000 : null, Source = Info.Id });
                if (!gif && media.Count == 1) warnings.Add("Only Reddit's video-only fallback is exposed; use video-only mode or a complete supplied manifest for this source.");
            }
            if (Text(source, "url_overridden_by_dest") is { } declared && Uri.TryCreate(WebUtility.HtmlDecode(declared), UriKind.Absolute, out var declaredUrl) && declaredUrl.Host == "v.redd.it") VerifyAsset(declaredUrl);
            void VerifyAsset(Uri url)
            {
                var id = RedditAddress.AssetId(url);
                if (asset is not null && asset != id) throw new InvalidDataException("Reddit returned conflicting video identities.");
                asset = id;
            }
            void Add(string id, string field, MediaKind kind, string extension)
            {
                if (Text(video, field) is not { Length: > 0 } value) return;
                var url = MediaUrl(value); VerifyAsset(url);
                if (PageMediaExtractor.Classify(url) != kind) throw new InvalidDataException("Reddit returned a different type of manifest.");
                media.Add(new() { Id = id, Url = url, Kind = kind, Extension = extension, Source = Info.Id });
            }
        }
        else if ((Text(source, "url_overridden_by_dest") ?? Text(source, "url")) is { } linked)
        {
            if (Uri.TryCreate(WebUtility.HtmlDecode(linked), UriKind.Absolute, out var target) && target.Scheme is "http" or "https" && target.UserInfo.Length == 0 &&
                !RedditAddress.IsHost(target)) embeds.Add(target);
        }
        var images = Thumbnails(post);
        if (images.Count == 0 && Text(source, "id") != address.Id) images = Thumbnails(source);
        return new() { Id = address.Id, Extractor = Info.Id, Title = Text(post, "title") ?? address.Id, Description = Text(post, "selftext"),
            Uploader = Text(post, "author"), DurationSeconds = Number(video, "duration"), Media = media, EmbeddedPages = embeds,
            Thumbnails = images, ThumbnailUrl = images.OrderByDescending(image => (long)(image.Width ?? 0) * (image.Height ?? 0)).FirstOrDefault()?.Url.AbsoluteUri,
            SourcePageUrl = address.Url, Warnings = warnings, PlaybackHeaders = new Dictionary<string, string> { ["Referer"] = address.Url.AbsoluteUri } };
    }

    private static void ValidatePost(JsonElement post, string expectedId)
    {
        if (!RedditAddress.IsPostId(expectedId) || Text(post, "id") != expectedId || Text(post, "name") is { } name && name != "t3_" + expectedId)
            throw new InvalidDataException("Reddit returned another post identity.");
        if (Text(post, "permalink") is { } path)
        {
            if (!Uri.TryCreate(new Uri("https://www.reddit.com/"), path, out var permalink) || !RedditAddress.IsHost(permalink)) throw new InvalidDataException("Reddit returned an invalid permalink.");
            var address = RedditAddress.Parse(permalink);
            if (address.MediaResource || address.Id != expectedId) throw new InvalidDataException("Reddit returned an inconsistent post permalink.");
        }
        if (Text(post, "removed_by_category") is { Length: > 0 } || Text(post, "subreddit_type") == "private" || True(post, "is_deleted"))
            throw new NotSupportedException("This Reddit post is removed or requires additional account access.");
    }
    private static JsonElement Video(JsonElement source)
    {
        var secure = Object(Object(source, "secure_media"), "reddit_video"); var plain = Object(Object(source, "media"), "reddit_video");
        if (secure.ValueKind != JsonValueKind.Undefined && plain.ValueKind != JsonValueKind.Undefined)
            foreach (var field in new[] { "hls_url", "dash_url", "fallback_url" })
                if (Text(secure, field) is { Length: > 0 } first && Text(plain, field) is { Length: > 0 } second && MediaUrl(first) != MediaUrl(second))
                    throw new InvalidDataException("Reddit returned conflicting secure and public video descriptors.");
        return secure.ValueKind != JsonValueKind.Undefined ? secure : plain;
    }
    private static Uri MediaUrl(string value)
    {
        if (!Uri.TryCreate(WebUtility.HtmlDecode(value), UriKind.Absolute, out var url)) throw new InvalidDataException("Reddit returned an invalid media URL.");
        _ = RedditAddress.AssetId(url); return url;
    }
    private static List<ThumbnailCandidate> Thumbnails(JsonElement post)
    {
        var found = new List<ThumbnailCandidate>();
        foreach (var image in Array(Object(post, "preview"), "images"))
            foreach (var value in Array(image, "resolutions").Append(Object(image, "source")))
            {
                if (found.Count >= 256) throw new InvalidDataException("Reddit returned too many preview images.");
                if (ImageUrl(Text(value, "url")) is { } url) found.Add(new() { Id = "preview-" + found.Count, Url = url, Width = Integer(value, "width"), Height = Integer(value, "height") });
            }
        if (ImageUrl(Text(post, "thumbnail")) is { } thumbnail) found.Add(new() { Id = "thumbnail", Url = thumbnail, Width = Integer(post, "thumbnail_width"), Height = Integer(post, "thumbnail_height") });
        return found;
    }
    private static Uri? ImageUrl(string? value) => Uri.TryCreate(WebUtility.HtmlDecode(value), UriKind.Absolute, out var url) && url.Scheme == "https" && url.IsDefaultPort &&
        url.UserInfo.Length == 0 && url.Host is "i.redd.it" or "preview.redd.it" or "external-preview.redd.it" or "b.thumbs.redditmedia.com" or "a.thumbs.redditmedia.com" ? url : null;
    private static int? PositiveInteger(JsonElement value, string name) => Integer(value, name) is > 0 and var number ? number : null;
}
