namespace YoutubeDl.Engine.Providers.Panopto;

internal sealed record PanoptoAddress(Uri Page, string Id)
{
    internal Uri Api => new(Page, "/Panopto/Pages/Viewer/DeliveryInfo.aspx");
    internal string Origin => Page.GetLeftPart(UriPartial.Authority);
    internal static bool Matches(Uri url) => url.IsAbsoluteUri && (url.AbsolutePath.Equals("/Panopto/Pages/Viewer.aspx", StringComparison.OrdinalIgnoreCase) ||
        url.AbsolutePath.Equals("/Panopto/Pages/Embed.aspx", StringComparison.OrdinalIgnoreCase));
    internal static PanoptoAddress Parse(Uri url)
    {
        if (!Matches(url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length > 0)
            throw new NotSupportedException("Use an HTTPS Panopto viewer or embedded recording URL.");
        var query = url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Split('=', 2)).ToArray();
        if (query.Any(part => Uri.UnescapeDataString(part[0]).Equals("pid", StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("Panopto playlists need additional native support. Supply a recording URL without a playlist identifier.");
        var parameters = query.Where(part => Uri.UnescapeDataString(part[0]).Equals("id", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (parameters.Length != 1 || parameters[0].Length != 2 || !Guid.TryParseExact(Uri.UnescapeDataString(parameters[0][1]), "D", out var id) || id == Guid.Empty)
            throw new NotSupportedException("Panopto folders and playlists need additional native support. Supply one recording's viewer URL.");
        return new(url, id.ToString("D"));
    }
    internal void CheckPage(Uri final)
    {
        var other = Parse(final);
        if (other.Origin != Origin || other.Id != Id) throw new InvalidDataException("Panopto redirected to another recording or server.");
    }
}
