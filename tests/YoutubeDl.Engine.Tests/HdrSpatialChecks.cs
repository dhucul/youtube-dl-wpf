using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Media;

internal static class HdrSpatialChecks
{
    internal static LiveDashSelectionPolicy Policy=>HdrMetadataChecks.PolicyValue with{TransitionColorPolicy="hdr-spatial"};
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
        new[]{"valid","codec","disabled","peak","identity","profile5","profile10","enhancement"}.Select(mode=>("HDR spatial policy "+mode,(Func<Task>)(()=>PolicyCheck(mode))))
        .Concat(new[]{"identity","up","down","pad","area","negative","bounds","uniform","chroma","plus-pad","window-bound","radius-bound"}.Select(mode=>("HDR spatial geometry "+mode,(Func<Task>)(()=>Geometry(mode)))))
        .Concat(new[]{"one","two","three","scale","invalid-count","version","bounds","angle","ellipse","truncated"}.Select(mode=>("HDR spatial Plus window "+mode,(Func<Task>)(()=>Plus(mode)))))
        .Concat(new[]{"profile8","profile84","cmv40","add","scale","padding","crc","short","wrapper","outside"}.Select(mode=>("HDR spatial Dolby RPU "+mode,(Func<Task>)(()=>Rpu(mode)))))
        .Concat(new[]{"fraction","metadata","geometry","unknown","truncated","size","extension","header"}.Select(mode=>("HDR spatial transport "+mode,(Func<Task>)(()=>Transport(mode)))));
    private static void Check(bool value,string message)=>WriteCleanupChecks.Check(value,message);
    private static Task PolicyCheck(string mode)
    {
        if(mode is "profile5" or "profile10" or "enhancement"){var cfg=new TransitionDolbyConfiguration(mode=="profile5"?5:10,mode=="profile5"?0:1,1,1,mode=="enhancement"?1:0,1,0,"none");try{cfg.Validate("smpte2084",true);Check(mode!="enhancement","Enhancement-layer configuration succeeded.");}catch(NotSupportedException)when(mode=="enhancement"){}return Task.CompletedTask;}
        var policy=Policy;policy=mode switch{"codec"=>policy with{TransitionVideoCodec="vp9"},"disabled"=>policy with{NormalizeTransitions=false},"peak"=>policy with{HdrPeakNits=1000},_=>policy};if(mode=="identity"){var track=new DashRepresentation{Id="v",Url=new("https://fixture.example/v"),MimeType="video/mp4",Codecs="hvc1",Width=160,Height=90};var request=new NativeDownloadRequest{Url=track.Url,OutputDirectory="unused",LiveDashSelection=policy};Check(new DashTrackSelection(track,request).Key!=new DashTrackSelection(track,request with{LiveDashSelection=policy with{TransitionColorPolicy="hdr-metadata"}}).Key,"Spatial intent reused a metadata-only recovery scope.");return Task.CompletedTask;}
        try{policy.Validate();Check(mode=="valid","Invalid spatial policy succeeded.");}catch(ArgumentException)when(mode!="valid"){return Task.CompletedTask;}Check(mode=="valid","Invalid spatial policy was accepted.");return Task.CompletedTask;
    }
    private static Task Geometry(string mode)
    {
        var invalid=mode is "negative" or "bounds" or "uniform" or "chroma" or "plus-pad" or "window-bound" or "radius-bound";
        try
        {
            var g=mode switch{"down"=>HdrSpatialGeometry.Create(320,180,160,90,false),"pad" or "area" or "plus-pad"=>HdrSpatialGeometry.Create(128,96,320,180,mode=="plus-pad"),"negative"=>HdrSpatialGeometry.Create(-1,90,320,180,false),"bounds"=>HdrSpatialGeometry.Create(10000,90,320,180,false),"uniform"=>HdrSpatialGeometry.Create(127,72,320,180,false),"chroma"=>HdrSpatialGeometry.Create(126,96,320,180,false),_=>HdrSpatialGeometry.Create(160,90,mode=="identity"?160:320,mode=="identity"?90:180,false)};
            if(mode=="window-bound")g.X(161);if(mode=="radius-bound")g.Radius(161);Check(!invalid,"Unsupported HDR geometry succeeded.");if(mode=="area")Check(g.ActiveArea(8,8,4,4)==(55,55,8,8),"Dolby active-area offsets did not include centered padding.");if(mode=="up")Check(g.X(80)==160&&g.Y(45)==90&&g.Radius(20)==40,"Uniform window/ellipse scaling drifted.");if(mode=="identity")Check(g.Identity&&!g.Padding,"Identity geometry changed its canvas.");
        }
        catch(Exception e)when(invalid&&e is InvalidDataException or NotSupportedException){return Task.CompletedTask;}Check(!invalid,"Invalid HDR geometry did not fail.");return Task.CompletedTask;
    }
    internal static byte[] PlusPayload(int windows=2,int width=160,int height=90)
    {
        var bits=new HdrBits();foreach(var b in new byte[]{0xb5,0,0x3c,0,1,4,1})bits.Put(b,8);bits.Put(windows,2);for(var i=1;i<windows;i++){foreach(var value in new[]{16,8,width-16,height-8,width/2,height/2})bits.Put(value,16);bits.Put(30,8);foreach(var value in new[]{20,30,15})bits.Put(value,16);bits.Put(1,1);}bits.Put(1000,27);bits.Put(0,1);for(var i=0;i<windows;i++){for(var c=0;c<3;c++)bits.Put(100000,17);bits.Put(50000,17);bits.Put(0,4);bits.Put(0,10);}bits.Put(0,1);for(var i=0;i<windows;i++){bits.Put(0,1);bits.Put(0,1);}bits.Align();return bits.Bytes();
    }
    private static Task Plus(string mode)
    {
        var input=PlusPayload(mode=="one"?1:mode=="three"?3:2);var invalid=mode is "invalid-count" or "version" or "bounds" or "angle" or "ellipse" or "truncated";
        if(mode=="invalid-count")input[7]&=0x3f;if(mode=="version")input[6]=2;if(mode=="truncated")input=input[..10];
        if(mode is "bounds" or "angle" or "ellipse"){var bits=new HdrBits(input);var at=mode=="bounds"?58:mode=="angle"?58+96:58+104;var value=new HdrBits();value.Put(mode=="bounds"?200:mode=="angle"?181:40,mode=="angle"?8:16);for(var i=0;i<value.Data.Count;i++)bits.Data[at+i]=value.Data[i];input=bits.Bytes();}
        try{var g=HdrSpatialGeometry.Create(160,90,mode=="scale"?320:160,mode=="scale"?180:90,true);var mapped=HdrPlusSpatial.Map(input,g);Check(!invalid,"Malformed HDR10+ window succeeded.");if(mode=="scale"){var bits=new HdrBits(mapped);bits.Skip(58);Check(bits.Read(16)==32&&bits.Read(16)==16,"HDR10+ corners did not scale in source coordinates.");}else Check(mapped.SequenceEqual(input),"Identity HDR10+ mapping changed non-spatial payload bits.");}
        catch(Exception e)when(invalid&&e is InvalidDataException or NotSupportedException){return Task.CompletedTask;}Check(!invalid,"Invalid window metadata succeeded.");return Task.CompletedTask;
    }
    internal static byte[] Nal(byte[] fixture)=>new byte[]{0x7c,1}.Concat(fixture[4..]).ToArray();
    private static Task Rpu(string mode)
    {
        var nal=Nal(mode=="profile84"?DolbyMetadataFixtures.Profile84:mode=="cmv40"?DolbyMetadataFixtures.Cmv40:DolbyMetadataFixtures.Profile8);var invalid=mode is "crc" or "short" or "wrapper" or "outside";if(mode=="crc")nal[^3]^=1;if(mode=="short")nal=nal[..8];if(mode=="wrapper")nal[2]=24;
        try{var g=mode=="cmv40"?HdrSpatialGeometry.Create(3840,2160,160,90,false):mode is "add" or "padding"?HdrSpatialGeometry.Create(128,96,320,180,false):HdrSpatialGeometry.Create(160,90,320,180,false);if(mode=="outside")g.ActiveArea(160,1,0,0);var mapped=DolbySpatialRpu.Map(nal,g);Check(!invalid,"Malformed Dolby geometry succeeded.");var area=DolbySpatialRpu.Area(mapped);if(mode is "add" or "padding")Check(area==(40,40,0,0),"A padded Dolby picture did not gain an active-area record.");else Check(mapped.Length>0,"Dolby remapping lost its RPU.");}
        catch(Exception e)when(invalid&&e is InvalidDataException or NotSupportedException){return Task.CompletedTask;}Check(!invalid,"Invalid Dolby data succeeded.");return Task.CompletedTask;
    }
    private static Task Transport(string mode)
    {
        if(mode is "metadata" or "geometry" or "fraction"){var a=new HdrFrameMetadata(new(),"<side_data><side_datum key='num_windows' value='2'/><side_datum key='center_of_ellipse_x' value='80'/></side_data>",null);var b=a with{Plus=a.Plus!.Replace(mode=="metadata"?"value='2'":"value='80'",mode=="metadata"?"value='3'":"value='160'")};Check(HdrSpatialMetadata.Equivalent(a,b)==(mode!="metadata"),"Spatial comparison ignored non-spatial metadata or retained coordinate text.");return Task.CompletedTask;}
        var invalid=mode is "truncated" or "size" or "extension" or "header";var packet=mode switch{"truncated"=>new byte[]{0x2a},"size"=>new byte[]{0x2a,0xff,0xff,0xff,0xff,0x7f},"extension"=>new byte[]{0x2e,0x20,0},"header"=>new byte[]{0xaa,0},_=>new byte[]{0x12,0}};try{Check(Av1DolbyMetadata.Rpus(packet).Count==0&&!invalid,"Malformed AV1 wrapper succeeded.");}catch(Exception e)when(invalid&&e is InvalidDataException or NotSupportedException){return Task.CompletedTask;}Check(!invalid,"Invalid AV1 metadata did not fail.");return Task.CompletedTask;
    }
}
