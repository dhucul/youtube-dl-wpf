namespace YoutubeDl.Engine.Media;

// Convert a custom PQ signal into the canonical HDR10-compatible Dolby domain.
// This happens after reshaping/residual composition and before raster transforms.
internal sealed record DolbySignalDomain(double[] Nonlinear,double[] Offset,double[] Linear)
{
    internal static readonly int[] CanonicalNonlinear=[9574,0,13802,9574,-1540,-5348,9574,17610,0];
    internal static readonly uint[] CanonicalOffset=[16777216,134217728,134217728];
    internal static readonly int[] CanonicalLinear=[7222,8771,390,2654,12430,1300,0,422,15962];
    private static readonly double[] InverseNonlinear=Inverse(CanonicalNonlinear.Select(n=>n/8192d).ToArray());
    private static readonly double[] InverseLinear=Inverse(CanonicalLinear.Select(n=>n/16384d).ToArray());
    internal static DolbySignalDomain? Create(short[] nonlinear,uint[] offsets,short[] linear,int eotf,int p0,int p1,uint p2,int depth,int color,int chroma,int range)
    {
        if(eotf!=65535||p0!=0||p1!=0||p2!=0||depth!=12||color!=0||chroma!=0||range!=1)throw new NotSupportedException("Custom Dolby signal reconstruction requires twelve-bit PQ YCbCr with declared standard range semantics.");
        if(nonlinear.Select(n=>(int)n).SequenceEqual(CanonicalNonlinear)&&offsets.SequenceEqual(CanonicalOffset)&&linear.Select(n=>(int)n).SequenceEqual(CanonicalLinear))return null;
        var n=nonlinear.Select(v=>v/8192d).ToArray();var o=offsets.Select(v=>v/268435456d).ToArray();var l=linear.Select(v=>v/16384d).ToArray();if(o.Any(v=>v is <0 or >1))throw new NotSupportedException("Dolby signal offsets exceed the normalized signal domain.");Inverse(n);Inverse(l);return new(n,o,l);
    }
    internal void Convert(Span<double> signal)
    {
        if(signal.Length!=3||!double.IsFinite(signal[0]+signal[1]+signal[2])||signal[0] is <0 or >1||signal[1] is <0 or >1||signal[2] is <0 or >1)throw new InvalidDataException("Invalid custom Dolby signal sample.");Span<double> v=stackalloc double[3];for(var i=0;i<3;i++)v[i]=signal[i]-Offset[i];Multiply(Nonlinear,v,signal);for(var i=0;i<3;i++)signal[i]=Decode(signal[i]);Multiply(Linear,signal,v);Multiply(InverseLinear,v,signal);for(var i=0;i<3;i++)signal[i]=Encode(signal[i]);Multiply(InverseNonlinear,signal,v);for(var i=0;i<3;i++){var value=v[i]+CanonicalOffset[i]/268435456d;if(!double.IsFinite(value))throw new InvalidDataException("Nonfinite custom Dolby reconstruction.");signal[i]=Math.Clamp(value,0,1);}
    }
    private static void Multiply(double[] m,ReadOnlySpan<double> x,Span<double> y){for(var i=0;i<3;i++)y[i]=m[i*3]*x[0]+m[i*3+1]*x[1]+m[i*3+2]*x[2];}
    private static double Decode(double value){var p=Math.Pow(Math.Clamp(value,0,1),32d/2523);return Math.Pow(Math.Max(p-3424d/4096,0)/(2413d/128-2392d/128*p),16384d/2610);}
    private static double Encode(double value){var p=Math.Pow(Math.Clamp(value,0,1),2610d/16384);return Math.Pow((3424d/4096+2413d/128*p)/(1+2392d/128*p),2523d/32);}
    private static double[] Inverse(double[] m)
    {
        var a=m[0];var b=m[1];var c=m[2];var d=m[3];var e=m[4];var f=m[5];var g=m[6];var h=m[7];var i=m[8];var det=a*(e*i-f*h)-b*(d*i-f*g)+c*(d*h-e*g);if(!double.IsFinite(det)||Math.Abs(det)<0.00001)throw new NotSupportedException("Singular or ill-conditioned Dolby signal matrix.");var result=new[]{e*i-f*h,c*h-b*i,b*f-c*e,f*g-d*i,a*i-c*g,c*d-a*f,d*h-e*g,b*g-a*h,a*e-b*d}.Select(v=>v/det).ToArray();if(result.Any(v=>Math.Abs(v)>64))throw new NotSupportedException("Dolby signal matrix inverse exceeds its conditioning bound.");return result;
    }
}
