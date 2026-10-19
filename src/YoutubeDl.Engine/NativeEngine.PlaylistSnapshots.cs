using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine;

public sealed partial class NativeEngine
{
    private NativePlaylistSnapshot? CapturePlaylist(NativeDownloadRequest request, PageMetadata page)
    {
        if (!page.IsPlaylist) return null;
        if (page.Entries.Count > request.MaximumPlaylistEntries) throw new InvalidDataException("The inspected playlist exceeds its configured inventory limit.");
        if (page.Entries.Any(entry => entry.Position < 1) || page.Entries.Select(entry => entry.Position).Distinct().Count() != page.Entries.Count)
            throw new InvalidDataException("The inspected playlist contains invalid or repeated source positions.");
        var items = new List<NativePlaylistSnapshot.Item>();
        foreach (var entry in page.Entries)
        {
            var child = entry.Url is not null && _providers.CanHandle(entry.Url) ? _providers.Resolve(entry.Url) : null;
            var refreshParent = child is null || child.Info.Id is "direct" or "generic" ||
                entry.ResolvedMedia?.Media.Any(media => media.Url == entry.Url) == true;
            var expected = entry.ExpectedArchiveIdentity;
            if (expected is null && entry.ResolvedMedia is { } media && (media.ArchiveIdentity is not null || media.Id is not null) && entry.Url is not null)
                expected = DownloadArchiveIdentity.FromMetadata(media, entry.Url);
            if (entry.Available && refreshParent && string.IsNullOrWhiteSpace(entry.Id) && expected is null) return null;
            var referer = entry.ResolvedMedia?.PlaybackHeaders.FirstOrDefault(header => header.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase)).Value ??
                page.SourcePageUrl?.AbsoluteUri ?? request.Url.AbsoluteUri;
            items.Add(new(new(entry.Position, entry.Id, entry.Title, refreshParent ? null : entry.Url, entry.DurationSeconds, null, entry.Available,
                ExpectedArchiveIdentity: expected,CollectionMemberId:entry.CollectionMemberId), refreshParent, expected is null ? entry.ResolvedMedia?.Id ?? entry.Id : null, referer));
        }
        return new(request.Url, _providers.Resolve(request.Url).Info.Id, page, items);
    }
    private async Task<PageMetadata> RestorePlaylistAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var snapshot = request.PlaylistSnapshot!;
        ValidatePlaylistSnapshot(request);
        var original = snapshot.Items.Select(item => item.Entry).ToArray();
        var selected = PlaylistSelection.Select(original, request with { PlaylistReverse = false, PlaylistRandom = false }, Random.Shared, ct);
        var positions = selected.Select(item => item.Position).ToHashSet();
        var needParent = snapshot.Items.Any(item => (item.RefreshParent||snapshot.Metadata.RevalidateCollectionMembership) && item.Entry.Available && positions.Contains(item.Entry.Position));
        PageMetadata? fresh = null;
        if (needParent)
        {
            fresh = await LoadAsync(request with { PlaylistSnapshot = null, ExpandPlaylist = true }, http, ct).ConfigureAwait(false);
            if (!fresh.IsPlaylist || fresh.Id != snapshot.Metadata.Id || fresh.PlaylistId != snapshot.Metadata.PlaylistId || fresh.Extractor != snapshot.Metadata.Extractor)
                throw new InvalidDataException("The playlist changed identity since inspection.");
        }
        var entries = new List<NativePlaylistEntry>();
        foreach (var item in snapshot.Items)
        {
            var old = item.Entry;
            if ((!item.RefreshParent&&!snapshot.Metadata.RevalidateCollectionMembership) || !old.Available || !positions.Contains(old.Position)) { entries.Add(old); continue; }
            var matches = fresh!.Entries.Where(entry => SameMember(old, entry)).OrderBy(entry=>entry.Position).ToArray();
            if (matches.Length == 0)
            {
                entries.Add(old with { Available = false }); continue;
            }
            // Complete collection inventories bind each occurrence in source
            // order; generic refreshes still require unambiguous playback.
            if (!snapshot.Metadata.RevalidateCollectionMembership&&matches.Length > 1 && matches.Skip(1).Any(entry => entry.Url != matches[0].Url || entry.ResolvedMedia?.Id != matches[0].ResolvedMedia?.Id))
                throw new InvalidDataException("The selected playlist identity now has ambiguous playback records.");
            var occurrence=snapshot.Metadata.RevalidateCollectionMembership?snapshot.Items.Count(prior=>prior.Entry.Position<old.Position&&SameMember(old,prior.Entry)):0;
            var occurrences=snapshot.Items.Count(prior=>SameMember(old,prior.Entry));
            if(snapshot.Metadata.RevalidateCollectionMembership&&matches.Length!=occurrences)throw new InvalidDataException("The selected collection occurrence count changed since inspection.");
            var current = matches[occurrence];
            if(old.ExpectedArchiveIdentity is { } pinned&&current.Available&&current.ExpectedArchiveIdentity is { } issued&&pinned.Record!=issued.Record)throw new InvalidDataException("The selected member changed its hosted media since inspection.");
            entries.Add(current with { Position = old.Position, Id = old.Id, ExpectedArchiveIdentity = old.ExpectedArchiveIdentity ?? current.ExpectedArchiveIdentity });
        }
        return snapshot.Metadata with { Entries = entries.AsReadOnly() };
    }
    private static bool SameMember(NativePlaylistEntry old, NativePlaylistEntry current)
    {
        if(old.CollectionMemberId is { } member)return current.CollectionMemberId==member;
        if (old.ExpectedArchiveIdentity is { } expected)
        {
            var actual = current.ExpectedArchiveIdentity;
            if (actual is null && current.ResolvedMedia is { } media && current.Url is not null) actual = DownloadArchiveIdentity.FromMetadata(media, current.Url);
            return actual?.Record == expected.Record;
        }
        return old.Id is not null && current.Id == old.Id;
    }
    private void ValidatePlaylistSnapshot(NativeDownloadRequest request)
    {
        if (request.PlaylistSnapshot is not { } snapshot) return;
        if (!request.DownloadPlaylist || request.Url != snapshot.RequestUrl || _providers.Resolve(request.Url).Info.Id != snapshot.ProviderId)
            throw new ArgumentException("The inspected playlist selection does not belong to this download URL/provider or playlist mode.");
        if (snapshot.Count > request.MaximumPlaylistEntries) throw new InvalidOperationException("The inspected playlist exceeds the configured complete-inventory limit.");
    }
}
