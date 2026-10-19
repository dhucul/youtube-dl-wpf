using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Vimeo;

internal sealed class VimeoSession(MediaHttp owner,NativeDownloadRequest request,VimeoAddress address):IDisposable
{
    private readonly MediaHttp _controlled=owner.CreateDiscoverySession(request.Authentication.IsConfigured);private RenewableMediaAuthorization? _lease;
    private readonly SessionLifetime _lifetime=new();
    private PageMetadata? _initial;
    internal async Task<PageMetadata> ReadAsync(CancellationToken ct)
    {
        using var operation=_lifetime.Begin(ct);ct=operation.Token;await _lifetime.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
        ct.ThrowIfCancellationRequested();var page=_initial??await ReadFreshCoreAsync(ct).ConfigureAwait(false);ct.ThrowIfCancellationRequested();
        return _lifetime.Read(()=>
        {
            if(owner.CanControlRedirects&&(page.Media.Any(m=>m.Kind is MediaKind.Progressive or MediaKind.Hls or MediaKind.Dash)||page.Subtitles.Count>0))_lease??=new(page,ReadFreshAsync,SafeRedirect,(url,token)=>_controlled.ReadPlaybackManifestAsync(url,SafeRedirect,Referer,token));
            _initial??=page;
            return page with {SourcePageUrl=request.Url,PlaybackAuthorization=_lease,PlaybackHeaders=new Dictionary<string,string>{["Referer"]=string.IsNullOrWhiteSpace(request.Referer)?request.Url.AbsoluteUri:request.Referer}};
        });
        }finally{_lifetime.Gate.Release();}
    }
    private async Task<PageMetadata> ReadFreshAsync(CancellationToken ct)
    {
        using var operation=_lifetime.Begin(ct);ct=operation.Token;await _lifetime.Gate.WaitAsync(ct).ConfigureAwait(false);
        try{var page=await ReadFreshCoreAsync(ct).ConfigureAwait(false);ct.ThrowIfCancellationRequested();return page;}finally{_lifetime.Gate.Release();}
    }
    private async Task<PageMetadata> ReadFreshCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(request.Authentication.AccessToken.Length>0||request.Authentication.OAuth is not null)
        {
            var url=new Uri("https://api.vimeo.com/videos/"+address.VideoId+(address.UnlistedHash is null?"":":"+Uri.EscapeDataString(address.UnlistedHash)));
            var document=request.Authentication.OAuth is { } credentials?await owner.SessionState("oauth:vimeo",()=>new Authentication.NativeOAuthSession(owner,credentials)).GetAsync(url,ct).ConfigureAwait(false):await _controlled.GetDocumentAsync(url,2*1024*1024,ct,new Dictionary<string,string>{["Authorization"]="Bearer "+request.Authentication.AccessToken,["Accept"]="application/vnd.vimeo.*+json;version=3.4"}).ConfigureAwait(false);
            if(document.Url!=url)throw new InvalidDataException("The Vimeo API request changed its selected resource.");
            var page=ParseApi(document.Text,address.VideoId);
            // Fetch account text tracks only when the caller asks for captions.
            if(request.WriteSubs||request.WriteAutoSubs||request.EmbedSubs)
                page=page with {Subtitles=await TextTracksAsync(ct).ConfigureAwait(false)};
            return page;
        }
        var config=await ConfigurationAsync(ct).ConfigureAwait(false);
        using(var doc=JsonDocument.Parse(config.Text))
        {
            if(Long(doc.RootElement,"view")==4)
            {
                var id=Text(Object(doc.RootElement,"video"),"id")??Long(Object(doc.RootElement,"video"),"id")?.ToString(CultureInfo.InvariantCulture);
                if(id!=address.VideoId)throw new InvalidDataException("The password gate changed its video identity.");
                if(request.Authentication.VideoPassword.Length==0)throw new NotSupportedException("This Vimeo video requires its video password.");
                var referrer=string.IsNullOrWhiteSpace(request.Referer)?request.Url.AbsoluteUri:request.Referer;
                var passwordUrl=new Uri("https://player.vimeo.com/video/"+address.VideoId+"/check-password?referrer="+Uri.EscapeDataString(referrer)+(address.UnlistedHash is null?"":"&h="+Uri.EscapeDataString(address.UnlistedHash)));
                var bytes=Encoding.UTF8.GetBytes(request.Authentication.VideoPassword);string encoded;try{encoded=Convert.ToBase64String(bytes);}finally{System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);}
                var unlocked=await _controlled.PostFormDocumentAsync(passwordUrl,new Dictionary<string,string>{["password"]=encoded},2*1024*1024,ct).ConfigureAwait(false);
                if(unlocked.Url!=passwordUrl)throw new InvalidDataException("The password submission redirected outside its selected gate.");
                using var result=JsonDocument.Parse(unlocked.Text);if(result.RootElement.ValueKind==JsonValueKind.False)throw new NotSupportedException("Vimeo rejected the supplied video password.");
                config=result.RootElement.ValueKind==JsonValueKind.Object&&Object(result.RootElement,"video").ValueKind==JsonValueKind.Object&&Long(result.RootElement,"view") is 1 or 3?
                    unlocked:await ConfigurationAsync(ct).ConfigureAwait(false);
            }
        }
        return VimeoProvider.ParseConfiguration(config.Text,address.VideoId,request.VideoCodec);
    }
    private async Task<MediaDocument> ConfigurationAsync(CancellationToken ct)
    {
        var headers=new Dictionary<string,string>{["Referer"]=Referer};
        try
        {
            var document=await _controlled.GetDocumentAsync(address.ConfigurationUrl,2*1024*1024,ct,headers).ConfigureAwait(false);
            if(document.Url!=address.ConfigurationUrl)throw new InvalidDataException("Vimeo changed the selected player configuration address.");return document;
        }
        catch(HttpRequestException e)when(e.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            // The issuer's actual player may provide literal configuration while
            // its standalone configuration endpoint is not exposed.
            var player=await _controlled.GetDocumentAsync(address.PlayerUrl,2*1024*1024,ct,headers).ConfigureAwait(false);
            if(player.Url!=address.PlayerUrl)throw new InvalidDataException("Vimeo changed the selected player page.");
            using var configuration=BoundedPageState.Read(player.Text,"playerConfig",windowObject:true);
            var json=configuration.RootElement.GetRawText();
            var id=Text(Object(configuration.RootElement,"video"),"id")??Long(Object(configuration.RootElement,"video"),"id")?.ToString(CultureInfo.InvariantCulture);
            if(id!=address.VideoId)throw new InvalidDataException("The literal Vimeo player changed its video identity.");
            return new MediaDocument(address.ConfigurationUrl,json,"application/json");
        }
    }
    private string Referer=>string.IsNullOrWhiteSpace(request.Referer)?request.Url.AbsoluteUri:request.Referer;
    internal static PageMetadata ParseApi(string json,string expectedId)
    {
        if(Encoding.UTF8.GetByteCount(json)>2*1024*1024)throw new InvalidDataException("The Vimeo API response exceeds its limit.");
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        if(Text(root,"uri")!="/videos/"+expectedId)throw new InvalidDataException("The Vimeo API response belongs to another video.");
        var media=new List<MediaCandidate>();var ids=new HashSet<string>(StringComparer.Ordinal);var files=Array(Object(root,"play"),"progressive");if(files.Length==0)files=Array(root,"files");if(files.Length==0)files=Array(root,"download");
        if(files.Length>1000)throw new InvalidDataException("The Vimeo API format count exceeds its limit.");
        foreach(var file in files)
        {
            var mime=Text(file,"type")??Text(file,"mime");if(mime is not ("video/mp4" or "video/webm"))continue;
            if(Object(file,"drm").ValueKind==JsonValueKind.Object||Text(file,"drm") is not null)throw new NotSupportedException("The API supplied a protected file representation.");
            var link=Text(file,"link")??Text(file,"link_secure");if(link is null)continue;var url=Secure(link);
            var height=Integer(file,"height");var width=Integer(file,"width");var quality=Text(file,"rendition")??Text(file,"quality")??height?.ToString(CultureInfo.InvariantCulture)??"file";
            var id="api-"+quality+"-"+height;if(!ids.Add(id))throw new InvalidDataException("The Vimeo API returned ambiguous file identities.");
            media.Add(new(){Id=id,Url=url,Kind=MediaKind.Progressive,Source="vimeo:api",MimeType=mime,Extension=mime=="video/mp4"?"mp4":"webm",Height=height,Width=width,HasVideo=true,HasAudio=true,
                FramesPerSecond=Number(file,"fps"),FileSize=Long(file,"size"),ResourceExpires=DateTimeOffset.TryParse(Text(file,"expires")??Text(file,"expires_time"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var expiry)?expiry:null});
        }
        foreach(var kind in new[]{"hls","dash"})
        {
            var descriptor=Object(Object(root,"play"),kind);var link=Text(descriptor,"link")??Text(descriptor,"link_secure");if(link is null)continue;
            if(Object(descriptor,"drm").ValueKind==JsonValueKind.Object||Text(descriptor,"drm") is not null)throw new NotSupportedException("The API supplied a protected adaptive representation.");
            media.Add(new(){Id="api-"+kind,Url=Secure(link),Kind=kind=="hls"?MediaKind.Hls:MediaKind.Dash,Source="vimeo:api",Extension=kind=="hls"?"m3u8":"mpd",HasVideo=true,HasAudio=true,
                ResourceExpires=DateTimeOffset.TryParse(Text(descriptor,"expires")??Text(descriptor,"expires_time"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var expiry)?expiry:null});
        }
        if(media.Count==0)throw new NotSupportedException("The Vimeo API did not grant supported clear file links. Check the token's account access and video_files scope.");
        return new(){Id=expectedId,Extractor="vimeo",Title=Text(root,"name")??expectedId,Description=Text(root,"description"),DurationSeconds=Number(root,"duration"),Uploader=Text(Object(root,"user"),"name"),Media=media};
    }
    private async Task<IReadOnlyList<SubtitleTrack>> TextTracksAsync(CancellationToken ct)
    {
        var captions=new List<SubtitleTrack>();var identities=new HashSet<string>(StringComparer.Ordinal);var urls=new HashSet<Uri>();var next=new Uri("https://api.vimeo.com/videos/"+address.VideoId+(address.UnlistedHash is null?"":":"+Uri.EscapeDataString(address.UnlistedHash))+"/texttracks?per_page=100");long? total=null;
        for(var page=0;page<100;page++)
        {
            if(!urls.Add(next))throw new InvalidDataException("Vimeo text tracks repeated a continuation.");
            var document=request.Authentication.OAuth is { } credentials?await owner.SessionState("oauth:vimeo",()=>new Authentication.NativeOAuthSession(owner,credentials)).GetAsync(next,ct).ConfigureAwait(false):await _controlled.GetDocumentAsync(next,2*1024*1024,ct,new Dictionary<string,string>{["Authorization"]="Bearer "+request.Authentication.AccessToken}).ConfigureAwait(false);
            using var doc=JsonDocument.Parse(document.Text);var root=doc.RootElement;var count=Long(root,"total");if(count is null||count>256||total is not null&&count!=total)throw new InvalidDataException("Vimeo text-track counts changed or exceed the limit.");total=count;
            var data=Array(root,"data");if(data.Length==0&&Text(Object(root,"paging"),"next") is not null)throw new InvalidDataException("Vimeo text-track pagination made no progress.");
            foreach(var item in data)
            {
                var id=Text(item,"uri");if(id is null||!id.StartsWith("/videos/"+address.VideoId+"/texttracks/",StringComparison.Ordinal)||!identities.Add(id))throw new InvalidDataException("Vimeo changed or duplicated a text-track identity.");
                if(!True(item,"active"))continue;var language=Text(item,"language")??"und";var link=Text(Object(item,"download_links"),"vtt")??Text(item,"link");if(link is null||language.Length>128)throw new InvalidDataException("Vimeo text tracks omit required file facts.");
                captions.Add(new(){Url=Secure(link),Language=language,Extension="vtt",ResourceIdentity=id,ResourceExpires=DateTimeOffset.TryParse(Text(item,"download_links_expires_time"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var expiry)?expiry:null,Automatic=language.EndsWith("-x-autogen",StringComparison.OrdinalIgnoreCase)});
            }
            var following=Text(Object(root,"paging"),"next");if(following is null){if(identities.Count!=total)throw new InvalidDataException("Vimeo returned incomplete text-track inventory.");return captions;}
            var candidate=new Uri(next,following);if(candidate.Scheme!="https"||candidate.Host!="api.vimeo.com"||!candidate.IsDefaultPort||candidate.AbsolutePath!=next.AbsolutePath||candidate.UserInfo.Length>0||candidate.Fragment.Length>0)throw new InvalidDataException("Vimeo text tracks changed their account API scope.");next=candidate;
        }
        throw new InvalidDataException("Vimeo text-track pagination exceeds its limit.");
    }
    private static Uri Secure(string value)=>Uri.TryCreate(value,UriKind.Absolute,out var url)&&url.Scheme=="https"&&url.IsDefaultPort&&url.UserInfo.Length==0&&url.Fragment.Length==0?url:throw new InvalidDataException("Vimeo supplied an invalid file address.");
    private static bool SafeRedirect(Uri url)=>url.Scheme=="https"&&url.IsDefaultPort&&url.UserInfo.Length==0&&url.Fragment.Length==0&&
        (url.Host=="player.vimeo.com"||url.Host.EndsWith(".vimeocdn.com",StringComparison.OrdinalIgnoreCase)||url.Host.EndsWith(".akamaized.net",StringComparison.OrdinalIgnoreCase)||url.Host.EndsWith(".cloudfront.net",StringComparison.OrdinalIgnoreCase));
    public void Dispose(){_lifetime.Dispose(()=>{_lease?.Dispose();_lease=null;_initial=null;});_controlled.Dispose();}
}
