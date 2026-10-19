using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Music;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Whyp;

/// <summary>Public Whyp tracks, using only the issuer's current clear audio grants.</summary>
public sealed class WhypProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("whyp", "Whyp public audio tracks", 200);
    public bool CanHandle(Uri url) => url.IsAbsoluteUri && url.Host is "whyp.it" or "www.whyp.it";
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => long.TryParse(Parse(request.Url), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? new("whyp", id.ToString(CultureInfo.InvariantCulture)) : null;

    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var identity = Parse(request.Url);
        async Task<PageMetadata> Read(MediaHttp controlled, CancellationToken token)
        {
            using var anonymous = controlled.CreateAnonymousSession();
            var root = await MusicCatalog.ReadAsync(anonymous, new("https://api.whyp.it/api/tracks/" + Uri.EscapeDataString(identity)), token).ConfigureAwait(false);
            var track = Object(root, "track");
            var numeric = Long(track, "id"); var publicId = Text(track, "public_id");
            if (numeric is not > 0 || publicId is null || !Regex.IsMatch(publicId, @"\A[A-Za-z0-9]{12}\z") ||
                (long.TryParse(identity, NumberStyles.None, CultureInfo.InvariantCulture, out var requested) ? requested != numeric : identity != publicId))
                throw new InvalidDataException("Whyp returned another track identity.");
            var canonical = Text(track, "permalink") ?? Text(track, "url");
            if (canonical is null || !canonical.StartsWith("/tracks/", StringComparison.Ordinal) || Parse(new Uri("https://whyp.it" + canonical)) != publicId || Text(track, "api_path") != "/tracks/" + publicId)
                throw new InvalidDataException("Whyp changed its canonical track or API identity.");
            var duration = Number(track, "duration"); var title = Text(track, "title"); var version = Text(track, "audio_changed_at");
            if (duration is not (> 0 and <= 604800) || string.IsNullOrWhiteSpace(title) || version is null || !DateTimeOffset.TryParse(version, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new InvalidDataException("Whyp omitted complete track metadata or its audio version.");
            var page = new PageMetadata { Id = numeric.Value.ToString(CultureInfo.InvariantCulture), Extractor = "whyp", ArchiveIdentity = new("whyp", numeric.Value.ToString(CultureInfo.InvariantCulture)),
                Title = title, Description = Text(track, "description"), Uploader = Text(Object(track, "user"), "username"), DurationSeconds = duration, AnonymousPlayback = true,
                ThumbnailUrl = Image(Text(track, "artwork_url") ?? Text(track, "artwork_url_fallback")), UploadTimestamp = DateTimeOffset.TryParse(Text(track, "created_at"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var created) ? created : null };
            if (!True(track, "public") || True(track, "via_link") || True(track, "one_person_link") || !True(track, "allow_downloads"))
                return page with { Warnings = ["Whyp has not enabled public downloads for this track."] };
            if (await MusicCatalog.RecordedAsync(request, page, token).ConfigureAwait(false)) return page;
            var formats = new List<MediaCandidate>();
            foreach (var (kind, extension) in new[] { ("lossy", "mp3"), ("lossless", "flac") })
            {
                if (Text(track, kind + "_url") is not { Length: > 0 } raw) continue;
                var url = Resource(raw);
                if (!url.AbsolutePath.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("This Whyp audio encoding is not supported yet.");
                var size = Long(track, kind + "_size"); if (size is not > 0) throw new InvalidDataException("Whyp omitted its complete audio size.");
                var stamp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(version)))[..16];
                formats.Add(MusicCatalog.Audio(url, kind + "-" + stamp, extension, duration.Value, "whyp", size));
            }
            return page with { Media = formats, Warnings = formats.Count == 0 ? ["Whyp did not offer complete clear audio for this public session."] : [] };
        }
        return await http.SessionState("playback:whyp:" + identity, () => new ProviderPlaybackSession(http, request, "whyp", Read, reorderedFormats: false)).ReadAsync(ct).ConfigureAwait(false);
    }
    internal static string Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0 || url.Query.Length != 0 || url.Host is not ("whyp.it" or "www.whyp.it"))
            throw new ArgumentException("Use a public Whyp track URL without private-link parameters.");
        var match = Regex.Match(url.AbsolutePath, @"\A/tracks/(?:(?<numeric>[1-9][0-9]{0,15})(?:/[A-Za-z0-9_-]{1,256})?|(?:[A-Za-z0-9_-]{1,256}-)?(?<public>[A-Za-z0-9]{12}))/?\z", RegexOptions.CultureInvariant);
        if (!match.Success) throw new NotSupportedException("This Whyp route requires additional native support.");
        return match.Groups["numeric"].Success ? match.Groups["numeric"].Value : match.Groups["public"].Value;
    }
    private static string? Image(string? raw) => raw is null ? null : Resource(raw).AbsoluteUri;
    private static Uri Resource(string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.Scheme != "https" || url.Host != "cdn.whyp.it" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0)
            throw new InvalidDataException("Whyp returned a foreign or invalid audio resource.");
        return url;
    }
}
