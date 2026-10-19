using System.Net;
using System.Text.Json.Serialization;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Providers.YouTube;

/// <summary>An owned cookie snapshot restricted to YouTube playback and associated Google identity hosts.</summary>
public sealed class YouTubePlaybackCookies
{
    [JsonIgnore] public IReadOnlyList<YouTubeSessionCookie> Items { get; }
    public int Count => Items.Count;
    public static YouTubePlaybackCookies Empty { get; } = new([]);
    private YouTubePlaybackCookies(YouTubeSessionCookie[] cookies) => Items = Array.AsReadOnly(cookies);
    public static bool IsRelevantDomain(string domain)
    {
        var host = domain.TrimStart('.').ToLowerInvariant();
        return host == "youtube.com" || host.EndsWith(".youtube.com", StringComparison.Ordinal) || host is "google.com" or "www.google.com" or "accounts.google.com" ||
            host == "googlevideo.com" || host.EndsWith(".googlevideo.com", StringComparison.Ordinal);
    }
    public static YouTubePlaybackCookies FromContainer(CookieContainer jar, CancellationToken ct = default)
    {
        var result = new List<YouTubeSessionCookie>(); var now = DateTime.UtcNow;
        foreach (Cookie cookie in jar.GetAllCookies())
        {
            ct.ThrowIfCancellationRequested();
            if (cookie.Expired || !IsRelevantDomain(cookie.Domain) || cookie.Expires != DateTime.MinValue && cookie.Expires.ToUniversalTime() <= now) continue;
            if (cookie.Version != 0 || !string.IsNullOrEmpty(cookie.Port)) throw new NotSupportedException("Versioned or port-bound playback cookies require additional support.");
            var host = cookie.Domain.TrimStart('.');
            // Domain without a leading dot can be either host-only or explicitly set
            // by a server. Observe the jar's matching policy instead of widening it.
            var includesChildren = jar.GetCookies(new UriBuilder("https", "scope-check." + host) { Path = cookie.Path }.Uri).Cast<Cookie>()
                .Any(value => value.Name == cookie.Name && value.Path == cookie.Path && value.Domain.Equals(cookie.Domain, StringComparison.OrdinalIgnoreCase) && value.Value == cookie.Value);
            result.Add(new(cookie.Name, cookie.Value, host, cookie.Path, includesChildren, cookie.Secure, cookie.HttpOnly,
                cookie.Expires == DateTime.MinValue ? null : new DateTimeOffset(cookie.Expires.ToUniversalTime())));
            if (result.Count > 2000) throw new InvalidDataException("The selected YouTube cookie set exceeds its supported count.");
        }
        return new(result.ToArray());
    }
    public CookieContainer ToContainer(CancellationToken ct = default)
    {
        var jar = ImportedCookieJar.Create(); var now = DateTimeOffset.UtcNow;
        foreach (var cookie in Items)
        {
            ct.ThrowIfCancellationRequested();
            ImportedCookieJar.Add(jar, cookie.Domain, cookie.IncludeSubdomains, cookie.Path, cookie.Name, cookie.Value, cookie.Secure, cookie.HttpOnly,
                cookie.Expires?.ToUnixTimeSeconds() ?? 0, true, now, cookie.Expires);
        }
        return jar;
    }
    public static YouTubePlaybackCookies LoadRequested(NativeDownloadRequest request, CancellationToken ct = default)
    {
        var jar = !string.IsNullOrWhiteSpace(request.CookieFile) ? NetscapeCookieFile.Load(request.CookieFile, ct) :
            !string.IsNullOrWhiteSpace(request.BrowserCookies) ? BrowserCookieReader.Load(request.BrowserCookies, ct) : null;
        return jar is null ? Empty : FromContainer(jar, ct);
    }
    public override string ToString() => "Scoped YouTube cookies (" + Count + ")";
}

public sealed record YouTubeSessionCookie(
    [property: JsonIgnore] string Name, [property: JsonIgnore] string Value,
    [property: JsonIgnore] string Domain, [property: JsonIgnore] string Path,
    [property: JsonIgnore] bool IncludeSubdomains, [property: JsonIgnore] bool Secure, [property: JsonIgnore] bool HttpOnly,
    [property: JsonIgnore] DateTimeOffset? Expires)
{
    public override string ToString() => "Scoped playback cookie";
}
