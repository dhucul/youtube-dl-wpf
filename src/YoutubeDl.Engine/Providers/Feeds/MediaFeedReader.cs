using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Providers.Feeds;

public static class MediaFeedReader
{
    public static async Task<PageMetadata> ReadAsync(MediaDocument first, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var firstPage = MediaFeedParser.Parse(first.Text, first.Url, request.MaximumPlaylistEntries);
        var seenPages = new HashSet<string>(StringComparer.Ordinal) { Normalize(first.Url) };
        var entries = new List<NativePlaylistEntry>(); var byId = new Dictionary<string, NativePlaylistEntry>(StringComparer.Ordinal);
        var document = first; var page = firstPage; var totalCharacters = first.Text.Length;
        for (var pageNumber = 0; ; pageNumber++)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var entry in page.Metadata.Entries)
            {
                if (byId.TryGetValue(entry.Id!, out var prior))
                {
                    if (prior.Title != entry.Title || prior.Available != entry.Available ||
                        !prior.ResolvedMedia!.Media.Select(item => item.Url.AbsoluteUri).SequenceEqual(entry.ResolvedMedia!.Media.Select(item => item.Url.AbsoluteUri)))
                        throw new InvalidDataException("The feed reused an episode identifier for changed media.");
                    continue;
                }
                if (entries.Count == request.MaximumPlaylistEntries) throw new InvalidDataException("The media feed exceeds its item limit; it was not truncated.");
                var positioned = entry with { Position = entries.Count + 1 }; entries.Add(positioned); byId.Add(entry.Id!, positioned);
            }
            if (page.Next is null) break;
            if (pageNumber >= 99 || !seenPages.Add(Normalize(page.Next))) throw new InvalidDataException("The media feed repeats pagination or exceeds its page limit.");
            document = await http.GetDocumentAsync(page.Next, 5 * 1024 * 1024, ct).ConfigureAwait(false);
            if (Normalize(document.Url) != Normalize(page.Next) && !seenPages.Add(Normalize(document.Url))) throw new InvalidDataException("The media feed redirected to a previously read page.");
            totalCharacters = checked(totalCharacters + document.Text.Length);
            if (totalCharacters > 32 * 1024 * 1024) throw new InvalidDataException("The combined media feed exceeds its data limit.");
            page = MediaFeedParser.Parse(document.Text, document.Url, request.MaximumPlaylistEntries, firstPage.Metadata.Id);
            if (page.ExplicitId != firstPage.ExplicitId || page.Metadata.Title != firstPage.Metadata.Title) throw new InvalidDataException("Feed pagination changed the collection identity.");
        }
        return firstPage.Metadata with { Entries = entries };
    }
    private static string Normalize(Uri url) => new UriBuilder(url) { Fragment = "" }.Uri.AbsoluteUri;
}
