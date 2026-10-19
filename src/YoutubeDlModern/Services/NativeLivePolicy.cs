using YoutubeDl.Engine;
using YoutubeDlModern.Models;

namespace YoutubeDlModern.Services;

internal static class NativeLivePolicy
{
    internal static LiveDashSelectionPolicy? Create(FormatSettings settings)
    {
        if(!settings.NativeLiveDash)return null;
        var active=settings.LiveNormalizeTransitions;var layout=active?Choice(settings.LiveTransitionAudioLayout):null;var color=active?Choice(settings.LiveTransitionColorPolicy):null;
        var policy=new LiveDashSelectionPolicy{AudioLanguages=Tokens(settings.LiveAudioLanguages),AudioRoles=Tokens(settings.LiveAudioRoles),CaptionRoles=Tokens(settings.LiveCaptionRoles),FollowQuality=settings.LiveFollowQuality,ConvertCaptionCodecs=settings.LiveConvertCaptions,AdaptiveQualityLadder=settings.LiveAdaptiveLadder,NormalizeTransitions=active,TransitionHeight=active?settings.LiveTransitionHeight:0,
            TransitionVideoCodec=active?Choice(settings.LiveTransitionVideoCodec):null,TransitionAudioCodec=active?Choice(settings.LiveTransitionAudioCodec):null,TransitionAudioLayout=layout,TransitionAudioSampleRate=layout is null?0:settings.LiveTransitionAudioSampleRate,NormalizeVideoGeometry=active&&settings.LiveNormalizeGeometry,TransitionColorPolicy=color,HdrPeakNits=color=="hdr-to-bt709"?settings.LiveHdrPeakNits:0,TransitionSelectorPolicy=active&&color=="hdr-reconstruct"&&settings.LiveHdrSelectorPolicy!="strict"?Choice(settings.LiveHdrSelectorPolicy):null,TransitionCrop=active&&color=="hdr-reconstruct"&&!string.IsNullOrWhiteSpace(settings.LiveTransitionCrop)?settings.LiveTransitionCrop.Trim():null};
        policy.Validate();return policy;
    }
    private static string[] Tokens(string? value)=>(value??"").Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
    private static string? Choice(string? value)=>string.IsNullOrWhiteSpace(value)||value is "auto" or "preserve"?null:value;
}
