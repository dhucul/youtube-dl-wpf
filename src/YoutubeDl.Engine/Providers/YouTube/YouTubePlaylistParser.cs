using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.YouTube;

public sealed record YouTubePlaylistPage(string? Title, IReadOnlyList<NativePlaylistEntry> Entries, string? Continuation);

public static class YouTubePlaylistParser
{
    public static YouTubePlaylistPage Parse(string json, int firstPosition = 1, int maximumEntries = 1000)
    {
        if (firstPosition < 1 || maximumEntries < 1) throw new ArgumentOutOfRangeException(nameof(firstPosition));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var entries = new List<NativePlaylistEntry>();
        string? continuation = null;
        string? title = null;
        if (root.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("playlistMetadataRenderer", out var playlistMetadata))
            title = Text(playlistMetadata, "title");
        var scopes = new List<JsonElement>();
        if (root.TryGetProperty("contents", out var contents)) scopes.Add(contents);
        foreach (var name in new[] { "onResponseReceivedActions", "onResponseReceivedEndpoints" })
            if (root.TryGetProperty(name, out var actions)) scopes.Add(actions);
        if (root.TryGetProperty("continuationContents", out var continuationContents)) scopes.Add(continuationContents);
        foreach (var scope in scopes) Walk(scope, 0);
        if (entries.Count == 0 && continuation is null) throw new InvalidOperationException("The YouTube response did not contain accessible playlist items.");
        return new(title, entries, continuation);

        void Walk(JsonElement element, int depth)
        {
            if (depth > 80) throw new InvalidDataException("The playlist response exceeds the nesting limit.");
            if (element.ValueKind == JsonValueKind.Array) { foreach (var item in element.EnumerateArray()) Walk(item, depth + 1); return; }
            if (element.ValueKind != JsonValueKind.Object) return;
            if (element.TryGetProperty("playlistVideoRenderer", out var legacy)) { Add(legacy, modern: false); return; }
            if (element.TryGetProperty("lockupViewModel", out var modern))
            {
                if (Text(modern, "contentType") is null or "LOCKUP_CONTENT_TYPE_VIDEO") Add(modern, modern: true);
                return;
            }
            if (element.TryGetProperty("continuationItemRenderer", out var oldContinuation)) { SetContinuation(oldContinuation); return; }
            if (element.TryGetProperty("continuationItemViewModel", out var newContinuation)) { SetContinuation(newContinuation); return; }
            foreach (var property in element.EnumerateObject()) Walk(property.Value, depth + 1);
        }
        void Add(JsonElement element, bool modern)
        {
            if (entries.Count == maximumEntries) throw new InvalidDataException("The playlist response exceeds the configured item limit.");
            var id = Text(element, modern ? "contentId" : "videoId");
            if (id is not null && !Regex.IsMatch(id, @"\A[A-Za-z0-9_-]{11}\z")) throw new InvalidDataException("The playlist contains an invalid video identifier.");
            string? entryTitle;
            if (modern) entryTitle = ReadText(Object(Object(element, "metadata"), "lockupMetadataViewModel"), "title");
            else entryTitle = ReadText(element, "title");
            var available = id is not null && !(element.TryGetProperty("isPlayable", out var playable) && playable.ValueKind == JsonValueKind.False);
            double? duration = null;
            if (!modern) duration = NumberText(Text(element, "lengthSeconds")) ?? ParseDuration(ReadText(element, "lengthText"));
            else
            {
                var badge = Find(element, "thumbnailBadgeViewModel");
                if (badge is { } value) duration = ParseDuration(Text(value, "text"));
            }
            var image = modern ? Find(element, "thumbnailViewModel") : Object(element, "thumbnail");
            string? thumbnail = null;
            if (image is { } source)
            {
                var images = modern ? Array(Object(source, "image"), "sources") : Array(source, "thumbnails");
                thumbnail = images.Where(item => Uri.TryCreate(Text(item, "url"), UriKind.Absolute, out var url) && url.Scheme == "https")
                    .OrderByDescending(item => NumberText(Text(item, "width")) ?? Number(item, "width") ?? 0).Select(item => Text(item, "url")).FirstOrDefault();
            }
            var position = checked(firstPosition + entries.Count);
            entries.Add(new(position, id, entryTitle ?? "Unavailable video", id is null ? null : new Uri("https://www.youtube.com/watch?v=" + id), duration, thumbnail, available));
        }
        void SetContinuation(JsonElement element)
        {
            var command = Find(element, "continuationCommand");
            var token = command is { } value ? Text(value, "token") : null;
            if (token is null)
            {
                foreach (var candidate in FindAll(element, "continuationCommand"))
                    if (Text(candidate, "token") is { } found) { token = found; break; }
            }
            if (token is null) throw new InvalidDataException("The playlist continuation has no supported token.");
            if (token.Length > 16384 || continuation is not null && continuation != token) throw new InvalidDataException("The playlist continuation is oversized or ambiguous.");
            continuation = token;
        }
    }
    private static IEnumerable<JsonElement> FindAll(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name == name) yield return property.Value;
                foreach (var found in FindAll(property.Value, name)) yield return found;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) foreach (var found in FindAll(item, name)) yield return found;
    }
    private static JsonElement? Find(JsonElement element, string name) => FindAll(element, name).Select(value => (JsonElement?)value).FirstOrDefault();
    private static JsonElement Object(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
    private static JsonElement[] Array(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
    private static string? Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static double? Number(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number >= 0 ? number : null;
    private static string? ReadText(JsonElement element, string name)
    {
        var value = Object(element, name);
        return Text(value, "simpleText") ?? Text(value, "content") ?? (Array(value, "runs") is { Length: > 0 } runs ? string.Concat(runs.Select(run => Text(run, "text"))) : null);
    }
    private static double? NumberText(string? text) => double.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value >= 0 ? value : null;
    private static double? ParseDuration(string? text)
    {
        if (text is null) return null;
        var parts = text.Split(':');
        if (parts.Length is < 2 or > 3) return null;
        double result = 0;
        foreach (var part in parts) { if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) return null; result = result * 60 + number; }
        return result;
    }
}
