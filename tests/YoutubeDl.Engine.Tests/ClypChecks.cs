using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.Clyp;

internal static class ClypChecks
{
    internal const string Id="abcd1234";
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
    [
        ("Clyp public API retains recording identity dates duration and complete secure formats",Facts),
        ("Clyp page API widget stream and download aliases bind selected native formats",Aliases),
        ("Clyp private routes queries and unsupported collections cannot fall through",Boundaries),
        ("Clyp public stream availability remains distinct from download permission",Access),
        ("Clyp identity canonical and supplied storage conflicts fail before media",Ownership),
        ("Clyp missing malformed duplicate and incomplete metadata fails explicitly",Metadata),
        ("Clyp selected widgets bind recording duration resource and public access",Widgets),
        ("Clyp widgets reject executable ambiguous and unbound player data",Literals),
        ("Clyp descriptors reject changed redirects types partial and incomplete sizes",Descriptors),
        ("Clyp denial and missing selected formats cannot fetch alternate hidden playback",Availability),
        ("Clyp native simulation discovers facts without audio or output side effects",Simulation),
        ("Clyp stable recording archives skip aliases before all network access",Archives),
        ("Clyp anonymous discovery and cancellation preserve caller authentication boundaries",Transport),
        ("Clyp oversized metadata cannot reach selected playback descriptors",Limits)
    ];
    internal static NativeDownloadRequest Request(string output)=>new(){Url=new("https://clyp.it/"+Id),OutputDirectory=output,OutputTemplate="%(id)s.%(ext)s",Retries=0};
    internal static JsonObject Data()=>new(){["Status"]="Public",["AudioFileId"]=Id,["Title"]="Track",["Description"]="Published description",["Duration"]=2,["Url"]="https://clyp.it/"+Id,
        ["Mp3Url"]="http://a.clyp.it/"+Id+".mp3",["SecureMp3Url"]="https://a.clyp.it/"+Id+".mp3",["OggUrl"]="https://a.clyp.it/"+Id+".ogg",["SecureOggUrl"]="https://a.clyp.it/"+Id+".ogg",["DateCreated"]="2026-01-31T12:30:00Z"};
    internal static JsonObject Widget()=>new(){["AudioFileId"]=Id,["Duration"]=2,["UserProvidedToken"]=null,["PlayerPageUrl"]="https://clyp.it/"+Id,["PlayerWidgetUrl"]="https://clyp.it/"+Id+"/widget",["Mp3Url"]="https://a.clyp.it/"+Id+".mp3",["OggUrl"]="https://a.clyp.it/"+Id+".ogg"};
    internal static string Html(JsonObject? model=null)=>"<script>createPlayer("+(model??Widget()).ToJsonString()+");function createPlayer(model){ /* publisher UI */ }</script>";
    internal static MediaHttp Http(List<(HttpMethod Method,Uri Url)> calls,Dictionary<string,byte[]>? media=null,Action<string,JsonObject>? mutate=null,
        Action<HttpRequestMessage,HttpResponseMessage>? responseChange=null,Func<HttpRequestMessage,HttpResponseMessage>? body=null,MediaHttpOptions? options=null,string? raw=null,string? html=null)
    {
        media??=new(){["mp3"]="fixture audio"u8.ToArray(),["ogg"]="fixture ogg"u8.ToArray()};var resources=media;
        return new(new Handler(request=>
        {
            var url=request.RequestUri!;calls.Add((request.Method,url));Assert(!request.Headers.Contains("Authorization")&&!request.Headers.Contains("Cookie"),"Clyp leaked caller authentication.");HttpResponseMessage response;
            if(url.Host=="api.clyp.it")
            {var data=Data();mutate?.Invoke("api",data);response=new(HttpStatusCode.OK){RequestMessage=request,Content=new StringContent(raw??data.ToJsonString(),System.Text.Encoding.UTF8,"application/json")};}
            else if(url.Host=="clyp.it")
            {var model=Widget();mutate?.Invoke("widget",model);response=new(HttpStatusCode.OK){RequestMessage=request,Content=new StringContent(html??Html(model),System.Text.Encoding.UTF8,"text/html")};}
            else if(request.Method==HttpMethod.Head)
            {
                var ext=url.AbsolutePath[^3..];response=new(HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent([])};response.Content.Headers.ContentLength=resources[ext].Length;response.Content.Headers.ContentType=new(ext=="mp3"?"audio/mpeg":"audio/ogg");
            }
            else response=body?.Invoke(request)??throw new InvalidOperationException("Core Clyp check fetched audio.");
            responseChange?.Invoke(request,response);return response;
        }),options);
    }
    private static async Task Facts()
    {
        var calls=new List<(HttpMethod,Uri)>();using var http=Http(calls);var page=await new ClypProvider().ExtractAsync(Request("."),http,default);
        Assert(page.Id==Id&&page.ArchiveIdentity==new DownloadArchiveIdentity("clyp",Id)&&page.DurationSeconds==2&&page.UploadDate==new DateOnly(2026,1,31)&&page.Media.Count==2&&page.Media.All(source=>source.IsCompleteMedia&&source.Url.Scheme=="https"&&source.VerificationFileSize>0),"Clyp identity/date/source facts changed.");
        Assert(calls.Count==3&&calls[0].Item1==HttpMethod.Get&&calls.Skip(1).All(call=>call.Item1==HttpMethod.Head)&&MediaProviderRegistry.CreateDefault().Resolve(Request(".").Url).Info.Id=="clyp","Clyp registration or read-only request flow changed.");
    }
    private static async Task Aliases()
    {
        foreach(var url in new[]{"http://www.clyp.it/"+Id,"https://api.clyp.it/"+Id,"https://clyp.it/"+Id+"?utm_source=fixture","https://clyp.it/"+Id+"/widget?autoplay=false","https://clyp.it/"+Id+".mp3","https://api.clyp.it/"+Id+".ogg","https://a.clyp.it/"+Id+".ogg"})
        {using var http=Http([]);var page=await new ClypProvider().ExtractAsync(Request(".")with{Url=new(url)},http,default);Assert(page.Id==Id&&(!url.Contains(".ogg")||page.Media.Single().Extension=="ogg"),"Clyp alias changed recording or selected format.");}
    }
    private static async Task Boundaries()
    {
        var registry=MediaProviderRegistry.CreateDefault();foreach(var url in new[]{"https://clyp.it/"+Id+"?token=private","https://clyp.it/"+Id+"#t=2","https://user:secret@clyp.it/"+Id,"https://clyp.it:8443/"+Id,"https://clyp.it/playlist/"+Id,"https://clyp.it/"+Id+"/widget?autoplay=maybe","https://clyp.it/"+Id+"/widget?autoplay=0&autoplay=1","https://api.clyp.it/me","https://clyp.it/private.mp3","https://a.clyp.it/"+Id+".wav"})
        {Assert(!new ClypProvider().CanHandle(new(url)),"Unsupported Clyp address accepted.");await Expect<Exception>(()=>Task.FromResult(registry.Resolve(new(url))));}
    }
    private static async Task Access()
    {
        foreach(var status in new[]{"Private","PrivateDownloadDisabled","Deleted","DistributionDisabled","Unknown"})
        {using var http=Http([],mutate:(kind,node)=>{if(kind=="api")node["Status"]=status;});await Expect<NotSupportedException>(()=>new ClypProvider().ExtractAsync(Request("."),http,default));}
        using var streaming=Http([],mutate:(kind,node)=>{if(kind=="api")node["Status"]="DownloadDisabled";});var page=await new ClypProvider().ExtractAsync(Request("."),streaming,default);Assert(page.Media.Count==2&&page.MetadataFields["download_allowed"] is false,"Public playback was confused with original download permission.");
        await Expect<NotSupportedException>(()=>new ClypProvider().ExtractAsync(Request(".")with{Url=new("https://api.clyp.it/"+Id+".mp3")},streaming,default));
    }
    private static async Task Ownership()
    {
        foreach(var change in new Action<JsonObject>[] {node=>node["AudioFileId"]="different",node=>node["Url"]="https://clyp.it/other123",node=>node["Url"]="https://api.clyp.it/"+Id,node=>node["SecureMp3Url"]="https://a.clyp.it/other123.mp3",node=>node["Mp3Url"]="http://other.example/"+Id+".mp3",node=>node["SecureOggUrl"]="https://a.clyp.it/preview/"+Id+".ogg"})
        {using var http=Http([],mutate:(kind,node)=>{if(kind=="api")change(node);});await Expect<InvalidDataException>(()=>new ClypProvider().ExtractAsync(Request("."),http,default));}
    }
    private static async Task Metadata()
    {
        foreach(var change in new Action<JsonObject>[] {node=>node.Remove("Status"),node=>node["Duration"]=0,node=>node["Duration"]="NaN",node=>node["Title"]=""})
        {using var http=Http([],mutate:(kind,node)=>{if(kind=="api")change(node);});await Expect<Exception>(()=>new ClypProvider().ExtractAsync(Request("."),http,default));}
        using var duplicate=Http([],raw:"{\"AudioFileId\":\"abcd1234\",\"AudioFileId\":\"other123\"}");await Expect<InvalidDataException>(()=>new ClypProvider().ExtractAsync(Request("."),duplicate,default));
    }
    private static async Task Widgets()
    {
        foreach(var change in new Action<JsonObject>[] {node=>node["AudioFileId"]="other123",node=>node["Duration"]=3,node=>node["UserProvidedToken"]="private",node=>node["UserProvidedToken"]=42,node=>node["PlayerPageUrl"]="https://clyp.it/other123",node=>node["Mp3Url"]="https://a.clyp.it/other123.mp3"})
        {using var http=Http([],mutate:(kind,node)=>{if(kind=="widget")change(node);});await Expect<InvalidDataException>(()=>new ClypProvider().ExtractAsync(Request(".")with{Url=new("https://clyp.it/"+Id+"/widget")},http,default));}
    }
    private static async Task Literals()
    {
        foreach(var html in new[]{Html()+Html(),"<script>createPlayer(getModel());</script>",Html().Replace(");function", "+run());function"),"<!--"+Html()+"-->"})
        {using var http=Http([],html:html);await Expect<InvalidDataException>(()=>new ClypProvider().ExtractAsync(Request(".")with{Url=new("https://clyp.it/"+Id+"/widget")},http,default));}
    }
    private static async Task Descriptors()
    {
        foreach(var change in new Action<HttpRequestMessage,HttpResponseMessage>[] {(request,response)=>response.Content.Headers.ContentLength=0,(request,response)=>response.Content.Headers.ContentType=new("text/html"),(request,response)=>response.StatusCode=HttpStatusCode.PartialContent,(request,response)=>response.RequestMessage=new(HttpMethod.Head,"https://a.clyp.it/other123.mp3")})
        {using var http=Http([],responseChange:(request,response)=>{if(request.Method==HttpMethod.Head)change(request,response);});await Expect<InvalidDataException>(()=>new ClypProvider().ExtractAsync(Request("."),http,default));}
        using var apiRedirect=Http([],responseChange:(request,response)=>{if(request.RequestUri!.Host=="api.clyp.it")response.RequestMessage=new(HttpMethod.Get,"https://api.clyp.it/other123");});await Expect<InvalidDataException>(()=>new ClypProvider().ExtractAsync(Request("."),apiRedirect,default));
    }
    private static async Task Availability()
    {
        using var denied=Http([],responseChange:(request,response)=>{if(request.RequestUri!.Host=="api.clyp.it")response.StatusCode=HttpStatusCode.Forbidden;});await Expect<HttpRequestException>(()=>new ClypProvider().ExtractAsync(Request("."),denied,default));
        using var missing=Http([],mutate:(kind,node)=>{if(kind=="api"){node.Remove("OggUrl");node.Remove("SecureOggUrl");}});await Expect<NotSupportedException>(()=>new ClypProvider().ExtractAsync(Request(".")with{Url=new("https://a.clyp.it/"+Id+".ogg")},missing,default));
    }
    private static async Task Simulation()
    {var directory=Path.Combine(Path.GetTempPath(),"clyp-sim-"+Guid.NewGuid().ToString("N"));var calls=new List<(HttpMethod,Uri)>();var engine=new NativeEngine([new ClypProvider()],_=>Http(calls));var result=await engine.DownloadAsync(Request(directory)with{Simulate=true},null,null,default);Assert(result.Simulated&&calls.Count==3&&!Directory.Exists(directory),"Clyp simulation fetched media or created output.");}
    private static async Task Archives()
    {using var files=new Files();var archive=Path.Combine(files.Path,"archive.txt");await File.WriteAllTextAsync(archive,"clyp "+Id+"\n");var engine=new NativeEngine([new ClypProvider()],_=>throw new InvalidOperationException("Clyp archive reached HTTP."));foreach(var url in new[]{"https://clyp.it/"+Id,"https://clyp.it/"+Id+"/widget","https://a.clyp.it/"+Id+".mp3"}){var result=await engine.DownloadAsync(Request(files.Path)with{Url=new(url),DownloadArchiveFile=archive},null,null,default,_=>throw new InvalidOperationException("Archive emitted completion."));Assert(result.Skipped,"Clyp alias repeated archived media.");}}
    private static async Task Transport()
    {using var http=Http([],options:new(){Headers={["Authorization"]="private fixture",["Cookie"]="private=fixture"}});await new ClypProvider().ExtractAsync(Request("."),http,default);using var stop=new CancellationTokenSource();stop.Cancel();await Expect<OperationCanceledException>(()=>new ClypProvider().ExtractAsync(Request("."),http,stop.Token));}
    private static async Task Limits()
    {using var http=Http([],raw:new string(' ',1024*1024+1)+Data().ToJsonString());await Expect<InvalidDataException>(()=>new ClypProvider().ExtractAsync(Request("."),http,default));}
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> run):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(run(request));}}
    private sealed class Files:IDisposable{internal string Path{get;}=System.IO.Path.Combine(AppContext.BaseDirectory,"test-data","clyp-"+Guid.NewGuid().ToString("N"));internal Files()=>Directory.CreateDirectory(Path);public void Dispose()=>Directory.Delete(Path,true);}
    private static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static async Task Expect<T>(Func<Task> action)where T:Exception{try{await action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
}
