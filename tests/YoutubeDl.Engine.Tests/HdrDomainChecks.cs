using System.Buffers.Binary;
using YoutubeDl.Engine.Media;

internal static class HdrDomainChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>new[]{"independent","dependent","compressed","chain","missing","reset","crc","truncated","compression","static-refresh","failed-state","custom","linear","custom-fel","singular-signal","signal-eotf","container","signal-header"}.Select(m=>("HDR domains Dolby "+m,(Func<Task>)(()=>Dolby(m))))
        .Concat(new[]{"two","three","inclusive","different","reflect","quarter","anisotropic","perspective-window","crop-window","empty","nan","cancel"}.Select(m=>("HDR domains windows "+m,(Func<Task>)(()=>Windows(m)))))
        .Concat(new[]{"perspective","horizon","inset","translated","raster","cancel"}.Select(m=>("HDR domains projective "+m,(Func<Task>)(()=>Perspective(m)))));
    private static void Check(bool v,string message)=>WriteCleanupChecks.Check(v,message);
    private static Task Dolby(string mode)
    {
        var invalid=mode is "missing" or "reset" or "crc" or "truncated" or "compression" or "singular-signal" or "signal-eotf" or "container" or "signal-header";try
        {
            if(mode is "custom" or "linear" or "custom-fel"){var p=DolbyFelProgram.Read(mode=="custom-fel"?HdrDomainFixtures.CustomFel:mode=="linear"?HdrDomainFixtures.CustomLinear:HdrDomainFixtures.CustomSingle,mode!="custom-fel");Check(p.Signal is not null,"Custom PQ matrices were ignored.");Check(DolbyFelProgram.Read(p.SingleLayerRpu,true).Signal is null,"Custom signal was retained after baking.");Span<double> sample=stackalloc double[]{.5,.5,.5};p.Signal!.Convert(sample);Check(sample.ToArray().All(double.IsFinite)&&Math.Abs(sample[0]-.5)>.00001,"Custom signal conversion did not affect pixels.");return Task.CompletedTask;}
            if(mode is "singular-signal" or "signal-eotf"){DolbySignalDomain.Create(new short[9],DolbySignalDomain.CanonicalOffset,DolbySignalDomain.CanonicalLinear.Select(v=>(short)v).ToArray(),mode=="signal-eotf"?0:65535,0,0,0,12,0,0,1);throw new Exception("Unsupported signal succeeded.");}
            if(mode=="signal-header"){var bits=new HdrBits(HdrBits.Unescape(HdrDomainFixtures.Independent.AsSpan(3))[..^5]);for(var i=0;i<16;i++)bits.Data[292+384+i]=false;var seq=new DolbyRpuSequence();var full=seq.Expand(Finish(bits));Check(seq.NeedsSignalReconstruction,"A changed signal EOTF was ignored.");DolbyFelProgram.Read(full,true);throw new Exception("Unsupported EOTF accepted.");}
            var sequence=new DolbyRpuSequence(mode!="container");if(mode is not ("reset" or "missing"))Check(sequence.Expand(HdrDomainFixtures.Independent).SequenceEqual(HdrDomainFixtures.Independent),"Independent RPU changed.");var input=mode=="dependent"?HdrDomainFixtures.Dependent:mode=="missing"?HdrDomainFixtures.MissingReference:HdrDomainFixtures.Compressed;
            if(mode=="crc")input[^3]^=1;if(mode=="truncated")input=input[..8];if(mode=="compression"){var bits=new HdrBits(HdrBits.Unescape(input.AsSpan(3))[..^5]);bits.Data[53]=true;input=Finish(bits);}
            if(mode=="failed-state"){var bad=HdrDomainFixtures.Compressed;bad[^3]^=1;try{sequence.Expand(bad);throw new Exception("Bad CRC accepted.");}catch(InvalidDataException){}}
            var output=sequence.Expand(input);var canonical=DolbyFelProgram.Read(output,true);Check(canonical.Signal is null&&DolbySpatialRpu.Area(output)==DolbySpatialRpu.Area(HdrDomainFixtures.Independent),"Expanded signal/active area differs.");Check(Blocks(output).Order(StringComparer.Ordinal).SequenceEqual(Blocks(HdrDomainFixtures.Independent).Order(StringComparer.Ordinal)),"Static/dynamic extension inheritance differs.");
            if(mode=="chain")for(var i=0;i<100;i++)Check(sequence.Expand(HdrDomainFixtures.Compressed).SequenceEqual(output),"Dependent chain drifted.");if(mode=="static-refresh"){sequence.Expand(HdrDomainFixtures.CustomSingle);Check(sequence.NeedsSignalReconstruction,"Static signal refresh was not observed.");Check(DolbyFelProgram.Read(sequence.Expand(HdrDomainFixtures.Compressed),true).Signal is not null,"Compressed frame lost the refreshed signal.");}Check(!invalid,"Invalid sequence succeeded.");
        }
        catch(Exception e)when(invalid&&e is InvalidDataException or NotSupportedException){return Task.CompletedTask;}Check(!invalid,"Invalid Dolby sequence was accepted.");return Task.CompletedTask;
    }
    private static string[] Blocks(byte[] nal)
    {
        var bits=new HdrBits(HdrBits.Unescape(nal.AsSpan(3))[..^5]);bits.Skip(801);var count=bits.Ue();bits.AlignRead();var blocks=new List<string>();for(var i=0;i<count;i++){var size=bits.Ue();var level=bits.Read(8);var bytes=new byte[size];for(var j=0;j<size;j++)bytes[j]=(byte)bits.Read(8);blocks.Add(level+":"+Convert.ToHexString(bytes));}return blocks.ToArray();
    }
    private static byte[] Finish(HdrBits bits){bits.Align();var body=bits.Bytes();var tail=new byte[5];BinaryPrimitives.WriteUInt32BigEndian(tail,DolbySpatialRpu.Crc(body));tail[4]=0x80;return new byte[]{0x7c,1,25}.Concat(HdrBits.Escape(body.Concat(tail))).ToArray();}
    private static Task Windows(string mode)
    {
        var invalid=mode is "perspective-window" or "crop-window" or "empty" or "nan" or "cancel";try
        {
            var input=HdrSpatialChecks.PlusPayload(mode=="three"?3:2,128,96);var matrix=mode=="reflect"?HdrReconstructChecks.Matrix(-1,0,0,1):mode=="quarter"?HdrReconstructChecks.Matrix(0,-1,1,0):mode=="anisotropic"?HdrReconstructChecks.Matrix(1.5,0,0,1):mode=="perspective-window"?HdrReconstructChecks.Matrix(1,0,0,1,1048576):null;var g=HdrAffineCanvas.Create(128,96,160,90,"1:1",matrix,mode=="crop-window"?"32,0,0,0":null);var mapped=HdrPlusHistogram.Map(input,g);var windows=HdrPlusHistogram.Windows(mapped,160,90);Check(windows.Count==(mode=="three"?3:2),"HDR window inventory changed.");var raster=HdrReconstructChecks.FloatRaster(Enumerable.Range(0,160*90).Select(i=>i%160<80?.25f:.75f).ToArray());if(mode=="nan")BinaryPrimitives.WriteInt32LittleEndian(raster,BitConverter.SingleToInt32Bits(float.NaN));using var cancel=new CancellationTokenSource();if(mode=="cancel")cancel.Cancel();var w=mode=="empty"?new HdrPlusWindow(2,0,1,0):new HdrPlusWindow(0,0,79,89);var left=HdrPixelHistogram.Measure(raster,160,90,w,cancel.Token);var right=HdrPixelHistogram.Measure(raster,160,90,new(80,0,159,89));Check(left.Average==25000&&right.Average==75000&&left.Bright==1000,"Inclusive region statistics differ.");var stats=windows.Select(region=>HdrPixelHistogram.Measure(raster,160,90,region)).ToArray();var output=HdrPlusHistogram.Regenerate(mapped,stats);Check(HdrPlusHistogram.Windows(output,160,90).SequenceEqual(windows),"Measured output changed selector geometry.");if(mode=="different")Check(stats[0].Bins.Aggregate(0UL,(s,n)=>s+n)!=stats[1].Bins.Aggregate(0UL,(s,n)=>s+n),"Inset statistics accidentally used the full picture.");Check(!invalid,"Invalid window succeeded.");
        }
        catch(Exception e)when(invalid&&e is InvalidDataException or NotSupportedException or OperationCanceledException){return Task.CompletedTask;}Check(!invalid,"Invalid window was accepted.");return Task.CompletedTask;
    }
    private static Task Perspective(string mode)
    {
        var invalid=mode is "horizon" or "inset" or "cancel";try
        {
            var matrix=HdrReconstructChecks.Matrix(1,0,0,1,mode=="horizon"?-536870912:67108864);if(mode=="translated")matrix=matrix.Replace("00000002: 0 0","00000002: 655360 -655360");var g=HdrAffineCanvas.Create(4,4,8,8,"1:1",matrix,null);var p=g.Point(4,4);Check(p.X>0&&p.X<=8&&p.Y>0&&p.Y<=8,"Perspective corners exceed the fitted canvas.");if(mode=="inset")g.ActiveArea(1,0,0,0);using var cancel=new CancellationTokenSource();if(mode=="cancel")cancel.Cancel();var raster=new byte[4*4*6];for(var i=0;i<48;i++)BinaryPrimitives.WriteUInt16LittleEndian(raster.AsSpan(i*2),512);var output=new byte[8*8*6];g.Render(raster,output,cancel.Token);Check(output.Any(b=>b!=0),"Perspective raster was empty.");Check(!invalid,"Invalid perspective succeeded.");
        }
        catch(Exception e)when(invalid&&e is InvalidDataException or NotSupportedException or OperationCanceledException){return Task.CompletedTask;}Check(!invalid,"Invalid perspective was accepted.");return Task.CompletedTask;
    }
}
