using System.Text.Json;
using System.Net;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Rumble;

public sealed partial class RumbleProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("rumble", "Rumble public videos, embeds and creator listings", 200);
    public bool CanHandle(Uri url)
    {
        try { if (RumbleCollectionAddress.IsCollection(url)) _ = RumbleCollectionAddress.Parse(url); else _ = RumbleAddress.Parse(url); return true; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidDataException) { return false; }
    }
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)
    { if (RumbleCollectionAddress.IsCollection(request.Url)) return null; var address = RumbleAddress.Parse(request.Url); return address.Embed ? new("rumble", address.Id) : null; }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        if (RumbleCollectionAddress.IsCollection(request.Url)) return await ReadCollectionAsync(request, RumbleCollectionAddress.Parse(request.Url), http, ct).ConfigureAwait(false);
        var address = RumbleAddress.Parse(request.Url); var id = address.Id; string? description = null;
        var headers = new Dictionary<string, string> { ["User-Agent"] = string.IsNullOrWhiteSpace(request.UserAgent) ? "YoutubeDlNative/" + NativeEngine.Version : request.UserAgent,
            ["Referer"] = string.IsNullOrWhiteSpace(request.Referer) ? request.Url.AbsoluteUri : request.Referer };
        using var session = http.WithHeaders(headers);
        // Visiting the public player establishes the same anonymous HTTP session
        // used for metadata and preserves page-level availability declarations.
        {
            var page = await session.GetDocumentAsync(request.Url, 8 * 1024 * 1024, ct).ConfigureAwait(false);
            var final = RumbleAddress.Parse(page.Url);
            if (final.Embed != address.Embed || final.Id != address.Id) throw new InvalidDataException("Rumble redirected to a different video.");
            if (!address.Embed && RumblePage.Meta(page.Text, "og:url") is { } canonical && RumbleAddress.Parse(new Uri(page.Url, canonical)).Id != address.Id)
                throw new InvalidDataException("Rumble returned a different canonical watch identity.");
            var players = RumblePage.Players(page.Text);
            var ids = players.Select(RumblePage.PlayerId).Distinct(StringComparer.Ordinal).ToArray();
            if (ids.Length != 1) throw new NotSupportedException("Rumble did not expose one unambiguous public player for this watch page.");
            if (address.Embed && ids[0] != address.Id) throw new InvalidDataException("Rumble returned another embedded player identity.");
            foreach (var player in players)
            {
                if (RegionBlocked(player))
                    throw new NotSupportedException("Rumble reports that this video is not available in the current region.");
            }
            id = ids[0]; description = RumblePage.Meta(page.Text, "og:description") ?? RumblePage.Meta(page.Text, "description");
        }
        var endpoint = new Uri("https://rumble.com/embedJS/u4/?ifr=0&dref=&request=video&ver=2&v=" + id);
        var document = await session.GetDocumentAsync(endpoint, 4 * 1024 * 1024, ct).ConfigureAwait(false);
        if (document.Url != endpoint) throw new InvalidDataException("Rumble redirected playback metadata away from the requested video.");
        using var json = JsonDocument.Parse(document.Text); var root = json.RootElement;
        if (Long(root, "vid") != RumbleAddress.NumericId(id)) throw new InvalidDataException("Rumble returned another playback identity.");
        if (Integer(root, "loaded") != 1 || Integer(root, "live") != 0 || Active(root, "live_placeholder"))
            throw new NotSupportedException("Rumble has not exposed this video as a complete supported on-demand upload.");
        if (new[] { "restricted", "regionBlocked", "is_preview", "is_premium", "premium_only", "drm", "key_systems" }.Any(name => Active(root, name)))
            throw new NotSupportedException("Restricted, premium, preview or protected Rumble playback requires additional native support.");
        if (root.TryGetProperty("restrict", out var restrict) && restrict.ValueKind is not (JsonValueKind.Null or JsonValueKind.False))
        {
            // The current public player explicitly allows restriction modes <= -3.
            // Domain allow/deny lists require a separate verified embedding workflow.
            if (restrict.ValueKind != JsonValueKind.Array || restrict.GetArrayLength() != 2 || restrict[0].ValueKind != JsonValueKind.Number ||
                !restrict[0].TryGetInt32(out var mode) || mode > -3)
                throw new NotSupportedException("Rumble requires a restricted embedding or access workflow for this video.");
        }
        if (root.TryGetProperty("timeline", out var timeline) && timeline.ValueKind != JsonValueKind.Null &&
            (timeline.ValueKind != JsonValueKind.Array || timeline.GetArrayLength() != 2 ||
             timeline.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var offset) || offset != 0)))
            throw new NotSupportedException("Rumble playback with source trim offsets requires additional native timeline support.");
        var duration = Number(root, "duration");
        if (duration is not > 0) throw new InvalidDataException("Rumble did not provide a complete on-demand duration.");
        Uri source = RumbleAddress.EmbedUrl(id);
        if (Text(root, "l") is { Length: > 0 } link)
        {
            source = new Uri(new Uri("https://rumble.com/"), link); var declared = RumbleAddress.Parse(source);
            if (declared.Embed || !address.Embed && declared.Id != address.Id) throw new InvalidDataException("Rumble playback belongs to another watch page.");
        }
        else if (!address.Embed) throw new InvalidDataException("Rumble omitted the watch identity needed to bind this player.");
        var media = new List<MediaCandidate>(); var warnings = new List<string>();
        var groups = Object(root, "ua");
        if (groups.ValueKind != JsonValueKind.Object || groups.EnumerateObject().Count() > 32) throw new NotSupportedException("Rumble did not expose supported playback resources.");
        var formats = new HashSet<string>(StringComparer.Ordinal); var protectedSeen = false;
        foreach (var group in groups.EnumerateObject())
        {
            if (group.Name is "timeline" or "tar") continue;
            if (group.Name is not ("mp4" or "webm" or "hls")) { warnings.Add("An offered Rumble media type requires additional native support."); continue; }
            if (group.Value.ValueKind != JsonValueKind.Object || group.Value.EnumerateObject().Count() > 256) throw new InvalidDataException("Rumble returned an invalid rendition group.");
            foreach (var rendition in group.Value.EnumerateObject())
            {
                ct.ThrowIfCancellationRequested();
                if (!Regex.IsMatch(rendition.Name, @"\A[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant)) throw new InvalidDataException("Rumble returned an invalid rendition identifier.");
                if (Active(rendition.Value, "drm") || Active(rendition.Value, "key_systems")) { protectedSeen = true; continue; }
                var raw = Text(rendition.Value, "url"); if (string.IsNullOrWhiteSpace(raw)) continue;
                var url = Resource(raw); var meta = Object(rendition.Value, "meta"); var hls = group.Name == "hls";
                if (!hls && Path.GetExtension(url.AbsolutePath).TrimStart('.').ToLowerInvariant() != group.Name)
                    throw new InvalidDataException("Rumble returned a different progressive media container.");
                var formatId = group.Name + "-" + rendition.Name;
                if (!formats.Add(formatId) || formats.Count > 512) throw new InvalidDataException("Rumble repeated or exceeded its rendition inventory.");
                media.Add(new() { Id = formatId, Url = url, Kind = hls ? MediaKind.Hls : MediaKind.Progressive, Extension = hls ? "m3u8" : group.Name,
                    HasVideo = true, IsCompleteMedia = !hls, Source = "rumble", MimeType = hls ? "application/vnd.apple.mpegurl" : "video/" + group.Name,
                    Width = Integer(meta, "w") is > 0 and var width ? width : null, Height = Integer(meta, "h") is > 0 and var height ? height : null,
                    FramesPerSecond = Number(meta, "fps") is > 0 and var fps ? fps : null, Bandwidth = Number(meta, "bitrate") is > 0 and var bitrate ? bitrate * 1000 : null,
                    FileSize = !hls && Long(meta, "size") is > 0 and var size ? size : null,
                    VerificationDemuxer = hls ? "mpegts" : group.Name == "mp4" ? "mov" : "matroska", VerificationDurationSeconds = duration });
            }
        }
        if (Array(root, "cc").Length > 0) warnings.Add("Rumble-specific caption mapping requires additional native support.");
        if (protectedSeen) warnings.Add("A protected Rumble rendition is unavailable to the native engine.");
        if (media.Count == 0) throw new NotSupportedException("Rumble did not expose a supported complete playback resource; preview timelines and raw archives are not standalone videos.");
        return new() { Id = id, Extractor = Info.Id, Title = WebUtility.HtmlDecode(Text(root, "title") ?? id), Description = description, Uploader = Text(Object(root, "author"), "name"),
            DurationSeconds = duration, ThumbnailUrl = Text(root, "i") is { Length: > 0 } image ? Resource(image).AbsoluteUri : null,
            Media = media, Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray(), SourcePageUrl = source, PlaybackHeaders = headers };
    }
    internal static bool RegionBlocked(JsonElement player) => Active(player, "region_blocked") || Active(Object(player, "opts"), "region_blocked") ||
        Array(player, "opts").Any(option => option.ValueKind == JsonValueKind.String && option.GetString() == "region_blocked");
    private static bool Active(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) && field.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.False => false, JsonValueKind.Number => !field.TryGetDecimal(out var number) || number != 0,
        JsonValueKind.String => field.GetString() is not (null or "" or "0" or "false"), JsonValueKind.Array => field.GetArrayLength() > 0,
        JsonValueKind.Object => field.EnumerateObject().Any(), _ => true
    };
    private static Uri Resource(string raw)
    {
        if (raw.StartsWith("//", StringComparison.Ordinal)) raw = "https:" + raw;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0 ||
            !(url.Host == "rumble.com" || url.Host.EndsWith(".rumble.com", StringComparison.Ordinal) || url.Host.EndsWith(".rumble.cloud", StringComparison.Ordinal)))
            throw new InvalidDataException("Rumble returned an invalid or unverified resource origin.");
        return url;
    }
}
