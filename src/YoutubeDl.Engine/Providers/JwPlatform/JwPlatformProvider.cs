using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.JwPlatform;

public sealed record JwAddress(string Id, bool IsPlaylist, Uri Endpoint);
public sealed partial class JwPlatformProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("jwplatform", "JW public delivery media and playlists", 200);
    public NativeAuthenticationCapabilities AuthenticationCapabilities=>NativeAuthenticationCapabilities.Hosting;
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)
    {
        var address = ParseAddress(request.Url,request.Authentication.Hosting?.SigningKey.Length>0);
        return address.IsPlaylist ? null : new("jwplatform", address.Id);
    }
    public bool CanHandle(Uri url) => IsHost(url) && (url.AbsolutePath.StartsWith("/v2/media/", StringComparison.Ordinal) || url.AbsolutePath.StartsWith("/v2/playlists/", StringComparison.Ordinal) || url.AbsolutePath.StartsWith("/players/", StringComparison.Ordinal));
    public static JwAddress ParseAddress(Uri url,bool canResign=false)
    {
        if (!IsHost(url)) throw new ArgumentException("This is not a supported JW delivery URL.");
        var match = Regex.Match(url.AbsolutePath, @"\A/v2/(?<kind>media|playlists)/(?<id>[A-Za-z0-9]{8})/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (match.Success) return new(match.Groups["id"].Value, match.Groups["kind"].Value == "playlists", url);
        match = Regex.Match(url.AbsolutePath, @"\A/players/(?<id>[A-Za-z0-9]{8})-[A-Za-z0-9]{8}\.(?:html|js)\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) throw new NotSupportedException("This JW delivery path is not supported yet. Use a media, playlist, or player URL.");
        if (!string.IsNullOrEmpty(url.Query)&&!canResign) throw new NotSupportedException("Signed JW player-link resolution needs the matching authorized media endpoint or the publisher signing credential.");
        return new(match.Groups["id"].Value, false, new Uri("https://cdn.jwplayer.com/v2/media/" + match.Groups["id"].Value));
    }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var credentials=request.Authentication.Hosting;var address=ParseAddress(request.Url,credentials?.SigningKey.Length>0);
        async Task<PageMetadata> Read(MediaHttp controlled,CancellationToken token)
        {
            if(address.IsPlaylist)return await ReadPlaylistAsync(controlled,request,address,credentials,token).ConfigureAwait(false);
            var url=Authentication.HostingDeliverySigning.Jw(address.Endpoint,credentials);var document=await controlled.GetDocumentAsync(url,8*1024*1024,token,new Dictionary<string,string>{["Referer"]=string.IsNullOrWhiteSpace(request.Referer)?request.Url.AbsoluteUri:request.Referer}).ConfigureAwait(false);
            if(credentials is not null&&document.Url!=url)throw new InvalidDataException("JW delivery redirected outside the signed resource.");return ParseDelivery(document.Text,address,request.MaximumPlaylistEntries);
        }
        if(!address.IsPlaylist)return await http.SessionState("hosting:jw:"+address.Endpoint.AbsoluteUri,()=>new ProviderPlaybackSession(http,request,"jwplatform",Read,credentials is not null)).ReadAsync(ct).ConfigureAwait(false);
        using var transport=http.CreateDiscoverySession(credentials is not null);var page=await Read(transport,ct).ConfigureAwait(false);var entries=new List<NativePlaylistEntry>();
        foreach(var entry in page.Entries)
        {
            var occurrence=page.Entries.TakeWhile(e=>e.Position<entry.Position).Count(e=>e.Id==entry.Id);var occurrences=page.Entries.Count(e=>e.Id==entry.Id);var captured=entry.ResolvedMedia!;async Task<PageMetadata> Fresh(MediaHttp controlled,CancellationToken token)
            {var renewed=await Read(controlled,token).ConfigureAwait(false);var matches=renewed.Entries.Where(e=>e.Id==entry.Id).ToArray();return matches.Length==occurrences?matches[occurrence].ResolvedMedia!:throw new InvalidDataException("The JW playlist lost the selected media identity.");}
            var session=http.SessionState("hosting:jw-list:"+address.Endpoint.AbsoluteUri+":"+entry.Id+":"+entry.Position,()=>new ProviderPlaybackSession(http,request with {Url=entry.Url!},"jwplatform",Fresh,credentials is not null));
            entries.Add(entry with {ResolvedMedia=session.BindInitial(captured),ExpectedArchiveIdentity=new("jwplatform",entry.Id!)});
        }
        return page with {Entries=entries,SourcePageUrl=request.Url};
    }
    public static PageMetadata ParseDelivery(string json, JwAddress address, int maximumEntries = 1000,bool pageSlice=false)
    {
        if (Encoding.UTF8.GetByteCount(json) > 8 * 1024 * 1024) throw new InvalidDataException("The JW delivery response exceeds the extraction limit.");
        if (maximumEntries is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var items = Array(root, "playlist");
        if (items.Length == 0) throw new InvalidOperationException("JW did not return accessible media items.");
        if (items.Length > maximumEntries) throw new InvalidDataException("The JW playlist exceeds its configured entry limit; it was not truncated.");
        if (!address.IsPlaylist)
        {
            if (items.Length != 1 || Text(items[0], "mediaid") != address.Id) throw new InvalidDataException("The JW response belongs to different media.");
            return ReadMedia(items[0]);
        }
        if(Text(root,"feedid") is { } feed&&feed!=address.Id)throw new InvalidDataException("The JW response belongs to a different playlist.");
        if(!pageSlice&&(Text(Object(root,"links"),"next") is {Length:>0}||Integer(root,"count") is { } count&&count>items.Length))throw new NotSupportedException("This JW response is a page; complete inventory requires native collection discovery.");
        var entries = items.Select((item, position) =>
        {
            var media = ReadMedia(item);
            var url = new Uri("https://cdn.jwplayer.com/v2/media/" + media.Id);
            return new NativePlaylistEntry(position + 1, media.Id, media.Title, url, media.DurationSeconds, media.ThumbnailUrl, true, media);
        }).ToArray();
        return new() { Id = address.Id, PlaylistId = address.Id, IsPlaylist = true, Extractor = "jwplatform", Title = Text(root, "title") ?? address.Id,
            Description = Text(root, "description"), Entries = entries };
    }
    private static PageMetadata ReadMedia(JsonElement item)
    {
        var id = Text(item, "mediaid");
        if (id is null || !Regex.IsMatch(id, @"\A[A-Za-z0-9]{8}\z")) throw new InvalidDataException("The JW response has an invalid media identifier.");
        var sources = Array(item, "sources");
        if (sources.Length > 1000) throw new InvalidDataException("JW returned too many media sources.");
        var media = new List<MediaCandidate>();
        foreach (var source in sources)
        {
            if (HasProtection(source) || Text(source, "file") is not { } raw) continue;
            var url = Resource(raw);
            var mime = Text(source, "type");
            var kind = PageMediaExtractor.Classify(url, mime);
            if (kind == MediaKind.Unknown) continue;
            var audio = mime?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true;
            var extension = PageMediaExtractor.ExtensionFrom(url, mime);
            if (audio && extension == "mp4") extension = "m4a";
            media.Add(new()
            {
                Id = kind.ToString().ToLowerInvariant() + "-" + media.Count, Url = url, Kind = kind, Extension = extension, MimeType = mime, Source = "jwplatform",
                Height = Integer(source, "height"), Width = Integer(source, "width"), FileSize = Long(source, "filesize"), Bandwidth = Number(source, "bitrate"),
                FramesPerSecond = Number(source, "fps"), HasVideo = !audio, HasAudio = audio ? true : null, Label = Text(source, "label")
            });
        }
        var tracks = Array(item, "tracks");
        if (tracks.Length > 256) throw new InvalidDataException("JW returned too many caption tracks.");
        var subtitles = tracks.Where(track => Text(track, "kind") is "captions" or "subtitles" && Text(track, "file") is not null).Select(track =>
        {
            var url = Resource(Text(track, "file")!);
            return new SubtitleTrack { Url = url, Language = Text(track, "language") ?? Text(track, "srclang") ?? "und", Extension = Path.GetExtension(url.AbsolutePath).Equals(".srt", StringComparison.OrdinalIgnoreCase) ? "srt" : "vtt" };
        }).ToArray();
        return new()
        {
            Id = id, Extractor = "jwplatform", Title = Text(item, "title") ?? id, Description = Text(item, "description"), DurationSeconds = Number(item, "duration"),
            ThumbnailUrl = Text(item, "image") is { } image ? Resource(image).AbsoluteUri : null, Media = media, Subtitles = subtitles,
            Thumbnails = Array(item, "images").Where(image => Text(image, "src") is { Length: > 0 }).Select((image, index) => new ThumbnailCandidate
            { Id = "image-" + index, Url = Resource(Text(image, "src")!), Width = Integer(image, "width"), Height = Integer(image, "height") }).ToArray(),
            Warnings = media.Count == 0 ? ["JW did not expose native unprotected media sources."] : []
        };
    }
    private static bool HasProtection(JsonElement source) => new[] { "drm", "keySystems", "key_systems" }.Any(name => source.TryGetProperty(name, out var value) && value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Any(), JsonValueKind.Array => value.GetArrayLength() > 0,
        JsonValueKind.Null or JsonValueKind.False => false, JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()), _ => true
    });
    private static bool IsHost(Uri url) => url.IsAbsoluteUri && url.Scheme is "http" or "https" && url.IsDefaultPort && string.IsNullOrEmpty(url.UserInfo) && url.Host is "cdn.jwplayer.com" or "content.jwplatform.com" or "cdn.jwplatform.com";
    private static Uri Resource(string value) => Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" && string.IsNullOrEmpty(url.UserInfo)
        ? url : throw new InvalidDataException("JW returned an invalid media URL.");
}
