using System.Globalization;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Rutube;

internal sealed record RutubeAddress(string Kind, string Id)
{
    internal bool Collection => Kind != "video";
    internal Uri Page => new("https://rutube.ru/" + (Kind switch
    {
        "video" => "video/" + Id, "person" => "video/person/" + Id, "channel" => "channel/" + Id,
        "movie" => "metainfo/tv/" + Id, "tags" => "tags/video/" + Id, _ => "plst/" + Id
    }) + "/");
    internal string Connection => Kind switch
    {
        "person" or "channel" => "/api/video/person/" + Id + "/",
        "movie" => "/api/metainfo/tv/" + Id + "/video/",
        "tags" => "/api/tags/video/" + Id + "/",
        "playlist" => "/api/playlist/custom/" + Id + "/videos/",
        _ => throw new InvalidOperationException("This Rutube source is not a collection.")
    };
    internal static bool Host(Uri uri) => uri.IsAbsoluteUri && uri.Host is "rutube.ru" or "www.rutube.ru";
    internal static RutubeAddress Parse(Uri uri)
    {
        if (!Host(uri) || uri.Scheme is not ("http" or "https") || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Use a Rutube video, embed, channel, person, show, tag or playlist URL.");
        var query = SoundCloud.SoundCloudAddress.Query(uri);
        if (query.Keys.Any(key => key is "p" or "private" or "token" or "plst_id")) throw new NotSupportedException("Rutube private links and track-linked playlist context require another native contract.");
        var path = uri.AbsolutePath;
        var video = Regex.Match(path, @"\A/(?:video/|play/embed/|embed/)(?<id>[0-9a-fA-F]{32})/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (video.Success) return new("video", video.Groups["id"].Value.ToLowerInvariant());
        foreach (var (prefix, kind) in new[] { ("video/person", "person"), ("channel", "channel"), ("metainfo/tv", "movie"), ("tags/video", "tags"), ("plst", "playlist") })
        {
            var match = Regex.Match(path, @"\A/" + prefix + @"/(?<id>[1-9][0-9]{0,17})/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (match.Success && long.TryParse(match.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out _)) return new(kind, match.Groups["id"].Value);
        }
        throw new NotSupportedException("This Rutube URL shape is not implemented by the native provider.");
    }
}
