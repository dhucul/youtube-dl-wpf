using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

internal static class RecordingReceiptChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>new[]{"released read lock","persistent read lock","cancelled lock","directory target","capture reopen"}.Select(m=>("HDR reconstruction Windows receipt "+m,(Func<Task>)(()=>Run(m))));
    private static void Check(bool condition,string message)=>WriteCleanupChecks.Check(condition,message);
    private static async Task Run(string mode)
    {
        var parent=Path.GetFullPath(Path.GetTempPath());var root=Path.GetFullPath(Path.Combine(parent,"native-receipt-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
        try
        {
            if(mode=="capture reopen")
            {
                using var http=new MediaHttp();var destination=Path.Combine(root,"out.mp4");using(var checkpoint=await TimelineCheckpoint.OpenAsync(http,destination,"fixture",default))
                {
                    async Task<IndexedDashDownloader.ResourceVersion?> Fetch(string path,CancellationToken ct){await File.WriteAllBytesAsync(path,new byte[]{1,2,3},ct);return null;}await checkpoint.CaptureAsync("one","facts",new("https://receipt.example/one"),null,[1,2,3],Path.Combine(root,"one.bin"),Fetch,default);var receipt=Directory.GetFiles(root,"timeline.json",SearchOption.AllDirectories).Single();using var read=new FileStream(receipt,FileMode.Open,FileAccess.Read,FileShare.Read);var capture=checkpoint.CaptureAsync("two","facts",new("https://receipt.example/two"),null,[1,2,3],Path.Combine(root,"two.bin"),Fetch,default);await Task.Delay(60);read.Dispose();await capture;Check(checkpoint.Records.Count==2,"Receipt retry lost in-memory records.");
                }
                using var restored=await TimelineCheckpoint.OpenAsync(http,destination,"fixture",default);Check(restored.Records.Count==2,"Released reader prevented a durable timeline record.");return;
            }
            var target=Path.Combine(root,"timeline.json");var pending=Path.Combine(root,"pending.tmp");await File.WriteAllTextAsync(pending,"new");if(mode=="directory target"){Directory.CreateDirectory(target);try{await RecordingReceipt.ReplaceAsync(pending,target,default);throw new Exception("Directory target was replaced.");}catch(IOException){}Check(Directory.Exists(target)&&File.Exists(pending),"Rejected receipt target changed.");return;}
            if(!OperatingSystem.IsWindows())return;await File.WriteAllTextAsync(target,"old");using var held=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read);using var cancel=new CancellationTokenSource();if(mode=="cancelled lock")cancel.CancelAfter(45);var save=RecordingReceipt.ReplaceAsync(pending,target,cancel.Token);
            if(mode=="released read lock"){await Task.Delay(60);Check(!save.IsCompleted,"Read lock did not block atomic replacement.");held.Dispose();await save;Check(await File.ReadAllTextAsync(target)=="new"&&!File.Exists(pending),"Released reader lost replacement bytes.");}
            else{try{await save;throw new Exception("Persistent receipt lock succeeded.");}catch(OperationCanceledException)when(mode=="cancelled lock"){}catch(Exception e)when(mode=="persistent read lock"&&e is IOException or UnauthorizedAccessException){}held.Dispose();Check(await File.ReadAllTextAsync(target)=="old"&&await File.ReadAllTextAsync(pending)=="new","Denied/cancelled replacement changed receipt bytes.");}
        }
        finally{if(!root.StartsWith(Path.Combine(parent,"native-receipt-"),StringComparison.OrdinalIgnoreCase)||Path.GetDirectoryName(root)!=parent.TrimEnd(Path.DirectorySeparatorChar))throw new IOException("Unexpected owned receipt-test root.");Directory.Delete(root,true);}
    }
}
