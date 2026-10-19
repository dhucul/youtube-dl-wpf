using System.Diagnostics;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;

internal static class WriteCleanupChecks
{
    internal static readonly string[] Phases=["locator-writing","locator-flushed","locator-replaced","receipt-plan-writing","receipt-plan-flushed","receipt-plan-replaced",
        "output-writing:0","output-writing:1","output-flushed:0","receipt-commit-writing","receipt-commit-flushed","receipt-commit-replaced",
        "receipt-archive-plan-writing","archive-writing","archive-flushed","archive-replaced","receipt-archive-writing","receipt-archive-flushed",
        "receipt-claim-writing","receipt-claim-flushed","receipt-claim-replaced","cleanup-sibling:0","cleanup-sibling:1","cleanup-entry:0","cleanup-receipt","cleanup-empty","cleanup-root"];
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>Phases.Select(phase=>("Write cleanup crash "+phase,(Func<Task>)(()=>Crash(phase,"top"))))
        .Concat(new[]{"nested","absolute","parent","legacy-nested","legacy-absolute","changed"}.Select(layout=>("Write cleanup destination "+layout,(Func<Task>)(()=>Crash("cleanup-entry:0",layout)))))
        .Concat(new[]{"output-writing:0","receipt-commit-writing","archive-writing","cleanup-sibling:0","cleanup-entry:0"}.Select(phase=>("Write cleanup failure "+phase,(Func<Task>)(()=>Failure(phase,false)))))
        .Concat([("Write cleanup canceled output preserves previous files",(Func<Task>)(()=>Failure("output-writing:0",true))),
            ("Write cleanup changed locator refuses cleanup",(Func<Task>)(()=>Crash("cleanup-entry:0","locator-edit"))),
            ("Write cleanup edited output refuses partially completed cleanup",(Func<Task>)(()=>Crash("cleanup-entry:0","output-edit"))),
            ("Write cleanup rollback interruption restores remaining previous files",(Func<Task>)Rollback),
            ("Write cleanup locator archive collision preserves previous outputs",(Func<Task>)ArchiveCollision)]);
    internal static NativeDownloadRequest Request(string directory,string phase,string layout)
    {
        var template=layout switch{"nested" or "legacy-nested"=>"nested/%(title)s/movie.%(ext)s","absolute" or "legacy-absolute"=>Path.Combine(directory,"external","%(title)s","movie.%(ext)s"),"parent"=>"../outside-"+Path.GetFileName(directory)+"/movie.%(ext)s",_=>"movie.%(ext)s"};
        return PublicationRecoveryChecks.Request(Path.Combine(directory,"base"),!phase.StartsWith("receipt-claim",StringComparison.Ordinal)) with {OutputTemplate=template};
    }
    internal static string Destination(string directory,string layout)=>layout switch{"nested" or "legacy-nested"=>Path.Combine(directory,"base","nested","Movie","movie.mp4"),
        "absolute" or "legacy-absolute"=>Path.Combine(directory,"external","Movie","movie.mp4"),"parent"=>Path.Combine(directory,"outside-"+Path.GetFileName(directory),"movie.mp4"),_=>Path.Combine(directory,"base","movie.mp4")};
    internal static async Task<int> Worker(string directory,string phase,string layout)
    {
        PublicationFaults.Observer=async value=>{if(value!=phase)return;await File.WriteAllTextAsync(Path.Combine(directory,"ready"),Environment.ProcessId.ToString());await Task.Delay(Timeout.Infinite);};
        await PublicationRecoveryChecks.Engine().DownloadAsync(Request(directory,phase,layout),null,null,default,path=>File.AppendAllText(Path.Combine(directory,"callbacks"),path+"\n"));return 1;
    }
    internal static async Task Stop(string directory,string phase,string layout)
    {
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{typeof(WriteCleanupChecks).Assembly.Location,"--write-cleanup-worker",directory,phase,layout})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();var ready=false;
        try
        {
            for(var count=0;count<500&&!process.HasExited;count++){try{ready=File.Exists(Path.Combine(directory,"ready"))&&int.Parse(await File.ReadAllTextAsync(Path.Combine(directory,"ready")))==process.Id;}catch(IOException){}if(ready)break;await Task.Delay(40);}
            Check(ready,"Worker did not reach "+phase+": "+(process.HasExited?await errors:"timeout"));process.Kill(true);await process.WaitForExitAsync();
        }
        finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}await output;await errors;}
    }
    private static async Task Crash(string phase,string layout)
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));var main=Destination(directory,layout);var captions=Path.ChangeExtension(main,"en.vtt");Directory.CreateDirectory(Path.GetDirectoryName(main)!);Directory.CreateDirectory(Path.Combine(directory,"base"));
        try
        {
            await File.WriteAllTextAsync(main,"old media");await File.WriteAllTextAsync(captions,"old captions");var archive=Path.Combine(directory,"base","archive.txt");await File.WriteAllTextAsync(archive,"prior record\n");
            await Stop(directory,phase,layout);var archived=File.ReadAllLines(archive).Contains("publication-fixture movie");var claimed=File.Exists(Path.Combine(directory,"callbacks"))?File.ReadAllLines(Path.Combine(directory,"callbacks")).Length:0;
            if(phase.StartsWith("output-writing:",StringComparison.Ordinal))
            {
                using var document=System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Directory.GetFiles(directory,"publication.json",SearchOption.AllDirectories).Single()));
                var item=document.RootElement.GetProperty("Items")[int.Parse(phase.Split(':')[1])];var size=new FileInfo(item.GetProperty("Temporary").GetString()!).Length;
                Check(size>0&&size<item.GetProperty("After").GetProperty("Bytes").GetInt64(),"The killed output write did not leave persisted partial bytes.");
            }
            if(phase=="archive-writing"){var partial=Directory.GetFiles(directory,".native-archive-*.tmp",SearchOption.AllDirectories).Single();Check(new FileInfo(partial).Length>0&&new FileInfo(partial).Length<new FileInfo(archive).Length,"The killed archive write was not partial.");}
            if(phase.EndsWith("-writing",StringComparison.Ordinal)&&(phase.StartsWith("receipt-",StringComparison.Ordinal)||phase=="locator-writing"))
                Check(Directory.GetFiles(directory,"*.tmp",SearchOption.AllDirectories).Any(path=>new FileInfo(path).Length>0),"The killed receipt write left no persisted prefix.");
            if(layout.StartsWith("legacy",StringComparison.Ordinal))foreach(var index in Directory.GetFiles(Path.Combine(directory,"base"),".native-recovery-*.json"))File.Delete(index);
            if(layout=="locator-edit"){var file=Directory.GetFiles(Path.Combine(directory,"base"),".native-recovery-*.json").Single();var node=System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(file))!;node["Root"]=directory;await File.WriteAllTextAsync(file,node.ToJsonString());}
            if(layout=="output-edit")await File.WriteAllTextAsync(main,"user edit");
            var calls=0;var callbacks=0;var request=Request(directory,phase,layout);if(layout=="changed")request=request with {OutputTemplate="different/%(title)s.%(ext)s",WriteDescription=true};
            var failure=layout is "locator-edit" or "output-edit";
            try
            {
                var result=await PublicationRecoveryChecks.Engine(()=>calls++).DownloadAsync(request,null,null,default,_=>callbacks++);
                Check(!failure&&result.Succeeded,"Retry did not complete.");Check(await File.ReadAllTextAsync(main)=="complete new media"&&(await File.ReadAllTextAsync(captions)).Contains("Saved cue."),"Saved output or captions changed.");
                if(archived||phase=="receipt-commit-replaced"||phase.StartsWith("receipt-archive-plan",StringComparison.Ordinal)||phase.StartsWith("archive-",StringComparison.Ordinal)||phase.StartsWith("receipt-claim",StringComparison.Ordinal))Check(calls==0,"Committed media transferred again.");
                if(archived)Check(callbacks==0&&result.Skipped,"Archive recovery repeated completion.");
                else if(phase=="receipt-claim-replaced")Check(callbacks==0&&claimed==0,"Claimed external callback repeated.");
                else Check(callbacks==1,"Incomplete callback did not complete once.");
                Check(Directory.GetDirectories(directory,".native-download-*",SearchOption.AllDirectories).Length==0&&Directory.GetFiles(directory,".native-save-*",SearchOption.AllDirectories).Length==0&&Directory.GetFiles(directory,".native-backup-*",SearchOption.AllDirectories).Length==0&&Directory.GetFiles(directory,".native-archive-*.tmp",SearchOption.AllDirectories).Length==0&&Directory.GetFiles(directory,".native-recovery-*",SearchOption.AllDirectories).Length==0,"Recovery debris remains.");
            }
            catch(Exception e)when(failure&&e is IOException or InvalidDataException or System.Text.Json.JsonException){Check(callbacks==0&&calls==0,"Invalid recovery transferred media.");}
            if(layout=="output-edit")Check(await File.ReadAllTextAsync(main)=="user edit","User edit removed.");
        }
        finally{Directory.Delete(directory,true);}
    }
    private static async Task Failure(string phase,bool cancel)
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(directory,"base"));var main=Destination(directory,"top");var captions=Path.ChangeExtension(main,"en.vtt");
        try
        {
            await File.WriteAllTextAsync(main,"old media");await File.WriteAllTextAsync(captions,"old captions");var archive=Path.Combine(directory,"base","archive.txt");await File.WriteAllTextAsync(archive,"prior record\n");using var stop=new CancellationTokenSource();var fired=false;var callbacks=0;
            PublicationFaults.Observer=value=>{if(value==phase&&!fired){fired=true;if(cancel){stop.Cancel();throw new OperationCanceledException(stop.Token);}throw new IOException("Injected write or cleanup interruption.");}return Task.CompletedTask;};
            try{var result=await PublicationRecoveryChecks.Engine().DownloadAsync(Request(directory,phase,"top"),null,null,stop.Token,_=>callbacks++);Check(!result.Succeeded,"Failed write reported success.");}catch(Exception e)when(e is IOException or OperationCanceledException){}
            finally{PublicationFaults.Observer=null;}
            Check(fired,"Write boundary was not exercised.");
            if(phase.StartsWith("output-",StringComparison.Ordinal)||phase.StartsWith("receipt-commit",StringComparison.Ordinal))Check(await File.ReadAllTextAsync(main)=="old media"&&await File.ReadAllTextAsync(captions)=="old captions"&&callbacks==0&&await File.ReadAllTextAsync(archive)=="prior record\n","Previous files or archive were lost.");
            var result2=await PublicationRecoveryChecks.Engine().DownloadAsync(Request(directory,phase,"top"),null,null,default,_=>callbacks++);Check(result2.Succeeded&&await File.ReadAllTextAsync(main)=="complete new media","Write/cleanup retry failed.");
            Check(File.ReadAllLines(archive).Count(line=>line=="publication-fixture movie")==1&&callbacks==1,"Retry repeated archive or callbacks.");
        }
        finally{PublicationFaults.Observer=null;Directory.Delete(directory,true);}
    }
    private static async Task Rollback()
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var first=Path.Combine(directory,"first");var second=Path.Combine(directory,"second");await File.WriteAllTextAsync(first,"old first");await File.WriteAllTextAsync(second,"old second");
            using(var workspace=new DownloadWorkspace(first,new("https://rollback.example/")))
            {
                var a=workspace.RawPath("a");var b=workspace.RawPath("b");await File.WriteAllTextAsync(a,"new first");await File.WriteAllTextAsync(b,"new second");var fired=false;
                PublicationFaults.Observer=phase=>{if(phase=="installed:0"||phase=="rollback-restored:0"&&!fired){if(phase=="rollback-restored:0")fired=true;throw new IOException("Interrupted restore.");}return Task.CompletedTask;};
                try{await workspace.PublishAsync([new(a,first,[]),new(b,second,[])],false,default);throw new InvalidOperationException("Rollback interruption accepted.");}catch(AggregateException){}
                finally{PublicationFaults.Observer=null;}
            }
            using(var retry=new DownloadWorkspace(first,new("https://rollback.example/"))){Check(await File.ReadAllTextAsync(first)=="old first"&&await File.ReadAllTextAsync(second)=="old second","Interrupted rollback lost originals.");retry.CompleteWithoutPublishing();}
        }
        finally{PublicationFaults.Observer=null;Directory.Delete(directory,true);}
    }
    internal static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static async Task ArchiveCollision()
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var request=PublicationRecoveryChecks.Request(directory);var main=Path.Combine(directory,"movie.mp4");await File.WriteAllTextAsync(main,"previous output");
            var scope=AdaptiveFragmentCheckpoint.Hash(Path.GetFullPath(main)+"\n"+request.Url.AbsoluteUri);var archive=RecoveryIndex.PathFor(directory,scope);var transfers=0;
            try{await PublicationRecoveryChecks.Engine(()=>transfers++).DownloadAsync(request with {DownloadArchiveFile=archive},null,null,default,_=>throw new InvalidOperationException("Collision callback fired."));throw new InvalidOperationException("Locator archive collision accepted.");}catch(IOException){}
            Check(transfers==0&&!File.Exists(archive)&&await File.ReadAllTextAsync(main)=="previous output","Locator replaced archive or media.");
        }
        finally{Directory.Delete(directory,true);}
    }
}
