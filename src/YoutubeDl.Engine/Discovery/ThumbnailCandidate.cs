using System.Globalization;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Discovery;

public sealed record ThumbnailCandidate
{
    public required Uri Url { get; init; }
    public string? Id { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
}

internal static class ThumbnailCatalog
{
    internal const int MaximumCount = 256;
    internal static PageMetadata Prepare(PageMetadata page)
    {
        if (page.Thumbnails.Count > MaximumCount) throw new InvalidDataException("The thumbnail inventory exceeds its count limit.");
        var items = page.Thumbnails.ToList();
        Uri? preferred = null;
        if (!string.IsNullOrWhiteSpace(page.ThumbnailUrl))
        {
            if (!Uri.TryCreate(page.ThumbnailUrl, UriKind.Absolute, out preferred)) throw new InvalidDataException("The preferred thumbnail address is invalid.");
            if (!items.Any(item => item.Url == preferred)) items.Add(new() { Url = preferred, Id = items.Count == 0 ? "0" : "preferred" });
        }
        if (items.Count > MaximumCount) throw new InvalidDataException("The thumbnail inventory exceeds its count limit.");
        var urls = new HashSet<string>(StringComparer.Ordinal); var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var result = new List<ThumbnailCandidate>();
        foreach (var item in items)
        {
            if (item.Id is { Length: > 1024 }) throw new InvalidDataException("A thumbnail identifier exceeds its size limit.");
            if (!item.Url.IsAbsoluteUri || item.Url.Scheme is not ("http" or "https") || item.Url.UserInfo.Length != 0 || item.Url.Fragment.Length != 0)
                throw new InvalidDataException("The provider returned an invalid thumbnail address.");
            if (!urls.Add(item.Url.AbsoluteUri)) continue;
            var id = string.IsNullOrEmpty(item.Id) ? result.Count.ToString(CultureInfo.InvariantCulture) : Regex.Replace(item.Id, "[^A-Za-z0-9_-]", "_", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (id.Length > 64) id = id[..64];
            if (id.Length == 0) id = result.Count.ToString(CultureInfo.InvariantCulture);
            var unique = id; for (var index = 2; !ids.Add(unique); index++) unique = id + "-" + index.ToString(CultureInfo.InvariantCulture);
            result.Add(item with { Id = unique, Width = item.Width is > 0 ? item.Width : null, Height = item.Height is > 0 ? item.Height : null });
        }
        preferred ??= result.OrderByDescending(item => (long)(item.Width ?? 0) * (item.Height ?? 0)).ThenByDescending(item => item.Width ?? 0).ThenByDescending(item => item.Height ?? 0).FirstOrDefault()?.Url;
        return page with { ThumbnailUrl = preferred?.AbsoluteUri, Thumbnails = result };
    }
}
