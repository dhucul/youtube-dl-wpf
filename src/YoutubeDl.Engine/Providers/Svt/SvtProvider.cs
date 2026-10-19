using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;
using static YoutubeDl.Engine.Providers.PublicProviderApi;

namespace YoutubeDl.Engine.Providers.Svt;

/// <summary>Current SVT Play replay pages with first-party alias-bound playback and captions.</summary>
public sealed class SvtProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("svt", "SVT Play public replays and live programmes", 204);
    public bool SupportsGeoVerificationProxy => true;
    public bool CanHandle(Uri url) => url.IsAbsoluteUri && url.Host is "www.svtplay.se" or "svtplay.se";
    internal static string Id(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Host is not ("www.svtplay.se" or "svtplay.se") || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0) throw new ArgumentException("Use a canonical SVT Play video URL.");
        var match = Match(url.AbsolutePath, @"\A/video/(?<id>[A-Za-z0-9]{7,16})(?:/[a-z0-9-]{1,300}){0,3}/?\z"); return match.Success ? match.Groups["id"].Value : throw new NotSupportedException("This SVT Play route is not supported yet.");
    }
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => new("svt:play", Id(request.Url));
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        NativeGeoVerification.ValidateProvider(request, this); var id = Id(request.Url); var url = new UriBuilder(request.Url) { Scheme = "https", Host = "www.svtplay.se", Port = -1 }.Uri; string? apiId = null, versionId = null;
        async Task<PageMetadata> Current(MediaHttp controlled, CancellationToken token)
        {
            using var verification = controlled.CreateVerificationSession(request.GeoVerification?.Proxy);
            var html = await verification.GetStringAsync(url, 8 * 1024 * 1024, token).ConfigureAwait(false); var next = PublicPageState.Next(html); var query = Object(next, "query"); var slugs = Array(query, "slug");
            if (slugs.Length < 2 || slugs[0].GetString() != "video" || slugs[1].GetString() != id) throw new InvalidDataException("SVT returned another video page identity.");
            var details = PublicPageState.Svt(html); var item = Object(details, "item"); var linked = Text(Object(item, "urls"), "svtplay");
            if (Text(item, "svtId") != id || Text(item, "__typename") is not ("Single" or "Episode") || linked is null || Id(new Uri("https://www.svtplay.se" + linked)) != id || Text(Object(Object(details, "analytics"), "json"), "contentId") != id) throw new InvalidDataException("SVT returned another selected item or a collection.");
            var root = await Read(verification, new("https://api.svt.se/video/" + id), token).ConfigureAwait(false); var currentId = Text(root, "svtId"); var currentVersion = Text(root, "programVersionId"); var duration = Number(root, "contentDuration"); var live = True(root, "live");
            if (currentId is null || !Match(currentId, @"\A[A-Za-z0-9]{7,16}\z").Success || currentVersion is null || !Match(currentVersion, @"\A[A-Za-z0-9-]{1,64}\z").Success || apiId is not null && apiId != currentId || versionId is not null && versionId != currentVersion || duration is not (> 0 and <= 604800) || !live && Number(item, "duration") is { } publishedDuration && Math.Abs(publishedDuration - duration.Value) > 1.1) throw new InvalidDataException("SVT changed its published replay alias, version or duration.");
            apiId ??= currentId; versionId ??= currentVersion; var rights = Object(root, "rights"); var from = Date(Text(rights, "validFrom")); var to = Date(Text(rights, "validTo"));
            var page = new PageMetadata { Id = id, Extractor = "svt:play", ArchiveIdentity = new("svt:play", id), Title = Title(item, "name"), Description = Text(details, "description"), DurationSeconds = duration, IsLive = live, Uploader = "SVT", SourcePageUrl = url, AnonymousPlayback = true, PublicServicePlayback = true, UploadTimestamp = from, UploadDate = from is null ? null : DateOnly.FromDateTime(from.Value.UtcDateTime), MetadataFields = new Dictionary<string, object?> { ["svt_playback_id"] = currentId, ["svt_program_version"] = currentVersion } };
            if (rights.ValueKind != JsonValueKind.Object || from is null || to is null || from > DateTimeOffset.UtcNow || to <= DateTimeOffset.UtcNow || True(rights, "geoBlockedSweden") || True(rights, "drmCopyProtection") || Drm(root)) return page with { DrmRefused = True(rights, "drmCopyProtection") || Drm(root), Warnings = ["SVT has not granted supported complete clear playback in this session."] };
            if (await Music.MusicCatalog.RecordedAsync(request, page, token).ConfigureAwait(false)) return page;
            var formats = new List<MediaCandidate>(); var formatIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reference in Rows(root, "videoReferences", 64))
            {
                var format = Text(reference, "format"); if (format is not ("hls" or "dash-full")) continue;
                if (Drm(reference)) throw new NotSupportedException("SVT declared protected playback in the selected media.");
                var resource = MediaUrl(Text(reference, "url")); var kind = format == "hls" ? MediaKind.Hls : MediaKind.Dash;
                if (PageMediaExtractor.Classify(resource) != kind || !formatIds.Add(format)) throw new InvalidDataException("SVT returned ambiguous or mismatched manifest formats.");
                formats.Add(new() { Id = format, Url = resource, Kind = kind, Extension = "mp4", HasAudio = true, HasVideo = true, IsCompleteMedia = true, Source = Info.Id, VerificationDemuxer = live ? null : "mov", VerificationDurationSeconds = live ? null : duration });
            }
            var captions = new List<SubtitleTrack>(); var captionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reference in Rows(root, "subtitleReferences", 64))
            {
                if (Text(reference, "format") != "webvtt" || Text(reference, "type") is not ("subtitle" or "caption-sdh")) continue;
                var language = Text(reference, "language"); if (language is null || !Match(language, @"\A[a-z]{2,3}(?:-[A-Za-z0-9]{2,8})*\z").Success) throw new InvalidDataException("SVT omitted a stable caption language.");
                var identity = language + ":" + Text(reference, "type"); if (!captionIds.Add(identity)) throw new InvalidDataException("SVT returned ambiguous caption identities.");
                var resource = MediaUrl(Text(reference, "url")); if (!resource.AbsolutePath.EndsWith(".vtt", StringComparison.Ordinal)) throw new InvalidDataException("SVT changed the caption encoding.");
                captions.Add(new() { Url = resource, Language = language + (Text(reference, "type") == "caption-sdh" ? "-x-sdh" : ""), Extension = "vtt", ResourceIdentity = identity });
            }
            if (formats.Count == 0) return page with { Warnings = ["SVT supplied no supported full playback formats."] };
            return page with { Media = formats, Subtitles = captions };
        }
        return await http.SessionState(Key(Info.Id, request), () => new ProviderPlaybackSession(http, request, Info.Id, Current, reorderedFormats: false, publicSession: true)).ReadAsync(ct).ConfigureAwait(false);
    }
    internal static bool MediaHost(Uri url) => url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 && url.Host is "svt-vod-1t.akamaized.net" or "svt-live-event.akamaized.net";
    private static Uri MediaUrl(string? raw) => Uri.TryCreate(raw, UriKind.Absolute, out var url) && MediaHost(url) && url.AbsoluteUri.Length <= 16384 ? url : throw new InvalidDataException("SVT returned an undeclared media origin.");
}
