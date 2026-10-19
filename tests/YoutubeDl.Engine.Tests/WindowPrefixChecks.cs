using System.Text.Json;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Protocols.Sabr;
using YoutubeDl.Engine.Protocols;
internal static class WindowPrefixChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>new[]{"copy","corrupt","missing","receipt","metadata","null","foreign","secrets"}.Select(mode=>("Window prefix journal "+mode,(Func<Task>)(()=>Journal(mode))))
        .Concat(new[]{"same","bytes","time"}.Select(mode=>("Window prefix retransmission "+mode,(Func<Task>)(()=>Replay(mode)))));
    private static async Task Journal(string mode)
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            using var http=AdaptiveAccountChecks.Http((_,_)=>throw new InvalidOperationException("A saved copy must not issue HTTP."));
            var target=Path.Combine(directory,"output");var stage=Path.Combine(directory,"stage");var uri=new Uri("https://cdn.example/data?secret=must-not-save");
            TimelineCheckpoint.Record record;string root;
            using(var journal=await TimelineCheckpoint.OpenAsync(http,target,"source",default))
            {
                await journal.CaptureAsync("id","facts",uri,null,"media"u8.ToArray(),stage,async(path,ct)=>{await File.WriteAllBytesAsync(path,"media"u8.ToArray(),ct);return null;},default,"{\"Time\":0}");
                record=journal.Records.Single();root=Directory.GetDirectories(directory,".native-fragments-*").Single();
            }
            File.Delete(stage);var data=Path.Combine(root,"fragment-000000.bin");
            if(mode=="corrupt")await File.WriteAllTextAsync(data,"other");
            if(mode=="missing")File.Delete(data);
            if(mode=="receipt")await File.WriteAllTextAsync(data+".json","{}");
            if(mode is "metadata" or "null")
            {
                var file=Path.Combine(root,"timeline.json");var text=await File.ReadAllTextAsync(file);
                var node=System.Text.Json.Nodes.JsonNode.Parse(text)!;node["Records"]![0]![mode=="metadata"?"Metadata":"Identity"]=mode=="metadata"?"{\"Time\":1}":null;await File.WriteAllTextAsync(file,node.ToJsonString());
            }
            var failed=false;
            try
            {
                using var retry=await TimelineCheckpoint.OpenAsync(http,target,"source",default);
                await retry.CopySavedAsync(mode=="foreign"?record with {Digest=new string('A',64)}:retry.Records.Single(),stage,default);
                if(mode=="secrets")foreach(var file in Directory.GetFiles(root,"*.json")){var text=await File.ReadAllTextAsync(file);AdaptiveRecoveryChecks.Assert(!text.Contains("must-not-save")&&!text.Contains("https://"));}
                AdaptiveRecoveryChecks.Assert(await File.ReadAllTextAsync(stage)=="media");
            }
            catch(InvalidDataException){failed=true;}
            AdaptiveRecoveryChecks.Assert(failed==(mode is "corrupt" or "missing" or "receipt" or "metadata" or "null" or "foreign"),"Unexpected prefix journal result: "+mode);
        }
        finally{Directory.Delete(directory,true);}
    }
    private static Task Replay(string mode)
    {
        using var assembler=new SabrSegmentAssembler(SabrFixture.VideoId);var segment=new SabrSegment(SabrFixture.Video,2,false,1000,1000,"data"u8.ToArray(),new(1000,1000,1000));assembler.RememberRestored(segment);
        using var stream=new MemoryStream();SabrFixture.Segment(stream,SabrFixture.Video,1,2,mode=="bytes"?"other"u8.ToArray():"data"u8.ToArray(),new(1000,mode=="time"?999:1000,1000));stream.Position=0;
        return Read();
        async Task Read()
        {
            var failed=false;try{await foreach(var part in UmpReader.ReadAsync(stream)){AdaptiveRecoveryChecks.Assert(assembler.Accept(part) is null);}}catch(InvalidDataException){failed=true;}
            AdaptiveRecoveryChecks.Assert(failed==(mode!="same"));
        }
    }
}
