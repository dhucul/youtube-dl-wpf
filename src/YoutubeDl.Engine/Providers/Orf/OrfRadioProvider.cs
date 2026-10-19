using System.Globalization;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Music;
using static YoutubeDl.Engine.Providers.ProviderJson;
using static YoutubeDl.Engine.Providers.PublicProviderApi;

namespace YoutubeDl.Engine.Providers.Orf;

internal sealed record OrfRadioAddress(string Station, string Kind, string? Id)
{
    internal static readonly string[] Stations = ["oe1", "oe3", "fm4", "bgl", "ktn", "noe", "ooe", "sbg", "stm", "tir", "vbg", "wie"];
    internal Uri Url => new("https://sound.orf.at/" + (Kind == "live" ? "play/live/" + Station : "radio/" + Station + "/" + Kind + (Id is null ? "" : "/" + Id)));
    internal string Api => "https://audioapi.orf.at/" + Station + "/api/json/5.0/";
    internal static OrfRadioAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0) throw new ArgumentException("Use a canonical ORF radio URL.");
        var path = url.Host == "sound.orf.at" ? Match(url.AbsolutePath, @"\A/(?:radio/(?<station>[a-z0-9]{2,3})/(?:(?<kind>sendung|beitrag)/(?<id>[1-9][0-9]{0,11})|(?<kind>kalender))|play/(?<kind>live)/(?<station>[a-z0-9]{2,3}))/?\z") : url.Host == "audioapi.orf.at" ? Match(url.AbsolutePath, @"\A/(?<station>[a-z0-9]{2,3})/api/json/5\.0/(?<kind>broadcast|broadcastitem)/(?<id>[1-9][0-9]{0,11})/?\z") : Match("", "a");
        if (!path.Success || !Stations.Contains(path.Groups["station"].Value, StringComparer.Ordinal)) throw new NotSupportedException("This ORF radio route or station is not supported yet.");
        var kind = path.Groups["kind"].Value; return new(path.Groups["station"].Value, kind, path.Groups["id"].Success ? path.Groups["id"].Value : null);
    }
}

