using System.Text.Json;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.YouTube;

internal sealed record YouTubeStreamUrl(Uri? Url, FormatAvailability Availability, string? Reason)
{
    public static YouTubeStreamUrl Read(JsonElement format, bool hasSabr, IYouTubePlayerSession? player = null, CancellationToken ct = default)
    {
        var raw = Text(format, "url");
        Dictionary<string, string>? cipher = null;
        var cipherText = Text(format, "signatureCipher") ?? Text(format, "cipher");
        if (raw is null && cipherText is not null)
        {
            cipher = YouTubeQuery.Parse(cipherText);
            cipher.TryGetValue("url", out raw);
        }
        if (raw is null) return new(null, hasSabr ? FormatAvailability.AdaptiveTransportRequired : FormatAvailability.MissingUrl,
            hasSabr ? "YouTube SABR transport has not been implemented yet." : "YouTube did not expose a stream URL for this format.");
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.Scheme != "https" || !string.IsNullOrEmpty(url.UserInfo) ||
            !url.IsDefaultPort || !(url.Host.Equals("googlevideo.com", StringComparison.OrdinalIgnoreCase) ||
                url.Host.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("YouTube returned an invalid media stream address.");
        var transformed = false;
        if (cipher is not null)
        {
            if (cipher.TryGetValue("s", out var signature) && !string.IsNullOrWhiteSpace(signature))
            {
                if (player is null) return new(null, FormatAvailability.PlayerChallengeRequired, "The YouTube signature challenge has not been solved yet.");
                var parameter = cipher.GetValueOrDefault("sp", "signature");
                if (!Regex.IsMatch(parameter, @"\A[A-Za-z0-9_]{1,32}\z")) throw new InvalidDataException("YouTube returned an invalid signature parameter.");
                url = Transform(player, url, parameter, signature, ct);
                transformed = true;
            }
            else if (cipher.TryGetValue("sig", out var clearSignature) || cipher.TryGetValue("signature", out clearSignature))
            {
                var parameter = cipher.GetValueOrDefault("sp", "signature");
                if (!Regex.IsMatch(parameter, @"\A[A-Za-z0-9_]{1,32}\z")) throw new InvalidDataException("YouTube returned an invalid signature parameter.");
                url = YouTubeQuery.Set(url, parameter, clearSignature);
            }
        }
        if (!transformed && YouTubeQuery.Parse(url.Query).TryGetValue("n", out var throttle) && !string.IsNullOrEmpty(throttle))
        {
            if (player is null) return new(null, FormatAvailability.PlayerChallengeRequired, "The YouTube throttling challenge has not been solved yet.");
            url = Transform(player, url, "signature", "", ct);
        }
        return new(url, FormatAvailability.Ready, null);
    }

    private static Uri Transform(IYouTubePlayerSession player, Uri original, string parameter, string signature, CancellationToken ct)
    {
        var result = player.Transform(original, parameter, signature, ct);
        if (!result.IsAbsoluteUri || result.Scheme != "https" || result.Host != original.Host || result.Port != original.Port ||
            result.AbsolutePath != original.AbsolutePath || !string.IsNullOrEmpty(result.UserInfo) || result.Fragment.Length != 0)
            throw new InvalidDataException("The YouTube player transformation changed the media origin or path.");
        var after = YouTubeQuery.Parse(result.Query);
        if (signature.Length > 0 && (!after.TryGetValue(parameter, out var value) || string.IsNullOrEmpty(value)))
            throw new InvalidDataException("The YouTube player did not return the required signature.");
        if (YouTubeQuery.Parse(original.Query).TryGetValue("n", out var n) && !string.IsNullOrEmpty(n) &&
            (!after.TryGetValue("n", out var changed) || string.IsNullOrEmpty(changed) || changed == n))
            throw new InvalidDataException("The YouTube player did not solve the throttling transformation.");
        return result;
    }

    private static string? Text(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
        ? value.GetString() : null;
}
