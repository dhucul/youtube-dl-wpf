namespace YoutubeDl.Engine.Download;

internal static class RecordingReceipt
{
    internal static async Task ReplaceAsync(string pending,string target,CancellationToken ct)
    {
        if(!string.Equals(Path.GetDirectoryName(Path.GetFullPath(pending)),Path.GetDirectoryName(Path.GetFullPath(target)),StringComparison.OrdinalIgnoreCase))throw new IOException("Recording receipt replacement must stay in its owned directory.");
        for(var attempt=0;;attempt++)
        {
            ct.ThrowIfCancellationRequested();Regular(pending);Regular(target);
            try{File.Move(pending,target,true);return;}
            catch(Exception e)when(attempt<7&&e is IOException or UnauthorizedAccessException&&(e.HResult&0xffff) is 5 or 32 or 33&&File.Exists(pending)&&File.Exists(target))
            {await Task.Delay(15*(attempt+1),ct).ConfigureAwait(false);}
        }
    }
    private static void Regular(string path){try{if((File.GetAttributes(path)&(FileAttributes.Directory|FileAttributes.ReparsePoint))!=0)throw new IOException("Recording receipts must be regular owned files.");}catch(FileNotFoundException){}}
}
