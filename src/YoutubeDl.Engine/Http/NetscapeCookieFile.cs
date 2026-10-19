using System.Net;
using System.Globalization;

namespace YoutubeDl.Engine.Http;

/// <summary>Reads a Netscape cookies.txt file into a <see cref="CookieContainer"/>.</summary>
public static class NetscapeCookieFile
{
    public static CookieContainer Load(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ct.ThrowIfCancellationRequested();
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("The selected cookies file does not exist.", path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > 16 * 1024 * 1024) throw new InvalidDataException("The cookies file exceeds the 16 MiB import limit.");
        using var reader = new StreamReader(input);
        var container = ImportedCookieJar.Create();
        var now = DateTimeOffset.UtcNow; var count = 0; var lineNumber = 0;
        while (reader.ReadLine() is { } raw)
        {
            ct.ThrowIfCancellationRequested(); lineNumber++;
            var line = raw;
            var httpOnly = line.StartsWith("#HttpOnly_", StringComparison.Ordinal);
            if (httpOnly) line = line[10..];
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            if (++count > ImportedCookieJar.MaximumCookies) throw new InvalidDataException("The cookies file exceeds the cookie-count limit.");
            var parts = line.Split('\t');
            if (parts.Length != 7 || !bool.TryParse(parts[1], out var includeSubdomains) || !bool.TryParse(parts[3], out var secure) ||
                !long.TryParse(parts[4], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var expiry))
                throw InvalidLine(lineNumber);
            try
            {
                ImportedCookieJar.Add(container, parts[0], includeSubdomains, parts[2], parts[5], parts[6], secure, httpOnly, expiry, zeroIsSession: true, now);
            }
            catch (CookieException)
            {
                throw InvalidLine(lineNumber);
            }
        }
        return container;
        static InvalidDataException InvalidLine(int line) => new("The cookies file contains an invalid cookie on line " + line + ". Cookie values were not logged.");
    }
}
