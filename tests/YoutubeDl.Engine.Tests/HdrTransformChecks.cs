using YoutubeDl.Engine;
using YoutubeDl.Engine.Media;

internal static class HdrTransformChecks
{
    internal static LiveDashSelectionPolicy Policy=>HdrSpatialChecks.Policy with{TransitionColorPolicy="hdr-transform"};
    internal static byte[] Mel=>HdrSpatialChecks.Nal(Convert.FromBase64String("AAAAARkICQhAYTZQrwA/+AH/wA/8AB//oAAAEAAADQAAAwCAAABoAAAEAAADAABAAAAgAAAgAAADAAQAAAMCAAADAgAAAwAAQAAAIAAAIAAANErMAABr1ErN8/nWOErMiZQAAAMCAAADABAAAAMAEAAAAwA4bESGAwwUvGEcCigAAAMDTHy1//4AAAMAAAMAAAMAAMBA+5wCowAwCAFpuZmAwCghgAgAgAW2hUEAAgIAAAMACAUAAAMAAAMAAAMAEgwfQABkAAADAAD4/5emgA=="));
    internal static byte[] Fel=>HdrSpatialChecks.Nal(Convert.FromBase64String("AAAAARkICQhAYTZQrwA/+AH/wA/8AB//oAAAEAAADQAAAwCAAABoAAAEAAADAAFAAAAgAAAgAAADAAQAAAMCAAADAgAAAwAAQAAAIAAAIAAANErMAABr1ErN8/nWOErMiZQAAAMCAAADABAAAAMAEAAAAwA4bESGAwwUvGEcCigAAAMDTHy1//4AAAMAAAMAAAMAAMBA+5wCowAwCAFpuZmAwCghgAgAgAW2hUEAAgIAAAMACAUAAAMAAAMAAAMAEgwfQABkAAADAACspoVRgA=="));
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>new[]{"valid","codec","disabled","peak"}.Select(m=>("HDR transform policy "+m,(Func<Task>)(()=>PolicyCheck(m))))
        .Concat(new[]{"identity","padding","anamorphic","fractional","clockwise","counterclockwise","halfturn","oblique","negative-angle","large","bad-aspect","bad-dimensions","bad-angle","inverted","rectangle","point","ellipse","ellipse-degenerate","active","inset","collapse"}.Select(m=>("HDR transform canvas "+m,(Func<Task>)(()=>Canvas(m)))))
        .Concat(new[]{"identity","quarter","oblique","shear","reflection","perspective","disagreement"}.Select(m=>("HDR transform matrix "+m,(Func<Task>)(()=>Matrix(m)))))
        .Concat(new[]{"identity","padding","two-pad","three-pad","anamorphic","quarter","oblique","oblique-window","percentiles","full-percentiles","bright","bad-percentiles","truncated","trailing","version","count","edge","bad-max","zero-axis","display-grid","grid-pad","grid-quarter"}.Select(m=>("HDR transform Plus "+m,(Func<Task>)(()=>Plus(m)))))
        .Concat(new[]{"convert","content","fel","crc","truncated","header","missing-layer","duplicate-rpu","layer-id"}.Select(m=>("HDR transform MEL "+m,(Func<Task>)(()=>MelCheck(m)))));
    private static void Check(bool value,string message)=>WriteCleanupChecks.Check(value,message);
    private static Task PolicyCheck(string mode){var p=mode switch{"codec"=>Policy with{TransitionVideoCodec="vp9"},"disabled"=>Policy with{NormalizeTransitions=false},"peak"=>Policy with{HdrPeakNits=1000},_=>Policy};try{p.Validate();Check(mode=="valid","Invalid HDR transform policy succeeded.");}catch(ArgumentException)when(mode!="valid"){}return Task.CompletedTask;}
    private static Task Canvas(string mode)
    {
        var invalid=mode is "large" or "bad-aspect" or "bad-dimensions" or "bad-angle" or "inverted" or "rectangle" or "point" or "inset" or "collapse";
        try
        {
            var g=HdrCanvasTransform.Create(mode=="bad-dimensions"?-1:mode is "padding" or "fractional"?126:160,mode is "padding" or "fractional"?96:90,320,180,mode=="bad-aspect"?"0:1":mode is "anamorphic" or "ellipse"?"2:1":"1:1",mode switch{"bad-angle"=>double.NaN,"clockwise"=>-90,"counterclockwise"=>90,"halfturn"=>180,"oblique" or "rectangle" or "inset"=>30,"negative-angle"=>-17,_=>0});
            if(mode=="large")HdrCanvasTransform.Create(8000,90,320,180,"1:1",0);if(mode=="inverted")g.Rectangle(20,10,10,20,true);if(mode=="rectangle")g.Rectangle(0,0,160,90,true);if(mode=="point")g.Point(161,20);if(mode=="inset")g.ActiveArea(4,4,4,4);if(mode=="collapse")g.ActiveArea(160,0,0,0);
            if(mode=="identity")Check(g.Point(80,45)==(160,90)&&Math.Abs(g.ContentFraction-1)<1e-12,"Identity transform drifted.");if(mode=="padding")Check(g.ContentFraction<1&&g.Left>=0&&g.ScaledWidth%2==0,"Padding ratio was lost.");if(mode=="counterclockwise")Check(g.Point(0,0).Y>g.Point(160,0).Y,"Rotation direction was reversed.");if(mode=="ellipse"){var e=g.Ellipse(30,10,20,8);Check(e.Outer>e.Minor&&e.Inner<=e.Outer&&e.Angle is >=0 and <180,"Anamorphic ellipse became invalid.");}if(mode=="ellipse-degenerate")Check(g.Ellipse(0,0,0,0)==(0,0,0,0),"Degenerate ellipse changed.");if(mode=="active")Check(g.ActiveArea(8,8,4,4)==(16,16,8,8),"Active area changed.");Check(!invalid,"Invalid HDR transform geometry succeeded.");
        }
        catch(Exception e)when(invalid&&e is NotSupportedException or InvalidDataException){return Task.CompletedTask;}Check(!invalid,"Invalid canvas was accepted.");return Task.CompletedTask;
    }
    internal static byte[] Payload(int windows=1,int width=128,int height=96,int count=3,bool bad=false,int bright=400)
    {
        var b=new HdrBits(HdrSpatialChecks.PlusPayload(windows,width,height));var stats=58+(windows-1)*153+28;var prefix=b.Data.Take(stats+51).ToList();var o=new HdrBits();o.Data.AddRange(prefix);o.Put(50000,17);o.Put(count,4);for(var i=0;i<count;i++){o.Put(bad?50:(i+1)*100/count,7);o.Put((i+1)*100000/count,17);}o.Put(bright,10);o.Put(0,1);for(var i=0;i<windows;i++){o.Put(0,1);o.Put(0,1);}o.Align();return o.Bytes();
    }
    private static Task Matrix(string mode)
    {
        var invalid=mode is "shear" or "reflection" or "perspective" or "disagreement";var values=mode is "oblique" or "disagreement"?new[]{56756,-32768,0,32768,56756,0,0,0,1073741824}:mode=="quarter"?new[]{0,-65536,0,65536,0,0,0,0,1073741824}:new[]{65536,0,0,0,65536,0,0,0,1073741824};if(mode=="shear")values[3]=32768;if(mode=="reflection")values[0]=-65536;if(mode=="perspective")values[2]=1;var matrix=string.Join('\n',Enumerable.Range(0,3).Select(row=>row.ToString("x8")+": "+string.Join(' ',values.Skip(row*3).Take(3))));
        try{var g=HdrCanvasTransform.Create(128,72,160,90,"1:1",mode=="quarter"?90:mode=="oblique"?29:0,matrix);Check(!invalid,"Unsupported display matrix succeeded.");if(mode=="oblique")Check(g.Degrees>29.99&&g.Degrees<30.01,"Integer-truncated probe angle replaced matrix precision.");}catch(Exception e)when(invalid&&e is InvalidDataException or NotSupportedException){return Task.CompletedTask;}Check(!invalid,"Invalid display matrix was accepted.");return Task.CompletedTask;
    }
    private static Task Plus(string mode)
    {
        if(mode=="zero-axis")
        {
            var small=new HdrBits(HdrSpatialChecks.PlusPayload(2,128,96));var axes=new HdrBits();for(var i=0;i<3;i++)axes.Put(1,16);for(var i=0;i<48;i++)small.Data[58+104+i]=axes.Data[i];
            try{HdrPlusTransform.Map(small.Bytes(),HdrCanvasTransform.Create(128,96,16,16,"1:1",0));throw new Exception("Subpixel HDR10+ axes became zero-length metadata.");}catch(NotSupportedException){}
        }
        var invalid=mode is "oblique-window" or "full-percentiles" or "bad-percentiles" or "truncated" or "trailing" or "version" or "count" or "bad-max" or "zero-axis";var windows=mode is "two-pad" or "oblique-window" or "quarter" or "anamorphic" or "edge" or "zero-axis"?2:mode=="three-pad"?3:1;var payload=windows==1?Payload(count:mode=="full-percentiles"?15:3,bad:mode=="bad-percentiles"):HdrSpatialChecks.PlusPayload(windows,128,96);if(mode=="truncated")payload=payload[..8];if(mode=="trailing")payload=payload.Concat(new byte[]{255}).ToArray();if(mode=="version")payload[6]=2;if(mode=="count")payload[7]&=63;
        if(mode is "bad-max" or "zero-axis" or "edge"){var b=new HdrBits(payload);var change=new HdrBits();change.Put(mode=="bad-max"?131071:mode=="edge"?127:0,mode=="bad-max"?17:16);var at=mode=="bad-max"?86:mode=="edge"?58+32:58+104;for(var i=0;i<change.Data.Count;i++)b.Data[at+i]=change.Data[i];payload=b.Bytes();}
        if(mode is "display-grid" or "grid-pad" or "grid-quarter"){var b=new HdrBits(payload);b.Data.RemoveRange(243,b.Data.Count-243);var grid=new HdrBits();grid.Put(1,1);grid.Put(2,5);grid.Put(3,5);foreach(var value in new[]{1,2,3,4,5,6})grid.Put(value,4);b.Data.RemoveAt(85);b.Data.InsertRange(85,grid.Data);while(b.Data.Count%8!=0)b.Data.Add(false);payload=b.Bytes();}
        try
        {
            var identity=mode is "identity" or "display-grid";var g=HdrCanvasTransform.Create(128,96,identity?128:320,identity?96:180,mode=="anamorphic"?"2:1":"1:1",mode is "quarter" or "grid-quarter"?90:mode is "oblique" or "oblique-window"?30:0);var result=HdrPlusTransform.Map(payload,g);Check(!invalid,"Invalid HDR10+ transform succeeded.");if(identity)Check(result.SequenceEqual(payload),"Identity changed statistics.");if(mode.StartsWith("grid-",StringComparison.Ordinal))Check(new HdrBits(result).Data.Skip(85).Take(35).SequenceEqual(new HdrBits(payload).Data.Skip(85).Take(35)),"Picture geometry changed display calibration grids.");else if(windows==1&&!identity){var bits=new HdrBits(result);bits.Skip(58+28+51);Check(bits.Read(17)==HdrCanvasTransform.Round(50000*g.ContentFraction),"Padded average did not include black area.");var count=bits.Read(4);Check(count>=3,"CDF knots were lost.");}
        }
        catch(Exception e)when(invalid&&e is InvalidDataException or NotSupportedException){return Task.CompletedTask;}Check(!invalid,"Invalid HDR10+ statistics were accepted.");return Task.CompletedTask;
    }
    private static Task MelCheck(string mode)
    {
        var invalid=mode is not ("convert" or "content");var rpu=mode=="fel"?Fel:Mel;if(mode=="crc")rpu[^3]^=1;if(mode=="truncated")rpu=rpu[..8];if(mode=="header")rpu[2]=24;
        try
        {
            var result=mode is "missing-layer" or "duplicate-rpu" or "layer-id"?Mp4MelBridge.SampleBytes(mode=="missing-layer"?[rpu]:mode=="duplicate-rpu"?[rpu,rpu,new byte[]{126,1,2,1}]:[rpu,new byte[]{126,9,2,1}]):DolbyMelRpu.ToSingleLayer(rpu);Check(!invalid,"Invalid MEL source succeeded.");if(mode=="content")Check(DolbySpatialRpu.ContentIdentity(result)==DolbySpatialRpu.ContentIdentity(HdrSpatialChecks.Nal(DolbyMetadataFixtures.Profile8)),"MEL conversion changed mapping/display metadata.");else Check(DolbySpatialRpu.Area(result)==DolbySpatialRpu.Area(HdrSpatialChecks.Nal(DolbyMetadataFixtures.Profile8)),"MEL conversion changed active areas.");
        }
        catch(Exception e)when(invalid&&e is InvalidDataException or NotSupportedException){return Task.CompletedTask;}Check(!invalid,"Invalid MEL source was accepted.");return Task.CompletedTask;
    }
}
