using System.Text.Json.Nodes;
using YoutubeDl.Engine.Media;
internal static class DurableLiveChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>new[]{"same","body","timing","scope","partial","linked","budget","null","duplicate","secrets","replace"}.Select(mode=>("Durable live snapshot "+mode,(Func<Task>)(()=>Snapshot(mode))));
    private static async Task Snapshot(string mode)
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            using var http=AdaptiveAccountChecks.Http((_,_)=>throw new InvalidOperationException());var destination=Path.Combine(directory,"captions");
            using(var journal=await LiveCaptionCheckpoint.OpenAsync(http,destination,"source",default))
            {
                await journal.SaveAsync([LiveCaptionCheckpoint.Row("segment0","time0","{\"Time\":0}","WEBVTT\n\n"u8.ToArray())],default);
                if(mode=="replace")await journal.SaveAsync([LiveCaptionCheckpoint.Row("segment0","time0","{\"Time\":0}","WEBVTT\n\nnew"u8.ToArray())],default);
            }
            AdaptiveRecoveryChecks.Assert(LiveCaptionCheckpoint.Exists(destination,"source"));
            var root=Directory.GetDirectories(directory,".native-fragments-*").Single();var file=Path.Combine(root,"captions.json");
            if(mode is "body" or "timing" or "null")
            {
                var document=JsonNode.Parse(await File.ReadAllTextAsync(file))!;document["Entries"]![0]![mode=="body"?"Bytes":mode=="timing"?"Description":"Bytes"]=mode=="body"?"b3RoZXI=":mode=="timing"?"{\"Time\":1}":null;
                await File.WriteAllTextAsync(file,document.ToJsonString());
            }
            if(mode=="partial")await File.WriteAllTextAsync(file+"."+Guid.NewGuid().ToString("N")+".tmp","{partial");
            if(mode=="linked"){File.Delete(file);Directory.CreateDirectory(file);}
            var failed=false;
            try
            {
                using var retry=await LiveCaptionCheckpoint.OpenAsync(http,destination,mode=="scope"?"other":"source",default);
                if(mode=="scope")AdaptiveRecoveryChecks.Assert(retry.Entries.Count==0);
                else AdaptiveRecoveryChecks.Assert(retry.Entries.Count==1);
                if(mode=="budget")await retry.SaveAsync([LiveCaptionCheckpoint.Row("too-big","facts","{}",new byte[8*1024*1024+1])],default);
                if(mode=="duplicate")await retry.SaveAsync([retry.Entries[0],retry.Entries[0]],default);
                if(mode=="secrets")foreach(var path in Directory.GetFiles(root,"*.json")){var text=await File.ReadAllTextAsync(path);AdaptiveRecoveryChecks.Assert(!text.Contains("https://")&&!text.Contains("proof")&&!text.Contains("cookie"));}
                if(mode=="replace")AdaptiveRecoveryChecks.Assert(System.Text.Encoding.UTF8.GetString(retry.Entries[0].Bytes).EndsWith("new"));
            }
            catch(Exception error)when(error is InvalidDataException or IOException){failed=true;}
            AdaptiveRecoveryChecks.Assert(failed==(mode is "body" or "timing" or "null" or "linked" or "budget" or "duplicate"),"Snapshot result: "+mode);
        }
        finally{Directory.Delete(directory,true);}
    }
}
