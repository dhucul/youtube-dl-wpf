using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Jamendo;

public sealed partial class JamendoProvider
{
    private sealed record Member(int Position, string Id);
    private static Member[] Membership(JsonElement album, int maximum)
    {
        if (!album.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array || tracks.GetArrayLength() == 0)
            throw new InvalidDataException("Jamendo omitted a complete album track inventory.");
        if (tracks.GetArrayLength() > maximum) throw new NotSupportedException("The complete Jamendo album exceeds the configured inventory limit.");
        var members = tracks.EnumerateArray().Select(track => new Member(Integer(track, "position") ?? 0, Id(track))).OrderBy(track => track.Position).ToArray();
        if (!members.Select(track => track.Position).SequenceEqual(Enumerable.Range(1, members.Length)) || members.Select(track => track.Id).Distinct(StringComparer.Ordinal).Count() != members.Length)
            throw new InvalidDataException("Jamendo's album contains missing, duplicate or invalid track positions/identities.");
        return members;
    }
    private static async Task<PageMetadata> AlbumAsync(JamendoAddress address, NativeDownloadRequest request, MediaHttp http, Budget budget, CancellationToken ct)
    {
        using var initial = await ApiAsync("albums", [address.Id], http, budget, ct).ConfigureAwait(false);
        var album = Single(initial.RootElement, address.Id); ValidateAlbum(album); var artistId = Id(album, "artistId"); var members = Membership(album, request.MaximumPlaylistEntries);
        using var artist = await ApiAsync("artists", [artistId], http, budget, ct).ConfigureAwait(false);
        var owner = Single(artist.RootElement, artistId); ValidateArtist(owner); var tracks = new Dictionary<string, NativePlaylistEntry>(StringComparer.Ordinal);
        foreach (var batch in members.Chunk(100))
        {
            ct.ThrowIfCancellationRequested(); using var document = await ApiAsync("tracks", batch.Select(track => track.Id).ToArray(), http, budget, ct).ConfigureAwait(false);
            var wanted = batch.Select(track => track.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var track in document.RootElement.EnumerateArray())
            {
                var id = Id(track); if (!wanted.Remove(id) || Id(track, "albumId") != address.Id || Id(track, "artistId") != artistId)
                    throw new InvalidDataException("Jamendo returned repeated or conflicting album track ownership.");
                var position = members.Single(member => member.Id == id).Position; var available = Flag(Object(track, "status"), "available", required: true) && Flag(track, "isDownloadable", required: true);
                var resolved = available ? Track(track, owner, album) : null;
                tracks.Add(id, new(position, id, resolved?.Title ?? RequiredName(track), available ? new JamendoAddress(id, false).Web : null,
                    resolved?.DurationSeconds ?? Number(track, "duration"), resolved?.ThumbnailUrl, available, ExpectedArchiveIdentity: new("jamendo", id)));
            }
            if (wanted.Count != 0) throw new InvalidDataException("Jamendo ended before its complete album inventory.");
        }
        using var final = await ApiAsync("albums", [address.Id], http, budget, ct).ConfigureAwait(false);
        var refreshed = Single(final.RootElement, address.Id); ValidateAlbum(refreshed);
        if (Id(refreshed, "artistId") != artistId || Text(refreshed, "name") != Text(album, "name") || Long(refreshed, "dateUpdated") != Long(album, "dateUpdated") ||
            !Membership(refreshed, request.MaximumPlaylistEntries).SequenceEqual(members)) throw new InvalidDataException("Jamendo changed the album during complete inspection.");
        var images = Covers(album, address.Id, address.Id); var published = Published(album);
        return new() { Id = address.Id, PlaylistId = address.Id, Extractor = "jamendo:album", IsPlaylist = true, Title = RequiredName(album), Uploader = RequiredName(owner),
            Description = Text(Object(album, "description"), "en"), UploadTimestamp = published, UploadDate = MetadataFacts.Day(published), SourcePageUrl = address.Web,
            ThumbnailUrl = images.OrderByDescending(image => image.Width).FirstOrDefault()?.Url.AbsoluteUri, Thumbnails = images, AnonymousPlayback = true,
            Entries = members.Select(member => tracks[member.Id]).ToArray() };
    }
}
