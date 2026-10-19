using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Providers.YouTube;

public sealed record YouTubeWaaChallenge(string RequestKey, string? MessageId, YouTubeAttestationChallenge Material);
/// <summary>Service-returned candidates. Their presence does not establish playback authorization.</summary>
public sealed record YouTubeIntegrityResponse(byte[] IntegrityToken, byte[] FallbackToken, int EstimatedTtlSeconds, int MintRefreshThreshold);

/// <summary>Native reader/client for the public player's WAA RPC protocol, with no token-generation implementation.</summary>
public static class YouTubeWaaClient
{
    private const string Endpoint = "https://jnn-pa.googleapis.com/$rpc/google.internal.waa.v1.Waa/";
    // Public service identifier supplied by Google's web client; this is not a user credential.
    private const string PublicApiKey = "AIzaSyDyT5W0Jh49F30Pqqtyfdf7pDLFKLJoAnw";

    public static async Task<YouTubeWaaChallenge> CreateAsync(string requestKey, MediaHttp http, CancellationToken ct, string? interpreterHash = null)
    {
        ValidateKey(requestKey);
        if (interpreterHash is not null && (string.IsNullOrWhiteSpace(interpreterHash) || interpreterHash.Length > 256 || interpreterHash.Any(char.IsControl)))
            throw new ArgumentException("The WAA interpreter hash is invalid.", nameof(interpreterHash));
        var body = interpreterHash is null ? new[] { requestKey } : new[] { requestKey, interpreterHash };
        var json = await CallAsync("Create", body, http, YouTubeAttestationParser.MaximumResponseBytes, ct).ConfigureAwait(false);
        return ReadChallenge(json, requestKey);
    }

    public static async Task<YouTubeIntegrityResponse> GenerateIntegrityAsync(string requestKey, string snapshot, MediaHttp http, CancellationToken ct)
    {
        ValidateKey(requestKey);
        if (string.IsNullOrWhiteSpace(snapshot) || Encoding.UTF8.GetByteCount(snapshot) > 64 * 1024 || snapshot.Contains('\0'))
            throw new ArgumentException("The WAA snapshot is empty or excessive.", nameof(snapshot));
        var json = await CallAsync("GenerateIT", new[] { requestKey, snapshot }, http, 64 * 1024, ct).ConfigureAwait(false);
        return ReadIntegrity(json);
    }

