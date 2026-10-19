using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Twitch;

public sealed class TwitchProvider : IMediaProvider
{
    // Public WWW client identifier observed in Twitch's own shipped auth settings on 2026-10-08.
    private const string PublicClientId = "kimne78kx3ncx6brgo4mv6wki5h1ko";
    private const string ClipQuery = """
        query NativeClip($slug:ID!){clip(slug:$slug){id slug title durationSeconds broadcaster{login displayName} thumbnailURL videoQualities{quality frameRate sourceURL} playbackAccessToken(params:{platform:"web",playerBackend:"mediaplayer",playerType:"clips"}){signature value}}}
        """;
    private const string ListQuery = """
        query NativeClipList($login:String!,$after:Cursor){user(login:$login){id login displayName clips(first:50,after:$after,criteria:{period:ALL_TIME}){pageInfo{hasNextPage}edges{cursor node{id slug title durationSeconds thumbnailURL}}}}}
        """;
    public MediaProviderInfo Info { get; } = new("twitch:clips", "Twitch public clips and channel clip listings", 200);
    public bool CanHandle(Uri url)
    {
        if (!TwitchAddress.IsHost(url)) return false;
        try { _ = TwitchAddress.Parse(url); return true; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return false; }
    }
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => TwitchAddress.Parse(request.Url).Clip is { } slug ? new("native:twitch:clips", slug) : null;

    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = TwitchAddress.Parse(request.Url);
        using var controlled = http.AllowAutoRedirect ? http.CreateWithoutRedirects() : null;
        http = controlled ?? http;
        if (address.Clip is null) return await ListAsync(address, request, http, ct).ConfigureAwait(false);
        var data = await QueryAsync(http, "NativeClip", ClipQuery, new { slug = address.Clip }, ct).ConfigureAwait(false);
        var clip = Object(data, "clip");
        if (clip.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("This Twitch clip is missing or unavailable to the current session.");
        if (Text(clip, "slug") != address.Clip || !IsId(Text(clip, "id"))) throw new InvalidDataException("Twitch returned another clip identity.");
        var page = Metadata(clip, address.Clip);
        var token = Object(clip, "playbackAccessToken"); var value = Text(token, "value"); var signature = Text(token, "signature");
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(signature)) return page with { Warnings = ["Twitch did not grant a playback token for this clip."] };
        if (value.Length > 16384 || signature.Length > 4096 || signature.Any(char.IsControl)) throw new InvalidDataException("The Twitch playback token exceeds its supported bounds.");
        using var authorization = JsonDocument.Parse(value);
        var root = authorization.RootElement;
        if (Text(root, "clip_slug") != address.Clip) throw new InvalidDataException("The Twitch playback token belongs to another clip.");
        var permission = Object(root, "authorization");
        if (permission.ValueKind != JsonValueKind.Object || !permission.TryGetProperty("forbidden", out var forbidden) || forbidden.ValueKind != JsonValueKind.False)
            return page with { Warnings = ["Twitch denied playback of this clip for the current session."] };
        var expiry = Long(root, "expires");
        if (root.TryGetProperty("expires", out _) && expiry is null) throw new InvalidDataException("The Twitch playback token has an invalid expiry timestamp.");
        if (expiry is { } expires && expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            return page with { Warnings = ["Twitch returned an expired playback token. A fresh authorized session is required."] };
        var tokenUri = Text(root, "clip_uri") is { } tokenUrl ? Resource(tokenUrl) : null;
        var qualities = Array(clip, "videoQualities");
        if (qualities.Length > 100) throw new InvalidDataException("Twitch returned too many clip qualities.");
        var media = new List<MediaCandidate>(); var formats = new List<NativeFormat>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var quality in qualities)
        {
            ct.ThrowIfCancellationRequested();
            var raw = Resource(Text(quality, "sourceURL"));
            if (tokenUri is not null && raw.Host != tokenUri.Host) throw new InvalidDataException("The Twitch clip quality and token have different media origins.");
            var kind = PageMediaExtractor.Classify(raw);
            if (kind is not (MediaKind.Progressive or MediaKind.Hls)) throw new NotSupportedException("This Twitch clip media type is not supported.");
            var url = WithToken(raw, signature, value);
            var label = Text(quality, "quality") ?? throw new InvalidDataException("A Twitch quality has no label.");
            if (!Regex.IsMatch(label, @"\A[A-Za-z0-9_-]{1,40}\z")) throw new InvalidDataException("Twitch returned an invalid quality label.");
            int? height = int.TryParse(label, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed is > 0 and <= 16384 ? parsed : null;
            var fps = Number(quality, "frameRate") is > 0 and <= 1000 and var rate ? rate : (double?)null;
            var baseId = (height is null ? label : height + "p") + (fps is > 30 ? fps.Value.ToString(CultureInfo.InvariantCulture) : "");
            var id = baseId; for (var suffix = 2; !ids.Add(id); suffix++) id = baseId + "-" + suffix;
            // The current asset route explicitly declares its video codec; older routes may omit that fact.
            var codec = raw.AbsolutePath.Contains("/avc/", StringComparison.Ordinal) ? "avc1" : raw.AbsolutePath.Contains("/hevc/", StringComparison.Ordinal) ? "hvc1" : null;
            var extension = PageMediaExtractor.ExtensionFrom(raw);
            media.Add(new() { Id = id, Url = url, Kind = kind, Extension = extension, HasVideo = true, VideoCodec = codec, Height = height,
                FramesPerSecond = fps, Source = "twitch:clips", MimeType = kind == MediaKind.Progressive ? "video/mp4" : "application/vnd.apple.mpegurl" });
            formats.Add(new() { Id = id, Url = url, Extension = extension, HasVideo = true, VideoCodec = codec, Height = height, FramesPerSecond = fps,
                Protocol = kind == MediaKind.Hls ? "m3u8" : "https", Note = label });
        }
        return page with { Media = media, ReportedFormats = formats, Warnings = media.Count == 0 ? ["Twitch did not expose playable clip qualities."] : [] };
    }

