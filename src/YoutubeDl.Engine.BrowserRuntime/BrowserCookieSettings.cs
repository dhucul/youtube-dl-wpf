using System.IO;
using System.Net;
using System.Text;
using Microsoft.Web.WebView2.Core;
using YoutubeDl.Engine.Providers.YouTube;

namespace YoutubeDl.Engine.BrowserRuntime;

internal static class BrowserCookieSettings
{
    internal static async Task ImportAsync(CoreWebView2 core, YouTubePlaybackCookies snapshot, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var cookies = snapshot.Items.Where(cookie => cookie.Expires is null || cookie.Expires > now).ToArray();
        if (cookies.GroupBy(cookie => Group(cookie.Domain)).Any(group => group.Count() > 180) ||
            cookies.Any(cookie => Encoding.UTF8.GetByteCount(cookie.Name + cookie.Value + cookie.Domain + cookie.Path) > 4096))
            throw new NotSupportedException("The selected cookie set exceeds the browser's supported cookie limits.");
        foreach (var cookie in cookies)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var value = core.CookieManager.CreateCookie(cookie.Name, cookie.Value, (cookie.IncludeSubdomains ? "." : "") + cookie.Domain, cookie.Path);
                value.IsSecure = cookie.Secure; value.IsHttpOnly = cookie.HttpOnly;
                if (cookie.Expires is { } expiry) value.Expires = expiry.UtcDateTime;
                core.CookieManager.AddOrUpdateCookie(value);
            }
            catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException)
            { throw new InvalidDataException("A selected cookie could not be imported into the temporary browser session."); }
        }
        var actual = await ExportAsync(core, ct);
        foreach (var cookie in cookies)
        {
            var matching = actual.Items.FirstOrDefault(item => item.Name == cookie.Name && item.Domain.Equals(cookie.Domain, StringComparison.OrdinalIgnoreCase) && item.Path == cookie.Path);
            if (matching is null || matching.Value != cookie.Value || matching.IncludeSubdomains != cookie.IncludeSubdomains || matching.Secure != cookie.Secure || matching.HttpOnly != cookie.HttpOnly ||
                cookie.Expires is null != (matching.Expires is null) || cookie.Expires is { } expected && matching.Expires is { } found && Math.Abs((expected - found).TotalSeconds) > 1)
                throw new InvalidDataException("The temporary browser could not preserve the selected cookie fields.");
        }
    }
    internal static async Task<YouTubePlaybackCookies> ExportAsync(CoreWebView2 core, CancellationToken ct)
    {
        var values = await core.CookieManager.GetCookiesAsync(null).WaitAsync(ct);
        var jar = new CookieContainer(10000, 10000, 16384); var now = DateTime.UtcNow;
        foreach (var value in values)
        {
            ct.ThrowIfCancellationRequested();
            if (!YouTubePlaybackCookies.IsRelevantDomain(value.Domain) || !value.IsSession && value.Expires.ToUniversalTime() <= now) continue;
            try
            {
                var cookie = new Cookie(value.Name, value.Value, value.Path) { Secure = value.IsSecure, HttpOnly = value.IsHttpOnly };
                if (!value.IsSession) cookie.Expires = value.Expires.ToUniversalTime();
                if (value.Domain.StartsWith('.')) cookie.Domain = value.Domain;
                jar.Add(new UriBuilder(value.IsSecure ? "https" : "http", value.Domain.TrimStart('.')).Uri, cookie);
            }
            catch (CookieException) { throw new InvalidDataException("A captured cookie has unsupported fields."); }
        }
        return YouTubePlaybackCookies.FromContainer(jar, ct);
    }
    private static string Group(string host) => host.EndsWith("googlevideo.com", StringComparison.OrdinalIgnoreCase) ? "googlevideo.com" : host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase) ? "youtube.com" : "google.com";
}
