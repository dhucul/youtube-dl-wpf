namespace YoutubeDl.Engine.Discovery;

internal static class FirefoxProfiles
{
    internal static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mozilla", "Firefox");
    private sealed record Profile(string Name, string CookiePath, bool Default);
    internal static string Resolve(string? selection, string root)
    {
        root = Path.GetFullPath(root);
        if (selection is not null && (Path.IsPathRooted(selection) || selection.Contains('/') || selection.Contains('\\')))
            return Require(CookiePath(Path.GetFullPath(Environment.ExpandEnvironmentVariables(selection), root)));
        var profiles = new List<Profile>(); var defaults = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filename in new[] { "profiles.ini", "installs.ini" })
            foreach (var section in ReadIni(Path.Combine(root, filename)))
            {
                var values = section.Value;
                if (section.Key.StartsWith("Profile", StringComparison.Ordinal) && values.TryGetValue("Path", out var path))
                {
                    var relative = values.GetValueOrDefault("IsRelative") == "1";
                    if (!relative && !Path.IsPathRooted(path)) throw new InvalidDataException("A Firefox profile path is not absolute.");
                    profiles.Add(new(values.GetValueOrDefault("Name", ""), CookiePath(Path.GetFullPath(path, root)), values.GetValueOrDefault("Default") == "1"));
                }
                else if (section.Key != "General" && values.TryGetValue("Default", out var installed) && installed.Length > 0)
                    defaults.Add(CookiePath(Path.GetFullPath(installed, root)));
            }
        if (selection is not null)
        {
            var matches = profiles.Where(profile => profile.Name.Equals(selection, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(Path.GetDirectoryName(profile.CookiePath)), selection, StringComparison.OrdinalIgnoreCase)).Select(profile => profile.CookiePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (matches.Length > 1) throw new InvalidDataException("The Firefox profile name is ambiguous. Select its full path.");
            return Require(matches.Length == 1 ? matches[0] : CookiePath(Path.Combine(root, "Profiles", selection)));
        }
        var available = profiles.Where(profile => File.Exists(profile.CookiePath)).ToList();
        var installedDefaults = defaults.Where(File.Exists).ToArray();
        if (installedDefaults.Length > 0) return Latest(installedDefaults);
        var declared = available.Where(profile => profile.Default).ToArray();
        if (declared.Length > 0) return Latest(declared.Select(profile => profile.CookiePath));
        var folder = Path.Combine(root, "Profiles");
        var loose = Directory.Exists(folder) ? Directory.EnumerateDirectories(folder).Take(1001).ToArray() : [];
        if (loose.Length > 1000) throw new InvalidDataException("There are too many Firefox profile directories.");
        var candidates = available.Select(profile => profile.CookiePath).Concat(loose.Select(CookiePath).Where(File.Exists)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (candidates.Length == 0) throw new FileNotFoundException("No Firefox cookie database was found. Select a profile path or export a cookies file.");
        return Latest(candidates);
    }
    private static string Latest(IEnumerable<string> paths) => paths.OrderByDescending(File.GetLastWriteTimeUtc).ThenBy(path => path, StringComparer.OrdinalIgnoreCase).First();
    private static string CookiePath(string path) => Path.GetFileName(path).Equals("cookies.sqlite", StringComparison.OrdinalIgnoreCase) ? path : Path.Combine(path, "cookies.sqlite");
    private static string Require(string path) => File.Exists(path) ? Path.GetFullPath(path) : throw new FileNotFoundException("The selected Firefox profile has no cookies.sqlite file.", path);
    private static Dictionary<string, Dictionary<string, string>> ReadIni(string path)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        if (!File.Exists(path)) return result;
        if (new FileInfo(path).Length > 256 * 1024) throw new InvalidDataException("Firefox profile metadata exceeds its size limit.");
        Dictionary<string, string>? section = null;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (result.Count >= 1000 || !result.TryAdd(line[1..^1], section = new(StringComparer.Ordinal))) throw new InvalidDataException("Firefox profile sections are repeated or excessive.");
                continue;
            }
            var separator = line.IndexOf('=');
            if (section is null || separator <= 0 || !section.TryAdd(line[..separator].Trim(), line[(separator + 1)..].Trim())) throw new InvalidDataException("Firefox profile metadata is invalid.");
        }
        return result;
    }
}
