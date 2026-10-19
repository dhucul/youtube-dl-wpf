using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Rumble;

internal static class RumblePage
{
    internal static IReadOnlyList<Uri> EmbeddedUrls(string html)
    {
        try { return Players(html).Where(player => !RumbleProvider.RegionBlocked(player)).Select(player => RumbleAddress.EmbedUrl(PlayerId(player))).Distinct().ToArray(); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException) { return []; }
    }
    internal static IReadOnlyList<JsonElement> Players(string html)
    {
        var result = new List<JsonElement>();
        foreach (Match script in Regex.Matches(html, @"<script\b[^>]{0,4096}>(?<body>.*?)</script\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            var body = script.Groups["body"].Value; var cursor = 0; var state = '\0'; var escaped = false;
            foreach (Match call in Regex.Matches(body, @"\bRumble\s*\(\s*['""]play['""]\s*,\s*(?<json>\{)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                while (cursor < call.Index)
                {
                    var current = body[cursor]; var next = cursor + 1 < body.Length ? body[cursor + 1] : '\0';
                    if (state == 'L') { if (current is '\r' or '\n') state = '\0'; }
                    else if (state == 'B') { if (current == '*' && next == '/') { state = '\0'; cursor++; } }
                    else if (state != '\0')
                    { if (escaped) escaped = false; else if (current == '\\') escaped = true; else if (current == state) state = '\0'; }
                    else if (current is '\'' or '"' or '`') state = current;
                    else if (current == '/' && next is '/' or '*') { state = next == '/' ? 'L' : 'B'; cursor++; }
                    cursor++;
                }
                if (state != '\0') continue;
                var start = call.Groups["json"].Index; var depth = 0; var quote = '\0'; var quotedEscape = false; var end = -1;
                for (var index = start; index < body.Length && index - start <= 2 * 1024 * 1024; index++)
                {
                    var character = body[index];
                    if (quote != '\0') { if (quotedEscape) quotedEscape = false; else if (character == '\\') quotedEscape = true; else if (character == quote) quote = '\0'; }
                    else if (character is '"' or '\'' or '`') quote = character;
                    else if (character == '{') depth++;
                    else if (character == '}' && --depth == 0) { end = index + 1; break; }
                }
                if (end < 0) throw new InvalidDataException("The Rumble player declaration is incomplete or excessive.");
                if (result.Count >= 64) throw new InvalidDataException("The page contains too many Rumble player declarations.");
                result.Add(ReadConfiguration(body[start..end]));
            }
        }
        return result;
    }
    private static JsonElement ReadConfiguration(string text)
    {
        // Site callbacks are code, not metadata. Read only top-level literal
        // fields and skip other property values without evaluating them.
        var result = new JsonObject(); var cursor = 1; var properties = 0;
        var keyPattern = new Regex(@"\G(?:""(?<double>[^""\\]+)""|'(?<single>[^'\\]+)'|(?<bare>[A-Za-z_$][A-Za-z0-9_$]*))\s*:\s*", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        while (cursor < text.Length - 1)
        {
            while (cursor < text.Length && (char.IsWhiteSpace(text[cursor]) || text[cursor] == ',')) cursor++;
            if (text[cursor] == '}') break;
            if (++properties > 256) throw new InvalidDataException("The Rumble player has too many configuration properties.");
            var key = keyPattern.Match(text, cursor);
            if (!key.Success) throw new NotSupportedException("Rumble uses an unsupported player-property declaration.");
            var name = key.Groups["double"].Success ? key.Groups["double"].Value : key.Groups["single"].Success ? key.Groups["single"].Value : key.Groups["bare"].Value;
            cursor += key.Length; var start = cursor; var depth = 0; var quote = '\0'; var escaped = false;
            while (cursor < text.Length - 1)
            {
                var character = text[cursor];
                if (quote != '\0') { if (escaped) escaped = false; else if (character == '\\') escaped = true; else if (character == quote) quote = '\0'; }
                else if (character is '"' or '\'' or '`') quote = character;
                else if (character is '{' or '[' or '(') depth++;
                else if (character is '}' or ']' or ')') depth--;
                else if (character == ',' && depth == 0) break;
                cursor++;
            }
            if (quote != '\0' || depth != 0) throw new InvalidDataException("Rumble returned an incomplete player property.");
            if (name is "video" or "opts" or "region_blocked")
            {
                if (result.ContainsKey(name)) throw new InvalidDataException("Rumble repeated a player identity or access property.");
                try { result[name] = JsonNode.Parse(text[start..cursor]); }
                catch (JsonException) { throw new NotSupportedException("Rumble uses a computed player identity or access property."); }
            }
        }
        using var document = JsonDocument.Parse(result.ToJsonString()); return document.RootElement.Clone();
    }
    internal static string PlayerId(JsonElement player)
    {
        var id = ProviderJson.Text(player, "video") ?? ProviderJson.Text(ProviderJson.Object(player, "video"), "id");
        if (id is null) throw new NotSupportedException("Rumble did not declare one supported video in this player.");
        _ = RumbleAddress.NumericId(id); return id;
    }
    internal static string? Meta(string html, string key)
    {
        foreach (Match tag in Regex.Matches(html, @"<meta\b(?<attrs>[^>]{0,8192})>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            var attributes = Attributes(tag.Groups["attrs"].Value);
            if (attributes.GetValueOrDefault("property") == key || attributes.GetValueOrDefault("name") == key) return attributes.GetValueOrDefault("content");
        }
        return null;
    }
    internal static Dictionary<string, string> Attributes(string text, params string[] fields)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match attribute in Regex.Matches(text, @"(?<name>[A-Za-z0-9_:.-]+)\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)'|(?<bare>[^\s""'=<>`]+))", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            if (fields.Length > 0 && !fields.Contains(attribute.Groups["name"].Value, StringComparer.OrdinalIgnoreCase)) continue;
            if (!result.TryAdd(attribute.Groups["name"].Value, WebUtility.HtmlDecode(attribute.Groups["double"].Success ? attribute.Groups["double"].Value : attribute.Groups["single"].Success ? attribute.Groups["single"].Value : attribute.Groups["bare"].Value)))
                throw new InvalidDataException("A Rumble metadata tag repeats an attribute.");
        }
        return result;
    }
}
