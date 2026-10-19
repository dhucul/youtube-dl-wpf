using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Music;
using static YoutubeDl.Engine.Providers.ProviderJson;
using static YoutubeDl.Engine.Providers.PublicProviderApi;

namespace YoutubeDl.Engine.Providers.Orf;

internal sealed record OrfOnAddress(string Kind, string Id)
{
    internal Uri Url => new("https://on.orf.at/" + Kind + "/" + Id);
    internal static OrfOnAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Host != "on.orf.at" || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0) throw new ArgumentException("Use a canonical ORF ON episode or profile URL.");
        var match = Match(url.AbsolutePath, @"\A/(?<kind>video|profile)/(?<id>[1-9][0-9]{0,11})(?:/[a-z0-9-]{1,240})?/?\z");
        if (!match.Success) throw new NotSupportedException("This ORF ON route is not supported yet.");
        return new(match.Groups["kind"].Value, match.Groups["id"].Value);
    }
}

/// <summary>Uses the public client configuration issued by ORF ON, without an account session.</summary>
public sealed class OrfOnProvider : IMediaProvider
{
    private const string Api = "https://api-tvthek.orf.at/api/v4.3/";
    public MediaProviderInfo Info { get; } = new("orf-on", "ORF ON public episodes and series", 201);
    public bool SupportsGeoVerificationProxy => true;
    public bool CanHandle(Uri url) => url.IsAbsoluteUri && url.Host == "on.orf.at";
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        NativeGeoVerification.ValidateProvider(request, this); var address = OrfOnAddress.Parse(request.Url);
        if (address.Kind == "profile")
        {
            using var verification = http.CreateVerificationSession(request.GeoVerification?.Proxy); var client = await Client.Create(verification, ct).ConfigureAwait(false);
            var parent = await client.Read("profile/" + address.Id, ct).ConfigureAwait(false); Validate(parent, "profile/" + address.Id, address.Id);
            var entries = new List<NativePlaylistEntry>(); var seen = new HashSet<string>(StringComparer.Ordinal); int? total = null, pages = null;
            for (var page = 1; ; page++)
            {
                if (page > 100) throw new InvalidDataException("ORF ON exceeded the bounded complete inventory.");
                var row = await client.Read("profile/" + address.Id + "/episodes?limit=100&page=" + page, ct).ConfigureAwait(false);
                var count = Integer(row, "total"); var last = Integer(row, "pages");
                if (Integer(row, "page") != page || Integer(row, "limit") != 100 || count is null or < 0 || last is null or < 1 or > 100 || last != Math.Max(1, (int)Math.Ceiling(count.Value / 100d)) || total is not null && total != count || pages is not null && pages != last) throw new InvalidDataException("ORF ON changed its inventory bounds.");
                total = count; pages = last; Limit(total.Value, request); var items = Rows(Object(row, "_embedded"), "items", 100);
                if (items.Length != Math.Min(100, total.Value - entries.Count)) throw new InvalidDataException("ORF ON returned a partial inventory page.");
                foreach (var item in items)
                {
                    var id = Numeric(item); Validate(item, "episode/" + id, id);
                    var profile = Secure(Text(Object(Object(item, "_links"), "profile"), "href"), "api-tvthek.orf.at");
                    if (profile.AbsoluteUri != Api + "profile/" + address.Id || !seen.Add(id)) throw new InvalidDataException("ORF ON changed or repeated series membership.");
                    var metadata = Metadata(item); entries.Add(new(entries.Count + 1, id, metadata.Title, new OrfOnAddress("video", id).Url, metadata.DurationSeconds, metadata.ThumbnailUrl, !Blocked(item), ExpectedArchiveIdentity: metadata.ArchiveIdentity, CollectionMemberId: id));
                }
                if (page == pages) break;
            }
            if (entries.Count != total) throw new InvalidDataException("ORF ON omitted series episodes.");
            return Inventory("orf:on", address.Id, Title(parent), entries, request);
        }
        async Task<PageMetadata> Current(MediaHttp controlled, CancellationToken token)
        {
            using var verification = controlled.CreateVerificationSession(request.GeoVerification?.Proxy); var client = await Client.Create(verification, token).ConfigureAwait(false);
            var row = await client.Read("episode/" + address.Id, token).ConfigureAwait(false); Validate(row, "episode/" + address.Id, address.Id); var metadata = Metadata(row);
            if (Text(row, "video_type") != "episode") throw new InvalidDataException("ORF ON returned another media type.");
            if (Blocked(row)) return metadata with { DrmRefused = True(row, "is_drm_protected"), RefusalReason = "ORF ON has not granted clear public playback.", Warnings = ["ORF ON requires another region, time, age or entitlement context."] };
            if (!True(row, "is_gapless")) throw new NotSupportedException("This ORF ON episode does not declare a complete gapless source.");
            if (await MusicCatalog.RecordedAsync(request, metadata, token).ConfigureAwait(false)) return metadata;
            var formats = new List<MediaCandidate>(); var ids = new HashSet<string>(StringComparer.Ordinal); var sources = Object(row, "sources");
            foreach (var (name, kind, suffix) in new[] { ("hls", MediaKind.Hls, ".m3u8"), ("dash", MediaKind.Dash, ".mpd") })
            foreach (var source in Rows(sources, name, 32))
            {
                if (True(source, "is_drm_protected")) continue;
                var quality = Text(source, "quality_key"); if (quality is null || !Match(quality, @"\AQ[0-9A-Z]{2}\z").Success || !ids.Add(name + "-" + quality)) throw new InvalidDataException("ORF ON returned ambiguous format identities.");
                var url = Secure(Text(source, "src"), "apasfiis.sf.apa.at");
                if (!url.AbsolutePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) || !Match(url.AbsolutePath, "_" + address.Id + @"__").Success) throw new InvalidDataException("ORF ON returned another episode's source.");
                formats.Add(new() { Id = name + "-" + quality, Url = url, Kind = kind, Extension = "mp4", HasAudio = true, HasVideo = true, Source = Info.Id, Label = Text(source, "quality_description"), VerificationDemuxer = "mov", VerificationDurationSeconds = metadata.DurationSeconds,
                    ProviderQualityOrder = quality switch { "Q1A" => 10, "Q4A" => 40, "Q6A" => 60, "Q8C" => 80, "QXA" => 90, "QXB" => 91, _ => 0 } });
            }
            if (formats.Count == 0) throw new NotSupportedException("ORF ON offered no supported complete clear HLS or DASH source.");
            var captions = new List<SubtitleTrack>(); var subtitle = Object(Object(row, "_embedded"), "subtitle");
            foreach (var extension in new[] { "vtt", "srt", "ttml" })
            if (Text(subtitle, extension + "_url") is { } raw)
            {
                var url = Secure(raw, "api-tvthek.orf.at"); if (!url.AbsolutePath.StartsWith("/assets/subtitles/", StringComparison.Ordinal) || !url.AbsolutePath.EndsWith("." + extension, StringComparison.Ordinal)) throw new InvalidDataException("ORF ON returned an undeclared subtitle resource.");
                captions.Add(new() { Url = url, Extension = extension, Language = "de", Automatic = True(row, "ai_subtitles"), ResourceIdentity = Numeric(subtitle) + ":" + extension });
            }
            return metadata with { Media = formats, Subtitles = captions };
        }
        return await http.SessionState(Key(Info.Id, request), () => new ProviderPlaybackSession(http, request, Info.Id, Current, reorderedFormats: false)).ReadAsync(ct).ConfigureAwait(false);
    }
    private static bool Blocked(JsonElement row) => True(row, "is_drm_protected") || True(row, "has_active_youth_protection") || True(row, "is_geo_ip_blocked") || True(row, "requiresLogin") || Date(Text(row, "livedate")) is { } start && start > DateTimeOffset.UtcNow || Date(Text(row, "killdate")) is { } end && end <= DateTimeOffset.UtcNow;
    private static void Validate(JsonElement row, string route, string id)
    {
        if (Numeric(row) != id || Secure(Text(Object(Object(row, "_links"), "self"), "href"), "api-tvthek.orf.at").AbsoluteUri != Api + route) throw new InvalidDataException("ORF ON returned another resource identity.");
    }
    private static PageMetadata Metadata(JsonElement row)
    {
        var id = Numeric(row); var duration = Number(row, "exact_duration") / 1000 ?? Number(row, "duration_seconds"); if (duration is not (> 0 and <= 604800)) throw new InvalidDataException("ORF ON omitted the complete episode duration.");
        var date = Date(Text(row, "date"));
        return new() { Id = id, Extractor = "orf:on", ArchiveIdentity = new("orf:on", id), Title = Title(row), Description = Text(row, "description"), DurationSeconds = duration, Uploader = Text(row, "profile_title") ?? "ORF", UploadTimestamp = date,
            UploadDate = date is null ? null : DateOnly.FromDateTime(date.Value.UtcDateTime), AnonymousPlayback = true, SourcePageUrl = new OrfOnAddress("video", id).Url };
    }
    private sealed class Client(MediaHttp http, string authorization)
    {
        internal static async Task<Client> Create(MediaHttp http, CancellationToken ct)
        {
            var html = await http.GetStringAsync(new("https://on.orf.at/"), 4 * 1024 * 1024, ct).ConfigureAwait(false);
            var config = Match(html, "api:\\{baseUrl:(\"[^\"\\r\\n]{1,160}\"),user:(\"[^\"\\r\\n]{1,128}\"),password:(\"[^\"\\r\\n]{1,256}\")");
            if (!config.Success || JsonSerializer.Deserialize<string>(config.Groups[1].Value) != Api) throw new InvalidDataException("ORF ON did not issue its supported public API client configuration.");
            var user = JsonSerializer.Deserialize<string>(config.Groups[2].Value)!; var password = JsonSerializer.Deserialize<string>(config.Groups[3].Value)!;
            if (user.Contains(':') || user.Any(char.IsControl) || password.Any(char.IsControl)) throw new InvalidDataException("ORF ON returned invalid public client fields.");
            return new(http, Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password)));
        }
        internal async Task<JsonElement> Read(string route, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Api + route); request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authorization);
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false); response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await http.ReadBoundedBytesAsync(response, 8 * 1024 * 1024, ct).ConfigureAwait(false)); return json.RootElement.Clone();
        }
    }
}
