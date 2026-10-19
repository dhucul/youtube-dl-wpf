using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;
using static YoutubeDl.Engine.Providers.PublicProviderApi;

namespace YoutubeDl.Engine.Providers.ArdSounds;

internal sealed record ArdSoundsAddress(string Id, bool Collection, string? Slug = null)
{
    internal Uri Page => new("https://www.ardsounds.de/" + (Collection ? "sendung/" + Slug + "/" : "episode/") + Id + "/");
    internal static ArdSoundsAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Host is not ("www.ardsounds.de" or "ardsounds.de" or "www.ardaudiothek.de" or "ardaudiothek.de") || url.Scheme is not ("https" or "http") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0) throw new ArgumentException("Use a canonical ARD Sounds episode or show URL.");
        var episode = Match(url.AbsolutePath, @"\A/episode/(?<id>urn:ard:(?:episode|section|extra):[a-f0-9]{16})/?\z");
        if (episode.Success) return new(episode.Groups["id"].Value, false);
        var show = Match(url.AbsolutePath, @"\A/sendung/(?<slug>[a-z0-9-]{1,256})/(?<id>urn:ard:show:[a-f0-9]{16})/?\z");
        return show.Success ? new(show.Groups["id"].Value, true, show.Groups["slug"].Value) : throw new NotSupportedException("This ARD Sounds route needs additional native support.");
    }
}

