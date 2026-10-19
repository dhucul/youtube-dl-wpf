using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Rutube;

public sealed partial class RutubeProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("rutube", "Rutube public videos, embeds and collections", 200);
    public bool CanHandle(Uri url) => RutubeAddress.Host(url);
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)
    { var address = RutubeAddress.Parse(request.Url); return address.Collection ? null : new("rutube", address.Id); }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = RutubeAddress.Parse(request.Url);
        if (address.Collection) return await InventoryAsync(address, request, http, ct).ConfigureAwait(false);
        return await http.SessionState("playback:rutube:" + address.Id, () => new ProviderPlaybackSession(http, request, "rutube",
            (controlled, token) => VideoAsync(address, controlled, token), reorderedFormats: false)).ReadAsync(ct).ConfigureAwait(false);
    }
    private static async Task<PageMetadata> VideoAsync(RutubeAddress address, MediaHttp http, CancellationToken ct)
    {
        var metadata = await ApiAsync(http, "/api/video/" + address.Id + "/", ct).ConfigureAwait(false);
        ValidateVideo(metadata, address.Id);
        var page = Metadata(metadata);
        if (Unavailable(metadata) is { } reason) return page with { Warnings = [reason] };
        if (True(metadata, "is_livestream") || True(metadata, "is_on_air")) throw new NotSupportedException("Rutube live playback requires additional native provider coverage.");
        var options = await ApiAsync(http, "/api/play/options/" + address.Id + "/", ct).ConfigureAwait(false);
        if (Text(options, "video_id") != address.Id || Text(options, "effective_video") is { } effective && effective != address.Id ||
            Long(options, "id") is { } track && Long(metadata, "track_id") is { } declared && track != declared)
            throw new InvalidDataException("Rutube playback changed the selected video identity.");
        if (!True(Object(options, "acl_access"), "allowed")) throw new NotSupportedException("Rutube did not grant playback access to this session.");
        if (Present(options, "drm_token") || Present(options, "drm") || Present(options, "key_systems"))
            return page with { DrmRefused = true, RefusalReason = "Protected Rutube playback is not supported by the native provider." };
        if (True(options, "is_hidden")) throw new NotSupportedException("This Rutube playback requires another access contract.");
        var balancer = Object(options, "video_balancer");
        if (balancer.ValueKind != JsonValueKind.Object || balancer.EnumerateObject().Count() > 32) throw new InvalidDataException("Rutube did not provide a bounded media inventory.");
        var media = new List<MediaCandidate>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in balancer.EnumerateObject())
        {
            ct.ThrowIfCancellationRequested();
            if (!Regex.IsMatch(source.Name, @"\A[A-Za-z0-9_-]{1,64}\z") || source.Value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Rutube returned an invalid media descriptor.");
            var url = Resource(source.Value.GetString());
            var kind = PageMediaExtractor.Classify(url, null); var extension = PageMediaExtractor.ExtensionFrom(url, null);
            if (kind is not (MediaKind.Hls or MediaKind.Dash) && !(kind == MediaKind.Progressive && extension is "mp4" or "webm")) continue;
            if (!seen.Add(url.AbsoluteUri)) continue;
            var audio = True(metadata, "is_audio");
            if (kind == MediaKind.Hls)
            {
                var master = http.AllowAutoRedirect ? await http.GetDocumentAsync(url, 8 * 1024 * 1024, ct).ConfigureAwait(false) :
                    await http.ReadPlaybackManifestAsync(url, MediaHost, "https://rutube.ru/", ct).ConfigureAwait(false);
                if (!MediaHost(master.Url)) throw new InvalidDataException("Rutube redirected its HLS manifest outside the media scope.");
                var playlist = Download.HlsPlaylistParser.Parse(master.Text, master.Url);
                if (playlist.UnsupportedFeatures.Count > 0) throw new NotSupportedException("This Rutube HLS manifest uses an unsupported feature.");
                if (playlist.IsMaster)
                {
                    if (playlist.AudioRenditions.Count != 0 || playlist.SubtitleRenditions.Count != 0 || playlist.Variants.Count is < 1 or > 100 ||
                        playlist.Variants.Any(variant => variant.AudioGroup is not null || variant.SubtitleGroup is not null || variant.ClosedCaptionGroup is not (null or "NONE")))
                        throw new NotSupportedException("This Rutube master requires additional rendition-group coverage.");
                    foreach (var group in playlist.Variants.GroupBy(variant => JsonSerializer.Serialize(new object?[] { variant.Height, variant.Bandwidth, variant.VideoCodec, variant.FramesPerSecond })))
                    {
                        var selected = group.First(); var alternatives = group.ToArray();
                        foreach (var variant in alternatives)
                            if (!MediaHost(variant.Url) || variant.Url.AbsolutePath != selected.Url.AbsolutePath || variant.Url.Query != selected.Url.Query)
                                throw new InvalidDataException("Rutube offered ambiguous equal-fact HLS resources rather than equivalent CDN mirrors.");
                        media.Add(new() { Id = source.Name + "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(group.Key)))[..16],
                            Url = selected.Url, Kind = MediaKind.Hls, Extension = "m3u8", Source = "rutube", HasVideo = !audio, HasAudio = true,
                            Height = selected.Height, Bandwidth = selected.Bandwidth, VideoCodec = selected.VideoCodec, FramesPerSecond = selected.FramesPerSecond,
                            ResourceExpires = RenewableMediaAuthorization.Expiry(url) });
                    }
                    continue;
                }
            }
            media.Add(new() { Id = source.Name, Url = url, Kind = kind, Extension = extension, Source = "rutube", HasVideo = !audio, HasAudio = true });
        }
        return page with { Media = media, Warnings = (media.Count == 0 ? new[] { "Rutube did not expose supported clear playback resources." } : [])
            .Concat(Array(options, "captions").Length > 0 ? ["Rutube caption conversion requires additional provider coverage."] : []).ToArray() };
    }
    private static PageMetadata Metadata(JsonElement item)
    {
        var id = VideoId(item); DateTimeOffset? timestamp = DateTimeOffset.TryParse(Text(item, "publication_ts"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
        return new() { Id = id, Extractor = "rutube", Title = Text(item, "title") ?? id, Description = Text(item, "description"),
            DurationSeconds = Number(item, "duration"), Uploader = Text(Object(item, "author"), "name"), ViewCount = Long(item, "hits"),
            AgeLimit = Integer(Object(item, "pg_rating"), "age"), UploadTimestamp = timestamp, UploadDate = timestamp is { } date ? DateOnly.FromDateTime(date.UtcDateTime) : null,
            ThumbnailUrl = Text(item, "thumbnail_url") is { Length: > 0 } thumb ? Resource(thumb).AbsoluteUri : null, SourcePageUrl = new("https://rutube.ru/video/" + id + "/") };
    }
    private static string VideoId(JsonElement item) => Text(item, "id") is { } id && Regex.IsMatch(id, @"\A[0-9a-f]{32}\z") ? id : throw new InvalidDataException("Rutube returned an invalid video identity.");
    private static void ValidateVideo(JsonElement item, string id)
    {
        if (VideoId(item) != id || !Uri.TryCreate(Text(item, "video_url"), UriKind.Absolute, out var uri)) throw new InvalidDataException("Rutube returned another or incomplete video identity.");
        var parsed = RutubeAddress.Parse(uri);
        if (parsed.Collection || parsed.Id != id) throw new InvalidDataException("Rutube returned another video permalink.");
    }
    private static string? Unavailable(JsonElement item) => True(item, "is_hidden") || True(item, "is_deleted") || True(item, "is_locked") ||
        True(item, "is_paid") || True(item, "is_club") || Integer(Object(item, "action_reason"), "id") is > 0 ? "This Rutube video is unavailable or requires an access contract not supplied here." : null;
    private static bool Present(JsonElement item, string name) => item.TryGetProperty(name, out var field) && field.ValueKind is not (JsonValueKind.Null or JsonValueKind.False) &&
        !(field.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(field.GetString()));
    internal static bool MediaHost(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        new[] { "rutube.ru", "rtbcdn.ru", "rutubelist.ru" }.Any(host => uri.Host == host || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
    private static Uri Resource(string? text) => Uri.TryCreate(text, UriKind.Absolute, out var uri) && MediaHost(uri) ? uri : throw new InvalidDataException("Rutube returned a resource outside its supported HTTPS media scope.");
    private static async Task<JsonElement> ApiAsync(MediaHttp http, string path, CancellationToken ct)
    {
        var url = new Uri("https://rutube.ru" + path);
        var document = await http.GetDocumentAsync(url, 8 * 1024 * 1024, ct, new Dictionary<string, string> { ["Referer"] = "https://rutube.ru/" }).ConfigureAwait(false);
        if (document.Url != url) throw new InvalidDataException("Rutube changed the selected API resource.");
        using var json = JsonDocument.Parse(document.Text);
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Rutube returned an invalid API object.");
        return json.RootElement.Clone();
    }
}
