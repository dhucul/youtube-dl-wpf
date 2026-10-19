using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;

internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> InventoryRetentionMediaChecks(string ffmpeg)=>
        new[]{"nested","ancestor","absolute","relative-parent","schema-one","concurrent","retain-write-failed","retain-flush-failed","write-failed","retained-blocked","legacy-locked","missing-original"}
            .Select(mode=>("Inventory retention media "+mode,(Func<Task>)(()=>InventoryRetentionMedia(ffmpeg,mode))));
    private static async Task InventoryRetentionMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var fixtures=Path.Combine(files.Directory,"fixtures");Directory.CreateDirectory(fixtures);var source=Path.Combine(fixtures,"source.mp4");
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=10","-f","lavfi","-i","sine=frequency=650:sample_rate=48000","-t","1","-c:v","libx264","-g","10","-c:a","aac",source]);
        await Run(ffmpeg,["-v","error","-y","-i",source,"-c","copy","-f","hls","-hls_time","1","-hls_list_size","0","-hls_segment_filename",Path.Combine(fixtures,"segment%d.ts"),Path.Combine(fixtures,"video.m3u8")]);
        var output=Path.Combine(files.Directory,"out");var request=InventoryRecoveryChecks.Request(output) with {FfmpegPath=ffmpeg,OutputTemplate=mode=="absolute"?Path.Combine(files.Directory,"absolute","%(id)s","nested","recording.%(ext)s"):mode=="relative-parent"?"../sibling/%(id)s/nested/recording.%(ext)s":"one/two/recording.%(ext)s"};
        var reads=0;NativeEngine Engine(bool deny=false)=>new([new AdaptiveNoOverwriteChecks.Provider(InventoryRecoveryChecks.Page())],_=>AdaptiveAccountChecks.Http(async(message,ct)=>
        {
            reads++;if(deny)throw new HttpRequestException("Inventory repeat requested HTTP.");var name=Path.GetFileName(message.RequestUri!.AbsolutePath);
            if(name=="live.m3u8")return AdaptiveAccountChecks.Text((await File.ReadAllTextAsync(Path.Combine(fixtures,"video.m3u8"),ct)).Replace("#EXT-X-ENDLIST","").Replace("#EXTM3U","#EXTM3U\n#EXT-X-PLAYLIST-TYPE:EVENT\n#EXT-X-PROGRAM-DATE-TIME:2026-01-01T00:00:00Z"));
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(fixtures,name),ct))};
        }));
        var first=await Engine().DownloadAsync(request,null,null,default);WriteCleanupChecks.Check(first.Succeeded&&first.OutputPaths.Count==2,"Retention media seed failed.");
        var anchor=NativeOutputTemplate.Render(request,InventoryRecoveryChecks.Page(),InventoryRecoveryChecks.Page().Media[0],"mp4");var intent=InventoryRetentionChecks.EngineIntent(request,InventoryRecoveryChecks.Page(),anchor);var entry=(await StoredOutputInventory.ReadAsync(intent.File,default))!;
        var blocked=mode is "retain-write-failed" or "retain-flush-failed" or "write-failed" or "retained-blocked";
        var legacy=intent.File;FileStream? held=null;
        try
        {
            if(blocked)
            {
                entry=entry with {Processed=false,Digest=""};entry=entry with {Digest=StoredOutputInventory.Digest(entry)};await File.WriteAllTextAsync(intent.File,JsonSerializer.Serialize(entry));
                var previous=await File.ReadAllBytesAsync(intent.File);var retained=StoredOutputInventory.RetainedFile(intent.File,entry);if(mode=="retained-blocked")Directory.CreateDirectory(retained);
                var phase=mode=="retain-write-failed"?"inventory-retention-writing":mode=="retain-flush-failed"?"inventory-retention-flushed":"inventory-writing";
                PublicationFaults.Observer=value=>mode!="retained-blocked"&&value==phase?Task.FromException(new IOException("Temporary storage outage")):Task.CompletedTask;
                var result=await Engine().DownloadAsync(request,null,null,default);WriteCleanupChecks.Check(result.Succeeded&&Directory.GetFiles(files.Directory,"publication.json",SearchOption.AllDirectories).Length==1,"Storage outage lost committed receipt.");
                WriteCleanupChecks.Check(previous.AsEnumerable().SequenceEqual(await File.ReadAllBytesAsync(intent.File)),"Storage outage discarded prior inventory.");
                PublicationFaults.Observer=null;if(mode=="retained-blocked")Directory.Delete(retained);
            }
            else
            {
                if(mode=="schema-one"){entry=entry with {Schema=1,Digest=""};entry=entry with {Digest=StoredOutputInventory.Digest(entry)};}
                if(mode!="concurrent")
                {
                    var root=mode is "absolute" or "relative-parent"?RecoveryIndex.DiscoveryDirectories(request).Last():mode=="ancestor"?Path.GetDirectoryName(Path.GetDirectoryName(anchor)!)!:output;Directory.CreateDirectory(root);
                    legacy=StoredOutputInventory.FileFor(Path.Combine(root,"anchor"),intent.Scope);await File.WriteAllTextAsync(legacy,JsonSerializer.Serialize(entry));File.Delete(intent.File);
                    if(mode=="legacy-locked")held=new(legacy,FileMode.Open,FileAccess.Read,FileShare.None);
                }
                if(mode=="missing-original")File.Delete(first.OutputPaths[1]);
            }
            var hashes=first.OutputPaths.Where(File.Exists).ToDictionary(path=>path,path=>System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));reads=0;var callbacks=0;
            var repeatRequest=request with {NoOverwrites=true,FfmpegPath=mode is "legacy-locked" or "missing-original"?ffmpeg:Path.Combine(files.Directory,"unused-ffmpeg.exe")};
            if(mode is "legacy-locked" or "missing-original")
            {
                var result=await Engine().DownloadAsync(repeatRequest,null,null,default,_=>callbacks++);WriteCleanupChecks.Check(result.Succeeded&&reads>0,"Unavailable legacy evidence falsely skipped transfer.");held?.Dispose();held=null;
                WriteCleanupChecks.Check(callbacks==(mode=="missing-original"?1:0),"Protected complete media emitted new callbacks.");
            }
            else
            {
                var results=await Task.WhenAll(Enumerable.Range(0,mode=="concurrent"?6:1).Select(_=>Engine(true).DownloadAsync(repeatRequest,null,null,default,_=>Interlocked.Increment(ref callbacks))));
                WriteCleanupChecks.Check(results.All(result=>result.Succeeded&&result.Skipped)&&reads==0&&callbacks==0,"Recovery/legacy/concurrent inventory requested HTTP or callbacks.");
            }
            foreach(var (path,hash) in hashes)WriteCleanupChecks.Check(hash.AsEnumerable().SequenceEqual(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path))),"Inventory handling changed coded media.");
            foreach(var path in first.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",path,"-map","0:v?","-map","0:a?","-f","null","-"],true);
            var current=(await StoredOutputInventory.ReadAsync(intent.File,default))!;WriteCleanupChecks.Check(current.Schema==2&&StoredOutputInventory.Valid(current),"Media inventory did not migrate/recover.");
            WriteCleanupChecks.Check(Directory.GetFiles(files.Directory,"publication.json",SearchOption.AllDirectories).Length==0&&Directory.GetFiles(files.Directory,"*.tmp",SearchOption.AllDirectories).Length==0,"Recovered storage left transaction debris.");
            reads=0;callbacks=0;await Engine(true).DownloadAsync(repeatRequest,null,null,default,_=>callbacks++);WriteCleanupChecks.Check(reads==0&&callbacks==0,"Cleaned completion lost retained inventory.");
        }
        finally{held?.Dispose();PublicationFaults.Observer=null;}
    }
}

