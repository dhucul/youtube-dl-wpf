using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Bluesky;

internal sealed record BlueskyAddress(string Actor, string Key)
{
    internal Uri Web => new("https://bsky.app/profile/" + Actor + "/post/" + Key);
    internal string Record(string did) => "at://" + did + "/app.bsky.feed.post/" + Key;
    internal static BlueskyAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || url.Host is not ("bsky.app" or "www.bsky.app") ||
            url.UserInfo.Length != 0 || !url.IsDefaultPort || url.Fragment.Length != 0) throw new ArgumentException("Unsupported Bluesky post address.");
        var parts = url.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 4 || parts[0] != "profile" || parts[2] != "post") throw new ArgumentException("A Bluesky post address is required.");
        var actor = Uri.UnescapeDataString(parts[1]);
        if (!Did(actor) && !Handle(actor) || !Regex.IsMatch(parts[3], @"\A[A-Za-z0-9._~:-]{1,512}\z", RegexOptions.CultureInvariant) || parts[3] is "." or "..")
            throw new ArgumentException("Invalid Bluesky actor or post key.");
        return new(actor, parts[3]);
    }
    internal static BlueskyAddress FromRecord(string? record)
    {
        var match = Regex.Match(record ?? "", @"\Aat://(?<actor>[^/]+)/app\.bsky\.feed\.post/(?<key>[^/?#]+)\z", RegexOptions.CultureInvariant);
        if (!match.Success || !Did(match.Groups["actor"].Value)) throw new InvalidDataException("Bluesky returned an invalid post reference.");
        try { return Parse(new Uri("https://bsky.app/profile/" + match.Groups["actor"].Value + "/post/" + match.Groups["key"].Value)); }
        catch (ArgumentException) { throw new InvalidDataException("Bluesky returned an invalid post reference."); }
    }
    internal static bool Did(string value) => Regex.IsMatch(value, @"\Adid:plc:[a-z2-7]{24}\z", RegexOptions.CultureInvariant) || value.StartsWith("did:web:", StringComparison.Ordinal) && Handle(value[8..]);
    internal static bool Handle(string value) => value.Length <= 253 && Regex.IsMatch(value, @"\A(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\z", RegexOptions.CultureInvariant);
}
