using System.Buffers.Binary;
using System.Net;
using System.IO.Compression;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Facebook;

internal static class FacebookFlowChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All => new[]{"facts","worst","h264","fps","height","best-mixed","tail","ignored-range","redirect","foreign","range","entity","truncated","oversized","protected","cancel","variable-fps","renewal-facts",
        "fallback-dash-forbidden","fallback-dash-missing","fallback-dash-gone","fallback-sd","fallback-unavailable","fallback-renewal","fallback-selected","fallback-dash-invalid","fallback-dash-cancel",
        "renewal-recovery-asset","renewal-recovery-facts","renewal-recovery-newfacts","renewal-new-dash","renewal-concurrent","renewal-cancel-waiter",
        "silent-facts","compressed-range","silent-progressive","silent-dash","silent-progressive-renewal","silent-dash-renewal","silent-audio-change","silent-dash-audio-change","dash-audio-disappears","silent-dash-mixed-audio",
        "complete-best","complete-worst","complete-video","complete-audio","complete-height","complete-codec","complete-explicit","complete-selector","complete-paired","complete-silent",
        "inventory-width","inventory-width-selector","inventory-width-renewal"}
        .Select(mode=>("Practical Facebook flow "+mode,(Func<Task>)(()=>Check(mode))));
    private static async Task Check(string mode)
    {
        if(mode.StartsWith("inventory-",StringComparison.Ordinal)){await Inventory(mode);return;}
        if(mode.StartsWith("complete-",StringComparison.Ordinal)){await Complete(mode);return;}
        if(mode.StartsWith("silent-",StringComparison.Ordinal)&&mode!="silent-facts"||mode=="dash-audio-disappears"){await Silent(mode);return;}
        if(mode.StartsWith("fallback-",StringComparison.Ordinal)){await Fallback(mode);return;}
        if(mode.StartsWith("renewal-",StringComparison.Ordinal)&&mode!="renewal-facts"){await Renewal(mode);return;}
        if(mode=="renewal-facts")
        {
            using var files=new ExecSponsorChecks.Files();var fixture=new PracticalFacebookChecks.Fixture{Mode="probe-renewal-change"};using var owner=fixture.Http();
            var issued=await fixture.Provider.ExtractAsync(fixture.Request(files.Directory),owner,default);using var clean=owner.CreatePublicServiceSession();using var playback=clean.WithAuthorization(issued.PlaybackAuthorization!);
            await ProviderFamiliesChecks.Expect<InvalidDataException>(()=>new FileDownloader(playback).DownloadAsync(issued.Media.Single(format=>format.Id=="hd").Url,Path.Combine(files.Directory,"movie.mp4"),null,null,default,0));
            PracticalFacebookChecks.Assert(fixture.Generation==2&&fixture.Transfers==0,"Changed MP4 facts were accepted during renewal.");return;
        }
        var movie=Movie(358,mode=="tail",mode=="protected",mode=="variable-fps",audio:mode!="silent-facts");var requests=0;long received=0;var calls=0;
        using var cancel=new CancellationTokenSource();
        using var http=new MediaHttp(new Handler((request,ct)=>
        {
            ct.ThrowIfCancellationRequested();requests++;
            PracticalFacebookChecks.Assert(request.Headers.Referrer?.Host=="www.facebook.com"&&request.Headers.Authorization is null,"Movie probe lost public header isolation.");
            if(mode is "redirect" or "foreign"&&requests==1)return new(HttpStatusCode.Redirect){Headers={Location=new(mode=="foreign"?"https://other.example/movie.mp4":"https://video.next.fbcdn.net/movie.mp4")}};
            if(mode=="cancel"){cancel.Cancel();ct.ThrowIfCancellationRequested();}
            var offered=movie;
            if(mode=="compressed-range"&&!request.Headers.AcceptEncoding.Any(value=>value.Value=="identity"))
            {
                using var packed=new MemoryStream();using(var gzip=new GZipStream(packed,CompressionMode.Compress,true))gzip.Write(movie);offered=packed.ToArray();
            }
            var range=request.Headers.Range!.Ranges.Single();var start=range.From!.Value;var end=Math.Min(range.To!.Value,offered.Length-1);
            var data=mode=="ignored-range"?offered:offered[(int)start..((int)end+1)];calls++;
            if(mode=="oversized"&&start==24){data=Box("moov",new byte[8]);BinaryPrimitives.WriteUInt32BigEndian(data,9*1024*1024);data=data[..8];}
            if(mode=="truncated")data=[];
            var content=new StreamContent(new CountingStream(data,count=>received+=count));content.Headers.ContentLength=data.Length;
            if(!ReferenceEquals(offered,movie))content.Headers.ContentEncoding.Add("gzip");
            if(mode!="ignored-range")content.Headers.ContentRange=new(mode=="range"?start+1:start,mode=="range"?end+1:end,mode=="oversized"?10*1024*1024:offered.Length);
            var response=new HttpResponseMessage(mode=="ignored-range"?HttpStatusCode.OK:HttpStatusCode.PartialContent){Content=content};
            response.Headers.ETag=new(mode=="entity"&&calls>1?"\"changed\"":"\"stable\"");return response;
        }),new(){AllowAutoRedirect=false});
        async Task<FacebookMp4Probe.Facts> Probe()=>await FacebookMp4Probe.ReadAsync(http,new("https://video.owned.fbcdn.net/movie.mp4"),new("https://www.facebook.com/reel/10153231379946729/"),cancel.Token);
        if(mode is "foreign" or "range" or "entity" or "truncated" or "oversized" or "protected" or "cancel")
        {
            if(mode=="cancel")await ProviderFamiliesChecks.Expect<OperationCanceledException>(async()=>await Probe());
            else await ProviderFamiliesChecks.Expect<Exception>(async()=>await Probe());return;
        }
        var facts=await Probe();
        PracticalFacebookChecks.Assert(facts.Height==358&&facts.Width==476&&facts.VideoCodec=="avc1"&&facts.HasAudio==(mode!="silent-facts")&&facts.AudioCodec==(mode=="silent-facts"?null:"mp4a")&&facts.FramesPerSecond==(mode=="variable-fps"?60:30)&&facts.FileSize==movie.Length,"MP4 movie facts changed.");
        PracticalFacebookChecks.Assert(requests<=6&&received<2048,"Movie probe fetched payloads or repeated without bounds.");
        var formats=new List<MediaCandidate>
        {
            new(){Id="sd",Url=new("https://video.owned.fbcdn.net/sd.mp4"),Kind=MediaKind.Progressive,HasVideo=true,HasAudio=true,IsCompleteMedia=true,Height=facts.Height,Width=facts.Width,VideoCodec=facts.VideoCodec,AudioCodec=facts.AudioCodec,FramesPerSecond=facts.FramesPerSecond,Extension="mp4"},
            new(){Id="dash-low",Url=new("https://video.owned.fbcdn.net/low.mp4"),Kind=MediaKind.Progressive,HasVideo=true,HasAudio=false,Height=286,VideoCodec="av01",FramesPerSecond=30,Extension="mp4"},
            new(){Id="dash-audio",Url=new("https://video.owned.fbcdn.net/audio.mp4"),Kind=MediaKind.Progressive,HasVideo=false,HasAudio=true,AudioCodec="mp4a",Extension="m4a"}
        };
        var page=new PageMetadata{Media=formats};var request=new NativeDownloadRequest{Url=new("https://www.facebook.com/reel/10153231379946729/"),OutputDirectory=Path.GetTempPath()};
        if(mode=="worst")request=request with{Quality=QualityPreference.Worst};
        if(mode=="h264")request=request with{VideoCodec="h264"};
        if(mode=="fps")request=request with{FormatId="sd",MaximumFrameRate=30};
        if(mode=="height")request=request with{MaximumHeight=360};
        if(mode is "worst" or "h264" or "fps" or "height" or "best-mixed")
        {
            var plan=MediaDownloadPlan.Create(page,request);
            PracticalFacebookChecks.Assert(plan.Primary.Id==(mode=="worst"?"dash-low":"sd")&&plan.NeedsMerge==(mode=="worst"),"Measured progressive/DASH selection lost quality or constraints.");
        }
    }
    private static async Task Inventory(string mode)
    {
        using var files=new ExecSponsorChecks.Files();var fixture=new PracticalFacebookChecks.Fixture{Mode="dash-width-renewal"};using var owner=fixture.Http();var request=fixture.Request(files.Directory);
        var page=await fixture.Provider.ExtractAsync(request,owner,default);var video=page.Media.Single(format=>format.Id=="dash-video");
        if(mode=="inventory-width")
        {
            PracticalFacebookChecks.Assert(video.Width==1280&&video.Height==720,"Facebook lost the publisher's complete video dimensions.");return;
        }
        if(mode=="inventory-width-selector")
        {
            var plan=MediaDownloadPlan.Create(page,request with{FormatSelector="bestvideo[width>=1000]+bestaudio"});
            PracticalFacebookChecks.Assert(plan.Primary.Id==video.Id&&plan.Audio?.Id=="dash-audio","A declared-width selection lost Facebook's video/audio pair.");return;
        }
        var authorization=page.PlaybackAuthorization!;var first=await authorization.AuthorizeAsync(video.Url,default);var identity=authorization.RecoveryIdentity(video.Url);
        await ProviderFamiliesChecks.Expect<InvalidDataException>(()=>authorization.RenewAsync(video.Url,first.Generation,default));
        PracticalFacebookChecks.Assert(await authorization.AuthorizeAsync(video.Url,default)==first&&authorization.RecoveryIdentity(video.Url)==identity,"A changed video width partially committed playback renewal.");
        var recovered=await authorization.RenewAsync(video.Url,first.Generation,default);
        PracticalFacebookChecks.Assert(recovered.Generation==2&&fixture.Generation==3&&authorization.Canonicalize(recovered.Url)==video.Url,"A rejected dimension change poisoned a later valid renewal.");
    }
    private static async Task Silent(string mode)
    {
        using var files=new ExecSponsorChecks.Files();var fixture=new PracticalFacebookChecks.Fixture{Mode=mode};using var owner=fixture.Http();var request=fixture.Request(files.Directory);
        var page=await fixture.Provider.ExtractAsync(request,owner,default);
        if(mode=="silent-dash-mixed-audio")
        {
            PracticalFacebookChecks.Assert(page.Media.Any(row=>row.HasAudio==true)&&page.Media.Where(row=>row.Id!.StartsWith("dash-",StringComparison.Ordinal)).All(row=>!row.IsCompleteMedia),"An incomplete DASH offer hid audio available in a complete movie.");
            var full=MediaDownloadPlan.Create(page,request);
            PracticalFacebookChecks.Assert(full.Primary.Id=="hd"&&!full.NeedsMerge&&full.Primary.HasAudio==true,"Automatic playback ignored a complete Facebook file with audio.");return;
        }
        var plan=MediaDownloadPlan.Create(page,request);
        var changed=mode is "silent-audio-change" or "silent-dash-audio-change" or "dash-audio-disappears";
        if(mode!="dash-audio-disappears")
        {
            PracticalFacebookChecks.Assert(!plan.NeedsMerge&&plan.Primary.HasAudio==false&&plan.Primary.IsCompleteMedia,"A complete silent Facebook video required or invented audio.");
            await ProviderFamiliesChecks.Expect<Exception>(()=>Task.FromResult(MediaDownloadPlan.Create(page,request with{AudioOnly=true})));
        }
        if(changed||mode.EndsWith("-renewal",StringComparison.Ordinal))
        {
            var authorization=page.PlaybackAuthorization!;var first=await authorization.AuthorizeAsync(plan.Primary.Url,default);
            if(changed)
            {
                await ProviderFamiliesChecks.Expect<InvalidDataException>(()=>authorization.RenewAsync(plan.Primary.Url,first.Generation,default));
                PracticalFacebookChecks.Assert(await authorization.AuthorizeAsync(plan.Primary.Url,default)==first,"Changed audio requirements partially committed a renewal.");
            }
            else PracticalFacebookChecks.Assert((await authorization.RenewAsync(plan.Primary.Url,first.Generation,default)).Generation==2,"Silent Facebook playback did not renew.");
            PracticalFacebookChecks.Assert(fixture.Generation==2,"Silent renewal escaped its expected generation.");
        }
        PracticalFacebookChecks.Assert(fixture.Transfers==0,"Silent-video inspection transferred payloads.");
    }
    private static async Task Complete(string mode)
    {
        MediaCandidate Format(string id,int height,string codec,bool audio,bool complete)=>new(){Id=id,Url=new("https://video.owned.fbcdn.net/"+id+".mp4"),Kind=MediaKind.Progressive,Extension="mp4",Height=height,VideoCodec=codec,HasVideo=true,HasAudio=audio,IsCompleteMedia=complete};
        var formats=new List<MediaCandidate>{Format("hd",546,"avc1",mode!="complete-silent",true),Format("sd",358,"avc1",mode!="complete-silent",true),Format("dash-high",720,"av01",false,false),Format("dash-low",144,"av01",false,false)};
        if(mode=="complete-paired")formats.Add(new(){Id="audio",Url=new("https://video.owned.fbcdn.net/audio.m4a"),Kind=MediaKind.Progressive,Extension="m4a",HasVideo=false,HasAudio=true,AudioCodec="mp4a"});
        var page=new PageMetadata{Media=formats};var request=new NativeDownloadRequest{Url=new("https://www.facebook.com/reel/12345/"),OutputDirectory=Path.GetTempPath()};
        request=mode switch
        {
            "complete-worst"=>request with{Quality=QualityPreference.Worst},"complete-video"=>request with{VideoOnly=true},"complete-audio"=>request with{AudioOnly=true},
            "complete-height"=>request with{MaximumHeight=360},"complete-codec"=>request with{VideoCodec="av1"},"complete-explicit"=>request with{FormatId="dash-high"},
            "complete-selector"=>request with{FormatSelector="dash-high"},_=>request
        };
        if(mode is "complete-codec" or "complete-explicit")
        {
            await ProviderFamiliesChecks.Expect<NotSupportedException>(()=>Task.FromResult(MediaDownloadPlan.Create(page,request)));return;
        }
        var plan=MediaDownloadPlan.Create(page,request);
        var expected=mode is "complete-video" or "complete-selector" or "complete-paired"?"dash-high":mode is "complete-worst" or "complete-height"?"sd":"hd";
        PracticalFacebookChecks.Assert(plan.Primary.Id==expected&&plan.NeedsMerge==(mode=="complete-paired"),"Complete playback selection violated quality, constraints or explicit intent.");
    }
    private static async Task Renewal(string mode)
    {
        using var files=new ExecSponsorChecks.Files();var fixture=new PracticalFacebookChecks.Fixture{Mode=mode};using var owner=fixture.Http();
        var page=await fixture.Provider.ExtractAsync(fixture.Request(files.Directory),owner,default);var authorization=page.PlaybackAuthorization!;
        var url=page.Media.Single(row=>row.Id=="hd").Url;var first=await authorization.AuthorizeAsync(url,default);var identity=authorization.RecoveryIdentity(url);
        if(mode is "renewal-concurrent" or "renewal-cancel-waiter")
        {
            fixture.RenewalEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);fixture.RenewalRelease=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending=authorization.RenewAsync(url,first.Generation,default);
            await fixture.RenewalEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                using var cancel=new CancellationTokenSource();
                var waiting=Enumerable.Range(0,8).Select(_=>authorization.RenewAsync(url,first.Generation,mode=="renewal-cancel-waiter"?cancel.Token:default)).ToArray();
                if(mode=="renewal-cancel-waiter")
                {
                    cancel.Cancel();foreach(var task in waiting)await ProviderFamiliesChecks.Expect<OperationCanceledException>(()=>task);
                }
                fixture.RenewalRelease.TrySetResult();var renewed=await pending;
                if(mode=="renewal-concurrent")PracticalFacebookChecks.Assert((await Task.WhenAll(waiting)).All(value=>value==renewed),"Concurrent Facebook failures refreshed more than one generation.");
                var stale=await authorization.RenewAsync(url,first.Generation,default);
                PracticalFacebookChecks.Assert(stale==renewed&&fixture.Generation==2&&renewed.Generation==2,"A canceled waiter or stale failure reopened Facebook discovery.");
            }
            finally{fixture.RenewalRelease.TrySetResult();}
        }
        else
        {
            var generation=first.Generation;
            if(mode.StartsWith("renewal-recovery-",StringComparison.Ordinal))
            {
                await ProviderFamiliesChecks.Expect<InvalidDataException>(()=>authorization.RenewAsync(url,generation,default));
                PracticalFacebookChecks.Assert(await authorization.AuthorizeAsync(url,default)==first,"A rejected Facebook proposal replaced the accepted generation.");
            }
            else generation=(await authorization.RenewAsync(url,generation,default)).Generation;
            var recovered=await authorization.RenewAsync(url,generation,default);
            PracticalFacebookChecks.Assert(fixture.Generation==3&&recovered.Url.Query.EndsWith("g=3",StringComparison.Ordinal)&&recovered.Generation==(mode=="renewal-new-dash"?3:2),"A rejected or unused DASH proposal poisoned subsequent progressive renewal.");
        }
        PracticalFacebookChecks.Assert(authorization.RecoveryIdentity(url)==identity&&authorization.Canonicalize((await authorization.AuthorizeAsync(url,default)).Url)==url&&fixture.Transfers==0,"Facebook renewal changed recovery identity or downloaded payloads.");
    }
    private static async Task Fallback(string mode)
    {
        using var files=new ExecSponsorChecks.Files();using var cancel=new CancellationTokenSource();
        var fixture=new PracticalFacebookChecks.Fixture{Mode=mode,CancelProbe=mode=="fallback-dash-cancel"?cancel:null};using var owner=fixture.Http();var request=fixture.Request(files.Directory);
        if(mode=="fallback-unavailable")
        {
            await ProviderFamiliesChecks.Expect<HttpRequestException>(()=>fixture.Provider.ExtractAsync(request,owner,default));return;
        }
        if(mode=="fallback-dash-invalid")
        {
            await ProviderFamiliesChecks.Expect<InvalidDataException>(()=>fixture.Provider.ExtractAsync(request,owner,default));return;
        }
        if(mode=="fallback-dash-cancel")
        {
            await ProviderFamiliesChecks.Expect<OperationCanceledException>(()=>fixture.Provider.ExtractAsync(request,owner,cancel.Token));return;
        }
        var page=await fixture.Provider.ExtractAsync(request,owner,default);
        if(mode is "fallback-renewal" or "fallback-selected")
        {
            var format=page.Media.Single(row=>row.Id==(mode=="fallback-selected"?"hd":"dash-video"));
            using var clean=owner.CreatePublicServiceSession();using var playback=clean.WithAuthorization(page.PlaybackAuthorization!);
            var transfer=new FileDownloader(playback).DownloadAsync(format.Url,Path.Combine(files.Directory,"movie.mp4"),null,null,default,0);
            if(mode=="fallback-selected")await ProviderFamiliesChecks.Expect<InvalidDataException>(()=>transfer);
            else{await transfer;PracticalFacebookChecks.Assert(File.ReadAllBytes(Path.Combine(files.Directory,"movie.mp4")).SequenceEqual(fixture.Bytes),"DASH renewal changed media after unused progressive files disappeared.");}
            PracticalFacebookChecks.Assert(fixture.Generation==2&&fixture.Transfers==(mode=="fallback-selected"?0:1),"Unavailable files reopened or bypassed bounded renewal.");return;
        }
        var selected=MediaDownloadPlan.Create(page,request);
        PracticalFacebookChecks.Assert(selected.Primary.Id==(mode=="fallback-sd"?"sd":"dash-video")&&page.Warnings.Count==(mode=="fallback-sd"?1:2),"Unavailable progressive files blocked valid offered playback or lost their warning.");
        await ProviderFamiliesChecks.Expect<Exception>(()=>Task.FromResult(MediaDownloadPlan.Create(page,request with{FormatId="hd"})));
        PracticalFacebookChecks.Assert(fixture.Transfers==0,"Inspection downloaded a movie payload.");
    }
    internal static byte[] Movie(int height,bool tail=false,bool protectedVideo=false,bool variableFps=false,bool audio=true)
    {
        static byte[] Number(uint value){var bytes=new byte[4];BinaryPrimitives.WriteUInt32BigEndian(bytes,value);return bytes;}
        static byte[] Track(string role,byte[] entry,bool variable)
        {
            var clock=new byte[24];BinaryPrimitives.WriteUInt32BigEndian(clock.AsSpan(12),15360);BinaryPrimitives.WriteUInt32BigEndian(clock.AsSpan(16),46080);
            var handler=new byte[12];Encoding.ASCII.GetBytes(role).CopyTo(handler,8);
            var timing=variable?new uint[]{0,2,45,512,90,256}:new uint[]{0,1,90,512};
            return Box("trak",Box("mdia",Box("mdhd",clock),Box("hdlr",handler),Box("minf",Box("stbl",Box("stsd",new byte[4],Number(1),entry),Box("stts",timing.SelectMany(Number).ToArray())))));
        }
        var visual=new byte[78];BinaryPrimitives.WriteUInt16BigEndian(visual.AsSpan(24),height==546?(ushort)728:(ushort)476);BinaryPrimitives.WriteUInt16BigEndian(visual.AsSpan(26),(ushort)height);
        var tracks=new List<byte[]>{Track("vide",Box(protectedVideo?"encv":"avc1",visual),variableFps)};
        if(audio)tracks.Add(Track("soun",Box("mp4a",new byte[28]),false));
        var moov=Box("moov",tracks.ToArray());
        var ftyp=Box("ftyp",Encoding.ASCII.GetBytes("isom0000isom0000"));var body=Box("mdat",new byte[tail?1024*1024:4096]);
        return tail?ftyp.Concat(body).Concat(moov).ToArray():ftyp.Concat(moov).Concat(body).ToArray();
    }
    private static byte[] Box(string type,params byte[][] parts)
    {
        var bytes=new byte[8+parts.Sum(part=>part.Length)];BinaryPrimitives.WriteUInt32BigEndian(bytes,(uint)bytes.Length);Encoding.ASCII.GetBytes(type).CopyTo(bytes,4);var offset=8;
        foreach(var part in parts){part.CopyTo(bytes,offset);offset+=part.Length;}return bytes;
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,HttpResponseMessage> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(send(request,ct));}
    private sealed class CountingStream(byte[] bytes,Action<int> received):MemoryStream(bytes)
    {public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default){var count=await base.ReadAsync(buffer,ct);received(count);return count;}}
}
