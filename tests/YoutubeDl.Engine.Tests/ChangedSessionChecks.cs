using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Authentication;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Kaltura;

internal static class ChangedSessionChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
        new[]{"identify","waiter","grant","resource","refresh-identity","failed","switched","same-token","cancel-waiter"}.Select(mode=>("Changed job OAuth "+mode,(Func<Task>)(()=>OAuth(mode))))
        .Concat(new[]{"redirects","headers","queries","anonymous","cookies","authorization"}.Select(mode=>("Changed job disposed HTTP clone "+mode,(Func<Task>)(()=>Clone(mode)))))
        .Concat(new[]{"ks","flashvars","explicit","playback","renewal"}.Select(mode=>("Changed job Kaltura referrer "+mode,(Func<Task>)(()=>Referrer(mode)))));
    private static TaskCompletionSource Signal()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Canceled(Task task){try{await task.WaitAsync(TimeSpan.FromSeconds(5));throw new InvalidOperationException("Retired OAuth operation completed.");}catch(Exception e)when(e is OperationCanceledException or ObjectDisposedException){}}
    private static async Task OAuth(string mode)
    {
        var entered=Signal();var release=Signal();var rejected=Signal();var grants=0;var identities=0;var resources=0;var early=0;
        var credentials=new NativeOAuthCredentials{Provider=NativeOAuthProvider.YouTube,ClientId="client-fixture",AccessToken="old",RefreshToken="refresh-fixture",Scopes=NativeOAuthSession.RequiredScopes(NativeOAuthProvider.YouTube),Expires=mode=="grant"?DateTimeOffset.UtcNow.AddSeconds(-1):DateTimeOffset.UtcNow.AddHours(1)};
        using var http=AdaptiveAccountChecks.Http(async(message,_)=>
        {
            if(message.Method==HttpMethod.Post)
            {
                grants++;if(mode=="grant"){entered.TrySetResult();await release.Task;}if(mode=="failed"&&grants==1)throw new IOException("Issuer unavailable.");
                return AdaptiveAccountChecks.Text(AdaptiveAccountChecks.Token(NativeOAuthProvider.YouTube,mode=="same-token"?"old":"new"));
            }
            if(message.RequestUri!.Host=="openidconnect.googleapis.com")
            {
                identities++;if(mode is "identify" or "waiter" or "cancel-waiter"||mode=="refresh-identity"&&identities>1){entered.TrySetResult();await release.Task;}
                return AdaptiveAccountChecks.Text(JsonSerializer.Serialize(new{sub=mode=="switched"&&identities>1?"other-account":"account"}));
            }
            resources++;WriteCleanupChecks.Check(message.RequestUri.Host=="www.googleapis.com","OAuth bearer left its issuer.");
            if(mode=="resource"){entered.TrySetResult();await release.Task;}
            if(mode=="same-token"&&grants==0){if(Interlocked.Increment(ref early)==8)rejected.TrySetResult();await rejected.Task;return AdaptiveAccountChecks.Text("{}",HttpStatusCode.Unauthorized);}
            return AdaptiveAccountChecks.Text("{}",mode is "failed" or "switched" or "refresh-identity"&&resources==1?HttpStatusCode.Unauthorized:HttpStatusCode.OK);
        });
        var session=new NativeOAuthSession(http,credentials);
        try
        {
            var url=new Uri("https://www.googleapis.com/youtube/v3/playlists?part=id");
            if(mode=="same-token"){await Task.WhenAll(Enumerable.Range(0,8).Select(_=>session.GetAsync(url,default)));WriteCleanupChecks.Check(grants==1&&resources==16,"Unchanged renewed token refreshed once per rejected caller.");return;}
            if(mode is "failed" or "switched")
            {
                try{await session.GetAsync(url,default);throw new InvalidOperationException("Invalid OAuth refresh accepted.");}catch(Exception e)when(e is IOException or InvalidDataException){}
                WriteCleanupChecks.Check(session.Credentials.AccessToken=="old"&&session.Credentials.AccountId=="account","Failed OAuth grant replaced valid credentials.");
                var again=await session.GetAsync(url,default);WriteCleanupChecks.Check(again.StatusCode==HttpStatusCode.OK&&grants==1,"Unrelated next request discarded valid credentials.");return;
            }
            var task=mode is "resource" or "refresh-identity"?(Task)session.GetAsync(url,default):session.EnsureAsync(default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if(mode=="cancel-waiter")
            {
                using var stop=new CancellationTokenSource();var waiting=session.EnsureAsync(stop.Token);stop.Cancel();await Canceled(waiting);release.TrySetResult();await task;await session.EnsureAsync(default);WriteCleanupChecks.Check(identities==1&&grants==0,"Canceled waiter damaged the surviving OAuth session.");return;
            }
            var waiter=mode=="waiter"?session.EnsureAsync(default):null;session.Dispose();release.TrySetResult();await Canceled(task);if(waiter is not null)await Canceled(waiter);
            try{_ =session.Credentials;throw new InvalidOperationException("Disposed OAuth credentials remained readable.");}catch(ObjectDisposedException){}
            try{await session.GetAsync(url,default);throw new InvalidOperationException("Disposed OAuth session reopened.");}catch(ObjectDisposedException){}
        }
        finally{release.TrySetResult();rejected.TrySetResult();session.Dispose();}
    }
    private static Task Clone(string mode)
    {
        var http=new MediaHttp(new(){AllowAutoRedirect=false});http.Dispose();
        try
        {
            using var child=mode switch{"redirects"=>http.CreateWithoutRedirects(),"headers"=>http.WithHeaders(new Dictionary<string,string>()),"queries"=>http.WithQueries([]),"anonymous"=>http.CreateAnonymousSession(),"cookies"=>http.CreateCookieSession(YoutubeDl.Engine.Providers.YouTube.YouTubePlaybackCookies.FromContainer(new CookieContainer(),default)),_=>http.WithAuthorization(new FakeAuthorization())};
            throw new InvalidOperationException("Disposed HTTP created another session.");
        }
        catch(ObjectDisposedException){}
        return Task.CompletedTask;
    }
    private sealed class FakeAuthorization:IPlaybackAuthorization
    {public bool AppliesTo(Uri uri)=>false;public Task<PlaybackLease> AuthorizeAsync(Uri uri,CancellationToken ct)=>Task.FromResult(new PlaybackLease(uri,0));public Task<PlaybackLease> RenewAsync(Uri uri,long generation,CancellationToken ct)=>AuthorizeAsync(uri,ct);public Uri Canonicalize(Uri uri)=>uri;}
    private static async Task Referrer(string mode)
    {
        var url=new Uri("https://cdnapisec.kaltura.com/p/309/embedPlaykitJs?entry_id=1_fixture&"+(mode=="flashvars"?"flashvars%5Bks%5D":"ks")+"=private-ks&player=literal");
        var request=new NativeDownloadRequest{Url=url,OutputDirectory=".",Referer=mode=="explicit"?"https://publisher.example/article":null};
        var referer=KalturaAddress.PublicReferer(request);WriteCleanupChecks.Check(!referer.Contains("private-ks")&&!referer.Contains("flashvars")&&!referer.Contains("&ks=")&&referer.Contains(mode=="explicit"?"publisher.example":"player=literal"),"Default referrer retained a session key.");
        if(mode is "ks" or "flashvars" or "explicit")return;
        var media=0;var contexts=0;using var http=AdaptiveAccountChecks.Http(async(message,ct)=>
        {
            if(message.RequestUri!.Host=="cdn.example"){media++;WriteCleanupChecks.Check(message.Headers.Referrer?.AbsoluteUri==referer&&message.Headers.Authorization is null,"Private session entered media headers.");return AdaptiveAccountChecks.Text("complete",mode=="renewal"&&media==1?HttpStatusCode.Forbidden:HttpStatusCode.OK);}
            using var body=JsonDocument.Parse(await message.Content!.ReadAsStringAsync(ct));var action=message.RequestUri.Segments[^1];
            if(action=="get"&&message.RequestUri.AbsolutePath.Contains("/session/"))return AdaptiveAccountChecks.Text(JsonSerializer.Serialize(new{partnerId=309,userId="fixture",type=2,expiry=DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}));
            if(action=="getPlaybackContext"){contexts++;WriteCleanupChecks.Check(body.RootElement.GetProperty("contextDataParams").GetProperty("referrer").GetString()==referer,"Private session entered playback-context referrer.");return AdaptiveAccountChecks.Text(SharedHostingChecks.KContext);}
            return AdaptiveAccountChecks.Text(action=="get"?SharedHostingChecks.KMedia:"\"https://cdn.example/media.mp4\"");
        });
        var page=await new KalturaProvider().ExtractAsync(request,http,default);
        using var playback=http.WithHeaders(page.PlaybackHeaders).WithAuthorization(page.PlaybackAuthorization!);
        var bytes=await playback.GetBytesAsync(page.Media[0].Url,1024,default);WriteCleanupChecks.Check(System.Text.Encoding.UTF8.GetString(bytes)=="complete"&&contexts==(mode=="renewal"?2:1),"Kaltura session did not retain safe renewed playback.");
    }
}
