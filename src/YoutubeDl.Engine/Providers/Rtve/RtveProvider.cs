using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Music;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Rtve;

public sealed class RtveProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("rtveaudio", "RTVE and RNE public audio and programmes", 200);
    public bool SupportsGeoVerificationProxy => true;
    public bool CanHandle(Uri url) => RtveAddress.Host(url);
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)
    { var address = RtveAddress.Parse(request.Url); return address.Collection ? null : new("rtve.es:audio", address.Id!); }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        NativeGeoVerification.ValidateProvider(request, this);
        var address = RtveAddress.Parse(request.Url);
        if (address.Collection) return await Inventory(address, request, http, ct).ConfigureAwait(false);
        async Task<PageMetadata> Read(MediaHttp controlled, CancellationToken token)
        {
            using var verification = controlled.CreateVerificationSession(request.GeoVerification?.Proxy);
            var row = Single(await ReadApi(verification, "audios/" + address.Id + ".json", token).ConfigureAwait(false), address.Id!);
            var metadata = Metadata(row);
            if (await MusicCatalog.RecordedAsync(request, metadata, token).ConfigureAwait(false)) return metadata;
            if (Refusal(row) is { } reason) return metadata with { Warnings = [reason], DrmRefused = True(row, "hasDRM"), RefusalReason = reason };
            var qualities = MusicCatalog.Rows(row, "qualities");
            if (qualities.Length is < 1 or > 32) throw new InvalidDataException("RTVE did not declare complete audio qualities.");
            var formats = new List<MediaCandidate>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var quality in qualities)
            {
                var id = MusicCatalog.Numeric(quality, "identifier"); if (!ids.Add(id)) throw new InvalidDataException("RTVE repeated an audio quality identity.");
                var duration = Number(quality, "duration"); var size = Long(quality, "filesize");
                if (duration is not > 0 || Math.Abs(duration.Value / 1000 - metadata.DurationSeconds!.Value) > 1.1 || size is not > 0 || size > 64L * 1024 * 1024 * 1024)
                    throw new InvalidDataException("RTVE supplied incomplete or mismatched full audio metadata.");
                if (Text(quality, "type") is not ("audio/mpeg" or "audio/mp3")) throw new NotSupportedException("This RTVE audio encoding needs additional native verification support.");
                var resource = Resource(Text(quality, "filePath"));
                if (!resource.AbsolutePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("RTVE did not supply a complete MP3 resource.");
                formats.Add(MusicCatalog.Audio(resource, id, "mp3", metadata.DurationSeconds!.Value, Info.Id, size, Number(quality, "bitRate")));
            }
            return metadata with { Media = formats };
        }
        var verificationKey = Download.AdaptiveFragmentCheckpoint.Hash(request.GeoVerification?.Proxy ?? "");
        return await http.SessionState("playback:rtveaudio:" + address.Id + ":" + verificationKey, () => new ProviderPlaybackSession(http, request, Info.Id, Read, reorderedFormats: false)).ReadAsync(ct).ConfigureAwait(false);
    }

    internal static bool MediaHost(Uri url) => url.IsAbsoluteUri && url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 &&
        url.Host is "ztnr.rtve.es" or "rtve-mediavod-lote3.rtve.es";
    private static Uri Resource(string? text) => Uri.TryCreate(text, UriKind.Absolute, out var url) && MediaHost(url) && !url.AbsoluteUri.Contains("_PROVIDER_", StringComparison.Ordinal) && !url.AbsoluteUri.Contains("_TOKEN_", StringComparison.Ordinal) ? url : throw new InvalidDataException("RTVE supplied an unresolved or unapproved audio resource.");
    private static string? Refusal(JsonElement row)
    {
        if (True(row, "hasDRM") || True(row, "drm")) return "RTVE declares protected audio; native DRM playback is unavailable.";
        if (row.TryGetProperty("allowedInCountry", out var allowed) && allowed.ValueKind == JsonValueKind.False || True(row, "geolocalizado") && !True(row, "allowedInCountry")) return "RTVE has not allowed this audio in the verification session's region.";
        if (True(row, "paidContent") || True(row, "paidContentPromo")) return "RTVE requires an entitled account for this audio.";
        if (True(row, "notDownloadable") || True(row, "disabledAlacarta") || Text(Object(row, "pubState"), "code") != "ENPUB") return "RTVE has not published this audio for downloading.";
        return null;
    }
    private static PageMetadata Metadata(JsonElement row)
    {
        var id = MusicCatalog.Numeric(row); var address = RtveAddress.Parse(ResourcePage(Text(row, "htmlUrl")));
        if (address.Collection || address.Id != id || Text(row, "assetType") != "audio" || Text(row, "contentType") != "audio" || Text(row, "consumption") is not ("podcast" or "ondemand"))
            throw new InvalidDataException("RTVE changed the selected recorded audio type or identity.");
        var duration = Number(row, "duration"); if (duration is not (> 0 and <= 604800000)) throw new InvalidDataException("RTVE omitted the complete audio duration.");
        var title = Text(row, "title"); if (string.IsNullOrWhiteSpace(title) || title.Length > 8192) throw new InvalidDataException("RTVE omitted the selected audio title.");
        var stamp = Long(row, "publicationDateTimestamp");
        var date = stamp is >= 0 and <= 253402300799999 ? DateTimeOffset.FromUnixTimeMilliseconds(stamp.Value) : (DateTimeOffset?)null;
        return new() { Id = id, Extractor = "rtve.es:audio", ArchiveIdentity = new("rtve.es:audio", id), Title = WebUtility.HtmlDecode(title), Description = Text(row, "shortDescription") ?? Text(row, "description"),
            DurationSeconds = duration / 1000, Uploader = Text(Object(row, "programInfo"), "title") ?? "RTVE", UploadTimestamp = date, UploadDate = date is null ? null : DateOnly.FromDateTime(date.Value.UtcDateTime),
            ThumbnailUrl = Image(Text(row, "thumbnail")), SourcePageUrl = ResourcePage(Text(row, "htmlUrl")), AnonymousPlayback = true };
    }
    private static Uri ResourcePage(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == "https" && RtveAddress.Host(url) && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Query.Length == 0 && url.Fragment.Length == 0 ? url : throw new InvalidDataException("RTVE omitted its canonical public page.");
    private static string? Image(string? value) => value is null ? null : Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 && url.Host is "img.rtve.es" or "img2.rtve.es" ? value : throw new InvalidDataException("RTVE supplied an unapproved image origin.");

    private static async Task<PageMetadata> Inventory(RtveAddress address, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var id = address.Id;
        if (id is null)
        {
            using var source = http.CreatePublicServiceSession();
            var document = await source.GetDocumentAsync(address.Canonical, 2 * 1024 * 1024, ct).ConfigureAwait(false);
            var ids = Regex.Matches(document.Text, "<meta\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)).Select(match =>
            {
                var attributes = Regex.Matches(match.Value, "(?<key>[A-Za-z.:_-]+)\\s*=\\s*['\"](?<value>[^'\"]*)['\"]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                var values = attributes.ToDictionary(item => item.Groups["key"].Value, item => WebUtility.HtmlDecode(item.Groups["value"].Value), StringComparer.OrdinalIgnoreCase);
                return values.GetValueOrDefault("name") == "DC.identifier" ? RtveAddress.Numeric(values.GetValueOrDefault("content")) : null;
            }).Where(value => value is not null).Distinct(StringComparer.Ordinal).ToArray();
            if (ids.Length != 1) throw new InvalidDataException("RTVE did not bind the radio programme page to one API identity."); id = ids[0];
        }
        using var verification = http.CreateVerificationSession(request.GeoVerification?.Proxy);
        var parent = Single(await ReadApi(verification, "programas/" + id + ".json", ct).ConfigureAwait(false), id!);
        var pageUrl = ResourcePage(Text(parent, "htmlUrl")); var canonical = RtveAddress.Parse(pageUrl);
        if (!canonical.Collection || canonical.Id is not null || address.Slug is not null && canonical.Slug != address.Slug || Text(parent, "contentType") != "programa") throw new InvalidDataException("RTVE changed the selected radio programme identity.");
        var parentRefusal = Refusal(parent);
        var entries = new List<NativePlaylistEntry>(); long? total = null; var pages = 1;
        const int size = 50;
        for (var number = 1; number <= pages; number++)
        {
            var page = await ReadApi(verification, "programas/" + id + "/audios.json?size=50&page=" + number.ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
            var rows = MusicCatalog.Rows(page, "items"); var declared = Long(page, "total"); var count = Integer(page, "totalPages");
            if (declared is null or < 0 or > 10000 || declared > request.MaximumPlaylistEntries || total is not null && declared != total || Integer(page, "number") != number || Integer(page, "size") != size || Long(page, "offset") != entries.Count ||
                Integer(page, "numElements") != rows.Length || rows.Length != Math.Min(size, declared.Value - entries.Count) || count != (declared + size - 1) / size)
                throw new InvalidDataException("RTVE returned a changing, incomplete or oversized programme inventory; no partial list was accepted.");
            total = declared; pages = count!.Value;
            foreach (var row in rows)
            {
                var metadata = Metadata(row);
                entries.Add(MusicCatalog.Entry(metadata, entries.Count + 1, metadata.SourcePageUrl!, parentRefusal is null && Refusal(row) is null));
            }
        }
        if (entries.Count != total) throw new InvalidDataException("RTVE did not return the complete programme inventory.");
        return MusicCatalog.Inventory("rtve.es:program", id!, MusicCatalog.Title(parent), entries, request.Url) with { Description = Text(parent, "description"), ThumbnailUrl = Image(Text(parent, "thumbnail")), Warnings = parentRefusal is null ? [] : [parentRefusal] };
    }
    private static JsonElement Single(JsonElement page, string id)
    {
        var rows = MusicCatalog.Rows(page, "items");
        if (rows.Length != 1 || Long(page, "total") != 1 || Integer(page, "numElements") != 1 || MusicCatalog.Numeric(rows[0]) != id) throw new InvalidDataException("RTVE returned another or incomplete resource identity.");
        return rows[0];
    }
    private static async Task<JsonElement> ReadApi(MediaHttp http, string path, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, "https://api.rtve.es/api/" + path);
        using var response = await http.SendAsync(message, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await http.ReadBoundedBytesAsync(response, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        using var json = JsonDocument.Parse(bytes);
        var page = Object(json.RootElement, "page"); if (page.ValueKind != JsonValueKind.Object) throw new InvalidDataException("RTVE omitted its API page envelope."); return page.Clone();
    }
}
