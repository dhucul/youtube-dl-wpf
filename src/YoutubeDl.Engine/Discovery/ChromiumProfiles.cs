using System.Text.Json;

namespace YoutubeDl.Engine.Discovery;

internal sealed record ChromiumProfile(string CookiePath, string LocalStatePath);
internal static class ChromiumProfiles
{
    internal static ChromiumProfile Resolve(string browser, string? selection, string? rootOverride = null)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = rootOverride ?? browser switch
        {
            "chrome" => Path.Combine(local, "Google", "Chrome", "User Data"),
            "edge" => Path.Combine(local, "Microsoft", "Edge", "User Data"),
            "brave" => Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"),
            "chromium" => Path.Combine(local, "Chromium", "User Data"),
            "vivaldi" => Path.Combine(local, "Vivaldi", "User Data"),
            "opera" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Opera Software", "Opera Stable"),
            _ => throw new NotSupportedException("This Chromium-based browser is not supported.")
        };
        root = Path.GetFullPath(root);
        if (selection is not null && (Path.IsPathRooted(selection) || selection.Contains('/') || selection.Contains('\\')))
        {
            var selected = Path.GetFullPath(Environment.ExpandEnvironmentVariables(selection), root);
            if (Path.GetFileName(selected).Equals("Cookies", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(selected))
            {
                if (!File.Exists(selected)) throw Missing();
                var folder = Path.GetDirectoryName(selected)!;
                return At(Path.GetFileName(folder).Equals("Network", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(folder)! : folder, selected);
            }
            if (FindCookies(selected) is not null) return At(selected);
            return Default(selected);
        }
        if (selection is null) return Default(root);
        ValidateFolder(selection);
        var direct = Path.Combine(root, selection);
        if (FindCookies(direct) is not null) return At(direct);
        var statePath = Path.Combine(root, "Local State");
        if (!File.Exists(statePath)) throw Missing();
        using var state = ReadState(statePath);
        var profile = Providers.ProviderJson.Object(state.RootElement, "profile");
        var cache = Providers.ProviderJson.Object(profile, "info_cache");
        if (cache.ValueKind != JsonValueKind.Object || cache.EnumerateObject().Count() > 1000) throw new InvalidDataException("Browser profile metadata is invalid or excessive.");
        var matches = cache.EnumerateObject().Where(item => selection.Equals(Providers.ProviderJson.Text(item.Value, "name"), StringComparison.OrdinalIgnoreCase)).Select(item => item.Name).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("The browser profile name is missing or ambiguous. Choose a profile folder or full path.");
        ValidateFolder(matches[0]); return At(Path.Combine(root, matches[0]));
    }
    private static ChromiumProfile Default(string root)
    {
        if (FindCookies(root) is not null) return At(root);
        var statePath = Path.Combine(root, "Local State");
        if (File.Exists(statePath))
        {
            using var state = ReadState(statePath);
            if (state.RootElement.TryGetProperty("profile", out var metadata) && metadata.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Browser default-profile metadata is invalid.");
            if (metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty("last_used", out var lastValue) && lastValue.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new InvalidDataException("The browser default-profile identity is invalid.");
            if (Providers.ProviderJson.Text(Providers.ProviderJson.Object(state.RootElement, "profile"), "last_used") is { Length: > 0 } last)
            { ValidateFolder(last); return At(Path.Combine(root, last)); }
        }
        if (FindCookies(Path.Combine(root, "Default")) is not null) return At(Path.Combine(root, "Default"));
        var profiles = Directory.Exists(root) ? Directory.EnumerateDirectories(root).Take(1001).ToArray() : [];
        if (profiles.Length > 1000) throw new InvalidDataException("There are too many browser profile directories.");
        var available = profiles.Where(path => FindCookies(path) is not null).ToArray();
        if (available.Length == 0) throw Missing();
        if (available.Length > 1) throw new InvalidDataException("No default browser profile is recorded. Select a profile folder explicitly.");
        return At(available[0]);
    }
    private static ChromiumProfile At(string profile, string? selectedCookies = null)
    {
        var path = selectedCookies ?? FindCookies(profile) ?? throw Missing();
        var state = Path.Combine(profile, "Local State");
        if (!File.Exists(state)) state = Path.Combine(Path.GetDirectoryName(profile) ?? profile, "Local State");
        return new(Path.GetFullPath(path), Path.GetFullPath(state));
    }
    private static string? FindCookies(string profile)
    {
        var modern = Path.Combine(profile, "Network", "Cookies");
        if (File.Exists(modern)) return modern;
        var legacy = Path.Combine(profile, "Cookies"); return File.Exists(legacy) ? legacy : null;
    }
    private static void ValidateFolder(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
            throw new InvalidDataException("Browser metadata contains an invalid profile folder name.");
    }
    internal static JsonDocument ReadState(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The selected browser profile has no Local State file for its encryption key.", path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > 8 * 1024 * 1024) throw new InvalidDataException("Browser Local State exceeds its size limit.");
        try
        {
            var document = JsonDocument.Parse(file);
            if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
            document.Dispose(); throw new InvalidDataException("Browser Local State is not a JSON object.");
        }
        catch (JsonException) { throw new InvalidDataException("Browser Local State is not valid JSON."); }
    }
    private static FileNotFoundException Missing() => new("The selected browser profile has no Cookies database. Choose its profile path or export a cookies file.");
}
