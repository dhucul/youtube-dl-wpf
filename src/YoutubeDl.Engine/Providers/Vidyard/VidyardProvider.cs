using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Vidyard;

public sealed class VidyardProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("vidyard", "Vidyard public videos and player playlists", 200);
    public static bool IsUuid(string? value) => value is not null && Regex.IsMatch(value, @"\A[A-Za-z0-9_-]{22}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public bool CanHandle(Uri url)
    {
        try { _ = Parse(url); return true; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return false; }
    }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var id = Parse(request.Url);
        foreach (var pair in request.Url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var key = Uri.UnescapeDataString(pair.Split('=')[0]);
            if (key is "vyac" or "chapter" or "video" or "video_index" or "v" or "start" or "end" or "time" or "second")
                throw new NotSupportedException("Vidyard access codes, video-link selection and time offsets require additional native support. Use playlist selection for a public player.");
        }
        if (request.Url.Fragment.Contains("vyac", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("Vidyard access tokens require additional native account support.");
        var endpoint = new Uri("https://play.vidyard.com/player/" + id + ".json");
        using var scoped = http.WithHeaders(string.IsNullOrWhiteSpace(request.Referer) ? new Dictionary<string, string>() : new Dictionary<string, string> { ["referrer"] = request.Referer });
        var document = await scoped.GetDocumentAsync(endpoint, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        if (document.Url != endpoint) throw new InvalidDataException("Vidyard redirected its player metadata to another address.");
        using var json = JsonDocument.Parse(document.Text);
        var root = json.RootElement;
        if (Text(root, "type") != "pomo-player") throw new NotSupportedException("Vidyard requires an unsupported player or access workflow for this address.");
        var payload = Object(root, "payload");
        if (Text(payload, "playerUuid") != id) throw new InvalidDataException("Vidyard returned another player identity.");
        if (Nonempty(payload, "hasAccessCodes") || Nonempty(payload, "whitelistedEmbedDomains"))
            throw new NotSupportedException("Restricted Vidyard players require additional native access verification.");
        if (request.MaximumPlaylistEntries is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(request.MaximumPlaylistEntries));
        if (!payload.TryGetProperty("chapters", out var chapters) || chapters.ValueKind != JsonValueKind.Array || chapters.GetArrayLength() == 0)
            throw new InvalidDataException("Vidyard did not expose a player video list.");
        if (chapters.GetArrayLength() > request.MaximumPlaylistEntries) throw new InvalidDataException("The Vidyard player exceeds its video limit; it was not truncated.");
        var source = new Uri("https://play.vidyard.com/" + id + ".html");
        var seen = new HashSet<long>(); var entries = new List<NativePlaylistEntry>();
        foreach (var chapter in chapters.EnumerateArray())
        {
            var position = entries.Count + 1;
            if (chapter.ValueKind == JsonValueKind.Null) { entries.Add(new(position, null, "Unavailable Vidyard video", null, null, null, false)); continue; }
            var chapterId = Long(chapter, "chapterId");
            if (chapterId is not > 0 || !seen.Add(chapterId.Value)) throw new InvalidDataException("Vidyard repeated or omitted a player video identity.");
            var page = ReadVideo(chapter, source);
            entries.Add(new(position, page.Id, page.Title, source, page.DurationSeconds, page.ThumbnailUrl, page.Media.Count > 0, page));
        }
        if (entries.Count == 1 && entries[0].ResolvedMedia is { } single) return single;
        var metadata = Object(payload, "player");
        return new() { Id = id, PlaylistId = id, IsPlaylist = true, Extractor = "vidyard", Title = Text(payload, "name") ?? id,
            Description = Text(metadata, "description"), Entries = entries, SourcePageUrl = source };
    }
    private static PageMetadata ReadVideo(JsonElement video, Uri source)
    {
        var id = Text(video, "videoUuid");
        if (!IsUuid(id)) throw new InvalidDataException("Vidyard returned an invalid video identity.");
        var media = new List<MediaCandidate>(); var warnings = new List<string>();
        var ready = Text(video, "status") == "ready" && Text(video, "type") == "normal" && !Nonempty(video, "videoJwt");
        var sources = Object(video, "sources"); var formats = new List<(string Kind, JsonElement Value)>();
        if (ready && sources.ValueKind == JsonValueKind.Object)
            foreach (var group in sources.EnumerateObject())
            {
                if (group.Value.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Vidyard returned an invalid source group.");
                foreach (var item in group.Value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Vidyard returned an invalid source descriptor.");
                    if (formats.Count >= 256) throw new InvalidDataException("Vidyard returned too many source formats.");
                    formats.Add((group.Name, item));
                }
            }
        var auto = formats.Any(format => format.Kind == "hls" && Text(format.Value, "profile") == "auto" && Text(format.Value, "url") is not null);
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (kind, value) in formats)
        {
            if (kind is not ("mp4" or "hls") || Text(value, "url") is not { } raw) continue;
            if (Nonempty(value, "drm") || Nonempty(value, "keySystems")) { warnings.Add("A protected Vidyard source is unsupported."); continue; }
            var profile = Text(value, "profile"); var number = Long(value, "id");
            var formatId = kind + "-" + (number?.ToString(CultureInfo.InvariantCulture) ?? (kind == "hls" && profile == "auto" ? "auto" : throw new InvalidDataException("Vidyard omitted a format identity.")));
            if (!identifiers.Add(formatId)) throw new InvalidDataException("Vidyard repeated a format identity.");
            var url = Resource(raw);
            if (kind == "mp4" && !url.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || kind == "hls" && !url.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Vidyard returned an inconsistent media container.");
            media.Add(new() { Id = formatId, Url = url, Kind = kind == "hls" ? MediaKind.Hls : MediaKind.Progressive, Extension = kind == "hls" ? "m3u8" : "mp4",
                MimeType = kind == "hls" ? "application/vnd.apple.mpegurl" : "video/mp4", HasVideo = true, Source = "vidyard", Label = profile,
                Height = Integer(value, "height") is > 0 and var height ? height : null, Width = Integer(value, "width") is > 0 and var width ? width : null,
                FramesPerSecond = Number(value, "framerate") is > 0 and var fps ? fps : null, IsFallback = auto && !(kind == "hls" && profile == "auto") });
        }
        var captions = Array(video, "captions");
        if (captions.Length > 256) throw new InvalidDataException("Vidyard returned too many caption tracks.");
        if (!ready) warnings.Add("This Vidyard video is not available as a supported public upload.");
        else if (media.Count == 0) warnings.Add("Vidyard did not expose supported clear media sources.");
        return new() { Id = id, Extractor = "vidyard", Title = Text(video, "name") ?? id!, Description = Text(video, "description"),
            DurationSeconds = Number(video, "milliseconds") is > 0 and var milliseconds ? milliseconds / 1000 : Number(video, "seconds"),
            ThumbnailUrl = Text(Object(video, "thumbnailUrls"), "normal") is { } thumbnail ? Resource(thumbnail).AbsoluteUri : null,
            Thumbnails = Object(video, "thumbnailUrls") is { ValueKind: JsonValueKind.Object } images ? images.EnumerateObject().Where(image => image.Value.ValueKind == JsonValueKind.String)
                .Select(image => new ThumbnailCandidate { Id = image.Name, Url = Resource(image.Value.GetString()!) }).ToArray() : [],
            Media = media, SourcePageUrl = source, Subtitles = ready ? captions.Where(track => Text(track, "vttUrl") is not null).Select(track => new SubtitleTrack
            { Url = Resource(Text(track, "vttUrl")!), Language = Text(track, "language") ?? "und", Extension = "vtt" }).ToArray() : [],
            PlaybackHeaders = new Dictionary<string, string> { ["Referer"] = source.AbsoluteUri }, Warnings = warnings };
    }
    private static bool Nonempty(JsonElement value, string name) => value.TryGetProperty(name, out var field) && field.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.False => false, JsonValueKind.Object => field.EnumerateObject().Any(), JsonValueKind.Array => field.GetArrayLength() > 0,
        JsonValueKind.String => !string.IsNullOrWhiteSpace(field.GetString()), _ => true
    };
    private static string Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0 ||
            !(url.Host is "play.vidyard.com" or "embed.vidyard.com" or "share.vidyard.com" || url.Host.EndsWith(".hubs.vidyard.com", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Use a Vidyard player or sharing URL.");
        var match = Regex.Match(url.AbsolutePath, @"\A/(?:share/|watch/|player/)?(?<id>[A-Za-z0-9_-]{22})(?:\.(?:html|json)|/type/background)?/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) throw new NotSupportedException("This Vidyard URL shape is not supported yet.");
        return match.Groups["id"].Value;
    }
    private static Uri Resource(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0 ||
            !(url.Host == "vidyard.com" || url.Host.EndsWith(".vidyard.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Vidyard returned an invalid media, caption or image address.");
        return url;
    }
}
