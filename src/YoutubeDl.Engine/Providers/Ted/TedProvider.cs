using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Ted;

public sealed class TedProvider : IMediaProvider
{
    private enum PageKind { Talk, Playlist, Series }
    private sealed record Address(string Key, PageKind Kind, Uri Url);
    public MediaProviderInfo Info { get; } = new("ted", "TED public talks, embeds, playlists and series", 200);
    public bool CanHandle(Uri url)
    {
        try { _ = Parse(url); return true; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return false; }
    }
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)
    {
        var address = Parse(request.Url);
        return address.Kind == PageKind.Talk && Regex.IsMatch(address.Key, @"\A[1-9][0-9]*\z") ? new("ted", address.Key) : null;
    }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = Parse(request.Url);
        var document = await http.GetDocumentAsync(address.Url, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        var final = Parse(document.Url);
        if (final.Kind != address.Kind || address.Kind != PageKind.Talk && final.Key != address.Key) throw new InvalidDataException("TED redirected to a different collection.");
        var matches = Regex.Matches(document.Text, @"<script\b(?=[^>]{0,8192}\bid\s*=\s*['""]__NEXT_DATA__['""])[^>]*>(?<json>[\s\S]*?)</script\s*>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (matches.Count != 1) throw new NotSupportedException("TED did not expose a supported page data document.");
        var json = matches[0].Groups["json"].Value;
        if (Encoding.UTF8.GetByteCount(json) > 6 * 1024 * 1024) throw new InvalidDataException("The TED page data exceeds its size limit.");
        using var parsed = JsonDocument.Parse(json);
        var props = Object(Object(parsed.RootElement, "props"), "pageProps");
        if (address.Kind == PageKind.Playlist) return ReadPlaylist(Object(props, "playlist"), address.Key, request.MaximumPlaylistEntries);
        if (address.Kind == PageKind.Series) return ReadSeries(props, address.Key, request.MaximumPlaylistEntries);
        var video = Object(props, "videoData"); var id = Id(video); var slug = Text(video, "slug");
        var expected = Regex.IsMatch(address.Key, @"\A[1-9][0-9]*\z") ? address.Key : null;
        if (expected is not null ? id != expected : slug != final.Key) throw new InvalidDataException("The TED page belongs to another talk.");
        ValidateCanonical(video, slug!);
        var player = Object(video, "videoPlayerData");
        if (player.ValueKind == JsonValueKind.Object && (Text(player, "slug") is { } playerSlug && playerSlug != slug || Text(player, "canonical") is { } canonical && Parse(Resource(canonical)).Key != slug))
            throw new InvalidDataException("The TED player belongs to another talk.");
        var resources = Object(player, "resources"); var media = new List<MediaCandidate>();
        var hls = Text(video, "hlsUrl") ?? Text(Object(resources, "hls"), "stream");
        if (!string.IsNullOrWhiteSpace(hls)) media.Add(new() { Id = "hls", Url = Resource(hls), Kind = MediaKind.Hls, Extension = "m3u8", HasVideo = true, HasAudio = true, Source = "ted" });
        var direct = Array(resources, "h264");
        if (direct.Length > 100) throw new InvalidDataException("TED returned too many progressive streams.");
        for (var index = 0; index < direct.Length; index++)
        {
            var stream = direct[index];
            if (Text(stream, "file") is not { } raw) continue;
            media.Add(new() { Id = "mp4-" + (index + 1), Url = Resource(raw), Kind = MediaKind.Progressive, Extension = "mp4", MimeType = "video/mp4", HasVideo = true,
                VideoCodec = "avc1", Width = Integer(stream, "width"), Height = Integer(stream, "height"), Bandwidth = Number(stream, "bitrate") * 1000,
                FramesPerSecond = Number(stream, "fps"), IsFallback = hls is not null, Source = "ted", Label = "Progressive fallback" });
        }
        var page = Metadata(video) with { Media = media, SourcePageUrl = document.Url,
            PlaybackHeaders = new Dictionary<string, string> { ["Referer"] = "https://www.ted.com/" } };
        if (Text(Object(resources, "hls"), "metadata") is { } metadataUrl)
        {
            var metadata = await http.GetStringAsync(Resource(metadataUrl), 2 * 1024 * 1024, ct).ConfigureAwait(false);
            using var trackDocument = JsonDocument.Parse(metadata);
            var tracks = Array(trackDocument.RootElement, "subtitles");
            if (tracks.Length > 200) throw new InvalidDataException("TED returned too many caption tracks.");
            var subtitles = tracks.Select(track => new SubtitleTrack { Url = Resource(Text(track, "webvtt")), Language = Text(track, "code") ?? "und", Extension = "vtt" }).ToArray();
            var domains = Array(trackDocument.RootElement, "domains");
            var durations = domains.Select(domain => Number(domain, "duration")).ToArray();
            var duration = durations.Length > 0 && durations.All(value => value is > 0) ? durations.Sum(value => value!.Value) : page.DurationSeconds;
            if (duration is { } seconds && !double.IsFinite(seconds)) throw new InvalidDataException("TED returned an invalid presentation duration.");
            page = page with { Subtitles = subtitles, DurationSeconds = duration };
        }
        if (media.Count > 0) return page;
        var external = Object(player, "external");
        if (Text(external, "service")?.Equals("YouTube", StringComparison.OrdinalIgnoreCase) == true && Text(external, "code") is { } code && Regex.IsMatch(code, @"\A[A-Za-z0-9_-]{11}\z"))
        {
            if (Number(external, "startTime") is > 0) throw new NotSupportedException("TED external-player clip offsets require additional native support.");
            return page with { EmbeddedPages = [new Uri("https://www.youtube.com/watch?v=" + code)] };
        }
        return page with { Warnings = ["TED did not expose playable native media for this talk."] };
    }
    private static PageMetadata ReadPlaylist(JsonElement playlist, string expectedId, int maximum)
    {
        if (maximum is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(maximum));
        if (Id(playlist) != expectedId) throw new InvalidDataException("TED returned another playlist identity.");
        return Metadata(playlist) with { IsPlaylist = true, PlaylistId = expectedId, Entries = ReadEntries(Object(playlist, "videos"), maximum, 0) };
    }
    private static PageMetadata ReadSeries(JsonElement props, string slug, int maximum)
    {
        if (maximum is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(maximum));
        var series = Object(props, "series");
        if (Text(series, "slug") != slug) throw new InvalidDataException("TED returned another series identity.");
        if (!props.TryGetProperty("seasons", out var seasons) || seasons.ValueKind != JsonValueKind.Array || seasons.GetArrayLength() > 1000)
            throw new InvalidDataException("TED did not expose a bounded series season list.");
        var entries = new List<NativePlaylistEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var season in seasons.EnumerateArray())
        {
            if (!seen.Add(Id(season))) throw new InvalidDataException("TED repeated a series season identity.");
            entries.AddRange(ReadEntries(Object(season, "videos"), maximum - entries.Count, entries.Count));
        }
        return Metadata(series) with { IsPlaylist = true, PlaylistId = Id(series), Entries = entries };
    }
    private static IReadOnlyList<NativePlaylistEntry> ReadEntries(JsonElement videos, int maximum, int preceding)
    {
        if (videos.ValueKind != JsonValueKind.Object || !videos.TryGetProperty("nodes", out var array) || array.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("TED did not expose a collection entry list.");
        var nodes = array.EnumerateArray().ToArray();
        if (Integer(videos, "totalCount") != nodes.Length) throw new NotSupportedException("This TED playlist requires additional pagination; it was not truncated.");
        if (nodes.Length > maximum) throw new InvalidDataException("The TED playlist exceeds its item limit; it was not truncated.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = nodes.Select((video, index) =>
        {
            if (video.ValueKind == JsonValueKind.Null) return new NativePlaylistEntry(preceding + index + 1, null, "Unavailable TED talk", null, null, null, false);
            var id = Id(video); if (!seen.Add(id)) throw new InvalidDataException("TED repeated a playlist talk identity.");
            var slug = Text(video, "slug") ?? throw new InvalidDataException("A TED playlist talk has no slug.");
            var url = ValidateCanonical(video, slug); var metadata = Metadata(video);
            return new NativePlaylistEntry(preceding + index + 1, id, metadata.Title, url, metadata.DurationSeconds, metadata.ThumbnailUrl, true);
        }).ToArray();
        return entries;
    }
    private static PageMetadata Metadata(JsonElement value) => new() { Id = Id(value), Extractor = "ted", Title = Text(value, "title") ?? Text(value, "name") ?? Id(value), Description = Text(value, "description"),
        Uploader = Text(value, "presenterDisplayName"), DurationSeconds = Number(value, "duration"),
        Thumbnails = Array(value, "primaryImageSet").Where(image => Text(image, "url") is { Length: > 0 }).Select((image, index) => new ThumbnailCandidate
        { Id = Text(image, "aspectRatioName") ?? index.ToString(CultureInfo.InvariantCulture), Url = Resource(Text(image, "url")), Width = Integer(image, "width"), Height = Integer(image, "height") }).ToArray(),
        ThumbnailUrl = Array(value, "primaryImageSet").OrderByDescending(image => Text(image, "aspectRatioName") == "16x9").Select(image => Text(image, "url")).FirstOrDefault(raw => !string.IsNullOrWhiteSpace(raw)) is { } imageUrl ? Resource(imageUrl).AbsoluteUri : null };
    private static string Id(JsonElement value) => (Text(value, "id") ?? Long(value, "id")?.ToString(CultureInfo.InvariantCulture)) is { } id && Regex.IsMatch(id, @"\A[1-9][0-9]{0,19}\z") ? id : throw new InvalidDataException("TED returned an invalid identity.");
    private static Uri ValidateCanonical(JsonElement value, string slug)
    {
        var url = Resource(Text(value, "canonicalUrl")); var address = Parse(url);
        if (address.Kind != PageKind.Talk || address.Key != slug) throw new InvalidDataException("TED returned an inconsistent talk URL.");
        return address.Url;
    }
    private static Address Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Host is not ("ted.com" or "www.ted.com" or "embed.ted.com"))
            throw new ArgumentException("Use a TED talk, embedded talk, playlist or series URL.");
        var talk = Regex.Match(url.AbsolutePath, @"\A/talks/(?:lang/[A-Za-z0-9-]{1,20}/)?(?<slug>[A-Za-z0-9_-]{1,250})(?:/transcript)?/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (talk.Success) return new(talk.Groups["slug"].Value, PageKind.Talk, new Uri("https://" + (url.Host == "embed.ted.com" ? "embed.ted.com" : "www.ted.com") + "/talks/" + talk.Groups["slug"].Value));
        var playlist = Regex.Match(url.AbsolutePath, @"\A/playlists/(?<id>[1-9][0-9]{0,19})(?:/[A-Za-z0-9_-]{1,250})?/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (playlist.Success) return new(playlist.Groups["id"].Value, PageKind.Playlist, new Uri("https://www.ted.com" + url.AbsolutePath.TrimEnd('/')));
        var series = Regex.Match(url.AbsolutePath, @"\A/series/(?<slug>[A-Za-z0-9_-]{1,250})/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (series.Success) return new(series.Groups["slug"].Value, PageKind.Series, new Uri("https://www.ted.com/series/" + series.Groups["slug"].Value));
        throw new NotSupportedException("Native support for this TED search or page type remains pending.");
    }
    private static Uri Resource(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0 ||
            !(url.Host == "ted.com" || url.Host.EndsWith(".ted.com", StringComparison.OrdinalIgnoreCase) || url.Host.EndsWith(".tedcdn.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("TED returned an invalid media, caption, image or page address.");
        return url;
    }
}
