using System.Diagnostics;
using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Media;
using YoutubeDl.Engine.Providers;
internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> PublicationSessionMediaChecks(string ffmpeg)=>
        new[]{"processing","processor-started","prepared","copied","backed-up:0","backed-up:1","installed:0","installed:1","installed:2","committed","archived","notified:0","edited","recipe"}.Select(mode=>("Publication session media "+mode,(Func<Task>)(()=>PublicationMedia(ffmpeg,mode))))
        .Concat([("Publication session owned processor dies with parent",(Func<Task>)ProcessorParentDeath)]);
    private sealed class PublicationMediaProvider:IMediaProvider
    {
        public MediaProviderInfo Info=>new("publication-media","Publication media fixture",0);public bool CanHandle(Uri uri)=>uri.Host=="publication-media.example";
        public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)=>new("publication-media","movie");
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request,YoutubeDl.Engine.Http.MediaHttp http,CancellationToken ct)=>Task.FromResult(new PageMetadata{Id="movie",Extractor="publication-media",Title="Crash-safe movie",DurationSeconds=3,
            Media=[new(){Id="main",Kind=MediaKind.Progressive,Url=new("https://cdn.example/movie.mp4"),Extension="mp4",HasAudio=true,HasVideo=true,VideoCodec="h264",AudioCodec="aac"}],
            Subtitles=[new(){Url=new("https://cdn.example/captions.vtt"),Language="en",Extension="vtt",Content="WEBVTT\n\n00:00:00.000 --> 00:00:03.000\nRecovered publication cue.\n"}]});
    }
    private static NativeDownloadRequest PublicationRequest(string directory,string ffmpeg,bool archive)=>new(){Url=new("https://publication-media.example/movie"),OutputDirectory=directory,OutputTemplate="recording.%(ext)s",FfmpegPath=ffmpeg,AddMetadata=true,WriteSubs=true,EmbedSubs=true,SubLanguages="en",KeepOriginal=true,DownloadArchiveFile=archive?Path.Combine(directory,"archive.txt"):"",Retries=0};
    private static NativeEngine PublicationEngine(string directory,Action? transfer=null)=>new([new PublicationMediaProvider()],_=>AdaptiveAccountChecks.Http(async(message,ct)=>{
        transfer?.Invoke();var bytes=await File.ReadAllBytesAsync(Path.Combine(directory,"source.mp4"),ct);var range=message.Headers.Range?.Ranges.SingleOrDefault();var response=new HttpResponseMessage(range is null?HttpStatusCode.OK:HttpStatusCode.PartialContent){Content=new ByteArrayContent(range is null?bytes:bytes[(int)range.From!.Value..((int)range.To!.Value+1)])};response.Headers.ETag=new("\"generated-source\"");
        if(range is not null)response.Content.Headers.ContentRange=new(range.From!.Value,range.To!.Value,bytes.Length);return response;
    }));
    internal static async Task<int> PublicationMediaWorker(string directory,string phase,string ffmpeg)
    {
        PublicationFaults.Observer=async value=>{if(value!=phase)return;await File.WriteAllTextAsync(Path.Combine(directory,"worker-ready"),Environment.ProcessId.ToString());await Task.Delay(Timeout.Infinite);};
        await PublicationEngine(directory).DownloadAsync(PublicationRequest(directory,ffmpeg,phase!="notified:0"),null,null,default,path=>File.AppendAllText(Path.Combine(directory,"callbacks.txt"),path+"\n"));return 1;
    }
    private static async Task PublicationMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var source=Path.Combine(files.Directory,"source.mp4");
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=12","-f","lavfi","-i","sine=frequency=720:sample_rate=48000","-t","3","-c:v","libx264","-c:a","aac","-movflags","+faststart",source]);
        var output=Path.Combine(files.Directory,"recording.mp4");var original=Path.Combine(files.Directory,"recording.original.mp4");var sidecar=Path.Combine(files.Directory,"recording.en.vtt");var archive=Path.Combine(files.Directory,"archive.txt");
        await File.WriteAllTextAsync(output,"old media");await File.WriteAllTextAsync(original,"old original");await File.WriteAllTextAsync(sidecar,"old captions");await File.WriteAllTextAsync(archive,"prior record\n");
        var phase=mode is "edited" or "recipe"?"committed":mode;var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
        foreach(var argument in new[]{typeof(FfmpegChecks).Assembly.Location,"--publication-media-worker",files.Directory,phase,ffmpeg})start.ArgumentList.Add(argument);
        using(var process=Process.Start(start)!)
        {
            var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();var ready=false;
            try{for(var attempt=0;attempt<600&&!process.HasExited;attempt++){try{ready=File.Exists(Path.Combine(files.Directory,"worker-ready"))&&int.Parse(await File.ReadAllTextAsync(Path.Combine(files.Directory,"worker-ready")))==process.Id;}catch(IOException){}if(ready)break;await Task.Delay(50);}
                AdaptiveRecoveryChecks.Assert(ready,"Media worker failed before "+phase+": "+(process.HasExited?await stderr:"timeout"));process.Kill(entireProcessTree:true);await process.WaitForExitAsync();}
            finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}await stdout;await stderr;}
        }
        if(mode=="edited")await File.WriteAllTextAsync(output,"user-edited output");
        var archivedAtStop=File.ReadAllLines(archive).Contains("publication-media movie");
        var request=PublicationRequest(files.Directory,ffmpeg,mode!="notified:0") with {WriteDescription=mode=="recipe"};var calls=0;var callbacks=0;
        try
        {
            var result=await PublicationEngine(files.Directory,()=>calls++).DownloadAsync(request,null,null,default,_=>callbacks++);
            AdaptiveRecoveryChecks.Assert(mode!="edited"&&result.Succeeded);
            if(mode is "committed" or "archived" or "notified:0" or "recipe")AdaptiveRecoveryChecks.Assert(calls==0,"Committed media was retransferred.");
            foreach(var path in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",path,"-map","0:v?","-map","0:a?","-f","null","-"],true);
            foreach(var role in new[]{"video","audio"}){var actual=await RecoveryPackets(ffmpeg,output,role);var expected=await RecoveryPackets(ffmpeg,source,role);AdaptiveRecoveryChecks.Assert(actual.AsEnumerable().SequenceEqual(expected));}
            AdaptiveRecoveryChecks.Assert((await File.ReadAllTextAsync(sidecar)).Contains("Recovered publication cue."));
            var embedded=await Run(ffmpeg,["-v","error","-i",output,"-map","0:s:0","-f","webvtt","-"]);AdaptiveRecoveryChecks.Assert(embedded.Contains("Recovered publication cue."));
            if(archivedAtStop||mode=="recipe")AdaptiveRecoveryChecks.Assert(callbacks==0);
            else if(mode=="notified:0")AdaptiveRecoveryChecks.Assert(callbacks==1&&File.ReadAllLines(Path.Combine(files.Directory,"callbacks.txt")).Length==1);
            else AdaptiveRecoveryChecks.Assert(callbacks==2);
            if(mode!="notified:0")AdaptiveRecoveryChecks.Assert(File.ReadAllLines(archive).Count(line=>line=="publication-media movie")==1);
            AdaptiveRecoveryChecks.Assert(Directory.GetDirectories(files.Directory,".native-download-*").Length==0&&Directory.GetFiles(files.Directory,".native-save-*").Length==0&&Directory.GetFiles(files.Directory,".native-backup-*").Length==0);
        }
        catch(IOException)when(mode=="edited"){AdaptiveRecoveryChecks.Assert(await File.ReadAllTextAsync(output)=="user-edited output"&&callbacks==0&&await File.ReadAllTextAsync(archive)=="prior record\n");}
    }
    internal static async Task<int> LifetimeParentWorker(string directory)
    {
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true};foreach(var argument in new[]{typeof(FfmpegChecks).Assembly.Location,"--lifetime-helper",directory})start.ArgumentList.Add(argument);
        using var process=Process.Start(start)!;using var lifetime=ProcessLifetime.Attach(process);
        await LifetimeMarker(Path.Combine(directory,"parent-ready"));await Task.Delay(Timeout.Infinite);return 1;
    }
    private static async Task LifetimeMarker(string path){var pending=path+".tmp";await File.WriteAllTextAsync(pending,Environment.ProcessId.ToString());File.Move(pending,path);}
    internal static async Task<int> LifetimeHelper(string directory){await LifetimeMarker(Path.Combine(directory,"helper-ready"));await Task.Delay(Timeout.Infinite);return 1;}
    private static async Task ProcessorParentDeath()
    {
        if(!OperatingSystem.IsWindows())return;using var files=new Files();var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true};foreach(var argument in new[]{typeof(FfmpegChecks).Assembly.Location,"--lifetime-parent",files.Directory})start.ArgumentList.Add(argument);
        using var parent=Process.Start(start)!;Process? helper=null;
        try
        {
            for(var attempt=0;attempt<200&&!parent.HasExited;attempt++){if(File.Exists(Path.Combine(files.Directory,"parent-ready"))&&File.Exists(Path.Combine(files.Directory,"helper-ready")))break;await Task.Delay(50);}
            AdaptiveRecoveryChecks.Assert(File.ReadAllText(Path.Combine(files.Directory,"parent-ready"))==parent.Id.ToString());
            helper=Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(files.Directory,"helper-ready"))));
            parent.Kill();await parent.WaitForExitAsync();await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));AdaptiveRecoveryChecks.Assert(helper.HasExited,"Owned processor survived a parent-only stop.");
        }
        finally{if(!parent.HasExited){parent.Kill(true);await parent.WaitForExitAsync();}if(helper is not null){if(!helper.HasExited){helper.Kill(true);await helper.WaitForExitAsync();}helper.Dispose();}}
    }
}
