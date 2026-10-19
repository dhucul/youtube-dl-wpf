using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Protocols;
using YoutubeDl.Engine.Protocols.Sabr;
using YoutubeDl.Engine.Providers;
using System.Net;
internal static class BroaderSessionChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>new[]{"dispose","waiting","waiting-renewal","waiting-manifest","late","extractor","live","playlist","failed","stale"}.Select(mode=>("Publication session issuer "+mode,(Func<Task>)(()=>Lease(mode))))
        .Concat([("Publication session SABR partial controls discarded",(Func<Task>)Sabr)])
        .Concat(new[]{"initial","renewal"}.Select(mode=>("Publication session discovery "+mode,(Func<Task>)(()=>Discovery(mode)))))
        .Concat(new[]{"authorize-dispose","renewal-dispose","caller-cancel","inner-dispose","issuer-failure"}.Select(mode=>("Publication session handoff "+mode,(Func<Task>)(()=>Handoff(mode)))))
        .Concat(new[]{"active","unused","caption"}.Select(mode=>("Publication session renewal address "+mode,(Func<Task>)(()=>RenewalAddress(mode)))))
        .Concat(new[]{"near","expired","failed","complete"}.Select(mode=>("Publication session renewal expiry "+mode,(Func<Task>)(()=>RenewalExpiry(mode)))))
        .Concat(new[]{"renewal","send","failure","redirect-renewal","late-response","successful-alias"}.Select(mode=>("Publication session HTTP "+mode,(Func<Task>)(()=>Http(mode)))))
        .Concat(new[]{"preflight","canonical","redirect","unrelated"}.Select(mode=>("Publication session HTTP cancellation boundary "+mode,(Func<Task>)(()=>HttpCancellationBoundary(mode)))));
    private static async Task HttpCancellationBoundary(string mode)
    {
        var page=Page(0);var url=page.Media[0].Url;var alias=new Uri("https://cdn.example/redirect.mp4");var calls=0;
        using var lease=new RenewableMediaAuthorization(page,_=>Task.FromResult(Page(1)),_=>true);using var cancel=new CancellationTokenSource();
        var failure=new ObjectDisposedException("Unrelated preflight failure");
        void Boundary(string step)
        {
            if(step!=mode&&!(mode=="unrelated"&&step=="preflight"))return;
            if(mode=="unrelated")throw failure;
            cancel.Cancel();lease.Dispose();
        }
        using var owner=AdaptiveAccountChecks.Http((_,_)=>{calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect){Headers={Location=alias}});});
        using var authorized=owner.WithAuthorization(new CancellationBoundaryAuthorization(lease,Boundary));using var request=new HttpRequestMessage(HttpMethod.Get,url);
        var pending=authorized.SendAsync(request,cancel.Token);
        if(mode=="unrelated")
        {
            try{using var unexpected=await pending;throw new InvalidOperationException("A failed preflight completed.");}catch(ObjectDisposedException exception)when(ReferenceEquals(exception,failure)){}
        }
        else await ProviderFamiliesChecks.Expect<OperationCanceledException>(()=>pending);
        AdaptiveRecoveryChecks.Assert(calls==(mode=="redirect"?1:0)&&request.RequestUri==url,"Canceled HTTP authorization sent another request or lost its canonical address.");
    }
    private sealed class CancellationBoundaryAuthorization(IPlaybackAuthorization inner,Action<string> boundary):IPlaybackAuthorization
    {
        public bool AppliesTo(Uri url){boundary("preflight");return inner.AppliesTo(url);}
        public Uri Canonicalize(Uri url){boundary("canonical");return inner.Canonicalize(url);}
        public Task<PlaybackLease> AuthorizeAsync(Uri url,CancellationToken ct)=>inner.AuthorizeAsync(url,ct);
        public Task<PlaybackLease> RenewAsync(Uri url,long generation,CancellationToken ct)=>inner.RenewAsync(url,generation,ct);
        public bool TryRedirect(Uri source,Uri destination){boundary("redirect");return inner.TryRedirect(source,destination);}
    }
    private static async Task RenewalExpiry(string mode)
    {
        PageMetadata Offer(int generation,DateTimeOffset? expiry=null,bool complete=true)=>Page(generation) with{Media=[new(){Id="file",Url=Page(generation).Media[0].Url,Kind=MediaKind.Progressive,Extension="mp4",HasAudio=true,HasVideo=true,IsCompleteMedia=complete,ResourceExpires=expiry}]};
        var initial=Offer(0);var url=initial.Media[0].Url;var reads=0;
        using var lease=new RenewableMediaAuthorization(initial,_=>
        {
            reads++;if(mode=="failed"&&reads==2)throw new IOException("Owned expiry refresh failure");
            return Task.FromResult(Offer(reads,DateTimeOffset.UtcNow.AddSeconds(reads==1&&mode!="complete"?1:3600),mode!="complete"||reads>1));
        });
        var first=await lease.AuthorizeAsync(url,default);var identity=lease.RecoveryIdentity(url);
        if(mode=="complete")
        {
            await ProviderFamiliesChecks.Expect<InvalidDataException>(()=>lease.RenewAsync(url,first.Generation,default));
            AdaptiveRecoveryChecks.Assert(await lease.AuthorizeAsync(url,default)==first,"A changed complete-media contract committed during renewal.");
        }
        else
        {
            var next=await lease.RenewAsync(url,first.Generation,default);
            if(mode=="expired")await Task.Delay(1100);
            if(mode=="failed")await ProviderFamiliesChecks.Expect<IOException>(()=>lease.RenewAsync(url,first.Generation,default));
            AdaptiveRecoveryChecks.Assert(lease.Canonicalize(next.Url)==url&&lease.RecoveryIdentity(url)==identity,"Expiry refresh changed accepted recovery bindings.");
        }
        var fresh=await lease.RenewAsync(url,first.Generation,default);
        AdaptiveRecoveryChecks.Assert(reads==(mode=="failed"?3:2)&&fresh.Generation==(mode=="complete"?2:3)&&fresh.Url==Offer(reads).Media[0].Url,"A stale rejection returned expired playback or repeated a valid generation.");
    }
    private static async Task RenewalAddress(string mode)
    {
        MediaCandidate Format(string id,Uri url)=>new(){Id=id,Kind=MediaKind.Progressive,Url=url,Extension="mp4",HasAudio=true,HasVideo=true};
        var a=new Uri("https://cdn.example/a.mp4");var b=new Uri("https://cdn.example/b.mp4");var shared=new Uri("https://cdn.example/shared.mp4");
        var initial=Page(0) with{Media=mode=="caption"?[Format("a",a)]:[Format("a",a),Format("b",b)],Subtitles=mode=="caption"?[new(){Url=b,Language="en"}]:[]};
        var reads=0;
        using var lease=new RenewableMediaAuthorization(initial,_=>
        {
            reads++;var bad=reads==1;
            return Task.FromResult(initial with{Media=mode=="caption"?[Format("a",shared)]:[Format("a",shared),Format("b",bad?shared:new("https://cdn.example/renewed-b.mp4"))],Subtitles=mode=="caption"?[new(){Url=bad?shared:new("https://cdn.example/renewed-en.vtt"),Language="en"}]:[]});
        });
        var first=await lease.AuthorizeAsync(a,default);var recovery=lease.RecoveryIdentity(a);
        if(mode=="active")await lease.AuthorizeAsync(b,default);
        await ProviderFamiliesChecks.Expect<InvalidDataException>(()=>lease.RenewAsync(a,first.Generation,default));
        AdaptiveRecoveryChecks.Assert(await lease.AuthorizeAsync(a,default)==first&&!lease.AppliesTo(shared)&&lease.RecoveryIdentity(a)==recovery,"An ambiguous proposal partially committed renewed mappings.");
        var recovered=await lease.RenewAsync(a,first.Generation,default);
        AdaptiveRecoveryChecks.Assert(recovered.Url==shared&&recovered.Generation==2&&reads==2&&lease.Canonicalize(shared)==a,"A rejected address collision poisoned the next valid generation.");
    }
    private static async Task Handoff(string mode)
    {
        using var owner=AdaptiveAccountChecks.Http((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var page=Page(0);var url=page.Media[0].Url;
        if(mode=="authorize-dispose")page=page with{Media=[new(){Id="file",Url=url,Kind=MediaKind.Progressive,Extension="mp4",HasAudio=true,HasVideo=true,ResourceExpires=DateTimeOffset.UtcNow.AddSeconds(-1)}]};
        using var child=new ProviderPlaybackSession(owner,new(){Url=new("https://www.facebook.com/reel/12345/"),OutputDirectory=Path.GetTempPath()},"facebook",(_,_)=>Task.FromResult(Page(1)));
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure=new ObjectDisposedException("Owned issuer failure");var calls=0;
        using var lease=new RenewableMediaAuthorization(page,async ct=>
        {
            calls++;entered.TrySetResult();await release.Task;
            if(mode=="issuer-failure"&&calls==1)throw failure;
            return await child.ReadAsync(ct);
        });
        using var cancel=new CancellationTokenSource();
        Task pending;
        if(mode=="authorize-dispose")pending=lease.AuthorizeAsync(url,cancel.Token);
        else{var first=await lease.AuthorizeAsync(url,default);pending=lease.RenewAsync(url,first.Generation,cancel.Token);}
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if(mode!="issuer-failure")child.Dispose();
        if(mode is "authorize-dispose" or "renewal-dispose")lease.Dispose();
        if(mode=="caller-cancel")cancel.Cancel();
        release.TrySetResult();
        if(mode=="issuer-failure")
        {
            try{await pending;throw new InvalidOperationException("An issuer failure completed.");}catch(ObjectDisposedException exception)when(ReferenceEquals(exception,failure)){}
            AdaptiveRecoveryChecks.Assert((await lease.RenewAsync(url,1,default)).Generation==2&&calls==2,"An unrelated issuer exception damaged the accepted lease.");
        }
        else if(mode=="inner-dispose")await ProviderFamiliesChecks.Expect<ObjectDisposedException>(()=>pending);
        else await ProviderFamiliesChecks.Expect<OperationCanceledException>(()=>pending);
    }
    private static async Task Discovery(string mode)
    {
        for(var iteration=0;iteration<100;iteration++)
        {
            using var owner=AdaptiveAccountChecks.Http((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var calls=0;
            using var session=new ProviderPlaybackSession(owner,new(){Url=new("https://www.facebook.com/reel/12345/"),OutputDirectory=Path.GetTempPath()},"facebook",async(_,ct)=>
            {
                calls++;if(mode=="initial"||calls>1){entered.TrySetResult();await release.Task;ct.ThrowIfCancellationRequested();}return Page(calls);
            });
            Task first;
            if(mode=="renewal")
            {
                var page=await session.ReadAsync(default);var lease=await page.PlaybackAuthorization!.AuthorizeAsync(page.Media[0].Url,default);
                first=page.PlaybackAuthorization.RenewAsync(page.Media[0].Url,lease.Generation,default);
            }
            else first=session.ReadAsync(default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var pending=Enumerable.Range(0,4).Select(_=>(Task)session.ReadAsync(default)).Prepend(first).ToArray();
            var shutdown=Task.Run(session.Dispose);release.TrySetResult();
            foreach(var task in pending)
            {
                try{await task;}catch(OperationCanceledException){}
            }
            await shutdown;
            await ProviderFamiliesChecks.Expect<ObjectDisposedException>(()=>session.ReadAsync(default));
        }
    }
    private static async Task Http(string mode)
    {
        var initial=Page(0);var url=initial.Media[0].Url;var alias=new Uri("https://cdn.example/alias.mp4");
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var lease=new RenewableMediaAuthorization(initial,async ct=>{entered.TrySetResult();await release.Task;ct.ThrowIfCancellationRequested();return Page(1);},_=>true);
        var failure=new IOException("Owned transport failure");var calls=0;var content=new TrackedContent();
        using var owner=AdaptiveAccountChecks.Http(async(message,ct)=>
        {
            calls++;
            if(mode=="successful-alias")return new(HttpStatusCode.OK){Content=new StringContent("complete")};
            if(mode=="redirect-renewal"&&calls==1)return new(HttpStatusCode.Redirect){Headers={Location=alias}};
            if(mode is "renewal" or "redirect-renewal")return new(HttpStatusCode.Forbidden);
            entered.TrySetResult();await release.Task;
            if(mode=="failure")throw failure;
            if(mode=="late-response")return new(HttpStatusCode.OK){Content=content};
            ct.ThrowIfCancellationRequested();return new(HttpStatusCode.OK);
        });
        using var authorized=owner.WithAuthorization(lease);using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        if(mode=="successful-alias")AdaptiveRecoveryChecks.Assert(lease.TryRedirect(url,alias));
        using var request=new HttpRequestMessage(HttpMethod.Get,mode=="successful-alias"?alias:url);
        var pending=authorized.SendAsync(request,cancel.Token);
        if(mode=="successful-alias")
        {
            using var response=await pending;
            var document=await authorized.GetDocumentAsync(alias,128,default);
            AdaptiveRecoveryChecks.Assert(request.RequestUri==url&&response.RequestMessage!.RequestUri==url&&document.Url==url&&document.EffectiveUrl==alias,"HTTP canonical/effective aliases changed.");return;
        }
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));if(mode!="failure")cancel.Cancel();lease.Dispose();release.TrySetResult();
        try{using var unexpected=await pending;throw new InvalidOperationException("A canceled or failed request succeeded.");}
        catch(OperationCanceledException)when(mode!="failure"){}
        catch(IOException exception)when(mode=="failure"&&ReferenceEquals(exception,failure)){}
        AdaptiveRecoveryChecks.Assert(request.RequestUri==url,"HTTP failure cleanup changed the canonical request URL.");
        if(mode=="late-response")AdaptiveRecoveryChecks.Assert(content.Disposed,"A late canceled HTTP response leaked its content.");
    }
    private sealed class TrackedContent:ByteArrayContent
    {
        internal bool Disposed;internal TrackedContent():base("late"u8.ToArray()){}
        protected override void Dispose(bool disposing){Disposed=true;base.Dispose(disposing);}
    }
    private static PageMetadata Page(int generation,MediaKind kind=MediaKind.Progressive)=>new(){Id="video",Extractor="fixture",Media=[new(){Id="file",Kind=kind,Url=new("https://cdn.example/video.mp4?generation="+generation),Extension="mp4",HasVideo=true,HasAudio=true}]};
    private static async Task Lease(string mode)
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var refresh=0;
        var waitingMode=mode.StartsWith("waiting",StringComparison.Ordinal);var kind=mode=="waiting-manifest"?MediaKind.Hls:MediaKind.Progressive;var initial=Page(0,kind);
        var manifest=new MediaDocument(initial.Media[0].Url,"#EXTM3U\n#EXT-X-TARGETDURATION:1\n#EXTINF:1,\nhttps://cdn.example/segment.ts\n#EXT-X-ENDLIST\n","application/vnd.apple.mpegurl");
        var lease=new RenewableMediaAuthorization(initial,async ct=>
        {
            refresh++;entered.TrySetResult();if(waitingMode||mode is "dispose" or "late")await release.Task;ct.ThrowIfCancellationRequested();
            if(mode=="failed"&&refresh==1)throw new IOException("issuer unavailable");
            var next=Page(1,kind);return mode switch{"extractor"=>next with {Extractor="other"},"live"=>next with {IsLive=true},"playlist"=>next with {IsPlaylist=true},_=>next};
        });
        try
        {
            var old=await lease.AuthorizeAsync(initial.Media[0].Url,default);var task=lease.RenewAsync(initial.Media[0].Url,old.Generation,default);
            if(waitingMode||mode is "dispose" or "late")
            {
                await entered.Task;Task? waiting=mode switch
                {
                    "waiting"=>lease.AuthorizeAsync(initial.Media[0].Url,default),
                    "waiting-renewal"=>lease.RenewAsync(initial.Media[0].Url,old.Generation,default),
                    "waiting-manifest"=>lease.ObserveManifestAsync(manifest,default),
                    _=>null
                };
                lease.Dispose();release.TrySetResult();
                try{await task;throw new InvalidOperationException("Disposed issuer refreshed.");}catch(OperationCanceledException){}
                if(waiting is not null)try{await waiting;throw new InvalidOperationException("Disposed gate admitted a waiter.");}catch(OperationCanceledException){}
                try{await lease.AuthorizeAsync(initial.Media[0].Url,default);throw new InvalidOperationException("Disposed lease reopened.");}catch(ObjectDisposedException){}
                try{await lease.RenewAsync(initial.Media[0].Url,old.Generation,default);throw new InvalidOperationException("Disposed lease renewed.");}catch(ObjectDisposedException){}
                try{await lease.ObserveManifestAsync(manifest,default);throw new InvalidOperationException("Disposed lease observed a manifest.");}catch(ObjectDisposedException){}
            }
            else if(mode is "extractor" or "live" or "playlist"){try{await task;throw new InvalidOperationException("Changed issuer identity accepted.");}catch(InvalidDataException){}var unchanged=await lease.AuthorizeAsync(initial.Media[0].Url,default);AdaptiveRecoveryChecks.Assert(unchanged.Url==initial.Media[0].Url);}
            else if(mode=="failed"){try{await task;throw new InvalidOperationException();}catch(IOException){}var recovered=await lease.RenewAsync(initial.Media[0].Url,old.Generation,default);AdaptiveRecoveryChecks.Assert(recovered.Url==Page(1).Media[0].Url&&refresh==2);}
            else{await task;var stale=await lease.RenewAsync(initial.Media[0].Url,old.Generation,default);AdaptiveRecoveryChecks.Assert(stale.Generation!=old.Generation&&refresh==1);}
        }
        finally{release.TrySetResult();lease.Dispose();}
    }
    private static async Task Sabr()
    {
        var calls=0;using var output=new MemoryStream();using var http=AdaptiveAccountChecks.Http(async(message,_)=>{
            calls++;var request=ProtobufReader.Read(await message.Content!.ReadAsByteArrayAsync());var context=ProtobufReader.Read(request.Single(field=>field.Number==19).Bytes);
            AdaptiveRecoveryChecks.Assert(!context.Any(field=>field.Number is 3 or 5),"Partial response state was sent in a retry.");
            using var response=new MemoryStream();SabrFixture.Status(response,1);SabrFixture.Metadata(response,SabrFixture.Video,1,1000);
            if(calls==1){var cookie=new ProtobufWriter();cookie.Bytes(7,"partial-cookie"u8.ToArray());SabrFixture.Part(response,35,cookie.ToArray());var state=new ProtobufWriter();state.UInt64(1,7);state.Bytes(3,"partial-context"u8.ToArray());state.UInt64(4,1);SabrFixture.Part(response,57,state.ToArray());SabrFixture.Segment(response,SabrFixture.Video,1,0,"init"u8.ToArray());SabrFixture.Segment(response,SabrFixture.Video,2,1,"media"u8.ToArray(),new(0,1000,1000),complete:false);}
            else{SabrFixture.Segment(response,SabrFixture.Video,1,0,"init"u8.ToArray());SabrFixture.Segment(response,SabrFixture.Video,2,1,"media"u8.ToArray(),new(0,1000,1000));}
            return SabrFixture.Response(response.ToArray());
        });
        var sink=new SabrTrackSink(new Dictionary<SabrFormatId,Stream>{[SabrFixture.Video]=output});
        await new SabrHttpSession(http).DownloadAsync(SabrFixture.Endpoint,SabrFixture.VideoId,sink,(_,_)=>new(){ClientVersion="fixture",ClientName=5,PlaybackConfiguration=[1],VideoFormats=[SabrFixture.Video]},default,retries:1);
        AdaptiveRecoveryChecks.Assert(calls==2&&System.Text.Encoding.UTF8.GetString(output.ToArray())=="initmedia");
    }
}
