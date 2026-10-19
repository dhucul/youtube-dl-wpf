using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;

internal static class InventoryRetentionChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
        new[]{"repeat","weaker","recipe","format","schema-one","corrupt","oversized","retain-blocked","limit","locked","cancel","retain-write-failed","retain-flush-failed","write-failed","flush-failed","replace-failed","bad-pending","pending-limit","wrong-scope"}
            .Select(mode=>("Inventory retention storage "+mode,(Func<Task>)(()=>Storage(mode))))
        .Concat(new[]{"nested","ancestor","absolute","relative-parent","schema-one","duplicate","conflict","corrupt-current","corrupt-legacy","wrong-scope","missing","main-only","blocked-migration","history-only"}
            .Select(mode=>("Inventory retention legacy "+mode,(Func<Task>)(()=>Legacy(mode)))))
        .Concat(new[]{"serialized","cancel-wait"}.Select(mode=>("Inventory retention concurrency "+mode,(Func<Task>)(()=>Concurrent(mode)))))
        .Concat(new[]{false,true}.Select(cancel=>("Inventory retention cross-process "+cancel,(Func<Task>)(()=>CrossProcess(cancel)))))
        .Concat(new[]{"inventory-retention-writing","inventory-retention-flushed","inventory-retained","inventory-writing"}.Select(phase=>("Inventory retention process interruption "+phase,(Func<Task>)(()=>Interrupted(phase)))));
    private static string DirectoryFor()=>Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));
    private static StoredOutputInventory.Intent Intent(string directory)=>new(Path.Combine(directory,".native-output-inventory-"+AdaptiveFragmentCheckpoint.Hash(directory)+".json"),AdaptiveFragmentCheckpoint.Hash(directory),AdaptiveFragmentCheckpoint.Hash("recipe"),AdaptiveFragmentCheckpoint.Hash("format"),true,true);
    private static async Task<string[]> Media(string directory)
    {Directory.CreateDirectory(directory);var paths=new[]{Path.Combine(directory,"main.ts"),Path.Combine(directory,"original.ts")};foreach(var path in paths)await File.WriteAllTextAsync(path,"complete "+Path.GetFileName(path));return paths;}
    private static void Check(bool value,string message)=>WriteCleanupChecks.Check(value,message);
    private static async Task Storage(string mode)
    {
        var directory=DirectoryFor();FileStream? held=null;
        try
        {
            var paths=await Media(directory);var intent=Intent(directory);Check(await StoredOutputInventory.SaveAsync(intent,paths),"Initial inventory failed.");
            var initial=(await StoredOutputInventory.ReadAsync(intent.File,default))!;
            if(mode=="schema-one"){initial=initial with {Schema=1,Digest=""};initial=initial with {Digest=StoredOutputInventory.Digest(initial)};await File.WriteAllTextAsync(intent.File,JsonSerializer.Serialize(initial));}
            var oldBytes=await File.ReadAllBytesAsync(intent.File);var retained=StoredOutputInventory.RetainedFile(intent.File,initial);
            if(mode=="corrupt")await File.WriteAllTextAsync(intent.File,"broken");
            if(mode=="oversized"){await File.WriteAllBytesAsync(intent.File,new byte[8*1024*1024+1]);Check(await StoredOutputInventory.ReadAsync(intent.File,default) is null,"Oversized record was read.");}
            if(mode=="retain-blocked")Directory.CreateDirectory(retained);
            if(mode=="limit")for(var index=0;index<64;index++)await File.WriteAllTextAsync(intent.File+"."+index.ToString("X64")+".retained.json","retained evidence");
            if(mode=="bad-pending")await File.WriteAllTextAsync(intent.File+".foreign.tmp","unrelated");
            if(mode=="pending-limit")for(var index=0;index<65;index++)await File.WriteAllTextAsync(intent.File+"."+Guid.NewGuid().ToString("N")+".tmp","partial");
            if(mode=="locked")held=new(intent.File,FileMode.Open,FileAccess.Read,FileShare.None);
            var token=mode=="cancel"?new CancellationToken(true):default;
            var phase=mode switch{"retain-write-failed"=>"inventory-retention-writing","retain-flush-failed"=>"inventory-retention-flushed","write-failed"=>"inventory-writing","flush-failed"=>"inventory-flushed","replace-failed"=>"inventory-replaced",_=>""};
            PublicationFaults.Observer=value=>value==phase?Task.FromException(new IOException("Injected unavailable storage")):Task.CompletedTask;
            var changed=intent with {Retained=false,Recipe=mode=="recipe"?AdaptiveFragmentCheckpoint.Hash("new recipe"):intent.Recipe,Format=mode=="format"?AdaptiveFragmentCheckpoint.Hash("new format"):intent.Format};
            if(mode=="repeat")changed=intent;
            if(mode=="wrong-scope")changed=changed with {Scope=AdaptiveFragmentCheckpoint.Hash("wrong")};
            var rejected=mode is "bad-pending" or "wrong-scope";var threw=false;var saved=false;
            try{saved=await StoredOutputInventory.SaveAsync(changed,mode=="repeat"?paths:[paths[0]],token);}catch(InvalidDataException){threw=true;}
            finally{held?.Dispose();held=null;PublicationFaults.Observer=null;}
            var blocked=mode is "retain-blocked" or "limit" or "locked" or "cancel" or "retain-write-failed" or "retain-flush-failed" or "write-failed" or "flush-failed" or "replace-failed" or "pending-limit";
            Check(threw==rejected&&saved==(!blocked&&!rejected),"Storage failure/result contract changed: "+mode);
            if((blocked||rejected)&&mode!="replace-failed")Check(oldBytes.AsEnumerable().SequenceEqual(await File.ReadAllBytesAsync(intent.File)),"A failed metadata update discarded the current inventory.");
            if(!blocked&&!rejected&&mode is not ("repeat" or "corrupt" or "oversized"))Check(oldBytes.AsEnumerable().SequenceEqual(await File.ReadAllBytesAsync(retained)),"Previous inventory was not retained exactly.");
            if(mode=="repeat")Check(Directory.GetFiles(directory,"*.retained.json").Length==0,"Identical writes grew retention.");
            foreach(var path in paths)Check(await File.ReadAllTextAsync(path)=="complete "+Path.GetFileName(path),"Metadata operation changed media.");
        }
        finally{held?.Dispose();PublicationFaults.Observer=null;if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    internal static StoredOutputInventory.Intent EngineIntent(NativeDownloadRequest request,PageMetadata page,string anchor)=>
        (StoredOutputInventory.Intent)typeof(NativeEngine).GetMethod("InventoryIntent",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,[request,MediaDownloadPlan.Create(page,request),anchor,true])!;
    private static async Task Legacy(string mode)
    {
        var directory=DirectoryFor();Directory.CreateDirectory(directory);
        try
        {
            var output=Path.Combine(directory,"out");var request=InventoryRecoveryChecks.Request(output) with {NoOverwrites=true,OutputTemplate=mode=="absolute"?Path.Combine(directory,"absolute","%(id)s","nested","recording.%(ext)s"):mode=="relative-parent"?"../sibling/%(id)s/nested/recording.%(ext)s":"one/two/recording.%(ext)s"};
            var page=InventoryRecoveryChecks.Page();var anchor=NativeOutputTemplate.Render(request,page,page.Media[0],"mp4");var intent=EngineIntent(request,page,anchor);
            var paths=await Media(Path.GetDirectoryName(anchor)!);Check(await StoredOutputInventory.SaveAsync(intent,paths),"Legacy seed failed.");
            var entry=(await StoredOutputInventory.ReadAsync(intent.File,default))!;
            if(mode=="schema-one")entry=entry with {Schema=1,Digest=""};
            if(mode=="wrong-scope")entry=entry with {Scope=AdaptiveFragmentCheckpoint.Hash("other"),Digest=""};
            entry=entry with {Digest=StoredOutputInventory.Digest(entry)};
            var root=mode is "absolute" or "relative-parent"?RecoveryIndex.DiscoveryDirectories(request).Last():mode=="ancestor"?Path.GetDirectoryName(Path.GetDirectoryName(anchor)!)!:output;
            Directory.CreateDirectory(root);var legacy=StoredOutputInventory.FileFor(Path.Combine(root,"anchor"),intent.Scope);var bytes=JsonSerializer.SerializeToUtf8Bytes(entry);await File.WriteAllBytesAsync(legacy,bytes);File.Delete(intent.File);
            if(mode is "duplicate" or "conflict")
            {
                var other=StoredOutputInventory.FileFor(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(anchor)!)!,"anchor"),intent.Scope);
                var duplicate=mode=="conflict"?entry with {Retained=false,Digest=""}:entry;duplicate=duplicate with {Digest=StoredOutputInventory.Digest(duplicate)};await File.WriteAllTextAsync(other,JsonSerializer.Serialize(duplicate));
            }
            if(mode=="corrupt-current")await File.WriteAllTextAsync(intent.File,"broken");
            if(mode=="corrupt-legacy")await File.WriteAllTextAsync(legacy,"broken");
            if(mode is "missing" or "main-only")File.Delete(paths[1]);
            if(mode=="blocked-migration")Directory.CreateDirectory(intent.File);
            if(mode=="history-only"){File.Move(legacy,StoredOutputInventory.RetainedFile(intent.File,entry));}
            var refused=mode is "conflict" or "corrupt-current" or "corrupt-legacy" or "wrong-scope" or "missing" or "history-only" or "blocked-migration";
            var calls=0;var callbacks=0;var engine=new NativeEngine([new AdaptiveNoOverwriteChecks.Provider(page)],_=>AdaptiveAccountChecks.Http((_,_)=>{calls++;throw new HttpRequestException("Inventory fallback");}));var failed=false;
            try{var result=await engine.DownloadAsync(request with {KeepOriginal=mode!="main-only"},null,null,default,_=>callbacks++);Check(!refused&&result.Skipped&&result.OutputPaths.SequenceEqual(mode=="main-only"?[paths[0]]:paths),"Legacy location did not reuse its exact inventory.");}
            catch(Exception e)when(refused&&e is IOException or InvalidDataException or HttpRequestException or InvalidOperationException){failed=true;}
            Check(failed==refused&&calls==0&&callbacks==0,"Legacy metadata caused unexpected transfer or callbacks: "+mode);
            if(!refused&&mode!="main-only"){var migrated=await StoredOutputInventory.ReadAsync(intent.File,default);Check(migrated is not null&&migrated.Schema==2&&StoredOutputInventory.Valid(migrated),"Legacy location was not migrated.");}
            if(mode!="history-only"&&mode!="corrupt-legacy")Check(bytes.AsEnumerable().SequenceEqual(await File.ReadAllBytesAsync(legacy)),"Legacy record was discarded.");
        }
        finally{Directory.Delete(directory,true);}
    }
    private static async Task Concurrent(string mode)
    {
        var directory=DirectoryFor();var arrived=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);Task<bool>? first=null;
        try
        {
            var paths=await Media(directory);var intent=Intent(directory);var once=0;
            PublicationFaults.Observer=async phase=>{if(phase=="inventory-writing"&&Interlocked.Increment(ref once)==1){arrived.SetResult();await release.Task;}};
            first=StoredOutputInventory.SaveAsync(intent,paths);await arrived.Task.WaitAsync(TimeSpan.FromSeconds(3));
            using var cancellation=new CancellationTokenSource();var second=StoredOutputInventory.SaveAsync(intent with {Retained=false},[paths[0]],cancellation.Token);
            await Task.Delay(100);Check(!second.IsCompleted&&Directory.GetFiles(directory,"*.tmp").Length==1,"Concurrent cleanup touched an active writer.");
            if(mode=="cancel-wait"){cancellation.Cancel();Check(!await second,"Canceled waiter persisted inventory.");}
            release.SetResult();Check(await first,"First serialized writer failed.");
            if(mode=="serialized")Check(await second,"Second serialized writer failed.");
            var value=(await StoredOutputInventory.ReadAsync(intent.File,default))!;Check(StoredOutputInventory.Valid(value)&&value.Retained==(mode=="cancel-wait"),"Serialized inventory lost policy.");
        }
        finally{release.TrySetResult();if(first is not null)await first;PublicationFaults.Observer=null;if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    internal static async Task<int> Worker(string directory,string phase)
    {
        var paths=await Media(directory);var intent=Intent(directory);Check(await StoredOutputInventory.SaveAsync(intent,paths),"Worker seed failed.");
        PublicationFaults.Observer=async value=>{if(value!=phase)return;await File.WriteAllTextAsync(Path.Combine(directory,"ready"),Environment.ProcessId.ToString());await Task.Delay(Timeout.Infinite);};
        await StoredOutputInventory.SaveAsync(intent with {Retained=false},[paths[0]]);return 1;
    }
    private static async Task CrossProcess(bool cancel)
    {
        var directory=DirectoryFor();Directory.CreateDirectory(directory);var start=new System.Diagnostics.ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var argument in new[]{typeof(InventoryRetentionChecks).Assembly.Location,"--inventory-retention-worker",directory,"inventory-writing"})start.ArgumentList.Add(argument);
        using var process=System.Diagnostics.Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();Task<bool>? waiting=null;
        try
        {
            for(var attempt=0;attempt<200&&!File.Exists(Path.Combine(directory,"ready"))&&!process.HasExited;attempt++)await Task.Delay(25);
            Check(File.Exists(Path.Combine(directory,"ready")),"Cross-process worker did not hold inventory lock.");var intent=Intent(directory);var value=(await StoredOutputInventory.ReadAsync(intent.File,default))!;
            using var cancellation=new CancellationTokenSource();waiting=StoredOutputInventory.SaveAsync(intent,value.Paths,cancellation.Token);await Task.Delay(100);Check(!waiting.IsCompleted,"Separate process bypassed the inventory lock.");
            if(cancel){cancellation.Cancel();Check(!await waiting,"Canceled process waiter wrote metadata.");}
            process.Kill(true);await process.WaitForExitAsync();if(!cancel)Check(await waiting.WaitAsync(TimeSpan.FromSeconds(3)),"Released process lock did not resume writer.");
            Check(await StoredOutputInventory.SaveAsync(intent,value.Paths),"Storage remained locked after process death.");
        }
        finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}if(waiting is not null)await waiting;await output;await errors;Directory.Delete(directory,true);}
    }
    private static async Task Interrupted(string phase)
    {
        var directory=DirectoryFor();Directory.CreateDirectory(directory);
        try
        {
            await InventoryRecoveryChecks.KillWorker(typeof(InventoryRetentionChecks).Assembly.Location,"--inventory-retention-worker",directory,phase);
            var intent=Intent(directory);var before=(await StoredOutputInventory.ReadAsync(intent.File,default))!;Check(before.Retained&&before.Paths.Length==2,"Interrupted retention discarded the current record.");
            var pending=StoredOutputInventory.SaveAsync(intent with {Retained=false},[before.Paths[0]]);Check(await pending.WaitAsync(TimeSpan.FromSeconds(3)),"Process lock was not released after interruption.");
            var retained=(await StoredOutputInventory.ReadAsync(StoredOutputInventory.RetainedFile(intent.File,before),default))!;Check(retained.Digest==before.Digest,"Process recovery discarded earlier evidence.");
            Check(Directory.GetFiles(directory,"*.tmp").Length==0,"Process recovery left partial metadata.");
        }
        finally{Directory.Delete(directory,true);}
    }
}


