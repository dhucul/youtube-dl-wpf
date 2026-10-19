using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace YoutubeDl.Engine.Providers.Jamendo;

/// <summary>The public web client's endpoint-path checksum; query data is appended after signing.</summary>
internal static class JamendoRequestIdentity
{
    internal static string Create(Uri endpoint) => Create(endpoint, "0." + RandomNumberGenerator.GetInt32(100000000, 1000000000).ToString(CultureInfo.InvariantCulture));
    internal static string Create(Uri endpoint, string nonce)
    {
        if (endpoint.Scheme != "https" || endpoint.Host != "www.jamendo.com" || !endpoint.IsDefaultPort || endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0 ||
            endpoint.AbsolutePath is not ("/api/tracks" or "/api/albums" or "/api/artists")) throw new ArgumentException("The Jamendo checksum is scoped to public metadata endpoints.");
        if (nonce.Length > 32 || !nonce.StartsWith("0.", StringComparison.Ordinal) || nonce.Length < 3 || nonce[2..].Any(c => !char.IsAsciiDigit(c)))
            throw new ArgumentException("The Jamendo request nonce is invalid.");
        var digest = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(endpoint.AbsolutePath + nonce))).ToLowerInvariant();
        return "$" + digest + "*" + nonce + "~";
    }
}
