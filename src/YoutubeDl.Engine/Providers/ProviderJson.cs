using System.Globalization;
using System.Text.Json;

namespace YoutubeDl.Engine.Providers;

internal static class ProviderJson
{
    public static JsonElement Object(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
    public static JsonElement[] Array(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
    public static string? Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    public static string? TextList(JsonElement element, string name)
    {
        if (Text(element, name) is { } text) return text;
        var parts = Array(element, name).Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return parts.Length == 0 ? null : string.Join("; ", parts);
    }
    public static double? Number(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return null;
        double number;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number) && double.IsFinite(number) && number >= 0) return number;
        return value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number) && number >= 0 ? number : null;
    }
    public static int? Integer(JsonElement element, string name) => Number(element, name) is { } value && value <= int.MaxValue && Math.Truncate(value) == value ? (int)value : null;
    public static long? Long(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0) return number;
        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out number) && number >= 0 ? number : null;
    }
    public static bool True(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.String && value.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true);
    public static double? Duration(string? text)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && double.IsFinite(seconds) && seconds >= 0) return seconds;
        if (text is null) return null;
        var parts = text.Split(':');
        if (parts.Length is < 2 or > 3) return null;
        seconds = 0;
        for (var index = 0; index < parts.Length; index++)
        {
            if (!double.TryParse(parts[index], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value < 0 || index > 0 && value >= 60 || index < parts.Length - 1 && Math.Truncate(value) != value) return null;
            seconds = seconds * 60 + value;
        }
        return seconds;
    }
}
