using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;
using static YoutubeDl.Engine.Providers.PublicProviderApi;

namespace YoutubeDl.Engine.Providers.Bbc;

/// <summary>Selected public BBC news-video pages and their issued media-selector resources.</summary>
public sealed class BbcProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("bbc", "BBC public news video", 204);
    public bool SupportsGeoVerificationProxy => true;
    public bool CanHandle(Uri url) => url.IsAbsoluteUri && url.Host is "www.bbc.com" or "bbc.com";
    internal static string Id(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Host is not ("www.bbc.com" or "bbc.com") || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0) throw new ArgumentException("Use a canonical BBC news video URL.");
        var match = Match(url.AbsolutePath, @"\A/news/videos/(?<id>c[a-z0-9]{10,15})/?\z"); return match.Success ? match.Groups["id"].Value : throw new NotSupportedException("This BBC route is not supported yet.");
    }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        NativeGeoVerification.ValidateProvider(request, this); var id = Id(request.Url); var url = new Uri("https://www.bbc.com/news/videos/" + id); string? selectedVersion = null;
        async Task<PageMetadata> Current(MediaHttp controlled, CancellationToken token)
        {
            using var verification = controlled.CreateVerificationSession(request.GeoVerification?.Proxy);
            var next = PublicPageState.Next(await verification.GetStringAsync(url, 8 * 1024 * 1024, token).ConfigureAwait(false)); var props = Object(Object(next, "props"), "pageProps"); var metadata = Object(props, "metadata"); var video = Object(metadata, "videoMetadata");
            if (Text(metadata, "id") != "urn:bbc:ares::article:" + id || Text(video, "subType") != "clip" || Text(video, "format") != "video" || Text(video, "smpKind") != "programme" || Array(video, "webcastVersions").Length != 0) throw new InvalidDataException("BBC returned another page, media type or live edition.");
            var versions = Rows(video, "versions", 16).Where(version => Array(version, "types").Any(type => type.ValueKind == JsonValueKind.String && type.GetString() == "Original")).ToArray();
            if (versions.Length != 1) throw new NotSupportedException("This BBC page has no unique complete original video edition.");
            var version = versions[0]; var vpid = Text(version, "versionId"); var duration = Number(version, "duration");
            if (vpid is null || !Match(vpid, @"\A[a-z0-9]{8}\z").Success || duration is not (> 0 and <= 604800) || selectedVersion is not null && selectedVersion != vpid) throw new InvalidDataException("BBC changed or omitted the selected playback identity."); selectedVersion ??= vpid;
            var stamp = Long(metadata, "lastPublished"); var timestamp = stamp is > 0 and <= 253402300799999 ? DateTimeOffset.FromUnixTimeMilliseconds(stamp.Value) : (DateTimeOffset?)null;
            var page = new PageMetadata { Id = vpid, Extractor = "bbc", ArchiveIdentity = new("bbc", vpid), Title = Title(metadata, "seoHeadline"), Description = Text(metadata, "description"), DurationSeconds = duration, Uploader = "BBC", UploadTimestamp = timestamp, UploadDate = timestamp is null ? null : DateOnly.FromDateTime(timestamp.Value.UtcDateTime), ThumbnailUrl = Text(Object(metadata, "indexImage"), "originalSrc") is { } image ? Secure(image, "c.files.bbci.co.uk", "ichef.bbci.co.uk").AbsoluteUri : null, AnonymousPlayback = true, PublicServicePlayback = true, SourcePageUrl = url, MetadataFields = new Dictionary<string, object?> { ["bbc_article_id"] = id, ["bbc_programme_id"] = Text(video, "id") } };
            var territories = Object(version, "availableTerritories"); var from = Long(version, "availableFrom");
            if (Drm(video) || Drm(version) || !True(territories, "uk") && !True(territories, "nonUk") || from > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) return page with { DrmRefused = Drm(video) || Drm(version), Warnings = ["BBC has not granted complete public playback for this edition."] };
            if (await Music.MusicCatalog.RecordedAsync(request, page, token).ConfigureAwait(false)) return page;
            var selector = await Read(verification, new("https://open.live.bbc.co.uk/mediaselector/6/select/version/2.0/mediaset/pc/vpid/" + vpid + "/format/json"), token).ConfigureAwait(false);
            if (Present(selector, "result") || Present(selector, "error")) return page with { Warnings = ["BBC media selection denied the current playback session."] };
            var formats = new List<MediaCandidate>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var media in Rows(selector, "media", 64))
            {
                if (Text(media, "kind") != "video") continue;
                if (Text(media, "type") != "video/mp4" || Text(media, "encoding") != "h264" || Drm(media)) throw new NotSupportedException("This BBC media encoding needs additional native support.");
                foreach (var connection in Rows(media, "connection", 32))
                {
                    if (Text(connection, "protocol") != "https") continue;
                    var transfer = Text(connection, "transferFormat"); if (transfer is not ("hls" or "dash")) continue;
                    var supplier = Text(connection, "supplier"); if (supplier is not ("mf_akamai" or "mf_cloudfront")) throw new NotSupportedException("This BBC delivery supplier needs additional native scope support.");
                    var resource = MediaUrl(Text(connection, "href")); var kind = transfer == "hls" ? MediaKind.Hls : MediaKind.Dash;
                    if (PageMediaExtractor.Classify(resource) != kind || !Match(resource.AbsolutePath, "(?:^|[-_/])" + vpid + @"(?:[-_/.]|$)").Success || Drm(connection)) throw new InvalidDataException("BBC returned another video's media or protection contract.");
                    var expires = Date(Text(connection, "authExpires")); if (expires is not null && expires <= DateTimeOffset.UtcNow.AddSeconds(5)) throw new InvalidDataException("BBC returned an expired playback resource.");
                    var formatId = transfer + "-" + supplier; if (!ids.Add(formatId)) throw new InvalidDataException("BBC returned ambiguous delivery format identities.");
                    formats.Add(new() { Id = formatId, Url = resource, Kind = kind, Extension = "mp4", HasAudio = true, HasVideo = true, IsCompleteMedia = true, Source = Info.Id, ResourceExpires = expires, VerificationDemuxer = "mov", VerificationDurationSeconds = duration });
                }
            }
            if (formats.Count == 0) return page with { Warnings = ["BBC supplied no supported complete clear playback resource."] };
            return page with { Media = formats };
        }
        return await http.SessionState(Key(Info.Id, request), () => new ProviderPlaybackSession(http, request, Info.Id, Current, reorderedFormats: false, publicSession: true)).ReadAsync(ct).ConfigureAwait(false);
    }
    internal static bool MediaHost(Uri url) => url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 && url.Host is "vod-hls-ww-live.akamaized.net" or "vod-dash-ww-live.akamaized.net" or "vod-hls-ww.live.cf.md.bbci.co.uk" or "vod-dash-ww.live.cf.md.bbci.co.uk";
    private static Uri MediaUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var url) && MediaHost(url) && url.AbsoluteUri.Length <= 16384 ? url : throw new InvalidDataException("BBC returned an undeclared media origin.");
}
