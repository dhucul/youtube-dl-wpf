using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Tumblr;

public sealed class TumblrProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("tumblr", "Tumblr public themed post media and players", 200);
    public bool CanHandle(Uri url) => TumblrAddress.Matches(url);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => new("tumblr", TumblrAddress.Parse(request.Url).Id);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = TumblrAddress.Parse(request.Url);
        using var anonymous = http.CreateAnonymousSession();
        var document = await anonymous.GetDocumentAsync(address.Page, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        if (TumblrAddress.Parse(document.Url) is not { } returned || returned.Id != address.Id || returned.Blog != address.Blog)
            throw new InvalidDataException("Tumblr redirected to another post/blog identity.");
        var article = TumblrPostReader.Article(document.Text, address.Id); var metadata = PageMetadataReader.Read(document.Text, document.Url);
        var title = metadata.Title; var entries = new List<NativePlaylistEntry>(); var native = new List<PageMetadata>(); var external = new List<Uri>();
        var position = 0;
        foreach (var json in TumblrPostReader.Blocks(article))
        {
            position++;
            using var block = JsonDocument.Parse(json, new() { MaxDepth = 48 }); var root = block.RootElement; var type = Text(root, "type");
            if (type == "paywall") throw new NotSupportedException("This Tumblr post requires additional native account/subscription support.");
            if (type is not ("video" or "audio"))
            { entries.Add(new(position, address.Id + "-block-" + position, title, null, null, null, false)); continue; }
            if (Text(root, "provider") != "tumblr")
            {
                var offered = Text(root, "embed_url") ?? Text(Object(root, "embed_iframe"), "url") ?? Text(root, "url");
                if (External(offered) is { } delegated) { external.Add(delegated); entries.Add(new(position, null, "Embedded Tumblr media", delegated, null, null, true)); }
                else entries.Add(new(position, null, "Unavailable Tumblr media", null, null, null, false));
                continue;
            }
            var media = Object(root, "media"); var url = Resource(Text(media, "url") ?? Text(root, "url"));
            if (Text(media, "media_key") is { } suppliedKey && !MediaKey(url, suppliedKey)) throw new InvalidDataException("Tumblr's native resource does not match its declared media key.");
            Uri? original = null;
            if (Text(media, "url") is { } one && Text(root, "url") is { } two && new Uri(one) != new Uri(two))
            {
                original = Resource(two); var key = Text(media, "media_key");
                if (key is null || !MediaKey(url, key) || !MediaKey(original, key)) throw new InvalidDataException("Tumblr returned conflicting native block URLs.");
            }
            var mime = Text(media, "type"); var audio = type == "audio"; var kind = PageMediaExtractor.Classify(url, mime);
            if (kind is not (MediaKind.Progressive or MediaKind.Hls) || mime is not ("video/mp4" or "video/quicktime" or "application/x-mpegURL" or "application/vnd.apple.mpegurl" or "audio/mp3" or "audio/mpeg" or "audio/mp4"))
                throw new NotSupportedException("This Tumblr native media format needs additional support.");
            var extension = kind == MediaKind.Hls ? "ts" : url.AbsolutePath.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ? "mov" : audio ? mime == "audio/mp4" ? "m4a" : "mp3" : "mp4";
            // The themed HTML's NPF annotation uses rounded seconds, unlike the
            // API's documented millisecond field. Do not apply this to API JSON.
            var duration = Number(root, "duration");
            var scaled = Regex.Match(url.AbsolutePath, @"/s(?<height>[0-9]{2,5})/", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            int? height = scaled.Success ? int.Parse(scaled.Groups["height"].Value, System.Globalization.CultureInfo.InvariantCulture) : True(media, "original_dimensions_missing") ? null : Integer(media, "height");
            int? width = scaled.Success || True(media, "original_dimensions_missing") ? null : Integer(media, "width");
            var poster = Array(root, "poster").Select(item => Text(item, "url")).FirstOrDefault(value => value is not null);
            var image = poster is null ? null : Image(poster);
            var asset = Text(media, "media_key")?.Replace(':', '-') ?? System.IO.Path.GetFileNameWithoutExtension(url.AbsolutePath); var id = address.Id + "-" + asset;
            var page = new PageMetadata { Id = id, Extractor = "tumblr", ArchiveIdentity = new("tumblr:media", id), Title = Text(root, "title") ?? title,
                Description = metadata.Description, Uploader = address.Blog, DurationSeconds = duration, ThumbnailUrl = image?.AbsoluteUri, AnonymousPlayback = true, SourcePageUrl = document.Url,
                Media = [new() { Id = "native", Url = url, Kind = kind, Extension = extension, MimeType = mime, Height = audio || kind == MediaKind.Hls ? null : height,
                    Width = audio || kind == MediaKind.Hls ? null : width, HasVideo = !audio, HasAudio = audio ? true : null, IsCompleteMedia = true, Source = "tumblr",
                    VerificationDemuxer = kind == MediaKind.Hls ? "mpegts" : audio && extension == "mp3" ? "mp3" : "mov", VerificationDurationSeconds = duration }] };
            if (original is not null && !audio && kind == MediaKind.Progressive)
                page = page with { Media = page.Media.Concat(new[] { new MediaCandidate { Id = "canonical", Url = original, Kind = kind, Extension = extension, MimeType = mime,
                    Width = null, Height = null,
                    HasVideo = true, HasAudio = null, IsCompleteMedia = true, Source = "tumblr", VerificationDemuxer = "mov", VerificationDurationSeconds = duration } }).ToArray() };
            native.Add(page); entries.Add(new(position, id, page.Title, url, duration, page.ThumbnailUrl, true, page, page.ArchiveIdentity));
        }
        // Legacy native/player declarations are limited to the selected article,
        // never suggested posts elsewhere in the theme document.
        foreach (Match iframe in Regex.Matches(article, @"<iframe\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)))
            if (External(TumblrPostReader.Attributes(iframe.Groups["attrs"].Value).GetValueOrDefault("src")) is { } delegated && !external.Contains(delegated))
            { external.Add(delegated); entries.Add(new(++position, null, "Embedded Tumblr media", delegated, null, null, true)); }
        external = external.DistinctBy(url => url.AbsoluteUri, StringComparer.Ordinal).ToList();
        if (native.Count == 1 && external.Count == 0) return native[0] with { Id = address.Id, ArchiveIdentity = new("tumblr", address.Id) };
        if (native.Count == 0) return new() { Id = address.Id, Extractor = Info.Id, Title = title, Description = metadata.Description, Uploader = address.Blog, SourcePageUrl = document.Url,
            EmbeddedPages = external, Warnings = external.Count == 0 ? ["This public post exposes no supported native audio/video or player. Other themes, images or account layouts need additional support."] : [] };
        if (entries.Count > request.MaximumPlaylistEntries) throw new NotSupportedException("The Tumblr media inventory exceeds the configured entry limit.");
        return new() { Id = address.Id, PlaylistId = address.Id, ArchiveIdentity = new("tumblr", address.Id), Extractor = "tumblr:post", IsPlaylist = true, Title = title,
            Description = metadata.Description, Uploader = address.Blog, Entries = entries, SourcePageUrl = document.Url };
    }
    private static Uri Resource(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 &&
        (url.Host.EndsWith(".media.tumblr.com", StringComparison.Ordinal) || url.Host is "vtt.tumblr.com" or "vt.tumblr.com") ? url : throw new InvalidDataException("Tumblr returned an unsupported native media origin.");
    private static Uri Image(string value) => Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Host.EndsWith(".media.tumblr.com", StringComparison.Ordinal) ? url :
        throw new InvalidDataException("Tumblr returned an unsupported poster origin.");
    private static bool MediaKey(Uri url, string key)
    {
        var parts = key.Split(':'); return parts.Length == 2 && parts.All(part => Regex.IsMatch(part, @"\A[A-Za-z0-9_-]{1,128}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) &&
            url.AbsolutePath.StartsWith("/" + parts[0] + "/" + parts[1] + "/", StringComparison.Ordinal);
    }
    private static Uri? External(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url)) return null;
        if (url.Host == "safe.txmblr.com" && url.AbsolutePath.StartsWith("/svc/embed/inline/", StringComparison.Ordinal))
        { if (!Uri.TryCreate(Uri.UnescapeDataString(url.AbsolutePath["/svc/embed/inline/".Length..]), UriKind.Absolute, out url)) return null; }
        return url.Scheme is "https" or "http" && url.IsDefaultPort && url.UserInfo.Length == 0 && !(url.Host.EndsWith(".tumblr.com", StringComparison.Ordinal) || url.Host == "tumblr.com") ? url : null;
    }
}
