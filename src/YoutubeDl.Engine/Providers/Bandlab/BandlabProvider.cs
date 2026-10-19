using System.Globalization;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Bandlab;

public sealed partial class BandlabProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("bandlab", "BandLab public recordings and complete playlists", 200);
    public bool CanHandle(Uri url) => BandlabAddress.Matches(url);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)
    { var address = BandlabAddress.Parse(request.Url); return address.Kind == "revision" ? new("bandlab", address.Id!) : address.Revision is { } revision ? new("bandlab", revision) : null; }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = BandlabAddress.Parse(request.Url); using var anonymous = http.CreateAnonymousSession();
        if (address.Kind == "collection") return await CollectionAsync(address, request, anonymous, ct).ConfigureAwait(false);
        JsonElement revision, song; string postId;
        if (address.Kind == "revision")
        {
            using var data = await ApiAsync("revisions/" + address.Id + "?edit=false", anonymous, ct).ConfigureAwait(false); revision = data.RootElement.Clone();
            if (Id(revision) != address.Id) throw new InvalidDataException("BandLab returned another requested revision."); song = Object(revision, "song"); postId = Id(revision, "postId");
        }
        else if (address.Kind == "song")
        {
            using var data = await ApiAsync("users/" + address.Owner + "/songs/" + address.Slug, anonymous, ct).ConfigureAwait(false); song = data.RootElement.Clone();
            var author = Object(song, "author");
            if (!Flag(song, "isPublic", true) || Text(song, "slug") != address.Slug || Text(author, "username") != address.Owner || Text(author, "type") != "User") throw new InvalidDataException("BandLab returned another or unavailable song owner.");
            _ = Id(song); _ = Id(author); revision = Object(song, "revision");
            if (address.Revision is not null)
            {
                using var selected = await ApiAsync("revisions/" + address.Revision + "?edit=false", anonymous, ct).ConfigureAwait(false); revision = selected.RootElement.Clone();
                if (Id(revision) != address.Revision || Id(Object(revision, "song")) != Id(song)) throw new InvalidDataException("BandLab selected another song's revision.");
            }
            postId = Id(revision, "postId");
        }
        else
        {
            using var data = await ApiAsync("posts/" + address.Id, anonymous, ct).ConfigureAwait(false); ValidatePost(data.RootElement, address.Id!); revision = Object(data.RootElement, "revision").Clone(); song = Object(revision, "song"); postId = address.Id!;
        }
        var revisionId = Id(revision); var songId = Id(song); var creatorId = Id(Object(revision, "creator")); ValidateRevision(revision);
        using var publication = await ApiAsync("posts/" + postId, anonymous, ct).ConfigureAwait(false); var post = publication.RootElement; ValidatePost(post, postId);
        var current = Object(post, "revision"); var publishedSong = Object(current, "song");
        if (Id(current) != revisionId || Id(publishedSong) != songId || Id(Object(current, "creator")) != creatorId || Id(Object(post, "creator")) != creatorId)
            throw new InvalidDataException("BandLab's recording and public publication identities conflict.");
        if (Text(song, "slug") != Text(publishedSong, "slug") || Text(Object(song, "author"), "username") != Text(Object(publishedSong, "author"), "username"))
            throw new InvalidDataException("BandLab changed the song's canonical owner.");
        var firstMix = Object(revision, "mixdown"); var finalMix = Object(current, "mixdown");
        if (Id(firstMix) != Id(finalMix) || Text(firstMix, "file") != Text(finalMix, "file") || Number(firstMix, "duration") != Number(finalMix, "duration"))
            throw new InvalidDataException("BandLab changed the public audio descriptor during discovery.");
        return Facts(current, publishedSong, post, postId);
    }
    private static void ValidatePost(JsonElement post, string id)
    {
        if (post.ValueKind != JsonValueKind.Object || Id(post) != id) throw new InvalidDataException("BandLab returned another public post.");
        if (Text(post, "type") != "Revision" || Text(post, "state") != "Public" || Flag(post, "isExclusive") || Flag(post, "isDeleted") || Flag(post, "isBlocked"))
            throw new NotSupportedException("This BandLab private, exclusive, unavailable or non-revision post needs additional native support.");
        ValidateRevision(Object(post, "revision"));
    }
    private static void ValidateRevision(JsonElement revision)
    {
        _ = Id(revision); _ = Id(Object(revision, "creator"));
        if (!Flag(revision, "isPublic", true) || Flag(revision, "isDeleted") || Flag(revision, "isPreview") || Flag(revision, "isExclusive")) throw new NotSupportedException("BandLab did not expose a complete public revision.");
        var mix = Object(revision, "mixdown"); _ = Id(mix);
        if (Text(mix, "status") != "Ready" || Flag(mix, "isPreview") || Flag(mix, "isMidi") || Number(mix, "duration") is not > 0 or > 604800) throw new NotSupportedException("BandLab's complete audio mix is unavailable or unsupported.");
        if (Text(mix, "creatorId") is { } creator && BandlabAddress.GuidId(creator) != Id(Object(revision, "creator"))) throw new InvalidDataException("BandLab's audio mix belongs to another creator.");
    }
    private static PageMetadata Facts(JsonElement revision, JsonElement song, JsonElement post, string postId)
    {
        var id = Id(revision); var owner = Text(Object(song, "author"), "username"); var slug = Text(song, "slug"); _ = Id(song); _ = Id(Object(song, "author"));
        if (!BandlabAddress.Username(owner) || !BandlabAddress.SlugValue(slug)) throw new InvalidDataException("BandLab omitted a valid canonical song owner.");
        var mix = Object(revision, "mixdown"); var mixId = Id(mix); var media = MediaUrl(Text(mix, "file"), mixId); var extension = Path.GetExtension(media.AbsolutePath).TrimStart('.');
        var duration = Number(mix, "duration"); var time = Time(Text(revision, "createdOn")); var counters = Object(revision, "counters"); var picture = Text(Object(song, "picture"), "url");
        string? thumbnail = null;
        if (picture is not null)
        {
            var cover = PublicUrl(picture); if (cover.Host != "bl-prod-images.azureedge.net" || !cover.AbsolutePath.StartsWith("/v1.", StringComparison.Ordinal) || !cover.AbsolutePath.Contains("/songs/", StringComparison.Ordinal) || !cover.AbsolutePath.EndsWith('/')) throw new InvalidDataException("BandLab returned unsupported song artwork.");
            thumbnail = cover.AbsoluteUri + "360x360";
        }
        return new() { Id = id, Extractor = "bandlab", ArchiveIdentity = new("bandlab", id), Title = Text(song, "name") ?? id, Uploader = Text(Object(revision, "creator"), "name"), Description = Text(revision, "description") ?? Text(post, "caption"),
            SourcePageUrl = new("https://www.bandlab.com/track/" + postId), DurationSeconds = duration, UploadTimestamp = time, UploadDate = time is { } date ? DateOnly.FromDateTime(date.UtcDateTime) : null,
            ViewCount = Nonnegative(Long(counters, "plays")), AgeLimit = Flag(post, "isExplicit") ? 18 : 0, ThumbnailUrl = thumbnail, AnonymousPlayback = true,
            MetadataFields = new Dictionary<string, object?> { ["post_id"] = postId, ["song_id"] = Id(song), ["uploader_id"] = Id(Object(revision, "creator")), ["mixdown_id"] = mixId, ["like_count"] = Nonnegative(Long(counters, "likes")) },
            Media = [new() { Id = "mixdown", Url = media, Kind = MediaKind.Progressive, Source = "bandlab", Extension = extension, MimeType = extension == "mp3" ? "audio/mpeg" : extension == "wav" ? "audio/wav" : "audio/mp4", HasAudio = true, HasVideo = false,
                IsCompleteMedia = true, VerificationDemuxer = extension == "m4a" ? "mov" : extension, VerificationDurationSeconds = duration }] };
    }
    private static Uri MediaUrl(string? raw, string mixId)
    {
        var url = PublicUrl(raw); var extension = Path.GetExtension(url.AbsolutePath).TrimStart('.');
        if (url.Host != "static.bandlab.com" || extension is not ("m4a" or "mp3" or "wav") || url.AbsolutePath != "/revisions-formatted/" + mixId + "/" + mixId + "." + extension)
            throw new InvalidDataException("BandLab's supplied mixdown does not match its published identity."); return url;
    }
    private static string Id(JsonElement node, string name = "id") => BandlabAddress.GuidId(Text(node, name));
    private static long? Nonnegative(long? value) => value is >= 0 ? value : null;
    private static DateTimeOffset? Time(string? value) => value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date.ToUniversalTime() : null;
    private static bool Flag(JsonElement node, string name, bool required = false)
    {
        if (!node.TryGetProperty(name, out var value)) return required ? throw new InvalidDataException("BandLab omitted a public availability state.") : false;
        return value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False or JsonValueKind.Null => false, _ => throw new InvalidDataException("BandLab returned an invalid availability state.") };
    }
    private static Uri PublicUrl(string? value) => value is not null && value.Length <= 32768 && Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 ? url : throw new InvalidDataException("BandLab returned an invalid public resource.");
    private static async Task<JsonDocument> ApiAsync(string path, MediaHttp http, CancellationToken ct)
    {
        var url = new Uri("https://www.bandlab.com/api/v1.3/" + path); var data = await http.GetDocumentAsync(url, 8 * 1024 * 1024, ct, new Dictionary<string, string> { ["Accept"] = "application/json" }).ConfigureAwait(false);
        if (data.Url != url) throw new InvalidDataException("BandLab redirected public metadata outside the requested endpoint."); return JsonDocument.Parse(data.Text, new JsonDocumentOptions { MaxDepth = 64 });
    }
}
