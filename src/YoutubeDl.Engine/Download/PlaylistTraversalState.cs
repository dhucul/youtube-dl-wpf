using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Download;

internal sealed class PlaylistTraversalState
{
    private int _positions;
    internal IReadOnlyList<string> Enter(PageMetadata page, NativeDownloadRequest request)
    {
        var uri = new UriBuilder(page.SourcePageUrl ?? request.Url) { Fragment = "" }.Uri;
        var key = page.Extractor + "|" + uri.Host + "|" + (page.PlaylistId ?? page.Id ?? uri.AbsoluteUri);
        if (request.PlaylistAncestors.Count >= 5 || request.PlaylistAncestors.Contains(key, StringComparer.Ordinal))
            throw new NativePlaylistTraversalException("The nested playlist repeats an ancestor or exceeds five collection levels.");
        if (page.Entries.Count > request.MaximumPlaylistEntries || page.Entries.Any(entry => entry.Position < 1) || page.Entries.Select(entry => entry.Position).Distinct().Count() != page.Entries.Count)
            throw new NativePlaylistTraversalException("The nested playlist has excessive, invalid or repeated source positions.");
        if (Interlocked.Add(ref _positions, page.Entries.Count) > 10000)
            throw new NativePlaylistTraversalException("This playlist job exceeds the complete traversal limit of 10,000 source positions.");
        return Array.AsReadOnly(request.PlaylistAncestors.Append(key).ToArray());
    }
}

public sealed class NativePlaylistTraversalException(string message) : Exception(message);
