using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.PeerTube;
using YoutubeDl.Engine.Providers.Vimeo;
using YoutubeDl.Engine.Providers.Kaltura;

internal static class ProviderLifetimeChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
        new[]{"wistia","jwplatform","brightcove","kaltura"}.SelectMany(provider=>new[]{"initial","waiter","refresh","repeat"}.Select(mode=>("Write cleanup provider "+provider+" "+mode,(Func<Task>)(()=>Hosted(provider,mode)))))
        .Concat(new[]{"initial","waiter","refresh","repeat"}.Select(mode=>("Write cleanup provider vimeo "+mode,(Func<Task>)(()=>Vimeo(mode)))))
        .Concat(new[]{"initial","waiter","file","rejected","concurrent","failure","scopes"}.Select(mode=>("Write cleanup provider peertube "+mode,(Func<Task>)(()=>PeerTube(mode)))))
        .Concat(new[]{"identity","cancel","expired"}.Select(mode=>("Write cleanup provider kaltura retains valid state "+mode,(Func<Task>)(()=>Kaltura(mode)))))
        .Concat([("Write cleanup disposed HTTP cannot recreate provider state",(Func<Task>)(()=>HttpOwner(false))),("Write cleanup disposed HTTP factory remains unused",(Func<Task>)(()=>HttpOwner(true)))]);
    private static TaskCompletionSource Signal()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static PageMetadata Page(string provider,int generation)=>new(){Id="video",Extractor=provider,Media=[new(){Id="main",Url=new("https://cdn.example/"+generation+".mp4"),Kind=MediaKind.Progressive,Extension="mp4",Height=72,Width=128,HasVideo=true,HasAudio=true}]};
    private static async Task Canceled(Task task){try{await task.WaitAsync(TimeSpan.FromSeconds(5));throw new InvalidOperationException("Disposed provider completed.");}catch(Exception e)when(e is OperationCanceledException or ObjectDisposedException){}}
    private static async Task Hosted(string provider,string mode)
    {
        var entered=Signal();var release=Signal();var reads=0;using var http=AdaptiveAccountChecks.Http((_,_)=>throw new InvalidOperationException("Unexpected network."));
        var request=new NativeDownloadRequest{Url=new("https://article.example/video"),OutputDirectory="."};
        var session=new ProviderPlaybackSession(http,request,provider,async(_,_)=>{var number=Interlocked.Increment(ref reads);if(mode is not ("refresh" or "repeat")||number>1){entered.TrySetResult();await release.Task;}return Page(provider,number);});
        try
        {
            if(mode=="repeat"){var first=await session.ReadAsync(default);var again=await session.ReadAsync(default);WriteCleanupChecks.Check(reads==1&&again.Media[0].Url==first.Media[0].Url&&ReferenceEquals(again.PlaybackAuthorization,first.PlaybackAuthorization)&&again.PlaybackAuthorization!.AppliesTo(again.Media[0].Url),"Repeated hosting read returned unregistered URLs.");return;}
            var initial=mode=="refresh"?await session.ReadAsync(default):null;
            var task=initial is null?(Task)session.ReadAsync(default):initial.PlaybackAuthorization!.RenewAsync(initial.Media[0].Url,1,default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));var waiter=mode=="waiter"?session.ReadAsync(default):null;session.Dispose();release.TrySetResult();await Canceled(task);if(waiter is not null)await Canceled(waiter);
            try{await session.ReadAsync(default);throw new InvalidOperationException("Hosting session reopened.");}catch(ObjectDisposedException){}
            WriteCleanupChecks.Check(reads==(mode=="refresh"?2:1),"Disposed hosting waiter issued a request.");
        }
        finally{release.TrySetResult();session.Dispose();}
    }
    private static string VimeoJson(int generation)=>JsonSerializer.Serialize(new{uri="/videos/123",name="Fixture",files=new[]{new{quality="sd",type="video/mp4",width=128,height=72,size=100,link="https://cdn.example/"+generation+".mp4"}}});
    private static async Task Vimeo(string mode)
    {
        var entered=Signal();var release=Signal();var reads=0;using var http=AdaptiveAccountChecks.Http(async(message,_)=>{WriteCleanupChecks.Check(message.RequestUri!.Host=="api.vimeo.com"&&message.Headers.Authorization?.Parameter=="fixture-token","Vimeo credential left the issuer.");var number=Interlocked.Increment(ref reads);if(mode is not ("refresh" or "repeat")||number>1){entered.TrySetResult();await release.Task;}return AdaptiveAccountChecks.Text(VimeoJson(number));});
        var request=new NativeDownloadRequest{Url=new("https://vimeo.com/123"),OutputDirectory=".",Authentication=new(){AccessToken="fixture-token"}};var session=new VimeoSession(http,request,new("123",null));
        try
        {
            if(mode=="repeat"){var first=await session.ReadAsync(default);var again=await session.ReadAsync(default);WriteCleanupChecks.Check(reads==1&&again.Media[0].Url==first.Media[0].Url&&ReferenceEquals(again.PlaybackAuthorization,first.PlaybackAuthorization)&&again.PlaybackAuthorization!.AppliesTo(again.Media[0].Url),"Repeated Vimeo read bypassed renewal.");return;}
            var initial=mode=="refresh"?await session.ReadAsync(default):null;var task=initial is null?(Task)session.ReadAsync(default):initial.PlaybackAuthorization!.RenewAsync(initial.Media[0].Url,1,default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));var waiter=mode=="waiter"?session.ReadAsync(default):null;session.Dispose();release.TrySetResult();await Canceled(task);if(waiter is not null)await Canceled(waiter);
            try{await session.ReadAsync(default);throw new InvalidOperationException("Vimeo session reopened.");}catch(ObjectDisposedException){}
            WriteCleanupChecks.Check(reads==(mode=="refresh"?2:1),"Disposed Vimeo waiter requested credentials.");
        }
        finally{release.TrySetResult();session.Dispose();}
    }
    private static async Task PeerTube(string mode)
    {
        var entered=Signal();var release=Signal();var tokens=0;var calls=0;using var http=AuthenticationBatchChecks.Http(async(message,_)=>
        {
            calls++;var path=message.RequestUri!.AbsolutePath;
            if(path.EndsWith("/local"))return AuthenticationBatchChecks.Json("""{"client_id":"client","client_secret":"secret"}""");
            if(path.EndsWith("/users/token")){if(mode is "initial" or "waiter"){entered.TrySetResult();await release.Task;}return AuthenticationBatchChecks.Json(AuthenticationBatchChecks.Tokens("account-token"));}
            if(path.EndsWith("/token"))
            {
                tokens++;if(mode=="file"){entered.TrySetResult();await release.Task;}if(mode=="failure"&&tokens==2)throw new IOException("File issuer failed.");
                return AuthenticationBatchChecks.Json(JsonSerializer.Serialize(new{files=new{token="file-"+tokens,expires=DateTimeOffset.UtcNow.AddMinutes(2)}}));
            }
            WriteCleanupChecks.Check(message.Headers.Authorization?.Parameter=="account-token","PeerTube account context was lost.");return AuthenticationBatchChecks.Json("{}");
        });
        var credentials=mode is "initial" or "waiter"?new NativeAuthenticationOptions{Username="fixture-user",Password="fixture-password"}:new();
        var session=new PeerTubeSession(http,new("https://account.example/"),credentials);
        try
        {
            if(mode is "initial" or "waiter" or "file")
            {
                var task=mode=="file"?(Task)session.RequestFileTokenAsync(AuthenticationBatchChecks.Id,default):session.ReadAsync(new("https://account.example/api/v1/users/me"),1024,default);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));var waiter=mode=="waiter"?session.ReadAsync(new("https://account.example/api/v1/users/me"),1024,default):null;session.Dispose();release.TrySetResult();await Canceled(task);if(waiter is not null)await Canceled(waiter);
                try{await session.RequestFileTokenAsync(AuthenticationBatchChecks.Id,default);throw new InvalidOperationException("PeerTube session reopened.");}catch(ObjectDisposedException){}
                WriteCleanupChecks.Check(calls==(mode=="file"?1:2),"Disposed PeerTube issued another API request.");
            }
            else
            {
                var page=new PageMetadata{Id=AuthenticationBatchChecks.Id,Media=[new(){Id="main",Url=new("https://account.example/static/video.mp4"),Kind=MediaKind.Progressive,Extension="mp4",HasVideo=true}]};
                var lease=await session.PlaybackAuthorizationAsync(page,default);var url=page.Media[0].Url;var old=await lease.AuthorizeAsync(url,default);
                if(mode=="scopes")
                {
                    var changed=page with {Media=[new(){Id="main",Url=new("https://account.example/static/other.mp4"),Kind=MediaKind.Progressive,Extension="mp4",HasVideo=true}]};
                    try{await session.PlaybackAuthorizationAsync(changed,default);throw new InvalidOperationException("Changed private scope accepted.");}catch(InvalidDataException){}
                    WriteCleanupChecks.Check(tokens==1&&lease.AppliesTo(url)&&!lease.AppliesTo(changed.Media[0].Url),"Failed scope change damaged original lease.");return;
                }
                if(mode=="failure")
                {
                    try{await lease.RenewAsync(url,old.Generation,default);throw new InvalidOperationException("Failed file grant accepted.");}catch(IOException){}
                    WriteCleanupChecks.Check((await lease.AuthorizeAsync(url,default)).Url==old.Url,"Failed file grant destroyed valid token.");
                }
                else
                {
                    var renewed=await Task.WhenAll(Enumerable.Range(0,mode=="concurrent"?12:1).Select(_=>lease.RenewAsync(url,old.Generation,default)));
                    WriteCleanupChecks.Check(tokens==2&&renewed.All(value=>value.Url.Query=="?videoFileToken=file-2")&&(await lease.RenewAsync(url,old.Generation,default)).Generation==renewed[0].Generation,"Rejected generation repeated a file grant.");
                }
                session.Dispose();try{lease.Canonicalize(old.Url);throw new InvalidOperationException("Disposed file token remained available.");}catch(ObjectDisposedException){}
            }
        }
        finally{release.TrySetResult();session.Dispose();}
    }
    private static async Task Kaltura(string mode)
    {
        var grants=0;using var stop=new CancellationTokenSource();var request=new NativeDownloadRequest{Url=new("https://cdnapisec.kaltura.com/p/309/embedPlaykitJs?entry_id=1_test"),OutputDirectory=".",Authentication=new(){Username="fixture",Password="secret"}};
        using var http=AdaptiveAccountChecks.Http(async(message,_)=>
        {
            var action=message.RequestUri!.Segments[^1];await message.Content!.ReadAsStringAsync();
            if(action=="loginByLoginId")return AdaptiveAccountChecks.Text(JsonSerializer.Serialize("ks-"+(++grants)));
            WriteCleanupChecks.Check(action=="get","Unexpected Kaltura action.");if(grants==2&&mode=="cancel")stop.Cancel();
            return AdaptiveAccountChecks.Text(JsonSerializer.Serialize(new{partnerId=309,userId=grants==2&&mode=="identity"?"other":"fixture",type=2,expiry=grants==2&&mode=="expired"?DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeSeconds():DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}));
        });
        var account=new KalturaAccountSession(request,new(309,"1_test"));WriteCleanupChecks.Check(await account.GetAsync(http,default)=="ks-1","Initial Kaltura session missing.");account.Invalidate();
        try{await account.GetAsync(http,stop.Token);throw new InvalidOperationException("Invalid Kaltura renewal accepted.");}catch(Exception e)when(e is InvalidDataException or NotSupportedException or OperationCanceledException){}
        WriteCleanupChecks.Check(await account.GetAsync(http,default)=="ks-3"&&grants==3,"Invalid renewal was cached as a valid account.");
    }
    private sealed class OwnedState:IDisposable{internal int Calls;public void Dispose()=>Calls++;}
    private static Task HttpOwner(bool unused)
    {
        var factories=0;var http=AdaptiveAccountChecks.Http((_,_)=>throw new InvalidOperationException());var state=unused?null:http.SessionState("fixture",()=>new OwnedState());http.Dispose();
        try{http.SessionState("fixture",()=>{factories++;return new OwnedState();});throw new InvalidOperationException("Disposed owner created state.");}catch(ObjectDisposedException){}
        http.Dispose();WriteCleanupChecks.Check(factories==0&&(state is null||state.Calls==1),"Owner retained or recreated disposed state.");return Task.CompletedTask;
    }
}
