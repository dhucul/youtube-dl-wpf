using System.Globalization;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Music;
using static YoutubeDl.Engine.Providers.ProviderJson;
using static YoutubeDl.Engine.Providers.PublicProviderApi;

namespace YoutubeDl.Engine.Providers.RadioFrance;

internal sealed record RadioFranceAddress(string Station, string Path, string? Id)
{
    internal string Kind => Path.StartsWith("personnes/", StringComparison.Ordinal) ? "profile" : Path.EndsWith("/grille-programmes", StringComparison.Ordinal) ? "schedule" : "podcast";
    internal Uri Url => new("https://www.radiofrance.fr/" + Path);
    internal static RadioFranceAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Host is not ("www.radiofrance.fr" or "radiofrance.fr") || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0) throw new ArgumentException("Use a canonical Radio France podcast episode or collection URL.");
        if (Match(url.AbsolutePath, @"\A/personnes/[a-z0-9-]{1,240}/?\z").Success) return new("", url.AbsolutePath.Trim('/'), null);
        var schedule = Match(url.AbsolutePath, @"\A/(?<station>franceculture|franceinter|francemusique|franceinfo|fip|mouv)/grille-programmes/?\z");
        if (schedule.Success) return new(schedule.Groups["station"].Value, url.AbsolutePath.Trim('/'), null);
        var match = Match(url.AbsolutePath, @"\A/(?<station>franceculture|franceinter|francemusique|franceinfo|fip|mouv)/podcasts/(?<name>[a-z0-9-]{1,240})(?:/(?<episode>[a-z0-9-]{1,300}))?/?\z");
        if (!match.Success) throw new NotSupportedException("This Radio France route is not supported yet.");
        var slug = match.Groups["episode"].Success ? match.Groups["episode"].Value : match.Groups["name"].Value; var episode = Match(slug, @"-(?<id>[1-9][0-9]{5,11})\z");
        if (match.Groups["episode"].Success && !episode.Success) throw new NotSupportedException("This Radio France episode has no supported stable page identity.");
        return new(match.Groups["station"].Value, url.AbsolutePath.Trim('/'), episode.Success ? episode.Groups["id"].Value : null);
    }
}

