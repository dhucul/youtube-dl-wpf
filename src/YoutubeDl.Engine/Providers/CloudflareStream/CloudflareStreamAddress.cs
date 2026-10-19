using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Providers.CloudflareStream;

internal sealed record CloudflareStreamAddress(string Key, string? ClaimedId, bool Signed, Uri Url, MediaKind? ManifestKind)
{
    internal string FallbackId => ClaimedId ?? "token-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Key))).ToLowerInvariant();
    internal static bool IsCustomer(string host) => Regex.IsMatch(host, @"\Acustomer-[a-z0-9]{1,64}\.cloudflarestream\.com\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static bool IsHost(Uri url) => url.IsAbsoluteUri && url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 &&
        (IsCustomer(url.Host) || url.Host is "iframe.videodelivery.net" or "videodelivery.net" or "cloudflarestream.com" or "watch.cloudflarestream.com" or "embed.cloudflarestream.com" or "embed.videodelivery.net");
    internal static bool IsId(string? value) => value is not null && Regex.IsMatch(value, @"\A[0-9a-fA-F]{32}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static CloudflareStreamAddress Parse(Uri url)
    {
        if (!IsHost(url) || url.Fragment.Length > 0) throw new ArgumentException("Use a supported HTTPS Cloudflare Stream address.");
        if (url.Host is "embed.cloudflarestream.com" or "embed.videodelivery.net")
        {
            if (!Regex.IsMatch(url.AbsolutePath, @"\A/embed/[A-Za-z0-9_.-]{1,128}\.js\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                throw new NotSupportedException("This Stream SDK address does not identify a supported video.");
            string? video = null; var remaining = new List<string>();
            foreach (var part in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split('=', 2);
                if (Uri.UnescapeDataString(pair[0]) == "video")
                {
                    var value = Uri.UnescapeDataString(pair.Length == 2 ? pair[1] : "");
                    if (video is not null && video != value) throw new ArgumentException("The Stream SDK address contains conflicting video identities.");
                    video = value;
                }
                else remaining.Add(part);
            }
            if (string.IsNullOrEmpty(video)) throw new NotSupportedException("The Stream SDK address has no video identity.");
            // Validate the decoded value before inserting it into a URL. It must be
            // a video key, not another path or a new set of query parameters.
            if (!IsId(video)) _ = TokenSubject(video);
            return Parse(new Uri("https://iframe.videodelivery.net/" + video + (remaining.Count > 0 ? "?" + string.Join('&', remaining) : "")));
        }
        var parts = url.AbsolutePath.Trim('/').Split('/');
        if (parts.Length is < 1 or > 3) throw new NotSupportedException("This Cloudflare Stream address is not a supported player or manifest.");
        var key = parts[0]; var signed = !IsId(key); string? id = signed ? TokenSubject(key) : key.ToLowerInvariant();
        MediaKind? kind = parts.Length == 3 && parts[1] == "manifest" ? parts[2] switch { "video.m3u8" => MediaKind.Hls, "video.mpd" => MediaKind.Dash, _ => null } : null;
        if (!(parts.Length == 1 || parts.Length == 2 && parts[1] is "iframe" or "watch" || kind is not null))
            throw new NotSupportedException("This Cloudflare Stream path requires additional native support.");
        return new(key, id, signed, url, kind);
    }
    private static string? TokenSubject(string key)
    {
        if (key.Length > 8192 || !Regex.IsMatch(key, @"\A[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new ArgumentException("The Cloudflare Stream video ID or supplied token is invalid.");
        try
        {
            var part = key.Split('.')[1].Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(part.PadRight((part.Length + 3) / 4 * 4, '='));
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("The supplied Stream token payload is invalid.");
            var subject = Providers.ProviderJson.Text(json.RootElement, "sub");
            if (subject is not null && !IsId(subject)) throw new ArgumentException("The supplied Stream token has an unsupported subject.");
            return subject?.ToLowerInvariant();
        }
        catch (Exception ex) when (ex is FormatException or JsonException) { throw new ArgumentException("The supplied Stream token payload is invalid."); }
    }
    internal Uri Asset(string host, string suffix, string? referer = null)
    {
        var query = Url.Query;
        var hasParent = query.TrimStart('?').Split('&').Any(part => Uri.UnescapeDataString(part.Split('=')[0]) == "parentOrigin");
        if (!hasParent && Uri.TryCreate(referer, UriKind.Absolute, out var parent) && parent.Scheme is "http" or "https" && parent.UserInfo.Length == 0)
            query += (query.Length == 0 ? "?" : "&") + "parentOrigin=" + Uri.EscapeDataString(parent.GetLeftPart(UriPartial.Authority));
        return new Uri("https://" + host + "/" + Key + "/" + suffix + query);
    }
}
