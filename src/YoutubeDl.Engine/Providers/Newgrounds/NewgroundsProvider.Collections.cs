using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Newgrounds;

public sealed partial class NewgroundsProvider
{
    private sealed record Reference(int Type, string Id);
    private static Task<PageMetadata> CollectionAsync(NewgroundsAddress address, NativeDownloadRequest request, MediaDocument first, MediaHttp http, CancellationToken ct)
        => address.Kind == "playlist" ? PlaylistAsync(address, request, first, http, ct) : CreatorAsync(address, request, first, http, ct);

    private static Reference[] PlaylistReferences(string html, int limit)
    {
        var list = Regex.Match(html, @"<ul\b[^>]*\bid=[""']playlist_list[""'][^>]*>(?<list>[\s\S]*?)</ul>", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        if (!list.Success) throw new NotSupportedException("Newgrounds omitted a complete public playlist membership array.");
        var results = new List<Reference>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(list.Groups["list"].Value, @"data-visual-link=[""'](?<data>[^""']+)[""']", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)))
        {
            using var document = JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups["data"].Value)); var data = document.RootElement;
            if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() != 2 || !data[0].TryGetInt32(out var type) || type is < 1 or > 4 ||
                !data[1].TryGetInt64(out var numeric) || numeric <= 0 || !NewgroundsAddress.Numeric(numeric.ToString())) throw new InvalidDataException("Newgrounds returned an invalid playlist reference.");
            var id = numeric.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!seen.Add(type + ":" + id)) throw new InvalidDataException("Newgrounds repeated a playlist source identity.");
            if (results.Count >= limit) throw new NotSupportedException("The complete Newgrounds playlist exceeds the configured inventory limit.");
            results.Add(new(type, id));
        }
        if (html.Contains("playlist_scrolled", StringComparison.Ordinal) && Regex.IsMatch(html, @"loadMore\([^\n]+https", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new NotSupportedException("This Newgrounds paginated playlist needs additional complete-inventory support.");
        return results.ToArray();
    }
    private static async Task<PageMetadata> PlaylistAsync(NewgroundsAddress address, NativeDownloadRequest request, MediaDocument first, MediaHttp http, CancellationToken ct)
    {
        var members = PlaylistReferences(first.Text, request.MaximumPlaylistEntries); var entries = new List<NativePlaylistEntry>(); var totalBytes = 0;
        foreach (var batch in members.Chunk(100))
        {
            ct.ThrowIfCancellationRequested(); var ids = "[" + string.Join(',', batch.Select(member => "[" + member.Type + "," + member.Id + "]")) + "]";
            var endpoint = new Uri("https://www.newgrounds.com/visual-links-fetch");
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(new Dictionary<string, string>
                { ["ids"] = ids, ["component_params[context]"] = "playlist", ["include_all_suitabilities"] = "0" }) };
            message.Headers.Add("X-Requested-With", "XMLHttpRequest"); message.Headers.Referrer = address.Web;
            using var response = await http.SendAsync(message, ct).ConfigureAwait(false); response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri is { } final && final != endpoint) throw new InvalidDataException("Newgrounds redirected playlist records outside their requested endpoint.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false); using var output = new MemoryStream(); var buffer = new byte[64 * 1024];
            while (true) { var count = await http.ReadAsync(stream, buffer, ct).ConfigureAwait(false); if (count == 0) break; totalBytes = checked(totalBytes + count);
                if (totalBytes > 32 * 1024 * 1024 || output.Length + count > 8 * 1024 * 1024) throw new InvalidDataException("Newgrounds playlist metadata exceeds its limit."); await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false); }
            using var document = JsonDocument.Parse(output.ToArray()); var root = document.RootElement;
            if (!True(root, "success")) throw new NotSupportedException("Newgrounds refused public playlist metadata.");
            var partials = Object(root, "partials"); var simples = Object(root, "simples");
            foreach (var member in batch)
            {
                var group = Object(partials, member.Type.ToString()); var data = Object(Object(simples, member.Type.ToString()), member.Id);
                if (group.ValueKind != JsonValueKind.Object || !group.TryGetProperty(member.Id, out var partial) || partial.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("Newgrounds omitted a declared playlist item's availability.");
                var fragment = partial.GetString()!; var playable = member.Type is 1 or 3;
                var available = playable && !fragment.Contains("visual-link-unsuitable", StringComparison.Ordinal) && !fragment.Contains("visual-link-missing", StringComparison.Ordinal) &&
                    !fragment.Contains("visual-link-unpublished", StringComparison.Ordinal) && !True(Object(data, "status"), "unpublished");
                var kind = member.Type == 3 ? "audio" : "video"; var item = new NewgroundsAddress(kind, member.Id);
                if (data.ValueKind == JsonValueKind.Object)
                {
                    if (Id(data, "id") != member.Id || Text(data, "url") is not { } page || playable && NewgroundsAddress.Parse(new Uri(page)) != item)
                        throw new InvalidDataException("Newgrounds returned another requested playlist item.");
                }
                else if (available) throw new InvalidDataException("Newgrounds omitted a playable item's identity metadata.");
                entries.Add(new(entries.Count + 1, member.Id, Text(data, "title") ?? "Unavailable submission " + member.Id, available ? item.Web : null, null, null, available,
                    ExpectedArchiveIdentity: playable ? Archive(item) : null));
            }
        }
        var finalPage = await http.GetDocumentAsync(address.Web, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        if (!PlaylistReferences(finalPage.Text, request.MaximumPlaylistEntries).SequenceEqual(members)) throw new InvalidDataException("Newgrounds changed the playlist during complete inspection.");
        var title = MatchText(first.Text, @"<div\b[^>]*\bid=[""']playlist_list_pod[""'][^>]*>[\s\S]*?<h2\b[^>]*>(?<text>[\s\S]*?)</h2>") ?? PageMetadataReader.Read(first.Text, address.Web).Title;
        return new() { Id = address.Id, PlaylistId = address.Id, Extractor = "newgrounds:playlist", IsPlaylist = true, Title = title, SourcePageUrl = address.Web, Entries = entries,
            Warnings = entries.Any(entry => !entry.Available) ? ["Unavailable, unsupported or account-filtered submissions retain their original playlist positions."] : [] };
    }

    private static async Task<PageMetadata> CreatorAsync(NewgroundsAddress address, NativeDownloadRequest request, MediaDocument first, MediaHttp http, CancellationToken ct)
    {
        var declared = CreatorCount(first.Text, address); if (declared > request.MaximumPlaylistEntries) throw new NotSupportedException("The complete Newgrounds creator inventory exceeds the configured limit.");
        var entries = new List<NativePlaylistEntry>(); var seen = new HashSet<string>(StringComparer.Ordinal); var seenPages = new HashSet<string>(StringComparer.Ordinal) { address.Web.AbsoluteUri };
        var firstEntries = CreatorEntries(first.Text, address).ToArray(); Add(firstEntries); var next = Continuation(first.Text, address, 2); var characters = first.Text.Length;
        for (var pageNumber = 2; next is not null; pageNumber++)
        {
            ct.ThrowIfCancellationRequested(); if (pageNumber > 100 || !seenPages.Add(next.AbsoluteUri)) throw new InvalidDataException("Newgrounds repeated or exceeded its creator pagination.");
            var document = await http.GetDocumentAsync(next, 8 * 1024 * 1024, ct, new Dictionary<string, string> { ["X-Requested-With"] = "XMLHttpRequest", ["Referer"] = address.Web.AbsoluteUri }).ConfigureAwait(false);
            if (document.Url != next) throw new InvalidDataException("Newgrounds redirected creator continuation outside its expected page.");
            characters = checked(characters + document.Text.Length); if (characters > 32 * 1024 * 1024) throw new InvalidDataException("Newgrounds combined creator metadata exceeds its limit.");
            using var json = JsonDocument.Parse(document.Text); var root = json.RootElement;
            if (Text(root, "url") != next.AbsoluteUri || Text(root, "fresh_url") != address.Web.AbsoluteUri) throw new InvalidDataException("Newgrounds changed the requested creator scope.");
            var items = Object(root, "items"); if (items.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Newgrounds omitted a creator page inventory.");
            var fragments = new List<string>();
            foreach (var group in items.EnumerateObject())
            {
                if (!int.TryParse(group.Name, out var year) || year is < 1900 or > 9999 || group.Value.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Newgrounds returned an unsupported creator grouping.");
                foreach (var fragment in group.Value.EnumerateArray()) { if (fragment.ValueKind != JsonValueKind.String) throw new InvalidDataException("Newgrounds returned an invalid creator item."); fragments.Add(fragment.GetString()!); }
            }
            var added = 0; foreach (var fragment in fragments) { var batch = CreatorEntries(fragment, address).ToArray(); if (batch.Length != 1) throw new InvalidDataException("Newgrounds returned ambiguous creator item markup."); Add(batch); added++; }
            var more = Text(root, "load_more") ?? throw new InvalidDataException("Newgrounds omitted its terminal creator continuation.");
            next = Continuation(more, address, pageNumber + 1); if (more.Length != 0 && next is null || added == 0 && next is not null) throw new InvalidDataException("Newgrounds ended or invalidated creator pagination early.");
        }
        if (entries.Count != declared) throw new InvalidDataException("Newgrounds did not expose the full declared creator inventory; filters or unavailable entries require additional support.");
        var final = await http.GetDocumentAsync(address.Web, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        if (CreatorCount(final.Text, address) != declared || !CreatorEntries(final.Text, address).Select(entry => entry.Id).SequenceEqual(firstEntries.Select(entry => entry.Id)))
            throw new InvalidDataException("Newgrounds changed the creator inventory during inspection.");
        return new() { Id = address.Id + ":" + address.Kind[5..], PlaylistId = address.Id + ":" + address.Kind[5..], Extractor = "newgrounds:user", IsPlaylist = true,
            Title = PageMetadataReader.Read(first.Text, address.Web).Title, Uploader = address.User, SourcePageUrl = address.Web, Entries = entries };

        void Add(IEnumerable<NativePlaylistEntry> batch)
        {
            foreach (var entry in batch) { if (!seen.Add(entry.Id!) || entries.Count >= declared) throw new InvalidDataException("Newgrounds duplicated or exceeded declared creator entries."); entries.Add(entry with { Position = entries.Count + 1 }); }
        }
    }
    private static int CreatorCount(string html, NewgroundsAddress address)
    {
        var name = address.Kind[5..];
        foreach (Match anchor in Regex.Matches(html, @"<a\b(?<attrs>[^>]*)>(?<body>[\s\S]*?)</a>", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)))
        {
            var attrs = Attributes(anchor.Groups["attrs"].Value);
            if (!attrs.GetValueOrDefault("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("user-header-button") ||
                !Uri.TryCreate(address.Web, attrs.GetValueOrDefault("href"), out var uri) || uri.AbsolutePath != "/" + name || uri.Host != address.Web.Host) continue;
            var count = Regex.Match(anchor.Groups["body"].Value, @"<strong\b[^>]*>(?<count>[0-9,]+)</strong>", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (count.Success && int.TryParse(count.Groups["count"].Value.Replace(",", ""), out var total) && total >= 0) return total;
        }
        throw new InvalidDataException("Newgrounds omitted a complete declared creator count.");
    }
    private static IEnumerable<NativePlaylistEntry> CreatorEntries(string html, NewgroundsAddress address)
    {
        var required = address.Kind == "user-audio" ? "item-audiosubmission" : "item-portalsubmission";
        foreach (Match anchor in Regex.Matches(html, @"<a\b(?<attrs>[^>]*)>(?<body>[\s\S]*?)</a>", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)))
        {
            var attrs = Attributes(anchor.Groups["attrs"].Value); var classes = attrs.GetValueOrDefault("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!classes.Contains(required)) continue;
            if (!Uri.TryCreate(address.Web, attrs.GetValueOrDefault("href"), out var uri) || !NewgroundsAddress.Matches(uri)) throw new InvalidDataException("Newgrounds returned an invalid creator entry address.");
            var item = NewgroundsAddress.Parse(uri); if (item.Kind != (address.Kind == "user-audio" ? "audio" : "video")) throw new InvalidDataException("Newgrounds changed the selected creator content type.");
            var title = attrs.GetValueOrDefault("title") ?? MatchText(anchor.Groups["body"].Value, @"<h4\b[^>]*>(?<text>[\s\S]*?)</h4>") ?? item.Id;
            yield return new(0, item.Id, title, item.Web, null, null, true, ExpectedArchiveIdentity: Archive(item));
        }
    }
    private static Uri? Continuation(string html, NewgroundsAddress address, int expectedPage)
    {
        const string marker = "UsersContentController.loadMore("; var start = html.IndexOf(marker, StringComparison.Ordinal); if (start < 0) return null;
        var match = Regex.Match(html[start..], @",\s*(?<url>""(?:\\.|[^""\\])*"")\s*,", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) throw new InvalidDataException("Newgrounds continuation is not a literal URL.");
        var raw = JsonSerializer.Deserialize<string>(match.Groups["url"].Value);
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var next) || next.AbsoluteUri != address.Web.AbsoluteUri + "?page=" + expectedPage)
            throw new InvalidDataException("Newgrounds continuation changed creator, page order or filters.");
        return next;
    }
    private static Dictionary<string, string> Attributes(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(raw, @"(?<key>[A-Za-z_][A-Za-z0-9_:-]*)\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)')", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            if (!result.TryAdd(match.Groups["key"].Value, WebUtility.HtmlDecode(match.Groups["double"].Success ? match.Groups["double"].Value : match.Groups["single"].Value))) throw new InvalidDataException("Newgrounds repeated an HTML identity attribute.");
        return result;
    }
}
