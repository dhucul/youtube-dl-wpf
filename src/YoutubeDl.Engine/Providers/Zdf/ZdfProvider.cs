using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;
using static YoutubeDl.Engine.Providers.PublicProviderApi;

namespace YoutubeDl.Engine.Providers.Zdf;

public sealed class ZdfProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("zdf", "ZDFheute public video", 205);
    public bool SupportsGeoVerificationProxy => true;
    public bool CanHandle(Uri url) => url.IsAbsoluteUri && url.Host is "www.zdfheute.de" or "zdfheute.de";
    internal static string Id(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Host is not ("www.zdfheute.de" or "zdfheute.de") || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0) throw new ArgumentException("Use a canonical ZDFheute video page.");
        var match = Match(url.AbsolutePath, @"\A/video/(?:[a-z0-9-]+/){0,4}(?<id>[a-z0-9-]{1,240})\.html\z"); return match.Success ? match.Groups["id"].Value : throw new NotSupportedException("This ZDF route is not supported yet.");
    }
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => new("zdf", Id(request.Url));
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        NativeGeoVerification.ValidateProvider(request, this); var id = Id(request.Url); var pageUrl = new UriBuilder(request.Url) { Scheme = "https", Host = "www.zdfheute.de", Port = -1 }.Uri; string? deliveryPath = null; double? selectedDuration = null;
        async Task<PageMetadata> Current(MediaHttp controlled, CancellationToken token)
        {
            using var verification = controlled.CreateVerificationSession(request.GeoVerification?.Proxy); var html = await verification.GetStringAsync(pageUrl, 8 * 1024 * 1024, token).ConfigureAwait(false);
            var scripts = Regex.Matches(html, @"<script\b(?=[^>]{0,8192}\btype\s*=\s*['""]application/ld\+json['""])[^>]*>(?<json>[\s\S]*?)</script\s*>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)); var videos = new List<JsonElement>();
            foreach (Match script in scripts) { var root = PublicPageState.Parse(script.Groups["json"].Value); if (Text(root, "@type") == "VideoObject") videos.Add(root); }
            if (videos.Count != 1) throw new InvalidDataException("ZDF returned an ambiguous selected video description."); var video = videos[0]; var issuer = Secure(Text(video, "contentUrl"), "www.zdfheute.de");
            if (issuer.AbsolutePath != "/api/video/" + id + ".m3u8" || issuer.Query.Length > 0 || Text(Object(video, "author"), "name") != "ZDFheute") throw new InvalidDataException("ZDF returned another video's playback service.");
            var duration = BroadcastValues.Duration(Text(video, "duration")); if (selectedDuration is not null && selectedDuration != duration) throw new InvalidDataException("ZDF changed the complete selected duration."); selectedDuration ??= duration;
            var stamp = Date(Text(video, "uploadDate")); var image = Array(video, "thumbnailUrl").FirstOrDefault(); var page = new PageMetadata { Id = id, Extractor = "zdf", ArchiveIdentity = new("zdf", id), Title = Title(video, "name"), Description = Text(video, "description"), DurationSeconds = duration, UploadTimestamp = stamp, UploadDate = stamp is null ? null : DateOnly.FromDateTime(stamp.Value.UtcDateTime), ThumbnailUrl = image.ValueKind == JsonValueKind.String ? Secure(image.GetString(), "www.zdfheute.de").AbsoluteUri : null, Uploader = "ZDFheute", SourcePageUrl = pageUrl, AnonymousPlayback = true, PublicServicePlayback = true };
            if (Drm(video) || True(video, "requiresSubscription") || Date(Text(video, "expires")) is { } end && end <= DateTimeOffset.UtcNow) return page with { DrmRefused = Drm(video), Warnings = ["ZDF has not granted complete clear public playback."] };
            if (await Music.MusicCatalog.RecordedAsync(request, page, token).ConfigureAwait(false)) return page;
            using var message = new HttpRequestMessage(HttpMethod.Get, issuer); using var response = await verification.SendAsync(message, token).ConfigureAwait(false); Uri media;
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var next = response.Headers.Location; media = next is null ? throw new InvalidDataException("ZDF omitted its issued delivery address.") : next.IsAbsoluteUri ? next : new(issuer, next);
                if (!MediaHost(media) || media.Host == "www.zdfheute.de") throw new InvalidDataException("ZDF redirected outside its observed delivery scope.");
            }
            else { response.EnsureSuccessStatusCode(); media = issuer; }
            if (!media.AbsolutePath.EndsWith(".m3u8", StringComparison.Ordinal) || deliveryPath is not null && deliveryPath != media.GetLeftPart(UriPartial.Path)) throw new InvalidDataException("ZDF changed the selected delivery asset."); deliveryPath ??= media.GetLeftPart(UriPartial.Path);
            return page with { Media = [new() { Id = "hls", Url = media, Kind = MediaKind.Hls, Extension = "mp4", HasAudio = true, HasVideo = true, IsCompleteMedia = true, Source = Info.Id, VerificationDemuxer = "mov", VerificationDurationSeconds = duration }] };
        }
        return await http.SessionState(Key(Info.Id, request), () => new ProviderPlaybackSession(http, request, Info.Id, Current, reorderedFormats: false, publicSession: true)).ReadAsync(ct).ConfigureAwait(false);
    }
    internal static bool MediaHost(Uri u) => u.Scheme == "https" && u.IsDefaultPort && u.UserInfo.Length == 0 && u.Fragment.Length == 0 && (u.Host == "zdfvod.akamaized.net" || u.Host == "www.zdfheute.de" && u.AbsolutePath.StartsWith("/api/video/", StringComparison.Ordinal));
}
