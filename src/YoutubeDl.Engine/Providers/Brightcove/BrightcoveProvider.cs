using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Brightcove;

public sealed record BrightcoveAddress(string Account, string PlayerEmbed, string MediaId, bool IsPlaylist)
{
    public Uri Configuration => new("https://players.brightcove.net/" + Account + "/" + PlayerEmbed + "/config.json");
    public Uri Playback => new("https://edge.api.brightcove.com/playback/v1/accounts/" + Account + "/" + (IsPlaylist ? "playlists/" : "videos/") + Uri.EscapeDataString(MediaId));
    public static bool IsHost(Uri url) => url.IsAbsoluteUri && url.Scheme is "http" or "https" && url.IsDefaultPort && string.IsNullOrEmpty(url.UserInfo) && url.Host == "players.brightcove.net";
    public static BrightcoveAddress Parse(Uri url)
    {
        if (!IsHost(url)) throw new ArgumentException("This is not a supported Brightcove player URL.");
        var match = Regex.Match(url.AbsolutePath, @"\A/(?<account>[0-9]{1,20})/(?<player>[A-Za-z0-9_-]{1,100})/index\.html\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) throw new NotSupportedException("Native Brightcove legacy/player-script resolution remains pending. Use an iframe player URL with a media identifier.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = part.Split('=', 2); var key = Uri.UnescapeDataString(fields[0]);
            if (key is not ("videoId" or "playlistId")) continue;
            if (!values.TryAdd(key, Uri.UnescapeDataString(fields.Length == 2 ? fields[1] : ""))) throw new ArgumentException("The Brightcove URL contains duplicate media identifiers.");
        }
        var playlist = !values.ContainsKey("videoId") && values.ContainsKey("playlistId");
        var id = values.GetValueOrDefault(playlist ? "playlistId" : "videoId");
        if (id is null || !Regex.IsMatch(id, @"\A(?:[0-9]{1,20}|ref:[A-Za-z0-9_. -]{1,200})\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new ArgumentException("The Brightcove media identifier is missing or invalid.");
        return new(match.Groups["account"].Value, match.Groups["player"].Value, id, playlist);
    }
}
public sealed partial class BrightcoveProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("brightcove", "Brightcove public player playback", 200);
    public NativeAuthenticationCapabilities AuthenticationCapabilities=>NativeAuthenticationCapabilities.Hosting;
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)
    {
        var address = BrightcoveAddress.Parse(request.Url);
        return address.IsPlaylist || address.MediaId.StartsWith("ref:", StringComparison.Ordinal) ? null : new("brightcovenew", address.MediaId);
    }
    public bool CanHandle(Uri url) => BrightcoveAddress.IsHost(url);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = BrightcoveAddress.Parse(request.Url);
        async Task<PageMetadata> Read(MediaHttp controlled,CancellationToken token)
        {
        using var config = Parse(await controlled.GetStringAsync(address.Configuration, 2 * 1024 * 1024, token).ConfigureAwait(false));
        var configuredAccount = Text(config.RootElement, "account_id") ?? Long(config.RootElement, "account_id")?.ToString(CultureInfo.InvariantCulture);
        if (configuredAccount is not null && configuredAccount != address.Account) throw new InvalidDataException("The Brightcove player belongs to a different account.");
        var policy = Text(Object(config.RootElement, "video_cloud"), "policy_key");
        if (string.IsNullOrWhiteSpace(policy) || policy.Length > 8192 || policy.Any(char.IsControl) || policy.Contains(';'))
            throw new NotSupportedException("The Brightcove player did not expose a valid policy for native playback.");
        var headers = new Dictionary<string, string> { ["Accept"] = "application/json;pk=" + policy };
        var referer=string.IsNullOrWhiteSpace(request.Referer)?request.Url.AbsoluteUri:request.Referer;headers["Referer"]=referer;headers["Origin"]=new Uri(referer).GetLeftPart(UriPartial.Authority);
        if(request.Authentication.Hosting is { } credentials)headers["Authorization"]="Bearer "+credentials.AccessToken;
        if(address.IsPlaylist)return await ReadPlaylistAsync(controlled,request,address,headers,token).ConfigureAwait(false);
        var document=await controlled.GetDocumentAsync(address.Playback,8*1024*1024,token,headers).ConfigureAwait(false);if(request.Authentication.Hosting is not null&&document.Url!=address.Playback)throw new InvalidDataException("Brightcove changed its scoped playback API resource.");
        return ParsePlayback(document.Text, address, request.MaximumPlaylistEntries);
        }
        if(!address.IsPlaylist)return await http.SessionState("hosting:brightcove:"+address.Account+":"+address.MediaId+":"+address.PlayerEmbed,()=>new ProviderPlaybackSession(http,request,"brightcove",Read,request.Authentication.Hosting is not null)).ReadAsync(ct).ConfigureAwait(false);
        using var transport=http.CreateDiscoverySession(request.Authentication.Hosting is not null);var page=await Read(transport,ct).ConfigureAwait(false);var entries=new List<NativePlaylistEntry>();
        foreach(var entry in page.Entries)
        {
            var occurrence=page.Entries.TakeWhile(e=>e.Position<entry.Position).Count(e=>e.Id==entry.Id);var occurrences=page.Entries.Count(e=>e.Id==entry.Id);
            async Task<PageMetadata> Fresh(MediaHttp controlled,CancellationToken token){var renewed=await Read(controlled,token).ConfigureAwait(false);var found=renewed.Entries.Where(e=>e.Id==entry.Id).ToArray();return found.Length==occurrences?found[occurrence].ResolvedMedia!:throw new InvalidDataException("The Brightcove playlist lost the selected media identity.");}
            var session=http.SessionState("hosting:bc-list:"+address.Account+":"+address.MediaId+":"+entry.Id+":"+entry.Position,()=>new ProviderPlaybackSession(http,request with {Url=entry.Url!},"brightcove",Fresh,request.Authentication.Hosting is not null));
            entries.Add(entry with {ResolvedMedia=session.BindInitial(entry.ResolvedMedia!),ExpectedArchiveIdentity=new("brightcovenew",entry.Id!)});
        }
        return page with {Entries=entries,SourcePageUrl=request.Url};
    }
    public static PageMetadata ParsePlayback(string json, BrightcoveAddress address, int maximumEntries = 1000,bool pageSlice=false)
    {
        using var document = Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array) throw new InvalidOperationException("Brightcove did not authorize this playback request.");
        ValidateIdentity(root, address.Account, address.MediaId);
        if (!address.IsPlaylist) return ReadVideo(root);
        var videos = Array(root, "videos");
        if (maximumEntries is < 1 or > 10000 || videos.Length > maximumEntries) throw new InvalidDataException("The Brightcove playlist exceeds its configured item limit.");
        if (!pageSlice&&(Text(root, "next_link") is not null || Integer(root, "count") is { } total && total > videos.Length))
            throw new NotSupportedException("Native Brightcove paged playlists remain pending; the response was not truncated silently.");
        var entries = videos.Select((video, index) =>
        {
            var id = Text(video, "id") ?? Long(video, "id")?.ToString(CultureInfo.InvariantCulture) ?? throw new InvalidDataException("A Brightcove playlist video has no identity.");
            ValidateIdentity(video, address.Account, id);
            var page = ReadVideo(video);
            var uri = new Uri("https://players.brightcove.net/" + address.Account + "/" + address.PlayerEmbed + "/index.html?videoId=" + Uri.EscapeDataString(id));
            return new NativePlaylistEntry(index + 1, id, page.Title, uri, page.DurationSeconds, page.ThumbnailUrl, true, page);
        }).ToArray();
        return new() { Id = address.MediaId, PlaylistId = address.MediaId, IsPlaylist = true, Extractor = "brightcove", Title = Text(root, "name") ?? address.MediaId, Description = Text(root, "description"), Entries = entries };
    }
    private static void ValidateIdentity(JsonElement root, string account, string id)
    {
        if (Text(root, "account_id") != account && Long(root, "account_id")?.ToString(CultureInfo.InvariantCulture) != account)
            throw new InvalidDataException("The Brightcove response belongs to a different account.");
        var returned = Text(root, "id") ?? Long(root, "id")?.ToString(CultureInfo.InvariantCulture);
        if (id.StartsWith("ref:", StringComparison.Ordinal) ? Text(root, "reference_id") != id[4..] : returned != id)
            throw new InvalidDataException("The Brightcove response belongs to different media.");
    }
    private static PageMetadata ReadVideo(JsonElement root)
    {
        var sources = Array(root, "sources");
        if (sources.Length > 1000) throw new InvalidDataException("Brightcove returned too many sources.");
        var media = new List<MediaCandidate>();
        var urls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources.OrderByDescending(source => Text(source, "src")?.StartsWith("https:", StringComparison.OrdinalIgnoreCase) == true))
        {
            if (Protected(source) || Text(source, "src") is not { } raw) continue;
            var url = Resource(raw);
            if (!urls.Add(url.AbsoluteUri)) continue;
            var mime = Text(source, "type") ?? (Text(source, "container")?.Equals("MP4", StringComparison.OrdinalIgnoreCase) == true ? "video/mp4" : null);
            var kind = PageMediaExtractor.Classify(url, mime);
            if (kind == MediaKind.Unknown) continue;
            var audio = mime?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true;
            media.Add(new()
            {
                Id = kind.ToString().ToLowerInvariant() + "-" + media.Count, Url = url, Kind = kind, Source = "brightcove", Extension = PageMediaExtractor.ExtensionFrom(url, mime), MimeType = mime,
                Height = Integer(source, "height"), Width = Integer(source, "width"), FileSize = Long(source, "size"), Bandwidth = Number(source, "avg_bitrate") ?? Number(source, "encoding_rate"),
                VideoCodec = audio ? null : Text(source, "codec"), AudioCodec = audio ? Text(source, "codec") : null, FramesPerSecond = Number(source, "frame_rate"), HasVideo = !audio, HasAudio = audio ? true : null
            });
        }
        var tracks = Array(root, "text_tracks");
        if (tracks.Length > 256) throw new InvalidDataException("Brightcove returned too many text tracks.");
        var captions = tracks.Where(track => Text(track, "kind") is "captions" or "subtitles" && Text(track, "src") is not null).Select(track => new SubtitleTrack
        { Url = Resource(Text(track, "src")!), Language = Text(track, "srclang") ?? "und", Extension = "vtt" }).ToArray();
        var id = Text(root, "id") ?? Long(root, "id")?.ToString(CultureInfo.InvariantCulture);
        return new() { Id = id, Extractor = "brightcove", Title = Text(root, "name") ?? id ?? "Brightcove video", Description = Text(root, "long_description") ?? Text(root, "description"),
            DurationSeconds = Number(root, "duration") / 1000, ThumbnailUrl = Text(root, "poster") is { } poster ? Resource(poster).AbsoluteUri : null, Media = media, Subtitles = captions,
            Thumbnails = new[] { "poster", "thumbnail" }.Where(name => Text(root, name) is { Length: > 0 })
                .Select(name => new ThumbnailCandidate { Id = name, Url = Resource(Text(root, name)!) }).ToArray(),
            Warnings = media.Count == 0 ? ["Brightcove did not expose native unprotected sources for this request."] : [] };
    }
    private static bool Protected(JsonElement source) => source.TryGetProperty("key_systems", out var keys) && keys.ValueKind == JsonValueKind.Object && keys.EnumerateObject().Any();
    private static Uri Resource(string value) => Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" && string.IsNullOrEmpty(url.UserInfo)
        ? url : throw new InvalidDataException("Brightcove returned an invalid source URL.");
    private static JsonDocument Parse(string json) => Encoding.UTF8.GetByteCount(json) <= 8 * 1024 * 1024 ? JsonDocument.Parse(json) : throw new InvalidDataException("The Brightcove response exceeds the extraction limit.");
}
