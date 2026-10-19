using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Discovery;

internal static class ChromiumCookieReader
{
    internal static BrowserCookieImport Read(BrowserCookieSelection selection, CancellationToken ct)
    {
        var profile = ChromiumProfiles.Resolve(selection.Browser, selection.Profile);
        using var database = new ReadOnlyCookieDatabase(profile.CookiePath, ct);
        var tables = new HashSet<string>(StringComparer.Ordinal);
        database.Read("SELECT name FROM sqlite_master WHERE type='table' AND name IN ('cookies','meta')", row => tables.Add(row.Text(0, 32)), 2);
        if (tables.Count != 2) throw new InvalidDataException("The browser database is missing its cookies or version table.");
        var version = 0;
        database.Read("SELECT CAST(value AS TEXT) FROM meta WHERE key='version'", row =>
        {
            if (!int.TryParse(row.Text(0, 32), NumberStyles.None, CultureInfo.InvariantCulture, out version)) throw new InvalidDataException("The browser cookie schema version is invalid.");
        }, 1);
        if (version is < 5 or > 24) throw new NotSupportedException("This Chromium cookie schema version is not supported.");
        var columns = new HashSet<string>(StringComparer.Ordinal);
        database.Read("PRAGMA table_info(cookies)", row => columns.Add(row.Text(1, 128)), 256);
        if (new[] { "host_key", "name", "value", "encrypted_value", "path", "expires_utc", "is_secure", "is_httponly", "has_expires", "is_persistent" }.Any(name => !columns.Contains(name)))
            throw new NotSupportedException("This Chromium cookie table layout is not supported.");
        database.Read("SELECT count(*) FROM cookies", row =>
        { if (row.Integer(0) > ImportedCookieJar.MaximumCookies) throw new InvalidDataException("The browser cookie database exceeds the cookie-count limit."); }, 1);
        var partition = columns.Contains("top_frame_site_key") ? "top_frame_site_key" : "''";
        var order = columns.Contains("last_access_utc") ? " ORDER BY last_access_utc" : "";
        var jar = ImportedCookieJar.Create(); var now = DateTimeOffset.UtcNow; var expired = 0; var isolated = 0; var invalid = 0;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        using var cryptography = new ChromiumCookieCryptography(profile.LocalStatePath);
        database.Read("SELECT host_key,name,value,encrypted_value,path,expires_utc,is_secure,is_httponly,has_expires,is_persistent," + partition + " FROM cookies" + order, row =>
        {
            byte[]? encrypted = null;
            try
            {
                if (row.Text(10, 8192).Length > 0) { isolated++; return; }
                var hasExpiry = row.Integer(8); var persistent = row.Integer(9);
                if (hasExpiry is not (0 or 1) || persistent != hasExpiry) throw new InvalidDataException("Invalid browser persistence flags.");
                DateTimeOffset? expiration = null;
                if (hasExpiry == 1)
                {
                    var microseconds = row.Integer(5);
                    if (microseconds <= 0) { expired++; return; }
                    try { expiration = new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(checked(microseconds * 10)); }
                    catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException) { throw new InvalidDataException("Invalid browser cookie expiration."); }
                    if (expiration <= now) { expired++; return; }
                }
                var secure = row.Integer(6); var httpOnly = row.Integer(7);
                if (secure is not (0 or 1) || httpOnly is not (0 or 1)) throw new InvalidDataException("Invalid browser cookie flags.");
                var host = row.Text(0, 255); var name = row.Text(1); var path = row.Text(4, 4096); var plain = row.Text(2);
                encrypted = row.Blob(3);
                if (encrypted.Length > 0 && plain.Length > 0) throw new InvalidDataException("Browser cookie has conflicting stored values.");
                var identity = host.ToLowerInvariant() + "\0" + path + "\0" + name;
                if (!keys.Add(identity)) throw new NotSupportedException("The browser stores multiple source-bound cookies with the same identity. Export a selected cookie set instead.");
                string value;
                try { value = encrypted.Length == 0 ? plain : cryptography.Decrypt(encrypted, host, version); }
                catch (InvalidDataException) { throw new CryptographicException("The browser encryption configuration is invalid. Select the original profile or export cookies."); }
                ImportedCookieJar.Add(jar, host, host.StartsWith('.'), path, name, value, secure == 1, httpOnly == 1, expiration?.ToUnixTimeSeconds() ?? 0, true, now, expiration);
            }
            catch (Exception ex) when (ex is InvalidDataException or CookieException) { invalid++; }
            finally { if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted); }
        }, ImportedCookieJar.MaximumCookies);
        var warnings = new List<string>();
        if (isolated > 0) warnings.Add("Skipped " + isolated + " partitioned browser cookies; partition-aware import is not implemented yet.");
        if (invalid > 0) warnings.Add("Skipped " + invalid + " malformed browser cookies. Cookie values were not logged.");
        if (jar.Count == 0) warnings.Add("The selected browser profile supplied no active supported cookies.");
        return new() { Cookies = jar, ProfilePath = profile.CookiePath, ImportedCount = jar.Count, ExpiredCount = expired, Warnings = warnings };
    }
}
