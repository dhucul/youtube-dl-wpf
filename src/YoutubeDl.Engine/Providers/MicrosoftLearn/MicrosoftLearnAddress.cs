using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.MicrosoftLearn;

internal sealed record MicrosoftLearnAddress(string Locale, string Kind, string Collection, string? Slug, string? EntryId = null)
{
    internal bool IsCollection => Slug is null && EntryId is null;
    internal Uri Page => EntryId is null ? new("https://learn.microsoft.com/" + Locale + "/" + Kind + "/" + Collection + (Slug is null ? "/" : "/" + Slug)) :
        new("https://learn.microsoft.com/_themes/docs.theme/master/en-us/_themes/global/video-embed-one-stream.html?id=" + EntryId);
    internal static bool Matches(Uri url) { try { _ = Parse(url); return true; } catch (ArgumentException) { return false; } }
    internal static MicrosoftLearnAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0) throw new ArgumentException("A supported HTTPS Microsoft Learn video URL is required.");
        if (url.Host == "learn-video.azurefd.net" && url.AbsolutePath == "/vod/player" || url.Host == "learn.microsoft.com" &&
            Regex.IsMatch(url.AbsolutePath, @"\A/_themes/docs\.theme/master/[a-z-]+/_themes/global/video-embed(?:-one-stream)?\.html\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            var query = Query(url); var locale = query.GetValueOrDefault("locale", "en-us");
            if (!LocaleValid(locale)) throw new ArgumentException("Unsupported Microsoft Learn player locale.");
            var show = query.GetValueOrDefault("show"); var episode = query.GetValueOrDefault("ep"); var ev = query.GetValueOrDefault("ev"); var session = query.GetValueOrDefault("session"); var id = query.GetValueOrDefault("id");
            if (show is not null && episode is not null && ev is null && session is null && id is null && SlugValid(show) && SlugValid(episode)) return new(locale, "shows", show, episode);
            if (ev is not null && session is not null && show is null && episode is null && id is null && SlugValid(ev) && SlugValid(session)) return new(locale, "events", ev, session);
            if (id is not null && show is null && episode is null && ev is null && session is null) return new(locale, "entry", "", "", GuidId(id));
            throw new ArgumentException("The Microsoft Learn player has missing or conflicting video identities.");
        }
        if (url.Host is not ("learn.microsoft.com" or "docs.microsoft.com")) throw new ArgumentException("This is not a Microsoft Learn video origin.");
        var parts = url.AbsolutePath.Trim('/').Split('/');
        if (parts.Length is < 3 or > 4 || !LocaleValid(parts[0]) || parts[1] is not ("shows" or "events") || !SlugValid(parts[2]) || parts.Length == 4 && !SlugValid(parts[3]) || parts.Length == 3 && parts[1] != "shows")
            throw new ArgumentException("This Microsoft Learn page is not a supported show, episode or event session.");
        return new(parts[0], parts[1], parts[2], parts.Length == 4 ? parts[3] : null);
    }
    internal static string GuidId(string raw) => Guid.TryParseExact(raw, "D", out var id) && id != Guid.Empty ? id.ToString("D") : throw new ArgumentException("The Microsoft Learn entry identifier is invalid.");
    internal static bool SlugValid(string value) => Regex.IsMatch(value, @"\A[a-z0-9][a-z0-9_-]{0,199}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static bool LocaleValid(string value) => Regex.IsMatch(value, @"\A[a-z]{2,3}(?:-[a-z]{2,8}){1,2}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static Dictionary<string, string> Query(Uri url)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2); var key = Uri.UnescapeDataString(parts[0]); var value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "";
            if (!result.TryAdd(key, value)) throw new ArgumentException("Repeated Microsoft Learn query fields are unsupported.");
        }
        return result;
    }
}