    private static async Task<PageMetadata> ListAsync(TwitchAddress address, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        if (request.MaximumPlaylistEntries is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(request.MaximumPlaylistEntries));
        var entries = new List<NativePlaylistEntry>(); var slugs = new HashSet<string>(StringComparer.Ordinal); var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? after = null; string? userId = null; string? displayName = null;
        for (var page = 0; ; page++)
        {
            ct.ThrowIfCancellationRequested();
            if (page >= 200) throw new InvalidDataException("The Twitch clip listing exceeds its page limit; it was not truncated.");
            var data = await QueryAsync(http, "NativeClipList", ListQuery.Replace("ALL_TIME", address.Period, StringComparison.Ordinal), new { login = address.Channel, after }, ct).ConfigureAwait(false);
            var user = Object(data, "user"); var id = Text(user, "id");
            if (user.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("This Twitch channel is unavailable.");
            if (!IsId(id) || !string.Equals(Text(user, "login"), address.Channel, StringComparison.OrdinalIgnoreCase) || userId is not null && userId != id)
                throw new InvalidDataException("Twitch changed the clip listing's channel identity.");
            userId = id; displayName ??= Text(user, "displayName") ?? address.Channel;
            var connection = Object(user, "clips"); var edges = Array(connection, "edges"); var info = Object(connection, "pageInfo");
            if (connection.ValueKind != JsonValueKind.Object || !connection.TryGetProperty("edges", out var rawEdges) || rawEdges.ValueKind != JsonValueKind.Array ||
                edges.Length > 50 || info.ValueKind != JsonValueKind.Object || !info.TryGetProperty("hasNextPage", out var hasNext) || hasNext.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Twitch returned an incomplete clip listing page.");
            foreach (var edge in edges)
            {
                if (entries.Count == request.MaximumPlaylistEntries) throw new InvalidDataException("The Twitch clip listing exceeds its item limit; it was not truncated.");
                var clip = Object(edge, "node"); var slug = Text(clip, "slug");
                if (clip.ValueKind != JsonValueKind.Object) { entries.Add(new(entries.Count + 1, null, "Unavailable Twitch clip", null, null, null, false)); continue; }
                if (!TwitchAddress.IsSlug(slug) || !IsId(Text(clip, "id")) || !slugs.Add(slug!)) throw new InvalidDataException("Twitch repeated a clip or returned an invalid clip identifier.");
                var metadata = Metadata(clip, slug!);
                entries.Add(new(entries.Count + 1, slug, metadata.Title, new Uri("https://clips.twitch.tv/" + slug), metadata.DurationSeconds, metadata.ThumbnailUrl, true));
            }
            if (hasNext.ValueKind == JsonValueKind.False) break;
            after = edges.LastOrDefault().ValueKind == JsonValueKind.Object ? Text(edges[^1], "cursor") : null;
            if (string.IsNullOrWhiteSpace(after) || after.Length > 8192 || !cursors.Add(after)) throw new InvalidDataException("Twitch clip pagination did not advance.");
        }
        return new() { Id = userId + ":" + address.Period, PlaylistId = userId + ":" + address.Period, Title = displayName + " clips", Uploader = displayName,
            Extractor = "twitch:clips", IsPlaylist = true, Entries = entries };
    }

    private static PageMetadata Metadata(JsonElement clip, string slug)
    {
        var title = Text(clip, "title") ?? slug;
        if (title.Length > 4096) throw new InvalidDataException("The Twitch clip title exceeds its size limit.");
        var broadcaster = Object(clip, "broadcaster");
        var thumbnail = Text(clip, "thumbnailURL");
        return new() { Id = slug, Title = title, Extractor = "twitch:clips", DurationSeconds = Number(clip, "durationSeconds"),
            Uploader = Text(broadcaster, "displayName") ?? Text(broadcaster, "login"), ThumbnailUrl = thumbnail is null ? null : Resource(thumbnail).AbsoluteUri,
            ArchiveIdentity = new("native:twitch:clips", slug) };
    }
    private static async Task<JsonElement> QueryAsync(MediaHttp http, string operation, string query, object variables, CancellationToken ct)
    {
        var response = await http.PostJsonAsync(new Uri("https://gql.twitch.tv/gql"), new { operationName = operation, query, variables }, 4 * 1024 * 1024,
            new Dictionary<string, string> { ["Client-ID"] = PublicClientId, ["Origin"] = "https://www.twitch.tv", ["Referer"] = "https://www.twitch.tv/" }, ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(response); var root = document.RootElement;
        var errors = Array(root, "errors");
        if (errors.Any(error => Text(Object(error, "extensions"), "code") == "IntegrityCheckFailed") || Text(Object(Object(root, "extensions"), "challenge"), "type") == "integrity")
            throw new NotSupportedException("Twitch requires an integrity challenge that the native provider has not completed. The listing or media was not declared complete.");
        if (errors.Length > 0)
        {
            throw new InvalidOperationException("Twitch rejected the native metadata request.");
        }
        var data = Object(root, "data");
        if (data.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Twitch returned no metadata document.");
        return data.Clone();
    }
    private static bool IsId(string? value) => value is not null && Regex.IsMatch(value, @"\A[1-9][0-9]{0,19}\z");
    private static Uri Resource(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0 ||
            !new[] { ".twitch.tv", ".twitchcdn.net", ".ttvnw.net", ".jtvnw.net", ".cloudfront.net" }.Any(host => url.Host.EndsWith(host, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Twitch returned an invalid media or image address.");
        return url;
    }
    private static Uri WithToken(Uri source, string signature, string token)
    {
        var query = new List<string>();
        foreach (var part in source.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var key = Uri.UnescapeDataString(part.Split('=', 2)[0]);
            if (key is not ("sig" or "token")) query.Add(part);
        }
        query.Add("sig=" + Uri.EscapeDataString(signature)); query.Add("token=" + Uri.EscapeDataString(token));
        return new UriBuilder(source) { Query = string.Join('&', query) }.Uri;
    }
}
