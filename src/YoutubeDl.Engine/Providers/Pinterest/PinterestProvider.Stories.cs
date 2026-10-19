using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine.Discovery;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Pinterest;

public sealed partial class PinterestProvider
{
    private static PageMetadata ReadStory(NativeDownloadRequest request, JsonElement pin, string pinId, string title, string? description, string? uploader, Uri page)
    {
        var projections = Array(pin, "__storyProjections");
        var storyIds = projections.Select(item => Text(item, "id")).Distinct(StringComparer.Ordinal).ToArray();
        if (storyIds.Length == 1 && storyIds[0] is null) throw new NotSupportedException("Pinterest did not expose an identified story layout.");
        if (storyIds.Length != 1 || storyIds[0] is not { Length: > 0 and <= 256 } global) throw new InvalidDataException("Pinterest returned conflicting story identities.");
        string decoded;
        try { decoded = Encoding.UTF8.GetString(Convert.FromBase64String(global)); }
        catch (FormatException) { throw new InvalidDataException("Pinterest returned an invalid global story identity."); }
        const string prefix = "StoryPinData:";
        if (!decoded.StartsWith(prefix, StringComparison.Ordinal) || !NumericId(decoded[prefix.Length..]) || Convert.ToBase64String(Encoding.UTF8.GetBytes(decoded)) != global)
            throw new InvalidDataException("Pinterest returned an unsupported typed story identity.");
        var storyId = decoded[prefix.Length..];
        if (title == "Pinterest pin " + pinId && projections.Select(item => Text(Object(item, "metadata"), "pinTitle")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) is { } storyTitle)
            title = storyTitle;
        foreach (var projection in projections)
        {
            if (!projection.TryGetProperty("isDeleted", out var deleted) || deleted.ValueKind != JsonValueKind.False)
                throw new NotSupportedException("This Pinterest story is deleted or unavailable.");
            if (Text(projection, "entityId") is { } entity && entity != storyId) throw new InvalidDataException("Pinterest returned conflicting native/global story identities.");
        }
        var canonical = projections.FirstOrDefault(item => Array(item, "pages") is { Length: > 0 } pages && pages.All(value => NumericId(PageId(value))));
        if (canonical.ValueKind == JsonValueKind.Undefined && projections.All(item => Array(item, "pages") is { Length: 1 } pages && Array(pages[0], "blocks") is { Length: 1 } blocks &&
            Integer(blocks[0], "blockType") == 3 && Text(blocks[0], "__typename") == "StoryPinVideoBlock")) canonical = projections[0];
        var canonicalPages = Array(canonical, "pages");
        if (canonicalPages.Length is < 1 or > 200) throw new NotSupportedException("Pinterest did not expose a bounded story with identified pages.");
        if (canonicalPages.Select(PageId).Distinct(StringComparer.Ordinal).Count() != canonicalPages.Length) throw new InvalidDataException("Pinterest repeated a story page identity.");
        if (projections.Any(item => Array(item, "pages").Length != canonicalPages.Length)) throw new InvalidDataException("Pinterest returned incomplete or conflicting story page inventories.");
        var entries = new List<NativePlaylistEntry>(); var videos = new List<PageMetadata>(); var overlays = false; double duration = 0;
        for (var pageIndex = 0; pageIndex < canonicalPages.Length; pageIndex++)
        {
            var canonicalPage = canonicalPages[pageIndex]; var pageId = PageId(canonicalPage) ?? "single"; var blocks = Array(canonicalPage, "blocks");
            if (blocks.Length is < 1 or > 64) throw new InvalidDataException("Pinterest returned an empty or excessive story page.");
            var related = projections.Select(projection => Array(projection, "pages")[pageIndex]).ToArray();
            if (related.Any(value => PageId(value) is { } other && pageId != "single" && other != pageId || Array(value, "blocks").Length != blocks.Length || Integer(value, "layout") != Integer(canonicalPage, "layout")))
                throw new InvalidDataException("Pinterest changed a story page identity, layout or block inventory.");
            overlays |= Array(canonicalPage, "musicAttributions").Length > 0 || Integer(canonicalPage, "layout") != 0;
            for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
            {
                if (entries.Count >= request.MaximumPlaylistEntries || entries.Count >= 10000) throw new NotSupportedException("The Pinterest story exceeds its configured complete-inventory limit.");
                var block = blocks[blockIndex]; var blockType = Integer(block, "blockType");
                var counterparts = related.Select(value => Array(value, "blocks")[blockIndex]).ToArray();
                if (counterparts.Any(value => Integer(value, "blockType") != blockType || Text(value, "__typename") != Text(block, "__typename")))
                    throw new InvalidDataException("Pinterest changed a story block identity/type.");
                var position = entries.Count + 1;
                if (blockType != 3 || Text(block, "__typename") != "StoryPinVideoBlock")
                {
                    overlays |= blockType is not 1;
                    entries.Add(new(position, pinId + "-" + pageId + "-" + (blockIndex + 1), title + " — page " + (pageIndex + 1), null, null, null, false)); continue;
                }
                var canonicalVideo = NormalizeStoryVideo(Object(block, "videoDataV2"));
                var asset = canonicalVideo.Asset ?? throw new NotSupportedException("Pinterest omitted a complete story video resource.");
                foreach (var counterpart in counterparts)
                {
                    var other = NormalizeStoryVideo(Object(counterpart, "videoDataV2"));
                    if (other.Asset is not null && other.Asset != asset) throw new InvalidDataException("Pinterest story projections refer to different block video assets.");
                    MergeStoryFields(canonicalVideo.Formats, other.Formats); MergeStoryFields(canonicalVideo.Captions, other.Captions);
                }
                var durations = canonicalVideo.Formats.Select(pair => pair.Value is JsonObject format ? Number(JsonSerializer.SerializeToElement(format), "duration") : null).Where(value => value is not null).Distinct().ToArray();
                if (durations.Length != 1 || durations[0] is not > 0) throw new InvalidDataException("Pinterest omitted or disagreed on the story video's complete duration.");
                foreach (var key in canonicalVideo.Formats.Where(pair => pair.Value is not JsonObject format || format["url"] is null).Select(pair => pair.Key).ToArray()) canonicalVideo.Formats.Remove(key);
                var video = new JsonObject { ["duration"] = durations[0], ["videoList"] = canonicalVideo.Formats, ["videoListForCaptions"] = canonicalVideo.Captions };
                using var data = JsonDocument.Parse(video.ToJsonString());
                var logicalId = pinId + "-" + pageId + "-" + asset;
                var media = Hosted(logicalId, data.RootElement, title + " — page " + (pageIndex + 1), description, uploader, page, requireVideoId: false) with {
                    ArchiveIdentity = new("pinterest:story", logicalId) };
                videos.Add(media); duration += media.DurationSeconds!.Value;
                var style = Object(block, "blockStyle");
                overlays |= style.ValueKind != JsonValueKind.Object || Number(style, "width") != 100 || Number(style, "height") != 100 || Number(style, "xCoord") != 0 || Number(style, "yCoord") != 0 ||
                    counterparts.Any(value => Text(value, "text") is { Length: > 0 });
                entries.Add(new(position, logicalId, media.Title, media.Media.First().Url, media.DurationSeconds, media.ThumbnailUrl, true, media, media.ArchiveIdentity));
            }
        }
        if (videos.Count == 0) throw new NotSupportedException("This Pinterest story exposes no supported video clips.");
        foreach (var projection in projections)
            if (Number(projection, "totalVideoDuration") is { } declared && Math.Abs(declared / 1000 - duration) > 0.01)
                throw new InvalidDataException("Pinterest returned an incomplete or inconsistent story video-duration inventory.");
        var warnings = overlays ? new[] { "Saved story clips contain the offered videos. Separate text, music, cropping and interactive page overlays are not rendered into these files." } : System.Array.Empty<string>();
        if (canonicalPages.Length == 1 && entries.Count == 1 && videos.Count == 1)
            return videos[0] with { Id = pinId, ArchiveIdentity = new("pinterest", pinId), Title = title, Warnings = warnings };
        return new() { Id = pinId, PlaylistId = pinId, ArchiveIdentity = new("pinterest", pinId), Extractor = "pinterest:story", IsPlaylist = true, Title = title, Description = description, Uploader = uploader,
            Entries = entries.AsReadOnly(), SourcePageUrl = page, Warnings = warnings };
    }
    private static string? PageId(JsonElement page)
    {
        var id = Text(page, "storyPageId") ?? Text(page, "pageId");
        if (Text(page, "storyPageId") is { } one && Text(page, "pageId") is { } two && one != two) throw new InvalidDataException("Pinterest returned conflicting page identities.");
        return id;
    }
    private sealed record StoryVideo(JsonObject Formats, JsonObject Captions, string? Asset);
    private static StoryVideo NormalizeStoryVideo(JsonElement video)
    {
        var formats = new JsonObject(); var captions = new JsonObject(); string? asset = null;
        if (video.ValueKind != JsonValueKind.Object) return new(formats, captions, asset);
        if (video.EnumerateObject().Count() > 64) throw new InvalidDataException("Pinterest returned too many story video declarations.");
        foreach (var group in video.EnumerateObject().Where(property => property.Name.StartsWith("videoList", StringComparison.Ordinal) || property.Name == "v_hlsv4_video_list"))
        {
            if (group.Value.ValueKind == JsonValueKind.Null) continue;
            if (group.Value.ValueKind != JsonValueKind.Object || group.Value.EnumerateObject().Count() > 64) throw new InvalidDataException("Pinterest returned an unsupported story format group.");
            foreach (var format in group.Value.EnumerateObject())
            {
                if (format.Name.StartsWith("__", StringComparison.Ordinal) || format.Value.ValueKind == JsonValueKind.Null) continue;
                if (format.Value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Pinterest returned a malformed story format.");
                if (Text(format.Value, "url") is { } offered)
                {
                    var resource = Resource(offered);
                    if (asset is not null && asset != resource.Asset) throw new InvalidDataException("Pinterest story variants refer to different assets.");
                    asset = resource.Asset;
                }
                var fields = new JsonObject();
                foreach (var key in new[] { "url", "width", "height", "thumbnail", "duration" }) if (format.Value.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null) fields[key] = JsonNode.Parse(value.GetRawText());
                if (fields.Count > 0) MergeStoryFields(formats, new JsonObject { [format.Name] = fields });
                if (Object(format.Value, "captionsUrls").ValueKind == JsonValueKind.Object)
                    MergeStoryFields(captions, new JsonObject { [format.Name] = new JsonObject { ["captionsUrls"] = JsonNode.Parse(Object(format.Value, "captionsUrls").GetRawText()) } });
            }
        }
        return new(formats, captions, asset);
    }
    private static void MergeStoryFields(JsonObject target, JsonObject source)
    {
        foreach (var pair in source)
        {
            if (!target.TryGetPropertyValue(pair.Key, out var old) || old is null) target[pair.Key] = pair.Value?.DeepClone();
            else if (old is JsonObject one && pair.Value is JsonObject two) MergeStoryFields(one, two);
            else if (!JsonNode.DeepEquals(old, pair.Value)) throw new InvalidDataException("Pinterest returned conflicting story media facts.");
        }
    }
}
