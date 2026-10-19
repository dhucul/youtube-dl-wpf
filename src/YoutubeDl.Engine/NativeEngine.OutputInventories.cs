using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;

namespace YoutubeDl.Engine;

public sealed partial class NativeEngine
{
    private static string InventoryScope(NativeDownloadRequest request,string anchor)=>AdaptiveFragmentCheckpoint.Hash(Path.GetFullPath(anchor)+"\n"+request.Url.AbsoluteUri);
    private static string InventoryFile(string anchor,string scope)=>StoredOutputInventory.FileFor(anchor,scope);
    private static StoredOutputInventory.Intent InventoryIntent(NativeDownloadRequest request,MediaDownloadPlan plan,string anchor,bool processed)=>
        new(InventoryFile(anchor,InventoryScope(request,anchor)),InventoryScope(request,anchor),DownloadWorkspace.Recipe(request with {KeepOriginal=false}),InventoryFormat(plan),request.KeepOriginal,processed);
    private static string InventoryFormat(MediaDownloadPlan plan)=>AdaptiveFragmentCheckpoint.Hash(JsonSerializer.Serialize(new[]{Format(plan.Primary),plan.Audio is null?null:Format(plan.Audio)}));
    private static object Format(MediaCandidate value)=>new{value.Id,value.Kind,value.Extension,value.Source,value.VideoCodec,value.AudioCodec,value.Height,value.FramesPerSecond,value.HasAudio,value.HasVideo};
    private static IEnumerable<string> InventoryLocations(NativeDownloadRequest request,string anchor,string scope)
    {
        yield return InventoryFile(anchor,scope);
        var roots=RecoveryIndex.DiscoveryDirectories(request).Distinct(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal).ToArray();
        foreach(var root in roots)
        {
            yield return InventoryFile(Path.Combine(root,"anchor"),scope);
            var parent=Path.GetDirectoryName(anchor);
            for(var depth=0;parent is not null&&depth<16;depth++,parent=Path.GetDirectoryName(parent))
            {
                if(SamePath(parent,root))break;
                if(!parent.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))break;
                yield return InventoryFile(Path.Combine(parent,"anchor"),scope);
            }
        }
    }
    private static async Task<NativeDownloadResult?> ExistingRecordedOutputsAsync(NativeDownloadRequest request,MediaDownloadPlan plan,string anchor,IReadOnlyList<string> protectedPaths,Action<string>? log,CancellationToken ct)
    {
        if(plan.Primary.Kind is not (MediaKind.Hls or MediaKind.Dash)||!request.NoOverwrites&&(!request.NoPostOverwrites||request.KeepOriginal))return null;
        var scope=InventoryScope(request,anchor);var file=InventoryFile(anchor,scope);PublicationTransaction.PlainPath(file);
        if(protectedPaths.Any(path=>SamePath(path,file)))return null;
        StoredOutputInventory.Entry? value=null;string? location=null;
        try
        {
            // A present current record is authoritative, even when corrupt or incomplete.
            // Historical snapshots are retained evidence, never an alternative playback inventory.
            if(File.Exists(file)) { value=await StoredOutputInventory.ReadAsync(file,ct).ConfigureAwait(false);location=file; }
            else foreach(var candidate in InventoryLocations(request,anchor,scope).Distinct(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal))
            {
                PublicationTransaction.PlainPath(candidate);if(protectedPaths.Any(path=>SamePath(path,candidate)))continue;
                var legacy=await StoredOutputInventory.ReadAsync(candidate,ct).ConfigureAwait(false);if(legacy is null)continue;
                if(!StoredOutputInventory.Valid(legacy)||legacy.Scope!=scope)return null;
                if(value is not null&&value.Digest!=legacy.Digest)return null;
                value=legacy;location=candidate;
            }
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){ct.ThrowIfCancellationRequested();return null;}
        if(value is null||!StoredOutputInventory.Valid(value)||value.Scope!=scope||value.Recipe!=DownloadWorkspace.Recipe(request with {KeepOriginal=false})||value.Format!=InventoryFormat(plan)||request.KeepOriginal&&!value.Retained||!request.NoOverwrites&&!value.Processed)return null;
        var result=await ExistingSetAsync(request.KeepOriginal?value.Paths:[value.Paths[0]],protectedPaths,log,ct).ConfigureAwait(false);
        if(result is not null&&(value.Schema==1||location!=file)&&await ExistingSetAsync(value.Paths,protectedPaths,null,ct).ConfigureAwait(false) is not null)
        {
            var intent=new StoredOutputInventory.Intent(file,value.Scope,value.Recipe,value.Format,value.Retained,value.Processed);
            if(!await StoredOutputInventory.SaveAsync(intent,value.Paths,ct).ConfigureAwait(false))log?.Invoke("[native] Legacy recording inventory could not be upgraded; its existing record was retained.");
        }
        return result;
    }
}
