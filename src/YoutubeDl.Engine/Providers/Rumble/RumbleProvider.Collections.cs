using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Rumble;

public sealed partial class RumbleProvider
{
    private static async Task<PageMetadata> ReadCollectionAsync(NativeDownloadRequest request, RumbleCollectionAddress address, MediaHttp http, CancellationToken ct)
    {
        using var session = http.WithHeaders(new Dictionary<string, string> { ["User-Agent"] = string.IsNullOrWhiteSpace(request.UserAgent) ? "YoutubeDlNative/" + NativeEngine.Version : request.UserAgent,
            ["Referer"] = string.IsNullOrWhiteSpace(request.Referer) ? request.Url.AbsoluteUri : request.Referer });
        var entries = new List<NativePlaylistEntry>(); var seen = new HashSet<string>(StringComparer.Ordinal); var numericIds = new HashSet<long>();
        var next = address.Url; var pageNumber = address.Page; string? collectionId = null, title = null, description = null;
        var warnings = new List<string>();
        if (address.Page > 1) warnings.Add("This listing starts at the requested page " + address.Page + "; earlier pages are not included.");
        for (var pages = 0; next is not null; pages++)
        {
            ct.ThrowIfCancellationRequested();
            if (pages >= 1000) throw new InvalidDataException("Rumble exceeded the native pagination limit; the listing was not truncated.");
            var document = await session.GetDocumentAsync(next, 8 * 1024 * 1024, ct).ConfigureAwait(false);
            var final = RumbleCollectionAddress.Parse(document.Url);
            if (!address.SameListing(final) || final.Page != pageNumber) throw new InvalidDataException("Rumble redirected to another listing or page.");
            if (RumblePage.Meta(document.Text, "og:url") is not { } canonical) throw new InvalidDataException("Rumble omitted the collection's canonical identity.");
            var declared = RumbleCollectionAddress.Parse(new Uri(document.Url, canonical));
            if (!address.SameListing(declared) || declared.Page != pageNumber) throw new InvalidDataException("Rumble returned the wrong collection page.");
            var identities = new List<Dictionary<string, string>>();
            foreach (Match tag in Regex.Matches(document.Text, @"<div\b(?<attributes>[^>]{0,8192})>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                if (!tag.Groups["attributes"].Value.Contains("followed-channel", StringComparison.Ordinal)) continue;
                var attributes = RumblePage.Attributes(tag.Groups["attributes"].Value, "class", "data-type", "data-slug", "data-id", "data-title");
                if (attributes.GetValueOrDefault("class")?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains("followed-channel") == true && attributes.ContainsKey("data-type")) identities.Add(attributes);
            }
            if (identities.Count != 1) throw new NotSupportedException("Rumble did not expose one supported collection header.");
            var header = identities[0];
            if (header.GetValueOrDefault("data-type") != address.Type || !string.Equals(header.GetValueOrDefault("data-slug"), address.Slug, StringComparison.OrdinalIgnoreCase) ||
                !long.TryParse(header.GetValueOrDefault("data-id"), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                throw new InvalidDataException("Rumble returned a mismatched collection header.");
            var currentId = address.Type + ":" + id.ToString(CultureInfo.InvariantCulture);
            if (address.Type == "channel" && Regex.Match(address.Slug, @"\Ac-(?<id>[0-9]+)\z", RegexOptions.CultureInvariant) is { Success: true } numericSlug &&
                (!long.TryParse(numericSlug.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var claimedId) || claimedId != id))
                throw new InvalidDataException("Rumble returned another numeric channel identity.");
            if (collectionId is not null && collectionId != currentId) throw new InvalidDataException("The Rumble collection identity changed during pagination.");
            collectionId ??= currentId; title ??= header.GetValueOrDefault("data-title") ?? RumblePage.Meta(document.Text, "og:title") ?? address.Slug;
            description ??= RumblePage.Meta(document.Text, "og:description") ?? RumblePage.Meta(document.Text, "description");
            var grids = Regex.Matches(document.Text, @"<rum-videos-grid\b[^>]{0,4096}>(?<body>.*?)</rum-videos-grid\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (grids.Count != 1) throw new NotSupportedException("Rumble did not return one supported video grid.");
            var payloads = new List<string>();
            foreach (Match script in Regex.Matches(grids[0].Groups["body"].Value, @"<script\b(?<attributes>[^>]{0,4096})>(?<body>.*?)</script\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                if (RumblePage.Attributes(script.Groups["attributes"].Value, "type").GetValueOrDefault("type") == "application/json") payloads.Add(script.Groups["body"].Value);
            if (payloads.Count != 1 || payloads[0].Length > 6 * 1024 * 1024) throw new InvalidDataException("Rumble returned missing, ambiguous or excessive grid data.");
            using var json = JsonDocument.Parse(payloads[0]); var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count(property => property.Name == "items") != 1 ||
                !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Rumble did not expose a complete grid inventory.");
            if (items.GetArrayLength() > request.MaximumPlaylistEntries - entries.Count) throw new InvalidDataException("Rumble exceeds the configured collection limit; the listing was not truncated.");
            foreach (var item in items.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var position = entries.Count + 1;
                if (Text(item, "object_type") != "video")
                { entries.Add(new(position, null, WebUtility.HtmlDecode(Text(item, "title") ?? "Unsupported Rumble collection item"), null, null, null, false)); continue; }
                var permalink = Text(item, "permalink_id"); var numeric = Long(item, "id");
                if (permalink is null || numeric is not > 0 || !seen.Add(permalink) || !numericIds.Add(numeric.Value)) throw new InvalidDataException("Rumble omitted or repeated a listing video identity.");
                _ = RumbleAddress.NumericId(permalink);
                if (Text(item, "url") is not { } raw || !Uri.TryCreate(document.Url, raw, out var url)) throw new InvalidDataException("Rumble omitted a listing video link.");
                var video = RumbleAddress.Parse(url);
                if (video.Embed || video.Id != permalink) throw new InvalidDataException("Rumble returned inconsistent listing video identities.");
                if (Text(item, "relative_url") is { } relative)
                {
                    var relativeAddress = RumbleAddress.Parse(new Uri(document.Url, relative));
                    if (relativeAddress.Embed || relativeAddress.Id != permalink) throw new InvalidDataException("Rumble returned a different relative video identity.");
                }
                var available = Text(item, "visibility") == "public" && !new[] { "is_age_restricted", "is_processing", "live", "live_placeholder", "livestream_status", "availability" }.Any(name => Active(item, name));
                entries.Add(new(position, permalink, WebUtility.HtmlDecode(Text(item, "title") ?? permalink), url, Number(item, "duration"),
                    Text(item, "thumb") is { Length: > 0 } thumbnail ? Resource(thumbnail).AbsoluteUri : null, available));
            }
            var nextLinks = new List<Uri>();
            foreach (Match tag in Regex.Matches(document.Text, @"<link\b(?<attributes>[^>]{0,8192})>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                var attributes = RumblePage.Attributes(tag.Groups["attributes"].Value, "rel", "href");
                if (attributes.GetValueOrDefault("rel")?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains("next", StringComparer.OrdinalIgnoreCase) == true)
                {
                    if (!Uri.TryCreate(document.Url, attributes.GetValueOrDefault("href"), out var link)) throw new InvalidDataException("Rumble omitted its next-page address.");
                    nextLinks.Add(link);
                }
            }
            if (nextLinks.Count > 1) throw new InvalidDataException("Rumble returned ambiguous next-page links.");
            next = nextLinks.SingleOrDefault();
            if (next is not null)
            {
                var continuation = RumbleCollectionAddress.Parse(next);
                if (!address.SameListing(continuation) || continuation.Page != pageNumber + 1 || items.GetArrayLength() == 0)
                    throw new InvalidDataException("Rumble changed, skipped, repeated or stalled its collection pagination.");
            }
            else
            {
                // A missing head continuation must not turn an advertised partial
                // listing into a complete playlist.
                foreach (Match tag in Regex.Matches(document.Text, @"<a\b(?<attributes>[^>]{0,8192})>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                {
                    var attributes = RumblePage.Attributes(tag.Groups["attributes"].Value, "href");
                    if (Uri.TryCreate(document.Url, attributes.GetValueOrDefault("href"), out var link) && RumbleCollectionAddress.IsCollection(link))
                    {
                        var candidate = RumbleCollectionAddress.Parse(link);
                        if (address.SameListing(candidate) && candidate.Page > pageNumber) throw new InvalidDataException("Rumble advertises later pages without a complete continuation.");
                    }
                }
            }
            pageNumber++;
        }
        return new() { Id = collectionId, PlaylistId = collectionId, IsPlaylist = true, Extractor = "rumble:channel", Title = title!, Description = description,
            Entries = entries, SourcePageUrl = request.Url, Warnings = warnings };
    }
}
