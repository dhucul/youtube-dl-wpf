using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Wistia;

public sealed partial class WistiaProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("wistia", "Wistia public embedded media", 200);
    public NativeAuthenticationCapabilities AuthenticationCapabilities=>NativeAuthenticationCapabilities.Hosting;
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => IsChannel(request.Url)||IsLegacyPlaylist(request.Url)?null:new("wistia", ParseIdentifier(request.Url));
    public bool CanHandle(Uri url) => IsHost(url) && (IsChannel(url)||IsLegacyPlaylist(url)||url.AbsolutePath.StartsWith("/medias/", StringComparison.Ordinal) || url.AbsolutePath.StartsWith("/m/", StringComparison.Ordinal) || url.AbsolutePath.StartsWith("/embed/iframe/", StringComparison.Ordinal) || url.AbsolutePath.StartsWith("/embed/medias/", StringComparison.Ordinal));
    public static string ParseIdentifier(Uri url)
    {
        if (!IsHost(url)) throw new ArgumentException("This is not a supported Wistia URL.");
        var match = Regex.Match(url.AbsolutePath, @"\A/(?:medias/|m/|embed/iframe/|embed/medias/)(?<id>[a-z0-9]{10})(?:(?:\.jsonp?|\.m3u8)|/manage)?/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return match.Success ? match.Groups["id"].Value : throw new NotSupportedException("Native Wistia share-slug/channel/playlist resolution remains pending. Use a media or iframe URL.");
    }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        if(IsChannel(request.Url))return await ReadChannelAsync(request,http,ct).ConfigureAwait(false);
        if(IsLegacyPlaylist(request.Url))return await ReadLegacyPlaylistAsync(request,http,ct).ConfigureAwait(false);
        var id = ParseIdentifier(request.Url);
        async Task<PageMetadata> Read(MediaHttp controlled,CancellationToken token)
        {
            var credentials=request.Authentication.Hosting;var url=new Uri(credentials is null?"https://fast.wistia.com/embed/medias/"+id+".json":"https://api.wistia.com/v2/medias/"+id+".json");
            var document=await controlled.GetDocumentAsync(url,4*1024*1024,token,credentials is null?null:new Dictionary<string,string>{["Authorization"]="Bearer "+credentials.AccessToken}).ConfigureAwait(false);
            if(document.Url!=url)throw new InvalidDataException("Wistia changed its selected media resource.");
            var page=credentials is null?ParseMedia(document.Text,id):ParseApi(document.Text,id);
            return await ResolveApiCaptionsAsync(page,id,request,credentials,controlled,token).ConfigureAwait(false);
        }
        return await http.SessionState("hosting:wistia:"+id,()=>new ProviderPlaybackSession(http,request,"wistia",Read,request.Authentication.Hosting is not null)).ReadAsync(ct).ConfigureAwait(false);
    }
    internal static PageMetadata ParseApi(string json,string expectedId)
    {
        if(Encoding.UTF8.GetByteCount(json)>4*1024*1024)throw new InvalidDataException("The Wistia API response exceeds its limit.");
        using var document=JsonDocument.Parse(json);var root=document.RootElement;var attributes=Object(Object(root,"data"),"attributes");if(attributes.ValueKind==JsonValueKind.Object)root=attributes;
        if(Text(root,"hashed_id")!=expectedId&&Text(root,"hashedId")!=expectedId)throw new InvalidDataException("Wistia API returned another media identity.");
        if(Text(root,"status") is { } status&&status!="ready")throw new NotSupportedException("The Wistia API media is not ready for complete playback.");
        var assets=new List<object>();foreach(var asset in Array(root,"assets"))
        {
            var raw=Text(asset,"url");if(raw is null)continue;var mime=Text(asset,"contentType")??Text(asset,"content_type");var url=Resource(raw);var extension=Text(asset,"container")??PageMediaExtractor.ExtensionFrom(url,mime);
            assets.Add(new{type=Text(asset,"type")??"api",url=raw,container=extension,status=2,height=Integer(asset,"height"),width=Integer(asset,"width"),size=Long(asset,"fileSize")??Long(asset,"size"),bitrate=Number(asset,"bitrate"),frameRate=Number(asset,"frameRate"),codec=Text(asset,"codec")});
        }
        return ParseMedia(JsonSerializer.Serialize(new{media=new{hashedId=expectedId,name=Text(root,"name"),duration=Number(root,"duration"),mediaType=Text(root,"type"),seoDescription=Text(root,"description"),assets,captions=Array(root,"captions"),availableTranscripts=Array(root,"availableTranscripts")}}),expectedId);
    }
    public static PageMetadata ParseMedia(string json, string expectedId)
    {
        if (Encoding.UTF8.GetByteCount(json) > 4 * 1024 * 1024) throw new InvalidDataException("The Wistia response exceeds the extraction limit.");
        using var document = JsonDocument.Parse(json);
        var root = Object(document.RootElement, "media");
        if (Text(root, "hashedId") != expectedId) throw new InvalidDataException("The Wistia response belongs to a different media item.");
        if(True(root,"protected"))throw new NotSupportedException("This public Wistia media requires another access contract.");
        var assets = Array(root, "assets");
        if (assets.Length > 1000) throw new InvalidDataException("Wistia returned too many assets.");
        var media = new List<MediaCandidate>();
        string? thumbnail = null;
        var thumbnails = new List<ThumbnailCandidate>();
        foreach (var asset in assets)
        {
            if (Integer(asset, "status") is { } status && status != 2) continue;
            if (Text(asset, "url") is not { } raw) continue;
            var url = Resource(raw);
            var type = Text(asset, "type") ?? "";
            if (type == "still_image")
            {
                thumbnail ??= url.AbsoluteUri;
                thumbnails.Add(new() { Url = url, Id = "still-" + thumbnails.Count.ToString(CultureInfo.InvariantCulture), Width = Integer(asset, "width"), Height = Integer(asset, "height") });
                continue;
            }
            var container = Text(asset, "container");
            if(container is "m3u8" or "mpd")
            {
                media.Add(new(){Id=type+"-"+media.Count,Url=url,Kind=container=="m3u8"?MediaKind.Hls:MediaKind.Dash,Extension=container,Source="wistia",HasVideo=Text(root,"mediaType")!="Audio",HasAudio=Text(root,"mediaType")=="Audio"?true:null});continue;
            }
            if (type == "original" && container is null) continue; // A .bin original does not establish its container.
            if (container is not ("mp4" or "webm" or "m4a" or "mp3" or "ogg" or "flac" or "wav")) continue;
            var audio = Text(root, "mediaType") == "Audio" || type.Contains("audio", StringComparison.OrdinalIgnoreCase) || container is "m4a" or "mp3" or "ogg" or "flac" or "wav";
            media.Add(new()
            {
                Id = type + "-" + media.Count, Url = url, Extension = container, Kind = MediaKind.Progressive, Source = "wistia", MimeType = (audio ? "audio/" : "video/") + container,
                Height = audio ? null : Integer(asset, "height"), Width = Integer(asset, "width"), FileSize = Long(asset, "size"), Bandwidth = Number(asset, "bitrate") * 1000,
                VideoCodec = audio ? null : Text(asset, "codec"), AudioCodec = audio ? Text(asset, "codec") : null, FramesPerSecond = Number(asset, "frameRate"),
                HasVideo = !audio, HasAudio = audio ? true : null, Label = Text(asset, "display_name")
            });
        }
        return new()
        {
            Id = expectedId, Extractor = "wistia", Title = Text(root, "name") ?? expectedId, Description = Text(root, "seoDescription"),
            DurationSeconds = Number(root, "duration"), ThumbnailUrl = thumbnail, Thumbnails = thumbnails, Media = media,Subtitles=ReadCaptions(root,expectedId),
            Warnings = media.Count == 0 ? ["Wistia did not expose native downloadable assets. The item may require additional access or media support."] : []
        };
    }
    private static bool IsHost(Uri url) => url.IsAbsoluteUri && url.Scheme is "http" or "https" && string.IsNullOrEmpty(url.UserInfo) && url.IsDefaultPort &&
        (url.Host is "wistia.com" or "wistia.net" or "wi.st" || url.Host.EndsWith(".wistia.com", StringComparison.OrdinalIgnoreCase) || url.Host.EndsWith(".wistia.net", StringComparison.OrdinalIgnoreCase));
    private static Uri Resource(string value) => Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" && string.IsNullOrEmpty(url.UserInfo)
        ? url : throw new InvalidDataException("Wistia returned an invalid asset URL.");
}
