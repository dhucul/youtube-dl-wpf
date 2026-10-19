using System.Globalization;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Audius;

public sealed partial class AudiusProvider
{
    private static async Task<PageMetadata> ArtistAsync(AudiusAddress address, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var endpoint = new Uri(Api + "users/" + (address.Id ?? "handle/" + Uri.EscapeDataString(address.Handle!))); using var document = await JsonAsync(endpoint, http, ct).ConfigureAwait(false); var user = Data(document.RootElement); var id = Id(user); var handle = Text(user, "handle");
        ValidateUser(user); if (address.Id is { } expected && id != expected || address.Handle is { } expectedHandle && !string.Equals(handle, expectedHandle, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Audius returned another requested artist.");
        var count = Integer(user, "track_count") ?? throw new InvalidDataException("Audius omitted its declared artist track count."); Limit(count, request); var entries = new List<NativePlaylistEntry>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            using var page = await JsonAsync(new(Api + "users/" + id + "/tracks?limit=100&offset=" + entries.Count.ToString(CultureInfo.InvariantCulture) + "&filter_tracks=public&sort_method=release_date&sort_direction=desc"), http, ct).ConfigureAwait(false); var tracks = Data(page.RootElement);
            if (tracks.ValueKind != JsonValueKind.Array || tracks.GetArrayLength() > 100) throw new InvalidDataException("Audius omitted a bounded artist track page.");
            foreach (var track in tracks.EnumerateArray()) { if (Id(Object(track, "user")) != id || !seen.Add(Id(track)) || entries.Count >= count) throw new InvalidDataException("Audius changed artist ownership or repeated an unexpected track."); entries.Add(Entry(track, entries.Count + 1)); }
            if (tracks.GetArrayLength() < 100) { if (entries.Count != count) throw new InvalidDataException("Audius's public artist tracks do not match the declared complete inventory."); break; }
        }
        using var refreshed = await JsonAsync(new(Api + "users/" + id), http, ct).ConfigureAwait(false); if (Id(Data(refreshed.RootElement)) != id || Integer(Data(refreshed.RootElement), "track_count") != count) throw new InvalidDataException("Audius changed artist identity or track count during pagination.");
        return new() { Id = id, PlaylistId = id, Extractor = "audius:artist", IsPlaylist = true, Title = Text(user, "name") ?? handle ?? id, Description = Text(user, "bio"), Uploader = Text(user, "name") ?? handle,
            ThumbnailUrl = Artwork(user, "profile_picture"), SourcePageUrl = new("https://audius.co/" + handle), Entries = entries };
    }
    private static async Task<PageMetadata> PlaylistAsync(JsonElement playlist, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var id = Id(playlist); var user = Object(playlist, "user"); ValidateUser(user);
        if (Flag(playlist, "is_private") || Flag(playlist, "is_delete") || Flag(playlist, "is_scheduled_release") || Flag(playlist, "is_stream_gated")) throw new NotSupportedException("This Audius playlist needs additional private scheduled or gated access support.");
        var count = Integer(playlist, "track_count") ?? throw new InvalidDataException("Audius omitted its playlist track count."); Limit(count, request); var contents = Array(playlist, "playlist_contents");
        if (contents.Length != count) throw new InvalidDataException("Audius's complete playlist membership count conflicts with its metadata.");
        var ids = contents.Select(item => Text(item, "track_id") is { } track && AudiusAddress.Identifier(track) ? track : throw new InvalidDataException("Audius omitted a stable playlist member identity.")).ToArray();
        using var response = await JsonAsync(new(Api + "playlists/" + id + "/tracks"), http, ct).ConfigureAwait(false); var tracks = Data(response.RootElement); if (tracks.ValueKind != JsonValueKind.Array || tracks.GetArrayLength() > request.MaximumPlaylistEntries) throw new InvalidDataException("Audius returned an unsupported playlist track inventory.");
        var mapped = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var track in tracks.EnumerateArray()) { var trackId = Id(track); if (!ids.Contains(trackId, StringComparer.Ordinal)) throw new InvalidDataException("Audius returned a track outside the requested playlist."); if (mapped.TryGetValue(trackId, out var old) && (Text(old, "track_cid") != Text(track, "track_cid") || Text(old, "permalink") != Text(track, "permalink"))) throw new InvalidDataException("Audius returned conflicting repeated playlist tracks."); mapped[trackId] = track; }
        var entries = ids.Select((trackId, index) => mapped.TryGetValue(trackId, out var track) ? Entry(track, index + 1) : new NativePlaylistEntry(index + 1, trackId, "Unavailable Audius track", null, null, null, false, ExpectedArchiveIdentity: new("audius", trackId))).ToArray();
        return new() { Id = id, PlaylistId = id, Extractor = "audius:playlist", IsPlaylist = true, Title = Text(playlist, "playlist_name") ?? id, Description = Text(playlist, "description"), Uploader = Text(user, "name") ?? Text(user, "handle"), ThumbnailUrl = Artwork(playlist, "artwork"), SourcePageUrl = Permalink(playlist), Entries = entries };
    }
    private static NativePlaylistEntry Entry(JsonElement track, int position)
    { var id = Id(track); var available = Available(track); return new(position, id, Text(track, "title") ?? id, available ? AudiusAddress.Track(id) : null, Number(track, "duration"), Artwork(track, "artwork"), available, ExpectedArchiveIdentity: new("audius", id)); }
    private static void Limit(int count, NativeDownloadRequest request) { if (count > Math.Min(10000, request.MaximumPlaylistEntries)) throw new NotSupportedException("The complete Audius inventory exceeds the configured limit."); }
}
