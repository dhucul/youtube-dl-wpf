using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Providers.YouTube;

/// <summary>Public challenge material, not an authorization token or proof of playback access.</summary>
public sealed record YouTubeAttestationChallenge(string Challenge, string GlobalName, string Program,
    string? InterpreterHash, Uri? InterpreterUrl, string? InterpreterScript, string? ClientExperimentsStateBlob);

/// <summary>Reads the page/API challenge without executing HTML or page scripts.</summary>
public static class YouTubeAttestationParser
{
    public const int MaximumResponseBytes = 512 * 1024;
    public const int MaximumInterpreterBytes = 256 * 1024;

    public static YouTubeAttestationChallenge? ReadPage(string html)
    {
        if (Encoding.UTF8.GetByteCount(html) > 8 * 1024 * 1024) throw new InvalidDataException("The YouTube page exceeds the challenge extraction limit.");
        var matches = Regex.Matches(html, @"\bytAtN\(\{\s*'R'\s*:\s*(?<json>'(?:\\.|[^'\\])*')",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (matches.Count == 0) return null;
        if (matches.Count != 1) throw new InvalidDataException("The page contains ambiguous attestation responses.");
        return ReadResponse(DecodeLiteral(matches[0].Groups["json"].Value));
    }

    public static YouTubeAttestationChallenge ReadResponse(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumResponseBytes) throw new InvalidDataException("The attestation response exceeds its limit.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        ValidateObject(root);
        if (!root.TryGetProperty("bgChallenge", out var bg)) throw new InvalidDataException("YouTube did not return a native attestation challenge.");
        ValidateObject(bg);
        var challenge = Text(root, "challenge", 16 * 1024, required: true)!;
        var global = Text(bg, "globalName", 64, required: true)!;
        if (!Regex.IsMatch(global, @"\A[A-Za-z_$][A-Za-z0-9_$]*\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new InvalidDataException("The attestation global name is invalid.");
        var program = Text(bg, "program", 256 * 1024, required: true)!;
        var hash = Text(bg, "interpreterHash", 256);
        var experiments = Text(bg, "clientExperimentsStateBlob", 16 * 1024);
        Uri? url = null;
        string? script = null;
        if (bg.TryGetProperty("interpreterUrl", out var address))
        {
            ValidateObject(address);
            var raw = Text(address, "privateDoNotAccessOrElseTrustedResourceUrlWrappedValue", 4096, required: true)!;
            if (!Uri.TryCreate(raw.StartsWith("//", StringComparison.Ordinal) ? "https:" + raw : raw, UriKind.Absolute, out url) || !IsInterpreterUrl(url))
                throw new InvalidDataException("The attestation interpreter address is invalid.");
        }
        if (bg.TryGetProperty("interpreterJavascript", out var javascript))
        {
            ValidateObject(javascript);
            script = Text(javascript, "privateDoNotAccessOrElseSafeScriptWrappedValue", MaximumInterpreterBytes, required: true);
        }
        if (url is null && script is null) throw new InvalidDataException("The attestation challenge has no interpreter.");
        return new(challenge, global, program, hash, url, script, experiments);
    }

    internal static bool IsInterpreterUrl(Uri url) => url.IsAbsoluteUri && url.Scheme == "https" && url.Host == "www.google.com" &&
        url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 && url.AbsolutePath.StartsWith("/js/th/", StringComparison.Ordinal) && url.AbsolutePath.Length > 7;

    private static void ValidateObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().GroupBy(property => property.Name, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new InvalidDataException("The attestation document contains an invalid or repeated field.");
    }

    private static string? Text(JsonElement value, string key, int limit, bool required = false)
    {
        if (!value.TryGetProperty(key, out var field))
        {
            if (required) throw new InvalidDataException("The attestation document is missing " + key + ".");
            return null;
        }
        if (field.ValueKind != JsonValueKind.String) throw new InvalidDataException("The attestation field " + key + " is not text.");
        var text = field.GetString()!;
        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > limit || text.Contains('\0'))
            throw new InvalidDataException("The attestation field " + key + " is empty, invalid, or excessive.");
        return text;
    }

    private static string DecodeLiteral(string literal)
    {
        if (literal.Length > 4 * MaximumResponseBytes) throw new InvalidDataException("The encoded attestation response exceeds its limit.");
        var text = new StringBuilder();
        for (var index = 1; index < literal.Length - 1; index++)
        {
            var value = literal[index];
            if (value != '\\') text.Append(value);
            else
            {
                if (++index >= literal.Length - 1) throw new InvalidDataException("The attestation string escape is incomplete.");
                value = literal[index];
                switch (value)
                {
                    case 'x': case 'u':
                        var count = value == 'x' ? 2 : 4;
                        if (index + count >= literal.Length - 1 || !ushort.TryParse(literal.AsSpan(index + 1, count), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                            throw new InvalidDataException("The attestation string has an invalid character escape.");
                        text.Append((char)code); index += count; break;
                    case '\\': case '\'': case '"': case '/': text.Append(value); break;
                    case 'n': text.Append('\n'); break;
                    case 'r': text.Append('\r'); break;
                    case 't': text.Append('\t'); break;
                    case 'b': text.Append('\b'); break;
                    case 'f': text.Append('\f'); break;
                    case 'v': text.Append('\v'); break;
                    case '\n': break;
                    case '\r': if (index + 1 < literal.Length - 1 && literal[index + 1] == '\n') index++; break;
                    default: throw new InvalidDataException("The attestation string uses an unsupported escape.");
                }
            }
            if (text.Length > MaximumResponseBytes) throw new InvalidDataException("The decoded attestation response exceeds its limit.");
        }
        return text.ToString();
    }
}

/// <summary>Uses the caller's isolated HTTP session to fetch public challenge material.</summary>
public static class YouTubeAttestationClient
{
    public static async Task<YouTubeAttestationChallenge> GetAsync(string html, MediaHttp http, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (YouTubeAttestationParser.ReadPage(html) is { } pageChallenge) return pageChallenge;
        var version = Regex.Match(html, @"""INNERTUBE_CLIENT_VERSION""\s*:\s*""(?<version>[^""]+)""",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Groups["version"].Value;
        if (version.Length is < 1 or > 128 || version.Any(value => char.IsControl(value)))
            throw new InvalidDataException("YouTube did not supply a client version for attestation.");
        var response = await http.PostJsonAsync(new Uri("https://www.youtube.com/youtubei/v1/att/get?prettyPrint=false"),
            new { context = new { client = new { clientName = "WEB", clientVersion = version } }, engagementType = "ENGAGEMENT_TYPE_UNBOUND" },
            YouTubeAttestationParser.MaximumResponseBytes, new Dictionary<string, string> { ["Origin"] = "https://www.youtube.com" }, ct).ConfigureAwait(false);
        return YouTubeAttestationParser.ReadResponse(response);
    }

    public static async Task<string> LoadInterpreterAsync(YouTubeAttestationChallenge challenge, MediaHttp http, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (challenge.InterpreterScript is { } script)
        {
            if (string.IsNullOrWhiteSpace(script) || script.Contains('\0') || Encoding.UTF8.GetByteCount(script) > YouTubeAttestationParser.MaximumInterpreterBytes)
                throw new InvalidDataException("The attestation interpreter is empty, invalid, or excessive.");
            return script;
        }
        if (challenge.InterpreterUrl is not { } url || !YouTubeAttestationParser.IsInterpreterUrl(url))
            throw new InvalidDataException("The attestation interpreter address is invalid.");
        var document = await http.GetDocumentAsync(url, YouTubeAttestationParser.MaximumInterpreterBytes, ct).ConfigureAwait(false);
        if (!YouTubeAttestationParser.IsInterpreterUrl(document.Url)) throw new InvalidDataException("The attestation interpreter redirect is invalid.");
        return document.Text;
    }
}
