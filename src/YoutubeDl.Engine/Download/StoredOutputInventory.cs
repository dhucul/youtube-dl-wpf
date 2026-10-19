using System.Text.Json;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Download;

internal static class StoredOutputInventory
{
    private const int MaximumBytes=8*1024*1024,MaximumRetained=64;
    internal sealed record Intent(string File,string Scope,string Recipe,string Format,bool Retained,bool Processed);
    // Version one has the same field order/digest and remains readable for migration.
    internal sealed record Entry(int Schema,string Scope,string Recipe,string Format,bool Retained,bool Processed,string[] Paths,string Digest);
    internal static string FileFor(string anchor,string scope)=>Path.Combine(Path.GetDirectoryName(anchor)!,".native-output-inventory-"+scope+".json");
    internal static string Digest(Entry value)=>AdaptiveFragmentCheckpoint.Hash(JsonSerializer.Serialize(value with {Digest=""}));
    private static bool Hash(string value)=>value is not null&&Regex.IsMatch(value,@"\A[A-F0-9]{64}\z");
    internal static bool Valid(Entry value)=>value.Schema is 1 or 2&&Hash(value.Scope)&&Hash(value.Recipe)&&Hash(value.Format)&&value.Paths is {Length:>=1 and <=8192}&&
        value.Paths.All(path=>!string.IsNullOrWhiteSpace(path)&&Path.IsPathFullyQualified(path)&&Path.GetFullPath(path)==path)&&value.Paths.Distinct(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal).Count()==value.Paths.Length&&value.Digest==Digest(value);
    internal static void ValidateIntent(Intent intent,string root,string scope,IEnumerable<string> outputs,string? archive)
    {
        if(string.IsNullOrWhiteSpace(intent.File))throw new InvalidDataException("The recording inventory intent has no destination.");
        PublicationTransaction.AbsolutePath(intent.File);
        if(PublicationTransaction.AttributesIfPresent(intent.File) is { } attributes&&(attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("The recording inventory is linked.");
        if(intent.Scope!=scope||!Hash(intent.Scope)||!Hash(intent.Recipe)||!Hash(intent.Format)||Path.GetDirectoryName(intent.File)!=Path.GetDirectoryName(root)||
            intent.File!=FileFor(Path.Combine(Path.GetDirectoryName(root)!,"anchor"),scope)||outputs.Any(path=>Same(path,intent.File))||archive is not null&&(Same(archive,intent.File)||Same(archive+".native.lock",intent.File)))
            throw new InvalidDataException("The recording inventory intent changed or overlaps publication.");
    }
    private static bool Same(string a,string b)=>string.Equals(Path.GetFullPath(a),Path.GetFullPath(b),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);
    internal static async Task<Entry?> ReadAsync(string file,CancellationToken ct)
    {
        PublicationTransaction.PlainPath(file);if(!File.Exists(file))return null;
        await using var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete,65536,FileOptions.Asynchronous);
        // Bound the opened snapshot, including a record replaced between the path check and open.
        if(input.Length>MaximumBytes)return null;
        var bytes=new byte[(int)input.Length];await input.ReadExactlyAsync(bytes,ct).ConfigureAwait(false);
        if(await input.ReadAsync(new byte[1],ct).ConfigureAwait(false)!=0)return null;
        return JsonSerializer.Deserialize<Entry>(bytes);
    }
    internal static async Task<bool> SaveAsync(Intent intent,IReadOnlyList<string> paths,CancellationToken ct=default)
    {
        var temporary=intent.File+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            if(paths.Count is <1 or >8192)return false;
            using var lease=await AcquireAsync(intent.File,ct).ConfigureAwait(false);
            PublicationTransaction.PlainPath(intent.File);foreach(var path in paths)PublicationTransaction.PlainPath(path);
            var value=new Entry(2,intent.Scope,intent.Recipe,intent.Format,intent.Retained,intent.Processed,paths.ToArray(),"");value=value with {Digest=Digest(value)};
            if(!Valid(value))throw new InvalidDataException("The recording inventory has invalid output paths.");
            var bytes=JsonSerializer.SerializeToUtf8Bytes(value);if(bytes.Length>MaximumBytes)return false;
            // Validate cleanup before replacement; a failed cleanup never discards the current record.
            CleanupPending(intent.File);
            try
            {
                var existing=await ReadAsync(intent.File,ct).ConfigureAwait(false);
                if(existing is not null&&Valid(existing))
                {
                    if(existing.Digest==value.Digest)return true;
                    if(existing.Scope!=intent.Scope)throw new InvalidDataException("The recording inventory changed scope.");
                    await RetainAsync(intent.File,existing,ct).ConfigureAwait(false);
                }
            }
            catch(JsonException){}
            await PublicationFaults.WriteAsync(temporary,bytes,"inventory",ct).ConfigureAwait(false);PublicationTransaction.PlainPath(intent.File);File.Move(temporary,intent.File,true);
            await PublicationFaults.AtAsync("inventory-replaced").ConfigureAwait(false);CleanupPending(intent.File);return true;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or OperationCanceledException){return false;}
        finally{if(File.Exists(temporary))try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    private static async Task<FileStream> AcquireAsync(string file,CancellationToken ct)
    {
        PublicationTransaction.AbsolutePath(file);
        var identity=OperatingSystem.IsWindows()?file.ToUpperInvariant():file;
        var directory=Path.Combine(Path.GetTempPath(),"YoutubeDlNativeLocks");Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"inventory-"+AdaptiveFragmentCheckpoint.Hash(identity)+".lock");
        while(true)
        {
            ct.ThrowIfCancellationRequested();PublicationTransaction.PlainPath(path);
            try{return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException e)when((e.HResult&0xffff)is 32 or 33){await Task.Delay(25,ct).ConfigureAwait(false);}
        }
    }
    internal static string RetainedFile(string file,Entry value)=>file+"."+value.Digest+".retained.json";
    private static async Task RetainAsync(string file,Entry value,CancellationToken ct)
    {
        var retained=RetainedFile(file,value);PublicationTransaction.PlainPath(retained);
        if(File.Exists(retained))
        {
            var previous=await ReadAsync(retained,ct).ConfigureAwait(false);
            if(previous is null||!Valid(previous)||previous.Digest!=value.Digest)throw new InvalidDataException("A retained recording inventory changed.");
            return;
        }
        var records=Directory.EnumerateFiles(Path.GetDirectoryName(file)!,Path.GetFileName(file)+".*.retained.json").Take(MaximumRetained+1).ToArray();
        if(records.Length>=MaximumRetained)throw new IOException("The recording inventory retention limit was reached; previous records were retained.");
        var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            await PublicationFaults.WriteAsync(temporary,JsonSerializer.SerializeToUtf8Bytes(value),"inventory-retention",ct).ConfigureAwait(false);
            File.Move(temporary,retained);await PublicationFaults.AtAsync("inventory-retained").ConfigureAwait(false);
        }
        finally{if(File.Exists(temporary))try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    private static void CleanupPending(string file)
    {
        var pending=Directory.EnumerateFiles(Path.GetDirectoryName(file)!,Path.GetFileName(file)+".*.tmp").Take(65).ToArray();
        if(pending.Length>64)throw new IOException("There are too many pending recording inventory writes.");
        foreach(var path in pending)
        {
            if(!Regex.IsMatch(Path.GetFileName(path),@"\A"+Regex.Escape(Path.GetFileName(file))+@"\.[a-f0-9]{32}\.tmp\z"))throw new InvalidDataException("A pending recording inventory name changed.");
            PublicationTransaction.PlainPath(path);
        }
        foreach(var path in pending)File.Delete(path);
    }
}
