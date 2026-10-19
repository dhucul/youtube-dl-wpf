using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;

internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> InventoryRecoveryMediaChecks(string ffmpeg)=>
        new[]{"committed","inventory-writing","inventory-flushed","inventory-replaced","archived","notified:0","cleanup-receipt","cleanup-root","legacy-receipt","schema-one","schema-one-main","schema-one-missing","schema-one-corrupt","nested","absolute","blocked","changed-job","archive-fast"}
            .Select(mode=>("Inventory recovery media "+mode,(Func<Task>)(()=>InventoryRecoveryMedia(ffmpeg,mode))));
    private static NativeDownloadRequest InventoryRecoveryRequest(string directory,string mode)=>InventoryRecoveryChecks.Request(Path.Combine(directory,"out")) with
    {Url=new("https://inventory.example/"+Path.GetFileName(directory)+"/live.m3u8"),OutputTemplate=mode=="nested"?"%(ext)s/%(title)s.%(ext)s":mode=="absolute"?Path.Combine(directory,"absolute","recording.%(ext)s"):"",DownloadArchiveFile=mode=="archive-fast"?Path.Combine(directory,"archive.txt"):""};
    private static NativeEngine InventoryRecoveryEngine(string directory,Action? transfer=null)=>new([new AdaptiveNoOverwriteChecks.Provider(InventoryRecoveryChecks.Page())],_=>AdaptiveAccountChecks.Http(async(message,ct)=>
    {
        var name=Path.GetFileName(message.RequestUri!.AbsolutePath);
        if(name=="live.m3u8")return AdaptiveAccountChecks.Text((await File.ReadAllTextAsync(Path.Combine(directory,"fixtures","video.m3u8"),ct)).Replace("#EXT-X-ENDLIST","").Replace("#EXTM3U","#EXTM3U\n#EXT-X-PLAYLIST-TYPE:EVENT\n#EXT-X-PROGRAM-DATE-TIME:2026-01-01T00:00:00Z"));
        transfer?.Invoke();return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(directory,"fixtures",name),ct))};
    }));
    internal static async Task<int> InventoryRecoveryMediaWorker(string ffmpeg,string directory,string encoded)
    {
        var parts=encoded.Split('|');var mode=parts[0];var phase=parts[1];
        PublicationFaults.Observer=async value=>{if(value!=phase)return;await File.WriteAllTextAsync(Path.Combine(directory,"ready"),Environment.ProcessId.ToString());await Task.Delay(Timeout.Infinite);};
        await InventoryRecoveryEngine(directory).DownloadAsync(InventoryRecoveryRequest(directory,mode) with {FfmpegPath=ffmpeg},null,null,default,path=>File.AppendAllText(Path.Combine(directory,"callbacks"),path+"\n"));return 1;
    }
    private static async Task InventoryRecoveryMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var fixtures=Path.Combine(files.Directory,"fixtures");Directory.CreateDirectory(fixtures);var source=Path.Combine(fixtures,"source.mp4");
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=10","-f","lavfi","-i","sine=frequency=650:sample_rate=48000","-t","1","-c:v","libx264","-g","10","-c:a","aac",source]);
        await Run(ffmpeg,["-v","error","-y","-i",source,"-c","copy","-f","hls","-hls_time","1","-hls_list_size","0","-hls_segment_filename",Path.Combine(fixtures,"segment%d.ts"),Path.Combine(fixtures,"video.m3u8")]);
        var request=InventoryRecoveryRequest(files.Directory,mode) with {FfmpegPath=ffmpeg};Directory.CreateDirectory(request.OutputDirectory);
        var anchor=NativeOutputTemplate.Render(request,InventoryRecoveryChecks.Page(),InventoryRecoveryChecks.Page().Media[0],"mp4");var scope=AdaptiveFragmentCheckpoint.Hash(anchor+"\n"+request.Url.AbsoluteUri);var record=StoredOutputInventory.FileFor(anchor,scope);
        var schema=mode.StartsWith("schema-one",StringComparison.Ordinal);var transfers=0;NativeDownloadResult? first=null;
        if(schema||mode=="blocked")
        {
            if(mode=="blocked"){Directory.CreateDirectory(Path.GetDirectoryName(record)!);Directory.CreateDirectory(record);}
            first=await InventoryRecoveryEngine(files.Directory).DownloadAsync(request,null,null,default);WriteCleanupChecks.Check(first.Succeeded,"Initial recovery fixture failed.");
            if(schema)
            {var entry=(await StoredOutputInventory.ReadAsync(record,default))!;entry=entry with {Schema=1,Digest=""};entry=entry with {Digest=mode=="schema-one-corrupt"?"invalid":StoredOutputInventory.Digest(entry)};await File.WriteAllTextAsync(record,JsonSerializer.Serialize(entry));}
        }
        else
        {
            var phase=mode is "legacy-receipt" or "nested" or "absolute"?"committed":mode=="changed-job"?"inventory-writing":mode=="archive-fast"?"archived":mode;
            var start=new System.Diagnostics.ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var value in new[]{typeof(FfmpegChecks).Assembly.Location,"--inventory-media-worker",ffmpeg,files.Directory,mode+"|"+phase})start.ArgumentList.Add(value);
            using var process=System.Diagnostics.Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();var ready=false;
            try
            {for(var attempt=0;attempt<300&&!process.HasExited;attempt++){try{ready=File.Exists(Path.Combine(files.Directory,"ready"))&&int.TryParse(await File.ReadAllTextAsync(Path.Combine(files.Directory,"ready")),out var pid)&&pid==process.Id;}catch(IOException){}if(ready)break;await Task.Delay(25);}WriteCleanupChecks.Check(ready,"Media inventory worker did not reach "+phase+": "+(process.HasExited?await errors:"timeout"));process.Kill(true);await process.WaitForExitAsync();}
            finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}await output;await errors;}
            if(mode=="legacy-receipt")
            {var receipt=Directory.GetFiles(files.Directory,"publication.json",SearchOption.AllDirectories).Single();var node=JsonNode.Parse(await File.ReadAllTextAsync(receipt))!;node["Context"]!.AsObject().Remove("Inventory");InventoryRecoveryChecks.RehashReceipt(node);await File.WriteAllTextAsync(receipt,node.ToJsonString());}
        }
        var mediaFiles=Directory.GetFiles(files.Directory,"recording*.ts",SearchOption.AllDirectories).Concat(Directory.GetFiles(files.Directory,"Fixture*.ts",SearchOption.AllDirectories)).Distinct().ToArray();
        WriteCleanupChecks.Check(mediaFiles.Length==2,"Committed media fixture lost its output set.");var missing=mediaFiles.Single(path=>path.Contains(".original."));
        if(mode=="schema-one-missing")File.Delete(missing);if(mode=="blocked"){WriteCleanupChecks.Check(Directory.GetFiles(files.Directory,"publication.json",SearchOption.AllDirectories).Length==1,"Failed inventory write lost its receipt.");Directory.Delete(record);}
        var hashes=mediaFiles.Where(File.Exists).ToDictionary(path=>path,path=>System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));var callbacks=new List<string>();
        var fallback=mode is "schema-one-missing" or "schema-one-corrupt" or "changed-job";
        var result=await InventoryRecoveryEngine(files.Directory,()=>transfers++).DownloadAsync(request with {NoOverwrites=true,KeepOriginal=mode!="schema-one-main",FileName=mode=="changed-job"?"new":request.FileName,FfmpegPath=fallback?ffmpeg:Path.Combine(files.Directory,"unused-ffmpeg.exe")},null,null,default,callbacks.Add);
        WriteCleanupChecks.Check(result.Succeeded&&(fallback?transfers>0:transfers==0),"Recovery or migration transferred unexpectedly.");
        if(mode=="schema-one-missing")WriteCleanupChecks.Check(callbacks.SequenceEqual([missing]),"Migration did not fill only its missing source.");
        foreach(var (path,hash) in hashes)WriteCleanupChecks.Check(hash.AsEnumerable().SequenceEqual(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path))),"Recovery changed existing coded media.");
        foreach(var path in mediaFiles)await Run(ffmpeg,["-v","error","-xerror","-i",path,"-map","0:v?","-map","0:a?","-f","null","-"],true);
        var stored=await StoredOutputInventory.ReadAsync(record,default);WriteCleanupChecks.Check(stored is not null&&stored.Schema==2&&StoredOutputInventory.Valid(stored),"Recovered/migrated inventory is invalid.");
        WriteCleanupChecks.Check(Directory.GetFiles(files.Directory,".native-output-inventory-*.tmp",SearchOption.AllDirectories).Length==0,"Inventory write debris remained.");
        if(!fallback)
        {var repeat=await InventoryRecoveryEngine(files.Directory,()=>transfers++).DownloadAsync(request with {NoOverwrites=true,FfmpegPath=Path.Combine(files.Directory,"unused-ffmpeg.exe")},null,null,default,_=>throw new InvalidOperationException("Repeated migration callback."));WriteCleanupChecks.Check(repeat.Skipped&&transfers==0,"Cleaned publication lost its inventory.");}
        WriteCleanupChecks.Check(Directory.GetDirectories(files.Directory,".native-download-*",SearchOption.AllDirectories).Length==0,"Publication receipt was not retired.");
    }
}
