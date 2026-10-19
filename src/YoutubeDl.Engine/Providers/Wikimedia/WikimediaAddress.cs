using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Wikimedia;

internal sealed record WikimediaAddress(string Host, string Title, bool OriginalOnly = false)
{
    internal Uri Page => new("https://" + Host + "/wiki/" + Uri.EscapeDataString(Title.Replace(' ', '_')));
    internal Uri Api => new("https://" + Host + "/w/api.php?action=query&format=json&formatversion=2&redirects=1&prop=videoinfo&titles=" + Uri.EscapeDataString(Title) +
        "&viprop=url%7Csize%7Cmime%7Cmediatype%7Cmetadata%7Cderivatives%7Ctimedtext%7Ccanonicaltitle%7Cuser%7Cextmetadata&viextmetadatafilter=Artist%7CImageDescription%7CObjectName&viurlwidth=640");
    internal static bool IsHost(Uri url) => url.IsAbsoluteUri && url.Scheme is "http" or "https" && url.IsDefaultPort && url.UserInfo.Length == 0 &&
        (url.Host is "www.mediawiki.org" or "www.wikidata.org" || Regex.IsMatch(url.Host, @"\A[a-z0-9-]{1,40}(?:\.m)?\.(?:wikimedia|wikipedia|wiktionary|wikibooks|wikinews|wikiquote|wikisource|wikiversity|wikivoyage)\.org\z", RegexOptions.CultureInvariant));
    internal static WikimediaAddress Parse(Uri url)
    {
        if (!IsHost(url)) throw new ArgumentException("Unsupported Wikimedia host.");
        var query = Query(url);
        if (query.ContainsKey("oldid") || query.ContainsKey("diff") || query.ContainsKey("width") || query.TryGetValue("action", out var action) && action is not ("" or "view") || url.Fragment.StartsWith("#t=", StringComparison.Ordinal))
            throw new NotSupportedException("Historical, resized, edited or clipped Wikimedia addresses require additional native support.");
        var title = url.AbsolutePath.StartsWith("/wiki/", StringComparison.Ordinal) ? Uri.UnescapeDataString(url.AbsolutePath[6..]) :
            url.AbsolutePath == "/w/index.php" && query.TryGetValue("title", out var named) ? named : throw new NotSupportedException("Use a Wikimedia file page.");
        var original = false;
        foreach (var prefix in new[] { "Special:FilePath/", "Special:Redirect/file/" })
            if (title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { title = "File:" + title[prefix.Length..]; original = true; break; }
        if (title.StartsWith("Image:", StringComparison.OrdinalIgnoreCase)) title = "File:" + title[6..];
        if (!title.StartsWith("File:", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("Use the file page rather than an article or collection.");
        title = "File:" + title[5..].Replace('_', ' ').Trim(); ValidateTitle(title);
        return new(url.Host.Replace(".m.", ".", StringComparison.Ordinal), title, original);
    }
    internal static void ValidateTitle(string title)
    {
        var colon = title.IndexOf(':');
        if (colon <= 0 || title.Length - colon is < 2 or > 1024 || title.Any(char.IsControl) || title[(colon + 1)..].IndexOfAny(['/', '\\', '|', '#']) >= 0)
            throw new InvalidDataException("The Wikimedia file title is invalid.");
    }
    internal static string FileName(string title) { ValidateTitle(title); return title[(title.IndexOf(':') + 1)..].Replace(' ', '_'); }
    internal (string Family, string Project) Storage
    {
        get
        {
            if (Host == "www.mediawiki.org") return ("wikipedia", "mediawiki");
            if (Host == "www.wikidata.org") return ("wikipedia", "wikidata");
            var parts = Host.Split('.'); return (parts[^2] == "wikimedia" ? "wikipedia" : parts[^2], parts[0]);
        }
    }
    internal static Dictionary<string, string> Query(Uri url)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2); var key = Uri.UnescapeDataString(pair[0].Replace('+', ' ')); var value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "";
            if (!result.TryAdd(key, value)) throw new InvalidDataException("The Wikimedia address repeats a query field.");
        }
        return result;
    }
}
