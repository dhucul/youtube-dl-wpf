using System.Globalization;

namespace YoutubeDl.Engine.Providers.Panopto;

internal sealed record PanoptoCollectionAddress(Uri Page, string? FolderId, string? PlaylistId, string? VideoId, string Query, int SortColumn, bool SortAscending, bool ExplicitSort, int PageSize)
{
    internal string Origin => Page.GetLeftPart(UriPartial.Authority);
    internal bool Playlist => PlaylistId is not null;
    internal static bool IsList(Uri url) => url.IsAbsoluteUri && url.AbsolutePath.Equals("/Panopto/Pages/Sessions/List.aspx", StringComparison.OrdinalIgnoreCase);
    internal static bool IsPlaylist(Uri url) => PanoptoAddress.Matches(url) && url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Any(part => Uri.UnescapeDataString(part.Split('=', 2)[0]).Equals("pid", StringComparison.OrdinalIgnoreCase));
    internal static bool Matches(Uri url) => IsList(url) || IsPlaylist(url);
    internal static PanoptoCollectionAddress Parse(Uri url)
    {
        if (!Matches(url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length > 0)
            throw new NotSupportedException("Use an HTTPS Panopto recording collection URL.");
        if (IsPlaylist(url))
        {
            var values = Parts(url.Query);
            return new(url, null, GuidText(values["pid"], "playlist"), values.TryGetValue("id", out var id) ? GuidText(id, "recording") : null, "", 1, false, false, 100);
        }
        var state = Parts(url.Fragment);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "folderID", "query", "sortColumn", "sortAscending", "maxResults", "page", "view" };
        if (state.Keys.Any(key => !allowed.Contains(key))) throw new NotSupportedException("This Panopto collection URL uses a filter that needs additional native support.");
        if (state.TryGetValue("page", out var page) && page != "0") throw new NotSupportedException("Use the start of a Panopto collection; select recording positions with playlist settings.");
        var column = state.TryGetValue("sortColumn", out var sort) ? Integer(sort, 0, 99, "sort column") : 1;
        var ascending = state.TryGetValue("sortAscending", out var direction) ? direction.ToLowerInvariant() switch { "true" => true, "false" => false, _ => throw new ArgumentException("Panopto sort direction must be true or false.") } : false;
        var size = state.TryGetValue("maxResults", out var maximum) ? Integer(maximum, 1, 250, "page size") : 100;
        var query = state.GetValueOrDefault("query", "");
        if (query.Length > 4096 || query.Any(char.IsControl)) throw new ArgumentException("The Panopto search text is invalid or excessive.");
        return new(url, state.TryGetValue("folderID", out var folder) ? GuidText(folder, "folder") : null, null, null, query, column, ascending, state.ContainsKey("sortColumn"), size);
    }
    internal static Uri Single(Uri url)
    {
        var values = Parts(url.Query);
        if (!values.TryGetValue("id", out var id)) throw new NotSupportedException("This Panopto playlist link does not identify a displayed recording.");
        _ = GuidText(id, "recording");
        var builder = new UriBuilder(url) { Query = string.Join('&', values.Where(pair => !pair.Key.Equals("pid", StringComparison.OrdinalIgnoreCase) && !pair.Key.Equals("advance", StringComparison.OrdinalIgnoreCase))
            .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))), Fragment = "" };
        return builder.Uri;
    }
    private static Dictionary<string, string> Parts(string value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in value.TrimStart('?', '#').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2) throw new ArgumentException("Panopto collection parameters require named values.");
            var key = Uri.UnescapeDataString(pair[0]); var decoded = Uri.UnescapeDataString(pair[1]);
            if (decoded.Length >= 2 && decoded[0] == '"' && decoded[^1] == '"') decoded = decoded[1..^1];
            if (!result.TryAdd(key, decoded)) throw new ArgumentException("Panopto collection parameters must not repeat.");
        }
        return result;
    }
    private static string GuidText(string value, string kind) => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty ? id.ToString("D") : throw new ArgumentException("The Panopto " + kind + " identifier is invalid.");
    private static int Integer(string value, int min, int max, string name) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result >= min && result <= max ?
        result : throw new ArgumentException("The Panopto " + name + " is outside its supported range.");
}
