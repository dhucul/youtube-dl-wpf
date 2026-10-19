using System.Text.RegularExpressions;
using YoutubeDl.Engine.Download;

namespace YoutubeDl.Engine;

/// <summary>Explicit live DASH preferences. Period transitions retain the chosen language, role and decoder profile.</summary>
public sealed record LiveDashSelectionPolicy
{
    public IReadOnlyList<string> AudioLanguages { get; init; } = [];
    public IReadOnlyList<string> AudioRoles { get; init; } = [];
    public IReadOnlyList<string> CaptionRoles { get; init; } = [];
    public bool FollowQuality { get; init; } = true;
    public bool ConvertCaptionCodecs { get; init; } = true;
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool AdaptiveQualityLadder {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool NormalizeTransitions {get;init;}
    /// <summary>Explicit 16:9 output height at 30 fps for converted video periods.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int TransitionHeight {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? TransitionVideoCodec {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? TransitionAudioCodec {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? TransitionAudioLayout {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int TransitionAudioSampleRate {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool NormalizeVideoGeometry {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? TransitionColorPolicy {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int HdrPeakNits {get;init;}

    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? TransitionCrop {get;init;}

    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? TransitionSelectorPolicy {get;init;}

    public void Validate()
    {
        if(TransitionSelectorPolicy is not null&&(TransitionSelectorPolicy!="ellipse-envelope"||TransitionColorPolicy!="hdr-reconstruct"))throw new ArgumentException("Ellipse envelope authoring requires explicit HDR reconstruction.");
        if(TransitionCrop is not null){if(TransitionColorPolicy!="hdr-reconstruct")throw new ArgumentException("Explicit crop requires HDR reconstruction.");Media.HdrCrop.Parse(TransitionCrop);}
        if(TransitionVideoCodec is not (null or "h264" or "hevc" or "vp9" or "av1")||TransitionAudioCodec is not (null or "alac" or "aac" or "flac" or "opus")||TransitionAudioLayout is not (null or "mono" or "stereo" or "5.1")||TransitionColorPolicy is not (null or "bt709" or "hdr-to-bt709" or "hdr-preserve" or "wide-to-bt709" or "bt2020" or "hdr-metadata" or "hdr-spatial" or "hdr-transform" or "hdr-reconstruct"))
            throw new ArgumentException("Choose supported conversion codecs, a mono/stereo/5.1 layout and a known color policy.");
        if(!NormalizeTransitions&&(TransitionVideoCodec is not null||TransitionAudioCodec is not null||TransitionAudioLayout is not null||TransitionAudioSampleRate!=0||NormalizeVideoGeometry||TransitionColorPolicy is not null||HdrPeakNits!=0))
            throw new ArgumentException("Conversion targets require explicit transition conversion.");
        if((TransitionAudioLayout is null)!=(TransitionAudioSampleRate==0)||TransitionAudioSampleRate!=0&&TransitionAudioSampleRate is not (32000 or 44100 or 48000 or 88200 or 96000)||TransitionAudioCodec=="opus"&&TransitionAudioSampleRate!=48000)
            throw new ArgumentException("Audio-layout conversion requires an explicit supported rate; Opus requires an explicit 48 kHz layout.");
        if(TransitionColorPolicy=="hdr-to-bt709"?HdrPeakNits is <100 or >10000:HdrPeakNits!=0)
            throw new ArgumentException("HDR tone mapping requires an explicit nominal peak between 100 and 10000 nits.");
        if(TransitionColorPolicy is "hdr-preserve" or "bt2020" or "hdr-metadata" or "hdr-spatial" or "hdr-transform" or "hdr-reconstruct"&&TransitionVideoCodec!="hevc")
            throw new ArgumentException("Ten-bit HDR/wide-gamut output requires the explicit HEVC conversion target.");
        if(AdaptiveQualityLadder&&!NormalizeTransitions||NormalizeTransitions&&(TransitionHeight is <16 or >2160||TransitionHeight%2!=0)||!NormalizeTransitions&&TransitionHeight!=0)
            throw new ArgumentException("Adaptive resolution changes require transition conversion with an even output height between 16 and 2160.");
        foreach(var values in new[]{AudioLanguages,AudioRoles,CaptionRoles})
            if(values is null||values.Count>16||values.Any(value=>value is null||!Regex.IsMatch(value,@"\A[A-Za-z0-9]+(?:[-_][A-Za-z0-9]+)*\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))||value.Length>64)||values.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=values.Count)
                throw new ArgumentException("Live DASH preferences require at most sixteen distinct language or role tokens of at most 64 characters.");
    }
    private static int LanguageRank(string? language,IReadOnlyList<string> choices)
    {
        for(var i=0;i<choices.Count;i++)if(language is not null){if(language.Equals(choices[i],StringComparison.OrdinalIgnoreCase))return i*2;if(language.StartsWith(choices[i]+"-",StringComparison.OrdinalIgnoreCase))return i*2+1;}
        return choices.Count*2;
    }
    private static int RoleRank(string roles,IReadOnlyList<string> choices)
    {
        var values=roles.Split('\n').Where(value=>value.StartsWith("urn:mpeg:dash:role:2011=",StringComparison.Ordinal)).Select(value=>value[24..]).ToArray();
        for(var i=0;i<choices.Count;i++)if(values.Contains(choices[i],StringComparer.OrdinalIgnoreCase))return i;
        return choices.Count;
    }
    internal IReadOnlyList<DashRepresentation> Prefer(IReadOnlyList<DashRepresentation> offered,bool captions=false)
    {
        Validate();var languages=captions?Array.Empty<string>():AudioLanguages;var roles=captions?CaptionRoles:AudioRoles;
        if(languages.Count==0&&roles.Count==0)return offered;
        var ranked=offered.Select(item=>(Item:item,Language:languages.Count==0?0:LanguageRank(item.Language,languages),Role:roles.Count==0?0:RoleRank(item.Roles,roles)))
            .Where(item=>(languages.Count==0||item.Language<languages.Count*2)&&(roles.Count==0||item.Role<roles.Count)).OrderBy(item=>item.Language).ThenBy(item=>item.Role).ToArray();
        if(ranked.Length==0)throw new NotSupportedException("No live DASH track satisfies the requested language and role preferences.");
        return ranked.Where(item=>item.Language==ranked[0].Language&&item.Role==ranked[0].Role).Select(item=>item.Item).ToArray();
    }
}


