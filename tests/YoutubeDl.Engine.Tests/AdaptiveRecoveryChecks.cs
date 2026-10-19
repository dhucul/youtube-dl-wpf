using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
internal static class AdaptiveRecoveryChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
        new[]{"strong","weak","missing","wildcard","changed","length","ignored-range","wrong-range","denied","corrupt","truncated","receipt","null-digest","scope","range","inline","secrets"}.Select(mode=>("Adaptive recovery checkpoint "+mode,(Func<Task>)(()=>Checkpoint(mode))))
        .Concat(new[]{"same","tokens","video","duration","sequence","range","init"}.Select(mode=>("Adaptive recovery declared identity "+mode,(Func<Task>)(()=>Identity(mode)))))
        .Concat([("Adaptive recovery concurrent owners wait and cancel without altering saved fragments",(Func<Task>)Lease),("Adaptive recovery mismatched ownership never overwrites local checkpoints",(Func<Task>)Owner)]);
    internal static void Assert(bool value,string text="Recovery expectation failed"){if(!value)throw new InvalidOperationException(text);}
    private static async Task Checkpoint(string mode)
    {
        using var files=new Files();var bytes=Encoding.UTF8.GetBytes("original");var full=Encoding.UTF8.GetBytes("01234567");var calls=0;var probes=0;var second=false;var tag=mode=="weak"?"W/\"a\"":mode=="missing"?null:mode=="wildcard"?"*":"\"a\"";var range=mode=="range"?new HlsByteRange(2,4):null;
        using var http=AdaptiveAccountChecks.Http((message,ct)=>{
            probes++;Assert(second&&message.RequestUri!.Query.Contains("issuer-secret")&&message.Headers.Range!.Ranges.Single().From==(range?.Offset??0));
            var response=new HttpResponseMessage(mode=="denied"?HttpStatusCode.Forbidden:mode=="changed"?HttpStatusCode.PreconditionFailed:mode=="ignored-range"?HttpStatusCode.OK:HttpStatusCode.PartialContent){Content=new ByteArrayContent(mode=="ignored-range"?bytes:[bytes[0]])};
            response.Headers.ETag=new("\"a\"");if(mode!="ignored-range")response.Content.Headers.ContentRange=new(range?.Offset??0,mode=="wrong-range"?1:range?.Offset??0,mode=="length"?9:8);return Task.FromResult(response);
        });
        var uri=new Uri("https://cdn.example/chunk?signature=issuer-secret");var target=Path.Combine(files.Directory,"assembled");var staging=Path.Combine(files.Directory,"copy1");
        async Task<IndexedDashDownloader.ResourceVersion?> Fetch(string path,CancellationToken ct){calls++;if(File.Exists(path))File.Delete(path);await AdaptiveFragmentCheckpoint.DurableWriteAsync(path,range is null?bytes:full[2..6],ct);return new(tag,null,8);}
        string root;using(var cache=await AdaptiveFragmentCheckpoint.OpenAsync(http,target,"scope",default)){root=cache.DirectoryPath;await cache.CopyAsync(0,uri,range,mode=="inline"?bytes:null,staging,Fetch,default);}
        Assert(await File.ReadAllTextAsync(staging)==(range is null?"original":"2345"));File.Delete(staging);
        if(mode=="corrupt")await File.WriteAllTextAsync(Path.Combine(root,"fragment-000000.bin"),"tampered");if(mode=="truncated")await File.WriteAllTextAsync(Path.Combine(root,"fragment-000000.bin"),"x");if(mode=="receipt")await File.WriteAllTextAsync(Path.Combine(root,"fragment-000000.bin.json"),"{bad");
        if(mode=="null-digest"){var node=JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root,"fragment-000000.bin.json")))!;node["Digest"]=null;await File.WriteAllTextAsync(Path.Combine(root,"fragment-000000.bin.json"),node.ToJsonString());}
        second=true;
        using(var cache=await AdaptiveFragmentCheckpoint.OpenAsync(http,target,mode=="scope"?"other scope":"scope",default))
        {
            try{await cache.CopyAsync(0,uri,range,mode=="inline"?bytes:null,staging,Fetch,default);Assert(mode!="denied");}
            catch(HttpRequestException)when(mode=="denied"){Assert(calls==1&&probes==1&&!File.Exists(staging));return;}
            var reuse=mode is "strong" or "ignored-range" or "range" or "inline" or "secrets";Assert(calls==(reuse?1:2)&&await File.ReadAllTextAsync(staging)==(range is null?"original":"2345"));
            if(mode is "weak" or "missing" or "wildcard" or "scope" or "receipt" or "null-digest" or "inline")Assert(probes==0);
            if(mode=="secrets")foreach(var file in System.IO.Directory.GetFiles(root,"*.json")){var json=await File.ReadAllTextAsync(file);Assert(!json.Contains("issuer-secret")&&!json.Contains("https://"));}
            cache.Complete();
        }
    }
    private static async Task Identity(string mode)
    {
        async Task<string> Get(int generation,string id,int duration,int sequence,int start,bool init)
        {
            var root=new Uri("https://cdn.example/master.m3u8?signature=g"+generation);var page=new PageMetadata{Id=id,Media=[new(){Id="hls",Url=root,Kind=MediaKind.Hls,Extension="m3u8"}]};
            using var owner=AdaptiveAccountChecks.Http((_,_)=>throw new InvalidOperationException("Identity should not fetch data."));using var lease=new RenewableMediaAuthorization(page,_=>Task.FromResult(page));
            var child=new Uri("https://cdn.example/chunk.m4s?signature=g"+generation);var manifest="#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:"+sequence+"\n"+(init?"#EXT-X-MAP:URI=\"https://cdn.example/init.mp4?signature=g"+generation+"\"\n":"")+(start>0?"#EXT-X-BYTERANGE:4@"+start+"\n":"")+"#EXTINF:"+duration+",\n"+child+"\n#EXT-X-ENDLIST\n";
            await lease.ObserveManifestAsync(new(root,manifest,"application/x-mpegURL"),default);using var http=owner.WithAuthorization(lease);return http.RecoveryIdentity(child);
        }
        var first=await Get(0,"video-a",1,0,0,false);var second=await Get(mode=="tokens"?1:0,mode=="video"?"video-b":"video-a",mode=="duration"?2:1,mode=="sequence"?1:0,mode=="range"?2:0,mode=="init");
        Assert((first==second)==(mode is "same" or "tokens"));
    }
    private static async Task Lease()
    {
        using var files=new Files();using var http=AdaptiveAccountChecks.Http((_,_)=>throw new InvalidOperationException());using var first=await AdaptiveFragmentCheckpoint.OpenAsync(http,Path.Combine(files.Directory,"file"),"facts",default);using var stop=new CancellationTokenSource(150);
        try{using var second=await AdaptiveFragmentCheckpoint.OpenAsync(http,Path.Combine(files.Directory,"file"),"facts",stop.Token);throw new InvalidOperationException("Concurrent owner acquired a live checkpoint.");}catch(OperationCanceledException)when(stop.IsCancellationRequested){}
        first.Complete();
    }
    private static async Task Owner()
    {
        using var files=new Files();using var http=AdaptiveAccountChecks.Http((_,_)=>throw new InvalidOperationException());var output=Path.Combine(files.Directory,"file");string root;
        using(var first=await AdaptiveFragmentCheckpoint.OpenAsync(http,output,"facts",default)){root=first.DirectoryPath;await first.CopyAsync(0,new("https://cdn.example/file"),null,null,Path.Combine(files.Directory,"copy"),async(path,ct)=>{await AdaptiveFragmentCheckpoint.DurableWriteAsync(path,"prior"u8.ToArray(),ct);return new("\"owner\"",null,5);},default);}
        await File.WriteAllTextAsync(Path.Combine(root,"scope.json"),"other owner");
        try{using var second=await AdaptiveFragmentCheckpoint.OpenAsync(http,output,"facts",default);throw new InvalidOperationException("Changed ownership accepted.");}catch(InvalidDataException){Assert(await File.ReadAllTextAsync(Path.Combine(root,"fragment-000000.bin"))=="prior");}
    }
    private sealed class Files:IDisposable
    {
        private static readonly string Root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"test-data"))+Path.DirectorySeparatorChar;
        internal string Directory {get;}=Path.Combine(Root,Guid.NewGuid().ToString("N"));
        internal Files()=>System.IO.Directory.CreateDirectory(Directory);
        public void Dispose(){if(!Path.GetFullPath(Directory).StartsWith(Root,StringComparison.OrdinalIgnoreCase))throw new IOException();System.IO.Directory.Delete(Directory,true);}
    }
}
