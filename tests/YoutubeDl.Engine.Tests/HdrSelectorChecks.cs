using System.Buffers.Binary;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Media;

internal static class HdrSelectorChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>new[]{"default","valid","unknown","wrong-policy","disabled","scope"}.Select(m=>("HDR selectors policy "+m,(Func<Task>)(()=>Policy(m))))
        .Concat(new[]{"id-one","id-fifteen","interleaved","replace","limited","missing","bad-crc","av1","av1-dependent","av1-footer","av1-missing","method-two"}.Select(m=>("HDR selectors compression "+m,(Func<Task>)(()=>Compression(m)))))
        .Concat(new[]{"hlg","linear","gamma22","gamma28","bt709","black","white","colored","peak","missing","unknown","nan","raster","cancel","hlg-low","hlg-high","hlg-400","hlg-2000","hlg-black"}.Select(m=>("HDR selectors transfer "+m,(Func<Task>)(()=>Transfer(m)))))
        .Concat(new[]{"identity","oblique","shear","reflect","crop","clipped","strict","projective-hard","projective-soft","unknown","three","collapsed"}.Select(m=>("HDR selectors envelope "+m,(Func<Task>)(()=>Envelope(m)))));
    private static void Check(bool v,string message)=>WriteCleanupChecks.Check(v,message);
    private static string Identity(byte[] nal)=>string.Join('|',DolbySpatialRpu.ContentIdentity(nal).Split('|').Select((part,i)=>i==0?part:string.Join(';',part.Split(';').Order(StringComparer.Ordinal))));
    private static Task Policy(string mode)
    {
        var policy=HdrReconstructChecks.Policy with{TransitionSelectorPolicy=mode=="default"?null:mode=="unknown"?"other":"ellipse-envelope"};if(mode=="wrong-policy")policy=policy with{TransitionColorPolicy="hdr-transform"};if(mode=="disabled")policy=policy with{NormalizeTransitions=false};var invalid=mode is "unknown" or "wrong-policy" or "disabled";try{policy.Validate();Check(!invalid,"Invalid authoring policy accepted.");if(mode=="scope"){var track=new YoutubeDl.Engine.Download.DashRepresentation{Id="v",Url=new("https://fixture.example/v"),MimeType="video/mp4",Codecs="hvc1",Width=160,Height=90};var request=new NativeDownloadRequest{Url=track.Url,OutputDirectory="unused",LiveDashSelection=policy};Check(new YoutubeDl.Engine.Download.DashTrackSelection(track,request).Key!=new YoutubeDl.Engine.Download.DashTrackSelection(track,request with{LiveDashSelection=policy with{TransitionSelectorPolicy=null}}).Key,"Authoring intent reused a strict recovery scope.");}}catch(ArgumentException)when(invalid){}return Task.CompletedTask;
    }
    internal static byte[] Obu(byte[] nal,bool badFooter=false)
    {
        var raw=HdrBits.Unescape(nal.AsSpan(3));var b=new HdrBits();foreach(var n in new[]{0xb5,0,0x3b,0,0,8,0})b.Put(n,8);b.Put(0x01be6841,27);b.Put(raw.Length,8);b.Put(0,1);foreach(var n in raw)b.Put(n,8);b.Put(badFooter?0:0x400,17);b.Align();var data=new byte[]{4}.Concat(b.Bytes()).ToArray();var size=new List<byte>();var length=data.Length;do{var next=length&127;length>>=7;size.Add((byte)(next|(length==0?0:128)));}while(length!=0);return new byte[]{0x2a}.Concat(size).Concat(data).ToArray();
    }
    private static Task Compression(string mode)
    {
        var invalid=mode is "limited" or "missing" or "bad-crc" or "av1-footer" or "av1-missing" or "method-two";try
        {
            var sequence=new DolbyRpuSequence(true,mode!="limited");var full=mode=="id-fifteen"?HdrSelectorFixtures.FullId15:HdrSelectorFixtures.FullId1;if(mode=="bad-crc")full[^3]^=1;if(mode=="method-two"){var bits=new HdrBits(HdrBits.Unescape(full.AsSpan(3))[..^5]);bits.Data[54]=true;full=Finish(bits);}if(mode is not ("missing" or "av1-missing"))sequence.Expand(full);
            byte[] output;if(mode.StartsWith("av1",StringComparison.Ordinal))output=Av1DolbyMetadata.Rpus(Obu(mode=="av1"?HdrSelectorFixtures.FullId1:HdrSelectorFixtures.RefId1,mode=="av1-footer"),sequence).Single();else output=sequence.Expand(mode=="id-fifteen"?HdrSelectorFixtures.RefId15:HdrSelectorFixtures.RefId1);Check(Identity(output)==Identity(HdrDomainFixtures.Independent),"Reference expansion failed to canonicalize mapping identity.");
            if(mode=="interleaved"){sequence.Expand(HdrSelectorFixtures.FullId15);Check(sequence.Expand(HdrSelectorFixtures.RefId1).SequenceEqual(output),"Interleaved mapping IDs lost their own state.");sequence.Expand(HdrSelectorFixtures.RefId15);}if(mode=="replace"){sequence.Expand(HdrSelectorFixtures.FullId1);Check(sequence.Expand(HdrSelectorFixtures.RefId1).SequenceEqual(output),"Mapping replacement drifted.");}Check(!invalid,"Invalid compression accepted.");
        }
        catch(Exception e)when(invalid&&e is InvalidDataException or NotSupportedException){return Task.CompletedTask;}Check(!invalid,"Invalid compression succeeded.");return Task.CompletedTask;
    }
    private static byte[] Finish(HdrBits bits){bits.Align();var data=bits.Bytes();var tail=new byte[5];BinaryPrimitives.WriteUInt32BigEndian(tail,DolbySpatialRpu.Crc(data));tail[4]=0x80;return new byte[]{0x7c,1,25}.Concat(HdrBits.Escape(data.Concat(tail))).ToArray();}
    private static Task Transfer(string mode)
    {
        if(mode is "hlg-low" or "hlg-high" or "hlg-400" or "hlg-2000" or "hlg-black")
        {
            var peak=mode=="hlg-low"?100:mode=="hlg-400"?400:mode=="hlg-2000"?2000:10000;var expected=mode=="hlg-low"?.8459066308929684:mode=="hlg-400"?1.0328651963577442:mode=="hlg-2000"?1.326432598178872:1.702315536266557;Check(Math.Abs(HdrSourceTransfer.HlgGamma(peak)-expected)<1e-12,"HLG nominal/extended reference gamma differs.");Span<double> black=stackalloc double[3];new HdrSourceTransfer("arib-std-b67",peak).ToPq(black);Check(black.ToArray().All(v=>double.IsFinite(v)&&v<.000001),"HLG extended-peak black produced invalid light.");return Task.CompletedTask;
        }
        var invalid=mode is "missing" or "unknown" or "nan" or "cancel";try
        {
            var trc=mode switch{"hlg"=>"arib-std-b67","unknown"=>"other","gamma22"=>"gamma22","gamma28"=>"gamma28","bt709"=>"bt709",_=>"linear"};var transfer=HdrSourceTransfer.Create(trc,new(mode=="missing"?null:"G(1,1)L(10000000,1)"))!;Span<double> value=stackalloc double[]{mode=="black"?0:mode=="white"?1:mode=="nan"?double.NaN:.5,.5,.5};if(mode=="black")value.Fill(0);if(mode=="white")value.Fill(1);if(mode=="colored"){value[0]=1;value[1]=.25;value[2]=0;}transfer.ToPq(value);Check(value.ToArray().All(double.IsFinite),"Non-PQ conversion produced nonfinite PQ.");if(mode=="white")Check(Math.Abs(value[0]-.751827096247041)<1e-10,"Declared 1000-nit white has wrong PQ value.");if(mode=="black")Check(value[0]<.000001,"Black did not remain black.");if(mode=="linear")Check(Math.Abs(value[0]-.6765848107833876)<1e-10,"Linear 500-nit anchor differs.");if(mode=="peak"){Span<double> other=stackalloc double[]{.5,.5,.5};new HdrSourceTransfer("linear",2000).ToPq(other);Check(other[0]>value[0],"Reference peak had no effect.");}if(mode is "raster" or "cancel"){var raster=new byte[4*4*6];for(var i=0;i<48;i++)BinaryPrimitives.WriteUInt16LittleEndian(raster.AsSpan(i*2),512);using var cancel=new CancellationTokenSource();if(mode=="cancel")cancel.Cancel();transfer.Convert(raster,4,4,cancel.Token);Check(BinaryPrimitives.ReadUInt16LittleEndian(raster)>512,"Linear raster conversion was not applied.");}Check(!invalid,"Invalid transfer succeeded.");
        }
        catch(Exception e)when(invalid&&e is InvalidDataException or NotSupportedException or OperationCanceledException){return Task.CompletedTask;}Check(!invalid,"Invalid transfer accepted.");return Task.CompletedTask;
    }
    internal static byte[] WindowPayload(bool hard=false,bool clipped=false,int windows=2)
    {
        var payload=new HdrBits(HdrSpatialChecks.PlusPayload(windows,128,96));for(var i=1;i<windows;i++){var b=new HdrBits();foreach(var v in new[]{16+i*2,8,112,88,64+i*3,48})b.Put(v,16);b.Put(30,8);foreach(var v in new[]{hard?20:10,clipped?70:20,12})b.Put(v,16);b.Put(1,1);for(var n=0;n<153;n++)payload.Data[58+(i-1)*153+n]=b.Data[n];}return payload.Bytes();
    }
    private static Task Envelope(string mode)
    {
        var invalid=mode is "clipped" or "strict" or "projective-soft" or "unknown" or "collapsed";try
        {
            var matrix=mode=="oblique"?HdrReconstructChecks.Matrix(.866,.5,-.5,.866):mode=="reflect"?HdrReconstructChecks.Matrix(-1,.2,0,1):mode is "shear" or "strict"?HdrReconstructChecks.Matrix(1,.3,.2,1):mode.StartsWith("projective",StringComparison.Ordinal)?HdrReconstructChecks.Matrix(1,0,0,1,2097152):null;var g=HdrAffineCanvas.Create(128,96,mode=="collapsed"?2:160,mode=="collapsed"?2:90,"1:1",matrix,mode=="crop"?"24,12,24,12":null);var payload=WindowPayload(mode=="projective-hard",mode=="clipped",mode=="three"?3:2);var mapped=HdrPlusHistogram.Map(payload,g,mode=="strict"?null:mode=="unknown"?"other":"ellipse-envelope");var windows=HdrPlusHistogram.Windows(mapped,g.Width,g.Height);Check(windows.Count==(mode=="three"?3:2)&&windows.Skip(1).All(w=>w.Outer>=w.Inner&&w.Minor>0),"Envelope lost selectors or ellipse axes.");Check(!invalid,"Invalid selector accepted.");
        }
        catch(Exception e)when(invalid&&e is InvalidDataException or NotSupportedException){return Task.CompletedTask;}Check(!invalid,"Invalid selector succeeded.");return Task.CompletedTask;
    }
}