public sealed class OrfRadioProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("orf-radio", "ORF public radio broadcasts and live", 201);
    public bool SupportsGeoVerificationProxy => true;
    public bool CanHandle(Uri url) => url.IsAbsoluteUri && (url.Host == "sound.orf.at" && (url.AbsolutePath.StartsWith("/radio/", StringComparison.Ordinal) || url.AbsolutePath.StartsWith("/play/live/", StringComparison.Ordinal)) || url.Host == "audioapi.orf.at");
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        NativeGeoVerification.ValidateProvider(request, this); var address = OrfRadioAddress.Parse(request.Url);
        if (address.Kind == "kalender")
        {
            using var verification = http.CreateVerificationSession(request.GeoVerification?.Proxy); var rows = await Calendar(verification, address, ct).ConfigureAwait(false); Limit(rows.Length, request);
            var entries = new List<NativePlaylistEntry>();
            foreach (var row in rows)
            {
                var page = Metadata(row, address, false); entries.Add(new(entries.Count + 1, page.Id, page.Title, new Uri(address.Api + "broadcast/" + Numeric(row)), page.DurationSeconds, null, Available(row), ExpectedArchiveIdentity: page.ArchiveIdentity, CollectionMemberId: Numeric(row)));
            }
            return Inventory("orf:radio", address.Station + ":calendar", "ORF " + address.Station + " available calendar", entries, request);
        }
        async Task<PageMetadata> Current(MediaHttp controlled, CancellationToken token)
        {
            using var verification = controlled.CreateVerificationSession(request.GeoVerification?.Proxy);
            if (address.Kind == "live")
            {
                var root = await Read(verification, new("https://orf.at/app-infos/sound/web/1.0/bundle.json"), token).ConfigureAwait(false); var config = Object(Object(root, "stations"), address.Station); var template = Text(config, "liveStreamUrlTemplate");
                var expected = "https://orf-live-" + address.Station + ".mdn.ors.at/out/u/" + address.Station + "/{quality}/manifest.m3u8";
                if (template != expected) throw new InvalidDataException("ORF changed its declared live station resource.");
                return new() { Id = address.Station + ":live", Extractor = "orf:radio", ArchiveIdentity = new("orf:radio", address.Station + ":live"), Title = Title(config, "name"), Uploader = "ORF", IsLive = true, AnonymousPlayback = true, SourcePageUrl = address.Url,
                    Media = [new() { Id = "live-hls", Url = new(template.Replace("{quality}", "q2a", StringComparison.Ordinal)), Kind = MediaKind.Hls, Extension = "m4a", HasAudio = true, HasVideo = false, Source = Info.Id }] };
            }
            JsonElement row;
            if (address.Kind == "sendung")
            {
                var rows = await Calendar(verification, address, token).ConfigureAwait(false); var matches = rows.Where(row => Text(row, "programKey") == address.Id).ToArray();
                if (matches.Length != 1) throw new InvalidDataException("ORF did not identify one complete selected programme in its available calendar.");
                row = Object(await Read(verification, new(address.Api + "broadcast/" + Numeric(matches[0]) + "?items"), token).ConfigureAwait(false), "payload");
                if (Text(row, "programKey") != address.Id || Numeric(row) != Numeric(matches[0])) throw new InvalidDataException("ORF changed the selected programme identity.");
            }
            else row = Object(await Read(verification, new(address.Api + (address.Kind is "beitrag" or "broadcastitem" ? "broadcastitem/" : "broadcast/") + address.Id + (address.Kind == "broadcast" ? "?items" : "")), token).ConfigureAwait(false), "payload");
            var item = address.Kind is "beitrag" or "broadcastitem"; Validate(row, address, item);
            if (address.Kind != "sendung" && Numeric(row) != address.Id) throw new InvalidDataException("ORF returned another selected radio identity.");
            var page = Metadata(row, address, item);
            if (!Available(row)) return page with { DrmRefused = Drm(row), Warnings = ["ORF has not published complete public playback for this radio item."] };
            var streams = item ? new[] { Object(row, "stream") } : Rows(row, "streams", 512); var parts = new List<MediaFilePart>(); DateTimeOffset? end = null; double total = 0;
            var declaredStart = Date(Text(row, "start")); var declaredEnd = Date(Text(row, "end"));
            foreach (var stream in streams)
            {
                var start = Date(Text(stream, "start")); var finish = Date(Text(stream, "end")); var duration = Number(stream, "duration"); var offset = Long(stream, "offsetStart"); var last = Long(stream, "offsetEnd");
                if (start is null || finish is null || finish <= start || duration is not (> 0 and <= 604800000) || Math.Abs((finish.Value - start.Value).TotalMilliseconds - duration.Value) > 1 || end is not null && start != end || offset is null || last is null || last - offset != duration) throw new InvalidDataException("ORF returned a gap, overlap or inconsistent radio part.");
                if (end is null && declaredStart != start) throw new InvalidDataException("ORF omitted the beginning of its radio source."); end = finish; total += duration.Value / 1000;
                var id = Text(stream, "loopStreamId"); if (id is null || !Match(id, @"\A[A-Za-z0-9_-]{1,240}\.mp3\z").Success) throw new InvalidDataException("ORF omitted a stable radio recording identity.");
                var host = Text(stream, "host"); if (host is null || !RadioHost(host) || Text(stream, "channel") != Channel(address.Station)) throw new InvalidDataException("ORF returned another station's radio resource.");
                // Only the documented empty optional URI-template expressions are expanded.
                var raw = Text(Object(stream, "urls"), "progressive") ?? throw new InvalidDataException("ORF omitted a complete progressive radio part.");
                raw = raw.Replace("{&shoutcast}", "&shoutcast=0", StringComparison.Ordinal);
                foreach (var optional in new[] { "{&player}", "{&referer}", "{&userid}" }) raw = raw.Replace(optional, "", StringComparison.Ordinal);
                var url = Secure(raw, host); if (url.AbsolutePath != "/" || raw.Contains('{') || raw.Contains('}')) throw new InvalidDataException("ORF returned an unsupported radio URI template.");
                var fields = url.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2)).ToArray();
                if (fields.Any(pair => pair.Length != 2) || fields.Select(pair => pair[0]).Distinct(StringComparer.Ordinal).Count() != fields.Length || fields.Any(pair => pair[0] is not ("channel" or "id" or "offset" or "offsetende" or "shoutcast"))) throw new InvalidDataException("ORF returned unsupported radio resource fields.");
                var values = fields.ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]), StringComparer.Ordinal);
                if (values.GetValueOrDefault("id") != id || values.GetValueOrDefault("channel") != Channel(address.Station) || values.GetValueOrDefault("shoutcast", "0") != "0" || values.GetValueOrDefault("offset", "0") != offset.Value.ToString(CultureInfo.InvariantCulture) || values.GetValueOrDefault("offsetende") != last.Value.ToString(CultureInfo.InvariantCulture)) throw new InvalidDataException("ORF changed its selected recording range.");
                parts.Add(new(url, "mp3", duration.Value / 1000));
            }
            if (parts.Count == 0 || end != declaredEnd || !item && Math.Abs(total - page.DurationSeconds!.Value) > 0.001) throw new InvalidDataException("ORF returned incomplete radio playback coverage.");
            // Item editorial durations can be rounded; the supplied stream interval is authoritative.
            page = page with { DurationSeconds = total };
            return page with { Media = [new() { Id = "radio-mp3", Url = parts[0].Url, Parts = parts.Count > 1 ? parts : [], Kind = parts.Count > 1 ? MediaKind.HttpSequence : MediaKind.Progressive, Extension = "mp3", HasAudio = true, HasVideo = false, AudioCodec = "mp3", Source = Info.Id, NormalizeMp3ClipBoundary = true, VerificationDemuxer = "mp3", VerificationDurationSeconds = total, VerificationMinimumDurationSeconds = total, VerificationMinimumDurationToleranceSeconds = 1.1 }] };
        }
        return await http.SessionState(Key(Info.Id, request), () => new ProviderPlaybackSession(http, request, Info.Id, Current, reorderedFormats: false)).ReadAsync(ct).ConfigureAwait(false);
    }
    internal static bool RadioHost(string host) => host is "loopstream01.apa.at" or "loopstreamoe1.apa.at" or "loopstreamoe3.apa.at" or "loopstreamfm4.apa.at" || OrfRadioAddress.Stations.Skip(3).Any(station => host == "loopstream" + Channel(station) + ".apa.at");
    private static string Channel(string station) => station switch { "bgl" => "oe2b", "ktn" => "oe2k", "noe" => "oe2n", "ooe" => "oe2o", "sbg" => "oe2s", "stm" => "oe2st", "tir" => "oe2t", "vbg" => "oe2v", "wie" => "oe2w", _ => station };
    private static bool Available(JsonElement row) => Text(row, "state") == "C" && True(row, "isOnDemand") && !Drm(row) && !(Date(Text(row, "expiry")) is { } expiry && expiry <= DateTimeOffset.UtcNow);
    private static void Validate(JsonElement row, OrfRadioAddress address, bool item)
    {
        var kind = item ? "BroadcastItem" : "Broadcast"; var route = item ? "broadcastitem/" : "broadcast/";
        if (Text(row, "station") != address.Station || Text(row, "entity") != kind || Secure(Text(row, "href"), "audioapi.orf.at", "audioapi2.orf.at").AbsolutePath != new Uri(address.Api + route + Numeric(row)).AbsolutePath) throw new InvalidDataException("ORF returned another station, type or radio identity.");
    }
    private static PageMetadata Metadata(JsonElement row, OrfRadioAddress address, bool item)
    {
        Validate(row, address, item); var id = address.Station + ":" + (item ? "item:" : "broadcast:") + Numeric(row); var duration = Number(row, "duration") / 1000;
        if (duration is not (> 0 and <= 604800)) throw new InvalidDataException("ORF omitted its radio duration."); var date = Date(Text(row, "start"));
        return new() { Id = id, Extractor = "orf:radio", ArchiveIdentity = new("orf:radio", id), Title = Title(row), Description = Text(row, "description"), DurationSeconds = duration, UploadTimestamp = date, UploadDate = date is null ? null : DateOnly.FromDateTime(date.Value.UtcDateTime), Uploader = "ORF " + address.Station, AnonymousPlayback = true, SourcePageUrl = address.Url };
    }
    private static async Task<JsonElement[]> Calendar(MediaHttp http, OrfRadioAddress address, CancellationToken ct)
    {
        var days = Rows(await Read(http, new(address.Api + "broadcasts"), ct).ConfigureAwait(false), "payload", 40); var rows = new List<JsonElement>(); var ids = new HashSet<string>(StringComparer.Ordinal); var dayIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var day in days)
        {
            var dayId = Numeric(day, "day"); if (!DateOnly.TryParseExact(dayId, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) || !dayIds.Add(dayId)) throw new InvalidDataException("ORF returned ambiguous calendar days.");
            foreach (var row in Rows(day, "broadcasts", 1000)) { Validate(row, address, false); if (Numeric(row, "broadcastDay") != dayId || !ids.Add(Numeric(row))) throw new InvalidDataException("ORF changed or repeated calendar membership."); rows.Add(row); if (rows.Count > 10000) throw new InvalidDataException("ORF exceeded its complete calendar bound."); }
        }
        return rows.ToArray();
    }
}
