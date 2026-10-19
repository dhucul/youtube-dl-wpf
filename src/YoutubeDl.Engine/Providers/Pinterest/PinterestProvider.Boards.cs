using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Pinterest;

public sealed partial class PinterestProvider
{
    private sealed record BoardState(string Id, string Title, string? Description, string? Uploader, int Count, string Revision, JsonObject Options, JsonElement[] Rows, string Bookmark);
    private static async Task<PageMetadata> ReadBoardAsync(NativeDownloadRequest request, PinterestBoardAddress address, MediaHttp session, CancellationToken ct, MediaDocument? existing = null)
    {
        var state = await Load(existing).ConfigureAwait(false);
        if (state.Count > request.MaximumPlaylistEntries || state.Count > 10000) throw new NotSupportedException("The Pinterest board exceeds the configured complete-inventory limit.");
        var entries = new List<NativePlaylistEntry>(); var seen = new HashSet<string>(StringComparer.Ordinal); var cursors = new HashSet<string>(StringComparer.Ordinal);
        var rows = state.Rows; var cursor = state.Bookmark; var paged = false;
        using var apiSession = session.WithHeaders(new Dictionary<string, string> { ["Referer"] = address.Page.AbsoluteUri, ["X-Requested-With"] = "XMLHttpRequest",
            ["X-Pinterest-Pws-Handler"] = "www/[username]/[slug].js", ["Accept"] = "application/json, text/javascript, */*; q=0.01" });
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (rows.Length > 200) throw new InvalidDataException("Pinterest returned an excessive board page.");
            foreach (var row in rows)
            {
                if (Text(row, "type") == "story" && Text(row, "story_type") == "related_interests_module") continue;
                if (Text(row, "type") != "pin" || !NumericId(Text(row, "id"))) throw new NotSupportedException("This Pinterest board row type needs additional native inventory support.");
                var id = Text(row, "id")!;
                if (!seen.Add(id) || entries.Count >= state.Count) throw new InvalidDataException("Pinterest repeated a pin or changed its board inventory.");
                if (Text(row, "node_id") is { } node && node != Convert.ToBase64String(Encoding.UTF8.GetBytes("Pin:" + id)))
                    throw new InvalidDataException("Pinterest returned conflicting board pin identities.");
                var videos = Object(row, "videos"); var hosted = Object(videos, "video_list").ValueKind == JsonValueKind.Object;
                // Grid declarations can omit external players and richer metadata.
                // Resolve selected pins freshly instead of calling that omission an image.
                var available = !True(row, "is_hidden") && !True(row, "is_deleted");
                entries.Add(new(entries.Count + 1, id, Text(row, "title") ?? Text(row, "description") ?? "Pinterest pin " + id,
                    new("https://www.pinterest.com/pin/" + id + "/"), Number(videos, "duration") / 1000, PostImage(Text(Object(Object(row, "images"), "orig"), "url"))?.AbsoluteUri, available,
                    ExpectedArchiveIdentity: hosted ? new("pinterest", id) : null));
            }
            if (cursor == "-end-") break;
            if (rows.Length == 0 || entries.Count >= state.Count || !cursors.Add(cursor) || cursors.Count > 1000)
                throw new InvalidDataException("Pinterest board pagination is incomplete, repeated or inconsistent.");
            var options = (JsonObject)state.Options.DeepClone(); options["bookmarks"] = new JsonArray(cursor);
            var payload = new JsonObject { ["options"] = options, ["context"] = new JsonObject() };
            var endpoint = new Uri("https://www.pinterest.com/resource/BoardFeedResource/get/?source_url=" + Uri.EscapeDataString(address.Page.AbsolutePath) + "&data=" + Uri.EscapeDataString(payload.ToJsonString()));
            MediaDocument response;
            try { response = await apiSession.GetDocumentAsync(endpoint, 8 * 1024 * 1024, ct).ConfigureAwait(false); }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            { throw new NotSupportedException("Pinterest refused public board pagination; additional native account/access support is required."); }
            if (response.Url != endpoint) throw new InvalidDataException("Pinterest redirected its board pagination lookup.");
            using var json = JsonDocument.Parse(response.Text, new() { MaxDepth = 64 }); var root = json.RootElement; var resource = Object(root, "resource"); var returned = Object(resource, "options");
            if (Text(resource, "name") != "BoardFeedResource" || Text(returned, "board_id") != state.Id) throw new InvalidDataException("Pinterest returned another board's pagination.");
            foreach (var option in state.Options)
                if (!returned.TryGetProperty(option.Key, out var value) || !JsonNode.DeepEquals(option.Value, JsonNode.Parse(value.GetRawText())))
                    throw new InvalidDataException("Pinterest changed the requested board feed scope during pagination.");
            var data = Object(root, "resource_response");
            if (Text(data, "status") != "success" || !data.TryGetProperty("data", out var page) || page.ValueKind != JsonValueKind.Array)
                throw new NotSupportedException("Pinterest did not return a complete public board page.");
            rows = page.EnumerateArray().Select(item => item.Clone()).ToArray(); cursor = Cursor(Array(returned, "bookmarks")); paged = true;
        }
        if (entries.Count != state.Count) throw new NotSupportedException("Pinterest exposed " + entries.Count + " of " + state.Count + " declared board pins. An incomplete public/account inventory was not returned as complete.");
        if (paged)
        {
            var final = await Load(null).ConfigureAwait(false);
            if (final.Id != state.Id || final.Count != state.Count || final.Revision != state.Revision)
                throw new InvalidDataException("The Pinterest board changed during pagination.");
        }
        return new() { Id = state.Id, PlaylistId = state.Id, Extractor = "pinterest:board", IsPlaylist = true, Title = state.Title, Description = state.Description,
            Uploader = state.Uploader, Entries = entries, SourcePageUrl = address.Page,
            Warnings = ["Some pins may be still images or unsupported stories. Only selected pins are checked for downloadable video."] };

