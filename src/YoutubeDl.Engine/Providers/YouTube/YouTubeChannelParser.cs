using System.Text.Json;
using System.Text.RegularExpressions;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.YouTube;

internal sealed record YouTubeChannelBatch(IReadOnlyList<NativePlaylistEntry> Entries, string? Continuation, string? Target);
internal sealed record YouTubeChannelPage(string Id, string Title, string? Description, string? Thumbnail,
    IReadOnlyDictionary<string, Uri> Tabs, string? SelectedTab, YouTubeChannelBatch? Batch);

internal static class YouTubeChannelParser
{
    internal static YouTubeChannelPage ParsePage(string json, YouTubeChannelAddress address, int maximumEntries, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var metadata = Object(Object(root, "metadata"), "channelMetadataRenderer");
        var id = Text(metadata, "externalId");
        if (!YouTubeChannelAddress.IsChannelId(id)) throw new NotSupportedException("YouTube did not expose accessible channel metadata.");
        if (address.ChannelId is not null && id != address.ChannelId) throw new InvalidDataException("YouTube returned a different channel identity.");
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { address.RootPath, "/channel/" + id };
        foreach (var value in Array(metadata, "ownerUrls").Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString())
            .Concat([Text(metadata, "vanityChannelUrl"), Text(metadata, "channelUrl")]))
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var url) && YouTubeChannelAddress.TryParse(url, out var alias) && alias!.Tab is null)
            {
                if (alias.ChannelId is not null && alias.ChannelId != id) throw new InvalidDataException("The channel metadata contains inconsistent identities.");
                aliases.Add(alias.RootPath);
            }
        }
        var tabs = new Dictionary<string, Uri>(StringComparer.Ordinal);
        string? selected = null; YouTubeChannelBatch? batch = null; var selectedCount = 0;
        foreach (var wrapper in Array(Object(Object(root, "contents"), "twoColumnBrowseResultsRenderer"), "tabs"))
        {
            ct.ThrowIfCancellationRequested();
            var tab = Object(wrapper, "tabRenderer");
            var active = True(tab, "selected");
            if (active) selectedCount++;
            var endpoint = Object(tab, "endpoint");
            var path = Text(Object(Object(endpoint, "commandMetadata"), "webCommandMetadata"), "url");
            if (!Uri.TryCreate(new Uri("https://www.youtube.com/"), path, out var url)) continue;
            var kind = url.AbsolutePath.TrimEnd('/').Split('/').Last();
            if (!YouTubeChannelAddress.IsMediaTab(kind)) continue;
            if (!YouTubeChannelAddress.TryParse(url, out var target) || target!.Tab != kind || !aliases.Contains(target.RootPath) ||
                target.ChannelId is not null && target.ChannelId != id || Text(Object(endpoint, "browseEndpoint"), "browseId") is { } browseId && browseId != id)
                throw new InvalidDataException("The channel tab points to another channel or an unsupported address.");
            if (!tabs.TryAdd(kind, target.Url)) throw new InvalidDataException("The channel contains duplicate media tabs.");
            if (active)
            {
                selected = kind;
                var content = Object(tab, "content");
                var grid = Object(content, "richGridRenderer");
                if (grid.ValueKind == JsonValueKind.Undefined) grid = Object(content, "gridRenderer");
                if (grid.ValueKind == JsonValueKind.Undefined) throw new NotSupportedException("This channel tab layout requires additional native support.");
                batch = ParseItems(Items(grid, grid.TryGetProperty("contents", out _) ? "contents" : "items"), Text(grid, "targetId"), maximumEntries, ct);
            }
        }
        if (selectedCount != 1 || address.Tab is not null && selected != address.Tab)
            throw new NotSupportedException("YouTube did not return the requested channel tab; it may be unavailable or restricted.");
        return new(id!, Text(metadata, "title") ?? id!, Text(metadata, "description"), Thumbnail(Array(Object(metadata, "avatar"), "thumbnails")), tabs, selected, batch);
    }

    internal static YouTubeChannelBatch ParseContinuation(string json, string target, int maximumEntries, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(json);
        var matches = new List<JsonElement>();
        foreach (var scope in new[] { "onResponseReceivedActions", "onResponseReceivedEndpoints" })
            foreach (var action in Array(document.RootElement, scope))
            {
                var append = Object(action, "appendContinuationItemsAction");
                if (Text(append, "targetId") == target) matches.Add(append);
            }
        if (matches.Count != 1) throw new InvalidDataException("The channel continuation did not target the selected feed exactly once.");
        return ParseItems(Items(matches[0], "continuationItems"), target, maximumEntries, ct);
    }

    internal static YouTubeChannelBatch ParseItems(IReadOnlyList<JsonElement> items, string? target, int maximumEntries, CancellationToken ct)
    {
        var entries = new List<NativePlaylistEntry>(); string? continuation = null;
        if (items.Count > maximumEntries + 1) throw new InvalidDataException("The channel page exceeds the configured item limit.");
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            var next = Object(item, "continuationItemRenderer");
            if (next.ValueKind != JsonValueKind.Undefined)
            {
                var token = Text(Object(Object(next, "continuationEndpoint"), "continuationCommand"), "token");
                if (string.IsNullOrWhiteSpace(token) || token.Length > 16384 || continuation is not null)
                    throw new InvalidDataException("The channel continuation is missing, oversized or ambiguous.");
                continuation = token; continue;
            }
            if (Object(item, "messageRenderer").ValueKind != JsonValueKind.Undefined)
                throw new NotSupportedException("YouTube returned a channel availability message instead of upload data.");
            if (Object(item, "adSlotRenderer").ValueKind != JsonValueKind.Undefined) continue;
            var content = Object(Object(item, "richItemRenderer"), "content");
            if (content.ValueKind == JsonValueKind.Undefined) content = item;
            var modern = Object(content, "lockupViewModel");
            var shorts = Object(content, "shortsLockupViewModel");
            var legacy = Object(content, "videoRenderer");
            if (legacy.ValueKind == JsonValueKind.Undefined) legacy = Object(content, "gridVideoRenderer");
            if (legacy.ValueKind == JsonValueKind.Undefined) legacy = Object(content, "reelItemRenderer");
            string? id, title, thumbnail; double? duration; var available = true;
            if (modern.ValueKind != JsonValueKind.Undefined)
            {
                if (Text(modern, "contentType") != "LOCKUP_CONTENT_TYPE_VIDEO") throw new NotSupportedException("This channel feed contains a non-video collection.");
                id = Text(modern, "contentId");
                title = ReadText(Object(Object(modern, "metadata"), "lockupMetadataViewModel"), "title");
                var image = Object(Object(modern, "contentImage"), "thumbnailViewModel");
                thumbnail = Thumbnail(Array(Object(image, "image"), "sources"));
                duration = Array(image, "overlays").SelectMany(overlay => Array(Object(overlay, "thumbnailBottomOverlayViewModel"), "badges"))
                    .Select(badge => Duration(Text(Object(badge, "thumbnailBadgeViewModel"), "text"))).FirstOrDefault(value => value is not null);
            }
            else if (shorts.ValueKind != JsonValueKind.Undefined)
            {
                id = Text(Object(Object(Object(shorts, "onTap"), "innertubeCommand"), "reelWatchEndpoint"), "videoId");
                title = ReadText(Object(shorts, "overlayMetadata"), "primaryText");
                var image = Object(shorts, "thumbnailViewModel");
                if (Object(image, "thumbnailViewModel").ValueKind != JsonValueKind.Undefined) image = Object(image, "thumbnailViewModel");
                thumbnail = Thumbnail(Array(Object(image, "image"), "sources"));
                duration = null;
            }
            else if (legacy.ValueKind != JsonValueKind.Undefined)
            {
                id = Text(legacy, "videoId"); title = ReadText(legacy, "title") ?? ReadText(legacy, "headline");
                thumbnail = Thumbnail(Array(Object(legacy, "thumbnail"), "thumbnails"));
                duration = Number(legacy, "lengthSeconds") ?? Duration(ReadText(legacy, "lengthText"));
                available = !(legacy.TryGetProperty("isPlayable", out var playable) && playable.ValueKind == JsonValueKind.False);
            }
            else throw new NotSupportedException("This channel feed item requires additional native support.");
            if (id is not null && !Regex.IsMatch(id, @"\A[A-Za-z0-9_-]{11}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) throw new InvalidDataException("The channel contains an invalid video identity.");
            if (id is null && available) throw new InvalidDataException("The channel video has no identity.");
            if (entries.Count >= maximumEntries) throw new InvalidDataException("The channel page exceeds the configured item limit.");
            entries.Add(new(entries.Count + 1, id, title ?? "Unavailable video", id is null ? null : new("https://www.youtube.com/watch?v=" + id), duration, thumbnail, available && id is not null));
        }
        if (continuation is not null && (string.IsNullOrWhiteSpace(target) || target.Length > 256)) throw new InvalidDataException("The channel page has no supported continuation target.");
        return new(entries, continuation, target);
    }

    private static string? ReadText(JsonElement parent, string name)
    {
        var value = Object(parent, name);
        return Text(value, "simpleText") ?? Text(value, "content") ?? (Array(value, "runs") is { Length: > 0 } runs ? string.Concat(runs.Select(run => Text(run, "text"))) : null);
    }
    private static JsonElement[] Items(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var values) || values.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The channel feed has no explicit item array.");
        return values.EnumerateArray().ToArray();
    }
    private static string? Thumbnail(IEnumerable<JsonElement> sources) => sources.Where(source => Uri.TryCreate(Text(source, "url"), UriKind.Absolute, out var url) &&
        url.Scheme == "https" && url.UserInfo.Length == 0).OrderByDescending(source => Number(source, "width") ?? 0).Select(source => Text(source, "url")).FirstOrDefault();
}
