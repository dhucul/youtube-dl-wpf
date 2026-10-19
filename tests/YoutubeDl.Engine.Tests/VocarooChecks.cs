using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.Vocaroo;

internal static class VocarooChecks
{
    internal const string Modern = "1AbCd234Ef56";
    internal const string Legacy = "AbCd234Ef56";
    internal static IEnumerable<(string Name,Func<Task> Test)> All =>
    [
        ("Vocaroo public descriptors retain exact recording bytes digest and audio identity",Facts),
        ("Vocaroo current legacy short embed and direct aliases bind the same recording",Aliases),
        ("Vocaroo ten-character recording IDs use the published current gateway",ShortIds),
        ("Vocaroo unsupported routes access selectors and private addresses cannot fall through",Boundaries),
        ("Vocaroo public gateway declarations cannot redirect discovery to other hosts",Gateways),
        ("Vocaroo configuration remains one bounded literal without script execution",Literals),
        ("Vocaroo conflicting filename storage and digest descriptors fail",Identity),
        ("Vocaroo missing sizes media roles and record descriptors remain unavailable",Incomplete),
        ("Vocaroo page and recording redirects cannot substitute another identity",Redirects),
        ("Vocaroo unavailable and partial source responses fail explicitly",Availability),
        ("Vocaroo simulation inspects descriptors without fetching media or creating output",Simulation),
        ("Vocaroo stable case-sensitive archives skip every alias before network access",Archives),
        ("Vocaroo discovery isolates authentication and cancels before public requests",Transport),
        ("Vocaroo oversized public configuration cannot reach media requests",Limits)
    ];
    internal static NativeDownloadRequest Request(string output,string id=Modern) => new(){Url=new("https://vocaroo.com/"+id),OutputDirectory=output,OutputTemplate="%(id)s.%(ext)s",Retries=0};
    internal static Uri Media(string id) => new("https://"+(id.Length==11?"media":"media1")+".vocaroo.com/mp3/"+id);
    internal static JsonObject Configuration() => new(){["MEDIA_MP3_FILE_URL"]="https://media.vocaroo.com/mp3/",["MEDIA_MP3_FILE_URL1"]="https://media1.vocaroo.com/mp3/"};
    internal static string Html(JsonObject? config=null) => "<script>var process={env : "+(config??Configuration()).ToJsonString()+"}</script>";
    internal static MediaHttp Http(List<(HttpMethod Method,Uri Url)> calls,byte[]? bytes=null,Func<HttpRequestMessage,HttpResponseMessage>? body=null,
        Action<HttpRequestMessage,HttpResponseMessage>? mutate=null,MediaHttpOptions? options=null,string? html=null)
    {
        bytes ??= "fixture audio"u8.ToArray(); var payload=bytes;
        return new(new Handler(request=>
        {
            calls.Add((request.Method,request.RequestUri!));
            Assert(!request.Headers.Contains("Authorization")&&!request.Headers.Contains("Cookie"),"Vocaroo leaked caller authentication.");
            var id=request.RequestUri!.AbsolutePath.Trim('/').Split('/')[^1]; HttpResponseMessage response;
            if(request.RequestUri.Host=="vocaroo.com") response=new(HttpStatusCode.OK){RequestMessage=request,Content=new StringContent(html??Html(),System.Text.Encoding.UTF8,"text/html")};
            else if(request.Method==HttpMethod.Head)
            {
                Assert(request.Headers.Referrer==new Uri("https://vocaroo.com/"),"Vocaroo descriptor referrer changed.");
                response=new(HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent([])};
                response.Content.Headers.ContentType=new("audio/mpeg");response.Content.Headers.ContentLength=payload.Length;
                response.Content.Headers.ContentDisposition=new("attachment"){FileName="Vocaroo "+id+".mp3"};
                response.Headers.Add("x-bz-file-name","media/mp3/"+id);response.Headers.Add("x-bz-content-sha1",Convert.ToHexString(SHA1.HashData(payload)).ToLowerInvariant());
            }
            else response=body?.Invoke(request)??throw new InvalidOperationException("Core Vocaroo check fetched audio.");
            mutate?.Invoke(request,response);return response;
        }),options);
    }
    private static async Task Facts()
    {
        var calls=new List<(HttpMethod,Uri)>();using var http=Http(calls);var page=await new VocarooProvider().ExtractAsync(Request("."),http,default);
        Assert(page.Id==Modern&&page.ArchiveIdentity==new DownloadArchiveIdentity("vocaroo",Modern)&&page.Media.Single().Url==Media(Modern)&&page.Media[0].VerificationFileSize==13&&page.Media[0].VerificationSha1?.Length==40&&page.Media[0].IsCompleteMedia&&page.DurationSeconds is null,"Vocaroo descriptor invented duration or lost exact source facts.");
        Assert(calls.Count==2&&calls[0].Item1==HttpMethod.Get&&calls[1].Item1==HttpMethod.Head&&MediaProviderRegistry.CreateDefault().Resolve(Request(".").Url).Info.Id=="vocaroo","Vocaroo registration or public methods changed.");
    }
    private static async Task Aliases()
    {
        foreach(var id in new[]{Modern,Legacy}) foreach(var url in new[]{"http://www.vocaroo.com/"+id,"https://vocaroo.com/i/"+id,"https://voca.ro/"+id,"https://vocaroo.com/embed/"+id+"?autoplay=0","https://vocaroo.com/"+id+"?utm_source=fixture",Media(id).AbsoluteUri})
        {using var http=Http([]);var page=await new VocarooProvider().ExtractAsync(Request(".",id)with{Url=new(url)},http,default);Assert(page.Id==id&&page.SourcePageUrl==new Uri("https://vocaroo.com/"+id)&&page.Media[0].Url==Media(id),"Vocaroo alias changed the recording or gateway.");}
    }
    private static async Task ShortIds()
    {const string id="AbCd1234Ef";using var http=Http([]);var page=await new VocarooProvider().ExtractAsync(Request(".",id),http,default);Assert(page.Media[0].Url.Host=="media1.vocaroo.com","Vocaroo current ten-character routing changed.");}
    private static async Task Boundaries()
    {
        var provider=new VocarooProvider();var registry=MediaProviderRegistry.CreateDefault();
        foreach(var url in new[]{"https://vocaroo.com/apps/main-api/delete/id/token","https://vocaroo.com/private.mp3","https://vocaroo.com/"+Modern+"?ownerToken=private","https://vocaroo.com/"+Modern+"#t=1","https://user:secret@vocaroo.com/"+Modern,"https://vocaroo.com:8443/"+Modern,"https://vocaroo.com/embed/"+Modern+"?autoplay=2","https://vocaroo.com/embed/"+Modern+"?autoplay=0&autoplay=1","https://vocaroo.com/2AbCd234Ef56","https://media.vocaroo.com/mp3/"+Modern,"https://cdn.vocaroo.com/private.mp3"})
        {Assert(!provider.CanHandle(new(url)),"Unsupported Vocaroo address accepted.");await Expect<Exception>(()=>Task.FromResult(registry.Resolve(new(url))));}
    }
    private static async Task Gateways()
    {
        foreach(var value in new JsonNode?[]{"https://other.example/mp3/","https://media1.vocaroo.com/mp3/?private=token",null,99})
        {var config=Configuration();config["MEDIA_MP3_FILE_URL1"]=value;var calls=new List<(HttpMethod,Uri)>();using var http=Http(calls,html:Html(config));await Expect<InvalidDataException>(()=>new VocarooProvider().ExtractAsync(Request("."),http,default));Assert(calls.Count==1,"Invalid gateway fetched a recording descriptor.");}
    }
    private static async Task Literals()
    {
        foreach(var html in new[]{Html()+Html(),"<script>var process={env : loadConfig()}</script>",Html().Replace("}</script>","};run();</script>"),"<!--"+Html()+"-->",Html().Replace("\"MEDIA_MP3_FILE_URL1\":", "\"MEDIA_MP3_FILE_URL\":")})
        {using var http=Http([],html:html);await Expect<InvalidDataException>(()=>new VocarooProvider().ExtractAsync(Request("."),http,default));}
    }
    private static async Task Identity()
    {
        foreach(var change in new Action<HttpResponseMessage>[] {response=>response.Content.Headers.ContentDisposition=new("attachment"){FileName="Vocaroo another.mp3"},response=>{response.Headers.Remove("x-bz-file-name");response.Headers.Add("x-bz-file-name","media/mp3/other");},response=>response.Headers.Add("x-bz-file-name","media/mp3/"+Modern)})
        {using var http=Http([],mutate:(request,response)=>{if(request.Method==HttpMethod.Head)change(response);});await Expect<InvalidDataException>(()=>new VocarooProvider().ExtractAsync(Request("."),http,default));}
        using var invalid=Http([],mutate:(request,response)=>{if(request.Method==HttpMethod.Head){response.Headers.Remove("x-bz-content-sha1");response.Headers.Add("x-bz-content-sha1","unsupported");}});await Expect<NotSupportedException>(()=>new VocarooProvider().ExtractAsync(Request("."),invalid,default));
    }
    private static async Task Incomplete()
    {
        foreach(var change in new Action<HttpResponseMessage>[] {response=>response.Content.Headers.ContentLength=0,response=>response.Content.Headers.ContentLength=null,response=>response.Content.Headers.ContentType=new("text/html"),response=>{response.Content.Headers.ContentDisposition=null;response.Headers.Remove("x-bz-file-name");}})
        {using var http=Http([],mutate:(request,response)=>{if(request.Method==HttpMethod.Head)change(response);});await Expect<InvalidDataException>(()=>new VocarooProvider().ExtractAsync(Request("."),http,default));}
    }
    private static async Task Redirects()
    {
        foreach(var head in new[]{false,true})
        {using var http=Http([],mutate:(request,response)=>{if((request.Method==HttpMethod.Head)==head)response.RequestMessage=new(request.Method,head?new Uri("https://media1.vocaroo.com/mp3/1Other000000"):new Uri("https://vocaroo.com/1Other000000"));});await Expect<InvalidDataException>(()=>new VocarooProvider().ExtractAsync(Request("."),http,default));}
    }
    private static async Task Availability()
    {
        foreach(var status in new[]{HttpStatusCode.Forbidden,HttpStatusCode.Gone})
        {using var http=Http([],mutate:(request,response)=>{if(request.Method==HttpMethod.Head)response.StatusCode=status;});await Expect<HttpRequestException>(()=>new VocarooProvider().ExtractAsync(Request("."),http,default));}
        using var partial=Http([],mutate:(request,response)=>{if(request.Method==HttpMethod.Head)response.StatusCode=HttpStatusCode.PartialContent;});await Expect<InvalidDataException>(()=>new VocarooProvider().ExtractAsync(Request("."),partial,default));
    }
    private static async Task Simulation()
    {
        var directory=Path.Combine(Path.GetTempPath(),"vocaroo-simulate-"+Guid.NewGuid().ToString("N"));var calls=new List<(HttpMethod,Uri)>();
        var engine=new NativeEngine([new VocarooProvider()],_=>Http(calls));var result=await engine.DownloadAsync(Request(directory)with{Simulate=true},null,null,default);
        Assert(result.Simulated&&result.OutputPaths.Count==0&&calls.Count==2&&!Directory.Exists(directory),"Vocaroo simulation fetched media or changed output.");
    }
    private static async Task Archives()
    {
        using var files=new Files();var archive=Path.Combine(files.Path,"archive.txt");await File.WriteAllTextAsync(archive,"vocaroo "+Modern+"\n");var engine=new NativeEngine([new VocarooProvider()],_=>throw new InvalidOperationException("Vocaroo archive reached HTTP."));
        foreach(var url in new[]{"https://voca.ro/"+Modern,"https://vocaroo.com/embed/"+Modern,Media(Modern).AbsoluteUri})
        {var result=await engine.DownloadAsync(Request(files.Path)with{Url=new(url),DownloadArchiveFile=archive},null,null,default,_=>throw new InvalidOperationException("Archive emitted completion."));Assert(result.Skipped,"Vocaroo alias repeated archived audio.");}
    }
    private static async Task Transport()
    {
        using var http=Http([],options:new(){Headers={["Authorization"]="private fixture",["Cookie"]="private=fixture"}});await new VocarooProvider().ExtractAsync(Request("."),http,default);
        using var stop=new CancellationTokenSource();stop.Cancel();await Expect<OperationCanceledException>(()=>new VocarooProvider().ExtractAsync(Request("."),http,stop.Token));
        var cookies=new CookieContainer();cookies.Add(new Uri("https://vocaroo.com"),new Cookie("private","fixture"));using var opaque=Http([],options:new(){CookieContainer=cookies});await Expect<InvalidOperationException>(()=>new VocarooProvider().ExtractAsync(Request("."),opaque,default));
    }
    private static async Task Limits()
    {var calls=new List<(HttpMethod,Uri)>();using var http=Http(calls,html:new string('a',512*1024+1)+Html());await Expect<InvalidDataException>(()=>new VocarooProvider().ExtractAsync(Request("."),http,default));Assert(calls.Count==1,"Oversized configuration reached media.");}
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> run):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(run(request));}}
    private sealed class Files:IDisposable
    {internal string Path{get;}=System.IO.Path.Combine(AppContext.BaseDirectory,"test-data","vocaroo-"+Guid.NewGuid().ToString("N"));internal Files()=>Directory.CreateDirectory(Path);public void Dispose()=>Directory.Delete(Path,true);}
    private static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static async Task Expect<T>(Func<Task> action)where T:Exception{try{await action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
}
