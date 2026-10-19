using System.Net;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.PeerTube;

public sealed class PeerTubeProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("peertube", "PeerTube public videos and playlists", 190);
    public NativeAuthenticationCapabilities AuthenticationCapabilities => NativeAuthenticationCapabilities.UserPassword | NativeAuthenticationCapabilities.TwoFactor | NativeAuthenticationCapabilities.VideoPassword;
    public bool CanHandle(Uri url) => PeerTubeAddress.CanHandle(url);
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)
    {
        var address=PeerTubeAddress.Parse(request.Url);
        return !address.IsPlaylist&&Guid.TryParseExact(address.Identifier,"D",out var id) ? new("peertube",id.ToString("D")) : null;
    }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = PeerTubeAddress.Parse(request.Url);
        PeerTubeSession? session = request.Authentication.IsConfigured ? http.SessionState("peertube:"+address.Instance.AbsoluteUri,()=>new PeerTubeSession(http,address.Instance,request.Authentication)) : null;
        Func<Uri,long,CancellationToken,Task<string>> read = session is null ? http.GetStringAsync : session.ReadAsync;
        if (address.IsPlaylist) return await PlaylistAsync(address, request.MaximumPlaylistEntries, read, ct).ConfigureAwait(false);
        var endpoint = new Uri(address.Instance, "api/v1/videos/" + Uri.EscapeDataString(address.Identifier));
        var json = await read(endpoint, 4 * 1024 * 1024, ct).ConfigureAwait(false);
        var page = ParseVideo(json, address);
        using var record = Parse(json);
        var privacy = Integer(Object(record.RootElement,"privacy"),"id");
        if (privacy is >=3 || privacy is null && request.Authentication.VideoPassword.Length>0)
        {
            session ??= http.SessionState("peertube:"+address.Instance.AbsoluteUri,()=>new PeerTubeSession(http,address.Instance,request.Authentication));
            read=session.ReadAsync;
            page=page with { PlaybackAuthorization=await session.PlaybackAuthorizationAsync(page,ct).ConfigureAwait(false) };
        }
        var captions = await OptionalAsync(read, new Uri(endpoint.AbsoluteUri + "/captions"), ct).ConfigureAwait(false);
        var chapters = await OptionalAsync(read, new Uri(endpoint.AbsoluteUri + "/chapters"), ct).ConfigureAwait(false);
        return page with
        {
            Subtitles = captions is null ? [] : ParseCaptions(captions, address.Instance),
            Chapters = chapters is null ? [] : ParseChapters(chapters, page.DurationSeconds)
        };
    }
    public static PageMetadata ParseVideo(string json, PeerTubeAddress address)
    {
        using var document = Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !Guid.TryParseExact(Text(root, "uuid"), "D", out var videoUuid) || videoUuid == Guid.Empty) throw new InvalidDataException("The server did not return a PeerTube video record.");
        var id = Text(root, "uuid")!;
        if (!id.Equals(address.Identifier, StringComparison.OrdinalIgnoreCase) && Text(root, "shortUUID") != address.Identifier && Long(root, "id")?.ToString(System.Globalization.CultureInfo.InvariantCulture) != address.Identifier)
            throw new InvalidDataException("The PeerTube response belongs to a different video.");
        var media = new List<MediaCandidate>();
        var formats = new List<NativeFormat>();
        foreach (var file in Array(root, "files")) AddFile(file, "web", fragmented: false);
        foreach (var playlist in Array(root, "streamingPlaylists"))
        {
            if (Integer(playlist, "type") != 1) continue;
            foreach (var file in Array(playlist, "files")) AddFile(file, "hls", fragmented: true);
            if (Text(playlist, "playlistUrl") is { } raw)
            {
                if (media.Count >= 1000) throw new InvalidDataException("The PeerTube video returned too many formats.");
                var manifestId = "hls-master-" + media.Count;
                var url = Resource(address.Instance, raw);
                media.Add(new() { Id = manifestId, Url = url, Kind = MediaKind.Hls, Extension = "m3u8", Source = "peertube", Label = "HLS master" });
                formats.Add(new() { Id = manifestId, Url = url, Extension = "m3u8", Protocol = "m3u8", Note = "HLS master" });
            }
        }
        return new PageMetadata
        {
            Id = id, Extractor = "peertube", Title = Text(root, "name") ?? id, DurationSeconds = Number(root, "duration"), IsLive = True(root, "isLive"),
            Description = Text(root, "description"), Uploader = Text(Object(root, "account"), "displayName") ?? Text(Object(root, "account"), "name"),
            ThumbnailUrl = Text(root, "thumbnailPath") is { } thumbnail ? Resource(address.Instance, thumbnail).AbsoluteUri : null,
            Thumbnails = new[] { "thumbnailPath", "previewPath" }.Where(key => Text(root, key) is { Length: > 0 })
                .Select(key => new ThumbnailCandidate { Id = key == "thumbnailPath" ? "thumbnail" : "preview", Url = Resource(address.Instance, Text(root, key)!) }).ToArray(),
            Media = media, ReportedFormats = formats, Warnings = media.Count == 0 ? ["PeerTube did not expose media files; the video may be unavailable or still processing."] : []
        };

        void AddFile(JsonElement file, string prefix, bool fragmented)
        {
            if (media.Count >= 1000) throw new InvalidDataException("The PeerTube video returned too many formats.");
            var raw = Text(file, "fileUrl") ?? Text(file, "fileDownloadUrl");
            if (raw is null) return;
            var url = Resource(address.Instance, raw);
            var resolution = Object(file, "resolution");
            var audio = Boolean(file, "hasVideo") == false || Integer(resolution, "id") == 0;
            var formatId = prefix + "-" + (Long(file, "id")?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? media.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var extension = PageMediaExtractor.ExtensionFrom(url) ?? (audio ? "m4a" : "mp4");
            if (audio && extension == "mp4") extension = "m4a";
            var candidate = new MediaCandidate
            {
                Id = formatId, Url = url, Kind = MediaKind.Progressive, Extension = extension, Source = "peertube",
                Label = Text(resolution, "label") + (fragmented ? " (fragmented MP4)" : ""), Height = audio ? null : Integer(file, "height") ?? Integer(resolution, "id"),
                Width = Integer(file, "width"), HasVideo = Boolean(file, "hasVideo") ?? !audio, HasAudio = Boolean(file, "hasAudio"), MimeType = audio ? "audio/" + (extension == "m4a" ? "mp4" : extension) : "video/" + extension,
                VerificationDemuxer = Integer(Object(root,"privacy"),"id") is >=3 ? extension switch { "mp4" or "m4a" or "mov"=>"mov","webm" or "mkv"=>"matroska","mp3"=>"mp3","ogg" or "opus"=>"ogg",_=>throw new NotSupportedException("This private PeerTube media container needs additional verification support.") } : null,
                VerificationFileSize = Integer(Object(root,"privacy"),"id") is >=3 && Long(file,"size") is >0 ? Long(file,"size") : null,
                VerificationMinimumDurationSeconds = Integer(Object(root,"privacy"),"id") is >=3 && !True(root,"isLive") ? Number(root,"duration") : null,
                FramesPerSecond = Number(file, "fps"), FileSize = Long(file, "size")
            };
            media.Add(candidate);
            formats.Add(new()
            {
                Id = formatId, Url = url, Extension = extension, Width = candidate.Width, Height = candidate.Height, Note = candidate.Label,
                AudioOnly = audio, HasAudio = candidate.HasAudio, HasVideo = candidate.HasVideo, FileSize = Long(file, "size"), FramesPerSecond = Number(file, "fps"), Protocol = url.Scheme
            });
        }
    }
    public static IReadOnlyList<SubtitleTrack> ParseCaptions(string json, Uri instance)
    {
        using var document = Parse(json);
        RequireArray(document.RootElement, "data");
        var tracks = Array(document.RootElement, "data");
        if (tracks.Length > 256) throw new InvalidDataException("PeerTube returned too many caption tracks.");
        return tracks.Where(track => Text(track, "fileUrl") is not null || Text(track, "captionPath") is not null).Select(track => new SubtitleTrack
        {
            Url = Resource(instance, Text(track, "fileUrl") ?? Text(track, "captionPath")!), Language = Text(Object(track, "language"), "id") ?? "und",
            Extension = "vtt", Automatic = True(track, "automaticallyGenerated")
        }).ToArray();
    }
    public static IReadOnlyList<ChapterMarker> ParseChapters(string json, double? duration)
    {
        using var document = Parse(json);
        RequireArray(document.RootElement, "chapters");
        var entries = Array(document.RootElement, "chapters");
        if (entries.Length > 1000) throw new InvalidDataException("PeerTube returned too many chapters.");
        if (duration is null or <= 0 || entries.Length == 0) return [];
        var chapters = entries.Select((entry, index) => new ChapterMarker
        {
            Title = Text(entry, "title") ?? "", StartSeconds = Number(entry, "timecode") ?? throw new InvalidDataException("The PeerTube chapter is missing its start time."),
            EndSeconds = index + 1 < entries.Length ? Number(entries[index + 1], "timecode") ?? throw new InvalidDataException("The PeerTube chapter is missing its end time.") : duration.Value
        }).ToArray();
        Media.ChapterMetadataWriter.Render(chapters);
        return chapters;
    }
    private static async Task<PageMetadata> PlaylistAsync(PeerTubeAddress address, int maximumEntries, Func<Uri,long,CancellationToken,Task<string>> read, CancellationToken ct)
    {
        if (maximumEntries is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        var endpoint = new Uri(address.Instance, "api/v1/video-playlists/" + Uri.EscapeDataString(address.Identifier));
        using var details = Parse(await read(endpoint, 4 * 1024 * 1024, ct).ConfigureAwait(false));
        var root = details.RootElement;
        if (Text(root, "uuid")?.Equals(address.Identifier, StringComparison.OrdinalIgnoreCase) != true && Text(root, "shortUUID") != address.Identifier &&
            Long(root, "id")?.ToString(System.Globalization.CultureInfo.InvariantCulture) != address.Identifier) throw new InvalidDataException("The PeerTube response belongs to a different playlist.");
        var entries = new List<NativePlaylistEntry>();
        int? total = null;
        while (total is null || entries.Count < total)
        {
            ct.ThrowIfCancellationRequested();
            using var page = Parse(await read(new Uri(endpoint.AbsoluteUri + "/videos?start=" + entries.Count + "&count=100"), 4 * 1024 * 1024, ct).ConfigureAwait(false));
            RequireArray(page.RootElement, "data");
            var declared = Integer(page.RootElement, "total") ?? throw new InvalidDataException("The PeerTube playlist is missing its item count.");
            if (declared > maximumEntries || total is not null && total != declared) throw new InvalidDataException("The PeerTube playlist exceeds its limit or changed during enumeration.");
            total = declared;
            var items = Array(page.RootElement, "data");
            if (items.Length > 100 || items.Length == 0 && entries.Count != total) throw new InvalidDataException("The PeerTube playlist ended before all declared items were received.");
            foreach (var item in items)
            {
                var video = Object(item, "video");
                var id = Text(video, "uuid");
                var title = Text(video, "name") ?? "Unavailable video";
                var position = Integer(item, "position") ?? entries.Count + 1;
                if (position < 1 || entries.Any(entry => entry.Position == position)) throw new InvalidDataException("The PeerTube playlist returned duplicate or invalid positions.");
                var available = id is not null && Guid.TryParseExact(id, "D", out _);
                var url = available ? new Uri(address.Instance, "videos/watch/" + id) : null;
                PageMetadata? refusal = null;
                if (Number(item, "startTimestamp") is > 0 || Number(item, "stopTimestamp") is > 0)
                    refusal = new() { Id = id, Title = title, Warnings = ["Native playback ranges for PeerTube playlist clips are not implemented yet."] };
                entries.Add(new(position, id, title, url, Number(video, "duration"), Text(video, "thumbnailPath") is { } thumbnail ? Resource(address.Instance, thumbnail).AbsoluteUri : null, available, refusal));
                if (entries.Count > total) throw new InvalidDataException("PeerTube returned more playlist entries than it declared.");
            }
        }
        return new() { Id = Text(root, "uuid"), PlaylistId = Text(root, "uuid"), IsPlaylist = true, Extractor = "peertube", Title = Text(root, "displayName") ?? address.Identifier,
            Description = Text(root, "description"), Entries = entries.OrderBy(entry => entry.Position).ToArray() };
    }
    private static async Task<string?> OptionalAsync(Func<Uri,long,CancellationToken,Task<string>> read, Uri endpoint, CancellationToken ct)
    {
        try { return await read(endpoint, 2 * 1024 * 1024, ct).ConfigureAwait(false); }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }
    private static JsonDocument Parse(string json) => Encoding.UTF8.GetByteCount(json) <= 4 * 1024 * 1024 ? JsonDocument.Parse(json) : throw new InvalidDataException("The PeerTube response exceeds the extraction limit.");
    private static void RequireArray(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The PeerTube response is missing its " + name + " array.");
    }
    private static bool? Boolean(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
    private static Uri Resource(Uri instance, string raw) => Uri.TryCreate(instance, raw, out var url) && url.Scheme is "http" or "https" && string.IsNullOrEmpty(url.UserInfo)
        ? url : throw new InvalidDataException("PeerTube returned an invalid resource URL.");
}
