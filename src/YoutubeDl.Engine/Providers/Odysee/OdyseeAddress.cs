using System.Text;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Odysee;

internal sealed record ClaimSelector(string Name, string? Prefix)
{
    internal string Rpc => Name + (Prefix is null ? "" : "#" + Prefix);
    internal string Web => Uri.EscapeDataString(Name).Replace("%40", "@", StringComparison.Ordinal) + (Prefix is null ? "" : ":" + Prefix);
}
internal sealed record OdyseeAddress(ClaimSelector? Channel, ClaimSelector? Stream, string? CollectionId)
{
    internal string Rpc => "lbry://" + (Channel is null ? "" : Channel.Rpc + (Stream is null ? "" : "/")) + (Stream?.Rpc ?? "");
    internal Uri Web => new("https://odysee.com/" + (CollectionId is not null ? "$/playlist/" + CollectionId :
        (Channel is null ? "" : Channel.Web + (Stream is null ? "" : "/")) + (Stream?.Web ?? "")));
    internal static bool IsHost(Uri url) => url.IsAbsoluteUri && (url.Scheme == "lbry" || url.Scheme is "http" or "https" && url.IsDefaultPort && url.UserInfo.Length == 0 &&
        url.Host is "odysee.com" or "www.odysee.com" or "lbry.tv" or "www.lbry.tv");
    internal static bool IsId(string? value, bool prefix = false) => value is not null && Regex.IsMatch(value, prefix ? @"\A[0-9a-fA-F]{1,40}\z" : @"\A[0-9a-fA-F]{40}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static OdyseeAddress Parse(Uri url)
    {
        if (!IsHost(url)) throw new ArgumentException("Use an Odysee/LBRY stream, channel or public collection address.");
        var native = url.Scheme == "lbry";
        string path;
        if (native)
        {
            if (!url.OriginalString.StartsWith("lbry://", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The LBRY URI is malformed.");
            path = url.OriginalString[7..];
            if (path.Contains('?')) throw new NotSupportedException("LBRY query-specific playback requires additional native support.");
        }
        else
        {
            if (url.Fragment.Length > 0) throw new NotSupportedException("Use colon claim selectors in Odysee web URLs.");
            foreach (var part in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var key = Uri.UnescapeDataString(part.Split('=', 2)[0]);
                if (key is not ("autoplay" or "loop" or "muted" or "embed_id" or "feature" or "ref" or "src") && !key.StartsWith("utm_", StringComparison.Ordinal))
                    throw new NotSupportedException("This Odysee query requires additional support: " + key);
            }
            path = url.AbsolutePath.Trim('/');
        }
        var segments = path.Trim('/').Split('/', StringSplitOptions.None);
        if (!native && segments.Length == 3 && segments[0] == "$" && segments[1] == "playlist")
            return IsId(segments[2]) ? new(null, null, segments[2].ToLowerInvariant()) : throw new NotSupportedException("Only public claim-backed Odysee collections are supported.");
        if (!native && segments.Length >= 3 && segments[0] == "$" && segments[1] == "embed") segments = segments[2..];
        if (!native && segments.Length == 4 && segments[0] == "$" && segments[1] is "download" or "stream")
        {
            if (!IsId(segments[3])) throw new ArgumentException("The Odysee stream claim ID is invalid.");
            segments = [segments[2] + ":" + segments[3]];
        }
        if (segments.Length is < 1 or > 2) throw new NotSupportedException("This Odysee address requires additional native support.");
        var first = Part(segments[0], native);
        if (first.Name.StartsWith('@')) return new(first, segments.Length == 2 ? Part(segments[1], native, channelAllowed: false) : null, null);
        if (segments.Length != 1) throw new ArgumentException("An Odysee stream path cannot contain another stream.");
        return new(null, first, null);
    }
    internal static ClaimSelector Part(string text, bool native, bool channelAllowed = true)
    {
        text = Uri.UnescapeDataString(text);
        var pieces = text.Split(native ? '#' : ':');
        if (pieces.Length is < 1 or > 2 || pieces.Length == 2 && !IsId(pieces[1], prefix: true)) throw new ArgumentException("The claim selector is invalid.");
        var name = pieces[0]; var channel = name.StartsWith('@'); var value = channel ? name[1..] : name;
        if (value.Length is < 1 or > 256 || channel && !channelAllowed || value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || "/\\#?:&%$@".Contains(character)))
            throw new ArgumentException("The claim name contains unsupported characters.");
        return new(name, pieces.Length == 2 ? pieces[1].ToLowerInvariant() : null);
    }
    internal static bool SameName(string first, string second) => string.Equals(first.Normalize(NormalizationForm.FormKC), second.Normalize(NormalizationForm.FormKC), StringComparison.OrdinalIgnoreCase);
    internal static Uri ClaimUrl(string name, string id) => new("https://odysee.com/" + new ClaimSelector(name, id).Web);
}
