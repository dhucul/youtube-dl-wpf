using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Mastodon;

internal sealed record MastodonAddress(Uri Origin, string Id, string? Account)
{
    public Uri Api => new(Origin, "/api/v1/statuses/" + Id);
    public static bool Matches(Uri url) { try { _ = Parse(url); return true; } catch (ArgumentException) { return false; } }
    public static MastodonAddress Parse(Uri url)
    {
        if (!PublicHttps(url)) throw new ArgumentException("A public HTTPS Mastodon status address is required.");
        var match = Regex.Match(url.AbsolutePath, @"\A(?:/@(?<account>[A-Za-z0-9_]{1,128}(?:@[A-Za-z0-9.-]{1,253})?)/(?<id>[1-9][0-9]{0,39})|/users/(?<account>[A-Za-z0-9_]{1,128})/statuses/(?<id>[1-9][0-9]{0,39})(?:/activity)?|/(?:web/)?statuses/(?<id>[1-9][0-9]{0,39}))(?:/embed)?/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) throw new ArgumentException("This Mastodon URL is not a supported public status address.");
        var account = match.Groups["account"].Success ? match.Groups["account"].Value : null;
        if (account?.Contains('@') == true && !Host(account.Split('@')[1])) throw new ArgumentException("The remote Mastodon account has an invalid domain.");
        return new(new Uri(url.GetLeftPart(UriPartial.Authority)), match.Groups["id"].Value, account);
    }
    internal static bool PublicHttps(Uri url) => url.IsAbsoluteUri && url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 && Host(url.IdnHost);
    internal static bool Host(string host) => Uri.CheckHostName(host) == UriHostNameType.Dns && host.Contains('.') && !host.EndsWith(".", StringComparison.Ordinal) &&
        host.Split('.').All(label => label.Length is > 0 and <= 63 && Regex.IsMatch(label, @"\A[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
}
