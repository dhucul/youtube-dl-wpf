namespace YoutubeDl.Engine.Providers.Vocaroo;

internal sealed record VocarooAddress(string Id, bool Embed = false, bool Direct = false)
{
    internal static bool IsHost(string host) => host == "voca.ro" || host.EndsWith(".voca.ro",StringComparison.OrdinalIgnoreCase) || host == "vocaroo.com" || host.EndsWith(".vocaroo.com",StringComparison.OrdinalIgnoreCase);
    internal static bool Matches(Uri url) { try { Parse(url); return true; } catch (ArgumentException) { return false; } }
    internal static VocarooAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0)
            throw new ArgumentException("Use a public Vocaroo recording, player or MP3 address.");
        var parts = url.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
        var direct = url.Host is "media.vocaroo.com" or "media1.vocaroo.com";
        if (direct)
        {
            if (parts.Length != 2 || parts[0] != "mp3" || !ValidId(parts[1]) || url.Query.Length != 0)
                throw new ArgumentException("Use a supported public Vocaroo MP3 address.");
            var address = new VocarooAddress(parts[1],Direct:true);
            if (address.Media.Host != url.Host) throw new ArgumentException("The Vocaroo address uses another recording gateway."); return address;
        }
        if (url.Host is not ("vocaroo.com" or "www.vocaroo.com" or "voca.ro")) throw new ArgumentException("Use a public Vocaroo host.");
        var embed = parts.Length == 2 && parts[0] == "embed";
        if (parts.Length is not (1 or 2) || parts.Length == 2 && !embed && parts[0] != "i" || !ValidId(parts[^1]) || url.Host == "voca.ro" && parts.Length != 1)
            throw new ArgumentException("This Vocaroo route needs additional native support.");
        var query = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in url.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries))
        {
            var value = item.Split('=',2); var name = Uri.UnescapeDataString(value[0]); var text = value.Length == 2 ? Uri.UnescapeDataString(value[1]) : "";
            if (!query.Add(name) || !(name.StartsWith("utm_",StringComparison.Ordinal) || embed && name == "autoplay" && text is "0" or "1"))
                throw new ArgumentException("This Vocaroo access or player selector needs additional native support.");
        }
        return new(parts[^1],embed);
    }
    private static bool ValidId(string id) => (id.Length is 10 or 11 || id.Length == 12 && id[0] == '1') && id.All(char.IsAsciiLetterOrDigit);
    internal Uri Canonical => new("https://vocaroo.com/"+Id);
    internal Uri Page => new("https://vocaroo.com/"+(Embed?"embed/":"")+Id);
    internal Uri Media => new("https://"+(Id.Length == 11 ? "media" : "media1")+".vocaroo.com/mp3/"+Id);
}
