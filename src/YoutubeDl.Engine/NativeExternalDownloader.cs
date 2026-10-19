using System.Globalization;
using System.Text;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine;

/// <summary>Explicit downloader choices and bounded curl transfer arguments.</summary>
public static class NativeExternalDownloader
{
    internal sealed record Selection(string Program, string[] Arguments);

    public static void Validate(string? downloader, IReadOnlyList<string>? arguments) => _ = Parse(downloader, arguments);

    internal static Selection? Parse(string? downloader, IReadOnlyList<string>? arguments)
    {
        var name = downloader?.Trim() ?? "";
        var native = name.Length == 0 || name.Equals("native", StringComparison.OrdinalIgnoreCase);
        if (name.Length > 4096 || name.Any(char.IsControl)) throw new ArgumentException("The downloader choice is invalid.");
        if (!native && !name.Equals("curl", StringComparison.OrdinalIgnoreCase) && !name.Equals("curl.exe", StringComparison.OrdinalIgnoreCase) &&
            !(Path.IsPathFullyQualified(name) && Path.GetFileName(name).Equals(OperatingSystem.IsWindows() ? "curl.exe" : "curl", StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("Choose native, curl, or an absolute path to curl for native HTTP file downloads.");
        if (arguments is { Count: > 32 }) throw new ArgumentException("Too many downloader argument groups.");
        var result = new List<string>();
        foreach (var group in arguments ?? [])
        {
            if (string.IsNullOrWhiteSpace(group) || group.Length > 4096 || group.Any(char.IsControl)) throw new ArgumentException("The downloader arguments are invalid.");
            if (native) throw new NotSupportedException("Downloader arguments require the curl backend.");
            var body = group.StartsWith("curl:", StringComparison.OrdinalIgnoreCase) ? group[5..] : group;
            var tokens = Split(body);
            for (var i = 0; i < tokens.Count; i++)
            {
                var equal = tokens[i].IndexOf('=');
                var flag = equal < 0 ? tokens[i] : tokens[i][..equal];
                if (flag is "--no-buffer" or "--tcp-nodelay" or "--no-keepalive" or "--http1.1")
                {
                    if (equal >= 0) throw new ArgumentException("This curl switch does not take a value.");
                    result.Add(flag); continue;
                }
                if (flag is not ("--connect-timeout" or "--max-time" or "--speed-time" or "--speed-limit" or "--limit-rate"))
                    throw new NotSupportedException("This curl argument needs additional native transfer support.");
                var value = equal >= 0 ? tokens[i][(equal + 1)..] : ++i < tokens.Count ? tokens[i] : throw new ArgumentException("A curl option needs a value.");
                if (flag == "--limit-rate")
                {
                    long? rate;
                    try { rate = Download.ByteRateLimiter.Parse(value); }
                    catch (FormatException) { throw new ArgumentException("The curl rate limit must be a positive byte count."); }
                    if (rate is not > 0) throw new ArgumentException("The curl rate limit must be positive.");
                    value = rate.Value.ToString(CultureInfo.InvariantCulture);
                }
                else if (!double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var count) ||
                    !double.IsFinite(count) || count <= 0 || count > (flag == "--speed-limit" ? 1_000_000_000 : 86400) ||
                    flag is "--speed-time" or "--speed-limit" && count != Math.Truncate(count))
                    throw new ArgumentException("The curl option needs a positive bounded number.");
                result.Add(flag); result.Add(value);
            }
        }
        if (result.Count > 128) throw new ArgumentException("Too many curl arguments.");
        return native ? null : new(name, result.ToArray());
    }

    internal static void ValidateMedia(NativeDownloadRequest request, params MediaCandidate?[] candidates)
    {
        if (Parse(request.ExternalDownloader, request.ExternalDownloaderArguments) is null) return;
        if (candidates.Any(candidate => candidate is not null && (candidate.Kind is not (MediaKind.Progressive or MediaKind.HttpSequence) || candidate.Url.Scheme is not ("http" or "https"))))
            throw new NotSupportedException("The curl backend currently supports HTTP/HTTPS files and HTTP sequences; adaptive and live sources require the native downloader.");
    }

    internal static string Resolve(Selection selection)
    {
        if (Path.IsPathFullyQualified(selection.Program))
        {
            if (!File.Exists(selection.Program)) throw new FileNotFoundException("The configured curl executable was not found.");
            return selection.Program;
        }
        // Do not search the current working directory or consult curl/browser configuration.
        var system = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe") : "/usr/bin/curl";
        if (!File.Exists(system)) throw new FileNotFoundException("curl was not found. Configure an absolute curl executable path or choose native.");
        return system;
    }

    private static List<string> Split(string text)
    {
        var result = new List<string>(); var word = new StringBuilder(); char quote = '\0';
        foreach (var ch in text)
        {
            if (ch == quote) quote = '\0';
            else if (quote == '\0' && ch is '\'' or '"') quote = ch;
            else if (quote == '\0' && char.IsWhiteSpace(ch)) { if (word.Length > 0) { result.Add(word.ToString()); word.Clear(); } }
            else word.Append(ch);
        }
        if (quote != '\0') throw new ArgumentException("The curl arguments contain an unterminated quote.");
        if (word.Length > 0) result.Add(word.ToString());
        if (result.Count == 0) throw new ArgumentException("The curl arguments are empty.");
        return result;
    }
}
