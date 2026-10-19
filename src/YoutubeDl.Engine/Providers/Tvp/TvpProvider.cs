using System.Globalization;
using System.Net;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;
using static YoutubeDl.Engine.Providers.PublicProviderApi;

namespace YoutubeDl.Engine.Providers.Tvp;

internal sealed record TvpAddress(string Kind, string Id)
{
    internal static TvpAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Host != "vod.tvp.pl" || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0) throw new ArgumentException("Use a canonical TVP VOD programme, series or live channel URL.");
        var api = Match(url.AbsolutePath, @"\A/api/products/(?<kind>vods/serials|vods|lives)/(?<id>[1-9][0-9]{0,11})/?\z");
        if (api.Success) return new(api.Groups["kind"].Value switch { "vods/serials" => "series", "lives" => "live", _ => "vod" }, api.Groups["id"].Value);
        var match = Match(url.AbsolutePath, @"\A/(?<category>[a-z0-9-]+),[1-9][0-9]*/(?<item>[a-zA-Z0-9,-]+)(?:/(?<episode>[a-zA-Z0-9,-]+))?/?\z");
        if (!match.Success) throw new NotSupportedException("This TVP URL route is not supported yet.");
        var item = match.Groups["episode"].Success ? match.Groups["episode"].Value : match.Groups["item"].Value; var id = item[(item.LastIndexOf(',') + 1)..];
        if (!Match(id, @"\A[1-9][0-9]{0,11}\z").Success) throw new ArgumentException("TVP omitted its numeric product identity.");
        return new(match.Groups["category"].Value == "live" ? "live" : !match.Groups["episode"].Success && item.EndsWith("-odcinki," + id, StringComparison.Ordinal) ? "series" : "vod", id);
    }
}
public sealed class TvpProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("tvp-vod", "TVP public VOD, live channels and series", 200);
    public bool SupportsGeoVerificationProxy => true;
    public bool CanHandle(Uri url) => url.IsAbsoluteUri && url.Host == "vod.tvp.pl";
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) { var address = TvpAddress.Parse(request.Url); return address.Kind == "series" ? null : new("tvp:vod", address.Id); }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        NativeGeoVerification.ValidateProvider(request, this); var address = TvpAddress.Parse(request.Url);
        if (address.Kind == "series") return await Series(address, request, http, ct).ConfigureAwait(false);
        async Task<PageMetadata> Current(MediaHttp controlled, CancellationToken token)
        {
            using var verification = controlled.CreateVerificationSession(request.GeoVerification?.Proxy);
            var row = await Fetch(verification, "products/" + (address.Kind == "live" ? "lives/" : "vods/") + address.Id, token).ConfigureAwait(false);
            var page = Metadata(row, address); if (Blocked(row)) return page with { Warnings = ["TVP requires access not granted to this public session."] };
            if (await Music.MusicCatalog.RecordedAsync(request, page, token).ConfigureAwait(false)) return page;
            JsonElement grant;
            try { grant = await Read(verification, new("https://vod.tvp.pl/api/products/" + address.Id + "/videos/playlist?videoType=" + (address.Kind == "live" ? "LIVE" : "MOVIE") + "&platform=BROWSER"), token).ConfigureAwait(false); }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { return page with { Warnings = ["TVP did not grant full playback to this session or region."] }; }
            if (Drm(grant)) return page with { DrmRefused = true, RefusalReason = "TVP declares protected playback.", Warnings = ["Native TVP DRM playback is unavailable."] };
            if (Text(Object(grant, "externalVideo"), "externalUid") != Text(row, "externalUid")) throw new InvalidDataException("TVP changed its selected playback resource identity.");
            var sources = Object(grant, "sources"); var formats = new List<MediaCandidate>();
            if (sources.ValueKind != JsonValueKind.Object) throw new InvalidDataException("TVP omitted its playback source map.");
            foreach (var (key, kind) in new[] { ("HLS", MediaKind.Hls), ("DASH", MediaKind.Dash) })
            {
                if (!sources.TryGetProperty(key, out var array)) continue;
                var entries = Rows(array, maximum: 16);
                for (var index = 0; index < entries.Length; index++)
                {
                    if (Drm(entries[index])) throw new NotSupportedException("This TVP transport declares protected playback.");
                    var url = Resource(Text(entries[index], "src")); if (PageMediaExtractor.Classify(url) != kind) throw new InvalidDataException("TVP returned another transport for its playback descriptor.");
                    formats.Add(new() { Id = key.ToLowerInvariant() + "-" + index, Url = url, Kind = kind, Extension = "mp4", HasAudio = true, HasVideo = true, Source = Info.Id, VerificationDurationSeconds = page.DurationSeconds });
                }
            }
            if (formats.Count == 0) throw new InvalidDataException("TVP omitted its complete supported playback sources."); return page with { Media = formats };
        }
        return await http.SessionState(Key(Info.Id, request), () => new ProviderPlaybackSession(http, request, Info.Id, Current, reorderedFormats: false)).ReadAsync(ct).ConfigureAwait(false);
    }
    internal static bool MediaHost(Uri url) => url.IsAbsoluteUri && url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 && (url.Host == "cdn.tvp.pl" || url.Host.EndsWith(".cdn.tvp.pl", StringComparison.Ordinal) || url.Host.EndsWith(".v4.cache.orange.pl", StringComparison.Ordinal));
    private static Uri Resource(string? text) => Uri.TryCreate(text, UriKind.Absolute, out var url) && url.AbsoluteUri.Length <= 16384 && MediaHost(url) ? url : throw new InvalidDataException("TVP returned an undeclared media origin.");
    private static bool Blocked(JsonElement row) => True(row, "payable") || True(row, "loginRequired") || True(row, "geoipManualLock") || row.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.False;
    private static PageMetadata Metadata(JsonElement row, TvpAddress expected)
    {
        var id = Numeric(row); var type = Text(row, "type"); if (id != expected.Id || expected.Kind == "live" && type != "LIVE" || expected.Kind == "vod" && type is not ("VOD" or "EPISODE")) throw new InvalidDataException("TVP returned another selected product identity or type.");
        var pageUrl = Secure(Text(row, "webUrl"), "vod.tvp.pl"); var page = TvpAddress.Parse(pageUrl); if (page.Id != id || page.Kind != expected.Kind) throw new InvalidDataException("TVP changed its canonical product page.");
        var external = Text(row, "externalUid"); if (external is null || !Match(external, @"\A[1-9][0-9]{0,15}\z").Success) throw new InvalidDataException("TVP omitted its playback identity.");
        var duration = Number(row, "duration"); if (expected.Kind != "live" && duration is not (> 0 and <= 604800)) throw new InvalidDataException("TVP omitted its full programme duration.");
        var images = Object(row, "images"); var image = images.ValueKind == JsonValueKind.Object && images.TryGetProperty("16x9", out var group) ? Rows(group, maximum: 32).FirstOrDefault() : default;
        var raw = Text(image, "url"); var date = Date(Text(row, "since"));
        return new() { Id = id, Extractor = "tvp:vod", ArchiveIdentity = new("tvp:vod", id), Title = Title(row), Description = Text(row, "description") ?? Text(row, "lead"), DurationSeconds = expected.Kind == "live" ? null : duration, IsLive = expected.Kind == "live", AgeLimit = Integer(row, "rating"),
            UploadTimestamp = date, UploadDate = date is null ? null : DateOnly.FromDateTime(date.Value.UtcDateTime), Uploader = "TVP", ThumbnailUrl = raw is null ? null : Secure(raw.StartsWith("//", StringComparison.Ordinal) ? "https:" + raw : raw, "s.tvp.pl").AbsoluteUri, AnonymousPlayback = true, SourcePageUrl = pageUrl };
    }
    private static async Task<PageMetadata> Series(TvpAddress address, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        using var verification = http.CreateVerificationSession(request.GeoVerification?.Proxy); var parent = await Fetch(verification, "products/vods/serials/" + address.Id, ct).ConfigureAwait(false);
        if (Numeric(parent) != address.Id || Text(parent, "type") != "SERIAL" || TvpAddress.Parse(Secure(Text(parent, "webUrl"), "vod.tvp.pl")) != address) throw new InvalidDataException("TVP returned another series identity.");
        var seasons = Rows(await Fetch(verification, "products/vods/serials/" + address.Id + "/seasons", ct).ConfigureAwait(false), maximum: 1000); var entries = new List<NativePlaylistEntry>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var season in seasons)
        {
            var id = Numeric(season); if (!seen.Add(id) || Text(season, "type") != "SEASON") throw new InvalidDataException("TVP repeated or changed a season identity.");
            var rows = Rows(await Fetch(verification, "products/vods/serials/" + address.Id + "/seasons/" + id + "/episodes", ct).ConfigureAwait(false));
            var range = Match(Text(season, "display") ?? "", @"\A(?<last>[0-9]+)\s*-\s*(?<first>[0-9]+)\z");
            if (rows.Length > 0)
            {
                if (!range.Success || !int.TryParse(range.Groups["last"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var last) || !int.TryParse(range.Groups["first"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var first) || first < 1 || last < 1 || first > 10000 || last > 10000)
                    throw new InvalidDataException("TVP did not declare a bounded season range.");
                var numbers = rows.Select(row => Integer(row, "number")).ToArray();
                if (numbers.Any(number => number is null) || !numbers.Select(number => number!.Value).Distinct().Order().SequenceEqual(Enumerable.Range(Math.Min(first, last), Math.Abs(last - first) + 1)))
                    throw new InvalidDataException("TVP did not return the complete declared season range.");
            }
            foreach (var row in rows)
            {
                var metadata = Metadata(row, new("vod", Numeric(row))); var relation = Object(row, "season");
                if (Numeric(relation) != id || Numeric(Object(relation, "serial")) != address.Id) throw new InvalidDataException("TVP changed an episode's selected series membership.");
                entries.Add(new(entries.Count + 1, metadata.Id, metadata.Title, metadata.SourcePageUrl, metadata.DurationSeconds, metadata.ThumbnailUrl, !Blocked(parent) && !Blocked(season) && !Blocked(row), ExpectedArchiveIdentity: metadata.ArchiveIdentity, CollectionMemberId: metadata.Id)); Limit(entries.Count, request);
            }
        }
        return Inventory("tvp:vod:series", address.Id, Title(parent), entries, request);
    }
    private static Task<JsonElement> Fetch(MediaHttp http, string path, CancellationToken ct) => Read(http, new("https://vod.tvp.pl/api/" + path + "?lang=pl&platform=BROWSER"), ct);
}
