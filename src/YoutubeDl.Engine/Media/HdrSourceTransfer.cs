using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Media;

// An explicit reconstruction uses the declared mastering maximum as its source
// reference white/peak. No nominal luminance is guessed for relative signals.
internal sealed record HdrSourceTransfer(string Transfer,double PeakNits)
{
    internal static HdrSourceTransfer? Create(string transfer,TransitionHdrMetadata metadata)
    {
        if(transfer=="smpte2084")return null;
        if(transfer is not ("arib-std-b67" or "linear" or "bt709" or "gamma22" or "gamma28" or "bt470m" or "bt470bg"))throw new NotSupportedException("Unsupported non-PQ reconstruction transfer function.");
        var match=Regex.Match(metadata.MasterDisplay??"",@"L\((\d+),(\d+)\)\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));if(!match.Success||!long.TryParse(match.Groups[1].Value,NumberStyles.None,CultureInfo.InvariantCulture,out var maximum)||maximum is <1000000 or >100000000)throw new NotSupportedException("Non-PQ reconstruction requires a declared mastering peak from 100 to 10000 nits.");return new(transfer,maximum/10000d);
    }
    internal void Convert(Span<byte> yuv444,int width,int height,CancellationToken ct=default)
    {
        var pixels=checked(width*height);if(pixels is <=0 or >7680*4320||yuv444.Length!=pixels*6)throw new InvalidDataException("Invalid non-PQ reconstruction raster.");Span<double> signal=stackalloc double[3];
        for(var i=0;i<pixels;i++)
        {
            if((i&16383)==0)ct.ThrowIfCancellationRequested();var y=BinaryPrimitives.ReadUInt16LittleEndian(yuv444.Slice(i*2));var u=BinaryPrimitives.ReadUInt16LittleEndian(yuv444.Slice((pixels+i)*2));var v=BinaryPrimitives.ReadUInt16LittleEndian(yuv444.Slice((pixels*2+i)*2));if((y|u|v)>1023)throw new InvalidDataException("Non-PQ source exceeds ten-bit precision.");var l=(y-64d)/876;var cb=(u-512d)/896;var cr=(v-512d)/896;signal[0]=l+1.4746*cr;signal[2]=l+1.8814*cb;signal[1]=(l-.2627*signal[0]-.0593*signal[2])/.678;ToPq(signal);var yy=.2627*signal[0]+.678*signal[1]+.0593*signal[2];var uu=(signal[2]-yy)/1.8814;var vv=(signal[0]-yy)/1.4746;Put(yuv444,i,64+876*yy);Put(yuv444,pixels+i,512+896*uu);Put(yuv444,pixels*2+i,512+896*vv);
        }
    }
    private static void Put(Span<byte> raster,int at,double value)=>BinaryPrimitives.WriteUInt16LittleEndian(raster.Slice(at*2),(ushort)Math.Clamp(Math.Round(value,MidpointRounding.AwayFromZero),0,1023));
    internal void ToPq(Span<double> rgb)
    {
        if(rgb.Length!=3||PeakNits is <100 or >10000||!double.IsFinite(PeakNits))throw new InvalidDataException("Invalid non-PQ reference domain.");
        for(var c=0;c<3;c++){if(!double.IsFinite(rgb[c]))throw new InvalidDataException("Nonfinite non-PQ sample.");var v=Math.Clamp(rgb[c],0,1);rgb[c]=Transfer switch{"linear"=>v,"gamma22" or "bt470m"=>Math.Pow(v,2.2),"gamma28" or "bt470bg"=>Math.Pow(v,2.8),"bt709"=>v<.081?v/4.5:Math.Pow((v+.099)/1.099,1/.45),"arib-std-b67"=>v<=.5?v*v/3:(Math.Exp((v-.55991073)/.17883277)+.28466892)/12,_=>throw new NotSupportedException("Unsupported source transfer.")};}
        var scale=PeakNits/10000;if(Transfer=="arib-std-b67"){var luminance=.2627*rgb[0]+.678*rgb[1]+.0593*rgb[2];var gamma=HlgGamma(PeakNits);scale*=luminance==0?0:Math.Pow(luminance,gamma-1);}for(var c=0;c<3;c++){var p=Math.Pow(Math.Clamp(rgb[c]*scale,0,1),2610d/16384);rgb[c]=Math.Pow((3424d/4096+2413d/128*p)/(1+2392d/128*p),2523d/32);}
    }
    internal static double HlgGamma(double peak)=>peak is >=400 and <=2000?1.2+.42*Math.Log10(peak/1000):1.2*Math.Pow(1.111,Math.Log2(peak/1000));
}
