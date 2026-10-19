using System.Diagnostics;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;

internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> ChangedJobMediaChecks(string ffmpeg)=>
        new[]{"commit","cleanup","directory","archive","archive-changed","fail-processing","no-overwrite","retire-claim","retire-cleanup"}
        .Select(mode=>("Changed job media "+mode,(Func<Task>)(()=>ChangedMedia(ffmpeg,mode))));
    private static NativeDownloadRequest ChangedMediaRequest(string directory,string ffmpeg,string mode,bool fresh)
    {
        var archive=mode.StartsWith("archive",StringComparison.Ordinal);var output=fresh&&mode=="directory"?Path.Combine(directory,"new"):Path.Combine(directory,"old");
        return PublicationRequest(output,ffmpeg,archive) with {WriteInfoJson=fresh,NoOverwrites=fresh&&mode=="no-overwrite",
            DownloadArchiveFile=archive?Path.Combine(directory,fresh&&mode=="archive-changed"?"new-archive.txt":"old-archive.txt"):""};
    }
    internal static async Task<int> ChangedMediaWorker(string directory,string mode,string ffmpeg,bool fresh=false)
    {
        var phase=fresh?mode=="retire-claim"?"retired-claimed":"cleanup-entry:0":mode=="cleanup"?"cleanup-sibling:0":mode.StartsWith("archive",StringComparison.Ordinal)?"archive-writing":"committed";
        PublicationFaults.Observer=async value=>{if(value!=phase)return;await File.WriteAllTextAsync(Path.Combine(directory,"ready"),Environment.ProcessId.ToString());await Task.Delay(Timeout.Infinite);};
        await PublicationEngine(directory).DownloadAsync(ChangedMediaRequest(directory,ffmpeg,mode,fresh),null,null,default);return 1;
    }
    private static async Task StopChangedMedia(string directory,string mode,string ffmpeg,bool fresh=false)
    {
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var value in new[]{typeof(FfmpegChecks).Assembly.Location,fresh?"--changed-media-retry-worker":"--changed-media-worker",directory,mode,ffmpeg})start.ArgumentList.Add(value);
        File.Delete(Path.Combine(directory,"ready"));using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();var ready=false;
        try{for(var i=0;i<700&&!process.HasExited;i++){try{ready=File.Exists(Path.Combine(directory,"ready"))&&int.Parse(await File.ReadAllTextAsync(Path.Combine(directory,"ready")))==process.Id;}catch(IOException){}if(ready)break;await Task.Delay(40);}AdaptiveRecoveryChecks.Assert(ready,"Changed media worker failed: "+(process.HasExited?await errors:"timeout"));process.Kill(true);await process.WaitForExitAsync();}
        finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}await output;await errors;}
    }
    private static async Task ChangedMedia(string ffmpeg,string mode)
    {
        using var files=new Files();Directory.CreateDirectory(Path.Combine(files.Directory,"old"));var source=Path.Combine(files.Directory,"source.mp4");
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=12","-f","lavfi","-i","sine=frequency=720:sample_rate=48000","-t","3","-c:v","libx264","-c:a","aac","-movflags","+faststart",source]);
        var oldRequest=ChangedMediaRequest(files.Directory,ffmpeg,mode,false);var oldMain=Path.Combine(oldRequest.OutputDirectory,"recording.mp4");var oldOriginal=Path.Combine(oldRequest.OutputDirectory,"recording.original.mp4");
        await File.WriteAllTextAsync(oldMain,"old media");await File.WriteAllTextAsync(oldOriginal,"old original");
        if(oldRequest.DownloadArchiveFile.Length>0)await File.WriteAllTextAsync(oldRequest.DownloadArchiveFile,"prior record\n");
        await StopChangedMedia(files.Directory,mode,ffmpeg);var beforeMain=await File.ReadAllBytesAsync(oldMain);var beforeOriginal=await File.ReadAllBytesAsync(oldOriginal);
        if(mode is "retire-claim" or "retire-cleanup")await StopChangedMedia(files.Directory,mode,ffmpeg,true);
        var request=ChangedMediaRequest(files.Directory,ffmpeg,mode,true);if(mode=="fail-processing")request=request with {FfmpegPath=Path.Combine(files.Directory,"missing-ffmpeg.exe")};
        var callbacks=0;var failed=false;
        try{var result=await PublicationEngine(files.Directory).DownloadAsync(request,null,null,default,_=>callbacks++);failed=!result.Succeeded;AdaptiveRecoveryChecks.Assert(mode=="fail-processing"||result.Succeeded);}
        catch(Exception e)when(mode=="fail-processing"&&e is IOException or InvalidOperationException){failed=true;}
        AdaptiveRecoveryChecks.Assert(failed==(mode=="fail-processing"));
        if(mode=="fail-processing")AdaptiveRecoveryChecks.Assert(beforeMain.AsEnumerable().SequenceEqual(await File.ReadAllBytesAsync(oldMain))&&beforeOriginal.AsEnumerable().SequenceEqual(await File.ReadAllBytesAsync(oldOriginal))&&callbacks==0,"Changed processing lost already committed originals.");
        else AdaptiveRecoveryChecks.Assert(callbacks==(mode=="no-overwrite"||mode=="archive"?0:2),"Changed media completion repeated old callbacks.");
        var main=Path.Combine(request.OutputDirectory,"recording.mp4");var original=Path.Combine(request.OutputDirectory,"recording.original.mp4");
        foreach(var path in new[]{oldMain,oldOriginal,main,original}.Distinct())await Run(ffmpeg,["-v","error","-xerror","-i",path,"-map","0:v?","-map","0:a?","-f","null","-"],true);
        foreach(var role in new[]{"video","audio"})
        {
            var expected=await RecoveryPackets(ffmpeg,source,role);
            foreach(var path in new[]{oldMain,oldOriginal,main,original}.Distinct())AdaptiveRecoveryChecks.Assert((await RecoveryPackets(ffmpeg,path,role)).AsEnumerable().SequenceEqual(expected),"Changed jobs altered coded source packets.");
        }
        if(oldRequest.DownloadArchiveFile.Length>0)AdaptiveRecoveryChecks.Assert(File.ReadAllLines(oldRequest.DownloadArchiveFile).Count(line=>line=="publication-media movie")==1);
        if(mode=="archive-changed")AdaptiveRecoveryChecks.Assert(File.ReadAllLines(request.DownloadArchiveFile).Count(line=>line=="publication-media movie")==1);
        if(mode!="fail-processing")AdaptiveRecoveryChecks.Assert(Directory.GetDirectories(files.Directory,".native-download-*",SearchOption.AllDirectories).Length==0&&Directory.GetFiles(files.Directory,".native-backup-*",SearchOption.AllDirectories).Length==0,"Changed media left recovery debris.");
    }
}
