using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Providers.Audiomack;

internal static class AudiomackClientConfiguration
{
    internal static async Task<AudiomackPublicClient> ReadAsync(string html, MediaHttp http, CancellationToken ct)
    {
        if (html.Length > 8 * 1024 * 1024) throw new InvalidDataException("Audiomack returned excessive public website data.");
        var paths = Regex.Matches(html, @"<script\b[^>]*\bsrc=[""'](?<src>[^""']+)[""']", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250))
            .Select(match => System.Net.WebUtility.HtmlDecode(match.Groups["src"].Value)).Where(path => path.StartsWith("/_next/static/chunks/", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (paths.Length is < 1 or > 64) throw new NotSupportedException("Audiomack did not expose a bounded public client configuration inventory.");
        // Prefer the current public configuration group; still inspect advertised chunks when the group changes.
        var ordered = paths.OrderBy(path => Path.GetFileName(path).StartsWith("7919-", StringComparison.Ordinal) ? 0 : 1).ToArray(); var total = 0;
        foreach (var path in ordered)
        {
            ct.ThrowIfCancellationRequested(); var uri = new Uri(new Uri("https://audiomack.com"), path);
            if (uri.Host != "audiomack.com" || uri.Scheme != "https" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
                throw new InvalidDataException("Audiomack advertised an unscoped client-source address.");
            var document = await http.GetDocumentAsync(uri, 4 * 1024 * 1024, ct).ConfigureAwait(false);
            if (document.Url != uri) throw new InvalidDataException("Audiomack redirected client-source discovery outside its advertised resource.");
            total = checked(total + document.Text.Length); if (total > 16 * 1024 * 1024) throw new InvalidDataException("Audiomack combined public client data exceeds its limit.");
            if (!document.Text.Contains("API_CONSUMER_KEY", StringComparison.Ordinal) || !document.Text.Contains("API_CONSUMER_SECRET", StringComparison.Ordinal)) continue;
            return Parse(document.Text);
        }
        throw new NotSupportedException("This Audiomack public client configuration layout needs additional native support.");
    }
    internal static AudiomackPublicClient Parse(string source)
    {
        string Literal(string name)
        {
            var matches = Regex.Matches(source, @"\b" + name + @"\s*:\s*(?<value>""(?:\\.|[^""\\])*"")", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (matches.Count != 1) throw new InvalidDataException("Audiomack returned ambiguous or computed public client configuration.");
            return JsonSerializer.Deserialize<string>(matches[0].Groups["value"].Value) ?? throw new InvalidDataException("Audiomack returned invalid public client configuration.");
        }
        var endpoint = Literal("API_PUBLIC_API_URL"); var key = Literal("API_CONSUMER_KEY"); var secret = Literal("API_CONSUMER_SECRET");
        if (endpoint != "https://api.audiomack.com/v1" || key.Length is < 1 or > 100 || secret.Length is < 1 or > 200 || key.Any(char.IsControl) || secret.Any(char.IsControl))
            throw new InvalidDataException("Audiomack public client configuration is outside its supported anonymous API scope.");
        return new(key, secret);
    }
}
