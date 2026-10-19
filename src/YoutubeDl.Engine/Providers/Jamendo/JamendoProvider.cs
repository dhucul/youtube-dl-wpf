using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Jamendo;

public sealed partial class JamendoProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("jamendo", "Jamendo public tracks and ordered albums", 200);
    public bool CanHandle(Uri url) => JamendoAddress.Matches(url);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => JamendoAddress.Parse(request.Url) is { Album: false } track ? new("jamendo", track.Id) : null;

    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = JamendoAddress.Parse(request.Url); using var anonymous = http.CreateAnonymousSession(); var budget = new Budget();
        if (address.Album) return await AlbumAsync(address, request, anonymous, budget, ct).ConfigureAwait(false);
        using var document = await ApiAsync("tracks", [address.Id], anonymous, budget, ct).ConfigureAwait(false);
        var track = Single(document.RootElement, address.Id); var artistId = Id(track, "artistId"); var albumId = OptionalId(track, "albumId");
        using var artist = await ApiAsync("artists", [artistId], anonymous, budget, ct).ConfigureAwait(false);
        var owner = Single(artist.RootElement, artistId); ValidateArtist(owner);
        if (albumId is null) return Track(track, owner, default);
        using var album = await ApiAsync("albums", [albumId], anonymous, budget, ct).ConfigureAwait(false);
        var parent = Single(album.RootElement, albumId); ValidateAlbum(parent);
        if (Id(parent, "artistId") != artistId || !Membership(parent, 10000).Any(item => item.Id == address.Id))
            throw new InvalidDataException("Jamendo returned a track outside its declared artist or album.");
        return Track(track, owner, parent);
    }

    private static PageMetadata Track(JsonElement track, JsonElement artist, JsonElement album)
    {
        var id = Id(track); var artistId = Id(track, "artistId"); var albumId = OptionalId(track, "albumId");
        if (Id(artist) != artistId || albumId is not null && (Id(album) != albumId || Id(album, "artistId") != artistId))
            throw new InvalidDataException("Jamendo returned conflicting track ownership.");
        RequireAvailable(track);
        if (!Flag(track, "isDownloadable", required: true)) throw new NotSupportedException("The Jamendo artist does not currently offer this track for download.");
        var duration = Number(track, "duration");
        if (duration is not > 0 or > 604800) throw new InvalidDataException("Jamendo did not declare a supported complete track duration.");
        foreach (var flag in new[] { "isPreview", "isLive", "requiresLogin", "isPremium", "drmProtected" })
            if (Flag(track, flag)) throw new NotSupportedException("This Jamendo preview, live or restricted workflow needs additional native support.");
        var sources = new List<MediaCandidate>();
        foreach (var property in new[] { "download", "stream" })
        {
            if (!track.TryGetProperty(property, out var formats) || formats.ValueKind == JsonValueKind.Null) continue;
            if (formats.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Jamendo returned an invalid audio format inventory.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var format in formats.EnumerateObject())
            {
                if (!seen.Add(format.Name)) throw new InvalidDataException("Jamendo repeated an audio format descriptor.");
                if (format.Value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Jamendo returned an invalid audio descriptor.");
                var raw = format.Value.GetString(); if (string.IsNullOrEmpty(raw)) continue;
                if (format.Name is not ("mp3" or "mp32" or "mp33" or "ogg" or "flac")) throw new NotSupportedException("This Jamendo audio encoding needs additional native support.");
                var codec = format.Name.StartsWith("mp3", StringComparison.Ordinal) ? "mp3" : format.Name == "ogg" ? "vorbis" : "flac";
                var extension = codec == "vorbis" ? "ogg" : codec; var url = Audio(raw, id, property, format.Name);
                sources.Add(new() { Id = property + "-" + format.Name, Url = url, Kind = MediaKind.Progressive, Source = "jamendo", Extension = extension,
                    MimeType = "audio/" + (codec == "mp3" ? "mpeg" : extension), HasVideo = false, HasAudio = true, AudioCodec = codec, IsCompleteMedia = true,
                    VerificationDemuxer = extension, VerificationDurationSeconds = duration, VerificationMinimumDurationSeconds = duration, VerificationMinimumDurationToleranceSeconds = 0.1,
                    ProviderQualityOrder = property == "download" ? format.Name == "flac" ? 90 : format.Name == "mp3" ? 80 : 70 : format.Name switch { "mp32" => 60, "mp33" => 50, "ogg" => 40, _ => 10 } });
            }
        }
        if (sources.Count == 0) throw new NotSupportedException("Jamendo did not expose complete audio for this public track.");
        var images = Covers(track, id, albumId); var published = Published(track); var stats = Object(track, "stats");
        var facts = new Dictionary<string, object?> { ["artist_id"] = artistId, ["album_id"] = albumId, ["album"] = Text(album, "name"),
            ["license"] = Text(track, "licenseCCUrl"), ["like_count"] = Long(stats, "favorited"), ["download_count"] = Long(stats, "downloadedAll") };
        if (album.ValueKind == JsonValueKind.Object) facts["track_number"] = Membership(album, 10000).Single(item => item.Id == id).Position;
        return new() { Id = id, Extractor = "jamendo", ArchiveIdentity = new("jamendo", id), Title = RequiredName(track), Uploader = RequiredName(artist),
            Description = Text(track, "description") ?? Text(track, "credits"), DurationSeconds = duration, UploadTimestamp = published, UploadDate = MetadataFacts.Day(published),
            ViewCount = Long(stats, "listenedAll"), MetadataFields = facts, ThumbnailUrl = images.OrderByDescending(image => image.Width).FirstOrDefault()?.Url.AbsoluteUri,
            Thumbnails = images, Media = sources, SourcePageUrl = new JamendoAddress(id, false).Web, AnonymousPlayback = true };
    }

    private static Uri Audio(string raw, string id, string source, string format)
    {
        var url = PublicUrl(raw);
        if (url.Host != "storage.jamendo.com" && !Regex.IsMatch(url.Host, @"\Aprod-[0-9]+\.storage\.jamendo\.com\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new NotSupportedException("This Jamendo media origin needs additional native support.");
        var encoding = format == "mp3" ? source == "stream" ? "mp31" : "mp32" : format;
        if (source == "download")
        {
            if (url.AbsolutePath != "/download/track/" + id + "/" + encoding + "/") throw new InvalidDataException("Jamendo's download URL does not belong to the declared track and format.");
        }
        else
        {
            var query = Query(url);
            if (url.AbsolutePath != "/" || query.GetValueOrDefault("trackid") != id || query.GetValueOrDefault("format") != encoding)
                throw new InvalidDataException("Jamendo's stream URL does not belong to the declared track and format.");
        }
        return url;
    }
    private static IReadOnlyList<ThumbnailCandidate> Covers(JsonElement root, string id, string? albumId)
    {
        var cover = Object(root, "cover"); var results = new List<ThumbnailCandidate>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        if (cover.ValueKind != JsonValueKind.Object) return results;
        foreach (var group in cover.EnumerateObject())
        {
            if (group.Value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Jamendo returned an invalid cover inventory.");
            foreach (var image in group.Value.EnumerateObject())
            {
                if (image.Value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Jamendo returned an invalid cover descriptor.");
                var raw = image.Value.GetString(); if (string.IsNullOrEmpty(raw)) continue; var url = PublicUrl(raw); var query = Query(url);
                if (url.Host != "usercontent.jamendo.com" || query.GetValueOrDefault("id") != (albumId ?? id) || query.GetValueOrDefault("type") != (albumId is null ? "track" : "album") ||
                    query.TryGetValue("trackid", out var track) && track != id) throw new InvalidDataException("Jamendo returned cover art belonging to another source.");
                if (!seen.Add(url.AbsoluteUri)) continue; if (results.Count >= 64) throw new InvalidDataException("Jamendo returned too many cover variants.");
                var width = int.TryParse(query.GetValueOrDefault("width"), NumberStyles.None, CultureInfo.InvariantCulture, out var pixels) && pixels is > 0 and <= 4096 ? (int?)pixels : null;
                results.Add(new() { Id = group.Name + "-" + image.Name, Url = url, Width = width });
            }
        }
        return results;
    }
    private static Dictionary<string, string> Query(Uri url)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2); if (!result.TryAdd(Uri.UnescapeDataString(pair[0]), pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : ""))
                throw new InvalidDataException("Jamendo repeated a resource query field.");
        }
        return result;
    }
    private static Uri PublicUrl(string? raw) => raw is not null && raw.Length <= 32768 && Uri.TryCreate(raw, UriKind.Absolute, out var url) && url.Scheme == "https" &&
        url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 ? url : throw new InvalidDataException("Jamendo returned an invalid public resource.");
    private static DateTimeOffset? Published(JsonElement root)
    {
        var timestamp = Long(root, "dateReleased") ?? Long(root, "dateCreated"); var date = MetadataFacts.Unix(timestamp);
        if (timestamp is not null && date is null) throw new InvalidDataException("Jamendo returned invalid publication time.");
        if (date > DateTimeOffset.UtcNow) throw new NotSupportedException("This Jamendo recording has not yet been published.");
        return date;
    }
    private static bool Flag(JsonElement root, string name, bool required = false)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Jamendo returned an invalid public state record.");
        if (!root.TryGetProperty(name, out var value)) return !required ? false : throw new InvalidDataException("Jamendo omitted a required public state.");
        return value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Number when value.TryGetInt32(out var number) && number is 0 or 1 => number == 1,
            _ => throw new InvalidDataException("Jamendo returned an invalid public state.") };
    }
    private static void RequireAvailable(JsonElement root)
    {
        var status = Object(root, "status"); if (status.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Jamendo omitted public availability.");
        if (!Flag(status, "available", required: true)) throw new NotSupportedException("Jamendo currently reports this recording unavailable.");
    }
    private static void ValidateArtist(JsonElement root)
    {
        if (Text(root, "status") != "visible") throw new NotSupportedException("This Jamendo artist is not publicly visible."); _ = RequiredName(root);
    }
    private static void ValidateAlbum(JsonElement root)
    {
        RequireAvailable(root); if (Text(root, "moderation") != "publishable") throw new NotSupportedException("This Jamendo album is not publicly published.");
        _ = Published(root); _ = RequiredName(root); _ = Id(root, "artistId");
    }
    private static string RequiredName(JsonElement root) => Text(root, "name") is { Length: > 0 and <= 2000 } name ? name : throw new InvalidDataException("Jamendo returned missing or excessive recording metadata.");
    private static string Id(JsonElement root, string field = "id") => OptionalId(root, field) ?? throw new InvalidDataException("Jamendo returned a missing numeric identity.");
    private static string? OptionalId(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.String && value.GetString() == "" || Long(root, field) == 0) return null;
        var id = Long(root, field)?.ToString(CultureInfo.InvariantCulture); return id is not null && JamendoAddress.Numeric(id) ? id : throw new InvalidDataException("Jamendo returned an invalid numeric identity.");
    }
    private static JsonElement Single(JsonElement root, string id) => root.GetArrayLength() == 1 && Id(root[0]) == id ? root[0] : throw new InvalidDataException("Jamendo returned another or missing requested entity.");
    private sealed class Budget
    {
        private int _characters;
        internal void Use(int count) { _characters = checked(_characters + count); if (_characters > 32 * 1024 * 1024) throw new InvalidDataException("Jamendo's combined metadata exceeds its limit."); }
    }
    private static async Task<JsonDocument> ApiAsync(string kind, IReadOnlyList<string> ids, MediaHttp http, Budget budget, CancellationToken ct)
    {
        if (ids.Count is < 1 or > 100 || ids.Any(id => !JamendoAddress.Numeric(id))) throw new InvalidDataException("Jamendo's metadata request exceeds its bounded identity scope.");
        var endpoint = new Uri("https://www.jamendo.com/api/" + kind + "?" + string.Join('&', ids.Select(id => "id%5B%5D=" + id)));
        var headers = new Dictionary<string, string> { ["X-Jam-Call"] = JamendoRequestIdentity.Create(endpoint), ["X-Requested-With"] = "XMLHttpRequest", ["Accept"] = "application/json", ["Referer"] = "https://www.jamendo.com/" };
        var document = await http.GetDocumentAsync(endpoint, 8 * 1024 * 1024, ct, headers).ConfigureAwait(false); budget.Use(document.Text.Length);
        if (document.Url.Scheme != endpoint.Scheme || document.Url.Host != endpoint.Host || document.Url.AbsolutePath != endpoint.AbsolutePath)
            throw new InvalidDataException("Jamendo redirected public metadata outside its requested endpoint.");
        var json = JsonDocument.Parse(document.Text, new JsonDocumentOptions { MaxDepth = 64 });
        if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() != ids.Count) { json.Dispose(); throw new InvalidDataException("Jamendo omitted requested metadata or returned an unsupported response."); }
        return json;
    }
}
