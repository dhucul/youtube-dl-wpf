using System.Globalization;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Rutube;

public sealed partial class RutubeProvider
{
    private static async Task<PageMetadata> InventoryAsync(RutubeAddress address, NativeDownloadRequest request, MediaHttp owner, CancellationToken ct)
    {
        using var http = owner.CreateDiscoverySession(false);
        var title = "Rutube " + address.Kind + " " + address.Id; int? count = null;
        if (address.Kind is "playlist" or "movie")
        {
            var metadata = await ApiAsync(http, address.Kind == "playlist" ? "/api/playlist/custom/" + address.Id + "/" : "/api/metainfo/tv/" + address.Id + "/", ct).ConfigureAwait(false);
            if (Long(metadata, "id")?.ToString(CultureInfo.InvariantCulture) != address.Id || True(metadata, "is_hidden")) throw new InvalidDataException("Rutube returned another or unavailable collection identity.");
            title = Text(metadata, address.Kind == "playlist" ? "title" : "name") ?? title;
            if (address.Kind == "playlist") count = Integer(metadata, "videos_count") ?? throw new InvalidDataException("Rutube omitted the playlist's complete count.");
            if (count > request.MaximumPlaylistEntries) throw new InvalidDataException("The complete Rutube playlist exceeds its configured limit.");
            if (address.Kind == "movie" && (!Uri.TryCreate(Text(metadata, "content"), UriKind.Absolute, out var content) || content.Scheme != "https" || content.Host != "rutube.ru" || content.UserInfo.Length != 0 || !content.IsDefaultPort || content.Query.Length != 0 || content.Fragment.Length != 0 || content.AbsolutePath.TrimEnd('/') != address.Connection.TrimEnd('/')))
                throw new InvalidDataException("Rutube changed its show collection resource.");
        }
        var entries = new List<NativePlaylistEntry>(); var known = new Dictionary<string, Uri>(StringComparer.Ordinal);
        for (var page = 1; ; page++)
        {
            ct.ThrowIfCancellationRequested();
            if (page > 1000) throw new InvalidDataException("Rutube exceeded its bounded collection pagination.");
            var endpoint = address.Connection + "?page=" + page + (address.Kind is "person" or "channel" ? "&client=wdp" : "");
            var reply = await ApiAsync(http, endpoint, ct).ConfigureAwait(false);
            if (Integer(reply, "page") != page || Integer(reply, "per_page") is not (>= 1 and <= 100) ||
                !reply.TryGetProperty("results", out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > Integer(reply, "per_page"))
                throw new InvalidDataException("Rutube returned invalid collection paging facts.");
            if (Integer(reply, "num_pages") is { } pages)
            {
                // The public API can report a growing estimate (current page + 1).
                if (pages < page && !(pages == 0 && page == 1 && rows.GetArrayLength() == 0)) throw new InvalidDataException("Rutube returned an invalid collection page estimate.");
            }
            var before = entries.Count;
            foreach (var row in rows.EnumerateArray())
            {
                var id = VideoId(row); ValidateVideo(row, id); var url = new Uri("https://rutube.ru/video/" + id + "/");
                if (address.Kind is "person" or "channel" && Long(Object(row, "author"), "id")?.ToString(CultureInfo.InvariantCulture) != address.Id)
                    throw new InvalidDataException("Rutube returned another channel's video.");
                if (known.TryGetValue(id, out var previous)) { if (previous != url) throw new InvalidDataException("Rutube repeated a member with another address."); if (address.Kind != "playlist") continue; }
                known[id] = url;
                if (entries.Count == request.MaximumPlaylistEntries) throw new InvalidDataException("The complete Rutube inventory exceeds its configured limit; it was not truncated.");
                var metadata = Metadata(row); var available = Unavailable(row) is null && !True(row, "is_livestream");
                entries.Add(new(entries.Count + 1, id, metadata.Title, url, metadata.DurationSeconds, metadata.ThumbnailUrl, available,
                    ExpectedArchiveIdentity: new("rutube", id), CollectionMemberId: id));
            }
            if (!reply.TryGetProperty("has_next", out var more) || more.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || !reply.TryGetProperty("next", out var next))
                throw new InvalidDataException("Rutube omitted its collection continuation state.");
            if (more.ValueKind == JsonValueKind.False)
            {
                if (next.ValueKind != JsonValueKind.Null) throw new InvalidDataException("Rutube ended an inconsistent collection.");
                break;
            }
            if (entries.Count == before || next.ValueKind != JsonValueKind.String || !Uri.TryCreate(next.GetString(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "rutube.ru" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath.TrimEnd('/') != address.Connection.TrimEnd('/'))
                throw new InvalidDataException("Rutube repeated or changed its collection continuation resource.");
            var query = SoundCloud.SoundCloudAddress.Query(uri);
            if (query.Keys.Any(key => key is not ("page" or "client")) || !query.TryGetValue("page", out var value) || value != (page + 1).ToString(CultureInfo.InvariantCulture) ||
                query.TryGetValue("client", out var client) && (address.Kind is not ("person" or "channel") || client != "wdp"))
                throw new InvalidDataException("Rutube changed its collection continuation query.");
        }
        if (count is not null && entries.Count != count) throw new InvalidDataException("Rutube returned an incomplete or changing playlist count.");
        var identity = (address.Kind == "channel" ? "person" : address.Kind) + "-" + address.Id;
        return new() { Id = identity, PlaylistId = identity, IsPlaylist = true, Extractor = "rutube", Title = title, Entries = entries,
            RevalidateCollectionMembership = true, SourcePageUrl = address.Page };
    }
}
