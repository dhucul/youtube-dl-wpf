using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;
using static YoutubeDl.Engine.Providers.PublicProviderApi;

namespace YoutubeDl.Engine.Providers.Ard;

internal sealed record ArdAddress(string Id, string Crid, bool Collection)
{
    internal Uri Page => new("https://www.ardmediathek.de/" + (Collection ? "sendung/" : "video/") + Id);
    internal static ArdAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Host is not ("www.ardmediathek.de" or "ardmediathek.de") || url.Scheme is not ("https" or "http") || !url.IsDefaultPort || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0) throw new ArgumentException("Use a canonical ARD Mediathek video or programme URL.");
        var match = Match(url.AbsolutePath, @"\A/(?<kind>video|sendung|serie)/(?:[a-zA-Z0-9-]+/){0,4}(?<id>[A-Za-z0-9_-]{8,1024})/?\z"); if (!match.Success) throw new NotSupportedException("This ARD route is not supported yet.");
        var id = match.Groups["id"].Value; string crid;
        try { crid = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(id.Replace('-', '+').Replace('_', '/') + new string('=', (4 - id.Length % 4) % 4))); }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException) { throw new ArgumentException("ARD requires its declared encoded content identity."); }
        if (!Uri.TryCreate(crid, UriKind.Absolute, out var identity) || identity.Scheme != "crid" || identity.Host.Length == 0 || identity.UserInfo.Length > 0 || identity.Query.Length > 0 || identity.Fragment.Length > 0) throw new ArgumentException("ARD returned an invalid content reference.");
        return new(id, crid, match.Groups["kind"].Value != "video");
    }
}
public sealed class ArdProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("ard", "ARD Mediathek programmes and complete published collections", 205);
    public bool SupportsGeoVerificationProxy => true;
    public bool CanHandle(Uri url) => url.IsAbsoluteUri && url.Host is "www.ardmediathek.de" or "ardmediathek.de";
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) { var a = ArdAddress.Parse(request.Url); return a.Collection ? null : new("ARDMediathek", a.Id); }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        NativeGeoVerification.ValidateProvider(request, this); var address = ArdAddress.Parse(request.Url);
        if (address.Collection) return await Collection(address, request, http, ct).ConfigureAwait(false);
        string? selectedPublication = null; double? selectedDuration = null;
        async Task<PageMetadata> Current(MediaHttp controlled, CancellationToken token)
        {
            using var verification = controlled.CreateVerificationSession(request.GeoVerification?.Proxy); var root = await Read(verification, Api("pages/ard/item/" + address.Id + "?embedded=false&mcV6=true"), token).ConfigureAwait(false);
            var self = Secure(Text(Object(Object(root, "links"), "self"), "href"), "api.ardmediathek.de");
            var widgets = Rows(root, "widgets", 32).Where(row => Text(row, "type") == "player_ondemand").ToArray();
            if (self.AbsolutePath != "/page-gateway/pages/ard/item/" + address.Id || Text(root, "coreAssetType") is not ("EPISODE" or "CLIP") || Text(Object(Object(root, "tracking"), "atiCustomVars"), "metadataId") != address.Crid || widgets.Length != 1) throw new InvalidDataException("ARD returned another selected programme or ambiguous player.");
            var widget = widgets[0]; var media = Object(Object(widget, "mediaCollection"), "embedded"); var publication = Text(media, "id"); var meta = Object(media, "meta"); var duration = Number(meta, "durationSeconds");
            if (publication is null || !Match(publication, @"\Aurn:ard:publication:[a-f0-9]{16}\z").Success || selectedPublication is not null && selectedPublication != publication || duration is not (> 0 and <= 604800) || selectedDuration is not null && selectedDuration != duration) throw new InvalidDataException("ARD changed or omitted the complete publication identity or duration."); selectedPublication ??= publication; selectedDuration ??= duration;
            var stamp = Date(Text(meta, "broadcastedOnDateTime")); var page = new PageMetadata { Id = address.Id, Extractor = "ARDMediathek", ArchiveIdentity = new("ARDMediathek", address.Id), Title = Title(meta), Description = Text(meta, "synopsis"), DurationSeconds = duration, Uploader = Text(meta, "clipSourceName"), SourcePageUrl = address.Page, AnonymousPlayback = true, PublicServicePlayback = true, UploadTimestamp = stamp, UploadDate = stamp is null ? null : DateOnly.FromDateTime(stamp.Value.UtcDateTime), MetadataFields = new Dictionary<string, object?> { ["ard_publication"] = publication, ["ard_crid"] = address.Crid } };
            if (True(widget, "blockedByLoginOnly") || True(widget, "blockedByFsk") || True(widget, "geoblocked") || True(media, "isGeoBlocked") || Drm(media) || Date(Text(meta, "availableToDateTime")) is { } end && end <= DateTimeOffset.UtcNow) return page with { DrmRefused = Drm(media), Warnings = ["ARD has not granted complete clear public playback for this programme."] };
            if (await Music.MusicCatalog.RecordedAsync(request, page, token).ConfigureAwait(false)) return page;
            var groups = Rows(media, "streams", 16).Where(row => Text(row, "kind") == "main").ToArray(); if (groups.Length != 1) throw new NotSupportedException("ARD has no unique supported main programme edition.");
            var formats = new List<MediaCandidate>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in Rows(groups[0], "media", 64))
            {
                if (True(row, "isHighDynamicRange") || True(row, "isProtocolRelative") || True(row, "isHbbtv")) continue;
                if (Drm(row)) throw new NotSupportedException("ARD declared protected playback in the selected media.");
                var url = Resource(Text(row, "url")); var kind = PageMediaExtractor.Classify(url, Text(row, "mimeType")); if (kind is not (MediaKind.Progressive or MediaKind.Hls)) continue;
                if (Text(row, "videoCodec") != "H.264") throw new NotSupportedException("This ARD encoding needs additional native support.");
                var audios = Rows(row, "audios", 16); if (audios.Length != 1 || Text(audios[0], "kind") != "standard") throw new NotSupportedException("This ARD audio-role selection needs additional support.");
                var width = Integer(row, "maxHResolutionPx"); var height = Integer(row, "maxVResolutionPx"); if (width is not (> 0 and <= 16384) || height is not (> 0 and <= 16384)) throw new InvalidDataException("ARD omitted its declared video dimensions.");
                var format = kind == MediaKind.Hls ? "hls-main" : "mp4-" + width + "x" + height; if (!ids.Add(format)) throw new InvalidDataException("ARD returned ambiguous programme format identities.");
                formats.Add(new() { Id = format, Url = url, Kind = kind, Extension = "mp4", HasVideo = true, HasAudio = true, IsCompleteMedia = true, VideoCodec = "h264", Height = kind == MediaKind.Progressive ? height : null, Width = kind == MediaKind.Progressive ? width : null, AudioLanguage = Text(audios[0], "languageCode"), Source = Info.Id, VerificationDemuxer = "mov", VerificationDurationSeconds = duration });
            }
            var captions = new List<SubtitleTrack>(); var captionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in Rows(media, "subtitles", 64))
            {
                if (Text(row, "kind") != "normal") continue; var language = Text(row, "languageCode"); if (language is null || !Match(language, @"\A[a-z]{2,3}\z").Success) throw new InvalidDataException("ARD omitted its caption language.");
                foreach (var source in Rows(row, "sources", 16).Where(item => Text(item, "kind") == "webvtt"))
                {
                    var url = Secure(Text(source, "url"), "api.ardmediathek.de"); if (!url.AbsolutePath.StartsWith("/player-service/subtitle/webvtt/urn:ard:subtitle:", StringComparison.Ordinal) || !captionIds.Add(language)) throw new InvalidDataException("ARD changed or duplicated its timed caption identity.");
                    captions.Add(new() { Url = url, Language = language, Extension = "vtt", ResourceIdentity = language + ":normal" });
                }
            }
            return page with { Media = formats, Subtitles = captions };
        }
        return await http.SessionState(Key(Info.Id, request), () => new ProviderPlaybackSession(http, request, Info.Id, Current, reorderedFormats: false, publicSession: true)).ReadAsync(ct).ConfigureAwait(false);
    }
    private static async Task<PageMetadata> Collection(ArdAddress address, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        using var verification = http.CreateVerificationSession(request.GeoVerification?.Proxy); var entries = new List<NativePlaylistEntry>(); var ids = new HashSet<string>(StringComparer.Ordinal); int? total = null; string? title = null;
        for (var page = 0; ; page++)
        {
            if (page >= 500) throw new InvalidDataException("ARD exceeded its bounded complete collection inventory.");
            var root = await Read(verification, Api("widgets/ard/asset/" + address.Id + "?pageNumber=" + page + "&pageSize=24"), ct).ConfigureAwait(false); var paging = Object(root, "pagination"); var count = Integer(paging, "totalElements");
            if (Text(root, "id") != address.Id || Text(root, "compilationType") != "itemsOfShow" || count is null or < 0 || total is not null && total != count || Integer(paging, "pageNumber") != page || Integer(paging, "pageSize") != 24) throw new InvalidDataException("ARD changed its collection identity or complete inventory bounds."); total = count; title ??= Title(root); Limit(count.Value, request);
            var rows = Rows(root, "teasers", 24); if (rows.Length != Math.Min(24, count.Value - entries.Count)) throw new InvalidDataException("ARD returned a partial collection page.");
            foreach (var row in rows)
            {
                var id = Text(row, "id"); var child = id is null ? throw new InvalidDataException("ARD omitted a collection member identity.") : ArdAddress.Parse(new("https://www.ardmediathek.de/video/" + id)); var show = Object(row, "show"); var target = Object(Object(row, "links"), "target");
                if (Text(show, "id") != address.Crid || Text(target, "id") != id || !ids.Add(id!)) throw new InvalidDataException("ARD changed or repeated programme membership.");
                var duration = Number(row, "duration"); var available = Text(row, "coreAssetType") is "EPISODE" or "CLIP" && duration is > 0 && !(Date(Text(row, "availableTo")) is { } end && end <= DateTimeOffset.UtcNow);
                entries.Add(new(entries.Count + 1, id, Title(row, "longTitle"), child.Page, duration, null, available, ExpectedArchiveIdentity: new("ARDMediathek", id!), CollectionMemberId: id));
            }
            if (entries.Count == total) break;
        }
        return Inventory("ARDMediathekCollection", address.Id, title!, entries, request);
    }
    private static Uri Api(string path) => new("https://api.ardmediathek.de/page-gateway/" + path);
    internal static bool MediaHost(Uri u) => u.Scheme == "https" && u.IsDefaultPort && u.UserInfo.Length == 0 && u.Fragment.Length == 0 && (u.Host is "tagesschau-progressive.ard-mcdn.de" or "adaptive.tagesschau.de" || u.Host == "api.ardmediathek.de" && u.AbsolutePath.StartsWith("/player-service/subtitle/", StringComparison.Ordinal));
    private static Uri Resource(string? raw) => Uri.TryCreate(raw, UriKind.Absolute, out var url) && MediaHost(url) ? url : throw new InvalidDataException("ARD returned an undeclared media origin.");
}
