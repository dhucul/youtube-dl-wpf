using System.Globalization;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Audiomack;

public sealed class AudiomackProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("audiomack", "Audiomack public tracks and ordered albums", 200);
    public bool CanHandle(Uri uri) => AudiomackAddress.Matches(uri);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = AudiomackAddress.Parse(request.Url); using var anonymous = http.CreateAnonymousSession();
        var website = await anonymous.GetDocumentAsync(address.Web, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        if (AudiomackAddress.Parse(website.Url) != address || website.Url.Scheme != "https") throw new InvalidDataException("Audiomack redirected to another requested identity.");
        var client = await AudiomackClientConfiguration.ReadAsync(website.Text, anonymous, ct).ConfigureAwait(false);
        // Keep website cookies separate from the API. Its current edge rejects
        // resumed TLS sessions with 406 even when the signed request is valid.
        using var api = http.CreateAnonymousSession(allowTlsResume: false);
        using var document = await ApiAsync("music/" + (address.Album ? "album/" : "song/") + address.Artist + "/" + address.Slug, new Dictionary<string, string>(), client, api, ct).ConfigureAwait(false);
        var root = Object(document.RootElement, "results"); Validate(root, address);
        if (address.Album) return await AlbumAsync(root, address, request, client, api, ct).ConfigureAwait(false);
        var metadata = Facts(root, address); var id = Id(root);
        if (!string.IsNullOrWhiteSpace(request.DownloadArchiveFile) && await DownloadArchive.ContainsAsync(request.DownloadArchiveFile, new("audiomack", id), ct).ConfigureAwait(false)) return metadata;
        var album = Object(root, "album_details"); var albumId = album.ValueKind == JsonValueKind.Object ? Id(album) : null;
        var albumSlug = album.ValueKind == JsonValueKind.Object ? Text(album, "url_slug") : null;
        if (album.ValueKind == JsonValueKind.Object && (Text(album, "uploader_url_slug") != address.Artist || albumSlug is null || !AudiomackAddress.SlugValue(albumSlug)))
            throw new InvalidDataException("Audiomack returned another album owner for the selected track.");
        var sources = new List<MediaCandidate>(); var seen = new HashSet<string>(StringComparer.Ordinal); var session = Guid.NewGuid().ToString("N");
        foreach (var high in new[] { true, false })
        {
            var values = new Dictionary<string, string> { ["environment"] = "desktop-web", ["hq"] = high ? "true" : "false", ["session"] = session };
            if (albumId is not null) values.Add("album_id", albumId);
            using var playback = await ApiAsync("music/play/" + id, values, client, api, ct).ConfigureAwait(false);
            var result = playback.RootElement;
            if (result.ValueKind != JsonValueKind.Object || result.TryGetProperty("error", out _) || result.TryGetProperty("errors", out _) || Flag(result, "isPreview") || Flag(result, "preview"))
                throw new NotSupportedException("Audiomack refused complete public playback for this track.");
            var raw = Text(result, "signedUrl") ?? throw new InvalidDataException("Audiomack did not return a current signed media descriptor.");
            var url = MediaUrl(raw, address, albumSlug); if (!seen.Add(url.AbsoluteUri)) continue;
            var extension = Path.GetExtension(url.AbsolutePath).TrimStart('.').ToLowerInvariant();
            if (extension is not ("mp3" or "m4a" or "aac")) throw new NotSupportedException("This Audiomack adaptive or protected encoding requires additional native support.");
            sources.Add(new() { Id = high ? "hq" : "standard", Url = url, Kind = MediaKind.Progressive, Source = "audiomack", Extension = extension,
                MimeType = extension == "mp3" ? "audio/mpeg" : extension == "m4a" ? "audio/mp4" : "audio/aac", HasAudio = true, HasVideo = false, IsCompleteMedia = true,
                ProviderQualityOrder = high ? 80 : 20, VerificationDemuxer = extension == "m4a" ? "mov" : extension, VerificationDurationSeconds = metadata.DurationSeconds });
        }
        return metadata with { Media = sources };
    }
    private static void Validate(JsonElement root, AudiomackAddress address)
    {
        if (root.ValueKind != JsonValueKind.Object || Text(root, "type") != (address.Album ? "album" : "song") || Text(root, "url_slug") != address.Slug ||
            Text(Object(root, "uploader"), "url_slug") != address.Artist || !AudiomackAddress.SlugValue(address.Artist) || Text(Object(root, "uploader"), "status") != "active")
            throw new InvalidDataException("Audiomack returned another or unidentified recording owner.");
        _ = Id(root); _ = Id(Object(root, "uploader"));
        var canonical = Text(Object(root, "links"), "self");
        if (canonical is null || AudiomackAddress.Parse(PublicUrl(canonical)) != address) throw new InvalidDataException("Audiomack returned another canonical recording address.");
        Restricted(root);
    }
    private static void Restricted(JsonElement root)
    {
        if (Text(root, "status") != "complete" || !Flag(root, "live", required: true)) throw new NotSupportedException("This Audiomack recording is unpublished or incomplete.");
        if (Flag(root, "private", required: true) || Flag(root, "premium_user_only") || Flag(root, "geo_restricted") || Flag(root, "isPreview") || Flag(root, "isLive"))
            throw new NotSupportedException("This Audiomack private, premium, regional, preview or live workflow needs additional native support.");
        if (!string.IsNullOrEmpty(Text(root, "preview")) || !string.IsNullOrEmpty(Text(root, "geo_lock")) || !string.IsNullOrEmpty(Text(root, "geo_premium")))
            throw new NotSupportedException("This Audiomack access or preview descriptor needs additional native support.");
        if (root.TryGetProperty("errors", out var errors) && (errors.ValueKind != JsonValueKind.Array || errors.GetArrayLength() != 0))
            throw new NotSupportedException("Audiomack reported an unavailable recording.");
    }
    private static bool Flag(JsonElement root, string name, bool required = false)
    {
        if (!root.TryGetProperty(name, out var value)) return required ? throw new InvalidDataException("Audiomack omitted a required public state.") : false;
        if (value.ValueKind == JsonValueKind.True) return true; if (value.ValueKind is JsonValueKind.False or JsonValueKind.Null) return false;
        if (value.ValueKind == JsonValueKind.String) return value.GetString() switch { "yes" or "1" or "true" => true, "no" or "0" or "false" or "" => false, _ => throw new InvalidDataException("Audiomack returned an invalid public state.") };
        throw new InvalidDataException("Audiomack returned an invalid public state.");
    }
    private static PageMetadata Facts(JsonElement root, AudiomackAddress address)
    {
        var duration = Number(root, "duration"); if (!address.Album && duration is not > 0 or > 604800) throw new InvalidDataException("Audiomack did not declare a supported complete track duration.");
        var published = MetadataFacts.Unix(Long(root, "released")); var uploaded = MetadataFacts.Unix(Long(root, "uploaded"));
        var image = Text(root, "image"); if (image is not null) { var url = PublicUrl(image); if (url.Host != "i.audiomack.com" || !url.AbsolutePath.StartsWith("/" + address.Artist + "/", StringComparison.Ordinal)) throw new InvalidDataException("Audiomack returned artwork outside the requested owner."); }
        var stats = Object(root, "stats"); var album = Object(root, "album_details");
        return new() { Id = Id(root), Extractor = "audiomack", ArchiveIdentity = address.Album ? null : new("audiomack", Id(root)), Title = Text(root, "title") ?? address.Slug,
            Uploader = Text(Object(root, "uploader"), "name"), Description = Text(root, "description"), DurationSeconds = duration, ThumbnailUrl = image, SourcePageUrl = address.Web,
            UploadTimestamp = uploaded, UploadDate = MetadataFacts.Day(uploaded), ViewCount = Long(stats, "plays-raw"), AgeLimit = Text(root, "explicit") is "no" or "clean" ? 0 : Text(root, "explicit") == "yes" ? 18 : null,
            MetadataFields = new Dictionary<string, object?> { ["release_date"] = MetadataFacts.Day(published)?.ToString("yyyyMMdd", CultureInfo.InvariantCulture), ["album"] = Text(album, "title"),
                ["album_id"] = album.ValueKind == JsonValueKind.Object ? Id(album) : null, ["artist_id"] = Id(Object(root, "uploader")), ["like_count"] = Long(stats, "favorites-raw"), ["repost_count"] = Long(stats, "reposts-raw") }, AnonymousPlayback = true };
    }
    private static async Task<PageMetadata> AlbumAsync(JsonElement root, AudiomackAddress address, NativeDownloadRequest request, AudiomackPublicClient client, MediaHttp http, CancellationToken ct)
    {
        if (!root.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array || tracks.GetArrayLength() == 0) throw new InvalidDataException("Audiomack omitted a complete album inventory.");
        if (tracks.GetArrayLength() > request.MaximumPlaylistEntries) throw new NotSupportedException("The complete Audiomack album exceeds the configured inventory limit.");
        var entries = new List<NativePlaylistEntry>(); var seen = new HashSet<string>(StringComparer.Ordinal); var albumId = Id(root);
        foreach (var track in tracks.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested(); var id = Id(track, "song_id"); var slug = Text(track, "url_slug");
            if (!seen.Add(id) || slug is null || !AudiomackAddress.SlugValue(slug) || Text(track, "uploader_url_slug") != address.Artist)
                throw new InvalidDataException("Audiomack returned repeated or conflicting album track identities.");
            var available = Text(track, "status") == "complete" && !Flag(track, "premium_user_only") && !Flag(track, "geo_restricted") && !Flag(track, "private") &&
                string.IsNullOrEmpty(Text(track, "preview")) && string.IsNullOrEmpty(Text(track, "geo_lock")) && string.IsNullOrEmpty(Text(track, "geo_premium")) &&
                (!track.TryGetProperty("errors", out var errors) || errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() == 0);
            var duration = Number(track, "duration");
            entries.Add(new(entries.Count + 1, id, Text(track, "title") ?? slug, available ? new AudiomackAddress(address.Artist, slug, false).Web : null, duration, null, available,
                ExpectedArchiveIdentity: new("audiomack", id)));
        }
        using var final = await ApiAsync("music/album/" + address.Artist + "/" + address.Slug, new Dictionary<string, string>(), client, http, ct).ConfigureAwait(false);
        var updated = Object(final.RootElement, "results"); Validate(updated, address);
        if (Id(updated) != albumId || Text(updated, "updated") != Text(root, "updated") || !Array(updated, "tracks").Select(track => Id(track, "song_id")).SequenceEqual(entries.Select(entry => entry.Id)))
            throw new InvalidDataException("Audiomack changed its album during complete inspection.");
        return Facts(root, address) with { IsPlaylist = true, PlaylistId = albumId, Extractor = "audiomack:album", Entries = entries };
    }
    private static Uri MediaUrl(string raw, AudiomackAddress address, string? album)
    {
        var uri = PublicUrl(raw); if (uri.Host != "music.audiomack.com") throw new NotSupportedException("This Audiomack media origin needs additional native support.");
        var directory = album is null ? "/streaming/" + address.Artist + "/" : "/albums/" + address.Artist + "/" + album + "/";
        var filename = Path.GetFileName(uri.AbsolutePath);
        if (!uri.AbsolutePath.StartsWith(directory, StringComparison.Ordinal) || !(filename.StartsWith(address.Slug + "-__", StringComparison.Ordinal) || filename.StartsWith(address.Slug + ".", StringComparison.Ordinal)) || uri.AbsolutePath.Contains("/preview/", StringComparison.Ordinal))
            throw new InvalidDataException("Audiomack's supplied media does not belong to the requested track/album.");
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Split('=', 2)).ToArray(); var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in query) if (!names.Add(Uri.UnescapeDataString(pair[0])) || pair.Length != 2) throw new InvalidDataException("Audiomack repeated or omitted a signed resource parameter.");
        var expires = query.SingleOrDefault(pair => pair[0] == "Expires");
        if (expires is null || !long.TryParse(expires[1], out var expiry) || expiry <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() || !names.Contains("Signature") || !names.Contains("Key-Pair-Id"))
            throw new InvalidDataException("Audiomack returned an expired or incomplete signed media descriptor.");
        return uri;
    }
    private static Uri PublicUrl(string? raw) => raw is not null && raw.Length <= 32768 && Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort &&
        uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 ? uri : throw new InvalidDataException("Audiomack returned an invalid public resource.");
    private static string Id(JsonElement root, string name = "id") => Long(root, name) is > 0 and < 1000000000000000 ? Long(root, name)!.Value.ToString(CultureInfo.InvariantCulture) : throw new InvalidDataException("Audiomack returned an invalid numeric identity.");
    private static async Task<JsonDocument> ApiAsync(string path, IReadOnlyDictionary<string, string> values, AudiomackPublicClient client, MediaHttp http, CancellationToken ct)
    {
        var endpoint = new Uri("https://api.audiomack.com/v1/" + path); var signed = AudiomackOAuth.Sign(endpoint, values, client);
        var document = await http.GetDocumentAsync(signed, 8 * 1024 * 1024, ct, new Dictionary<string, string> { ["Accept"] = "application/json", ["Origin"] = "https://audiomack.com" }).ConfigureAwait(false);
        if (document.Url.Scheme != endpoint.Scheme || document.Url.Host != endpoint.Host || document.Url.AbsolutePath != endpoint.AbsolutePath) throw new InvalidDataException("Audiomack redirected anonymous API data outside the requested endpoint.");
        return JsonDocument.Parse(document.Text, new JsonDocumentOptions { MaxDepth = 64 });
    }
}
