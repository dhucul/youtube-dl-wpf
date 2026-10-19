using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Kaltura;

public sealed class KalturaProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("kaltura", "Kaltura public player media", 200);
    public NativeAuthenticationCapabilities AuthenticationCapabilities=>NativeAuthenticationCapabilities.Hosting|NativeAuthenticationCapabilities.UserPassword|NativeAuthenticationCapabilities.TwoFactor;
    public bool CanHandle(Uri url) => KalturaAddress.IsPlayer(url);
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => new("kaltura", KalturaAddress.Parse(request.Url).EntryId);

    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = KalturaAddress.Parse(request.Url);
        if(address.Session is not null&&request.Authentication.Hosting is { } supplied&&address.Session!=supplied.AccessToken)throw new ArgumentException("The Kaltura URL and scoped session disagree.");
        var account=new KalturaAccountSession(request,address);
        async Task<PageMetadata> Fresh(MediaHttp controlled,CancellationToken token)
        {
            for(var attempt=0;;attempt++)
            {
                var ks=await account.GetAsync(controlled,token).ConfigureAwait(false);
                try{return await ReadEntryAsync(request,address,ks,controlled,token).ConfigureAwait(false);}
                catch(KalturaSessionExpiredException)when(attempt==0){account.Invalidate();}
                catch(HttpRequestException e)when(attempt==0&&e.StatusCode==System.Net.HttpStatusCode.Unauthorized){account.Invalidate();}
            }
        }
        return await http.SessionState("hosting:kaltura:"+address.PartnerId+":"+address.EntryId,()=>new ProviderPlaybackSession(http,request,"kaltura",Fresh,request.Authentication.IsConfigured||address.Session is not null,reorderedFormats:false)).ReadAsync(ct).ConfigureAwait(false);
    }
    private static async Task<PageMetadata> ReadEntryAsync(NativeDownloadRequest request,KalturaAddress address,string session,MediaHttp http,CancellationToken ct)
    {
        var referer = KalturaAddress.PublicReferer(request);
        var mediaJson = await Call("media", "get", new() { ["ks"] = session, ["entryId"] = address.EntryId }, http, ct).ConfigureAwait(false);
        using (var media = Read(mediaJson)) ValidateMedia(media.RootElement, address);
        var contextJson = await Call("baseentry", "getPlaybackContext", new()
        {
            ["ks"] = session, ["entryId"] = address.EntryId,
            ["contextDataParams"] = new { objectType = "KalturaPlaybackContextOptions", referrer = referer, mediaProtocol = "https" }
        }, http, ct).ConfigureAwait(false);
        using var context = Read(contextJson);
        var flavors = DownloadableFlavors(context.RootElement, address);
        var urls = new Dictionary<string, string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        foreach (var flavor in flavors)
        {
            ct.ThrowIfCancellationRequested();
            var id = Text(flavor, "id")!;
            try
            {
                using var response = Read(await Call("flavorasset", "getUrl", new()
                {
                    ["ks"] = session, ["id"] = id,
                    ["options"] = new { objectType = "KalturaFlavorAssetUrlOptions", referrer = referer }
                }, http, ct).ConfigureAwait(false));
                if (response.RootElement.ValueKind != JsonValueKind.String) throw new InvalidDataException("Kaltura returned an invalid flavor address.");
                urls.Add(id, Resource(response.RootElement.GetString()!).AbsoluteUri);
            }
            catch (InvalidOperationException e)when(e is not KalturaSessionExpiredException) { warnings.Add("Kaltura did not authorize the direct flavor " + id + "."); }
        }
        var page = ParsePlayback(mediaJson, contextJson, address, urls);
        return page with { Warnings = page.Warnings.Concat(warnings).ToArray() };
    }

    public static PageMetadata ParsePlayback(string mediaJson, string contextJson, KalturaAddress address, IReadOnlyDictionary<string, string> flavorUrls)
    {
        using var mediaDocument = Read(mediaJson); using var contextDocument = Read(contextJson);
        var entry = mediaDocument.RootElement; var context = contextDocument.RootElement;
        ValidateMedia(entry, address);
        var flavors = DownloadableFlavors(context, address);
        if (flavorUrls.Keys.Any(id => !flavors.Any(flavor => Text(flavor, "id") == id))) throw new InvalidDataException("Kaltura flavor URLs include an unoffered format.");
        var media = new List<MediaCandidate>();
        foreach (var flavor in flavors)
        {
            var id = Text(flavor, "id")!;
            if (!flavorUrls.TryGetValue(id, out var raw)) continue;
            var extension = Text(flavor, "fileExt")!.ToLowerInvariant();
            var audio = Integer(entry, "mediaType") == 5 || extension is "mp3" or "m4a" or "aac" or "ogg" or "wav" ||
                Integer(flavor, "width") == 0 && Integer(flavor, "height") == 0 && string.IsNullOrEmpty(Text(flavor, "videoCodecId"));
            media.Add(new()
            {
                Id = id, Url = Resource(raw), Kind = MediaKind.Progressive, Source = "kaltura", Extension = extension,
                MimeType = extension switch { "mp4" => audio ? "audio/mp4" : "video/mp4", "mov" => "video/quicktime", "mp3" => "audio/mpeg", "m4a" => "audio/mp4", _ => (audio ? "audio/" : "video/") + extension },
                Width = Integer(flavor, "width"), Height = Integer(flavor, "height"), FramesPerSecond = Number(flavor, "frameRate") is > 0 and var fps ? fps : null,
                Bandwidth = Number(flavor, "bitrate") * 1000, FileSize = Long(flavor, "sizeInBytes") is > 0 and var size ? size : null, VideoCodec = audio ? null : Text(flavor, "videoCodecId"),
                AudioCodec = Text(flavor, "audioCodecId"), HasVideo = !audio, HasAudio = audio ? true : null, Label = Text(flavor, "label")
            });
        }
        var seen = new HashSet<string>(media.Select(candidate => candidate.Url.AbsoluteUri), StringComparer.Ordinal);
        foreach (var source in Array(context, "sources"))
        {
            if (Protected(source) || Text(source, "url") is not { } raw) continue;
            var kind = Text(source, "format") switch { "applehttp" => MediaKind.Hls, "mpegdash" => MediaKind.Dash, _ => MediaKind.Unknown };
            if (kind == MediaKind.Unknown) continue;
            var url = Resource(raw);
            if (!seen.Add(url.AbsoluteUri)) continue;
            media.Add(new() { Id = (kind == MediaKind.Hls ? "hls-" : "dash-") + (Text(source, "deliveryProfileId") ?? Long(source, "deliveryProfileId")?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? media.Count.ToString()), Url = url,
                Kind = kind, Source = "kaltura", Extension = kind == MediaKind.Hls ? "m3u8" : "mpd", HasVideo = Integer(entry, "mediaType") == 5 ? false : null, HasAudio = Integer(entry, "mediaType") == 5 ? true : null });
        }
        var tracks = Array(context, "playbackCaptions");
        if (tracks.Length > 256) throw new InvalidDataException("Kaltura returned too many caption tracks.");
        var subtitles = new List<SubtitleTrack>();
        foreach (var track in tracks)
        {
            var raw = Text(track, "webVttUrl") ?? (Text(track, "format") is "3" or "webvtt" ? Text(track, "url") : null);
            if (raw is null) continue;
            subtitles.Add(new() { Url = Resource(raw), Language = Text(track, "languageCode") ?? "und", Extension = "vtt" });
        }
        return new()
        {
            Id = address.EntryId, Extractor = "kaltura", Title = Text(entry, "name") ?? address.EntryId, Description = Text(entry, "description"),
            Uploader = Text(entry, "creatorId") ?? Text(entry, "userId"), DurationSeconds = Number(entry, "msDuration") is > 0 and var ms ? ms / 1000 : Number(entry, "duration"),
            ThumbnailUrl = Text(entry, "thumbnailUrl") is { } thumbnail ? Resource(thumbnail).AbsoluteUri : null, Media = media, Subtitles = subtitles,
            Warnings = media.Count == 0 ? ["Kaltura did not expose downloadable unprotected sources for this entry."] : []
        };
    }

    private static JsonElement[] DownloadableFlavors(JsonElement context, KalturaAddress address)
    {
        if (context.ValueKind != JsonValueKind.Object || Text(context, "objectType") != "KalturaPlaybackContext") throw new InvalidDataException("Kaltura returned an invalid playback context.");
        foreach (var name in new[] { "actions", "sources", "flavorAssets", "playbackCaptions" })
            if (context.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null))
                throw new InvalidDataException("Kaltura returned an invalid playback collection.");
        if (Array(context, "actions").Length > 0) throw new NotSupportedException("Kaltura applies playback restrictions or processing actions that the native provider cannot yet honor.");
        var sources = Array(context, "sources"); var flavors = Array(context, "flavorAssets");
        if (sources.Length > 256 || flavors.Length > 64) throw new InvalidDataException("The Kaltura playback context exceeds its format limit.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources.Where(source => Text(source, "format") == "url" && !Protected(source)))
            foreach (var id in (Text(source, "flavorIds") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            { if (!KalturaAddress.IsEntryId(id)) throw new InvalidDataException("Kaltura returned an invalid flavor identity."); ids.Add(id); }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var flavor in flavors)
        {
            var id = Text(flavor, "id");
            if (id is null || !KalturaAddress.IsEntryId(id) || !seen.Add(id) || Text(flavor, "entryId") != address.EntryId ||
                Integer(flavor, "partnerId") is { } partner && partner != address.PartnerId) throw new InvalidDataException("Kaltura returned mismatched or ambiguous flavor identities.");
        }
        return flavors.Where(flavor => ids.Contains(Text(flavor, "id")!) && Integer(flavor, "status") == 2 && Text(flavor, "objectType") != "KalturaWidevineFlavorAsset" &&
            Text(flavor, "fileExt")?.ToLowerInvariant() is "mp4" or "m4a" or "mp3" or "mov" or "webm" or "ogg" or "aac" or "wav").ToArray();
    }
    private static void ValidateMedia(JsonElement entry, KalturaAddress address)
    {
        if (Text(entry, "id") != address.EntryId || Integer(entry, "partnerId") != address.PartnerId) throw new InvalidDataException("Kaltura returned a different entry or partner.");
        if (Integer(entry, "mediaType") is not (1 or 5)) throw new NotSupportedException("Native Kaltura live streams and non-audio/video entries remain pending.");
    }
    private static bool Protected(JsonElement source)
    {
        if (!source.TryGetProperty("drm", out var value) || value.ValueKind == JsonValueKind.Null) return false;
        if (value.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Kaltura returned invalid protection metadata.");
        return value.GetArrayLength() != 0;
    }
    private static Uri Resource(string value) => Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" && url.UserInfo.Length == 0
        ? url : throw new InvalidDataException("Kaltura returned an invalid resource address.");
    internal static async Task<string> Call(string service, string action, Dictionary<string, object?> arguments, MediaHttp http, CancellationToken ct)
    {
        arguments["format"] = 1;
        return await http.PostJsonAsync(new Uri("https://www.kaltura.com/api_v3/service/" + service + "/action/" + action), arguments, 8 * 1024 * 1024, null, ct, includeCharset: false).ConfigureAwait(false);
    }
    internal static JsonDocument Read(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > 8 * 1024 * 1024) throw new InvalidDataException("The Kaltura response exceeds its size limit.");
        var document = JsonDocument.Parse(json);
        if (Text(document.RootElement, "objectType") == "KalturaAPIException")
        {var expired=Text(document.RootElement,"code") is "INVALID_KS" or "KS_EXPIRED";document.Dispose();if(expired)throw new KalturaSessionExpiredException();throw new InvalidOperationException("Kaltura rejected this media request.");}
        return document;
    }
}
internal sealed class KalturaSessionExpiredException:InvalidOperationException{internal KalturaSessionExpiredException():base("Kaltura refused the expired session."){} }
