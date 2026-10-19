using System.Security.Cryptography;
using System.Text;

namespace YoutubeDl.Engine.Download;

/// <summary>Keeps unfinished media away from published files and commits a complete output set.</summary>
internal sealed class DownloadWorkspace : IDisposable
{
    private readonly string _root;
    private readonly string _outputDirectory;
    private readonly HashSet<string> _protectedPaths;
    private bool _published;
    private bool _recoveryFailed;
    private readonly string _scope,_recipe,_sourceIdentity;private readonly FileStream _lease;
    private PublicationTransaction? _transaction;
    private string? _indexFile;
    private PublicationTransaction.CompletionContext _context=new(null);
    internal sealed record Completion(string Root,string Scope,string? IndexFile=null);
    internal Completion? PendingCompletion=>_transaction is {Committed:true}?new(_root,_scope,_indexFile):null;
    internal static string Recipe(NativeDownloadRequest request)=>AdaptiveFragmentCheckpoint.Hash(System.Text.Json.JsonSerializer.Serialize(request with
    {CookieFile=null,BrowserCookies=null,Proxy=null,SourceAddress=null,UserAgent=null,Referer=null,CaptureText=null,FfmpegPath=null,RateLimit=null,ExternalDownloader=null,ExternalDownloaderArguments=null,Retries=0,FragmentRetries=null,TimeoutSeconds=0,ConcurrentFragments=1,NoContinue=false,NoPart=false,NoOverwrites=false,NoPostOverwrites=false,BufferSize="",HttpChunkSize="",NoResizeBuffer=false,SleepIntervalSeconds=null,MaximumSleepIntervalSeconds=null,SubtitleSleepSeconds=null,RequestSleepSeconds=null}));
    public string RawDirectory { get; }
    public string ProcessingDirectory { get; }

