using System.Text.RegularExpressions;

namespace YoutubeDl.Engine;

/// <summary>Parses user-configured request headers without logging their values.</summary>
public static class NativeHttpHeaders
{
    internal static NativeDownloadRequest Normalize(NativeDownloadRequest request)
    {
        Validate(request.HttpHeaders);
        var remaining = new Dictionary<string, string>(request.HttpHeaders, StringComparer.OrdinalIgnoreCase);
        if (remaining.Remove("User-Agent", out var agent)) request = request with { UserAgent = agent };
        if (remaining.Remove("Referer", out var referer)) request = request with { Referer = referer };
        return request with { HttpHeaders = remaining };
    }
    public static IReadOnlyDictionary<string, string> ParseLines(string? text)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (text ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            Add(headers, line);
        return headers;
    }
    public static IReadOnlyDictionary<string, string> Add(IReadOnlyDictionary<string, string> existing, string line)
    {
        var headers = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase); Add(headers, line); return headers;
    }
    private static void Add(Dictionary<string, string> headers, string line)
    {
        var colon = line.IndexOf(':');
        if (colon <= 0) throw new ArgumentException("Each custom header must use Name: Value syntax.");
        var name = line[..colon].Trim(); var value = line[(colon + 1)..].Trim(); Validate(name, value);
        headers[name] = value;
        if (headers.Count > 100 || headers.Sum(pair => pair.Key.Length + pair.Value.Length) > 65536) throw new ArgumentException("The custom request headers exceed their size limit.");
    }
    internal static void Validate(string name, string value)
    {
        if (name.Length is 0 or > 256 || !Regex.IsMatch(name, @"\A[!#$%&'*+.^_`|~0-9A-Za-z-]+\z", RegexOptions.CultureInvariant) ||
            value.Length > 16384 || value.Any(character => char.IsControl(character) && character != '\t')) throw new ArgumentException("A custom request header has an invalid name or value.");
        if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) || name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Custom request framing headers require additional native support.");
    }
    internal static void Validate(IReadOnlyDictionary<string, string> headers)
    {
        if (headers.Count > 100 || headers.Sum(pair => pair.Key.Length + (pair.Value?.Length ?? 0)) > 65536) throw new ArgumentException("The custom request headers exceed their size limit.");
        foreach (var pair in headers) { if (pair.Value is null) throw new ArgumentException("A custom request header has no value."); Validate(pair.Key, pair.Value); }
    }
}
