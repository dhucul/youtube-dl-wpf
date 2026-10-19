using System.Globalization;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Kaltura;

public sealed record KalturaAddress(int PartnerId, string EntryId, string? Session = null)
{
    public Uri PlayerUrl => new($"https://cdnapisec.kaltura.com/p/{PartnerId}/embedPlaykitJs?entry_id={Uri.EscapeDataString(EntryId)}" + (Session is null ? "" : "&ks=" + Uri.EscapeDataString(Session)));
    public static bool IsPlayer(Uri url) => url.IsAbsoluteUri && url.Scheme is "http" or "https" && url.IsDefaultPort && url.UserInfo.Length == 0 &&
        url.Host is "www.kaltura.com" or "kaltura.com" or "cdnapisec.kaltura.com" or "cdnapi.kaltura.com" &&
        (url.AbsolutePath.Contains("/embed", StringComparison.OrdinalIgnoreCase) || url.AbsolutePath.Contains("/kwidget", StringComparison.OrdinalIgnoreCase) || url.AbsolutePath.Contains("/extwidget", StringComparison.OrdinalIgnoreCase));

    public static KalturaAddress Parse(Uri url)
    {
        if (!IsPlayer(url)) throw new ArgumentException("This is not a supported Kaltura player address.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var parts = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index + 1 < parts.Length; index++)
        {
            var key = parts[index];
            if (key is "p" or "partner_id" or "wid") Put("partner", Uri.UnescapeDataString(parts[++index]).TrimStart('_'));
            else if (key == "entry_id") Put("entry", Uri.UnescapeDataString(parts[++index]));
        }
        foreach (var part in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2); var key = Decode(pair[0]); var value = Decode(pair.Length == 2 ? pair[1] : "");
            switch (key)
            {
                case "partner_id": case "partnerId": case "wid": Put("partner", value.TrimStart('_')); break;
                case "entry_id": case "entryId": case "flashvars[entryId]": Put("entry", value); break;
                case "ks": case "flashvars[ks]": Put("session", value); break;
                case "playlist_id": case "playlistId": case "service_url": case "config[provider]":
                    throw new NotSupportedException("Native Kaltura playlists and custom provider configurations remain pending.");
            }
        }
        if (!int.TryParse(values.GetValueOrDefault("partner"), NumberStyles.None, CultureInfo.InvariantCulture, out var partner) || partner <= 0 ||
            values.GetValueOrDefault("entry") is not { } entry || !IsEntryId(entry)) throw new ArgumentException("The Kaltura partner or entry identifier is missing or invalid.");
        var session = values.GetValueOrDefault("session");
        if (session is not null && (string.IsNullOrWhiteSpace(session) || session.Length > 8192 || session.Any(char.IsControl))) throw new ArgumentException("The Kaltura session parameter is invalid.");
        return new(partner, entry, session);

        void Put(string key, string value)
        {
            if (values.TryGetValue(key, out var previous) && previous != value) throw new ArgumentException("The Kaltura address contains conflicting identifiers or sessions.");
            values[key] = value;
        }
    }
    internal static bool IsEntryId(string id) => Regex.IsMatch(id, @"\A[A-Za-z0-9]{1,10}_[A-Za-z0-9]{1,64}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static string PublicReferer(NativeDownloadRequest request)
    {
        if(!string.IsNullOrWhiteSpace(request.Referer))return request.Referer;
        return new UriBuilder(request.Url){Query=string.Join('&',request.Url.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries)
            .Where(part=>Decode(part.Split('=',2)[0]) is not ("ks" or "flashvars[ks]")))}.Uri.AbsoluteUri;
    }
    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));
}
