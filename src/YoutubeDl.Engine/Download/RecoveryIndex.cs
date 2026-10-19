using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Download;

/// <summary>Finds owned pending jobs without scanning arbitrary output volumes.</summary>
internal static class RecoveryIndex
{
    private sealed record Entry(int Schema,string Root,string Scope,string Recipe,string SourceIdentity,string Digest,
        [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? IndexFile=null);
    private static string RegistryDirectory=>Path.Combine(Path.GetTempPath(),"YoutubeDlNativeRecovery");
    internal static string PathFor(string directory,string scope)=>Path.Combine(Path.GetFullPath(directory),".native-recovery-"+scope+".json");
    internal static string RegistryPath(string source,string scope)=>Path.Combine(RegistryDirectory,source+"-"+scope+".json");
    internal static async Task<string> SaveAsync(string directory,string root,string scope,string recipe,string source,CancellationToken ct)
    {
        var file=PathFor(directory,scope);PublicationTransaction.PlainPath(file);Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var entry=new Entry(1,root,scope,recipe,source,"",file);entry=entry with {Digest=Digest(entry)};
        if(File.Exists(file))
        {
            var previous=Read(file);SameJob(previous,entry);
            if(previous==entry){await RegisterAsync(entry,ct).ConfigureAwait(false);DeletePending(file);return file;}
        }
        await WriteAsync(file,entry,"locator",ct).ConfigureAwait(false);
        await RegisterAsync(entry,ct).ConfigureAwait(false);await PublicationFaults.AtAsync("locator-replaced").ConfigureAwait(false);
        DeletePending(file);return file;
    }
    private static async Task RegisterAsync(Entry entry,CancellationToken ct)
    {
        var file=RegistryPath(entry.SourceIdentity,entry.Scope);PublicationTransaction.PlainPath(file);Directory.CreateDirectory(RegistryDirectory);
        Entry? previous=null;if(File.Exists(file)){previous=Read(file,true);SameJob(previous,entry);}
        if(previous!=entry)await WriteAsync(file,entry,"registry",ct).ConfigureAwait(false);
        if(previous?.IndexFile is { } old&&old!=entry.IndexFile&&File.Exists(old))
        {
            var obsolete=Read(old);if(obsolete!=previous)throw new IOException("The previous recovery record changed; it was retained.");
            File.Delete(old);DeletePending(old);
        }
        DeletePending(file);
    }
    private static async Task WriteAsync(string file,Entry entry,string phase,CancellationToken ct)
    {
        var pending=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{await PublicationFaults.WriteAsync(pending,JsonSerializer.SerializeToUtf8Bytes(entry),phase,ct).ConfigureAwait(false);File.Move(pending,file,true);}
        finally{if(File.Exists(pending))File.Delete(pending);}
    }
    private static void SameJob(Entry a,Entry b){if(a.Root!=b.Root||a.Scope!=b.Scope||a.SourceIdentity!=b.SourceIdentity)throw new IOException("A recovery record changed; its previous job was retained.");}
    private static Entry Read(string file,bool registry=false)
    {
        PublicationTransaction.PlainPath(file);if(new FileInfo(file).Length>16384)throw new InvalidDataException("A recovery locator exceeds its limit.");
        using var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);var entry=JsonSerializer.Deserialize<Entry>(input)??throw new InvalidDataException("The recovery locator is empty.");
        if(entry.Schema!=1||!Hash(entry.Scope)||!Hash(entry.Recipe)||!Hash(entry.SourceIdentity)||entry.Digest!=Digest(entry)||
            Path.GetFileName(file)!=(registry?entry.SourceIdentity+"-"+entry.Scope+".json":".native-recovery-"+entry.Scope+".json")||
            Path.GetFullPath(entry.Root)!=entry.Root||Path.GetFileName(entry.Root)!=".native-download-"+entry.Scope[..24]||
            registry&&entry.IndexFile is null)throw new InvalidDataException("The recovery locator changed or has an invalid workspace.");
        PublicationTransaction.PlainPath(Path.Combine(entry.Root,"publication.json"));
        if(entry.IndexFile is { } index){PublicationTransaction.PlainPath(index);if(PathFor(Path.GetDirectoryName(index)!,entry.Scope)!=index||!registry&&index!=file)throw new InvalidDataException("The recovery record changed its index location.");}
        return entry;
    }
    internal static IEnumerable<(DownloadWorkspace.Completion Completion,string Recipe,string Source)> Find(string directory,string? source=null)
    {
        var seen=new HashSet<string>(StringComparer.Ordinal);
        if(source is not null&&Directory.Exists(RegistryDirectory))
        {
            if(!Hash(source))throw new InvalidDataException("The recovery source identity is invalid.");
            var files=Directory.GetFiles(RegistryDirectory,source+"-*.json");if(files.Length>2048)throw new IOException("There are too many pending recovery records.");
            foreach(var file in files)
            {
                var entry=ReadIfPresent(file,true);if(entry is null)continue;if(entry.SourceIdentity!=source)throw new InvalidDataException("The recovery record changed source identity.");
                if(!Directory.Exists(entry.Root))
                {
                    if(ReadIfPresent(entry.IndexFile!) is { } local){if(local!=entry)throw new IOException("A completed recovery locator changed.");DeleteIfPresent(entry.IndexFile!);DeletePending(entry.IndexFile!);}
                    DeleteIfPresent(file);DeletePending(file);continue;
                }
                if(ReadIfPresent(entry.IndexFile!) is { } active&&active!=entry)throw new IOException("The local and registered recovery records disagree.");
                seen.Add(entry.Scope);yield return(new(entry.Root,entry.Scope,entry.IndexFile),entry.Recipe,entry.SourceIdentity);
            }
        }
        if(!Directory.Exists(directory))yield break;
        var localFiles=Directory.GetFiles(directory,".native-recovery-*.json");if(localFiles.Length>2048)throw new IOException("There are too many pending recovery locators.");
        foreach(var file in localFiles){var entry=ReadIfPresent(file);if(entry is not null&&(source is null||entry.SourceIdentity==source)&&seen.Add(entry.Scope))yield return(new(entry.Root,entry.Scope,file),entry.Recipe,entry.SourceIdentity);}
    }
    internal static void Delete(DownloadWorkspace.Completion completion)
    {
        if(completion.IndexFile is { } file&&File.Exists(file))
        {
            var entry=ReadIfPresent(file);if(entry is not null&&(entry.Root!=completion.Root||entry.Scope!=completion.Scope))throw new IOException("The recovery locator changed before cleanup.");
            DeleteIfPresent(file);DeletePending(file);
        }
        if(Directory.Exists(RegistryDirectory))
        {
            if(!Hash(completion.Scope))throw new InvalidDataException("The completion scope is invalid.");
            var files=Directory.GetFiles(RegistryDirectory,"*-"+completion.Scope+".json");if(files.Length>1)throw new InvalidDataException("The registered recovery scope is ambiguous.");
            foreach(var path in files){var entry=ReadIfPresent(path,true);if(entry is null)continue;if(entry.Root!=completion.Root||completion.IndexFile is not null&&entry.IndexFile!=completion.IndexFile)throw new IOException("The registered recovery record changed.");DeleteIfPresent(path);DeletePending(path);}
        }
    }
    // Discovery and cleanup may race a completed job removing its optional locator.
    // Malformed, linked or changed records still fail through Read's validation.
    private static Entry? ReadIfPresent(string file,bool registry=false)
    {
        for(var attempt=0;;attempt++)
        {
            try{return Read(file,registry);}
            catch(FileNotFoundException)when(Missing(file)){return null;}
            catch(DirectoryNotFoundException)when(Missing(file)){return null;}
            // Windows can report access denied while another read handle drains a delete-pending file.
            // Only confirmed disappearance is optional; persistent access failures still propagate.
            catch(UnauthorizedAccessException)when(OperatingSystem.IsWindows()&&attempt<8)
            {if(Missing(file))return null;Thread.Sleep(1);}
        }
    }
    private static bool Missing(string file)
    {try{_ = File.GetAttributes(file);return false;}catch(FileNotFoundException){return true;}catch(DirectoryNotFoundException){return true;}}
    private static void DeleteIfPresent(string file)
    {
        for(var attempt=0;;attempt++)
        {
            try{File.Delete(file);return;}
            catch(UnauthorizedAccessException)when(OperatingSystem.IsWindows()&&attempt<8)
            {if(Missing(file))return;Thread.Sleep(1);}
        }
    }
    private static void DeletePending(string file)
    {
        var paths=Directory.GetFiles(Path.GetDirectoryName(file)!,Path.GetFileName(file)+".*.tmp");if(paths.Length>64)throw new IOException("Too many pending locator writes.");
        foreach(var path in paths){if(!Regex.IsMatch(Path.GetFileName(path),@"\A"+Regex.Escape(Path.GetFileName(file))+@"\.[a-f0-9]{32}\.tmp\z"))throw new InvalidDataException("A pending locator filename changed.");PublicationTransaction.PlainPath(path);File.Delete(path);}
    }
    internal static IEnumerable<string> LegacyRoots(string directory)
    {
        if(!Directory.Exists(directory))yield break;
        var pending=new Stack<(string Path,int Depth)>();pending.Push((Path.GetFullPath(directory),0));int visited=0,roots=0;
        while(pending.Count>0)
        {
            var (path,depth)=pending.Pop();if(++visited>8192)throw new IOException("Pending recovery discovery exceeds its folder limit.");
            if(PublicationTransaction.AttributesIfPresent(path) is not { } attributes||(attributes&FileAttributes.ReparsePoint)!=0)continue;
            foreach(var child in DirectoriesIfPresent(path))
            {
                if(PublicationTransaction.AttributesIfPresent(child) is not { } childAttributes||(childAttributes&FileAttributes.ReparsePoint)!=0)continue;
                if(Path.GetFileName(child).StartsWith(".native-download-",StringComparison.Ordinal))
                {if(++roots>2048)throw new IOException("There are too many pending output workspaces.");yield return child;}
                else if(depth<16)pending.Push((child,depth+1));
            }
        }
    }
    private static string[] DirectoriesIfPresent(string path)
    {
        for(var attempt=0;;attempt++)
        {
            try
            {
                var directories=Directory.EnumerateDirectories(path).Take(8193).ToArray();
                if(directories.Length>8192)throw new IOException("Pending recovery discovery exceeds its folder limit.");
                return directories;
            }
            catch(FileNotFoundException){return [];}catch(DirectoryNotFoundException){return [];}
            catch(UnauthorizedAccessException)when(OperatingSystem.IsWindows()&&attempt<8)
            {Thread.Sleep(1<<Math.Min(attempt,4));}
        }
    }
    internal static IEnumerable<string> DiscoveryDirectories(NativeDownloadRequest request)
    {
        yield return Path.GetFullPath(request.OutputDirectory);
        var template=request.OutputTemplate.Replace('/',Path.DirectorySeparatorChar).Replace('\\',Path.DirectorySeparatorChar);
        var percent=template.IndexOf('%');var prefix=percent<0?template:template[..percent];var slash=prefix.LastIndexOf(Path.DirectorySeparatorChar);
        if(slash<0)yield break;var directory=prefix[..(slash+1)];directory=Path.GetFullPath(Path.IsPathRooted(directory)?directory:Path.Combine(request.OutputDirectory,directory));
        if(directory!=Path.GetPathRoot(directory)&&directory!=Path.GetFullPath(request.OutputDirectory))yield return directory;
    }
    private static bool Hash(string value)=>Regex.IsMatch(value??"", @"\A[A-F0-9]{64}\z");
    private static string Digest(Entry entry)=>AdaptiveFragmentCheckpoint.Hash(JsonSerializer.Serialize(entry with {Digest=""}));
}
