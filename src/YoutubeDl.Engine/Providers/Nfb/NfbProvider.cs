using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Nfb;

public sealed partial class NfbProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("nfb", "NFB/ONF public films, series and embedded players", 200);
    public bool CanHandle(Uri url)
    {
        try { if (NfbSeriesAddress.IsSeries(url)) _ = NfbSeriesAddress.Parse(url); else _ = NfbAddress.Parse(url); return true; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return false; }
    }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        if (NfbSeriesAddress.IsSeries(request.Url)) return await ReadSeriesAsync(request, http, ct).ConfigureAwait(false);
        var address = NfbAddress.Parse(request.Url); long? expected = null; string? director = null;
        var headers = new Dictionary<string, string> { ["User-Agent"] = string.IsNullOrWhiteSpace(request.UserAgent) ? "YoutubeDlNative/" + NativeEngine.Version : request.UserAgent,
            ["Referer"] = string.IsNullOrWhiteSpace(request.Referer) ? address.Film.AbsoluteUri : request.Referer };
        using var session = http.WithHeaders(headers);
        if (!address.Embed)
        {
            var film = await session.GetDocumentAsync(request.Url, 4 * 1024 * 1024, ct).ConfigureAwait(false);
            if (NfbSeriesAddress.IsSeries(film.Url))
            {
                var episode = NfbSeriesAddress.Parse(film.Url);
                if (episode.Episode != address.Slug) throw new InvalidDataException("NFB redirected to another series episode.");
            }
            else
            {
                var final = NfbAddress.Parse(film.Url);
                if (final.Slug != address.Slug || final.Embed) throw new InvalidDataException("NFB redirected to another film.");
                address = final;
            }
            var ids = Regex.Matches(film.Text, @"\bwindow\.DISPLAYED_WORK_ID\s*=\s*(?<id>[0-9]+)\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                .Select(match => long.TryParse(match.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0).Distinct().ToArray();
            if (ids.Length != 1 || ids[0] <= 0) throw new NotSupportedException("NFB did not expose one supported film identity.");
            expected = ids[0]; director = ReadDirector(film.Text);
        }
        var document = await session.GetDocumentAsync(address.Player, 3 * 1024 * 1024, ct).ConfigureAwait(false);
        var playerAddress = NfbAddress.Parse(document.Url);
        if (!playerAddress.Embed || playerAddress.Slug != address.Slug) throw new InvalidDataException("NFB redirected to another embedded player.");
        using var json = ReadOptions(document.Text); var options = json.RootElement;
        var gtm = Object(options, "gtm"); var stats = Object(options, "unstats"); var overlay = Object(options, "overlay");
        var id = Long(gtm, "registryId");
        if (id is not > 0 || Long(stats, "registryId") != id || expected is not null && expected != id || Text(stats, "slug") != address.Slug)
            throw new InvalidDataException("NFB returned mismatched film and player identities.");
        if (Text(gtm, "versionType") != "film" || Text(stats, "type") != "film" ||
            new[] { "error", "geoblocked", "restricted", "isPreview", "isTrailer", "drm", "keySystems" }.Any(name => Active(options, name)))
            throw new NotSupportedException("NFB did not expose the complete film as supported clear playback.");
        if (Text(overlay, "url") is not { } declared || !Uri.TryCreate(declared, UriKind.Absolute, out var canonical)) throw new InvalidDataException("NFB omitted its canonical film address.");
        var canonicalAddress = NfbAddress.Parse(canonical);
        if (canonicalAddress.Embed || canonicalAddress.Slug != address.Slug || canonicalAddress.Film.Host != address.Film.Host)
            throw new InvalidDataException("NFB playback belongs to another film or language site.");
        var analyticsId = AnalyticsNumber(document.Text, "nfb_version_id");
        if (analyticsId is not null && analyticsId != id) throw new InvalidDataException("NFB returned another analytics film identity.");
        var duration = AnalyticsNumber(document.Text, "nfb_version_duration");
        if (duration is <= 0) throw new InvalidDataException("NFB returned an invalid film duration.");
        var media = new List<MediaCandidate>();
        Add("hls", "source", false); Add("hls-described", "dvSource", true);
        if (media.Count == 0 || media[0].Id != "hls") throw new NotSupportedException("NFB did not provide the complete film's primary playback URL.");
        return new() { Id = id.Value.ToString(CultureInfo.InvariantCulture), Extractor = Info.Id, ArchiveIdentity = new("nfb:registry", id.Value.ToString(CultureInfo.InvariantCulture)),
            Title = WebUtility.HtmlDecode(Text(overlay, "title") ?? Text(gtm, "title") ?? address.Slug), Description = Text(overlay, "description"), Uploader = director,
            DurationSeconds = duration, ThumbnailUrl = Text(options, "poster") is { Length: > 0 } poster ? Image(poster).AbsoluteUri : null,
            Media = media, SourcePageUrl = canonicalAddress.Film, PlaybackHeaders = headers };
        void Add(string formatId, string name, bool fallback)
        {
            if (Text(options, name) is not { Length: > 0 } raw) return;
            var url = Manifest(raw, id.Value);
            if (media.Any(item => item.Url == url)) return;
            media.Add(new() { Id = formatId, Url = url, Kind = MediaKind.Hls, Extension = "m3u8", MimeType = "application/vnd.apple.mpegurl", HasVideo = true,
                Source = "nfb", Label = fallback ? "Described audio" : "Original audio", AudioLanguage = Text(gtm, "versionLang"), IsFallback = fallback,
                // The catalog duration can exclude distribution leaders/credits.
                // The complete HLS timeline supplies the verification duration.
                VerificationDemuxer = "mpegts" });
        }
    }
    private static JsonDocument ReadOptions(string html)
    {
        var matches = Regex.Matches(html, @"\bwindow\.IFRAME_OPTIONS\s*=\s*(?<json>\{)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (matches.Count != 1) throw new NotSupportedException("NFB did not expose one supported player configuration.");
        var start = matches[0].Groups["json"].Index;
        var bytes = Encoding.UTF8.GetBytes(html.Substring(start, Math.Min(html.Length - start, 2 * 1024 * 1024)));
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 32 });
        try { return JsonDocument.ParseValue(ref reader); }
        catch (JsonException) { throw new InvalidDataException("NFB returned an invalid or excessive player configuration."); }
    }
    private static long? AnalyticsNumber(string html, string name)
    {
        var matches = Regex.Matches(html, "[\"']" + Regex.Escape(name) + "[\"']\\s*:\\s*[\"']?(?<value>[0-9]+)[\"']?\\s*(?:,|})", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var values = matches.Select(match => long.TryParse(match.Groups["value"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : -1).Distinct().ToArray();
        if (values.Length > 1 || values.Length == 1 && values[0] < 0) throw new InvalidDataException("NFB returned inconsistent film metadata.");
        return values.Length == 0 ? null : values[0];
    }
    private static string? ReadDirector(string html)
    {
        foreach (Match block in Regex.Matches(html, @"<script\b[^>]*type=['""]application/ld\+json['""][^>]*>(?<json>.*?)</script\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            if (block.Groups["json"].Length > 1024 * 1024) continue;
            try
            {
                using var doc = JsonDocument.Parse(block.Groups["json"].Value);
                if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("director", out var directors)) continue;
                var names = (directors.ValueKind == JsonValueKind.Array ? directors.EnumerateArray().ToArray() : [directors]).Select(item => Text(item, "name")).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().Take(64).ToArray();
                if (names.Length > 0) return string.Join("; ", names);
            }
            catch (JsonException) { }
        }
        return null;
    }
    private static Uri Manifest(string raw, long id)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0 ||
            url.Fragment.Length != 0 && !Regex.IsMatch(url.Fragment, @"\A#expires=[0-9]{1,12}\z", RegexOptions.CultureInvariant) || url.Host != "d3acx5b8mnvbua.cloudfront.net")
            throw new NotSupportedException("NFB returned an unverified playback origin.");
        var parts = url.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 3 || parts[0] != "hls" || !Regex.IsMatch(parts[1], @"\A[0-9]+_" + id.ToString(CultureInfo.InvariantCulture) + @"_[A-Za-z0-9_-]+\z", RegexOptions.CultureInvariant) || parts[2] != parts[1] + ".m3u8")
            throw new InvalidDataException("NFB's playback resource does not match the selected film identity.");
        return url;
    }
    private static Uri Image(string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.Scheme != "https" || url.UserInfo.Length != 0 || !url.IsDefaultPort || url.Host != "assets.nfbcdn.ca")
            throw new InvalidDataException("NFB returned an invalid poster address.");
        return url;
    }
    private static bool Active(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) && field.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.False => false, JsonValueKind.Number => !field.TryGetDecimal(out var number) || number != 0,
        JsonValueKind.String => field.GetString() is not (null or "" or "false" or "0"), JsonValueKind.Array => field.GetArrayLength() > 0,
        JsonValueKind.Object => field.EnumerateObject().Any(), _ => true
    };
}
