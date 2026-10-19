using System.Globalization;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.MicrosoftLearn;

public sealed partial class MicrosoftLearnProvider
{
    private static async Task<PageMetadata> CollectionAsync(MicrosoftLearnAddress address, bool series, PageMetadata metadata, int limit, MediaHttp http, CancellationToken ct)
    {
        var path = "/api/contentbrowser/search/shows/" + address.Collection + "/episodes";
        var order = "upload_date " + (series ? "asc" : "desc");
        const int pageSize = 30;
        var endpoint = new Uri("https://learn.microsoft.com" + path + "?locale=" + address.Locale + "&$top=" + pageSize + "&$skip=0&$orderby=" + Uri.EscapeDataString(order));
        var entries = new List<NativePlaylistEntry>(); var seen = new HashSet<string>(StringComparer.Ordinal); int? count = null;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            using var response = await JsonAsync(endpoint, http, ct).ConfigureAwait(false); var root = response.RootElement;
            var total = Integer(root, "count") ?? throw new InvalidDataException("Microsoft Learn omitted the complete show count.");
            if (total > limit || total > 10000) throw new NotSupportedException("The Microsoft Learn show exceeds its configured entry limit.");
            if (count is not null && count != total) throw new InvalidDataException("The Microsoft Learn show changed its total while being enumerated.");
            count = total;
            if (!root.TryGetProperty("results", out var supplied) || supplied.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Microsoft Learn omitted a show result inventory.");
            var rows = supplied.EnumerateArray().ToArray();
            if (rows.Length > pageSize || entries.Count + rows.Length > total) throw new InvalidDataException("Microsoft Learn returned an oversized or conflicting show page.");
            foreach (var row in rows)
            {
                var uid = Text(row, "uid");
                if (Text(row, "show_uid") != address.Collection || uid is null || !AuthoringUid(uid))
                    throw new InvalidDataException("Microsoft Learn returned another show, invalid episode or repeated result.");
                var raw = Text(row, "url"); var page = raw is not null ? new Uri(new Uri("https://learn.microsoft.com/"), raw) : null;
                if (page is null || page.Host != "learn.microsoft.com") throw new InvalidDataException("Microsoft Learn returned another episode origin.");
                // The search API emits locale-free page paths; the site's client adds locale.
                var parts = page.AbsolutePath.Trim('/').Split('/');
                if (parts.Length == 4 && parts[0] == address.Locale) parts = parts[1..];
                if (parts.Length != 3 || parts[0] != "shows" || parts[1] != address.Collection || !MicrosoftLearnAddress.SlugValid(parts[2]) ||
                    page.Query.Length != 0 || page.Fragment.Length != 0 || page.UserInfo.Length != 0 || page.Scheme != "https" || !seen.Add(page.AbsolutePath.TrimEnd('/')))
                    throw new InvalidDataException("Microsoft Learn's episode URL does not match the show result.");
                var slug = parts[2];
                var id = Text(row, "entry_id") is { Length: > 0 } entry ? EntryId(entry) : null;
                if (!row.TryGetProperty("hidden", out var hidden) || hidden.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new InvalidDataException("Microsoft Learn omitted a valid episode availability flag.");
                var available = hidden.ValueKind == JsonValueKind.False;
                var duration = Number(row, "duration_in_milliseconds") is { } milliseconds ? milliseconds / 1000 : (double?)null;
                var canonical = new MicrosoftLearnAddress(address.Locale, "shows", address.Collection, slug).Page;
                entries.Add(new(entries.Count + 1, id, Text(row, "title") ?? slug,
                    available ? canonical : null, duration, Text(row, "image_url"), available, ExpectedArchiveIdentity: id is null ? null : new("microsoftlearn", id)));
            }
            var next = Text(root, "@nextLink");
            if (next is null or "")
            {
                if (entries.Count != total) throw new InvalidDataException("Microsoft Learn ended the listing before its declared show count.");
                break;
            }
            if (rows.Length == 0 || entries.Count >= total) throw new InvalidDataException("Microsoft Learn did not complete the show pagination.");
            var nextUrl = new Uri(endpoint, next);
            if (nextUrl.Scheme != "https" || nextUrl.Host != "learn.microsoft.com" || !nextUrl.IsDefaultPort || nextUrl.AbsolutePath != path || nextUrl.UserInfo.Length != 0 || nextUrl.Fragment.Length != 0)
                throw new InvalidDataException("Microsoft Learn returned an unrelated show continuation URL.");
            Dictionary<string, string> query;
            try { query = MicrosoftLearnAddress.Query(nextUrl); } catch (ArgumentException) { throw new InvalidDataException("Microsoft Learn returned invalid continuation fields."); }
            if (query.Count != 4 || query.GetValueOrDefault("locale") != address.Locale || query.GetValueOrDefault("$top") != pageSize.ToString(CultureInfo.InvariantCulture) ||
                query.GetValueOrDefault("$orderby") != order || query.GetValueOrDefault("$skip") != entries.Count.ToString(CultureInfo.InvariantCulture))
                throw new InvalidDataException("Microsoft Learn's continuation changes the listing scope or position.");
            endpoint = nextUrl;
        }
        return new() { Id = address.Collection, PlaylistId = address.Collection, Extractor = "microsoftlearn:show", IsPlaylist = true, Title = metadata.Title,
            Description = metadata.Description, Uploader = "Microsoft Learn", ThumbnailUrl = metadata.ThumbnailUrl, Entries = entries, SourcePageUrl = address.Page };
    }
}
