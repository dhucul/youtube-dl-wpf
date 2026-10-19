using System.Text.Json;
using YoutubeDl.Engine.Media;

namespace YoutubeDl.Engine.Download;

// IDs are period-local. Semantic fallback remains unique and decoder/layout checks
// still run before publication; an accepted period cannot silently choose another ID.
internal sealed class DashTrackSelection(DashRepresentation selected,NativeDownloadRequest? request=null,bool captions=false)
{
    private readonly Dictionary<string,(string Id,string? Group)> _periods=new(StringComparer.Ordinal);
    private readonly Dictionary<string,string> _profiles=new(StringComparer.Ordinal);
    internal static string Profile(DashRepresentation value)=>JsonSerializer.Serialize(new {value.MimeType,value.Codecs,value.Width,value.Height,value.Bandwidth,value.FramesPerSecond,value.Language,value.Roles,value.Channels,value.SamplingRate,value.Viewpoints,value.Accessibility});
    private bool Convert=>captions&&request?.LiveDashSelection?.ConvertCaptionCodecs==true;
    private bool Quality=>!captions&&request?.LiveDashSelection?.FollowQuality==true&&string.IsNullOrWhiteSpace(request.FormatId)&&string.IsNullOrWhiteSpace(request.FormatSelector);
    private bool Normalize=>!captions&&request?.LiveDashSelection?.NormalizeTransitions==true;
    private bool Ladder=>Normalize&&request?.LiveDashSelection?.AdaptiveQualityLadder==true;
    internal static bool CaptionCodec(string? codec)=>codec=="wvtt"||Mp4TtmlReader.IsSupportedCodec(codec);
    internal static string PolicyProfile(DashRepresentation value,bool quality,bool conversion)=>JsonSerializer.Serialize(new{value.MimeType,Codecs=conversion&&CaptionCodec(value.Codecs)?"native-webvtt":value.Codecs,value.Width,value.Height,Bandwidth=quality||conversion?null:value.Bandwidth,value.FramesPerSecond,value.Language,value.Roles,value.Channels,value.SamplingRate,value.Viewpoints,value.Accessibility});
    internal string Key=>AdaptiveFragmentCheckpoint.Hash(request?.LiveDashSelection is null?Profile(selected):Normalize?JsonSerializer.Serialize(new{Profile=TransitionProfile(selected),request.Quality,request.MaximumHeight,request.MaximumFrameRate,request.VideoCodec,request.PreferFreeFormats,request.LiveDashSelection}):JsonSerializer.Serialize(new{Profile=PolicyProfile(selected,Quality,Convert),request.Quality,request.LiveDashSelection}));
    private string TransitionProfile(DashRepresentation value)=>JsonSerializer.Serialize(new{value.MimeType,Codecs="converted-"+(value.IsVideo?request!.LiveDashSelection!.TransitionVideoCodec??"h264":request!.LiveDashSelection!.TransitionAudioCodec??"alac"),Width=Ladder?null:value.Width,Height=Ladder?null:value.Height,FramesPerSecond=Ladder?null:value.FramesPerSecond,value.Language,value.Roles,Channels=LayoutTransition(value)?null:value.Channels,SamplingRate=LayoutTransition(value)?null:value.SamplingRate,value.Viewpoints,value.Accessibility});
    private bool LayoutTransition(DashRepresentation value)=>Normalize&&value.IsAudio&&request?.LiveDashSelection?.TransitionAudioLayout is not null;
    internal static bool ConvertibleCodec(DashRepresentation value)
    {
        var codec=value.Codecs?.ToLowerInvariant();var names=value.IsVideo?new[]{"avc1","avc3","h264","hvc1","hev1","dvhe","dvh1","hevc","vp09","vp9","av01","av1"}:new[]{"mp4a.40.2","alac","flac","opus","ac-3","ec-3","ac3","eac3"};
        return codec is not null&&names.Any(name=>codec==name||value.IsVideo&&codec.StartsWith(name+".",StringComparison.Ordinal));
    }
    private bool Same(DashRepresentation a,DashRepresentation b)=>a.MimeType==b.MimeType&&(a.Codecs==b.Codecs||Convert&&CaptionCodec(a.Codecs)&&CaptionCodec(b.Codecs)||Normalize&&ConvertibleCodec(a)&&ConvertibleCodec(b))&&
        (Ladder&&a.IsVideo?a.Width>0&&a.Height>0&&b.Width is >0 and <=7680&&b.Height is >0 and <=4320&&b.FramesPerSecond is >0 and <=120:a.Width==b.Width&&a.Height==b.Height&&a.FramesPerSecond==b.FramesPerSecond)&&
        a.Language==b.Language&&a.Roles==b.Roles&&(LayoutTransition(a)||a.Channels==b.Channels&&a.SamplingRate==b.SamplingRate)&&a.Viewpoints==b.Viewpoints&&a.Accessibility==b.Accessibility&&(!Normalize||ConvertibleCodec(b))&&Constraints(b);
    private bool Constraints(DashRepresentation value)=>!value.IsVideo||request is null||(request.MaximumHeight is not >0||value.Height<=request.MaximumHeight)&&(request.MaximumFrameRate<=0||value.FramesPerSecond<=request.MaximumFrameRate)&&FormatFacts.CodecMatches(value.Codecs,request.VideoCodec);
    private bool CrossGroup=>selected.AdaptationIdentity is not null&&!string.IsNullOrWhiteSpace(selected.Codecs)&&!string.IsNullOrWhiteSpace(selected.Roles)&&
        (selected.IsVideo?selected.Width>0&&selected.Height>0:!string.IsNullOrWhiteSpace(selected.Language)&&selected.Language!="und");
    internal void Pin(string period,string identity,string? group)
    {
        if(string.IsNullOrWhiteSpace(identity)||identity.Length>2048)throw new InvalidDataException("The saved DASH track binding is invalid.");
        if(_periods.TryGetValue(period,out var known)){if(known!=(identity,group))throw new InvalidDataException("An accepted DASH period changed its selected track identity.");return;}
        if(_periods.Count>=1000)throw new InvalidDataException("The DASH selection history exceeds its limit.");_periods.Add(period,(identity,group));
    }
    internal DashRepresentation Resolve(string period,IReadOnlyList<DashRepresentation> offered)
    {
        DashRepresentation? result;
        if(_periods.TryGetValue(period,out var pinned))result=offered.SingleOrDefault(item=>item.Id==pinned.Id&&item.AdaptationIdentity==pinned.Group);
        else
        {
            result=Quality?null:offered.SingleOrDefault(item=>item.Id==selected.Id&&item.AdaptationIdentity==selected.AdaptationIdentity);
            if(result is null&&selected.AdaptationIdentity is not null&&!string.IsNullOrWhiteSpace(selected.Codecs))
            {
                var matches=offered.Where(item=>item.AdaptationIdentity==selected.AdaptationIdentity&&Same(selected,item)&&Bandwidth(item)).ToArray();
                if(matches.Length>0)result=Choose(matches);
                else if(CrossGroup)
                {
                    matches=offered.Where(item=>item.AdaptationIdentity is not null&&Same(selected,item)&&Bandwidth(item)).ToArray();
                    if(matches.Length>0)result=Choose(matches);
                }
            }
        }
        if(result is null)throw new InvalidDataException("The selected DASH track disappeared or became ambiguous.");
        if(!Same(selected,result))throw new NotSupportedException("The selected DASH track changed its declared profile or role.");
        var profile=Profile(result);
        if(_profiles.TryGetValue(period,out var accepted)&&accepted!=profile)throw new InvalidDataException("An accepted DASH period changed its selected profile.");
        _profiles[period]=profile;
        Pin(period,result.Id,result.AdaptationIdentity);return result;
    }
    private bool Bandwidth(DashRepresentation item)=>Quality?selected.Bandwidth is >0?item.Bandwidth is >0:item.Bandwidth==selected.Bandwidth:Convert||item.Bandwidth==selected.Bandwidth;
    private DashRepresentation Choose(DashRepresentation[] matches)
    {
        if(Quality)
        {
            var ordered=matches.OrderBy(item=>Ladder&&item.IsVideo?item.Height:0).ThenBy(item=>Ladder&&item.IsVideo?item.FramesPerSecond:0).ThenBy(item=>request!.PreferFreeFormats&&item.Codecs is { } codec&&(codec.StartsWith("vp09",StringComparison.Ordinal)||codec.StartsWith("av01",StringComparison.Ordinal))?1:0).ThenBy(item=>item.Bandwidth);
            var best=request!.Quality==QualityPreference.Worst?ordered.First():ordered.Last();
            matches=matches.Where(item=>(!Ladder||!item.IsVideo||item.Height==best.Height&&item.FramesPerSecond==best.FramesPerSecond)&&item.Bandwidth==best.Bandwidth&&(!request.PreferFreeFormats||Free(item)==Free(best))).ToArray();
            if(matches.SingleOrDefault(item=>item.Id==selected.Id&&item.AdaptationIdentity==selected.AdaptationIdentity) is { } exact)return exact;
        }
        if(matches.Length!=1)throw new InvalidDataException("The selected DASH successor is ambiguous.");
        return matches[0];
    }
    private static bool Free(DashRepresentation item)=>item.Codecs is { } codec&&(codec.StartsWith("vp09",StringComparison.Ordinal)||codec.StartsWith("av01",StringComparison.Ordinal));
}