    public DownloadWorkspace(string destination, Uri source, IReadOnlyList<string>? protectedPaths = null,string? recipe=null)
    {
        _outputDirectory = Path.GetFullPath(Path.GetDirectoryName(destination)!);
        var key = Encoding.UTF8.GetBytes(Path.GetFullPath(destination) + "\n" + source.AbsoluteUri);
        _scope=Convert.ToHexString(SHA256.HashData(key));_recipe=recipe??_scope;_sourceIdentity=AdaptiveFragmentCheckpoint.Hash(source.AbsoluteUri);
        _root = Path.Combine(_outputDirectory, ".native-download-" + _scope[..24]);
        PublicationTransaction.PlainPath(Path.Combine(_root,"publication.json"));
        _protectedPaths = new((protectedPaths ?? []).Select(Path.GetFullPath), OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (_protectedPaths.Any(path => path.Equals(_root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            path.StartsWith(_root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            throw new IOException("The download archive cannot be stored inside this download's temporary workspace.");
        RawDirectory = Path.Combine(_root, "raw");
        ProcessingDirectory = Path.Combine(_root, "processing");
        CreateOwnedDirectory(_root);
        var lockPath=LockPath(_scope);PublicationTransaction.Regular(lockPath);
        _lease=new(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        try{
        CreateOwnedDirectory(RawDirectory);
        CreateOwnedDirectory(ProcessingDirectory);
        foreach (var recovery in Directory.EnumerateDirectories(_root, "rollback-*"))
        {
            VerifyOwnedPath(recovery);
            if ((File.GetAttributes(recovery) & FileAttributes.ReparsePoint) != 0 || Directory.EnumerateFileSystemEntries(recovery).Any())
                throw new IOException("Previous files remain in a recovery folder. Restore them before retrying: " + recovery);
        }
        _transaction=PublicationTransaction.Load(_root,_scope);
        if(_transaction is not null&&_transaction.Items.Any(item=>_protectedPaths.Contains(item.Target)))throw new IOException("A recovery output conflicts with the current archive.");
        if(_transaction is {Committed:true}&&_transaction.Recipe!=_recipe)throw new IOException("A committed download needs its original completion reconciled before changing this job.");
        if(_transaction is not null&&!_transaction.Committed)
        {_transaction.RollbackAsync().GetAwaiter().GetResult();_transaction=null;}
        if(_transaction is {Committed:true})return;
        // Processing files are reproducible; raw partial files retain their validated resume receipts.
        ClearDirectory(ProcessingDirectory);
        }catch{_lease.Dispose();throw;}
    }
    internal static async Task<DownloadWorkspace> OpenAsync(string destination,Uri source,IReadOnlyList<string> protectedPaths,string recipe,CancellationToken ct,string? discoveryDirectory=null,PublicationTransaction.CompletionContext? context=null)
    {
        while(true)
        {
            ct.ThrowIfCancellationRequested();DownloadWorkspace? workspace=null;
            try
            {
                workspace=new(destination,source,protectedPaths,recipe);workspace._context=context??new(null);
                if(discoveryDirectory is not null)
                {
                    if(workspace._protectedPaths.Contains(RecoveryIndex.PathFor(discoveryDirectory,workspace._scope))||workspace._protectedPaths.Contains(RecoveryIndex.RegistryPath(workspace._sourceIdentity,workspace._scope)))throw new IOException("The download archive uses a filename reserved for recovery.");
                    workspace._indexFile=await RecoveryIndex.SaveAsync(discoveryDirectory,workspace._root,workspace._scope,recipe,workspace._sourceIdentity,ct).ConfigureAwait(false);
                }
                return workspace;
            }
            catch(IOException error)when((error.HResult&0xffff)is 32 or 33){workspace?.Dispose();await Task.Delay(100,ct).ConfigureAwait(false);}
            catch{workspace?.Dispose();throw;}
        }
    }
    internal async Task<PublishedMediaOutputs?> RecoveredAsync()
    {
        if(_transaction is not {Committed:true})return null;
        using var publication = await LockPublishedOutputsAsync(_transaction).ConfigureAwait(false);
        await _transaction.ValidateCommittedAsync().ConfigureAwait(false);await _transaction.EnsureInventoryAsync().ConfigureAwait(false);return _transaction.Outputs;
    }
    internal void SetInventoryIntent(StoredOutputInventory.Intent intent)
    {
        StoredOutputInventory.ValidateIntent(intent,_root,_scope,[],_context.Archive?.Path);
        if(_protectedPaths.Contains(intent.File))throw new IOException("The recording inventory conflicts with a protected file.");
        _context=_context with {Inventory=intent};
    }
    internal async Task<bool> MigrateInventoryAsync(StoredOutputInventory.Intent intent)
    {
        if(_transaction is not {Committed:true})return false;
        if(_protectedPaths.Contains(intent.File))throw new IOException("The recording inventory conflicts with a protected file.");
        using var publication = await LockPublishedOutputsAsync(_transaction).ConfigureAwait(false);
        return await _transaction.MigrateInventoryAsync(intent).ConfigureAwait(false);
    }

    public string RawPath(string destination) => Path.Combine(RawDirectory, Path.GetFileName(destination));
    public void CompleteWithoutPublishing() => _published = true;

    public string PrepareProcessing(string rawMedia)
    {
        VerifyOwnedPath(rawMedia);
        var target = Path.Combine(ProcessingDirectory, Path.GetFileName(rawMedia));
        File.Copy(rawMedia, target, overwrite: true);
        return target;
    }

    public async Task<PublishedMediaOutputs> PublishAsync(IReadOnlyList<StagedMediaOutput> groups, bool noOverwrites, CancellationToken ct, Func<IReadOnlyList<string>, Task>? beforeCommit = null)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var mediaTimes = new Dictionary<string, DateTime>(comparer);
        var targets = new HashSet<string>(comparer);
        var protectedMedia = new HashSet<string>(comparer);
        var sources = new List<(string Source, string Target)>();
        var allMedia = new List<string>(); var newMedia = new List<string>(); var committedSidecars = new List<string>();
        foreach (var group in groups)
        {
            var target = Path.GetFullPath(group.Target);
            if (group.ModifiedUtc is { } modified) mediaTimes[target] = modified;
            if (_protectedPaths.Contains(target)) throw new IOException("An output filename conflicts with the download archive or its lock file.");
            if (!targets.Add(target)) throw new IOException("Two media outputs resolve to the same file.");
            if (Directory.Exists(target)) throw new IOException("An output file conflicts with an existing folder.");
            allMedia.Add(target);
            if (noOverwrites || group.NoOverwrite) protectedMedia.Add(target);
            if (protectedMedia.Contains(target) && File.Exists(target)) continue;
            newMedia.Add(target);
            var directory = Path.GetDirectoryName(target)!;
            var inputStem = Path.GetFileNameWithoutExtension(group.Source); var outputStem = Path.GetFileNameWithoutExtension(target);
            foreach (var source in group.Sidecars)
            {
                var name = Path.GetFileName(source);
                if (!name.StartsWith(inputStem + ".", StringComparison.Ordinal)) throw new IOException("A generated sidecar does not match its media filename.");
                var sidecar = Path.Combine(directory, outputStem + name[inputStem.Length..]);
                if (_protectedPaths.Contains(sidecar)) throw new IOException("A generated sidecar conflicts with the download archive or its lock file.");
                if (!targets.Add(sidecar)) throw new IOException("Two staged outputs resolve to the same file.");
                if (Directory.Exists(sidecar)) throw new IOException("A sidecar conflicts with an existing folder.");
                sources.Add((source, sidecar));
                committedSidecars.Add(sidecar);
            }
            sources.Add((group.Source, target));
        }
        foreach (var (source, _) in sources)
        {
            VerifyOwnedPath(source);
            if (!File.Exists(source) || (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A staged output is missing or is not a regular file.");
        }
        if(_context.Inventory is { } intent)StoredOutputInventory.ValidateIntent(intent,_root,_scope,allMedia.Concat(committedSidecars),_context.Archive?.Path);
        if (sources.Count == 0) { if(_context.Inventory is { } existing)await StoredOutputInventory.SaveAsync(existing,allMedia,ct).ConfigureAwait(false);_published = true; return new(allMedia, newMedia); }
        try
        {
            _transaction=await PublicationTransaction.PrepareAsync(_root,_scope,_recipe,_sourceIdentity,sources,allMedia,newMedia,committedSidecars,ct,_context).ConfigureAwait(false);
            var copyIndex=0;
            foreach(var item in _transaction.Items)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(item.Target)!);
                await using(var input=new FileStream(item.Source,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous))
                await using(var output=new FileStream(item.Temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous))
                {
                    var buffer=new byte[65536];int count;var first=true;
                    while((count=await input.ReadAsync(buffer,ct).ConfigureAwait(false))>0)
                    {
                        // Split the first block so even a small output has an interrupted write position.
                        if(first){var half=Math.Max(1,count/2);await output.WriteAsync(buffer.AsMemory(0,half),ct).ConfigureAwait(false);await output.FlushAsync(ct).ConfigureAwait(false);await PublicationFaults.AtAsync("output-writing:"+copyIndex).ConfigureAwait(false);await output.WriteAsync(buffer.AsMemory(half,count-half),ct).ConfigureAwait(false);first=false;}
                        else await output.WriteAsync(buffer.AsMemory(0,count),ct).ConfigureAwait(false);
                    }
                    await output.FlushAsync(ct).ConfigureAwait(false);output.Flush(true);await PublicationFaults.AtAsync("output-flushed:"+copyIndex++).ConfigureAwait(false);
                }
                if(!await PublicationTransaction.MatchesAsync(item.Temporary,item.After).ConfigureAwait(false))throw new IOException("A staged output changed before publication.");
                if(mediaTimes.TryGetValue(item.Target,out var modified))NativeFileTime.TrySet(item.Temporary,modified);
            }
            await PublicationFaults.AtAsync("copied").ConfigureAwait(false);ct.ThrowIfCancellationRequested();
            var index=0;
            foreach(var item in _transaction.Items)
            {
                if(item.Before is not null)
                {
                    if(protectedMedia.Contains(item.Target)||!await PublicationTransaction.MatchesAsync(item.Target,item.Before).ConfigureAwait(false))throw new IOException("A previous output changed before installation.");
                    File.Move(item.Target,item.Backup);
                }
                else if(File.Exists(item.Target))throw new IOException("An output appeared while publication was prepared.");
                await PublicationFaults.AtAsync("backed-up:"+index++).ConfigureAwait(false);
            }
            index=0;
            foreach(var item in _transaction.Items){File.Move(item.Temporary,item.Target);await PublicationFaults.AtAsync("installed:"+index++).ConfigureAwait(false);}
            if (beforeCommit is not null && newMedia.Contains(allMedia[0], comparer)) await beforeCommit(_transaction.Items.Select(item => item.Target).ToArray()).ConfigureAwait(false);
            await _transaction.CommitAsync().ConfigureAwait(false);
            return new(allMedia,newMedia){CommittedSidecars=committedSidecars};
        }
        catch(Exception failure)
        {
            if(_transaction is null)_transaction=PublicationTransaction.Load(_root,_scope);
            if(_transaction is not null)
            {
                try{await _transaction.RollbackAsync().ConfigureAwait(false);_transaction=null;}
                catch(Exception recovery)when(recovery is IOException or UnauthorizedAccessException){_recoveryFailed=true;throw new AggregateException("Publication failed; previous files remain in the owned recovery transaction.",failure,recovery);}
            }
            throw;
        }
    }
    public void Dispose()
    {
        try{
        if (_recoveryFailed||_transaction is not null) return;
        try
        {
            if (_published) DeleteOwnedDirectory(_root);
            else
            {
                ClearDirectory(ProcessingDirectory);
                // Retain source files and partial receipts for retry, but discard an unused empty workspace.
                if (!Directory.EnumerateFileSystemEntries(RawDirectory).Any() &&
                    !Directory.EnumerateDirectories(_root).Any(path => path != RawDirectory && path != ProcessingDirectory && Directory.EnumerateFileSystemEntries(path).Any()))
                    DeleteOwnedDirectory(_root);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { /* Cleanup may be retried later; a completed output does not become a failed download. */ }
        }finally{_lease.Dispose();if(!Directory.Exists(_root))RecoveryIndex.Delete(new(_root,_scope,_indexFile));}
    }
    private static string LockPath(string scope)
    {
        var directory=Path.Combine(Path.GetTempPath(),"YoutubeDlNativeLocks");
        if(Directory.Exists(directory)&&(File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("The native workspace lock directory is linked.");
        Directory.CreateDirectory(directory);return Path.Combine(directory,scope+".lock");
    }
    private static async Task<T?> WithCompletionAsync<T>(Completion completion,Func<PublicationTransaction,Task<T>> action,bool requireCommitted=true,CancellationToken ct=default)
    {
        var root=Path.GetFullPath(completion.Root);
        if(!System.Text.RegularExpressions.Regex.IsMatch(completion.Scope,@"\A[A-F0-9]{64}\z")||Path.GetFileName(root)!=".native-download-"+completion.Scope[..24])throw new IOException("The completion workspace identity changed.");
        PublicationTransaction.PlainPath(Path.Combine(root,"publication.json"));
        var lockPath=LockPath(completion.Scope);PublicationTransaction.Regular(lockPath);
        FileStream? lease=null;
        while(lease is null){ct.ThrowIfCancellationRequested();try{lease=new(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException error)when((error.HResult&0xffff)is 32 or 33){await Task.Delay(100,ct).ConfigureAwait(false);}}
        using(lease)
        {
            if(!Directory.Exists(root)){RecoveryIndex.Delete(completion);return default;}
            if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("The completion workspace is linked.");
            var transaction=PublicationTransaction.Load(root,completion.Scope);
            if(transaction is null)
            {
                if(Directory.EnumerateFileSystemEntries(root).Any()){if(!requireCommitted)return default;throw new InvalidDataException("The completion receipt is missing.");}
                Directory.Delete(root);RecoveryIndex.Delete(completion);return default;
            }
            if(!transaction.Committed){if(!requireCommitted)return default;throw new InvalidDataException("The output set has not committed.");}
            // Publishers briefly move existing targets to backups. Completion
            // must use the same directory locks before validating those targets.
            // Keep the normal lock order: workspace, then sorted publication directories.
            using var publication = await LockPublishedOutputsAsync(transaction, ct).ConfigureAwait(false);
            return await action(transaction).ConfigureAwait(false);
        }
    }
    private static Task<IDisposable> LockPublishedOutputsAsync(PublicationTransaction transaction, CancellationToken ct = default) =>
        DestinationLock.AcquirePublicationsAsync(transaction.Outputs.AllPaths.Concat(transaction.Outputs.CommittedSidecars)
            .Select(path => Path.GetDirectoryName(path)!), ct);
    internal static async Task FinishAsync(Completion completion,Action<string>? notify,bool archiveOnly=false)
    {
        if(archiveOnly){await WithCompletionAsync(completion,async transaction=>{await transaction.ArchivedAsync().ConfigureAwait(false);return true;}).ConfigureAwait(false);return;}
        var paths=await WithCompletionAsync(completion,transaction=>Task.FromResult(transaction.Outputs.NewPaths.ToArray())).ConfigureAwait(false)??[];
        for(var index=0;index<paths.Length;index++)
        {
            var path=paths[index];var claimed=await WithCompletionAsync(completion,transaction=>transaction.ClaimAsync(path)).ConfigureAwait(false);
            if(claimed){notify?.Invoke(path);await PublicationFaults.AtAsync("notified:"+index).ConfigureAwait(false);}
        }
        await WithCompletionAsync(completion,async transaction=>
        {
            if(transaction.Archived&&transaction.AllClaimed&&await transaction.EnsureInventoryAsync().ConfigureAwait(false)){await transaction.DeleteSiblingsAsync().ConfigureAwait(false);await DeleteCompletedTreeAsync(completion.Root).ConfigureAwait(false);RecoveryIndex.Delete(completion);}
            return true;
        }).ConfigureAwait(false);
    }
    internal static async Task FinalizeRecordedAsync(NativeDownloadRequest request)
    {
        var recipe=Recipe(request);var source=AdaptiveFragmentCheckpoint.Hash(request.Url.AbsoluteUri);var indexed=new HashSet<string>(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
        foreach(var entry in RecoveryIndex.Find(request.OutputDirectory,source))
        {
            if(entry.Source!=source)continue;
            indexed.Add(entry.Completion.Root);
            var completed=await WithCompletionAsync(entry.Completion,async transaction=>
            {
                if(transaction.Recipe!=entry.Recipe||transaction.SourceIdentity!=source)throw new InvalidDataException("The pending publication disagrees with its recovery locator.");
                await transaction.ValidateCommittedAsync().ConfigureAwait(false);return true;
            },requireCommitted:false).ConfigureAwait(false);
            if(completed){await FinishAsync(entry.Completion,null,archiveOnly:true).ConfigureAwait(false);await FinishAsync(entry.Completion,null).ConfigureAwait(false);}
        }
        foreach(var root in RecoveryIndex.DiscoveryDirectories(request).SelectMany(RecoveryIndex.LegacyRoots).Distinct(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal).ToArray())
        {
            if(indexed.Contains(root))continue;
            if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("A pending output workspace is linked.");
            var file=Path.Combine(root,"publication.json");if(!File.Exists(file))continue;PublicationTransaction.Regular(file);
            if(new FileInfo(file).Length>4*1024*1024)throw new IOException("A pending publication receipt exceeds its limit.");
            using var document=System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));var scope=document.RootElement.GetProperty("Scope").GetString()!;
            var transaction=PublicationTransaction.Load(root,scope)!;
            if(transaction.Committed&&transaction.SourceIdentity==source)
            {await FinishAsync(new(root,scope),null,archiveOnly:true).ConfigureAwait(false);await FinishAsync(new(root,scope),null).ConfigureAwait(false);}
        }
    }
    internal static async Task ReconcileChangedAsync(NativeDownloadRequest request,CancellationToken ct)
    {
        if(request.Simulate)return;
        var source=AdaptiveFragmentCheckpoint.Hash(request.Url.AbsoluteUri);var recipe=Recipe(request);
        var seen=new HashSet<string>(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
        foreach(var entry in RecoveryIndex.Find(request.OutputDirectory,source).ToArray())
        {
            ct.ThrowIfCancellationRequested();seen.Add(entry.Completion.Root);
            if(entry.Recipe!=recipe)await ReconcileOneAsync(entry.Completion,source,recipe,ct).ConfigureAwait(false);
        }
        foreach(var root in RecoveryIndex.DiscoveryDirectories(request).SelectMany(RecoveryIndex.LegacyRoots).Distinct(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal).ToArray())
        {
            ct.ThrowIfCancellationRequested();if(seen.Contains(root))continue;var file=Path.Combine(root,"publication.json");if(!File.Exists(file))continue;
            PublicationTransaction.Regular(file);if(new FileInfo(file).Length>4*1024*1024)throw new InvalidDataException("The pending receipt exceeds its limit.");
            using var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);using var document=System.Text.Json.JsonDocument.Parse(input);
            var scope=document.RootElement.GetProperty("Scope").GetString()!;input.Close();await ReconcileOneAsync(new(root,scope),source,recipe,ct).ConfigureAwait(false);
        }
    }
    private static async Task ReconcileOneAsync(Completion completion,string source,string recipe,CancellationToken ct)
    {
        var snapshot=PublicationTransaction.Load(completion.Root,completion.Scope);
        if(snapshot is null||!snapshot.Committed||snapshot.SourceIdentity!=source||snapshot.Recipe==recipe)return;
        if(snapshot.Context is null&&!snapshot.Archived)throw new IOException("The previous download needs its original job settings to finish safely.");
        // Keep the same lock order as ordinary downloads: archive, then workspace.
        var binding=snapshot.Context?.Archive;
        using var archive=!snapshot.Archived&&binding is not null?await DownloadArchive.OpenAsync(binding.Path,binding.Identity,ct).ConfigureAwait(false):null;
        await WithCompletionAsync(completion,async transaction=>
        {
            if(transaction.SourceIdentity!=source||transaction.Recipe!=snapshot.Recipe||transaction.Context!=snapshot.Context)throw new IOException("The pending job changed during completion recovery.");
            await transaction.ValidateCommittedAsync().ConfigureAwait(false);
            if(!transaction.Archived)
            {
                if(archive is not null)await archive.RecordAsync(prepareTemporary:transaction.RegisterArchiveTemporaryAsync).ConfigureAwait(false);
                await transaction.ArchivedAsync().ConfigureAwait(false);
            }
            // A changed job retires prior delivery intents; only its new outputs receive callbacks.
            await transaction.NotifyAsync(null).ConfigureAwait(false);await PublicationFaults.AtAsync("retired-claimed").ConfigureAwait(false);
            if(!await transaction.EnsureInventoryAsync().ConfigureAwait(false))throw new IOException("The completed recording inventory could not be recovered; its receipt was retained.");
            await transaction.DeleteSiblingsAsync().ConfigureAwait(false);await DeleteCompletedTreeAsync(completion.Root).ConfigureAwait(false);RecoveryIndex.Delete(completion);
            await PublicationFaults.AtAsync("retired-cleaned").ConfigureAwait(false);return true;
        },ct:ct).ConfigureAwait(false);
    }
    internal static async Task RegisterArchiveTemporaryAsync(Completion completion,string path)
    {await WithCompletionAsync(completion,async transaction=>{await transaction.RegisterArchiveTemporaryAsync(path).ConfigureAwait(false);return true;}).ConfigureAwait(false);}
    private static async Task DeleteCompletedTreeAsync(string root)
    {
        var entries=new List<(string Path,bool Directory)>();var folders=new Stack<string>();folders.Push(root);
        while(folders.Count>0)
        {
            foreach(var path in Directory.EnumerateFileSystemEntries(folders.Pop()))
            {
                var attributes=File.GetAttributes(path);if((attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Completion cleanup cannot follow a linked file.");
                var folder=(attributes&FileAttributes.Directory)!=0;entries.Add((path,folder));if(entries.Count>500000)throw new IOException("Completion cleanup exceeds its entry limit.");if(folder)folders.Push(path);
            }
        }
        // Keep the committed receipt until all other entries are gone, so every partial cleanup is replayable.
        var receipt=Path.Combine(root,"publication.json");var index=0;
        foreach(var entry in entries.OrderByDescending(e=>e.Path.Length))
        {
            if(entry.Path==receipt)continue;
            if(entry.Directory)Directory.Delete(entry.Path);else File.Delete(entry.Path);
            await PublicationFaults.AtAsync("cleanup-entry:"+index++).ConfigureAwait(false);
        }
        await PublicationFaults.AtAsync("cleanup-receipt").ConfigureAwait(false);File.Delete(receipt);
        await PublicationFaults.AtAsync("cleanup-empty").ConfigureAwait(false);Directory.Delete(root);
        await PublicationFaults.AtAsync("cleanup-root").ConfigureAwait(false);
    }

    private void VerifyOwnedPath(string path)
    {
        var full = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, comparison) && !full.Equals(_root, comparison))
            throw new IOException("The staging path is outside this download's workspace.");
    }
    private void CreateOwnedDirectory(string path)
    {
        VerifyOwnedPath(path);
        if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The download workspace cannot use a filesystem link.");
        Directory.CreateDirectory(path);
    }
    private void ClearDirectory(string path)
    {
        VerifyOwnedPath(path);
        if (!Directory.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked staging folders cannot be removed.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            VerifyOwnedPath(entry);
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked staging files cannot be removed.");
            if ((attributes & FileAttributes.Directory) != 0) DeleteOwnedDirectory(entry);
            else File.Delete(entry);
        }
    }
    private void DeleteOwnedDirectory(string path)
    {
        ClearDirectory(path);
        Directory.Delete(path);
    }
}
