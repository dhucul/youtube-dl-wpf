using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Audiomack;

/// <summary>Independent RFC 5849 signing for the public anonymous metadata/playback endpoints.</summary>
internal static class AudiomackOAuth
{
    internal static Uri Sign(Uri endpoint, IReadOnlyDictionary<string, string> values, AudiomackPublicClient client, long? timestamp = null, string? nonce = null)
    {
        if (endpoint.Scheme != "https" || endpoint.Host != "api.audiomack.com" || !endpoint.IsDefaultPort || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.UserInfo.Length != 0 ||
            !Regex.IsMatch(endpoint.AbsolutePath, @"\A/v1/music/(?:(?:song|album)/[a-z0-9][a-z0-9_-]{0,299}/[a-z0-9][a-z0-9_-]{0,299}|play/[1-9][0-9]{0,14})\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new ArgumentException("Audiomack signing is limited to public music metadata and playback.");
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            if (pair.Key.StartsWith("oauth_", StringComparison.Ordinal) || pair.Key is "key" or "token" or "secret") throw new ArgumentException("Private Audiomack authentication requires a separate supported workflow.");
            if (pair.Key.Length > 100 || pair.Value.Length > 1000 || pair.Key.Any(char.IsControl) || pair.Value.Any(char.IsControl)) throw new ArgumentException("Audiomack request parameters exceed their bounds.");
            parameters.Add(pair.Key, pair.Value);
        }
        var stamp = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(); var requestNonce = nonce ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        if (stamp < 0 || requestNonce.Length is < 1 or > 128 || requestNonce.Any(char.IsControl)) throw new ArgumentException("The Audiomack request identity is invalid.");
        parameters.Add("oauth_consumer_key", client.Key); parameters.Add("oauth_signature_method", "HMAC-SHA1"); parameters.Add("oauth_timestamp", stamp.ToString(CultureInfo.InvariantCulture));
        parameters.Add("oauth_nonce", requestNonce); parameters.Add("oauth_version", "1.0");
        var normalized = string.Join('&', parameters.Select(pair => (Key: Encode(pair.Key), Value: Encode(pair.Value))).OrderBy(pair => pair.Key, StringComparer.Ordinal).ThenBy(pair => pair.Value, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value));
        var input = "GET&" + Encode(endpoint.AbsoluteUri) + "&" + Encode(normalized);
        var signature = Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(Encode(client.Secret) + "&"), Encoding.UTF8.GetBytes(input)));
        return new(endpoint.AbsoluteUri + "?" + normalized + "&oauth_signature=" + Encode(signature));
    }
    private static string Encode(string value) => Uri.EscapeDataString(value);
}

internal sealed record AudiomackPublicClient(string Key, string Secret)
{
    public override string ToString() => "Audiomack public web-client configuration";
}
