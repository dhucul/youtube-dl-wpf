using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Protocols.Sabr;
internal static class LiveSabrRecoveryChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>new[]{"same","facts","payload","missing","corrupt","reordered","secrets"}.Select(mode=>("Live SABR recovery journal "+mode,(Func<Task>)(()=>Journal(mode))))
        .Concat([("Live SABR recovery explicit protection remains terminal",(Func<Task>)Protection)]);
    private static async Task Journal(string mode)
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            using var http=AdaptiveAccountChecks.Http((_,_)=>throw new InvalidOperationException());var target=Path.Combine(directory,"source");var url=new Uri("https://r1.googlevideo.com/videoplayback?proof=fixture-secret");var a=Path.Combine(directory,"a");var b=Path.Combine(directory,"b");string root;var replay=false;
            async Task<IndexedDashDownloader.ResourceVersion?> Fetch(string path,CancellationToken ct){if(File.Exists(path))File.Delete(path);await AdaptiveFragmentCheckpoint.DurableWriteAsync(path,replay&&mode=="payload"?"other"u8.ToArray():"data"u8.ToArray(),ct);return null;}
            using(var first=await TimelineCheckpoint.OpenAsync(http,target,"source",default)){await first.CaptureAsync("a","time0",url,null,"data"u8.ToArray(),a,Fetch,default);await first.CaptureAsync("b","time1",url,null,"data"u8.ToArray(),b,Fetch,default);root=Directory.GetDirectories(directory,".native-fragments-*").Single();}
            File.Delete(a);File.Delete(b);replay=true;if(mode=="corrupt")await File.WriteAllTextAsync(Path.Combine(root,"timeline.json"),"{broken");
            try
            {
                using var retry=await TimelineCheckpoint.OpenAsync(http,target,"source",default);retry.RequireVisible(mode=="missing"?["b"]:["a","b"]);
                if(mode=="reordered")await retry.CaptureAsync("b","time1",url,null,"data"u8.ToArray(),b,Fetch,default);
                await retry.CaptureAsync("a",mode=="facts"?"changed":"time0",url,null,mode=="payload"?"other"u8.ToArray():"data"u8.ToArray(),a,Fetch,default);
                AdaptiveRecoveryChecks.Assert(mode is "same" or "reordered" or "secrets"&&await File.ReadAllTextAsync(a)=="data");
                if(mode=="secrets")foreach(var file in Directory.GetFiles(root,"*.json")){var data=await File.ReadAllTextAsync(file);AdaptiveRecoveryChecks.Assert(!data.Contains("fixture-secret")&&!data.Contains("https://"));}
            }
            catch(Exception error)when(mode is "facts" or "payload" or "missing" or "corrupt"&&error is InvalidDataException or System.Text.Json.JsonException){}
        }
        finally{var allowed=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"test-data"))+Path.DirectorySeparatorChar;if(!Path.GetFullPath(directory).StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new IOException();Directory.Delete(directory,true);}
    }
    private static Task Protection(){SabrFixture.Throws<NotSupportedException>(()=>{var state=new SabrControlState(SabrFixture.VideoId,true);var writer=new YoutubeDl.Engine.Protocols.ProtobufWriter();writer.UInt64(1,3);state.Accept(new(58,writer.ToArray()));});return Task.CompletedTask;}
}

