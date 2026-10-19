using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Download;

internal static class PublicationFaults
{
    internal static Func<string,Task>? Observer {get;set;}
    internal static Task AtAsync(string phase)=>Observer?.Invoke(phase)??Task.CompletedTask;
    internal static async Task WriteAsync(string path,byte[] bytes,string phase,CancellationToken ct)
    {
        await using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous);
        var half=bytes.Length/2;await stream.WriteAsync(bytes.AsMemory(0,half),ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);await AtAsync(phase+"-writing").ConfigureAwait(false);await stream.WriteAsync(bytes.AsMemory(half),ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);stream.Flush(true);await AtAsync(phase+"-flushed").ConfigureAwait(false);
    }
}

// A durable plan is written before sibling files are created or previous outputs are moved.
internal sealed class PublicationTransaction
{
    internal sealed record ArchiveBinding(string Path,DownloadArchiveIdentity Identity);
    internal sealed record CompletionContext(ArchiveBinding? Archive,
        [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] StoredOutputInventory.Intent? Inventory=null);
    internal sealed record Fingerprint(long Bytes,string Digest);
    internal sealed record Item(string Source,string Target,string Temporary,string Backup,Fingerprint? Before,Fingerprint After);
    private sealed record State(int Schema,string Scope,string Recipe,string SourceIdentity,bool Committed,bool Archived,List<Item> Items,
        List<string> Media,List<string> NewMedia,List<string> Sidecars,List<string> Claimed,string Digest,
        [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] List<string>? ArchiveTemporaries=null,
        [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] CompletionContext? Context=null);
    private readonly string _root,_file;private State _state;
    internal bool Committed=>_state.Committed;
    internal bool Archived=>_state.Archived;
    internal string Recipe=>_state.Recipe;
    internal string SourceIdentity=>_state.SourceIdentity;
    internal CompletionContext? Context=>_state.Context;
    internal static CompletionContext ContextFor(NativeDownloadRequest request,Discovery.PageMetadata page)=>
        new(string.IsNullOrWhiteSpace(request.DownloadArchiveFile)||!request.ForceWriteArchive&&(request.SkipDownload||request.Simulate||request.IgnoreNoFormatsError&&page.Media.Count==0)?null:
            new(Path.GetFullPath(request.DownloadArchiveFile),DownloadArchiveIdentity.FromMetadata(page,request.Url)));
    internal IReadOnlyList<Item> Items=>_state.Items;
    internal PublishedMediaOutputs Outputs=>new(_state.Media,_state.NewMedia){CommittedSidecars=_state.Sidecars};
    private PublicationTransaction(string root,State state){_root=root;_file=Path.Combine(root,"publication.json");_state=state;}
    internal static PublicationTransaction? Load(string root,string scope)
    {
        var file=Path.Combine(root,"publication.json");Regular(file);if(!File.Exists(file))return null;
        if(new FileInfo(file).Length>4*1024*1024)throw new InvalidDataException("The publication receipt exceeds its limit.");
        using var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
        var state=JsonSerializer.Deserialize<State>(input)??throw new InvalidDataException("The publication receipt is empty.");
        if(state.Schema!=1||state.Scope!=scope||state.Items is null||state.Items.Count is 0 or >4096||state.Media is null||state.NewMedia is null||state.Sidecars is null||state.Claimed is null||
            state.Digest!=Digest(state)||!Hash(state.Recipe)||!Hash(state.SourceIdentity))throw new InvalidDataException("The publication receipt changed or belongs to another job.");
        var paths=new HashSet<string>(Comparer);
        foreach(var item in state.Items)
        {
            var source=Path.GetFullPath(item.Source);if(!source.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,Comparison))throw new IOException("The publication source is outside its owned workspace.");
            PlainPath(item.Target);PlainPath(item.Temporary);PlainPath(item.Backup);
            if(Path.GetDirectoryName(item.Target)!=Path.GetDirectoryName(item.Temporary)||Path.GetDirectoryName(item.Target)!=Path.GetDirectoryName(item.Backup)||
                !Regex.IsMatch(Path.GetFileName(item.Temporary),@"\A\.native-save-[a-f0-9]{32}\z")||Path.GetFileName(item.Backup)!=".native-backup-"+Path.GetFileName(item.Temporary)[".native-save-".Length..]||
                !paths.Add(item.Target)||!paths.Add(item.Temporary)||!paths.Add(item.Backup)||!Valid(item.After)||item.Before is not null&&!Valid(item.Before))
                throw new InvalidDataException("The publication receipt has invalid or overlapping file paths.");
        }
        if(state.Media.Count>4096||state.NewMedia.Any(path=>!state.Media.Contains(path,Comparer))||state.Sidecars.Any(path=>!state.Items.Any(item=>Comparer.Equals(item.Target,path)))||
            state.Claimed.Any(path=>!state.NewMedia.Contains(path,Comparer))||state.Claimed.Distinct(Comparer).Count()!=state.Claimed.Count)
            throw new InvalidDataException("The publication output inventory is invalid.");
        if(state.ArchiveTemporaries is { } archiveFiles)
        {
            if(archiveFiles.Count>16||archiveFiles.Distinct(Comparer).Count()!=archiveFiles.Count)throw new InvalidDataException("The archive write inventory exceeds its limit.");
            foreach(var path in archiveFiles){PlainPath(path);if(!Regex.IsMatch(Path.GetFileName(path),@"\A\.native-archive-[a-f0-9]{32}\.tmp\z")||!paths.Add(path))throw new InvalidDataException("An archive temporary path is invalid.");}
        }
        if(state.Context?.Archive is { } archive)
        {
            AbsolutePath(archive.Path);try{if((File.GetAttributes(archive.Path)&FileAttributes.ReparsePoint)!=0)throw new IOException("The completion archive is linked.");}catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
            _ = archive.Identity.Record;
            if(state.Items.Any(item=>Comparer.Equals(item.Target,archive.Path)||Comparer.Equals(item.Target,archive.Path+".native.lock")))throw new InvalidDataException("The completion archive overlaps a published output.");
        }
        if(state.Context?.Inventory is { } inventory)StoredOutputInventory.ValidateIntent(inventory,root,scope,state.Media.Concat(state.Sidecars),state.Context.Archive?.Path);
        return new(root,state);
    }
    internal static async Task<PublicationTransaction> PrepareAsync(string root,string scope,string recipe,string sourceIdentity,IReadOnlyList<(string Source,string Target)> sources,
        IReadOnlyList<string> media,IReadOnlyList<string> newMedia,IReadOnlyList<string> sidecars,CancellationToken ct,CompletionContext? context=null)
    {
        var rows=new List<Item>();
        foreach(var pair in sources)
        {
            PlainPath(pair.Target);var after=await DescribeAsync(pair.Source,ct).ConfigureAwait(false);var before=File.Exists(pair.Target)?await DescribeAsync(pair.Target,ct).ConfigureAwait(false):null;
            var id=Guid.NewGuid().ToString("N");var directory=Path.GetDirectoryName(pair.Target)!;
            rows.Add(new(pair.Source,pair.Target,Path.Combine(directory,".native-save-"+id),Path.Combine(directory,".native-backup-"+id),before,after));
        }
        var state=new State(1,scope,recipe,sourceIdentity,false,false,rows,media.ToList(),newMedia.ToList(),sidecars.ToList(),[],"",Context:context??new(null));
        var transaction=new PublicationTransaction(root,state);await transaction.SaveAsync(ct).ConfigureAwait(false);await PublicationFaults.AtAsync("prepared").ConfigureAwait(false);return transaction;
    }
    internal async Task CommitAsync()
    {
        await ValidateCommittedAsync().ConfigureAwait(false);_state=_state with {Committed=true};await SaveAsync(CancellationToken.None).ConfigureAwait(false);await PublicationFaults.AtAsync("committed").ConfigureAwait(false);
        await EnsureInventoryAsync().ConfigureAwait(false);
    }
    internal async Task<bool> EnsureInventoryAsync()
    {
        if(_state.Context?.Inventory is not { } intent)return true;
        await ValidateCommittedAsync().ConfigureAwait(false);
        return await StoredOutputInventory.SaveAsync(intent,_state.Media).ConfigureAwait(false);
    }
    internal async Task<bool> MigrateInventoryAsync(StoredOutputInventory.Intent intent)
    {
        if(_state.Context?.Inventory is not null)return await EnsureInventoryAsync().ConfigureAwait(false);
        if(_state.Context is null&&!_state.Archived)return false; // An unknown original archive context cannot be invented.
        StoredOutputInventory.ValidateIntent(intent,_root,_state.Scope,_state.Media.Concat(_state.Sidecars),_state.Context?.Archive?.Path);
        await ValidateCommittedAsync().ConfigureAwait(false);_state=_state with {Context=(_state.Context??new(null)) with {Inventory=intent}};
        await SaveAsync(CancellationToken.None).ConfigureAwait(false);await PublicationFaults.AtAsync("inventory-intent-migrated").ConfigureAwait(false);
        return await EnsureInventoryAsync().ConfigureAwait(false);
    }
    internal async Task ArchivedAsync()
    {
        await ValidateCommittedAsync().ConfigureAwait(false);_state=_state with {Archived=true};await SaveAsync(CancellationToken.None).ConfigureAwait(false);await PublicationFaults.AtAsync("archived").ConfigureAwait(false);
    }
    internal async Task NotifyAsync(Action<string>? notify)
    {
        await ValidateCommittedAsync().ConfigureAwait(false);
        foreach(var path in _state.NewMedia)
        {
            if(_state.Claimed.Contains(path,Comparer))continue;
            // Delivery intent precedes the external callback: retries never repeat a claimed callback.
            _state.Claimed.Add(path);await SaveAsync(CancellationToken.None).ConfigureAwait(false);
            notify?.Invoke(path);await PublicationFaults.AtAsync("notified:"+(_state.Claimed.Count-1)).ConfigureAwait(false);
        }
    }
    internal async Task<bool> ClaimAsync(string path)
    {
        if(!_state.NewMedia.Contains(path,Comparer))throw new InvalidDataException("The notification path is outside its output set.");
        if(_state.Claimed.Contains(path,Comparer))return false;
        await ValidateCommittedAsync().ConfigureAwait(false);_state.Claimed.Add(path);await SaveAsync(CancellationToken.None).ConfigureAwait(false);return true;
    }
    internal bool AllClaimed=>_state.NewMedia.All(path=>_state.Claimed.Contains(path,Comparer));
    internal async Task RegisterArchiveTemporaryAsync(string path)
    {
        PlainPath(path);if(!Regex.IsMatch(Path.GetFileName(path),@"\A\.native-archive-[a-f0-9]{32}\.tmp\z")||File.Exists(path))throw new IOException("The archive write path is not available.");
        var files=(_state.ArchiveTemporaries??[]).ToList();if(files.Count>=16)throw new IOException("This job exceeded its archive write recovery limit.");
        files.Add(path);_state=_state with {ArchiveTemporaries=files};await SaveAsync(CancellationToken.None,"archive-plan").ConfigureAwait(false);
    }
    internal async Task ValidateCommittedAsync()
    {
        foreach(var item in _state.Items)
            if(!await MatchesAsync(item.Target,item.After).ConfigureAwait(false))throw new IOException("A published output changed before its completion could be recovered.");
        foreach(var path in _state.Media){PlainPath(path);if(!File.Exists(path))throw new IOException("A published media output is missing.");}
    }
    internal async Task RollbackAsync()
    {
        // Check the entire set before changing anything, including files installed just before a crash.
        foreach(var item in _state.Items)
        {
            PlainPath(item.Target);PlainPath(item.Backup);PlainPath(item.Temporary);
            if(File.Exists(item.Backup))
            {
                if(item.Before is null||!await MatchesAsync(item.Backup,item.Before).ConfigureAwait(false)||File.Exists(item.Target)&&!await MatchesAsync(item.Target,item.After).ConfigureAwait(false))
                    throw new IOException("A recovery backup or installed output changed; previous files were retained.");
            }
            else if(item.Before is not null&&!await MatchesAsync(item.Target,item.Before).ConfigureAwait(false))
                throw new IOException("A previous output cannot be safely restored from its publication receipt.");
            else if(item.Before is null&&File.Exists(item.Target)&&!await MatchesAsync(item.Target,item.After).ConfigureAwait(false))
                throw new IOException("An unrelated output appeared during publication recovery.");
        }
        var restored=0;
        foreach(var item in _state.Items.AsEnumerable().Reverse())
        {
            if(File.Exists(item.Backup)){if(File.Exists(item.Target))File.Delete(item.Target);File.Move(item.Backup,item.Target);}
            else if(item.Before is null&&File.Exists(item.Target))File.Delete(item.Target);
            if(File.Exists(item.Temporary))File.Delete(item.Temporary);
            await PublicationFaults.AtAsync("rollback-restored:"+restored++).ConfigureAwait(false);
        }
        File.Delete(_file);
    }
    internal async Task DeleteSiblingsAsync()
    {
        foreach(var item in _state.Items){Regular(item.Temporary);Regular(item.Backup);if(File.Exists(item.Backup)&&(item.Before is null||!await MatchesAsync(item.Backup,item.Before).ConfigureAwait(false)))throw new IOException("A publication backup changed before cleanup.");}
        foreach(var path in _state.ArchiveTemporaries??[])PlainPath(path);
        var index=0;
        foreach(var item in _state.Items){if(File.Exists(item.Temporary))File.Delete(item.Temporary);if(File.Exists(item.Backup))File.Delete(item.Backup);await PublicationFaults.AtAsync("cleanup-sibling:"+index++).ConfigureAwait(false);}
        foreach(var path in _state.ArchiveTemporaries??[])if(File.Exists(path))File.Delete(path);
    }
    private async Task SaveAsync(CancellationToken ct,string? phase=null)
    {
        var state=_state with {Digest=""};state=state with {Digest=Digest(state)};var bytes=JsonSerializer.SerializeToUtf8Bytes(state);
        if(bytes.Length>4*1024*1024)throw new InvalidDataException("The publication receipt exceeds its limit.");
        var pending=_file+"."+Guid.NewGuid().ToString("N")+".tmp";Regular(_file);
        phase??=_state.Archived?(_state.Claimed.Count>0?"claim":"archive"):_state.Committed?"commit":"plan";
        try{await PublicationFaults.WriteAsync(pending,bytes,"receipt-"+phase,ct).ConfigureAwait(false);File.Move(pending,_file,true);_state=state;await PublicationFaults.AtAsync("receipt-"+phase+"-replaced").ConfigureAwait(false);}
        finally{try{File.Delete(pending);}catch(IOException){}}
    }
    private static string Digest(State state)=>AdaptiveFragmentCheckpoint.Hash(JsonSerializer.Serialize(state with {Digest=""}));
    private static bool Hash(string? value)=>value is {Length:64}&&value.All(c=>c is >= 'A' and <= 'F' or >= '0' and <= '9');
    private static bool Valid(Fingerprint fingerprint)=>fingerprint.Bytes>=0&&fingerprint.Bytes<=100L*1024*1024*1024&&Hash(fingerprint.Digest);
    internal static async Task<Fingerprint> DescribeAsync(string path,CancellationToken ct)
    {
        Regular(path);await using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous);
        if(input.Length>100L*1024*1024*1024)throw new IOException("A publication file exceeds its size limit.");
        return new(input.Length,Convert.ToHexString(await SHA256.HashDataAsync(input,ct).ConfigureAwait(false)));
    }
    internal static async Task<bool> MatchesAsync(string path,Fingerprint fingerprint)
    {
        Regular(path);if(!File.Exists(path)||new FileInfo(path).Length!=fingerprint.Bytes)return false;return(await DescribeAsync(path,CancellationToken.None).ConfigureAwait(false)).Digest==fingerprint.Digest;
    }
    internal static void PlainPath(string path)
    {
        AbsolutePath(path);Regular(path);
    }
    internal static void AbsolutePath(string path)
    {
        if(!Path.IsPathFullyQualified(path)||Path.GetFullPath(path)!=path||OperatingSystem.IsWindows()&&(Path.GetFileName(path).Contains(':')||path.StartsWith(@"\\.\",StringComparison.Ordinal)))throw new IOException("Publication paths must be ordinary absolute files.");
        var directory=Path.GetDirectoryName(path);
        while(directory is not null)
        {
            if(AttributesIfPresent(directory) is { } attributes&&(attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Publication cannot follow a linked folder.");
            directory=Path.GetDirectoryName(directory);
        }
    }
    internal static void Regular(string path)
    {if(AttributesIfPresent(path) is { } attributes&&(attributes&(FileAttributes.Directory|FileAttributes.ReparsePoint))!=0)throw new IOException("Publication files must be regular owned files.");}
    internal static FileAttributes? AttributesIfPresent(string path)
    {
        for(var attempt=0;;attempt++)
        {
            try{return File.GetAttributes(path);}
            catch(FileNotFoundException){return null;}catch(DirectoryNotFoundException){return null;}
            // A Windows ancestor pending deletion can briefly deny child attribute reads.
            catch(UnauthorizedAccessException)when(OperatingSystem.IsWindows()&&attempt<8)
            {Thread.Sleep(1<<Math.Min(attempt,4));}
        }
    }
    private static StringComparer Comparer=>OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal;
    private static StringComparison Comparison=>OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
}
