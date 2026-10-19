internal static partial class FfmpegChecks
{
    internal static string? LegacyNativeAssembly {get;set;}
    internal static async Task<int> LegacyAdaptationWorker(string legacy,string ffmpeg,string directory,string mode)
    {
        var context=new System.Runtime.Loader.AssemblyLoadContext("verified-0.168.0");var root=Path.GetDirectoryName(Path.GetFullPath(legacy))!;
        context.Resolving+=(load,name)=>{var path=Path.Combine(root,name.Name+".dll");return File.Exists(path)?load.LoadFromAssemblyPath(path):null;};
        var engine=context.LoadFromAssemblyPath(Path.Combine(root,"YoutubeDl.Engine.dll"));
        WriteCleanupChecks.Check((string?)engine.GetType("YoutubeDl.Engine.NativeEngine")!.GetField("Version")!.GetRawConstantValue()=="0.168.0","Legacy worker did not load the preserved engine version.");
        var assembly=context.LoadFromAssemblyPath(Path.GetFullPath(legacy));var worker=assembly.GetType("FfmpegChecks")!.GetMethod("AdaptationWorker",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
        return await (Task<int>)worker.Invoke(null,[ffmpeg,directory,mode])!;
    }
    internal static IEnumerable<(string Name,Func<Task> Test)> SelectionMigrationMediaChecks(string ffmpeg)=>
        new[]{"best","worst","language","role","convert-forward","convert-reverse","convert-restart","convert-shared","convert-boundary","convert-kill","convert-role","convert-wrong-init"}
        .Concat(LegacyNativeAssembly is null?[]:new[]{"legacy-wvtt","legacy-stpp","legacy-future","legacy-corrupt","legacy-cancel","legacy-missing"})
        .Select(mode=>("Selection migration media "+mode,(Func<Task>)(()=>AdaptationMedia(ffmpeg,"selection-"+mode))));
}
