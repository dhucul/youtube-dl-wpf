using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;
using static YoutubeDl.Engine.Providers.PublicProviderApi;

namespace YoutubeDl.Engine.Providers.Arte;

internal sealed record ArteAddress(string Language, string Id, bool Embed = false)
{
    internal bool Collection => Id.StartsWith("RC-", StringComparison.Ordinal);
    internal Uri Url => new("https://www.arte.tv/" + Language + "/videos/" + Id + "/");
    internal static ArteAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Host is not ("www.arte.tv" or "arte.tv" or "api.arte.tv") || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0) throw new ArgumentException("Use an ARTE video, explicit collection or player address.");
        if (url.Host != "api.arte.tv" && Match(url.AbsolutePath, @"\A/player/v[0-9]+/index\.(?:php|html)\z").Success)
        {
            var values = url.Query.TrimStart('?').Split('&').Select(value => value.Split('=', 2)).ToArray();
            var json = values.Where(pair => pair[0] == "json_url").ToArray();
            if (json.Length != 1 || json[0].Length != 2 || values.Any(pair => pair[0] is not ("json_url" or "autostart" or "mute" or "lang"))) throw new ArgumentException("The ARTE embed parameters are unsupported or ambiguous.");
            var target = Secure(Uri.UnescapeDataString(json[0][1]), "api.arte.tv"); var embedded = Parse(target); if (embedded.Collection) throw new NotSupportedException("An ARTE embedded collection needs an explicit collection selection."); return embedded with { Embed = true };
        }
        var pattern = url.Host == "api.arte.tv" ? @"\A/api/player/v2/(?<type>config|playlist)/(?<lang>fr|de|en|es|it|pl)/(?<id>[0-9]{6}-[0-9]{3}-[A-Z]|RC-[0-9]{6})/?\z" : @"\A/(?<lang>fr|de|en|es|it|pl)/videos/(?<id>[0-9]{6}-[0-9]{3}-[A-Z]|RC-[0-9]{6})(?:/[a-zA-Z0-9_-]+)?/?\z";
        var match = Match(url.AbsolutePath, pattern); if (!match.Success || url.Query.Length != 0 && !(url.Host == "api.arte.tv" && url.Query == "?platform=ARTE_NEXT")) throw new NotSupportedException("This ARTE URL route is not supported yet.");
        return new(match.Groups["lang"].Value, match.Groups["id"].Value);
    }
}
public sealed class ArteProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("arte", "ARTE public programmes and explicit collections", 200);
    public bool SupportsGeoVerificationProxy => true;
    public bool CanHandle(Uri url) => url.IsAbsoluteUri && url.Host is "www.arte.tv" or "arte.tv" or "api.arte.tv";
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) { var address = ArteAddress.Parse(request.Url); return address.Collection ? null : new("ArteTV", address.Id + "_" + address.Language); }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        NativeGeoVerification.ValidateProvider(request, this); var address = ArteAddress.Parse(request.Url);
        if (address.Collection) return await Collection(address, request, http, ct).ConfigureAwait(false);
        async Task<PageMetadata> Current(MediaHttp controlled, CancellationToken token)
        {
            using var verification = controlled.CreateVerificationSession(request.GeoVerification?.Proxy); var attributes = await Fetch(verification, address, false, token).ConfigureAwait(false); var metadata = Object(attributes, "metadata"); ValidateIdentity(metadata, address);
            var page = Metadata(metadata, address); var restriction = Object(attributes, "restriction");
            var age = Text(restriction, "ageRestriction");
            if (age is not null && age != "NONE")
            {
                var rating = Match(age, @"\A(?:AGE_|FSK_)?(?<age>[0-9]{1,2})\z");
                if (!rating.Success || !int.TryParse(rating.Groups["age"].Value, out var limit) || limit > 21) throw new NotSupportedException("This ARTE age restriction needs additional native policy support.");
                page = page with { AgeLimit = limit };
            }
            if (True(attributes, "live")) throw new NotSupportedException("ARTE live programme selection needs additional provider support.");
            if (Present(attributes, "error") || True(Object(restriction, "geoblocking"), "restrictedArea")) return page with { Warnings = ["ARTE did not grant playback rights to the current verification session."] };
            if (address.Embed && !True(restriction, "allowEmbed")) return page with { Warnings = ["ARTE has not allowed this programme in an embedded player."] };
            if (Drm(attributes)) return page with { DrmRefused = true, RefusalReason = "ARTE declares protected playback.", Warnings = ["Native ARTE DRM playback is unavailable."] };
            var rights = Object(attributes, "rights"); if (Date(Text(rights, "begin")) is { } rightsBegin && rightsBegin > DateTimeOffset.UtcNow || Date(Text(rights, "end")) is { } rightsEnd && rightsEnd <= DateTimeOffset.UtcNow) return page with { Warnings = ["ARTE has not granted playback in the current rights window."] };
            var streams = Rows(attributes, "streams", 32); var formats = new List<MediaCandidate>();
            for (var index = 0; index < streams.Length; index++)
            {
                var stream = streams[index]; if (Drm(stream)) throw new NotSupportedException("This ARTE stream declares protected playback.");
                var resource = Secure(Text(stream, "url"), "manifest-arte.akamaized.net", "arte-cmafhls.akamaized.net", "arteptweb-vh.akamaihd.net");
                var kind = PageMediaExtractor.Classify(resource); if (kind is not (MediaKind.Hls or MediaKind.Dash)) throw new NotSupportedException("This ARTE transport is unsupported.");
                if (Path.GetFileName(resource.AbsolutePath) != address.Id + (kind == MediaKind.Hls ? ".m3u8" : ".mpd")) throw new InvalidDataException("ARTE returned a manifest for another programme identity.");
                formats.Add(new() { Id = (kind == MediaKind.Hls ? "hls-" : "dash-") + index, Url = resource, Kind = kind, Extension = "mp4", HasVideo = true, HasAudio = true, AudioLanguage = address.Language, Source = Info.Id, VerificationDurationSeconds = page.DurationSeconds });
            }
            if (formats.Count == 0) throw new InvalidDataException("ARTE did not supply a complete playback stream.");
            var chapters = RowsOrEmpty(Object(attributes, "chapters"), "elements"); var markers = new List<ChapterMarker>();
            for (var index = 0; index < chapters.Length; index++)
            {
                var start = Number(chapters[index], "startTime"); var end = index + 1 < chapters.Length ? Number(chapters[index + 1], "startTime") : page.DurationSeconds;
                if (start is null or < 0 || end is null || end <= start || end > page.DurationSeconds) throw new InvalidDataException("ARTE returned invalid chapter boundaries.");
                markers.Add(new() { StartSeconds = start.Value, EndSeconds = end.Value, Title = Title(chapters[index]) });
            }
            return page with { Media = formats, Chapters = markers };
        }
        return await http.SessionState(Key(Info.Id, request), () => new ProviderPlaybackSession(http, request, Info.Id, Current, reorderedFormats: false)).ReadAsync(ct).ConfigureAwait(false);
    }
    private static async Task<PageMetadata> Collection(ArteAddress address, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        using var verification = http.CreateVerificationSession(request.GeoVerification?.Proxy); var root = await Fetch(verification, address, true, ct).ConfigureAwait(false); var metadata = Object(root, "metadata"); ValidateIdentity(metadata, address);
        if (Present(Object(metadata, "config"), "nextPage")) throw new NotSupportedException("This ARTE paginated collection needs additional complete-inventory support.");
        var rows = Rows(root, "items"); Limit(rows.Length, request); var entries = new List<NativePlaylistEntry>();
        foreach (var row in rows)
        {
            var id = Text(row, "providerId"); if (id is null || !Match(id, @"\A[0-9]{6}-[0-9]{3}-[A-Z]\z").Success || Text(row, "language") != address.Language) throw new InvalidDataException("ARTE returned an unsupported collection member identity.");
            var child = new ArteAddress(address.Language, id); var page = Metadata(row, child); var linked = ArteAddress.Parse(Secure(Text(Object(row, "link"), "url"), "www.arte.tv", "arte.tv"));
            if (linked.Id != id || linked.Language != address.Language) throw new InvalidDataException("ARTE changed a collection member page.");
            entries.Add(new(entries.Count + 1, page.Id, page.Title, child.Url, page.DurationSeconds, page.ThumbnailUrl, !Present(row, "error"), ExpectedArchiveIdentity: page.ArchiveIdentity, CollectionMemberId: page.Id));
        }
        return Inventory("ArteTVPlaylist", address.Id + "_" + address.Language, Title(metadata), entries, request);
    }
    private static void ValidateIdentity(JsonElement metadata, ArteAddress expected)
    {
        if (Text(metadata, "providerId") != expected.Id || Text(metadata, "language") != expected.Language) throw new InvalidDataException("ARTE returned another programme or collection edition.");
    }
    private static PageMetadata Metadata(JsonElement metadata, ArteAddress address)
    {
        var duration = Number(Object(metadata, "duration"), "seconds"); if (duration is not (> 0 and <= 604800)) throw new InvalidDataException("ARTE omitted the complete programme duration.");
        var images = RowsOrEmpty(metadata, "images"); var image = images.FirstOrDefault();
        return new() { Id = address.Id + "_" + address.Language, Extractor = "ArteTV", ArchiveIdentity = new("ArteTV", address.Id + "_" + address.Language), Title = Title(metadata) + (Text(metadata, "subtitle") is { Length: > 0 } subtitle ? " - " + subtitle : ""), Description = Text(metadata, "description"), DurationSeconds = duration,
            ThumbnailUrl = Text(image, "url") is { } raw ? Secure(raw, "api-cdn.arte.tv").AbsoluteUri : null, SourcePageUrl = address.Url, AnonymousPlayback = true };
    }
    private static JsonElement[] RowsOrEmpty(JsonElement row, string key) => row.ValueKind != JsonValueKind.Object || !row.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null ? [] : Rows(row, key, 1000);
    private static async Task<JsonElement> Fetch(MediaHttp http, ArteAddress address, bool playlist, CancellationToken ct)
    {
        var root = await Read(http, new("https://api.arte.tv/api/player/v2/" + (playlist ? "playlist/" : "config/") + address.Language + "/" + address.Id), ct).ConfigureAwait(false);
        var data = Object(root, "data"); var attributes = Object(data, "attributes"); if (Text(data, "type") != (playlist ? "Playlist" : "ConfigPlayer") || attributes.ValueKind != JsonValueKind.Object || Text(attributes, "provider") != "arte") throw new InvalidDataException("ARTE omitted its declared player response envelope."); return attributes;
    }
}
