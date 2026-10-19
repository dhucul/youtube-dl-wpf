using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.PeerTube;

public sealed record PeerTubeAddress(Uri Instance, string Identifier, bool IsPlaylist)
{
    public static bool CanHandle(Uri url) => TryParse(url, out _);
    public static PeerTubeAddress Parse(Uri url) => TryParse(url, out var address) ? address! : throw new ArgumentException("The address is not a supported PeerTube video or playlist URL.");
    public static bool IsIdentifier(string value) => Guid.TryParseExact(value, "D", out var uuid) && uuid != Guid.Empty ||
        Regex.IsMatch(value, @"\A[1-9A-HJ-NP-Za-km-z]{22}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) ||
        Regex.IsMatch(value, @"\A[1-9][0-9]{0,14}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static bool TryParse(Uri url, out PeerTubeAddress? address)
    {
        address = null;
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(url.UserInfo)) return false;
        var path = url.AbsolutePath.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var playlist = false;
        string? id = null;
        if (path.Length == 2 && path[0] == "w") id = path[1];
        else if (path.Length == 3 && path[0] == "videos" && path[1] is "watch" or "embed") id = path[2];
        else if (path.Length == 3 && path[0] == "w" && path[1] == "p") { id = path[2]; playlist = true; }
        else if (path.Length == 3 && path[0] == "video-playlists" && path[1] is "watch" or "embed") { id = path[2]; playlist = true; }
        if (id is null || !IsIdentifier(id)) return false;
        address = new(new Uri(url.GetLeftPart(UriPartial.Authority) + "/"), id, playlist);
        return true;
    }
}