        async Task<BoardState> Load(MediaDocument? supplied)
        {
            var document = supplied ?? await session.GetDocumentAsync(address.Page, 8 * 1024 * 1024, ct).ConfigureAwait(false);
            if (!PinterestBoardAddress.TryParse(document.Url, out var returned) || returned!.Page != address.Page) throw new InvalidDataException("Pinterest redirected to another board identity.");
            return ReadBoardState(document.Text, address);
        }
    }
    private static BoardState ReadBoardState(string html, PinterestBoardAddress address)
    {
        var clean = Regex.Replace(html, @"<!--.*?-->|<(?:template|textarea)\b[^>]*>.*?</(?:template|textarea)\s*>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
        var scripts = Regex.Matches(clean, @"<script\b[^>]*\bid\s*=\s*([""'])__PWS_INITIAL_PROPS__\1[^>]*>(?<json>.*?)</script\s*>", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));
        if (scripts.Count != 1) throw new NotSupportedException("Pinterest did not expose one supported board state document.");
        using var json = JsonDocument.Parse(scripts[0].Groups["json"].Value, new() { MaxDepth = 64 }); var redux = Object(json.RootElement, "initialReduxState"); var resources = Object(redux, "resources");
        var metadata = Resource(resources, "BoardResource", options => Text(options, "username")?.Equals(address.Username, StringComparison.OrdinalIgnoreCase) == true && Text(options, "slug") == address.Slug);
        var board = Object(metadata.Record, "data"); var id = Text(board, "id");
        if (!NumericId(id) || Text(board, "node_id") != Convert.ToBase64String(Encoding.UTF8.GetBytes("Board:" + id)) || Text(board, "url") != address.Page.AbsolutePath ||
            Text(Object(board, "owner"), "username")?.Equals(address.Username, StringComparison.OrdinalIgnoreCase) != true)
            throw new InvalidDataException("Pinterest returned conflicting board/owner identities.");
        if (Text(board, "privacy") != "public") throw new NotSupportedException("This Pinterest board requires additional native private/account access support.");
        if (Integer(board, "section_count") != 0) throw new NotSupportedException("Pinterest boards with sections require additional verified inventory support.");
        var count = Integer(board, "pin_count"); var revision = Text(board, "board_order_modified_at");
        if (count is null || revision is null) throw new InvalidDataException("Pinterest omitted a supported complete board count/revision.");
        var feed = Resource(resources, "BoardFeedResource", options => Text(options, "board_id") == id);
        if (Text(feed.Options, "field_set_key") != "react_grid_pin" || !True(feed.Options, "redux_normalize_feed") || !True(feed.Options, "is_react") ||
            True(feed.Options, "filter_section_pins") || True(feed.Options, "prepend") || Integer(feed.Options, "page_size") is not (> 0 and <= 100))
            throw new NotSupportedException("This Pinterest board feed scope/filter needs additional native support.");
        if (!feed.Record.TryGetProperty("data", out var listed) || listed.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Pinterest omitted the board pin inventory.");
        var rows = listed.EnumerateArray().ToArray(); var bookmark = Text(feed.Record, "nextBookmark");
        if (bookmark is null) throw new InvalidDataException("Pinterest omitted its board continuation state.");
        var options = JsonNode.Parse(feed.Options.GetRawText())!.AsObject();
        var allowed = new[] { "add_vase", "board_id", "field_set_key", "filter_section_pins", "gated", "is_react", "page_size", "prepend", "redux_normalize_feed" };
        if (options.Any(option => !allowed.Contains(option.Key, StringComparer.Ordinal)))
            throw new NotSupportedException("This Pinterest board feed option needs additional native scope support.");
        return new(id!, Text(board, "name") ?? id!, Text(board, "description"), Text(Object(board, "owner"), "full_name"), count.Value, revision,
            options, rows.Select(item => item.Clone()).ToArray(), Cursor([JsonSerializer.SerializeToElement(bookmark)]));
    }
    private static (JsonElement Options, JsonElement Record) Resource(JsonElement resources, string name, Func<JsonElement, bool> matches)
    {
        var group = Object(resources, name); if (group.ValueKind != JsonValueKind.Object || group.EnumerateObject().Count() > 128) throw new NotSupportedException("Pinterest omitted a supported board resource.");
        var found = new List<(JsonElement Options, JsonElement Record)>();
        foreach (var property in group.EnumerateObject())
        {
            using var key = JsonDocument.Parse(property.Name, new() { MaxDepth = 16 }); var pairs = key.RootElement;
            if (pairs.ValueKind != JsonValueKind.Array || pairs.GetArrayLength() > 32) throw new InvalidDataException("Pinterest returned malformed board resource options.");
            var options = new JsonObject();
            foreach (var pair in pairs.EnumerateArray())
            {
                if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() != 2 || pair[0].ValueKind != JsonValueKind.String || options.ContainsKey(pair[0].GetString()!))
                    throw new InvalidDataException("Pinterest returned repeated or invalid board resource options.");
                options[pair[0].GetString()!] = JsonNode.Parse(pair[1].GetRawText());
            }
            using var parsed = JsonDocument.Parse(options.ToJsonString());
            if (!matches(parsed.RootElement)) continue;
            if (!property.Value.TryGetProperty("fetching", out var fetching) || fetching.ValueKind != JsonValueKind.False)
                throw new NotSupportedException("Pinterest board state has not finished loading.");
            found.Add((parsed.RootElement.Clone(), property.Value.Clone()));
        }
        return found.Count == 1 ? found[0] : throw new InvalidDataException("Pinterest returned missing or conflicting requested board resources.");
    }
    private static string Cursor(JsonElement[] values) => values.Length == 1 && values[0].ValueKind == JsonValueKind.String && values[0].GetString() is { Length: > 0 and <= 16384 } value &&
        !value.Any(char.IsControl) ? value : throw new InvalidDataException("Pinterest returned an invalid board continuation state.");
}
