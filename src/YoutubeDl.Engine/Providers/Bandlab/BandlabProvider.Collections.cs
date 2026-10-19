using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Bandlab;

public sealed partial class BandlabProvider
{
    private static async Task<PageMetadata> CollectionAsync(BandlabAddress address, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        using var data = await ApiAsync("collections/" + address.Id, http, ct).ConfigureAwait(false); var root = data.RootElement; var owner = Collection(root, address);
        var posts = Array(root, "posts").ToArray(); var count = Long(Object(root, "counters"), "items");
        if (count is null || count < 0 || count != posts.Length) throw new InvalidDataException("BandLab did not expose its complete declared playlist inventory.");
        if (count > request.MaximumPlaylistEntries) throw new NotSupportedException("BandLab's complete playlist exceeds the configured inventory limit.");
        var entries = new List<NativePlaylistEntry>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var post in posts)
        {
            ct.ThrowIfCancellationRequested(); var postId = Id(post); if (!seen.Add(postId)) throw new InvalidDataException("BandLab repeated a playlist post identity.");
            var revision = Object(post, "revision"); var title = Text(Object(revision, "song"), "name") ?? Text(post, "caption") ?? postId;
            var available = Text(post, "type") == "Revision" && Text(post, "state") == "Public" && !Flag(post, "isExclusive") && !Flag(post, "isDeleted") && revision.ValueKind == JsonValueKind.Object && Flag(revision, "isPublic") && Text(Object(revision, "mixdown"), "status") == "Ready";
            DownloadArchiveIdentity? archive = null;
            if (available) { ValidatePost(post, postId); _ = MediaUrl(Text(Object(revision, "mixdown"), "file"), Id(Object(revision, "mixdown"))); archive = new("bandlab", Id(revision)); }
            entries.Add(new(entries.Count + 1, postId, title, available ? new Uri("https://www.bandlab.com/track/" + postId) : null, Number(Object(revision, "mixdown"), "duration"), null, available, ExpectedArchiveIdentity: archive));
        }
        using var final = await ApiAsync("collections/" + address.Id, http, ct).ConfigureAwait(false); var updated = final.RootElement;
        if (Collection(updated, address) != owner || Text(updated, "lastUpdatedOn") != Text(root, "lastUpdatedOn") || Long(Object(updated, "counters"), "items") != count || !Array(updated, "posts").Select(Signature).SequenceEqual(posts.Select(Signature)))
            throw new InvalidDataException("BandLab changed playlist membership during complete inspection.");
        return new() { Id = address.Id, Extractor = "bandlab:playlist", Title = Text(root, "name") ?? address.Id!, Uploader = Text(Object(root, "creator"), "name"), Description = Text(root, "description"), IsPlaylist = true,
            SourcePageUrl = address.Web, PlaylistId = address.Id, Entries = entries, AnonymousPlayback = true };
    }
    private static string Signature(JsonElement post)
    {
        var revision = Object(post, "revision"); var mix = Object(revision, "mixdown");
        return string.Join('\n', Id(post), Text(post, "type"), Text(post, "state"), Flag(post, "isExclusive").ToString(), Text(revision, "id"),
            revision.ValueKind == JsonValueKind.Object ? Flag(revision, "isPublic").ToString() : "", Text(mix, "id"), Text(mix, "status"), Text(mix, "file"), Number(mix, "duration")?.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    private static string Collection(JsonElement root, BandlabAddress address)
    {
        if (Id(root) != address.Id || Text(root, "type") != "Playlist" || !Flag(root, "isPublic", true) || Flag(root, "isDeleted")) throw new NotSupportedException("BandLab did not return the requested complete public playlist.");
        var creator = Object(root, "creator"); var owner = Text(creator, "username");
        if (!BandlabAddress.Username(owner) || address.Owner is not null && owner != address.Owner) throw new InvalidDataException("BandLab returned another playlist owner.");
        return Id(creator);
    }
}
