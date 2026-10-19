using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;
using static YoutubeDl.Engine.Providers.PublicProviderApi;

namespace YoutubeDl.Engine.Providers.Nrk;

public sealed class NrkProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("nrk", "NRK public on-demand programmes", 205);
    public bool SupportsGeoVerificationProxy => true;
    public bool CanHandle(Uri url) => url.IsAbsoluteUri && (url.Scheme == "nrk" || url.Host == "tv.nrk.no");
    internal static string Id(Uri url)
    {
        if (url.IsAbsoluteUri && url.Scheme == "nrk")
        {
            var opaque = Match(url.AbsoluteUri, @"\Anrk:(?<id>[A-Z]{4}[0-9]{8})\z"); return opaque.Success ? opaque.Groups["id"].Value : throw new ArgumentException("Use the declared NRK programme identity.");
        }
        if (!url.IsAbsoluteUri || url.Host != "tv.nrk.no" || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0) throw new ArgumentException("Use a canonical NRK programme URL.");
        var match = Match(url.AbsolutePath, @"\A/(?:program/|serie/[a-z0-9-]+/sesong/[a-zA-Z0-9-]+/episode/)(?<id>[A-Z]{4}[0-9]{8})/?\z"); return match.Success ? match.Groups["id"].Value : throw new NotSupportedException("This NRK route is not supported yet.");
    }
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => new("NRK", Id(request.Url));
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        NativeGeoVerification.ValidateProvider(request, this); var id = Id(request.Url); var canonical = new Uri("https://tv.nrk.no/program/" + id); var playbackRequest = request with { Url = canonical }; double? selectedDuration = null; string? assetPath = null;
        async Task<PageMetadata> Current(MediaHttp controlled, CancellationToken token)
        {
            using var verification = controlled.CreateVerificationSession(request.GeoVerification?.Proxy); var root = await Read(verification, new("https://psapi.nrk.no/playback/metadata/program/" + id), token).ConfigureAwait(false); var preplay = Object(root, "preplay"); var availability = Object(root, "availability"); var rights = Object(availability, "onDemand");
            if (Text(root, "id") != id || Text(root, "streamingMode") != "onDemand" || Text(root, "sourceMedium") != "video") throw new InvalidDataException("NRK returned another programme or unsupported streaming mode.");
            var duration = BroadcastValues.Duration(Text(root, "duration")); if (selectedDuration is not null && selectedDuration != duration) throw new InvalidDataException("NRK changed the complete programme duration."); selectedDuration ??= duration;
            var from = Date(Text(rights, "from")); var to = Date(Text(rights, "to")); var titles = Object(preplay, "titles"); var page = new PageMetadata { Id = id, Extractor = "NRK", ArchiveIdentity = new("NRK", id), Title = Title(titles) + (Text(titles, "subtitle") is { Length: > 0 } subtitle ? " - " + subtitle : ""), Description = Text(preplay, "description"), DurationSeconds = duration, Uploader = "NRK", SourcePageUrl = canonical, AnonymousPlayback = true, PublicServicePlayback = true, UploadTimestamp = from, UploadDate = from is null ? null : DateOnly.FromDateTime(from.Value.UtcDateTime) };
            if (Text(root, "playability") != "playable" || !True(rights, "hasRightsNow") || True(availability, "isGeoBlocked") || from is null || to is null || from > DateTimeOffset.UtcNow || to <= DateTimeOffset.UtcNow || Drm(root)) return page with { DrmRefused = Drm(root), Warnings = ["NRK has not granted complete clear public playback in this session."] };
            if (await Music.MusicCatalog.RecordedAsync(request, page, token).ConfigureAwait(false)) return page;
            if (Text(Object(root, "playable"), "resolve") != "/playback/manifest/program/" + id) throw new InvalidDataException("NRK returned another programme's playback service.");
            var manifest = await Read(verification, new("https://psapi.nrk.no/playback/manifest/program/" + id), token).ConfigureAwait(false); var playable = Object(manifest, "playable"); var manifestRights = Object(Object(manifest, "availability"), "onDemand");
            if (Text(manifest, "id") != id || Text(manifest, "playability") != "playable" || Text(manifest, "streamingMode") != "onDemand" || !True(manifestRights, "hasRightsNow") || True(Object(manifest, "availability"), "isGeoBlocked") || Math.Abs(BroadcastValues.Duration(Text(playable, "duration")) - duration) > 1.1) throw new InvalidDataException("NRK changed the selected playback identity, rights or full duration.");
            var assets = Rows(playable, "assets", 16); if (assets.Length != 1) throw new NotSupportedException("This NRK programme has no unique supported main asset."); var asset = assets[0]; var media = Resource(Text(asset, "url"));
            if (Text(asset, "format") != "HLS" || Text(asset, "encryptionScheme") != "none" || !asset.TryGetProperty("encrypted", out var encrypted) || encrypted.ValueKind != JsonValueKind.False || Drm(asset)) throw new NotSupportedException("This NRK asset is encrypted or uses an unsupported delivery contract.");
            if (!media.AbsolutePath.Contains("/" + id.ToLowerInvariant() + "/", StringComparison.Ordinal) || !media.AbsolutePath.EndsWith(".m3u8", StringComparison.Ordinal) || assetPath is not null && assetPath != media.GetLeftPart(UriPartial.Path)) throw new InvalidDataException("NRK changed the selected delivery asset."); assetPath ??= media.GetLeftPart(UriPartial.Path);
            var captions = new List<SubtitleTrack>(); var captionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var caption in Rows(playable, "subtitles", 32))
            {
                var language = Text(caption, "language"); if (language is null || !Match(language, @"\A[a-z]{2,3}\z").Success || !captionIds.Add(language)) throw new InvalidDataException("NRK returned ambiguous caption languages.");
                var url = Secure(Text(caption, "webVtt"), "undertekst.nrk.no"); if (!url.AbsolutePath.Contains("/" + id + "/", StringComparison.Ordinal) || !url.AbsolutePath.EndsWith(".vtt", StringComparison.Ordinal)) throw new InvalidDataException("NRK returned another programme's captions.");
                captions.Add(new() { Url = url, Language = language, Extension = "vtt", ResourceIdentity = language + ":" + Text(caption, "type") });
            }
            var points = Rows(preplay, "indexPoints", 1000); var chapters = new List<ChapterMarker>(); double previous = -1;
            for (var i = 0; i < points.Length; i++)
            {
                var start = BroadcastValues.Duration(Text(points[i], "startPoint"), true); var end = i + 1 < points.Length ? BroadcastValues.Duration(Text(points[i + 1], "startPoint"), true) : duration;
                if (start <= previous || end <= start || end > duration) throw new InvalidDataException("NRK returned invalid chapter ordering."); previous = start; chapters.Add(new() { StartSeconds = start, EndSeconds = end, Title = Title(points[i]) });
            }
            return page with { Media = [new() { Id = "hls", Url = media, Kind = MediaKind.Hls, Extension = "mp4", HasAudio = true, HasVideo = true, IsCompleteMedia = true, Source = Info.Id, NormalizeHlsPesCounterResets = true, VerificationDemuxer = "mov", VerificationDurationSeconds = duration }], Subtitles = captions, Chapters = chapters };
        }
        return await http.SessionState(Key(Info.Id, request), () => new ProviderPlaybackSession(http, playbackRequest, Info.Id, Current, reorderedFormats: false, publicSession: true)).ReadAsync(ct).ConfigureAwait(false);
    }
    internal static bool MediaHost(Uri u) => u.Scheme == "https" && u.IsDefaultPort && u.UserInfo.Length == 0 && u.Fragment.Length == 0 && u.Host is "nrk-od-world-36.akamaized.net" or "undertekst.nrk.no";
    private static Uri Resource(string? raw) => Uri.TryCreate(raw, UriKind.Absolute, out var url) && MediaHost(url) && url.Host != "undertekst.nrk.no" ? url : throw new InvalidDataException("NRK returned an undeclared media origin.");
}
