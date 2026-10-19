using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Protocols.Sabr;

namespace YoutubeDl.Engine.Providers.YouTube;

internal sealed class YouTubeVideoSession(Func<CancellationToken,Task<PageMetadata>> refresh,Func<Uri,CancellationToken,Task<MediaDocument>>? readManifest=null):IDisposable
{
    private RenewableMediaAuthorization? _media;private PageMetadata? _initial;
    internal PageMetadata Bind(PageMetadata page,bool authenticated,bool controlled=true)
    {
        _initial??=page;
        if(controlled&&(page.Media.Any(m=>m.Kind is MediaKind.Progressive or MediaKind.Hls or MediaKind.Dash)||page.Subtitles.Count>0))_media??=new(page,ct=>FreshAsync(ct,true),readManifest:readManifest);
        var source=page.Media.FirstOrDefault(m=>m.Sabr is not null)?.Sabr?.Session;
        if(source is null)return page with {PlaybackAuthorization=_media};
        var session=source with {Renewal=RenewAsync};
        return page with {Media=page.Media.Select(m=>m.Sabr is null?m:m.WithSabrSession(session)).ToArray(),PlaybackAuthorization=_media,AnonymousPlayback=!authenticated};
    }
    private async Task<PageMetadata> FreshAsync(CancellationToken ct,bool allowEnded=false)
    {
        var page=await refresh(ct).ConfigureAwait(false);if(page.Id!=_initial!.Id||page.IsPlaylist||page.DrmRefused||page.IsLive!=_initial.IsLive&&!(allowEnded&&_initial.IsLive&&!page.IsLive))throw new InvalidDataException("YouTube playback renewal changed video identity or access.");return page;
    }
    private async Task<SabrPlaybackSession> RenewAsync(SabrPlaybackSession previous,CancellationToken ct)
    {
        var page=await FreshAsync(ct).ConfigureAwait(false);var offered=page.Media.Where(m=>m.Sabr is not null).ToArray();var session=offered.FirstOrDefault()?.Sabr?.Session;
        var required=_initial!.Media.Where(m=>m.Sabr is not null).Select(m=>m.Sabr!.Format).ToHashSet();
        if(session is null||session.VideoId!=previous.VideoId||session.ClientName!=previous.ClientName||session.ClientVersion!=previous.ClientVersion||!required.IsSubsetOf(offered.Select(m=>m.Sabr!.Format).ToHashSet()))
            throw new InvalidDataException("The renewed SABR context changed its client or encoded format identity.");
        return session with {Renewal=RenewAsync};
    }
    public void Dispose()=>_media?.Dispose();
}
