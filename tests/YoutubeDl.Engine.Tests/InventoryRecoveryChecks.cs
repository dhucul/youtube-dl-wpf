using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;

internal static class InventoryRecoveryChecks
{
    internal static readonly string[] Phases=["prepared","copied","installed:0","committed","inventory-writing","inventory-flushed","inventory-replaced","archived","notified:0","cleanup-receipt","cleanup-root"];
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>Phases.Select(phase=>("Inventory recovery crash "+phase,(Func<Task>)(()=>Crash(phase))))
        .Concat(new[]{"legacy","legacy-null-archived","legacy-null-pending","edited","missing","bad-intent","blocked","no-overwrite","project-main","unknown-context"}.Select(mode=>("Inventory recovery policy "+mode,(Func<Task>)(()=>Crash(mode)))));
    internal static NativeDownloadRequest Request(string directory)=>new(){Url=new("https://inventory.example/"+Path.GetFileName(directory)+"/live.m3u8"),OutputDirectory=directory,FileName="recording",KeepOriginal=true,AddMetadata=true,LiveDurationSeconds=1,Retries=0};
    internal static void RehashReceipt(JsonNode node)
    {
        node["Digest"]="";var type=typeof(PublicationTransaction).GetNestedType("State",System.Reflection.BindingFlags.NonPublic)!;
        var state=System.Text.Json.JsonSerializer.Deserialize(node.ToJsonString(),type)!;
        var digest=(string)typeof(PublicationTransaction).GetMethod("Digest",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,[state])!;node["Digest"]=digest;
    }
    internal static PageMetadata Page()=>new(){Id="fixture",Extractor="inventory-fixture",Title="Fixture",Media=[new(){Id="main",Url=new("https://cdn.example/live.m3u8"),Kind=MediaKind.Hls,Extension="m3u8",HasVideo=true,HasAudio=true}]};
    internal static async Task<int> Worker(string directory,string phase)
    {
        var request=Request(directory);var anchor=Path.Combine(directory,"recording.mp4");var scope=AdaptiveFragmentCheckpoint.Hash(anchor+"\n"+request.Url.AbsoluteUri);
        PublicationFaults.Observer=async value=>{if(value!=phase)return;await File.WriteAllTextAsync(Path.Combine(directory,"ready"),Environment.ProcessId.ToString());await Task.Delay(Timeout.Infinite);};
        DownloadWorkspace.Completion? completion;
        using(var workspace=await DownloadWorkspace.OpenAsync(anchor,request.Url,[],DownloadWorkspace.Recipe(request),default,directory,PublicationTransaction.ContextFor(request,Page())))
        {
            var a=workspace.RawPath("main.ts");var b=workspace.RawPath("original.ts");await File.WriteAllTextAsync(a,"complete main");await File.WriteAllTextAsync(b,"complete original");
            if(!phase.StartsWith("legacy",StringComparison.Ordinal))workspace.SetInventoryIntent(new(StoredOutputInventory.FileFor(anchor,scope),scope,DownloadWorkspace.Recipe(request with {KeepOriginal=false}),AdaptiveFragmentCheckpoint.Hash("format"),true,true));
            var result=await workspace.PublishAsync([new(a,Path.Combine(directory,"recording.ts"),[]),new(b,Path.Combine(directory,"recording.original.ts"),[])],false,default);
            completion=workspace.PendingCompletion;
        }
        await DownloadWorkspace.FinishAsync(completion!,null,archiveOnly:true);
        await DownloadWorkspace.FinishAsync(completion!,path=>File.AppendAllText(Path.Combine(directory,"callbacks"),path+"\n"));
        return 1;
    }
    internal static async Task KillWorker(string assembly,string argument,string directory,string phase)
    {
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var value in new[]{assembly,argument,directory,phase})start.ArgumentList.Add(value);
        using var process=Process.Start(start)!;
        using var lifetime=YoutubeDl.Engine.Media.ProcessLifetime.Attach(process);
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var output=process.StandardOutput.ReadToEndAsync(deadline.Token);var errors=process.StandardError.ReadToEndAsync(deadline.Token);var ready=false;
        async Task StopAsync()
        {
            try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}
            lifetime.Dispose(); // Also closes descendants' pipes after the worker itself exits.
            try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));}catch(TimeoutException){}
            try{await Task.WhenAll(output,errors).WaitAsync(TimeSpan.FromSeconds(2));}catch(Exception error)when(error is TimeoutException or OperationCanceledException or IOException){}
        }
        try
        {
            for(var attempt=0;attempt<250&&!process.HasExited;attempt++)
            {try{ready=File.Exists(Path.Combine(directory,"ready"))&&int.TryParse(await File.ReadAllTextAsync(Path.Combine(directory,"ready"),deadline.Token),out var pid)&&pid==process.Id;}catch(IOException){}if(ready)break;await Task.Delay(25,deadline.Token);}
            await StopAsync();
            var diagnostic=errors.IsCompletedSuccessfully?errors.Result:"worker output did not drain";
            WriteCleanupChecks.Check(ready,"Inventory worker did not reach "+phase+": "+diagnostic);
        }
        finally{await StopAsync();}
    }
    private static async Task Crash(string mode)
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var main=Path.Combine(directory,"recording.ts");var original=Path.Combine(directory,"recording.original.ts");await File.WriteAllTextAsync(main,"old main");await File.WriteAllTextAsync(original,"old original");
            var phase=Phases.Contains(mode)?mode:"committed";
            await KillWorker(typeof(InventoryRecoveryChecks).Assembly.Location,"--inventory-worker",directory,phase);
            var request=Request(directory);var anchor=Path.Combine(directory,"recording.mp4");var scope=AdaptiveFragmentCheckpoint.Hash(anchor+"\n"+request.Url.AbsoluteUri);var file=StoredOutputInventory.FileFor(anchor,scope);
            var receipt=Directory.GetFiles(directory,"publication.json",SearchOption.AllDirectories).SingleOrDefault();
            if(mode.StartsWith("legacy",StringComparison.Ordinal)||mode=="unknown-context")
            {
                var node=JsonNode.Parse(await File.ReadAllTextAsync(receipt!))!;node["Context"]!.AsObject().Remove("Inventory");
                if(mode is "legacy-null-archived" or "legacy-null-pending" or "unknown-context")node.AsObject().Remove("Context");
                if(mode=="legacy-null-archived")node["Archived"]=true;
                RehashReceipt(node);await File.WriteAllTextAsync(receipt!,node.ToJsonString());
                if(File.Exists(file))File.Delete(file);
            }
            if(mode=="edited")await File.WriteAllTextAsync(main,"user edit");if(mode=="missing")File.Delete(original);
            if(mode=="bad-intent")
            {
                var node=JsonNode.Parse(await File.ReadAllTextAsync(receipt!))!;node["Context"]!["Inventory"]!["File"]=main;RehashReceipt(node);await File.WriteAllTextAsync(receipt!,node.ToJsonString());
            }
            if(mode=="blocked"){if(File.Exists(file))File.Delete(file);Directory.CreateDirectory(file);}
            var failure=mode is "edited" or "missing" or "bad-intent";var failed=false;
            try
            {
                if(mode.StartsWith("legacy",StringComparison.Ordinal)||mode=="unknown-context")
                {
                    var calls=0;var engine=new NativeEngine([new AdaptiveNoOverwriteChecks.Provider(Page())],_=>AdaptiveAccountChecks.Http((_,_)=>{calls++;throw new HttpRequestException("Recovery requested media.");}));
                    var result=await engine.DownloadAsync(request with {NoOverwrites=true},null,null,default);WriteCleanupChecks.Check(result.Succeeded&&result.Skipped&&calls==0,"Legacy publication was transferred again.");
                    if(mode is "legacy-null-pending" or "unknown-context")WriteCleanupChecks.Check(!File.Exists(file),"Unknown original context was invented.");
                    else
                    {var entry=await StoredOutputInventory.ReadAsync(file,default);WriteCleanupChecks.Check(entry is not null&&entry.Schema==2&&StoredOutputInventory.Valid(entry),"Legacy inventory was not recreated.");var repeat=await engine.DownloadAsync(request with {NoOverwrites=true,KeepOriginal=mode!="project-main"},null,null,default);WriteCleanupChecks.Check(repeat.Skipped&&calls==0,"Migrated inventory requested transfer.");}
                }
                else
                {
                    DownloadWorkspace.Completion? completion=null;
                    using(var workspace=await DownloadWorkspace.OpenAsync(anchor,request.Url,[],DownloadWorkspace.Recipe(request),default,directory,PublicationTransaction.ContextFor(request,Page())))
                    {
                        var recovered=await workspace.RecoveredAsync();
                        if(mode is "prepared" or "copied" or "installed:0"){WriteCleanupChecks.Check(recovered is null&&await File.ReadAllTextAsync(main)=="old main","Uncommitted inventory became visible.");return;}
                        if(mode=="cleanup-root")WriteCleanupChecks.Check(recovered is null&&File.Exists(file),"Cleanup lost the completed inventory.");
                        else{WriteCleanupChecks.Check(!failure&&recovered is not null,"Committed publication was not recovered.");completion=workspace.PendingCompletion;}
                    }
                    if(completion is not null){await DownloadWorkspace.FinishAsync(completion,null,archiveOnly:true);await DownloadWorkspace.FinishAsync(completion,null);}
                    if(mode=="blocked")
                    {WriteCleanupChecks.Check(Directory.GetFiles(directory,"publication.json",SearchOption.AllDirectories).Length==1,"Failed inventory save discarded recovery evidence.");Directory.Delete(file);var cleanupCompletion=new DownloadWorkspace.Completion(Path.GetDirectoryName(receipt!)!,scope,RecoveryIndex.PathFor(directory,scope));await DownloadWorkspace.FinishAsync(cleanupCompletion,null);}
                    var value=await StoredOutputInventory.ReadAsync(file,default);WriteCleanupChecks.Check(value is not null&&value.Schema==2&&StoredOutputInventory.Valid(value)&&value.Paths.SequenceEqual([main,original]),"Recovered inventory lost paths.");
                    WriteCleanupChecks.Check(Directory.GetDirectories(directory,".native-download-*").Length==0,"Recovered receipt was not cleaned.");
                }
            }
            catch(Exception e)when(failure&&e is IOException or InvalidDataException){failed=true;}
            WriteCleanupChecks.Check(failed==failure,"Unsafe inventory recovery was accepted.");
        }
        finally{Directory.Delete(directory,true);}
    }
}
