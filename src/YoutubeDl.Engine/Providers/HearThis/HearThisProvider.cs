using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.HearThis;

public sealed partial class HearThisProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("hearthisat", "hearthis.at public recordings and complete artist uploads", 200);
    public bool CanHandle(Uri url) => HearThisAddress.Matches(url);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = HearThisAddress.Parse(request.Url); using var anonymous = http.CreateAnonymousSession(); var expected = address.EmbeddedId;
        if (expected is not null) address = await EmbedAsync(address, anonymous, ct).ConfigureAwait(false);
        if (address.Track is null) return await ArtistAsync(address, request, anonymous, ct).ConfigureAwait(false);
        using var data = await ApiAsync(address.Api, anonymous, ct).ConfigureAwait(false); var root = data.RootElement; ValidateTrack(root, address);
        if (expected is not null && Id(root) != expected) throw new InvalidDataException("hearthis.at's player returned another recording identity.");
        return Facts(root, address);
    }
    private static async Task<HearThisAddress> EmbedAsync(HearThisAddress address, MediaHttp http, CancellationToken ct)
    {
        var page = await http.GetDocumentAsync(address.Web, 1024 * 1024, ct).ConfigureAwait(false);
        if (HearThisAddress.Parse(page.Url).EmbeddedId != address.EmbeddedId) throw new InvalidDataException("hearthis.at redirected another player.");
        var html = Regex.Replace(page.Text, @"<!--.*?-->|<script\b[^>]*>.*?</script\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        var tiles = Regex.Matches(html, @"<li\b(?<attributes>[^>]*)>(?<body>.*?)</li\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250))
            .Where(match => Attribute(match.Groups["attributes"].Value, "data-trackid") == address.EmbeddedId).ToArray();
        if (tiles.Length != 1) throw new InvalidDataException("hearthis.at did not expose one requested public player tile.");
        var links = Regex.Matches(tiles[0].Groups["body"].Value, @"<a\b(?<attributes>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250))
            .Select(match => Attribute(match.Groups["attributes"].Value, "data-detail")).Where(value => value is not null).Distinct(StringComparer.Ordinal).ToArray();
        if (links.Length != 1) throw new InvalidDataException("hearthis.at did not expose one canonical player recording.");
        var canonical = HearThisAddress.Parse(PublicUrl(links[0])); if (canonical.Track is null || canonical.EmbeddedId is not null) throw new InvalidDataException("hearthis.at's player did not identify a public track."); return canonical;
    }
    private static string? Attribute(string attributes, string name)
    {
        var matches = Regex.Matches(attributes, @"(?<![\w-])" + Regex.Escape(name) + @"\s*=\s*[""'](?<value>[^""']*)[""']", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        if (matches.Count > 1) throw new InvalidDataException("hearthis.at repeated a player attribute."); return matches.Count == 1 ? WebUtility.HtmlDecode(matches[0].Groups["value"].Value) : null;
    }
    private static void ValidateTrack(JsonElement root, HearThisAddress address)
    {
        var user = Object(root, "user"); _ = Id(root); var owner = Id(root, "user_id");
        if (owner != Id(user) || Text(user, "permalink") != address.Artist || Text(root, "permalink") != address.Track || HearThisAddress.Parse(PublicUrl(Text(root, "permalink_url"))) != address || PublicUrl(Text(root, "uri")) != address.Api)
            throw new InvalidDataException("hearthis.at returned another owner, recording or canonical resource.");
        if (Flag(root, "private", true) || Flag(root, "fan_exclusive_play") || Flag(root, "is_live") || Flag(root, "is_live_video") || !string.IsNullOrEmpty(Text(root, "video_stream")))
            throw new NotSupportedException("This hearthis.at private, fan-gated, live or video workflow needs additional native support.");
        if (Number(root, "duration") is not > 0 or > 604800) throw new InvalidDataException("hearthis.at did not declare a supported complete duration.");
    }
    private static PageMetadata Facts(JsonElement root, HearThisAddress address)
    {
        var id = Id(root); var owner = Id(root, "user_id"); var sources = new List<MediaCandidate>(); var seen = new HashSet<string>(StringComparer.Ordinal); var duration = Number(root, "duration");
        var download = Text(root, "download_url");
        if (Flag(root, "downloadable") && !Flag(root, "fan_exclusive_download") && !string.IsNullOrWhiteSpace(download)) Add(download, "download", Path.GetExtension(Text(root, "download_filename") ?? "").TrimStart('.').ToLowerInvariant(), 80);
        var stream = Text(root, "stream_url"); if (!string.IsNullOrWhiteSpace(stream)) Add(stream, "stream", "mp3", 20);
        if (sources.Count == 0) throw new NotSupportedException("hearthis.at did not offer complete public audio delivery.");
        var pictures = new List<ThumbnailCandidate>();
        foreach (var name in new[] { "thumb", "thumb_hires", "artwork_url", "artwork_url_retina" })
        {
            var raw = Text(root, name); if (string.IsNullOrWhiteSpace(raw)) continue; var url = PublicUrl(raw);
            if (url.Host != "img.hearthis.at" || !url.AbsolutePath.Contains("/uploads/" + owner + "/image_track/" + id + "/", StringComparison.Ordinal)) throw new InvalidDataException("hearthis.at returned artwork outside the requested recording.");
            if (pictures.All(picture => picture.Url != url)) pictures.Add(new() { Id = name, Url = url });
        }
        var uploaded = MetadataFacts.Unix(Long(root, "unix_created_at")); var user = Object(root, "user");
        return new() { Id = id, Extractor = "hearthisat", ArchiveIdentity = new("hearthisat", id), Title = Text(root, "title") ?? address.Track!, Uploader = Text(user, "username"), Description = Text(root, "description"),
            SourcePageUrl = address.Web, DurationSeconds = duration, UploadTimestamp = uploaded, UploadDate = MetadataFacts.Day(uploaded), ViewCount = Nonnegative(Long(root, "playback_count")), ThumbnailUrl = pictures.LastOrDefault()?.Url.AbsoluteUri, Thumbnails = pictures, AnonymousPlayback = true,
            MetadataFields = new Dictionary<string, object?> { ["uploader_id"] = owner, ["like_count"] = Nonnegative(Long(root, "favoritings_count")), ["download_count"] = Nonnegative(Long(root, "download_count")), ["license"] = Text(root, "license") }, Media = sources };
        void Add(string raw, string kind, string extension, int quality)
        {
            var url = PublicUrl(raw); var prefix = "/" + address.Artist + "/" + address.Track + "/";
            if (url.Host is not ("hearthis.app" or "hearthis.at") || !url.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal) || !(url.AbsolutePath == prefix + "listen/" || url.AbsolutePath == prefix + "stream.mp3" || kind == "download" && url.AbsolutePath == prefix + "download/"))
                throw new InvalidDataException("hearthis.at's supplied media gateway does not belong to the requested recording.");
            if (extension is not ("mp3" or "m4a" or "wav" or "ogg" or "flac")) throw new NotSupportedException("This hearthis.at original encoding needs additional native support.");
            if (!seen.Add(url.AbsoluteUri)) return;
            sources.Add(new() { Id = kind, Url = url, Kind = MediaKind.Progressive, Source = "hearthisat", Extension = extension, MimeType = extension == "mp3" ? "audio/mpeg" : extension == "m4a" ? "audio/mp4" : "audio/" + extension,
                HasAudio = true, HasVideo = false, IsCompleteMedia = true, ProviderQualityOrder = quality, VerificationDemuxer = extension == "m4a" ? "mov" : extension, VerificationDurationSeconds = duration });
        }
    }
    private static string Id(JsonElement root, string name = "id") => Text(root, name) is { } value && HearThisAddress.Numeric(value) ? value : throw new InvalidDataException("hearthis.at returned an invalid numeric identity.");
    private static long? Nonnegative(long? value) => value is >= 0 ? value : null;
    private static bool Flag(JsonElement root, string name, bool required = false)
    {
        if (!root.TryGetProperty(name, out var value)) return required ? throw new InvalidDataException("hearthis.at omitted an availability state.") : false;
        return value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False or JsonValueKind.Null => false, JsonValueKind.Number when value.TryGetInt32(out var number) => number switch { 0 => false, 1 => true, _ => throw new InvalidDataException("hearthis.at returned an invalid availability state.") },
            JsonValueKind.String => value.GetString() switch { "0" or "false" or "" => false, "1" or "true" => true, _ => throw new InvalidDataException("hearthis.at returned an invalid availability state.") }, _ => throw new InvalidDataException("hearthis.at returned an invalid availability state.") };
    }
    private static Uri PublicUrl(string? raw) => raw is not null && raw.Length <= 32768 && Uri.TryCreate(raw, UriKind.Absolute, out var url) && url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 ? url : throw new InvalidDataException("hearthis.at returned an invalid public resource.");
    private static async Task<JsonDocument> ApiAsync(Uri url, MediaHttp http, CancellationToken ct)
    {
        var data = await http.GetDocumentAsync(url, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        if (data.Url != url) throw new InvalidDataException("hearthis.at redirected metadata outside the requested resource.");
        return JsonDocument.Parse(data.Text, new JsonDocumentOptions { MaxDepth = 64 });
    }
}
