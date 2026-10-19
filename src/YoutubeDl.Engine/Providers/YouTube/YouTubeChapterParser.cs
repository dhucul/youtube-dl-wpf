using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Providers.YouTube;

public static class YouTubeChapterParser
{
    public static IReadOnlyList<ChapterMarker> Parse(string? initialData, double? durationSeconds, string? description = null)
    {
        if (durationSeconds is not { } duration || !double.IsFinite(duration) || duration <= 0) return [];
        if (initialData is not null)
        {
            if (Encoding.UTF8.GetByteCount(initialData) > 2 * 1024 * 1024) throw new InvalidDataException("The chapter response exceeds the extraction limit.");
            using var document = JsonDocument.Parse(initialData);
            foreach (var map in Find(Object(document.RootElement, "playerOverlays"), "markersMap"))
                if (map.ValueKind == JsonValueKind.Array)
                    foreach (var marker in map.EnumerateArray())
                    {
                        if (Text(marker, "key") is not ("DESCRIPTION_CHAPTERS" or "AUTO_CHAPTERS")) continue;
                        var entries = Array(Object(marker, "value"), "chapters").Select(entry => Object(entry, "chapterRenderer"))
                            .Select(entry => (Start: Number(entry, "timeRangeStartMillis") / 1000, Title: ReadText(entry, "title"))).ToArray();
                        if (Complete(entries, duration) is { Count: > 0 } chapters) return chapters;
                    }
            var panels = document.RootElement.TryGetProperty("engagementPanels", out var panelArray) ? panelArray : default;
            foreach (var list in Find(panels, "macroMarkersListRenderer"))
            {
                var entries = Array(list, "contents").Select(entry => Object(entry, "macroMarkersListItemRenderer"))
                    .Select(entry => (Start: Number(Object(Object(entry, "onTap"), "watchEndpoint"), "startTimeSeconds") ?? Timestamp(ReadText(entry, "timeDescription")),
                        Title: ReadText(entry, "title"))).ToArray();
                if (Complete(entries, duration) is { Count: > 0 } chapters) return chapters;
            }
        }
        if (string.IsNullOrWhiteSpace(description)) return [];
        var parsed = new List<(double? Start, string? Title)>();
        foreach (var line in description.Split('\n'))
        {
            var match = Regex.Match(line, @"\A\s*(?<time>(?:\d{1,3}:)?\d{1,2}:\d{2})\s*(?:[-–—|]\s*)?(?<title>\S.*?)\s*\z",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (match.Success) parsed.Add((Timestamp(match.Groups["time"].Value), match.Groups["title"].Value));
        }
        // Ordinary timestamp links are not sufficient evidence of a chapter list.
        return parsed.Count >= 3 && parsed[0].Start == 0 ? Complete(parsed, duration) : [];
    }
    private static IReadOnlyList<ChapterMarker> Complete(IEnumerable<(double? Start, string? Title)> source, double duration)
    {
        var items = source.ToArray();
        if (items.Length > 1000) throw new InvalidDataException("The video returned too many chapter markers.");
        if (items.Length == 0 || items.Any(item => item.Start is null || !double.IsFinite(item.Start.Value) || item.Start < 0 || item.Start >= duration || string.IsNullOrWhiteSpace(item.Title))) return [];
        for (var index = 1; index < items.Length; index++) if (items[index].Start <= items[index - 1].Start) return [];
        return items.Select((item, index) => new ChapterMarker
        { StartSeconds = item.Start!.Value, EndSeconds = index + 1 < items.Length ? items[index + 1].Start!.Value : duration, Title = item.Title! }).ToArray();
    }
    private static double? Timestamp(string? text)
    {
        if (text is null) return null;
        var parts = text.Split(':');
        if (parts.Length is < 2 or > 3) return null;
        double seconds = 0;
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0 || index > 0 && value >= 60) return null;
            seconds = seconds * 60 + value;
        }
        return seconds;
    }
    private static IEnumerable<JsonElement> Find(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name == name) yield return property.Value;
                foreach (var found in Find(property.Value, name)) yield return found;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) foreach (var found in Find(item, name)) yield return found;
    }
    private static JsonElement Object(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
    private static JsonElement[] Array(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
    private static string? Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static double? Number(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out number) ? number : null;
    }
    private static string? ReadText(JsonElement element, string name)
    {
        var value = Object(element, name);
        return Text(value, "simpleText") ?? Text(value, "content") ?? (Array(value, "runs") is { Length: > 0 } runs ? string.Concat(runs.Select(run => Text(run, "text"))) : null);
    }
}