public sealed class RadioFranceProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("radiofrance-podcast", "Radio France public episodes and complete podcasts", 202);
    public bool SupportsGeoVerificationProxy => true;
    public bool CanHandle(Uri url) => url.IsAbsoluteUri && url.Host is "www.radiofrance.fr" or "radiofrance.fr";
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        NativeGeoVerification.ValidateProvider(request, this); var address = RadioFranceAddress.Parse(request.Url); var membership = http.SessionState("radiofrance:members", () => new Membership());
        if (address.Kind != "podcast") return await ExtendedInventory(request, http, address, membership, ct).ConfigureAwait(false);
        if (address.Id is null)
        {
            using var verification = http.CreateVerificationSession(request.GeoVerification?.Proxy); var entries = new List<NativePlaylistEntry>(); string? parentId = null, parentType = null, title = null; int? total = null, limit = null, pages = null; var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var page = 1; ; page++)
            {
                if (page > 500) throw new InvalidDataException("Radio France exceeded the bounded complete podcast inventory.");
                var root = await Fetch(verification, address, page, ct).ConfigureAwait(false); var content = Object(root, "content"); var id = GuidId(content);
                if (Text(root, "contentType") != "Concept" || Text(root, "pageType") != (Text(content, "type") == "serie" ? "Serie" : "Podcast") || Text(content, "__typename") != "Concept" || Text(content, "type") is not ("podcast" or "serie")) throw new InvalidDataException("Radio France returned another collection type.");
                var type = Text(content, "type"); var count = Integer(content, "expressionCount"); var size = Integer(content, "limit"); var last = Integer(Object(content, "pagination"), "lastPage");
                if (count is null or < 0 || size is null or < 1 or > 100 || last != Math.Max(1, (int)Math.Ceiling(count.Value / (double)size.Value)) || parentId is not null && (parentId != id || parentType != type || total != count || limit != size || pages != last)) throw new InvalidDataException("Radio France changed its complete inventory bounds or identity.");
                parentId = id; parentType = type; total = count; limit = size; pages = last; title ??= Title(content); Limit(total.Value, request);
                var expressions = Object(content, "expressions"); var rows = Rows(expressions, "items", 100);
                var next = page < pages ? Cursor(page * limit.Value) : null; var previous = page > 1 ? Cursor((page - 2) * limit.Value) : null;
                if (Text(expressions, "next") != next || Text(expressions, "prev") != previous || rows.Length != Math.Min(limit.Value, total.Value - entries.Count)) throw new InvalidDataException("Radio France returned a partial or repeated inventory page.");
                foreach (var row in rows)
                {
                    var uuid = GuidId(row); var props = Object(row, "titleProps"); var item = Relative(Text(props, "href")); var child = RadioFranceAddress.Parse(item);
                    if (Text(row, "__typename") != "Expression" || child.Id is null || child.Station != address.Station || Text(row, "brand") != address.Station || parentType == "podcast" && Text(row, "conceptId") != parentId || !seen.Add(uuid)) throw new InvalidDataException("Radio France changed or repeated collection membership.");
                    membership.Add(item, uuid); var manifestation = Object(row, "manifestation"); var duration = Number(manifestation, "duration") ?? Number(row, "duration"); var available = Text(row, "type") == "episode" && Text(manifestation, "__typename") == "ManifestationAudio" && duration is > 0 && !Blocked(row);
                    entries.Add(new(entries.Count + 1, child.Id, Title(props), item, duration, Image(Object(row, "visual")), available, ExpectedArchiveIdentity: new("FranceCulture", child.Id), CollectionMemberId: uuid));
                }
                if (page == pages) break;
            }
            if (entries.Count != total) throw new InvalidDataException("Radio France omitted podcast members.");
            return Inventory("RadioFrancePodcast", parentId!, title!, entries, request);
        }
        string? selectedUuid = null;
        async Task<PageMetadata> Current(MediaHttp controlled, CancellationToken token)
        {
            using var verification = controlled.CreateVerificationSession(request.GeoVerification?.Proxy); var root = await Fetch(verification, address, 1, token).ConfigureAwait(false); var content = Object(root, "content"); var uuid = GuidId(content);
            membership.Validate(address.Url, uuid); if (selectedUuid is not null && selectedUuid != uuid) throw new InvalidDataException("Radio France changed the selected episode's publisher identity."); selectedUuid ??= uuid;
            if (Text(root, "contentType") != "Expression" || Text(root, "pageType") != "Expression" || Text(content, "__typename") != "Expression" || Text(content, "type") != "episode" || Text(content, "brand") != address.Station) throw new InvalidDataException("Radio France returned another media type or station.");
            var manifestation = Object(content, "manifestation"); var duration = Number(manifestation, "duration") ?? Number(content, "duration"); if (duration is not (> 0 and <= 604800)) throw new InvalidDataException("Radio France omitted the complete programme duration.");
            var published = Long(content, "startDate"); var date = published is > 0 and <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(published.Value) : (DateTimeOffset?)null;
            var page = new PageMetadata { Id = address.Id, Extractor = "FranceCulture", ArchiveIdentity = new("FranceCulture", address.Id), Title = Title(content), Description = Text(content, "chapo"), DurationSeconds = duration, ThumbnailUrl = Image(Object(content, "visual")), Uploader = Text(content, "conceptTitle") ?? Title(Object(root, "station")), UploadTimestamp = date, UploadDate = date is null ? null : DateOnly.FromDateTime(date.Value.UtcDateTime), AnonymousPlayback = true, SourcePageUrl = address.Url, MetadataFields = new Dictionary<string, object?> { ["radiofrance_uuid"] = uuid } };
            if (!True(content, "isStreamable") || Blocked(content)) return page with { DrmRefused = Drm(content), Warnings = ["Radio France has not granted complete public playback for this episode."] };
            if (await MusicCatalog.RecordedAsync(request, page, token).ConfigureAwait(false)) return page;
            if (Text(manifestation, "__typename") != "ManifestationAudio" || Drm(manifestation)) throw new NotSupportedException("This Radio France episode has no supported clear full audio manifestation.");
            var mediaId = GuidId(manifestation); var url = Secure(Text(manifestation, "url"), "media.radiofrance-podcast.net"); var extension = Path.GetExtension(url.AbsolutePath).TrimStart('.').ToLowerInvariant();
            if (extension is not ("mp3" or "m4a") || Number(content, "duration") is > 0 and var metadataDuration && Math.Abs(metadataDuration - duration.Value) > 1.1) throw new InvalidDataException("Radio France changed its complete audio duration or encoding.");
            return page with { Media = [MusicCatalog.Audio(url, "audio-" + uuid + "-" + mediaId, extension, duration.Value, Info.Id)] };
        }
        return await http.SessionState(Key(Info.Id, request), () => new ProviderPlaybackSession(http, request, Info.Id, Current, reorderedFormats: false)).ReadAsync(ct).ConfigureAwait(false);
    }
    private static async Task<JsonElement> Fetch(MediaHttp http, RadioFranceAddress address, int page, CancellationToken ct)
    {
        var envelope = await Read(http, new(address.Url.AbsoluteUri + "/__data.json" + (page == 1 ? "" : "?p=" + page)), ct).ConfigureAwait(false); var roots = SvelteData.Decode(envelope).Where(root => Object(root, "content").ValueKind == JsonValueKind.Object).ToArray();
        if (roots.Length != 1) throw new InvalidDataException("Radio France returned an ambiguous selected page."); var root = roots[0]; var canonical = Secure(Text(Object(root, "seo"), "canonicalUrl"), "www.radiofrance.fr");
        if (canonical != address.Url || Text(Object(root, "content"), "path") != address.Path || address.Kind != "profile" && Text(Object(root, "station"), "name") != address.Station) throw new InvalidDataException("Radio France returned another canonical page or station."); return root;
    }
    private static async Task<PageMetadata> ExtendedInventory(NativeDownloadRequest request, MediaHttp http, RadioFranceAddress address, Membership membership, CancellationToken ct)
    {
        using var verification = http.CreateVerificationSession(request.GeoVerification?.Proxy); var entries = new List<NativePlaylistEntry>(); string? parent = null, title = null; int? total = null, size = null, pages = null; long previousTime = 0, firstTime = 0; var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 1; ; page++)
        {
            if (page > 500) throw new InvalidDataException("Radio France exceeded its bounded complete profile inventory.");
            var root = await Fetch(verification, address, page, ct).ConfigureAwait(false); var content = Object(root, "content"); var id = GuidId(content);
            if (parent is not null && parent != id) throw new InvalidDataException("Radio France changed the selected inventory identity."); parent ??= id; title ??= Title(content);
            JsonElement[] rows;
            if (address.Kind == "profile")
            {
                if (Text(root, "contentType") != "Personality" || Text(root, "pageType") != "Personality" || Text(content, "__typename") != "Personality") throw new InvalidDataException("Radio France returned another profile type.");
                var documents = Object(content, "documents"); rows = Rows(documents, "items", 100); var count = Integer(documents, "total"); var last = Integer(Object(content, "pagination"), "lastPage");
                size ??= Math.Max(1, rows.Length);
                if (count is null or < 0 || last != Math.Max(1, (int)Math.Ceiling(count.Value / (double)size.Value)) || total is not null && total != count || pages is not null && pages != last) throw new InvalidDataException("Radio France capped or changed its complete profile inventory.");
                total = count; pages = last; Limit(total.Value, request);
                if (Text(documents, "next") != (page < pages ? Cursor(page * size.Value) : null) || Text(documents, "prev") != (page == 2 ? "" : page > 2 ? Cursor((page - 2) * size.Value) : null) || rows.Length != Math.Min(size.Value, total.Value - entries.Count)) throw new InvalidDataException("Radio France returned a partial or repeated profile page.");
            }
            else
            {
                if (Text(root, "contentType") != "PageTemplate" || Text(root, "pageType") != "ProgramGrid" || Text(content, "__typename") != "PageTemplate" || Text(content, "displayFormat") != "grid" || Text(content, "brand") != address.Station) throw new InvalidDataException("Radio France returned another programme grid.");
                rows = Rows(content, "programs", 1000); total = rows.Length; pages = 1; Limit(total.Value, request);
            }
            foreach (var row in rows)
            {
                var uuid = GuidId(row); var props = Object(row, "titleProps"); var raw = Text(props, "href"); Uri? url = null; RadioFranceAddress? child = null;
                if (!string.IsNullOrEmpty(raw))
                {
                    var relative = Relative(raw);
                    if (Text(row, "__typename") == "Expression")
                    {
                        try { child = RadioFranceAddress.Parse(relative); } catch (NotSupportedException) when (address.Kind == "profile") { }
                        if (child is not null && (child.Kind != "podcast" || child.Id is null || Text(row, "brand") != child.Station || address.Kind == "schedule" && child.Station != address.Station)) throw new InvalidDataException("Radio France changed a selected episode's station or type."); url = relative;
                    }
                }
                var member = uuid;
                if (address.Kind == "schedule")
                {
                    var start = Long(row, "startTimeUnix");
                    if (Text(row, "__typename") != "Expression" || Text(row, "brand") != address.Station || start is not (> 0 and <= 253402300799) || start <= previousTime) throw new InvalidDataException("Radio France changed or repeated its ordered schedule clocks.");
                    if (entries.Count == 0) firstTime = start.Value;
                    if (start.Value - firstTime > 129600) throw new InvalidDataException("Radio France returned an unbounded schedule horizon."); previousTime = start.Value; member += ":" + start.Value.ToString(CultureInfo.InvariantCulture);
                }
                if (!seen.Add(member)) throw new InvalidDataException("Radio France repeated an inventory member.");
                if (url is not null && child is not null)
                {
                    var playbackId = address.Kind == "schedule" ? Text(row, "playerId") : uuid;
                    if (playbackId is null || !Guid.TryParseExact(playbackId, "D", out _)) throw new InvalidDataException("Radio France omitted the scheduled replay identity.");
                    membership.Add(url, playbackId.ToLowerInvariant());
                }
                var duration = Number(Object(row, "manifestation"), "duration") ?? Number(row, "duration");
                var available = child is not null && !Blocked(row) && (address.Kind == "schedule" ? True(row, "hasManifestationAudio") : Text(row, "type") == "episode" && Text(Object(row, "manifestation"), "__typename") == "ManifestationAudio" && duration is > 0);
                entries.Add(new(entries.Count + 1, child?.Id ?? uuid, Title(props, Text(props, "text") is { Length: > 0 } ? "text" : "title"), url, duration, Image(Object(row, "visual")), available, ExpectedArchiveIdentity: child is null ? null : new("FranceCulture", child.Id!), CollectionMemberId: member));
            }
            if (page == pages) break;
        }
        if (entries.Count != total) throw new InvalidDataException("Radio France omitted inventory positions.");
        var identity = parent! + (address.Kind == "schedule" && entries.Count > 0 ? ":" + firstTime.ToString(CultureInfo.InvariantCulture) : "");
        return Inventory(address.Kind == "profile" ? "RadioFranceProfile" : "RadioFranceProgramSchedule", identity, title!, entries, request);
    }
    private static bool Blocked(JsonElement row) => Drm(row) || True(row, "requiresLogin") || True(row, "geoRestricted") || True(row, "payable");
    private static string GuidId(JsonElement row) => Text(row, "id") is { } id && Guid.TryParseExact(id, "D", out _) ? id.ToLowerInvariant() : throw new InvalidDataException("Radio France omitted a stable publisher identity.");
    private static string? Image(JsonElement visual) => Text(visual, "src") is { } raw ? Secure(raw.StartsWith("/_app/immutable/assets/", StringComparison.Ordinal) ? Relative(raw).AbsoluteUri : raw, "www.radiofrance.fr").AbsoluteUri : null;
    private static Uri Relative(string? raw) => raw is { Length: > 0 and <= 1024 } && raw.StartsWith('/') && !raw.StartsWith("//", StringComparison.Ordinal) ? new("https://www.radiofrance.fr" + raw) : throw new InvalidDataException("Radio France returned an undeclared member page.");
    private static string Cursor(int offset) => Convert.ToBase64String(Encoding.ASCII.GetBytes(offset.ToString(CultureInfo.InvariantCulture)));
    private sealed class Membership : IDisposable
    {
        private readonly Dictionary<Uri, string> _entries = [];
        internal void Add(Uri url, string id) { lock (_entries) { if (_entries.TryGetValue(url, out var old) && old != id) throw new InvalidDataException("Radio France changed an episode URL's membership identity."); _entries[url] = id; } }
        internal void Validate(Uri url, string id) { lock (_entries) if (_entries.TryGetValue(url, out var expected) && expected != id) throw new InvalidDataException("Radio France replaced the selected collection episode."); }
        public void Dispose() { lock (_entries) _entries.Clear(); }
    }
}
