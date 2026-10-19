using System.Net;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.MicrosoftEmbed;

internal sealed record MicrosoftEmbedAddress(Uri Page, string? PowerId, IReadOnlyDictionary<string, IReadOnlyList<string>> Query)
{
    internal static bool Matches(Uri url) => url.IsAbsoluteUri && url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Query.Length <= 256 * 1024 && url.Host is "www.microsoft.com" or "microsoft.com" &&
        Regex.IsMatch(url.AbsolutePath, @"\A/(?:[a-z]{2,3}-[a-z]{2,8}/)?videoplayer/embed(?:/[A-Za-z0-9]{1,80})?/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static MicrosoftEmbedAddress Parse(Uri url)
    {
        if (!Matches(url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Query.Length > 256 * 1024)
            throw new ArgumentException("A supported HTTPS Microsoft video player address is required.");
        var query = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var part in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2); var key = WebUtility.UrlDecode(pair[0]); var value = WebUtility.UrlDecode(pair.Length == 2 ? pair[1] : "");
            if (query.TryGetValue(key, out var previous))
            {
                if (key is not ("mp4" or "vtt" or "ttml" or "dlAudio" or "dlVideo")) throw new ArgumentException("The Microsoft player has repeated identity/settings fields.");
                query[key] = previous.Append(value).ToArray();
            }
            else query.Add(key, new[] { value });
            if (query.Count > 64 || query.Values.Sum(values => values.Count) > 128) throw new ArgumentException("The Microsoft player has excessive settings.");
        }
        var power = url.AbsolutePath.TrimEnd('/').Split('/').Last(); if (power == "embed") power = null;
        if (query.TryGetValue("powerCmsVideoId", out var supplied))
        {
            if (!Identifier(supplied[0]) || power is not null && power != supplied[0]) throw new ArgumentException("The Microsoft player has conflicting video IDs.");
            power = supplied[0];
        }
        return new(new UriBuilder(url) { Host = "www.microsoft.com", Fragment = "" }.Uri, power, query);
    }
    internal static bool Identifier(string value) => Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9_-]{0,159}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}
