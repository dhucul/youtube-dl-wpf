using System.Text;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Providers.Pinterest;

internal static class PinterestStoryChecks
{
    internal const string StoryId = "5284036561638308316";
    internal static JsonNode Block(string asset = PinterestChecks.Asset, double seconds = 2) => new JsonObject { ["__typename"] = "StoryPinVideoBlock", ["blockType"] = 3,
        ["blockStyle"] = new JsonObject { ["width"] = 100, ["height"] = 100, ["xCoord"] = 0, ["yCoord"] = 0 }, ["videoDataV2"] = Data(asset, seconds) };
    private static JsonNode Data(string asset, double seconds)
    {
        var video = PinterestChecks.Pin(seconds)["videos"]!.DeepClone(); ((JsonObject)video).Remove("entityId"); ((JsonObject)video).Remove("duration");
        var formats = video["videoList"]!.DeepClone();
        foreach (var format in ((JsonObject)formats).Where(pair => pair.Value is JsonObject).Select(pair => pair.Value!))
        { format["url"] = format["url"]!.GetValue<string>().Replace(PinterestChecks.Asset, asset, StringComparison.Ordinal); format["thumbnail"] = format["thumbnail"]!.GetValue<string>().Replace(PinterestChecks.Asset, asset, StringComparison.Ordinal); }
        video["videoList"] = formats; video["videoListForCaptions"] = null; return video;
    }
    internal static JsonNode Page(string id, params JsonNode[] blocks) => new JsonObject { ["storyPageId"] = id, ["pageId"] = id, ["layout"] = 0,
        ["musicAttributions"] = new JsonArray(), ["blocks"] = new JsonArray(blocks.Select(block => block.DeepClone()).ToArray()) };
    internal static JsonNode Pin(double total, params JsonNode[] pages)
    {
        var pin = PinterestChecks.Pin(); pin["videos"] = null; pin["storyPinData"] = new JsonObject { ["id"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("StoryPinData:" + StoryId)),
            ["entityId"] = StoryId, ["isDeleted"] = false, ["totalVideoDuration"] = total * 1000, ["pages"] = new JsonArray(pages.Select(page => page.DeepClone()).ToArray()) }; return pin;
    }
    private static async Task<PageMetadata> Read(params JsonNode[] pins)
    { using var http = PinterestChecks.Http(PinterestChecks.Html(pins)); return await new PinterestProvider().ExtractAsync(PinterestChecks.Request(Path.GetTempPath()), http, default); }
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Pinterest story single video retains post identity complete media and duration", Single),
        ("Pinterest story partial format projections combine only the same declared asset", Partial),
        ("Pinterest story page and block positions retain images and stable video archives", Multiple),
        ("Pinterest story deleted native and global identities remain explicit boundaries", Identity),
        ("Pinterest story mismatched page counts IDs block types and assets fail", Conflicts),
        ("Pinterest story total and per-format durations must describe its complete inventory", Durations),
        ("Pinterest story limits and missing identified pages do not silently truncate", Limits),
        ("Pinterest story overlays and music remain explicit source-clip boundaries", Overlays),
        ("Pinterest story caption resources bind to their source video and provenance", Captions),
        ("Pinterest story inspected selection refreshes only its original video asset", Selection)
    ];
    private static async Task Single()
    {
        var page = await Read(Pin(2, Page("123", Block())));
        Assert(!page.IsPlaylist && page.Id == PinterestChecks.Id && page.ArchiveIdentity?.Extractor == "pinterest" && page.DurationSeconds == 2 && page.Media.Count == 2 && page.Warnings.Count == 0,
            "Plain single-page story changed post identity, complete formats or duration.");
        var pin = Pin(2, Page("123", Block())); ((JsonObject)pin).Remove("isHidden");
        Assert((await Read(pin)).Media.Count == 2, "Completed public story data required an optional field omitted by the current pin projection.");
        ((JsonObject)pin["storyPinData"]!["pages"]![0]!).Remove("pageId"); ((JsonObject)pin["storyPinData"]!["pages"]![0]!).Remove("storyPageId");
        Assert((await Read(pin)).Id == PinterestChecks.Id, "Single-video story identity unnecessarily required omitted page IDs.");
        pin["isHidden"] = true; await Reject<NotSupportedException>(() => Read(pin));
    }
    private static async Task Partial()
    {
        var first = Pin(2, Page("123", Block())); var second = first.DeepClone();
        ((JsonObject)first["storyPinData"]!["pages"]![0]!["blocks"]![0]!["videoDataV2"]!["videoList"]!).Remove("v720P");
        ((JsonObject)second["storyPinData"]!["pages"]![0]!).Remove("pageId"); ((JsonObject)second["storyPinData"]!["pages"]![0]!).Remove("storyPageId");
        ((JsonObject)second["storyPinData"]!["pages"]![0]!["blocks"]![0]!["videoDataV2"]!["videoList"]!).Remove("vHLSV4");
        var page = await Read(first, second); Assert(page.Media.Count == 2 && page.Media.All(item => item.Url.Query == PinterestChecks.Query), "Partial story projections lost formats or literal queries.");
    }
    private static async Task Multiple()
    {
        var photo = new JsonObject { ["__typename"] = "StoryPinImageBlock", ["blockType"] = 1 };
        var page = await Read(Pin(4, Page("123", photo, Block()), Page("456", Block(PinterestChecks.Other))));
        Assert(page.IsPlaylist && page.Entries.Count == 3 && !page.Entries[0].Available && page.Entries[1].Position == 2 && page.Entries[2].Position == 3 &&
            page.Entries[1].ExpectedArchiveIdentity?.Extractor == "pinterest:story" && page.Entries[1].Id != page.Entries[2].Id, "Story source positions or asset archive identities collapsed.");
    }
    private static async Task Identity()
    {
        var pin = Pin(2, Page("123", Block())); pin["storyPinData"]!["isDeleted"] = true; await Reject<NotSupportedException>(() => Read(pin));
        pin = Pin(2, Page("123", Block())); pin["storyPinData"]!["entityId"] = "2"; await Reject<InvalidDataException>(() => Read(pin));
        pin = Pin(2, Page("123", Block())); pin["storyPinData"]!["id"] = "bad"; await Reject<InvalidDataException>(() => Read(pin));
    }
    private static async Task Conflicts()
    {
        var first = Pin(2, Page("123", Block()));
        await Reject<InvalidDataException>(() => Read(first, Pin(4, Page("123", Block()), Page("456", Block()))));
        await Reject<InvalidDataException>(() => Read(first, Pin(2, Page("456", Block()))));
        await Reject<InvalidDataException>(() => Read(first, Pin(2, Page("123", Block(PinterestChecks.Other)))));
        var changed = first.DeepClone(); changed["storyPinData"]!["pages"]![0]!["blocks"]![0]!["blockType"] = 1; await Reject<InvalidDataException>(() => Read(first, changed));
    }
    private static async Task Durations()
    {
        await Reject<InvalidDataException>(() => Read(Pin(4, Page("123", Block()))));
        var pin = Pin(2, Page("123", Block())); pin["storyPinData"]!["pages"]![0]!["blocks"]![0]!["videoDataV2"]!["videoList"]!["v720P"]!["duration"] = 3000;
        await Reject<InvalidDataException>(() => Read(pin));
    }
    private static async Task Limits()
    {
        var pin = Pin(4, Page("123", Block()), Page("456", Block(PinterestChecks.Other)));
        foreach (var page in pin["storyPinData"]!["pages"]!.AsArray()) { ((JsonObject)page!).Remove("pageId"); ((JsonObject)page!).Remove("storyPageId"); }
        await Reject<NotSupportedException>(() => Read(pin));
        using var http = PinterestChecks.Http(PinterestChecks.Html(Pin(4, Page("123", Block()), Page("456", Block()))));
        await Reject<NotSupportedException>(() => new PinterestProvider().ExtractAsync(PinterestChecks.Request(Path.GetTempPath()) with { MaximumPlaylistEntries = 1 }, http, default));
    }
    private static async Task Overlays()
    {
        var pin = Pin(2, Page("123", Block())); pin["storyPinData"]!["pages"]![0]!["blocks"]![0]!["blockStyle"]!["width"] = 50;
        var page = await Read(pin); Assert(page.Warnings.Count > 0 && page.Media.Count == 2, "Source clips silently claimed rendered cropped/story overlays.");
    }
    private static async Task Captions()
    {
        var pin = Pin(2, Page("123", Block())); pin["storyPinData"]!["pages"]![0]!["blocks"]![0]!["videoDataV2"]!["videoListForCaptions"] = PinterestChecks.Pin()["videos"]!["videoListForCaptions"]!.DeepClone();
        var page = await Read(pin); Assert(page.Subtitles.Count == 1 && page.Subtitles[0].Automatic, "Story caption language/provenance was discarded.");
    }
    private static async Task Selection()
    {
        var original = Pin(4, Page("123", Block()), Page("456", Block(PinterestChecks.Other)));
        var current = Pin(4, Page("456", Block(PinterestChecks.Other)), Page("123", Block())); var state = original;
        var engine = new NativeEngine([new PinterestProvider()], _ => PinterestChecks.Http(PinterestChecks.Html(state)));
        var request = PinterestChecks.Request(Path.GetTempPath()); var info = await engine.InspectAsync(request, default); state = current;
        var directory = Path.Combine(Path.GetTempPath(), "native-pin-story-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try { var result = await engine.DownloadAsync(request with { OutputDirectory = directory, PlaylistSnapshot = info.PlaylistSnapshot, PlaylistItems = "2", SkipDownload = true, WriteInfoJson = true }, null, null, default);
            Assert(result.Succeeded && result.SidecarPaths.Count == 1, "Reordered story could not refresh its originally selected asset.");
            var text = await File.ReadAllTextAsync(result.SidecarPaths.Single()); Assert(text.Contains(PinterestChecks.Other), "Story reordering substituted another video asset."); }
        finally { Directory.Delete(directory, true); }
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Reject<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
