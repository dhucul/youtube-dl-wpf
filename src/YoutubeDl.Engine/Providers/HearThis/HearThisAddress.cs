using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.HearThis;

internal sealed record HearThisAddress(string Artist, string? Track = null, string? EmbeddedId = null)
{
    internal static bool Matches(Uri url) { try { Parse(url); return true; } catch (ArgumentException) { return false; } }
    internal static HearThisAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || url.Host is not ("hearthis.at" or "www.hearthis.at" or "api-v2.hearthis.at") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0)
            throw new ArgumentException("Use a public hearthis.at track, artist or player address.");
        var parts = url.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray(); var embed = parts.Length is 2 or 3 && parts[0] == "embed";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Uri.UnescapeDataString(item.Split('=', 2)[0]);
            if (!seen.Add(name) || !(name.StartsWith("utm_", StringComparison.Ordinal) || embed && name is "hcolor" or "color" or "style" or "block_size" or "block_space" or "background" or "waveform" or "cover" or "autoplay"))
                throw new ArgumentException("This hearthis.at access or collection selector needs additional native support.");
        }
        if (embed)
        {
            if (!Numeric(parts[1]) || parts.Length == 3 && !Slug(parts[2])) throw new ArgumentException("Use a public numeric hearthis.at player.");
            return new("", EmbeddedId: parts[1]);
        }
        if (parts.Length is not (1 or 2) || !Slug(parts[0]) || parts[0] is "feed" or "search" or "login" or "logout" or "categories" or "api-v2" or "embed" || parts.Length == 2 && !Slug(parts[1]))
            throw new ArgumentException("This hearthis.at route needs additional native support.");
        return new(parts[0], parts.Length == 2 ? parts[1] : null);
    }
    internal static bool Slug(string? value) => value is not null && Regex.IsMatch(value, @"\A[a-zA-Z0-9][a-zA-Z0-9_.-]{0,299}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static bool Numeric(string? value) => value is not null && Regex.IsMatch(value, @"\A[1-9][0-9]{0,14}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal Uri Web => EmbeddedId is not null ? new("https://hearthis.at/embed/" + EmbeddedId + "/transparent_black/") : new("https://hearthis.at/" + Artist + "/" + (Track is null ? "" : Track + "/"));
    internal Uri Api => new("https://api-v2.hearthis.at/" + Artist + "/" + (Track is null ? "" : Track + "/"));
}
