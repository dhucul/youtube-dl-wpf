using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Providers;

/// <summary>Owns one issuer/read context and binds clear offered resources to renewable format identities.</summary>
internal sealed class ProviderPlaybackSession:IDisposable
{
    private readonly MediaHttp _owner,_controlled;private readonly NativeDownloadRequest _request;private readonly string _provider;
    private readonly Func<MediaHttp,CancellationToken,Task<PageMetadata>> _read;private readonly bool _reorderedFormats;
    private RenewableMediaAuthorization? _lease;private PageMetadata? _initial;
    private readonly SessionLifetime _lifetime=new();
    internal ProviderPlaybackSession(MediaHttp owner,NativeDownloadRequest request,string provider,Func<MediaHttp,CancellationToken,Task<PageMetadata>> read,bool sensitive=false,bool reorderedFormats=true,bool publicSession=false)
    {_owner=owner;_request=request;_provider=provider;_read=read;_reorderedFormats=reorderedFormats;_controlled=publicSession?owner.CreateVerificationSession(null):owner.CreateDiscoverySession(sensitive);}
    internal async Task<PageMetadata> ReadAsync(CancellationToken ct)
    {
        using var operation=_lifetime.Begin(ct);ct=operation.Token;await _lifetime.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var page=_lifetime.Read(ct,()=>_initial)??await FreshCoreAsync(ct).ConfigureAwait(false);
            return _lifetime.Read(ct,()=>BindCore(page));
        }finally{_lifetime.Gate.Release();}
    }
    internal PageMetadata BindInitial(PageMetadata page)
    {
        return _lifetime.Read(()=>BindCore(page));
    }
    private PageMetadata BindCore(PageMetadata page)
    {
        var secure=page.Media.All(m=>Secure(m.Url))&&page.Subtitles.All(s=>Secure(s.Url));
        if(!secure&&_request.Authentication.IsConfigured)throw new NotSupportedException("Authenticated hosting playback requires secure declared resources.");
        _initial??=page;
        if(_owner.CanControlRedirects&&secure&&(page.Media.Count>0||page.Subtitles.Count>0))_lease??=new(page,FreshAsync,Redirect,(url,token)=>_controlled.ReadPlaybackManifestAsync(url,Redirect,Referer,token));
        return page with {IsLive=_lease?.IsLive??page.IsLive,SourcePageUrl=_request.Url,PlaybackAuthorization=_lease,PlaybackHeaders=new Dictionary<string,string>{["Referer"]=Referer}};
    }
    private string Referer=>_provider=="kaltura"?Kaltura.KalturaAddress.PublicReferer(_request):string.IsNullOrWhiteSpace(_request.Referer)?_request.Url.AbsoluteUri:_request.Referer;
    private async Task<PageMetadata> FreshAsync(CancellationToken ct)
    {
        using var operation=_lifetime.Begin(ct);ct=operation.Token;await _lifetime.Gate.WaitAsync(ct).ConfigureAwait(false);
        try{return await FreshCoreAsync(ct).ConfigureAwait(false);}finally{_lifetime.Gate.Release();}
    }
    private async Task<PageMetadata> FreshCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        PageMetadata page;using(NativeExtractionDelay.Extracting())page=await _read(_controlled,ct).ConfigureAwait(false);ct.ThrowIfCancellationRequested();
        var initial=_lifetime.Read(ct,()=>_initial);
        if(initial is null)return page;
        if(page.Id!=initial.Id||page.Extractor!=initial.Extractor||page.IsPlaylist||page.DrmRefused||!initial.IsLive&&page.IsLive)throw new InvalidDataException("Hosting renewal changed its media identity or access.");
        if(!_reorderedFormats)return page;
        var used=new HashSet<MediaCandidate>(ReferenceEqualityComparer.Instance);var formats=new List<MediaCandidate>();
        foreach(var old in initial.Media)
        {
            var offered=page.Media.Where(m=>Fingerprint(m)==Fingerprint(old)).ToArray();
            if(offered.Length!=1||!used.Add(offered[0]))
            {
                if(_lifetime.Read(ct,()=>_lease?.IsActive("format:"+old.Id)==true))throw new InvalidDataException("A used hosting format changed, disappeared or became ambiguous during renewal.");
                continue;
            }
            formats.Add(offered[0].WithIdentity(old.Id!));
        }
        return page with {Media=formats};
    }
    private static string Fingerprint(MediaCandidate m)=>JsonSerializer.Serialize(new object?[]{m.Kind,m.Extension,m.MimeType,m.Height,m.Width,m.Bandwidth,m.FramesPerSecond,m.FileSize,m.VideoCodec,m.AudioCodec,m.HasAudio,m.HasVideo,m.IsCompleteMedia,m.AudioLanguage,m.IsDefaultAudio,m.Label});
    private static bool Secure(Uri u)=>u.Scheme=="https"&&u.IsDefaultPort&&u.UserInfo.Length==0&&u.Fragment.Length==0;
    private bool Redirect(Uri u)
    {
        if(!Secure(u))return false;
        bool Host(string h)=>u.Host==h||u.Host.EndsWith("."+h,StringComparison.OrdinalIgnoreCase);
        return _provider switch
        {"facebook"=>Facebook.FacebookProvider.MediaHost(u),"ardsounds"=>ArdSounds.ArdSoundsProvider.MediaHost(u),"ard"=>Ard.ArdProvider.MediaHost(u),"zdf"=>Zdf.ZdfProvider.MediaHost(u),"nrk"=>Nrk.NrkProvider.MediaHost(u),"bbc"=>Bbc.BbcProvider.MediaHost(u),"svt"=>Svt.SvtProvider.MediaHost(u),"radiofrance-podcast"=>u.Host=="media.radiofrance-podcast.net","orf-on"=>u.Host is "apasfiis.sf.apa.at" or "vod-ww.mdn.ors.at"||u.Host=="api-tvthek.orf.at"&&u.AbsolutePath.StartsWith("/assets/subtitles/",StringComparison.Ordinal),"orf-radio"=>Orf.OrfRadioProvider.RadioHost(u.Host)||Orf.OrfRadioAddress.Stations.Any(station=>u.Host=="orf-live-"+station+".mdn.ors.at"),"tvp-vod"=>Tvp.TvpProvider.MediaHost(u),"arte"=>u.Host is "manifest-arte.akamaized.net" or "arte-cmafhls.akamaized.net" or "arteptweb-vh.akamaihd.net","orf-podcast"=>u.Host=="podcast.orf.at","rtveaudio"=>Rtve.RtveProvider.MediaHost(u),"whyp"=>u.Host=="cdn.whyp.it","jiosaavn"=>Music.MusicCatalog.Host(u,"saavncdn.com","jiosaavn.com")||u.Host=="api.spreaker.com"||u.Host=="d1bxy2pveef3fq.cloudfront.net","qqmusic"=>Music.MusicCatalog.Host(u,"qq.com"),"netease"=>Music.MusicCatalog.Host(u,"126.net","163.com","netease.com"),"rutube"=>Rutube.RutubeProvider.MediaHost(u),"soundcloud"=>u.Host.EndsWith(".sndcdn.com",StringComparison.OrdinalIgnoreCase)||u.Host.EndsWith(".soundcloud.cloud",StringComparison.OrdinalIgnoreCase),"wistia"=>Host("wistia.com")||Host("wistia.net")||Host("cloudfront.net"),"jwplatform"=>Host("jwplayer.com")||Host("jwplatform.com")||Host("cloudfront.net"),
            "brightcove"=>Host("brightcove.com")||Host("brightcove.net")||Host("boltdns.net")||Host("bcovlive.io")||Host("akamaized.net")||Host("cloudfront.net"),
            "kaltura"=>Host("kaltura.com")||Host("kaltura.org")||Host("kaltura.tv")||Host("akcdn.com")||Host("akamaized.net")||Host("cloudfront.net")||u.Host=="kalhlspd-a.akamaihd.net",_=>false};
    }
    public void Dispose(){_lifetime.Dispose(()=>{_lease?.Dispose();_lease=null;_initial=null;});_controlled.Dispose();}
}
