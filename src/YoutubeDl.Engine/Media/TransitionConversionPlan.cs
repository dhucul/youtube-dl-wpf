using System.Globalization;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Media;

internal sealed record TransitionVideoFacts(string Pixel,string Transfer,string Primaries,string Matrix,string Range,string Aspect,double Rotation,bool SimpleDisplay=true,bool AdvancedHdr=false);
internal sealed record TransitionAudioFacts(int Channels,int Rate,string Layout,int Bits);
internal static class TransitionConversionPlan
{
    internal static bool SimpleMatrix(string? text)
    {
        if(string.IsNullOrWhiteSpace(text))return true;
        var rows=Regex.Matches(text,@"(?m)^\s*[0-9a-fA-F]{8}:\s*(-?\d+)\s+(-?\d+)\s+(-?\d+)",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        if(rows.Count!=3)return false;var values=rows.SelectMany(row=>row.Groups.Cast<Group>().Skip(1).Select(group=>long.TryParse(group.Value,out var value)?value:long.MaxValue)).ToArray();
        return values[2]==0&&values[5]==0&&values[6]==0&&values[7]==0&&values[8]==1073741824&&new[]{values[0],values[1],values[3],values[4]}.All(value=>value is -65536 or 0 or 65536)&&values[0]*values[4]-values[1]*values[3]==4294967296L;
    }
    private static bool Aspect(string value)
    {
        var parts=value.Split(':');return parts.Length==2&&int.TryParse(parts[0],out var n)&&int.TryParse(parts[1],out var d)&&n>0&&d>0&&(double)n/d is >=0.25 and <=4;
    }
    internal static string VideoFilter(LiveDashSelectionPolicy policy,TransitionVideoFacts facts,TransitionVideoFacts first,int width)
    {
        if(!facts.SimpleDisplay||!double.IsFinite(facts.Rotation)||Math.Abs(facts.Rotation%90)>0.00001||Math.Abs(facts.Rotation)>360||!Aspect(facts.Aspect)||!policy.NormalizeVideoGeometry&&(facts.Rotation!=0||facts.Aspect!="1:1"))
            throw new NotSupportedException("Conversion requires a known bounded aspect ratio and a supported quarter-turn display matrix.");
        var color="";
        if(policy.TransitionColorPolicy is null)
        {
            if(facts.Pixel!="yuv420p"||facts.AdvancedHdr||facts.Transfer is not ("" or "unknown" or "bt709" or "smpte170m" or "gamma22" or "gamma28")||facts.Primaries.Contains("2020",StringComparison.Ordinal)||facts.Matrix.Contains("2020",StringComparison.Ordinal)||facts.Transfer!=first.Transfer||facts.Primaries!=first.Primaries||facts.Matrix!=first.Matrix||facts.Range is "tv" or "pc"&&first.Range is "tv" or "pc"&&facts.Range!=first.Range)
                throw new NotSupportedException("Changing color facts or high-bit-depth/HDR input requires an explicit color conversion policy.");
        }
        else if(policy.TransitionColorPolicy is "hdr-preserve" or "hdr-metadata")
        {
            if(facts.AdvancedHdr&&policy.TransitionColorPolicy!="hdr-metadata"||facts.Pixel is not ("yuv420p10le" or "yuv422p10le" or "yuv444p10le" or "yuv420p12le" or "yuv422p12le" or "yuv444p12le")||facts.Transfer is not ("smpte2084" or "arib-std-b67")||facts.Transfer!=first.Transfer||facts.Primaries!="bt2020"||facts.Matrix!="bt2020nc"||facts.Range is not ("tv" or "pc"))
                throw new NotSupportedException("HDR preservation requires stable PQ or HLG BT.2020 YUV input with known range and at least ten bits.");
            color="zscale=p=bt2020:t="+facts.Transfer+":m=bt2020nc:r=limited:d=error_diffusion,format=yuv420p10le,";
        }
        else if(policy.TransitionColorPolicy is "wide-to-bt709" or "bt2020")
        {
            var rgb=facts.Pixel is "rgb24" or "bgr24" or "rgb48le" or "bgr48le" or "gbrp" or "gbrp10le" or "gbrp12le" or "gbrp16le";
            var yuv=facts.Pixel is "yuv420p" or "yuv422p" or "yuv444p" or "yuv420p10le" or "yuv422p10le" or "yuv444p10le" or "yuv420p12le" or "yuv422p12le" or "yuv444p12le";
            if(facts.AdvancedHdr||(!rgb&&!yuv)||facts.Range is not ("tv" or "pc")||rgb&&(facts.Matrix!="gbr"||facts.Range!="pc")||yuv&&facts.Matrix is not ("bt709" or "bt470bg" or "smpte170m" or "bt2020nc")||facts.Primaries is not ("bt709" or "smpte170m" or "bt470bg" or "bt2020" or "smpte432")||facts.Transfer is not ("bt709" or "smpte170m" or "iec61966-2-1" or "bt2020-10" or "bt2020-12" or "gamma22" or "gamma28"))
                throw new NotSupportedException("Wide-gamut/RGB conversion requires explicitly signaled supported SDR primaries, transfer, matrix and range without alpha.");
            color=(rgb?"format=gbrp16le,":"")+(policy.TransitionColorPolicy=="bt2020"?"zscale=p=bt2020:t=bt2020-10:m=bt2020nc:r=limited:d=error_diffusion,format=yuv420p10le,":"zscale=p=bt709:t=bt709:m=bt709:r=limited:d=error_diffusion,format=yuv420p,")+"sidedata=mode=delete:type=MASTERING_DISPLAY_METADATA,sidedata=mode=delete:type=CONTENT_LIGHT_LEVEL,";
        }
        else
        {
            if(facts.AdvancedHdr||facts.Pixel is not ("yuv420p" or "yuv422p" or "yuv444p" or "yuvj420p" or "yuvj422p" or "yuvj444p" or "yuv420p10le" or "yuv422p10le" or "yuv444p10le" or "yuv420p12le" or "yuv422p12le" or "yuv444p12le")||facts.Range is not ("tv" or "pc"))throw new NotSupportedException("Color conversion requires known YUV/range facts without advanced HDR metadata.");
            var hdr=facts.Transfer is "smpte2084" or "arib-std-b67";
            if(hdr)
            {
                if(policy.TransitionColorPolicy!="hdr-to-bt709"||facts.Primaries!="bt2020"||facts.Matrix!="bt2020nc")throw new NotSupportedException("PQ/HLG input requires explicit BT.2020 HDR-to-SDR tone mapping.");
                color="zscale=t=linear:npl=100,format=gbrpf32le,tonemap=tonemap=mobius:param=0.3:desat=2:peak="+(policy.HdrPeakNits/100d).ToString(CultureInfo.InvariantCulture)+",zscale=p=bt709:t=bt709:m=bt709:r=limited:d=error_diffusion,format=yuv420p,";
            }
            else
            {
                if(facts.Transfer is not ("bt709" or "smpte170m")||facts.Primaries is not ("bt709" or "bt470bg" or "smpte170m")||facts.Matrix is not ("bt709" or "bt470bg" or "smpte170m"))throw new NotSupportedException("SDR color conversion requires explicitly signaled supported primaries, transfer and matrix.");
                color="zscale=p=bt709:t=bt709:m=bt709:r=limited:d=error_diffusion,format=yuv420p,";
            }
            color+="sidedata=mode=delete:type=MASTERING_DISPLAY_METADATA,sidedata=mode=delete:type=CONTENT_LIGHT_LEVEL,";
        }
        return color+"scale="+width+":"+policy.TransitionHeight+":force_original_aspect_ratio=decrease:force_divisible_by=2"+(policy.NormalizeVideoGeometry?":reset_sar=1":"")+",pad="+width+":"+policy.TransitionHeight+":(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30";
    }
    internal static (int Channels,int Rate,string Layout) AudioProfile(LiveDashSelectionPolicy policy,TransitionAudioFacts value,TransitionAudioFacts first)
    {
        var namedChannels=value.Layout switch{"mono"=>1,"stereo"=>2,"3.0"=>3,"quad" or "4.0"=>4,"5.0" or "5.0(side)"=>5,"5.1" or "5.1(side)"=>6,"6.1"=>7,"7.1"=>8,_=>0};
        if(value.Channels is <1 or >8||value.Channels!=namedChannels||value.Rate is <8000 or >192000||value.Bits is <0 or >32)throw new NotSupportedException("Audio conversion requires a supported named layout and sample rate.");
        if(policy.TransitionAudioLayout is { } layout)return(layout=="mono"?1:layout=="stereo"?2:6,policy.TransitionAudioSampleRate,layout);
        if(value.Channels>2||value.Channels!=first.Channels||value.Rate!=first.Rate||value.Layout!=first.Layout||value.Bits>24)throw new NotSupportedException("Audio layout/rate changes require an explicit fixed output layout and rate.");
        return(first.Channels,first.Rate,first.Layout);
    }
    internal static string AudioFilter(LiveDashSelectionPolicy policy,(int Channels,int Rate,string Layout) target)=>policy.TransitionAudioLayout is null?"":",aresample="+target.Rate+":out_chlayout="+target.Layout+":rematrix_maxval=1";
    internal static string VideoPixel(LiveDashSelectionPolicy policy)=>policy.TransitionColorPolicy is "hdr-preserve" or "hdr-metadata" or "bt2020"?"yuv420p10le":"yuv420p";
    internal static IReadOnlyList<string> VideoColor(LiveDashSelectionPolicy policy,string transfer)=>policy.TransitionColorPolicy is null?[]:
        ["-color_primaries",policy.TransitionColorPolicy is "hdr-preserve" or "hdr-metadata" or "bt2020"?"bt2020":"bt709","-color_trc",policy.TransitionColorPolicy is "hdr-preserve" or "hdr-metadata"?transfer:policy.TransitionColorPolicy=="bt2020"?"bt2020-10":"bt709","-colorspace",policy.TransitionColorPolicy is "hdr-preserve" or "hdr-metadata" or "bt2020"?"bt2020nc":"bt709","-color_range","tv"];
    internal static IReadOnlyList<string> VideoEncoder(string? codec)=>codec switch
    {
        "hevc"=>["-c:v","libx265","-preset","fast","-x265-params","lossless=1:pools=1:frame-threads=1:log-level=error","-tag:v","hvc1"],
        "vp9"=>["-c:v","libvpx-vp9","-lossless","1","-deadline","good","-cpu-used","4"],
        "av1"=>["-c:v","libaom-av1","-crf","0","-b:v","0","-cpu-used","8","-lag-in-frames","0"],
        _=>["-c:v","libx264","-preset","fast","-qp","0"]
    };
    internal static IReadOnlyList<string> AudioEncoder(string? codec,int channels)=>codec switch
    {
        "aac"=>["-c:a","aac","-b:a",channels>2?"512k":"256k"],
        "flac"=>["-c:a","flac","-strict","-2"],
        "opus"=>["-c:a","libopus","-b:a",channels>2?"384k":"128k","-strict","-2"],
        _=>["-c:a","copy"]
    };
}

