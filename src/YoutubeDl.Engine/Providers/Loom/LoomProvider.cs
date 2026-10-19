using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Loom;

public sealed class LoomProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("loom", "Loom public shared and embedded recordings", 200);
    public bool CanHandle(Uri url) => LoomAddress.Matches(url);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = LoomAddress.Parse(request.Url);
        var headers = new Dictionary<string, string> { ["Referer"] = string.IsNullOrWhiteSpace(request.Referer) ? address.Page.AbsoluteUri : request.Referer,
            ["User-Agent"] = string.IsNullOrWhiteSpace(request.UserAgent) ? MediaHttp.DefaultUserAgent : request.UserAgent };
        using var session = http.WithHeaders(headers);
        var document = await session.GetDocumentAsync(address.Page, 4 * 1024 * 1024, ct).ConfigureAwait(false);
        if (LoomAddress.Parse(document.Url).Id != address.Id) throw new InvalidDataException("Loom redirected to another recording.");
        using var state = ReadState(document.Text); var root = state.RootElement; var queries = Object(root, "ROOT_QUERY");
        var refs = queries.ValueKind == JsonValueKind.Object ? queries.EnumerateObject().Where(property => CallId(property.Name, "getVideo", "id") == address.Id)
            .Select(property => Text(property.Value, "__ref")).Distinct().ToArray() : [];
        if (refs.Length != 1 || refs[0] is null || !root.TryGetProperty(refs[0]!, out var video)) throw new NotSupportedException("Loom did not expose one supported recording metadata record.");
        if (Text(video, "__typename") != "RegularUserVideo" || Text(video, "id") != address.Id) throw new InvalidDataException("Loom returned another video identity or an unsupported access state.");
        if (True(video, "archived") || !True(video, "complete") || True(video, "is_protected") || True(video, "needs_password") || True(video, "viewerNeedsPermission") || Text(video, "privacy") != "public")
            throw new NotSupportedException("This Loom recording is unavailable, incomplete or requires additional native account/password support.");
        var properties = Object(video, "video_properties"); var duration = Number(video, "playable_duration") ?? Number(properties, "duration");
        if (duration is not > 0 || duration > 7 * 24 * 3600) throw new InvalidDataException("Loom omitted a supported recording duration.");
        var sourceDuration = Number(video, "source_duration"); var trimDuration = Number(properties, "trim_duration");
        if (sourceDuration is { } original && Math.Abs(original - duration.Value) > 0.1 || trimDuration is { } trim && Math.Abs(trim - duration.Value) > 0.1 || Array(properties, "liveRewindTrimmedSections").Length > 0)
            throw new NotSupportedException("This Loom recording needs edit-timeline composition before its complete playback can be preserved.");
        var media = new List<MediaCandidate>(); var scopes = new List<ScopedMediaQuery>();
        foreach (var property in video.EnumerateObject().Where(property => property.Name.StartsWith("nullableRawCdnUrl(", StringComparison.Ordinal)))
        {
            if (property.Value.ValueKind == JsonValueKind.Null) continue;
            var value = property.Value; var url = MediaUrl(Text(value, "url"), address.Id); var kind = PageMediaExtractor.Classify(url, null);
            if (kind is not (MediaKind.Hls or MediaKind.Dash or MediaKind.Progressive)) continue;
            if (kind is MediaKind.Hls or MediaKind.Dash) AddScope(url, address.Id, scopes);
            media.Add(Candidate(url, kind, kind == MediaKind.Hls ? "hls" : kind == MediaKind.Dash ? "dash" : "progressive"));
        }
        // The public player exposes this read-only operation. It also serves older
        // recordings whose page does not preload a modern HLS/DASH playback URL.
        var query = "query NativeLoomMedia($id: ID!) { getVideo(id: $id, password: null) { __typename ... on RegularUserVideo { id nullableRawCdnUrl(acceptableMimes: [MP4], password: null) { __typename url } } } }";
        var body = new { operationName = "NativeLoomMedia", variables = new { id = address.Id }, query };
        var response = await session.PostJsonAsync(new("https://www.loom.com/graphql"), body, 1024 * 1024, new Dictionary<string, string> { ["Origin"] = "https://www.loom.com" }, ct).ConfigureAwait(false);
        using var api = JsonDocument.Parse(response, new() { MaxDepth = 32 });
        if (Array(api.RootElement, "errors").Length > 0) throw new NotSupportedException("Loom refused its public media lookup. This player/API layout needs further support.");
        var result = Object(Object(api.RootElement, "data"), "getVideo");
        if (Text(result, "id") != address.Id || Text(result, "__typename") != "RegularUserVideo") throw new InvalidDataException("Loom returned another recording's media lookup.");
        var raw = Object(result, "nullableRawCdnUrl");
        if (Text(raw, "url") is { } offered)
        {
            if (Text(raw, "__typename") != "CloudfrontSignedUrlPayload") throw new NotSupportedException("This Loom media authorization payload needs further native support.");
            var url = MediaUrl(offered, address.Id);
            if (PageMediaExtractor.Classify(url, null) != MediaKind.Progressive || !url.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Loom's MP4 lookup returned an unsupported resource type.");
            media.Add(Candidate(url, MediaKind.Progressive, "mp4"));
        }
        if (media.Count == 0) throw new NotSupportedException("Loom exposed no supported complete playback format.");
        var owner = Object(video, "owner"); var ownerRef = Text(owner, "__ref");
        if (ownerRef is not null && root.TryGetProperty(ownerRef, out var ownerValue)) owner = ownerValue;
        var thumbnails = Object(video, "thumbnails"); var thumbnail = Image(Text(thumbnails, "full") ?? Text(thumbnails, "default") ?? Text(thumbnails, "ogFull"), address.Id);
        return new() { Id = address.Id, Extractor = Info.Id, ArchiveIdentity = new("loom", address.Id), Title = Text(video, "name") ?? address.Id, Description = Text(video, "description"),
            Uploader = Text(owner, "display_name") ?? Text(owner, "first_name"), DurationSeconds = duration, ThumbnailUrl = thumbnail,
            Media = media.DistinctBy(candidate => candidate.Url).ToArray(), Subtitles = True(video, "hasTranscriptRecord") ? Captions(root, queries, address.Id) : [], Chapters = Chapters(video, duration.Value), SourcePageUrl = address.Page,
            AnonymousPlayback = true, PlaybackHeaders = headers, PlaybackQueries = scopes };

        MediaCandidate Candidate(Uri url, MediaKind kind, string id) => new() { Id = id, Url = url, Kind = kind, Extension = kind == MediaKind.Progressive ? "mp4" : kind == MediaKind.Hls ? "ts" : "mp4",
            Height = kind == MediaKind.Progressive ? Integer(properties, "height") : null, Width = kind == MediaKind.Progressive ? Integer(properties, "width") : null,
            HasVideo = true, HasAudio = null, IsCompleteMedia = true, Source = "loom", Label = id,
            VerificationDemuxer = kind == MediaKind.Progressive ? "mov" : kind == MediaKind.Hls ? "mpegts" : null, VerificationDurationSeconds = duration,
            NormalizeHlsTransportContinuity = kind == MediaKind.Hls };
    }
    internal static JsonDocument ReadState(string html)
    {
        var matches = Regex.Matches(html, @"window\.__APOLLO_STATE__\s*=\s*(?=\{)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        if (matches.Count != 1) throw new NotSupportedException("Loom did not expose one supported player metadata document.");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(html[matches[0].Index..]); var offset = System.Array.IndexOf(bytes, (byte)'{');
            var reader = new Utf8JsonReader(bytes.AsSpan(offset), new JsonReaderOptions { MaxDepth = 48 }); return JsonDocument.ParseValue(ref reader);
        }
        catch (JsonException) { throw new InvalidDataException("Loom returned invalid player metadata JSON."); }
    }
    private static string? CallId(string key, string name, string argument)
    {
        if (!key.StartsWith(name + "(", StringComparison.Ordinal) || !key.EndsWith(')')) return null;
        try { using var json = JsonDocument.Parse(key[(name.Length + 1)..^1]); return Text(json.RootElement, argument); }
        catch (JsonException) { throw new InvalidDataException("Loom player metadata contains malformed operation arguments."); }
    }
    private static Uri MediaUrl(string? value, string id)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length > 0 || url.Fragment.Length > 0)
            throw new InvalidDataException("Loom returned an unsupported media address.");
        if (url.Host == "luna.loom.com" && Regex.IsMatch(url.AbsolutePath, @"\A/id/" + id + @"/rev/[a-f0-9]{32,128}/resource/", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) return url;
        if (url.Host == "cdn.loom.com" && Regex.IsMatch(url.AbsolutePath, @"\A/sessions/(?:raw|transcoded)/" + id + @"(?:[-_/][A-Za-z0-9_-]+)?\.mp4\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) return url;
        throw new InvalidDataException("Loom media belongs to an unsupported origin, revision or recording identity.");
    }
    private static void AddScope(Uri url, string id, List<ScopedMediaQuery> scopes)
    {
        if (url.Host != "luna.loom.com") throw new NotSupportedException("This Loom adaptive media origin needs further support.");
        var marker = "/resource/"; var index = url.AbsolutePath.IndexOf(marker, StringComparison.Ordinal);
        var prefix = new Uri(url.GetLeftPart(UriPartial.Authority) + url.AbsolutePath[..(index + marker.Length)]);
        var parameters = url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Where(pair => pair.Split('=', 2)[0] is "Policy" or "Signature" or "Key-Pair-Id").ToArray();
        if (parameters.Length != 3) throw new InvalidDataException("Loom did not supply all adaptive playback query credentials.");
        scopes.Add(new(prefix, parameters));
    }
    private static string? Image(string? value, string id) => Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == "https" && url.Host == "cdn.loom.com" && url.UserInfo.Length == 0 &&
        url.AbsolutePath.Contains(id, StringComparison.Ordinal) ? url.AbsoluteUri : null;
    private static IReadOnlyList<SubtitleTrack> Captions(JsonElement root, JsonElement queries, string id)
    {
        var matches = queries.EnumerateObject().Where(property => CallId(property.Name, "fetchVideoTranscript", "videoId") == id).ToArray();
        if (matches.Length == 0) return [];
        if (matches.Length == 1 && Text(matches[0].Value, "__typename") == "VideoTranscriptError") return [];
        if (matches.Length != 1 || Text(matches[0].Value, "__ref") is not { } key || !root.TryGetProperty(key, out var transcript) || Text(transcript, "video_id") != id)
            throw new InvalidDataException("Loom returned another recording's transcript identity.");
        if (Text(transcript, "transcription_status") != "success" || Text(transcript, "captions_source_url") is not { } offered) return [];
        if (!Uri.TryCreate(offered, UriKind.Absolute, out var url) || url.Scheme != "https" || url.Host != "cdn.loom.com" || url.UserInfo.Length > 0 ||
            !url.AbsolutePath.StartsWith("/mediametadata/captions/" + id + "-", StringComparison.Ordinal) || !url.AbsolutePath.EndsWith(".vtt", StringComparison.Ordinal))
            throw new InvalidDataException("Loom returned an unrelated caption resource.");
        var language = Text(transcript, "captionsTranslatedLanguage") ?? Text(transcript, "language") ?? "und";
        return [new() { Url = url, Language = language, Extension = "vtt", Automatic = Text(transcript, "processing_service") is "rev_ai_async" or "assembly_ai" }];
    }
    private static IReadOnlyList<ChapterMarker> Chapters(JsonElement video, double duration)
    {
        var points = Array(video, "chapters"); if (points.Length == 0) return [];
        // Unknown chapter field/layouts must not become invented timestamps.
        throw new NotSupportedException("Loom chapter timelines need additional native field/layout verification.");
    }
}
