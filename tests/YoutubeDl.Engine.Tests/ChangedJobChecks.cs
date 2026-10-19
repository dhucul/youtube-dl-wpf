using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;

internal static class ChangedJobChecks
{
    internal static readonly string[] Modes=["commit","cleanup","folder","absolute","archive","archive-folder","archive-changed","failure","edited","backup","legacy","locked-archive","locked-backup","registry-edit","missing-index","same-destination","retire-claim","retire-cleanup","metadata"];
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>Modes.Select(mode=>("Changed job recovery "+mode,(Func<Task>)(()=>Check(mode))));
    internal static NativeDownloadRequest Request(string directory,string mode,bool fresh=false)
    {
        var archive=mode.StartsWith("archive",StringComparison.Ordinal)||mode=="locked-archive";
        var changedDirectory=fresh&&(mode is "folder" or "absolute" or "archive-folder" or "same-destination");
        return PublicationRecoveryChecks.Request(Path.Combine(directory,changedDirectory?"new":"old"),archive) with
        {
            OutputTemplate=mode is "absolute" or "same-destination"?Path.Combine(directory,"external","movie.%(ext)s"):"movie.%(ext)s",
            WriteInfoJson=fresh,SkipDownload=mode=="metadata",ForceWriteArchive=mode=="metadata",
            DownloadArchiveFile=archive?Path.Combine(directory,fresh&&mode=="archive-changed"?"new-archive.txt":"old-archive.txt"):""
        };
    }
    internal static async Task<int> Worker(string directory,string mode)
    {
        var phase=mode is "cleanup" or "locked-backup"?"cleanup-sibling:0":mode.StartsWith("archive",StringComparison.Ordinal)||mode=="locked-archive"?"archive-writing":"committed";
        PublicationFaults.Observer=async value=>{if(value!=phase)return;await File.WriteAllTextAsync(Path.Combine(directory,"ready"),Environment.ProcessId.ToString());await Task.Delay(Timeout.Infinite);};
        await PublicationRecoveryChecks.Engine().DownloadAsync(Request(directory,mode),null,null,default);return 1;
    }
    private static async Task Stop(string directory,string mode,bool second=false)
    {
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{typeof(ChangedJobChecks).Assembly.Location,second?"--changed-job-retry-worker":"--changed-job-worker",directory,mode})start.ArgumentList.Add(arg);
        File.Delete(Path.Combine(directory,"ready"));using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();var ready=false;
        try{for(var i=0;i<500&&!process.HasExited;i++){try{ready=File.Exists(Path.Combine(directory,"ready"))&&int.Parse(await File.ReadAllTextAsync(Path.Combine(directory,"ready")))==process.Id;}catch(IOException){}if(ready)break;await Task.Delay(40);}WriteCleanupChecks.Check(ready,"Changed-job worker failed: "+(process.HasExited?await errors:"timeout"));process.Kill(true);await process.WaitForExitAsync();}
        finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}await output;await errors;}
    }
    internal static async Task<int> RetryWorker(string directory,string mode)
    {
        var phase=mode=="retire-claim"?"retired-claimed":"cleanup-entry:0";PublicationFaults.Observer=async value=>{if(value!=phase)return;await File.WriteAllTextAsync(Path.Combine(directory,"ready"),Environment.ProcessId.ToString());await Task.Delay(Timeout.Infinite);};
        await PublicationRecoveryChecks.Engine().DownloadAsync(Request(directory,mode,true),null,null,default);return 1;
    }
    private static async Task Check(string mode)
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(directory,"old"));Directory.CreateDirectory(Path.Combine(directory,"external"));
        try
        {
            var old=Request(directory,mode);var main=mode is "absolute" or "same-destination"?Path.Combine(directory,"external","movie.mp4"):Path.Combine(directory,"old","movie.mp4");
            await File.WriteAllTextAsync(main,"original before any download");await File.WriteAllTextAsync(Path.ChangeExtension(main,"en.vtt"),"original captions");
            if(old.DownloadArchiveFile.Length>0)await File.WriteAllTextAsync(old.DownloadArchiveFile,"prior record\n");
            await Stop(directory,mode);var root=Directory.GetDirectories(directory,".native-download-*",SearchOption.AllDirectories).Single();var scope=Path.GetFileName(root)[".native-download-".Length..];
            var receipt=Path.Combine(root,"publication.json");var node=JsonNode.Parse(await File.ReadAllTextAsync(receipt))!;var fullScope=node["Scope"]!.GetValue<string>();var source=node["SourceIdentity"]!.GetValue<string>();
            if(mode=="metadata")WriteCleanupChecks.Check(File.Exists(Path.ChangeExtension(main,"en.vtt")),"Metadata sidecar missing.");else WriteCleanupChecks.Check(await File.ReadAllTextAsync(main)=="complete new media","Stopped job did not commit.");
            if(mode=="edited")await File.WriteAllTextAsync(main,"user edit");
            if(mode=="backup")await File.WriteAllTextAsync(Directory.GetFiles(directory,".native-backup-*",SearchOption.AllDirectories).First(),"backup edit");
            if(mode=="legacy"){node.AsObject().Remove("Context");node["Digest"]="";node["Digest"]=AdaptiveFragmentCheckpoint.Hash(node.ToJsonString());await File.WriteAllTextAsync(receipt,node.ToJsonString());}
            if(mode=="registry-edit"){var file=RecoveryIndex.RegistryPath(source,fullScope);var registry=JsonNode.Parse(await File.ReadAllTextAsync(file))!;registry["Root"]=directory;await File.WriteAllTextAsync(file,registry.ToJsonString());}
            if(mode=="missing-index")foreach(var file in Directory.GetFiles(Path.Combine(directory,"old"),".native-recovery-*.json"))File.Delete(file);
            if(mode is "retire-claim" or "retire-cleanup")await Stop(directory,mode,true);
            FileStream? held=null;
            try
            {
                if(mode=="locked-archive")held=new(old.DownloadArchiveFile,FileMode.Open,FileAccess.Read,FileShare.Read);
                if(mode=="locked-backup")held=new(Directory.GetFiles(directory,".native-backup-*",SearchOption.AllDirectories).First(),FileMode.Open,FileAccess.Read,FileShare.Read);
                var transfers=0;var callbacks=0;var failure=mode is "edited" or "backup" or "legacy" or "registry-edit" or "locked-archive" or "locked-backup";
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(mode=="locked-archive"?1:10));
                var engine=mode=="failure"?new NativeEngine([new FailedProvider()],_=>AdaptiveAccountChecks.Http((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)))):PublicationRecoveryChecks.Engine(()=>transfers++);
                try
                {
                    var result=await engine.DownloadAsync(Request(directory,mode,true),null,null,timeout.Token,_=>callbacks++);
                    WriteCleanupChecks.Check(!failure&&(mode=="failure"?!result.Succeeded:result.Succeeded),"Changed-job result differs.");
                    if(mode!="failure")WriteCleanupChecks.Check(mode=="metadata"||mode.StartsWith("archive",StringComparison.Ordinal)&&mode!="archive-changed"?callbacks==0:callbacks==1,"Retired job repeated completion callbacks.");
                }
                catch(Exception e)when((failure||mode=="failure")&&e is IOException or InvalidDataException or OperationCanceledException or HttpRequestException){WriteCleanupChecks.Check(callbacks==0&&transfers==0,"Invalid recovery transferred media.");}
                if(mode=="failure")WriteCleanupChecks.Check(await File.ReadAllTextAsync(main)=="complete new media","A failed changed job rolled back committed media.");
                if(mode=="edited")WriteCleanupChecks.Check(await File.ReadAllTextAsync(main)=="user edit","User edit was lost.");
                if(mode is "locked-archive" or "locked-backup" or "legacy")WriteCleanupChecks.Check(await File.ReadAllTextAsync(main)=="complete new media"&&File.Exists(receipt),"Pending committed data was lost.");
                if(!failure&&mode!="failure")
                {
                    WriteCleanupChecks.Check(Directory.GetDirectories(directory,".native-download-*",SearchOption.AllDirectories).Length==0&&Directory.GetFiles(directory,".native-recovery-*",SearchOption.AllDirectories).Length==0,"Changed-job cleanup left records.");
                    WriteCleanupChecks.Check(!File.Exists(RecoveryIndex.RegistryPath(source,fullScope)),"Registered completion remained.");
                    if(old.DownloadArchiveFile.Length>0)WriteCleanupChecks.Check(File.ReadAllLines(old.DownloadArchiveFile).Count(line=>line=="publication-fixture movie")==1,"Original archive was not reconciled.");
                    if(mode=="archive-changed")WriteCleanupChecks.Check(File.ReadAllLines(Request(directory,mode,true).DownloadArchiveFile).Count(line=>line=="publication-fixture movie")==1,"New archive missed its completion.");
                }
            }
            finally{held?.Dispose();}
            if(mode=="registry-edit")File.Delete(RecoveryIndex.RegistryPath(source,fullScope));
        }
        finally{Directory.Delete(directory,true);}
    }
    private sealed class FailedProvider:YoutubeDl.Engine.Providers.IMediaProvider
    {
        public YoutubeDl.Engine.Providers.MediaProviderInfo Info=>new("changed-failure","Changed failure",0);
        public bool CanHandle(Uri uri)=>true;
        public Task<YoutubeDl.Engine.Discovery.PageMetadata> ExtractAsync(NativeDownloadRequest request,YoutubeDl.Engine.Http.MediaHttp http,CancellationToken ct)=>Task.FromResult(new YoutubeDl.Engine.Discovery.PageMetadata{Id="movie",Extractor="publication-fixture",Title="Movie",Media=[new(){Id="file",Kind=YoutubeDl.Engine.Discovery.MediaKind.Progressive,Url=new("https://cdn.example/movie.mp4"),Extension="mp4"}]});
    }
}