    public static YouTubeWaaChallenge ReadChallenge(string json, string requestKey)
    {
        ValidateKey(requestKey);
        if (Encoding.UTF8.GetByteCount(json) > YouTubeAttestationParser.MaximumResponseBytes) throw new InvalidDataException("The WAA challenge response exceeds its limit.");
        using var response = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = response.RootElement;
        if (root.ValueKind != JsonValueKind.Array) throw new InvalidDataException("The WAA challenge is not an array message.");
        JsonDocument? decoded = null;
        try
        {
            JsonElement data;
            if (root.GetArrayLength() > 1 && root[1].ValueKind == JsonValueKind.String)
            {
                var bytes = DecodeBase64(root[1].GetString()!, YouTubeAttestationParser.MaximumResponseBytes);
                // The public RPC's scrambled field encodes every UTF-8 byte minus 97 modulo 256.
                for (var index = 0; index < bytes.Length; index++) bytes[index] = unchecked((byte)(bytes[index] + 97));
                decoded = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(bytes), new JsonDocumentOptions { MaxDepth = 32 });
                data = decoded.RootElement;
            }
            else data = root.GetArrayLength() > 0 && root[0].ValueKind == JsonValueKind.Array ? root[0] : root;
            if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() < 6) throw new InvalidDataException("The WAA challenge message is incomplete.");
            var messageId = OptionalText(data[0]);
            var script = WrappedText(data[1]);
            var url = data.GetArrayLength() > 2 ? WrappedText(data[2]) : null;
            var hash = OptionalText(data[3]);
            var program = RequiredText(data[4]);
            var name = RequiredText(data[5]);
            var experiments = data.GetArrayLength() > 7 ? OptionalText(data[7]) : null;
            var bg = new Dictionary<string, object?> { ["program"] = program, ["globalName"] = name };
            if (hash is not null) bg["interpreterHash"] = hash;
            if (experiments is not null) bg["clientExperimentsStateBlob"] = experiments;
            if (script is not null) bg["interpreterJavascript"] = new Dictionary<string, string> { ["privateDoNotAccessOrElseSafeScriptWrappedValue"] = script };
            if (url is not null) bg["interpreterUrl"] = new Dictionary<string, string> { ["privateDoNotAccessOrElseTrustedResourceUrlWrappedValue"] = url };
            var normalized = JsonSerializer.Serialize(new { challenge = messageId ?? requestKey, bgChallenge = bg });
            return new(requestKey, messageId, YouTubeAttestationParser.ReadResponse(normalized));
        }
        finally { decoded?.Dispose(); }
    }

    public static YouTubeIntegrityResponse ReadIntegrity(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > 64 * 1024) throw new InvalidDataException("The WAA integrity response exceeds its limit.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2) throw new InvalidDataException("The WAA integrity response is incomplete.");
        var token = OptionalText(root[0]);
        if (root[1].ValueKind != JsonValueKind.Number || !root[1].TryGetInt32(out var ttl) || ttl is <= 0 or > 7 * 24 * 60 * 60) throw new InvalidDataException("The WAA token lifetime is invalid.");
        var refresh = 0;
        if (root.GetArrayLength() > 2 && root[2].ValueKind != JsonValueKind.Null && (root[2].ValueKind != JsonValueKind.Number || !root[2].TryGetInt32(out refresh) || refresh < 0))
            throw new InvalidDataException("The WAA mint refresh threshold is invalid.");
        var fallback = root.GetArrayLength() > 3 ? OptionalText(root[3]) : null;
        return new(token is null ? [] : DecodeBase64(token, 16384), fallback is null ? [] : DecodeBase64(fallback, 16384), ttl, refresh);
    }

    private static async Task<string> CallAsync(string operation, string[] body, MediaHttp http, int limit, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (http.AllowAutoRedirect) throw new ArgumentException("WAA RPC sessions require automatic redirects to be disabled.", nameof(http));
        var url = new Uri(Endpoint + operation);
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json+protobuf") };
        request.Headers.TryAddWithoutValidation("x-goog-api-key", PublicApiKey);
        request.Headers.TryAddWithoutValidation("x-user-agent", "grpc-web-javascript/0.1");
        request.Headers.TryAddWithoutValidation("Origin", "https://www.youtube.com");
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if ((response.RequestMessage?.RequestUri ?? url) != url) throw new InvalidDataException("The WAA RPC response changed its endpoint.");
        if (response.Content.Headers.ContentLength is > 0 && response.Content.Headers.ContentLength > limit) throw new InvalidDataException("The WAA RPC response exceeds its limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var timed = http.WithReadTimeout(stream);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await timed.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0) break;
            if (bytes.Length + read > limit) throw new InvalidDataException("The WAA RPC response exceeds its limit.");
            bytes.Write(buffer, 0, read);
        }
        return new UTF8Encoding(false, true).GetString(bytes.ToArray());
    }

    private static string? WrappedText(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Array) throw new InvalidDataException("The WAA interpreter wrapper is invalid.");
        var text = value.EnumerateArray().Where(field => field.ValueKind == JsonValueKind.String).ToArray();
        if (text.Length != 1) throw new InvalidDataException("The WAA interpreter wrapper is empty or ambiguous.");
        return RequiredText(text[0]);
    }
    private static string? OptionalText(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : RequiredText(value);
    private static string RequiredText(JsonElement value) => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : throw new InvalidDataException("The WAA message contains an invalid text field.");
    private static byte[] DecodeBase64(string encoded, int limit)
    {
        if (encoded.Length > limit * 4L / 3 + 4 || !Regex.IsMatch(encoded, @"\A[A-Za-z0-9_+/\-]*={0,2}\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new InvalidDataException("The WAA message contains invalid or excessive base64 data.");
        var normalized = encoded.Replace('-', '+').Replace('_', '/');
        try
        {
            var bytes = Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '='));
            if (bytes.Length > limit) throw new InvalidDataException("The WAA decoded message exceeds its limit.");
            return bytes;
        }
        catch (FormatException ex) { throw new InvalidDataException("The WAA message contains invalid base64 data.", ex); }
    }
    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !Regex.IsMatch(key, @"\A[A-Za-z0-9_\-]{1,128}\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new ArgumentException("The WAA request key is invalid.", nameof(key));
    }
}
