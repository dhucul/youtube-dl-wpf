using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.HearThis;

public sealed partial class HearThisProvider
{
    private static async Task<PageMetadata> ArtistAsync(HearThisAddress address, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        using var profile = await ApiAsync(address.Api, http, ct).ConfigureAwait(false); var root = profile.RootElement; var owner = ValidateArtist(root, address); var expected = Long(root, "track_count");
        if (expected is null or < 0) throw new InvalidDataException("hearthis.at did not expose a complete declared artist inventory.");
        if (expected > request.MaximumPlaylistEntries) throw new NotSupportedException("The complete hearthis.at artist inventory exceeds the configured limit.");
        var entries = new List<NativePlaylistEntry>(); var seen = new HashSet<string>(StringComparer.Ordinal); var first = new List<string>();
        for (var page = 1; ; page++)
        {
            ct.ThrowIfCancellationRequested();
            if (page > 201) throw new InvalidDataException("hearthis.at artist pagination did not terminate within its bound.");
            using var data = await ApiAsync(new(address.Api.AbsoluteUri + "?type=tracks&page=" + page + "&count=50"), http, ct).ConfigureAwait(false);
            if (data.RootElement.ValueKind != JsonValueKind.Array || data.RootElement.GetArrayLength() > 50) throw new InvalidDataException("hearthis.at returned an invalid artist page.");
            var records = data.RootElement.EnumerateArray().ToArray();
            foreach (var item in records)
            {
                var id = Id(item); var slug = Text(item, "permalink");
                if (!seen.Add(id) || Id(item, "user_id") != owner || Id(Object(item, "user")) != owner || Text(Object(item, "user"), "permalink") != address.Artist || !HearThisAddress.Slug(slug))
                    throw new InvalidDataException("hearthis.at repeated or changed artist recording identities.");
                var track = new HearThisAddress(address.Artist, slug); if (HearThisAddress.Parse(PublicUrl(Text(item, "permalink_url"))) != track) throw new InvalidDataException("hearthis.at changed an artist entry's canonical address.");
                var available = !Flag(item, "private", true) && !Flag(item, "fan_exclusive_play") && !Flag(item, "is_live") && !Flag(item, "is_live_video") && string.IsNullOrEmpty(Text(item, "video_stream")) && !string.IsNullOrWhiteSpace(Text(item, "stream_url"));
                if (available) ValidateTrack(item, track);
                entries.Add(new(entries.Count + 1, id, Text(item, "title") ?? slug!, available ? track.Web : null, Number(item, "duration"), Text(item, "thumb"), available, ExpectedArchiveIdentity: new("hearthisat", id)));
                if (page == 1) first.Add(Signature(item));
                if (entries.Count > expected || entries.Count > request.MaximumPlaylistEntries) throw new InvalidDataException("hearthis.at exceeded its declared artist inventory.");
            }
            if (records.Length < 50) break;
            if (entries.Count == expected) break;
        }
        if (entries.Count != expected) throw new InvalidDataException("hearthis.at's public artist pages did not match their declared count.");
        using var finalProfile = await ApiAsync(address.Api, http, ct).ConfigureAwait(false);
        if (ValidateArtist(finalProfile.RootElement, address) != owner || Long(finalProfile.RootElement, "track_count") != expected) throw new InvalidDataException("hearthis.at changed its artist inventory during inspection.");
        using var final = await ApiAsync(new(address.Api.AbsoluteUri + "?type=tracks&page=1&count=50"), http, ct).ConfigureAwait(false);
        if (final.RootElement.ValueKind != JsonValueKind.Array || !final.RootElement.EnumerateArray().Select(Signature).SequenceEqual(first)) throw new InvalidDataException("hearthis.at changed its first artist page during complete inspection.");
        return new() { Id = owner, Extractor = "hearthisat:artist", Title = Text(root, "username") ?? address.Artist, Uploader = Text(root, "username"), SourcePageUrl = address.Web, IsPlaylist = true, PlaylistId = owner, Entries = entries, AnonymousPlayback = true };
    }
    private static string ValidateArtist(JsonElement root, HearThisAddress address)
    {
        var id = Id(root);
        if (Text(root, "permalink") != address.Artist || PublicUrl(Text(root, "permalink_url")) != address.Web || PublicUrl(Text(root, "uri")) != address.Api) throw new InvalidDataException("hearthis.at returned another artist identity."); return id;
    }
    private static string Signature(JsonElement root) => string.Join('\n', Id(root), Text(root, "permalink"), Text(root, "permalink_url"), Flag(root, "private", true).ToString(), Flag(root, "fan_exclusive_play").ToString(), Flag(root, "is_live").ToString(), Flag(root, "is_live_video").ToString(), Text(root, "stream_url"), Text(root, "duration"));
}
