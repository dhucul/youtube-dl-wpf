using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Rumble;

internal sealed record RumbleAddress(string Id, bool Embed, Uri Url)
{
    internal static bool IsHost(Uri url) => url.IsAbsoluteUri && url.Scheme is "http" or "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Host is "rumble.com" or "www.rumble.com";
    internal static RumbleAddress Parse(Uri url)
    {
        if (!IsHost(url)) throw new ArgumentException("Use a Rumble video or embed address.");
        var embed = Regex.Match(url.AbsolutePath, @"\A/embed/(?:u[0-9a-z]{1,16}\.)?(?<id>v[0-9a-z]{1,12})/?\z", RegexOptions.CultureInvariant);
        var watch = Regex.Match(url.AbsolutePath, @"\A/(?<id>v[0-9a-z]{1,12})(?:-[^/]{1,512})?\.html\z", RegexOptions.CultureInvariant);
        var share = Regex.Match(url.AbsolutePath, @"\A/share/(?<id>v[0-9a-z]{1,12})/?\z", RegexOptions.CultureInvariant);
        var match = embed.Success ? embed : watch.Success ? watch : share;
        if (!match.Success) throw new NotSupportedException("This Rumble page type requires additional native support.");
        var id = match.Groups["id"].Value; _ = NumericId(id);
        return new(id, embed.Success, url);
    }
    internal static long NumericId(string id)
    {
        if (!Regex.IsMatch(id, @"\Av[0-9a-z]{1,12}\z", RegexOptions.CultureInvariant)) throw new InvalidDataException("Rumble returned an invalid video identifier.");
        long value = 0;
        try { foreach (var character in id.AsSpan(1)) value = checked(value * 36 + (character is >= '0' and <= '9' ? character - '0' : character - 'a' + 10)); }
        catch (OverflowException) { throw new InvalidDataException("Rumble returned an excessive video identifier."); }
        return value > 0 ? value : throw new InvalidDataException("Rumble returned an empty video identifier.");
    }
    internal static Uri EmbedUrl(string id) => new("https://rumble.com/embed/" + id + "/");
}
