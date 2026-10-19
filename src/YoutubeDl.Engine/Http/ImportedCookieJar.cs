using System.Globalization;
using System.Net;
using System.Text;

namespace YoutubeDl.Engine.Http;

internal static class ImportedCookieJar
{
    internal const int MaximumCookies = 50000;
    internal const int MaximumCookieBytes = 16384;
    internal static CookieContainer Create() => new(100000, 100000, MaximumCookieBytes);

    internal static bool Add(CookieContainer jar, string host, bool includeSubdomains, string path, string name, string value,
        bool secure, bool httpOnly, long expiry, bool zeroIsSession, DateTimeOffset now, DateTimeOffset? preciseExpiry = null)
    {
        if (preciseExpiry is { } exact ? exact <= now : expiry < 0 || expiry == 0 && !zeroIsSession || expiry > 0 && expiry <= now.ToUnixTimeSeconds()) return false;
        if (host.Length is 0 or > 255 || host.Any(char.IsWhiteSpace) || host.Any(char.IsControl) || name.Length == 0 ||
            path.Length == 0 || path[0] != '/' || path.Any(char.IsControl) || name.Any(char.IsControl) || value.Any(char.IsControl) ||
            Encoding.UTF8.GetByteCount(name) + Encoding.UTF8.GetByteCount(value) > MaximumCookieBytes)
            throw new CookieException("An imported cookie has invalid fields.");
        var domain = host.TrimStart('.');
        if (domain.Length == 0 || domain.EndsWith('.') || domain.Contains('/') || domain.Contains('\\') || domain.Contains('@'))
            throw new CookieException("An imported cookie has an invalid domain.");
        try { domain = new IdnMapping().GetAscii(domain); }
        catch (ArgumentException) { throw new CookieException("An imported cookie has an invalid domain."); }
        if (Uri.CheckHostName(domain) == UriHostNameType.Unknown) throw new CookieException("An imported cookie has an invalid domain.");
        var cookie = new Cookie(name, value, path) { Secure = secure, HttpOnly = httpOnly };
        if (preciseExpiry is { } specific) cookie.Expires = specific.UtcDateTime;
        else if (expiry > 0)
        {
            try { cookie.Expires = DateTimeOffset.FromUnixTimeSeconds(expiry).UtcDateTime; }
            catch (ArgumentOutOfRangeException) { throw new CookieException("An imported cookie has an invalid expiration time."); }
        }
        if (includeSubdomains) cookie.Domain = "." + domain;
        // Adding by origin with no Domain attribute preserves host-only scope.
        jar.Add(new UriBuilder(secure ? "https" : "http", domain).Uri, cookie);
        return true;
    }
}
