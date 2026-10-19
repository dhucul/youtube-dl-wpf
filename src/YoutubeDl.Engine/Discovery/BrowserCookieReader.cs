using System.Globalization;
using System.Net;
using System.Text.Json;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Discovery;

public sealed record BrowserCookieImport
{
    [System.Text.Json.Serialization.JsonIgnore] public required CookieContainer Cookies { get; init; }
    public required string ProfilePath { get; init; }
    public int ImportedCount { get; init; }
    public int ExpiredCount { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public static class BrowserCookieReader
{
    public static CookieContainer Load(string browser, CancellationToken ct = default) => Read(browser, ct).Cookies;
    public static string? Locate(string browser)
    {
        var selection = BrowserCookieSelection.Parse(browser);
        return selection is null ? null : selection.Browser == "firefox" ? FirefoxProfiles.Resolve(selection.Profile, FirefoxProfiles.DefaultRoot) : ChromiumProfiles.Resolve(selection.Browser, selection.Profile).CookiePath;
    }
    public static BrowserCookieImport Read(string browser, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var selection = BrowserCookieSelection.Parse(browser);
        var jar = ImportedCookieJar.Create();
        if (selection is null) return new() { Cookies = jar, ProfilePath = "" };
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native browser cookie import currently requires Windows.");
        if (selection.Browser != "firefox") return ChromiumCookieReader.Read(selection, ct);
        var path = FirefoxProfiles.Resolve(selection.Profile, FirefoxProfiles.DefaultRoot);
        var context = ResolveContainer(selection.Container, Path.GetDirectoryName(path)!);
        using var database = new ReadOnlyCookieDatabase(path, ct);
        var table = false;
        database.Read("SELECT type FROM sqlite_master WHERE name='moz_cookies'", row => table = row.Text(0, 32) == "table", 1);
        if (!table) throw new InvalidDataException("The selected database has no Firefox cookie table.");
        var columns = new HashSet<string>(StringComparer.Ordinal);
        database.Read("PRAGMA table_info(moz_cookies)", row => columns.Add(row.Text(1, 128)), 256);
        if (new[] { "host", "name", "value", "path", "expiry", "isSecure", "isHttpOnly" }.Any(name => !columns.Contains(name)))
            throw new NotSupportedException("This Firefox cookie schema is not supported.");
        if (context != 0 && !columns.Contains("originAttributes")) throw new NotSupportedException("This Firefox profile does not expose container identities.");
        database.Read("SELECT count(*) FROM moz_cookies", row =>
        {
            if (row.Integer(0) > ImportedCookieJar.MaximumCookies) throw new InvalidDataException("The Firefox cookie database exceeds the cookie-count limit.");
        }, 1);
        var attributes = columns.Contains("originAttributes") ? "originAttributes" : "''";
        var partitionFlag = columns.Contains("isPartitionedAttributeSet") ? "isPartitionedAttributeSet" : "0";
        var order = columns.Contains("lastAccessed") ? " ORDER BY lastAccessed" : "";
        var now = DateTimeOffset.UtcNow; var expired = 0; var invalid = 0; var isolated = 0;
        database.Read("SELECT host,name,value,path,expiry,isSecure,isHttpOnly," + attributes + "," + partitionFlag + " FROM moz_cookies" + order, row =>
        {
            try
            {
                var expiry = row.Integer(4);
                if (expiry <= now.ToUnixTimeSeconds()) { expired++; return; }
                var (container, isolation) = ReadOriginAttributes(row.Text(7, 8192));
                if (container != context) return;
                var partitioned = row.Integer(8);
                if (partitioned is not (0 or 1)) throw new InvalidDataException("Invalid Firefox partition flag.");
                if (isolation || partitioned == 1) { isolated++; return; }
                var secure = row.Integer(5); var httpOnly = row.Integer(6);
                if (secure is not (0 or 1) || httpOnly is not (0 or 1)) throw new InvalidDataException("Invalid browser cookie flags.");
                var host = row.Text(0, 255);
                ImportedCookieJar.Add(jar, host, host.StartsWith('.'), row.Text(3, 4096), row.Text(1), row.Text(2), secure == 1, httpOnly == 1, expiry, zeroIsSession: false, now);
            }
            catch (Exception ex) when (ex is InvalidDataException or CookieException) { invalid++; }
        }, ImportedCookieJar.MaximumCookies);
        var warnings = new List<string>();
        if (isolated > 0) warnings.Add("Skipped " + isolated + " partitioned or otherwise isolated Firefox cookies; that isolation model is not supported yet.");
        if (invalid > 0) warnings.Add("Skipped " + invalid + " malformed Firefox cookies. Cookie values were not logged.");
        if (jar.Count == 0) warnings.Add("The selected Firefox profile and container supplied no active supported cookies.");
        return new() { Cookies = jar, ProfilePath = path, ImportedCount = jar.Count, ExpiredCount = expired, Warnings = warnings };
    }
    private static int ResolveContainer(string? name, string profile)
    {
        if (string.IsNullOrEmpty(name) || name.Equals("none", StringComparison.OrdinalIgnoreCase)) return 0;
        if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return id;
        if (name.Equals("all", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("Importing all Firefox containers into one HTTP session would mix their accounts. Choose one container.");
        var path = Path.Combine(profile, "containers.json");
        if (!File.Exists(path)) throw new FileNotFoundException("The selected Firefox profile has no named-container metadata.", path);
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Firefox container metadata exceeds its size limit.");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        if (!json.RootElement.TryGetProperty("identities", out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 1000)
            throw new InvalidDataException("Firefox container metadata is invalid.");
        var matches = values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.Object && value.TryGetProperty("name", out var item) && item.ValueKind == JsonValueKind.String && name.Equals(item.GetString(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || !matches[0].TryGetProperty("userContextId", out var context) || !context.TryGetInt32(out id) || id <= 0)
            throw new InvalidDataException("The Firefox container name is missing or ambiguous. Use its numeric container ID.");
        return id;
    }
    private static (int Context, bool Isolated) ReadOriginAttributes(string value)
    {
        if (value.Length == 0) return (0, false);
        if (value[0] != '^') throw new InvalidDataException("Invalid Firefox cookie origin attributes.");
        var context = 0; var isolated = false; var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value[1..].Split('&'))
        {
            var split = item.Split('=', 2);
            if (split.Length != 2 || !seen.Add(split[0])) throw new InvalidDataException("Invalid Firefox cookie origin attributes.");
            if (split[0] == "userContextId")
            {
                if (!int.TryParse(split[1], NumberStyles.None, CultureInfo.InvariantCulture, out context)) throw new InvalidDataException("Invalid Firefox container identity.");
            }
            else if (split[0] == "privateBrowsingId" && split[1] == "0" || split[0] is "partitionKey" or "firstPartyDomain" && split[1].Length == 0) { }
            else isolated = true;
        }
        return (context, isolated);
    }
}

internal sealed record BrowserCookieSelection(string Browser, string? Profile, string? Container)
{
    internal static BrowserCookieSelection? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        if (value.Length > 4096 || value.Any(char.IsControl)) throw new ArgumentException("The browser cookie selection is invalid.");
        var text = value.Trim(); string? container = null;
        var context = text.IndexOf("::", StringComparison.Ordinal);
        if (context >= 0)
        {
            container = text[(context + 2)..]; text = text[..context];
            if (container.Length == 0 || container.Contains("::", StringComparison.Ordinal)) throw new ArgumentException("Specify one Firefox container name or numeric ID.");
        }
        var separator = text.IndexOf(':');
        var browser = (separator < 0 ? text : text[..separator]).ToLowerInvariant();
        if (browser is not ("firefox" or "chrome" or "edge" or "brave" or "chromium" or "opera" or "vivaldi"))
            throw new NotSupportedException("Native cookie import for this browser or keyring is not implemented yet. Use a supported Windows browser or an exported cookies file.");
        if (browser != "firefox" && container is not null) throw new NotSupportedException("Container selection applies only to Firefox profiles.");
        var profile = separator < 0 ? null : text[(separator + 1)..];
        if (profile is { Length: 0 }) throw new ArgumentException("Specify a Firefox profile name or path after the colon.");
        return new(browser, profile, container);
    }
}
