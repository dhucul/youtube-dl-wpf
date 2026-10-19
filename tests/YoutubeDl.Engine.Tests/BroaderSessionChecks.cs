using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Protocols;
using YoutubeDl.Engine.Protocols.Sabr;
internal static class BroaderSessionChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>new[]{"dispose","waiting","late","extractor","live","playlist","failed","stale"}.Select(mode=>("Publication session issuer "+mode,(Func<Task>)(()=>Lease(mode))))
        .Concat([("Publication session SABR partial controls discarded",(Func<Task>)Sabr)]);
    private static PageMetadata Page(int generation)=>new(){Id="video",Extractor="fixture",Media=[new(){Id="file",Kind=MediaKind.Progressive,Url=new("https://cdn.example/video.mp4?generation="+generation),Extension="mp4",HasVideo=true,HasAudio=true}]};
    private static async Task Lease(string mode)
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var refresh=0;var initial=Page(0);
        var lease=new RenewableMediaAuthorization(initial,async ct=>
        {
            refresh++;entered.TrySetResult();if(mode is "dispose" or "waiting" or "late")await release.Task;ct.ThrowIfCancellationRequested();
            if(mode=="failed"&&refresh==1)throw new IOException("issuer unavailable");
            var next=Page(1);return mode switch{"extractor"=>next with {Extractor="other"},"live"=>next with {IsLive=true},"playlist"=>next with {IsPlaylist=true},_=>next};
        });
        try
        {
            var old=await lease.AuthorizeAsync(initial.Media[0].Url,default);var task=lease.RenewAsync(initial.Media[0].Url,old.Generation,default);
            if(mode is "dispose" or "waiting" or "late")
            {
                await entered.Task;var waiting=mode=="waiting"?lease.AuthorizeAsync(initial.Media[0].Url,default):null;lease.Dispose();release.TrySetResult();
                try{await task;throw new InvalidOperationException("Disposed issuer refreshed.");}catch(OperationCanceledException){}
                if(waiting is not null)try{await waiting;throw new InvalidOperationException("Disposed gate admitted a waiter.");}catch(OperationCanceledException){}
                try{await lease.AuthorizeAsync(initial.Media[0].Url,default);throw new InvalidOperationException("Disposed lease reopened.");}catch(ObjectDisposedException){}
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
