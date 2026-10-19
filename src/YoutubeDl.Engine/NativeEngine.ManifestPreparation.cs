using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine;

public sealed partial class NativeEngine
{
    // Only initial reads are reused. Live refreshes and issuer renewal always use their own readers.
    private sealed class ManifestPreparation
    {
        private readonly Dictionary<Uri,(HlsPlaylist Value,long At)> _hls=[];
        private readonly Dictionary<Uri,(DashManifest Value,long At)> _dash=[];
        internal bool LiveDetected {get;private set;}
        internal void MarkLive()=>LiveDetected=true;
        internal async Task<HlsPlaylist> PrepareHlsAsync(MediaHttp http,Uri url,CancellationToken ct)
        {
            var value=await new HlsDownloader(http).ReadAsync(url,ct).ConfigureAwait(false);
            if(_hls.Count<16)_hls[url]=(value,Environment.TickCount64);return value;
        }
        internal async Task<DashManifest> PrepareDashAsync(MediaHttp http,Uri url,CancellationToken ct,bool liveProjection=false)
        {
            var value=await new DashDownloader(http).ReadAsync(url,ct,liveProjection).ConfigureAwait(false);
            if(_dash.Count<16)_dash[url]=(value,Environment.TickCount64);return value;
        }
        internal async Task<HlsPlaylist> ReadHlsAsync(MediaHttp http,Uri url,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();HlsPlaylist value;
            if(_hls.Remove(url,out var saved)&&Fresh(saved.At,saved.Value.IsMaster||saved.Value.IsComplete?5:Math.Min(2,(saved.Value.TargetDurationSeconds??2)/2))&&FreshHls(saved.Value))value=saved.Value;
            else value=await new HlsDownloader(http).ReadAsync(url,ct).ConfigureAwait(false);
            if(!value.IsMaster&&!value.IsComplete)MarkLive();return value;
        }
        internal async Task<DashManifest> ReadDashAsync(MediaHttp http,Uri url,CancellationToken ct,bool liveProjection=false)
        {
            ct.ThrowIfCancellationRequested();DashManifest value;
            if(_dash.Remove(url,out var saved)&&(!liveProjection||saved.Value.LiveProjection)&&Fresh(saved.At,saved.Value.IsDynamic?Math.Min(2,saved.Value.MinimumUpdatePeriodSeconds/2):5)&&FreshDash(saved.Value))value=saved.Value;
            else value=await new DashDownloader(http).ReadAsync(url,ct,liveProjection).ConfigureAwait(false);
            if(value.IsDynamic||liveProjection)MarkLive();return value;
        }
        private static bool Fresh(long at,double seconds)=>seconds>0&&Environment.TickCount64-at<=seconds*1000;
        private static bool FreshHls(HlsPlaylist value)=>value.MediaSegments.All(segment=>FreshUri(segment.Url)&&
            (segment.Initialization is null||FreshUri(segment.Initialization.Url))&&(segment.Encryption is null||FreshUri(segment.Encryption.KeyUrl)));
        private static bool FreshDash(DashManifest value)=>value.Representations.Concat(value.CaptionTracks.Select(track=>track.Representation)).Concat(value.Periods.SelectMany(period=>period.Representations.Concat(period.Captions.Select(track=>track.Representation))))
            .All(track=>(track.Initialization is null||FreshUri(track.Initialization))&&FreshUri(track.Url)&&track.Segments.All(segment=>FreshUri(segment.Url)));
        private static bool FreshUri(Uri url)=>PlaybackResourceExpiry.Read(url) is not { } expiry||expiry>DateTimeOffset.UtcNow.AddSeconds(5);
    }
}
