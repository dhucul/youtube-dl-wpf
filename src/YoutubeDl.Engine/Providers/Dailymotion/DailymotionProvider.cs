using System.Globalization;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Dailymotion;

public sealed class DailymotionProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("dailymotion", "Dailymotion public player media and playlists", 200);
    public bool CanHandle(Uri url) => DailymotionAddress.IsHost(url);
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)
    {
        var address = DailymotionAddress.Parse(request.Url);
        return address.VideoId is { } id && (address.PlaylistId is null || !request.DownloadPlaylist && !request.ExpandPlaylist)
            ? new("dailymotion", id) : null;
    }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = DailymotionAddress.Parse(request.Url);
        if (address.PlaylistId is not null && (address.VideoId is null || request.ExpandPlaylist))
            return await PlaylistAsync(address.PlaylistId, request.MaximumPlaylistEntries, http, ct).ConfigureAwait(false);
        var json = await http.GetStringAsync(new Uri("https://www.dailymotion.com/player/metadata/video/" + address.VideoId), 4 * 1024 * 1024, ct).ConfigureAwait(false);
        return ParseMetadata(json, address.VideoId!);
    }
    public static PageMetadata ParseMetadata(string json, string expectedId)
    {
        using var document = Parse(json);
        var root = document.RootElement;
        if (Object(root, "error").ValueKind == JsonValueKind.Object)
            throw new InvalidOperationException("Dailymotion: " + (Text(Object(root, "error"), "title") ?? Text(Object(root, "error"), "message") ?? "Playback is unavailable."));
        if (Text(root, "id") != expectedId) throw new InvalidDataException("The Dailymotion response belongs to a different video.");
        var qualities = Object(root, "qualities");
        var media = new List<MediaCandidate>();
        if (qualities.ValueKind == JsonValueKind.Object)
            foreach (var quality in qualities.EnumerateObject())
                foreach (var descriptor in Array(qualities, quality.Name))
                {
                    if (media.Count == 1000) throw new InvalidDataException("Dailymotion returned too many formats.");
                    if (Text(descriptor, "url") is not { } raw) continue;
                    var url = Resource(raw);
                    var mime = Text(descriptor, "type");
                    var kind = PageMediaExtractor.Classify(url, mime);
                    if (kind == MediaKind.Unknown) continue;
                    if (True(descriptor, "drm") || Text(descriptor, "drm_type") is not null) continue;
                    var height = int.TryParse(quality.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : Integer(descriptor, "height");
                    var codecs = mime?.Contains("codecs=", StringComparison.OrdinalIgnoreCase) == true ? mime[(mime.IndexOf("codecs=", StringComparison.OrdinalIgnoreCase) + 7)..].Trim(' ', '"').Split(',', StringSplitOptions.TrimEntries) : [];
                    media.Add(new()
                    {
                        Id = quality.Name + "-" + kind.ToString().ToLowerInvariant() + "-" + media.Count, Url = url, Kind = kind, Source = "dailymotion",
                        Extension = PageMediaExtractor.ExtensionFrom(url, mime), MimeType = mime, Height = height, Width = Integer(descriptor, "width"),
                        FramesPerSecond = Number(descriptor, "fps"), Bandwidth = Number(descriptor, "bitrate"), VideoCodec = codecs.FirstOrDefault(), AudioCodec = codecs.Skip(1).FirstOrDefault(),
                        HasAudio = kind == MediaKind.Progressive ? true : null, HasVideo = true, Label = quality.Name
                    });
                }
        var thumbnails = Object(root, "thumbnails");
        var thumbnail = thumbnails.ValueKind == JsonValueKind.Object ? thumbnails.EnumerateObject().Where(item => item.Value.ValueKind == JsonValueKind.String)
            .OrderByDescending(item => int.TryParse(item.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var size) ? size : 0).Select(item => item.Value.GetString()).FirstOrDefault() : null;
        var captions = ReadCaptions(Object(root, "subtitles"));
        return new()
        {
            Id = expectedId, Title = Text(root, "title") ?? expectedId, Extractor = "dailymotion", Description = Text(root, "description"),
            DurationSeconds = Number(root, "duration"), Uploader = Text(Object(root, "owner"), "screenname") ?? Text(Object(root, "owner"), "username"),
            ThumbnailUrl = thumbnail is null ? null : Resource(thumbnail).AbsoluteUri, IsLive = Text(root, "stream_type") == "live", Media = media, Subtitles = captions,
            Thumbnails = thumbnails.ValueKind == JsonValueKind.Object ? thumbnails.EnumerateObject().Where(image => image.Value.ValueKind == JsonValueKind.String)
                .Select(image => new ThumbnailCandidate { Id = image.Name, Url = Resource(image.Value.GetString()!), Height = int.TryParse(image.Name, out var height) && height > 0 ? height : null }).ToArray() : [],
            Warnings = media.Count == 0 ? ["Dailymotion did not expose native downloadable media. Additional access or protected playback may be required."] : []
        };
    }
    private static IReadOnlyList<SubtitleTrack> ReadCaptions(JsonElement subtitles)
    {
        var tracks = new List<SubtitleTrack>();
        if (subtitles.ValueKind != JsonValueKind.Object || !subtitles.TryGetProperty("data", out var data)) return tracks;
        if (data.ValueKind == JsonValueKind.Array) foreach (var item in data.EnumerateArray()) Add(item, Text(item, "language"));
        else if (data.ValueKind == JsonValueKind.Object) foreach (var item in data.EnumerateObject()) Add(item.Value, item.Name);
        return tracks;
        void Add(JsonElement track, string? language)
        {
            if (tracks.Count >= 256) throw new InvalidDataException("Dailymotion returned too many caption tracks.");
            var raw = Text(track, "url") ?? Array(track, "urls").FirstOrDefault(item => item.ValueKind == JsonValueKind.String).GetStringOrNull();
            if (raw is null || language is null) return;
            var url = Resource(raw);
            var extension = Path.GetExtension(url.AbsolutePath).TrimStart('.').ToLowerInvariant();
            tracks.Add(new() { Url = url, Language = language, Automatic = True(track, "automatic"), Extension = extension is "vtt" or "srt" or "ass" or "ssa" or "ttml" ? extension : "vtt" });
        }
    }
    private static async Task<PageMetadata> PlaylistAsync(string id, int maximumEntries, MediaHttp http, CancellationToken ct)
    {
        if (maximumEntries is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        using var detail = Parse(await http.GetStringAsync(new Uri("https://api.dailymotion.com/playlist/" + id + "?fields=id,name"), 4 * 1024 * 1024, ct).ConfigureAwait(false));
        if (Text(detail.RootElement, "id") != id) throw new InvalidDataException("Dailymotion returned a different playlist.");
        var entries = new List<NativePlaylistEntry>();
        var page = 1;
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            using var response = Parse(await http.GetStringAsync(new Uri("https://api.dailymotion.com/playlist/" + id + "/videos?fields=id,title,url,duration,thumbnail_url&page=" + page + "&limit=100"), 4 * 1024 * 1024, ct).ConfigureAwait(false));
            var root = response.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("The Dailymotion playlist response is missing its item list.");
            if (Integer(root, "page") is { } returnedPage && returnedPage != page) throw new InvalidDataException("Dailymotion returned the wrong playlist page.");
            var items = list.EnumerateArray().ToArray();
            if (items.Length > 100 || entries.Count + items.Length > maximumEntries) throw new InvalidDataException("The playlist exceeds its configured limit; it was not truncated silently.");
            var signature = string.Join("\n", items.Select(item => Text(item, "id") ?? "unavailable"));
            if (items.Length > 0 && !signatures.Add(signature)) throw new InvalidDataException("Dailymotion repeated a playlist page.");
            foreach (var item in items)
            {
                var video = Text(item, "id");
                if (video is not null && !DailymotionAddress.IsId(video)) throw new InvalidDataException("The playlist contains an invalid video ID.");
                entries.Add(new(entries.Count + 1, video, Text(item, "title") ?? "Unavailable video", video is null ? null : new Uri("https://www.dailymotion.com/video/" + video),
                    Number(item, "duration"), Text(item, "thumbnail_url"), video is not null));
            }
            if (!True(root, "has_more")) break;
            if (items.Length == 0 || entries.Count >= maximumEntries || page++ >= 10000) throw new InvalidDataException("Dailymotion did not complete the playlist within its configured limit.");
        }
        return new() { Id = id, PlaylistId = id, IsPlaylist = true, Extractor = "dailymotion", Title = Text(detail.RootElement, "name") ?? id, Entries = entries };
    }
    private static JsonDocument Parse(string json) => Encoding.UTF8.GetByteCount(json) <= 4 * 1024 * 1024 ? JsonDocument.Parse(json) : throw new InvalidDataException("The Dailymotion response exceeds the extraction limit.");
    private static Uri Resource(string value) => Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" && string.IsNullOrEmpty(url.UserInfo)
        ? url : throw new InvalidDataException("Dailymotion returned an invalid media resource.");
}

internal static class DailymotionJsonExtensions
{
    public static string? GetStringOrNull(this JsonElement element) => element.ValueKind == JsonValueKind.String ? element.GetString() : null;
}
