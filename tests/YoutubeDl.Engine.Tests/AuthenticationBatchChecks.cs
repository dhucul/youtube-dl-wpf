using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.PeerTube;

internal static class AuthenticationBatchChecks
{
    internal const string Id="e9c1c3e4-479f-4cf8-a80d-33d81d62f430";
    internal const string Video="""{"uuid":"e9c1c3e4-479f-4cf8-a80d-33d81d62f430","name":"Account fixture","duration":2,"privacy":{"id":1},"files":[{"id":1,"fileUrl":"https://account.example/static/video.mp4","hasAudio":true,"hasVideo":true}]}""";
    internal static NativeDownloadRequest Request()=>new(){Url=new("https://account.example/videos/watch/"+Id),OutputDirectory=Path.GetTempPath(),Authentication=new(){Username="fixture-user",Password="fixture-secret",TwoFactor="123456"}};
    public static IEnumerable<(string Name,Func<Task> Test)> All =>
    [
        ("Authentication batch declares provider capabilities and rejects other sites before HTTP",Capabilities),
        ("Authentication batch uses one login and scoped two-factor/account headers",()=>Session(false)),
        ("Authentication batch refreshes rejected tokens exactly once",()=>Session(true)),
        ("Authentication batch account sessions are isolated between jobs",Isolation),
        ("Authentication batch video passwords bind API access and scoped static tokens",Password),
        ("Authentication batch refuses credential redirects and reflected errors",Redirect),
        ("Authentication batch bounded invalid token descriptors cannot establish a session",InvalidTokens),
        ("Authentication batch secret fields stay out of JSON and record diagnostics",()=>Sync(Privacy)),
        ("Authentication batch netrc uses exact hosts and explicit username matching",()=>Sync(Netrc)),
        ("Authentication batch netrc quoting and macros do not execute commands",()=>Sync(NetrcQuoted)),
        ("Authentication batch netrc missing/default/duplicate entries fail without secrets",()=>Sync(NetrcFailures)),
        ("Authentication batch read-only netrc values follow explicit overrides",NetrcFile),
        ("Authentication batch extra argument order preserves independent cookie choices",()=>Sync(Arguments)),
        ("Authentication batch cancellation leaves no account request or output",Cancellation)
    ];
    private static Task Sync(Action action){action();return Task.CompletedTask;}
    private static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    internal sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> response):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>response(request,ct);}
    internal static MediaHttp Http(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> handle)=>new(new Handler(handle),new(){AllowAutoRedirect=false});
    internal static HttpResponseMessage Json(string json,HttpStatusCode code=HttpStatusCode.OK)=>new(code){Content=new StringContent(json)};
    internal static string Tokens(string token)=>JsonSerializer.Serialize(new{access_token=token,refresh_token="refresh-value",token_type="Bearer",expires_in=3600});
    private static async Task Capabilities()
    {
        var calls=0;var engine=new NativeEngine(httpFactory:_=>{calls++;return Http((_,_)=>Task.FromResult(Json(Video)));});
        try{await engine.InspectAsync(Request() with {Url=new("https://vimeo.com/12345")},default);throw new InvalidOperationException("Unsupported login was accepted.");}catch(NotSupportedException){}
        Assert(calls==0,"Unsupported login opened an HTTP session.");
        try{await engine.InspectAsync(Request() with {Url=new("http://account.example/videos/watch/"+Id)},default);throw new InvalidOperationException("HTTP login accepted.");}catch(NotSupportedException){}
        Assert(calls==0,"HTTP login opened a session.");
    }
    private static async Task Session(bool refresh)
    {
        var grants=0;var videos=0;var engine=new NativeEngine(httpFactory:_=>Http(async(message,ct)=>
        {
            var path=message.RequestUri!.AbsolutePath;
            if(path.EndsWith("/local"))return Json("""{"client_id":"client","client_secret":"client-secret"}""");
            if(path.EndsWith("/users/token"))
            {var body=await message.Content!.ReadAsStringAsync(ct);grants++;Assert(grants==1?body.Contains("grant_type=password")&&body.Contains("password=fixture-secret")&&message.Headers.GetValues("x-peertube-otp").Single()=="123456":body.Contains("grant_type=refresh_token")&&!body.Contains("fixture-secret")&&!message.Headers.Contains("x-peertube-otp"),"Login/refresh fields or two-factor scope changed.");return Json(Tokens(grants==1?"initial":"renewed"));}
            Assert(message.Headers.Authorization?.Parameter==(grants==1?"initial":"renewed"),"API lost account authentication.");
            if(path.EndsWith(Id)){videos++;if(refresh&&videos==1)return Json("fixture-secret",HttpStatusCode.Unauthorized);return Json(Video);}
            return Json("{}",HttpStatusCode.NotFound);
        }));
        var result=await engine.InspectAsync(Request(),default);Assert(result.Id==Id&&grants==(refresh?2:1)&&videos==(refresh?2:1),"Account request/refresh count changed.");
    }
    private static async Task Isolation()
    {
        var logins=0;var engine=new NativeEngine(httpFactory:_=>Http((message,_)=>Task.FromResult(message.RequestUri!.AbsolutePath.EndsWith("/local")?Json("""{"client_id":"c","client_secret":"s"}"""):message.RequestUri.AbsolutePath.EndsWith("/users/token")?Json(Tokens("job"+(++logins))):message.RequestUri.AbsolutePath.EndsWith(Id)?Json(Video):Json("{}",HttpStatusCode.NotFound))));
        await engine.InspectAsync(Request(),default);await engine.InspectAsync(Request(),default);Assert(logins==2,"A later job reused account tokens.");
    }
    private static async Task Password()
    {
        using var http=Http((message,_)=>
        {Assert(message.Headers.GetValues("x-peertube-video-password").Single()=="video-secret"&&message.Headers.Authorization is null,"Video password or account scope changed.");return Task.FromResult(message.Method==HttpMethod.Post?Json(JsonSerializer.Serialize(new{files=new{token="file-token",expires=DateTimeOffset.UtcNow.AddMinutes(5)}})):message.RequestUri!.AbsolutePath.EndsWith(Id)?Json(Video.Replace("\"id\":1},\"files\"","\"id\":5},\"files\"")):Json("{}",HttpStatusCode.NotFound));});
        var page=await new PeerTubeProvider().ExtractAsync(Request() with {Authentication=new(){VideoPassword="video-secret"}},http,default);
        var authorization=page.PlaybackAuthorization!;var lease=await authorization.AuthorizeAsync(new("https://account.example/static/video.mp4"),default);Assert(lease.Url.Query=="?videoFileToken=file-token"&&!authorization.AppliesTo(new("https://account.example/static/other.mp4"))&&!authorization.AppliesTo(new("https://cdn.example/static/video.mp4")),"File token escaped its exact resource.");
    }
    private static async Task Redirect()
    {
        var requests=0;var engine=new NativeEngine(httpFactory:_=>Http((_,_)=>{requests++;var response=Json("fixture-secret",HttpStatusCode.TemporaryRedirect);response.Headers.Location=new("https://other.example/token");return Task.FromResult(response);}));
        try{await engine.InspectAsync(Request(),default);throw new InvalidOperationException("Account redirect accepted.");}catch(InvalidDataException ex){Assert(!ex.Message.Contains("fixture-secret"),"Credential body leaked.");}Assert(requests==1,"Account redirect was followed.");
    }
    private static async Task InvalidTokens()
    {
        foreach(var token in new[]{"{}","[]","{\"access_token\":\"private value\"}",Tokens("t").Replace("3600","0")})
        {var engine=new NativeEngine(httpFactory:_=>Http((message,_)=>Task.FromResult(message.RequestUri!.AbsolutePath.EndsWith("/local")?Json("""{"client_id":"c","client_secret":"s"}"""):Json(token))));try{await engine.InspectAsync(Request(),default);throw new InvalidOperationException("Malformed token accepted.");}catch(InvalidDataException ex){Assert(!ex.Message.Contains("private value"),"Malformed token leaked.");}}
    }
    private static void Privacy(){var request=Request();Assert(!request.ToString().Contains("fixture-secret")&&!JsonSerializer.Serialize(request).Contains("fixture-secret")&&!JsonSerializer.Serialize(request.Authentication).Contains("123456"),"Credentials entered diagnostics/metadata.");}
    private static void Netrc(){var a=NativeNetrc.Parse("machine other.example login stranger password other\nmachine account.example login first password one\nmachine account.example login second password two","account.example","second",default);Assert(a.Username=="second"&&a.Password=="two","Exact host/user netrc selection changed.");}
    private static void NetrcQuoted(){var a=NativeNetrc.Parse("macdef ignored\nlaunch something\n\nmachine account.example login \"user name\" password \"space \\\"quote\\\"\" # ignored\n","account.example","",default);Assert(a.Username=="user name"&&a.Password=="space \"quote\"","Quoted netrc fields/macros changed.");Assert(NativeNetrc.Parse("machine account.example login user password macdef","account.example","",default).Password=="macdef","A keyword inside a credential became a macro directive.");}
    private static void NetrcFailures(){foreach(var s in new[]{"default login user password secret","machine other.example login u password p","machine account.example login u password p machine account.example login u password p","machine account.example login u","machine account.example login u password p password duplicate","machine account.example password \"unclosed"}){try{NativeNetrc.Parse(s,"account.example","",default);throw new InvalidOperationException("Ambiguous or missing account accepted.");}catch(InvalidDataException ex){Assert(!ex.Message.Contains("secret"),"netrc error leaked credentials.");}}}
    private static async Task NetrcFile(){var path=Path.Combine(Path.GetTempPath(),"native-netrc-"+Guid.NewGuid());await File.WriteAllTextAsync(path,"machine account.example login fixture-user password from-file");try{var r=await NativeAuthentication.PrepareAsync(Request() with {Authentication=new(){UseNetrc=true,NetrcLocation=path,Password="explicit"}},new PeerTubeProvider(),default);Assert(r.Authentication.Username=="fixture-user"&&r.Authentication.Password=="explicit"&&await File.ReadAllTextAsync(path)=="machine account.example login fixture-user password from-file","Netrc override or read-only file changed.");}finally{File.Delete(path);}}
    private static void Arguments(){var r=NativeExtraArguments.Apply(Request(),["--netrc","--no-netrc","--username=next","--password","replacement","--video-password=v","--no-cookies"]);Assert(r.Authentication.Username=="next"&&r.Authentication.Password=="replacement"&&!r.Authentication.UseNetrc&&r.Authentication.VideoPassword=="v"&&r.CookieFile is null,"Ordered credentials changed unrelated fields.");}
    private static async Task Cancellation(){using var cts=new CancellationTokenSource();cts.Cancel();var calls=0;var engine=new NativeEngine(httpFactory:_=>{calls++;throw new InvalidOperationException();});try{await engine.InspectAsync(Request(),cts.Token);throw new InvalidOperationException("Cancellation ignored.");}catch(OperationCanceledException){}Assert(calls==0,"Cancelled account started HTTP.");}
}
