using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.YouTube;
using YoutubeDl.Engine.Providers.Vimeo;
using YoutubeDl.Engine.Protocols;
using YoutubeDl.Engine.Protocols.Sabr;

internal static class AccountRenewalChecks
{
    internal const string Config="ytcfg.set({\"LOGGED_IN\":true,\"SESSION_INDEX\":\"1\",\"DELEGATED_SESSION_ID\":\"brand-fixture\",\"DATASYNC_ID\":\"account-fixture\",\"INNERTUBE_CLIENT_VERSION\":\"fixture.web\"});";
    public static IEnumerable<(string Name,Func<Task> Test)> All=>
    [
        ("Account renewal credentials redact tokens and preserve analysis arity",()=>Sync(Options)),
        ("Account renewal provider capabilities refuse credentials on unrelated hosts",()=>Sync(Capabilities)),
        ("Account renewal YouTube signs origin-bound account headers",()=>Sync(Headers)),
        ("Account renewal YouTube bootstrap expressions are skipped without execution",()=>Sync(Bootstrap)),
        ("Account renewal YouTube rejects signed-out wrong and switched account contexts",()=>Sync(Bindings)),
        ("Account renewal YouTube browser/account queries retain selected identity",()=>Sync(Queries)),
        ("Account renewal YouTube API authorization retry refreshes one matching watch context",()=>Api(false)),
        ("Account renewal YouTube API identity switches stop before replay",()=>Api(true)),
        ("Account renewal Vimeo password submits exact UTF8 base64 within its player scope",()=>Password("valid")),
        ("Account renewal Vimeo wrong password stops before media",()=>Password("wrong")),
        ("Account renewal Vimeo mismatched password gates receive no secret",()=>Password("identity")),
        ("Account renewal Vimeo password redirects receive no secret",()=>Password("redirect")),
        ("Account renewal Vimeo bearer tokens stay on the exact API origin",ApiVimeo),
        ("Account renewal Vimeo API video identity and file permission are required",()=>Sync(ApiBoundaries)),
        ("Account renewal exact media leases share concurrent rejected-generation refresh",()=>Lease("concurrent")),
        ("Account renewal expiring media refresh before resource acquisition",()=>Lease("expire")),
        ("Account renewal changed formats and revoked access fail",()=>Lease("changed")),
        ("Account renewal foreign resource requests cannot acquire a lease",()=>Lease("foreign")),
        ("Account renewal repeated media rejection remains bounded",()=>Lease("denied")),
        ("Account renewal cancellation stops issuer and resource requests",()=>Lease("cancel")),
        ("Account renewal approved Vimeo CDN redirects retain read headers",()=>Redirect(false)),
        ("Account renewal foreign redirects fail without resource requests",()=>Redirect(true)),
        ("Account renewal SABR HTTP refusal renews once with preserved buffered state",()=>Sabr("http")),
        ("Account renewal SABR player reload requests use a fresh context",()=>Sabr("reload")),
        ("Account renewal SABR expired endpoints refresh before HTTP",()=>Sabr("expire")),
        ("Account renewal SABR explicit protection denial never renews",()=>Sabr("protection")),
        ("Account renewal SABR repeated refusal stops after its refresh bound",()=>Sabr("denied"))
    ];
    private static Task Sync(Action action){action();return Task.CompletedTask;}
    private static void Assert(bool value){if(!value)throw new InvalidOperationException("Account/session renewal expectation failed.");}
    internal static CookieContainer Jar(){var jar=new CookieContainer();jar.Add(new Cookie("SAPISID","fixture-cookie","/",".youtube.com"){Secure=true});return jar;}
    private static MediaHttp Http(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send,CookieContainer? jar=null)=>new(new Handler(send),new(){AllowAutoRedirect=false,CookieContainer=jar});
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>send(request,ct);}
    private static void Options(){var r=NativeExtraArguments.Apply(new(){Url=new("https://vimeo.com/123"),OutputDirectory="."},NativeAnalysisArguments.Select(["--vimeo-access-token=fixture-token","--output","--cookies=opaque","--youtube-account-index","1"]));Assert(r.Authentication.AccessToken=="fixture-token"&&r.Authentication.YouTubeAccountIndex==1&&!JsonSerializer.Serialize(r).Contains("fixture-token")&&!r.Authentication.ToString().Contains("fixture-token"));}
    private static void Capabilities(){var e=new NativeEngine();Assert(e.GetAuthenticationCapabilities(new("https://vimeo.com/123")).HasFlag(NativeAuthenticationCapabilities.VideoPassword)&&e.GetAuthenticationCapabilities(new("https://www.youtube.com/watch?v=abcdefghijk")).HasFlag(NativeAuthenticationCapabilities.CookieAccount));SabrFixture.Throws<NotSupportedException>(()=>e.ValidateAuthenticationOptions(new(){Url=new("https://unrelated.example/video.mp4"),OutputDirectory=".",Authentication=new(){AccessToken="fixture-token"}}));}
    private static void Headers(){using var http=Http((_,_)=>throw new InvalidOperationException("Header construction used HTTP."),Jar());using var session=new YouTubeAccountSession(http,1);session.Observe(Config,new("https://www.youtube.com/watch?v=abcdefghijk&authuser=1"),default);var h=session.Headers("1","fixture.web",default,1234);var hash=Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes("1234 fixture-cookie https://www.youtube.com"))).ToLowerInvariant();Assert(h["Authorization"]=="SAPISIDHASH 1234_"+hash&&h["X-Goog-AuthUser"]=="1"&&h["X-Goog-PageId"]=="brand-fixture");}
    private static void Bootstrap(){var config=YouTubePageJson.ReadConfiguration("ytcfg.set({unsafe:window.notExecuted()});"+Config);Assert(config.GetProperty("SESSION_INDEX").GetString()=="1");}
    private static void Bindings()
    {
        using var http=Http((_,_)=>throw new InvalidOperationException(),Jar());using var a=new YouTubeAccountSession(http,1);a.Observe(Config,new("https://www.youtube.com/"),default);
        SabrFixture.Throws<InvalidDataException>(()=>a.Observe(Config.Replace("account-fixture","other-account"),new("https://www.youtube.com/"),default));
        SabrFixture.Throws<NotSupportedException>(()=>a.Observe(Config.Replace("true","false"),new("https://www.youtube.com/"),default));
        using var wrong=new YouTubeAccountSession(http,0);SabrFixture.Throws<InvalidDataException>(()=>wrong.Observe(Config,new("https://www.youtube.com/"),default));
        using var unidentified=new YouTubeAccountSession(http,1);SabrFixture.Throws<NotSupportedException>(()=>unidentified.Observe(Config.Replace("account-fixture",""),new("https://www.youtube.com/"),default));
        using var fallback=new YouTubeAccountSession(http,1);var alternate=Config.Replace("DATASYNC_ID","USER_SESSION_ID");fallback.Observe(alternate,new("https://www.youtube.com/"),default);
        SabrFixture.Throws<InvalidDataException>(()=>fallback.Observe(alternate.Replace("account-fixture","other-account"),new("https://www.youtube.com/"),default));
    }
    private static void Queries()
    {
        using var http=Http((_,_)=>throw new InvalidOperationException(),Jar());using var session=new YouTubeAccountSession(http,1);
        Assert(session.Select(new("https://www.youtube.com/watch?v=abcdefghijk")).Query=="?v=abcdefghijk&authuser=1");
        foreach(var path in new[]{"watch?v=abcdefghijk","playlist?list=PLfixture","@native/videos?hl=en"})
        {
            var url=new Uri("https://www.youtube.com/"+path+"&authuser=0&%61uthuser=2");var selected=session.Select(url);
            Assert(selected.Query.EndsWith("&authuser=1",StringComparison.Ordinal)&&!selected.Query.Contains("authuser=0")&&!selected.Query.Contains("%61uthuser"));
            Assert(YouTubeAccountSession.WithoutSelection(selected).AbsoluteUri=="https://www.youtube.com/"+path);
        }
    }
    private static async Task Api(bool changed)
    {
        var posts=0;var gets=0;using var http=Http((message,_)=>{if(message.Method==HttpMethod.Get){gets++;return Task.FromResult(AuthenticationBatchChecks.Json(changed?Config.Replace("account-fixture","other-account"):Config));}posts++;Assert(message.Headers.Contains("Authorization")&&message.RequestUri!.Host=="www.youtube.com");return Task.FromResult(AuthenticationBatchChecks.Json("{}",posts==1?HttpStatusCode.Unauthorized:HttpStatusCode.OK));},Jar());using var session=new YouTubeAccountSession(http,1);session.Observe(Config,new("https://www.youtube.com/watch?v=abcdefghijk"),default);
        try{await session.PostAsync("browse",new{context=new{client=new{clientName="WEB"}}},"1","fixture.web",1024,default);Assert(!changed&&posts==2&&gets==1);}catch(InvalidDataException)when(changed){Assert(posts==1&&gets==1);}
    }
    internal static string VimeoConfig(string url="https://video.example/source.mp4",int view=1,int id=123)=>JsonSerializer.Serialize(new{view,video=new{id,title="fixture",duration=1},request=new{files=new{progressive=new[]{new{url,mime="video/mp4",quality="720p",width=128,height=72}}}}});
    private static async Task Password(string mode)
    {
        var submits=0;using var http=Http(async(message,ct)=>{if(message.Method==HttpMethod.Get){var response=AuthenticationBatchChecks.Json(VimeoConfig(view:4,id:mode=="identity"?124:123));if(mode=="redirect"){response.StatusCode=HttpStatusCode.Redirect;response.Headers.Location=new("https://foreign.example/");}return response;}submits++;Assert(message.RequestUri!.Host=="player.vimeo.com"&&message.RequestUri.AbsolutePath=="/video/123/check-password");var form=await message.Content!.ReadAsStringAsync(ct);Assert(form=="password="+Uri.EscapeDataString(Convert.ToBase64String(Encoding.UTF8.GetBytes("päss&word"))));return AuthenticationBatchChecks.Json(mode=="wrong"?"false":VimeoConfig(view:3));});
        var request=new NativeDownloadRequest{Url=new("https://vimeo.com/123"),OutputDirectory=".",Authentication=new(){VideoPassword="päss&word"}};
        try{var page=await new VimeoProvider().ExtractAsync(request,http,default);Assert(mode=="valid"&&submits==1&&page.Media.Count==1);}catch(Exception e)when(mode!="valid"&&e is InvalidDataException or NotSupportedException or HttpRequestException){Assert(submits==(mode=="wrong"?1:0));}
    }
    internal static string ApiJson(string url="https://video.example/source.mp4",int id=123)=>JsonSerializer.Serialize(new{uri="/videos/"+id,name="API fixture",duration=1,files=new[]{new{quality="hd",type="video/mp4",width=128,height=72,link=url}}});
    private static async Task ApiVimeo(){using var http=Http((message,_)=>{Assert(message.RequestUri!.Host=="api.vimeo.com"&&message.Headers.Authorization?.Parameter=="fixture-token");return Task.FromResult(AuthenticationBatchChecks.Json(ApiJson()));});var page=await new VimeoProvider().ExtractAsync(new(){Url=new("https://vimeo.com/123"),OutputDirectory=".",Authentication=new(){AccessToken="fixture-token"}},http,default);Assert(page.Media.Count==1&&page.PlaybackAuthorization is not null);}
    private static void ApiBoundaries(){SabrFixture.Throws<InvalidDataException>(()=>VimeoSession.ParseApi(ApiJson(id:124),"123"));SabrFixture.Throws<NotSupportedException>(()=>VimeoSession.ParseApi("{\"uri\":\"/videos/123\",\"files\":[]}","123"));SabrFixture.Throws<InvalidDataException>(()=>VimeoSession.ParseApi(ApiJson("http://unsafe.example/video.mp4"),"123"));Assert(VimeoSession.ParseApi(ApiJson().Replace("\"width\":128","\"width\":4294967296"),"123").Media[0].Width is null);}
    private static PageMetadata Page(string generation,int height=72)=>new(){Id="fixture",Media=[new(){Id="video",Url=new("https://video.example/"+generation+".mp4"),Kind=MediaKind.Progressive,Extension="mp4",Height=height,HasVideo=true}]};
    private static async Task Lease(string mode)
    {
        var renewals=0;var reads=0;var initial=Page("one");if(mode=="expire")initial=new(){Id="fixture",Media=[new(){Id="video",Url=initial.Media[0].Url,Kind=MediaKind.Progressive,Extension="mp4",Height=72,HasVideo=true,ResourceExpires=DateTimeOffset.UtcNow.AddSeconds(-1)}]};
        using var lease=new RenewableMediaAuthorization(initial,_=>{renewals++;return Task.FromResult(Page("two",mode=="changed"?144:72));});using var owner=Http((message,_)=>{reads++;return Task.FromResult(AuthenticationBatchChecks.Json("opaque",mode=="denied"||message.RequestUri!.AbsolutePath.Contains("one")?HttpStatusCode.Forbidden:HttpStatusCode.OK));});using var media=owner.WithAuthorization(lease);using var stop=new CancellationTokenSource();if(mode=="cancel")stop.Cancel();
        try
        {
            if(mode=="foreign"){await lease.AuthorizeAsync(new("https://foreign.example/video.mp4"),default);throw new InvalidOperationException();}
            async Task Read(){using var request=new HttpRequestMessage(HttpMethod.Get,initial.Media[0].Url);using var response=await media.SendAsync(request,stop.Token);Assert(request.RequestUri==initial.Media[0].Url&&(mode=="denied"?response.StatusCode==HttpStatusCode.Forbidden:response.IsSuccessStatusCode));}
            if(mode=="concurrent")await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Read()));else await Read();Assert(renewals==1&&reads<=(mode=="concurrent"?16:2));
        }
        catch(Exception e)when(mode is "changed" or "foreign" or "cancel"&&e is InvalidDataException or OperationCanceledException){if(mode is "foreign" or "cancel")Assert(reads==0&&renewals==0);}
    }
    private static async Task Redirect(bool foreign)
    {
        var calls=0;var page=Page("one");using var auth=new RenewableMediaAuthorization(page,_=>Task.FromResult(page),u=>u.Host=="cdn.vimeocdn.com");using var owner=Http((message,_)=>{calls++;if(calls==1){var r=AuthenticationBatchChecks.Json("",HttpStatusCode.Redirect);r.Headers.Location=new(foreign?"https://foreign.example/leak":"https://cdn.vimeocdn.com/video.mp4");return Task.FromResult(r);}Assert(message.Headers.Range is not null);return Task.FromResult(AuthenticationBatchChecks.Json("data"));});using var media=owner.WithAuthorization(auth);var request=new HttpRequestMessage(HttpMethod.Get,page.Media[0].Url);request.Headers.Range=new(0,5);
        try{using var response=await media.SendAsync(request,default);Assert(!foreign&&calls==2&&request.RequestUri==page.Media[0].Url);}catch(InvalidDataException)when(foreign){Assert(calls==1);}
    }
    private static async Task Sabr(string mode)
    {
        var reads=0;var renewals=0;var endpoint=mode=="expire"?new Uri("https://r1.googlevideo.com/videoplayback?expire=1"):SabrFixture.Endpoint;
        using var http=Http((_,_)=>{reads++;if(mode=="denied"||mode=="http"&&reads==1)return Task.FromResult(AuthenticationBatchChecks.Json("",HttpStatusCode.Forbidden));using var bytes=new MemoryStream();if(mode=="protection")SabrFixture.Status(bytes,3);else if(mode=="reload"&&reads==1)SabrFixture.Part(bytes,46,[]);else{SabrFixture.Segment(bytes,SabrFixture.Video,1,0,[1]);SabrFixture.Segment(bytes,SabrFixture.Video,1,1,[2],new(0,1000,1000));SabrFixture.Metadata(bytes,SabrFixture.Video,1,1000);}return Task.FromResult(SabrFixture.Response(bytes.ToArray()));});
        using var output=new MemoryStream();var sink=new SabrTrackSink(new Dictionary<SabrFormatId,Stream>{[SabrFixture.Video]=output});var session=new SabrHttpSession(http){Renew=_=>{renewals++;return Task.FromResult(SabrFixture.Endpoint);}};
        try{await session.DownloadAsync(endpoint,SabrFixture.VideoId,sink,(_,_)=>new(){ClientName=1,ClientVersion="fixture",PlaybackConfiguration=[1],VideoFormats=[SabrFixture.Video]},default,allowStartupPlayback:true);Assert(mode is not ("protection" or "denied")&&renewals==1&&output.Length==2);}catch(NotSupportedException)when(mode is "protection" or "denied"){Assert(renewals==(mode=="protection"?0:2));}
    }
}
