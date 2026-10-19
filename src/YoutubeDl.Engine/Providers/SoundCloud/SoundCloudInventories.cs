using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.SoundCloud;

public sealed partial class SoundCloudProvider
{
    private static async Task<PageMetadata> InventoryAsync(SoundCloudAddress address, NativeDownloadRequest request, MediaHttp owner, CancellationToken ct)
    {
        if (request.MaximumPlaylistEntries is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(request.MaximumPlaylistEntries));
        using var http = owner.CreateDiscoverySession(false);
        var pageUrl = address.ApiId is null ? address.Url : new Uri("https://soundcloud.com/");
        var document = await http.GetDocumentAsync(pageUrl, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        if (document.Url != pageUrl) throw new InvalidDataException("SoundCloud changed the collection's page identity.");
        var page = SoundCloudPage.Read(document.Text);
        var client = page.ClientId ?? throw new NotSupportedException("SoundCloud did not supply a public discovery client.");
        string path, id, title; string? uploader = null; string? ownerId = null; string? term = null;
        if (address.Collection == "search")
        {
            term = SoundCloudAddress.Query(address.Url)["q"];
            path = "/search/tracks";
            id = "search-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(term))).ToLowerInvariant();
            title = "SoundCloud search: " + term;
        }
        else if (address.Collection == "user")
        {
            var user = address.ApiId is null ? page.User : await ReadApiAsync(http,
                SoundCloudAddress.WithQuery(address.Url, ("client_id", client)), ct).ConfigureAwait(false);
            ownerId = Id(user);
            if (Text(user, "kind") != "user" || !Uri.TryCreate(Text(user, "permalink_url"), UriKind.Absolute, out var permalink))
                throw new InvalidDataException("SoundCloud did not return a complete profile identity.");
            var parsed = SoundCloudAddress.Parse(permalink);
            if (parsed.Collection != "user" || parsed.ApiId is not null || parsed.Url.Query.Length != 0 ||
                (address.ApiId is null ? parsed.Url != address.Url : ownerId != address.ApiId))
                throw new InvalidDataException("SoundCloud returned another profile identity.");
            path = "/users/" + ownerId + "/tracks"; id = "user-" + ownerId;
            uploader = Text(user, "username") ?? ownerId; title = uploader + " uploads";
        }
        else
        {
            ValidateIdentity(page.Item, address with { Collection = null });
            var trackId = Id(page.Item); path = "/tracks/" + trackId + "/related"; id = "related-" + trackId;
            title = "Related to " + (Text(page.Item, "title") ?? trackId);
        }
        var initial = SoundCloudAddress.WithQuery(new Uri("https://api-v2.soundcloud.com" + path),
            ("client_id", client), ("limit", "200"), ("linked_partitioning", "1"), ("q", term));
        var entries = new List<NativePlaylistEntry>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal); var next = initial; string? queryUrn = null;
        for (var pages = 0; ; pages++)
        {
            ct.ThrowIfCancellationRequested();
            if (pages >= 1000 || !cursors.Add(next.AbsoluteUri)) throw new InvalidDataException("SoundCloud repeated or exceeded its collection pagination.");
            var reply = await ReadApiAsync(http, next, ct).ConfigureAwait(false);
            if (reply.ValueKind != JsonValueKind.Object || !reply.TryGetProperty("collection", out var collection) || collection.ValueKind != JsonValueKind.Array || collection.GetArrayLength() > 200)
                throw new InvalidDataException("SoundCloud returned an invalid collection page.");
            if (Text(reply, "query_urn") is { } issued)
            {
                if (issued.Length is < 1 or > 512 || issued.Any(char.IsControl) || queryUrn is not null && issued != queryUrn)
                    throw new InvalidDataException("SoundCloud changed its collection query identity.");
                queryUrn = issued;
            }
            var before = entries.Count;
            foreach (var track in collection.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var trackId = Id(track);
                if (Text(track, "kind") != "track") throw new InvalidDataException("SoundCloud returned a non-track collection member.");
                var permalink = Permalink(track, false);
                if (ownerId is not null && Id(Object(track, "user")) != ownerId) throw new InvalidDataException("SoundCloud returned another profile's upload.");
                if (!ids.Add(trackId)) continue;
                if (entries.Count == request.MaximumPlaylistEntries) throw new InvalidDataException("The complete SoundCloud inventory exceeds its configured limit; it was not truncated.");
                var metadata = Metadata(track); var available = Unavailable(track) is null;
                entries.Add(new(entries.Count + 1, trackId, metadata.Title, permalink, metadata.DurationSeconds, metadata.ThumbnailUrl, available,
                    ExpectedArchiveIdentity: new("soundcloud", trackId), CollectionMemberId: trackId));
            }
            // SoundCloud terminates with either an absent or a null next_href.
            if (!reply.TryGetProperty("next_href", out var href) || href.ValueKind == JsonValueKind.Null) break;
            if (href.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("SoundCloud returned an invalid collection continuation state.");
            if (entries.Count == before) throw new InvalidDataException("SoundCloud's collection continuation made no progress.");
            next = Continuation(href.GetString(), path, client, term, ref queryUrn);
        }
        return new() { Id = id, PlaylistId = id, Extractor = "soundcloud", Title = title, Uploader = uploader,
            IsPlaylist = true, Entries = entries, RevalidateCollectionMembership = true, SourcePageUrl = request.Url };
    }

    private static Uri Continuation(string? raw, string path, string client, string? term, ref string? queryUrn)
    {
        if (raw?.Length > 8192) throw new InvalidDataException("The SoundCloud continuation exceeds its limit.");
        var uri = ApiUrl(raw);
        if (uri.AbsolutePath != path) throw new InvalidDataException("SoundCloud changed its collection endpoint.");
        var query = SoundCloudAddress.Query(uri);
        if (query.Any(pair => pair.Key is not ("client_id" or "limit" or "offset" or "linked_partitioning" or "q" or "query_urn")) ||
            !query.TryGetValue("offset", out var offset) || offset.Length is < 1 or > 2048 || offset.Any(char.IsControl) ||
            query.TryGetValue("client_id", out var supplied) && supplied != client ||
            query.TryGetValue("limit", out var limit) && (!int.TryParse(limit, out var size) || size is < 1 or > 200) ||
            query.TryGetValue("linked_partitioning", out var linked) && linked is not ("1" or "true") ||
            query.TryGetValue("q", out var search) && search != term)
            throw new InvalidDataException("SoundCloud changed its bounded collection query.");
        if (query.TryGetValue("query_urn", out var urn))
        {
            if (urn.Length is < 1 or > 512 || urn.Any(char.IsControl) || queryUrn is not null && queryUrn != urn)
                throw new InvalidDataException("SoundCloud changed its collection continuation identity.");
            queryUrn = urn;
        }
        return SoundCloudAddress.WithQuery(uri, ("client_id", client), ("q", term));
    }
}