public sealed class ArdSoundsProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("ardsounds", "ARD Sounds published audio and complete flat shows", 205);
    public bool SupportsGeoVerificationProxy => true;
    public bool CanHandle(Uri url) => url.IsAbsoluteUri && url.Host is "www.ardsounds.de" or "ardsounds.de" or "www.ardaudiothek.de" or "ardaudiothek.de";
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) { var a = ArdSoundsAddress.Parse(request.Url); return a.Collection ? null : new("ARDAudiothek", a.Id); }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        NativeGeoVerification.ValidateProvider(request, this); var address = ArdSoundsAddress.Parse(request.Url);
        if (address.Collection) return await Collection(address, request, http, ct).ConfigureAwait(false);
        string? publication = null, showId = null; double? duration = null;
        async Task<PageMetadata> Current(MediaHttp controlled, CancellationToken token)
        {
            using var verification = controlled.CreateVerificationSession(request.GeoVerification?.Proxy);
            var state = PublicPageState.Next(await verification.GetStringAsync(address.Page, 8 * 1024 * 1024, token).ConfigureAwait(false));
            if (Text(state, "page") != "/episode/[...slug]") throw new InvalidDataException("ARD Sounds returned another page type.");
            var item = Object(Object(Object(Object(Object(state, "props"), "pageProps"), "initialData"), "data"), "item");
            ValidateItem(item, address); var selected = Text(item, "coreId"); var seconds = Number(item, "duration"); var parent = Text(Object(item, "programSet"), "coreId");
            if (selected is null || !Match(selected, @"\Aurn:ard:publication:[a-f0-9]{16}\z").Success || seconds is not (> 0 and <= 604800) || parent is null || !Match(parent, @"\Aurn:ard:show:[a-f0-9]{16}\z").Success || publication is not null && selected != publication || duration is not null && seconds != duration || showId is not null && parent != showId) throw new InvalidDataException("ARD Sounds changed its selected publication, duration or programme.");
            publication ??= selected; duration ??= seconds; showId ??= parent;
            var stamp = Date(Text(item, "publishDate")); var page = new PageMetadata { Id = address.Id, Extractor = "ARDAudiothek", ArchiveIdentity = new("ARDAudiothek", address.Id), Title = Title(item), Description = Text(item, "description"), DurationSeconds = seconds, Uploader = Text(Object(Object(item, "programSet"), "publicationService"), "title"), UploadTimestamp = stamp, UploadDate = stamp is null ? null : DateOnly.FromDateTime(stamp.Value.UtcDateTime), AnonymousPlayback = true, PublicServicePlayback = true, SourcePageUrl = address.Page, MetadataFields = new Dictionary<string, object?> { ["ard_publication"] = selected, ["ard_show"] = parent } };
            if (!True(item, "isPublished") || Text(item, "status") is not ("PUBLISHED" or "SCHEDULED") || stamp is null || stamp > DateTimeOffset.UtcNow || Drm(item) || True(item, "geoblocked") || True(item, "requiresSubscription")) return page with { DrmRefused = Drm(item), Warnings = ["ARD Sounds has not granted current complete public audio for this episode."] };
            if (await Music.MusicCatalog.RecordedAsync(request, page, token).ConfigureAwait(false)) return page;
            var audios = Rows(item, "audios", 32); var availability = Rows(item, "audioList", 32); if (availability.Length != audios.Length) throw new InvalidDataException("ARD Sounds returned ambiguous audio availability.");
            var formats = new List<MediaCandidate>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < audios.Length; index++)
            {
                var row = audios[index]; if (Date(Text(availability[index], "availableTo")) is { } end && end <= DateTimeOffset.UtcNow) continue;
                if (Drm(row)) throw new NotSupportedException("ARD Sounds declared protected audio.");
                if (!True(row, "allowDownload")) continue;
                var raw = Text(row, "downloadUrl"); if (raw is null || raw != Text(row, "url")) throw new InvalidDataException("ARD Sounds changed its declared downloadable rendition.");
                var url = Resource(raw); var mime = Text(row, "mimeType"); var extension = mime == "audio/mp3" && url.AbsolutePath.EndsWith(".mp3", StringComparison.Ordinal) ? "mp3" : mime == "audio/aac" && url.AbsolutePath.EndsWith(".mp4", StringComparison.Ordinal) ? "m4a" : throw new NotSupportedException("This ARD Sounds audio encoding needs additional native support.");
                var id = extension + "-" + Download.AdaptiveFragmentCheckpoint.Hash(url.GetLeftPart(UriPartial.Path))[..16]; if (!ids.Add(id)) throw new InvalidDataException("ARD Sounds repeated a downloadable rendition.");
                formats.Add(new() { Id = id, Url = url, Kind = MediaKind.Progressive, MimeType = mime, Extension = extension, HasAudio = true, HasVideo = false, AudioCodec = extension == "mp3" ? "mp3" : "aac", IsCompleteMedia = true, VerificationDemuxer = extension == "mp3" ? "mp3" : "mov", VerificationDurationSeconds = seconds, Source = Info.Id });
            }
            return formats.Count == 0 ? page with { Warnings = ["ARD Sounds has no supported current downloadable audio rendition."] } : page with { Media = formats };
        }
        return await http.SessionState(Key(Info.Id, request), () => new ProviderPlaybackSession(http, request with { Url = address.Page }, Info.Id, Current, reorderedFormats: false, publicSession: true)).ReadAsync(ct).ConfigureAwait(false);
    }
    private static void ValidateItem(JsonElement item, ArdSoundsAddress address)
    {
        _ = Numeric(item); var type = address.Id.Split(':')[2].ToUpperInvariant();
        if (Text(item, "assetId") != address.Id || Text(item, "coreType") != type || Text(item, "path") != "/episode/" + address.Id + "/") throw new InvalidDataException("ARD Sounds returned another selected episode.");
    }
    private static async Task<PageMetadata> Collection(ArdSoundsAddress address, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        using var verification = http.CreateVerificationSession(request.GeoVerification?.Proxy); var entries = new List<NativePlaylistEntry>(); var ids = new HashSet<string>(StringComparer.Ordinal); var cursors = new HashSet<string>(StringComparer.Ordinal); int? total = null; string? title = null, numericId = null;
        for (var offset = 0; ; offset += 12)
        {
            if (offset >= 10000) throw new InvalidDataException("ARD Sounds exceeded its bounded full show inventory.");
            var query = "{result:programSet(id:\"" + address.Id + "\"){id coreId title path numberOfElements items(offset:" + offset + " first:12 filter:{isPublished:{equalTo:true},itemType:{notEqualTo:EVENT_LIVESTREAM}}){pageInfo{hasNextPage endCursor}nodes{id coreType coreId assetId title isPublished publishDate duration path programSet{id coreId title path}}}}}";
            var root = await Read(verification, new("https://api.ardaudiothek.de/graphql?query=" + Uri.EscapeDataString(query)), ct).ConfigureAwait(false); if (Present(root, "errors")) throw new InvalidDataException("ARD Sounds did not grant a complete show query.");
            var show = Object(Object(root, "data"), "result"); var count = Integer(show, "numberOfElements"); var parent = Numeric(show);
            if (Text(show, "coreId") != address.Id || Text(show, "path") != address.Page.AbsolutePath || count is null or < 0 || total is not null && count != total || numericId is not null && parent != numericId) throw new InvalidDataException("ARD Sounds changed its show identity or inventory count.");
            total = count; numericId ??= parent; title ??= Title(show); Limit(count.Value, request);
            var items = Object(show, "items"); var info = Object(items, "pageInfo"); var rows = Rows(items, "nodes", 12);
            if (rows.Length != Math.Min(12, total.Value - entries.Count) || !info.TryGetProperty("hasNextPage", out var hasNext) || hasNext.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || hasNext.GetBoolean() != (entries.Count + rows.Length < total)) throw new InvalidDataException("ARD Sounds returned a partial show page.");
            if (rows.Length > 0 && (Text(info, "endCursor") is not { Length: > 0 and <= 4096 } cursor || !cursors.Add(cursor))) throw new InvalidDataException("ARD Sounds repeated or omitted its inventory cursor.");
            foreach (var row in rows)
            {
                var id = Text(row, "assetId") ?? throw new InvalidDataException("ARD Sounds omitted a show member."); var child = ArdSoundsAddress.Parse(new("https://www.ardsounds.de/episode/" + id + "/")); ValidateItem(row, child);
                if (!ids.Add(id) || Text(Object(row, "programSet"), "coreId") != address.Id || Numeric(Object(row, "programSet")) != parent) throw new InvalidDataException("ARD Sounds changed or repeated show membership.");
                var seconds = Number(row, "duration"); var available = True(row, "isPublished") && seconds is > 0 && !(Date(Text(row, "publishDate")) is { } start && start > DateTimeOffset.UtcNow);
                entries.Add(new(entries.Count + 1, id, Title(row), child.Page, seconds, null, available, ExpectedArchiveIdentity: new("ARDAudiothek", id), CollectionMemberId: id));
            }
            if (entries.Count == total) break;
        }
        return Inventory("ARDAudiothekPlaylist", address.Id, title!, entries, request);
    }
    internal static bool MediaHost(Uri url) => url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 && url.Host is "cdn-storage.br.de" or "swr-dl.ard-mcdn.de" or "swr-pd.ard-mcdn.de" or "ndr-podcast.ard-mcdn.de" or "ondemand.unified.mdr.de";
    private static Uri Resource(string raw) => Uri.TryCreate(raw, UriKind.Absolute, out var url) && MediaHost(url) ? url : throw new InvalidDataException("ARD Sounds returned an undeclared audio origin.");
}
