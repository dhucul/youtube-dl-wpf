using System.Diagnostics;
using System.Net;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Providers;
internal static class PublicationRecoveryChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
        new[]{"processing","prepared","copied","backed-up:0","backed-up:1","installed:0","installed:1","committed","archived","notified:0","edited","receipt","backup","folder","recipe"}.Select(mode=>("Publication session crash "+mode,(Func<Task>)(()=>Crash(mode))));
    internal static NativeDownloadRequest Request(string directory,bool archive=true)=>new(){Url=new("https://publication.example/movie"),OutputDirectory=directory,OutputTemplate="movie.%(ext)s",WriteSubs=true,SubLanguages="en",DownloadArchiveFile=archive?Path.Combine(directory,"archive.txt"):"",Retries=0};
    internal static NativeEngine Engine(Action? transfer=null)=>new([new Provider()],_=>AdaptiveAccountChecks.Http((_,_)=>{transfer?.Invoke();var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent("complete new media"u8.ToArray())};response.Headers.ETag=new("\"fixture-v1\"");return Task.FromResult(response);}));
    private sealed class Provider:IMediaProvider
    {
        public MediaProviderInfo Info=>new("publication-fixture","Publication fixture",0);public bool CanHandle(Uri uri)=>uri.Host=="publication.example";
        public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)=>new("publication-fixture","movie");
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request,YoutubeDl.Engine.Http.MediaHttp http,CancellationToken ct)=>Task.FromResult(new PageMetadata
        {Id="movie",Extractor="publication-fixture",Title="Movie",Media=[new(){Id="file",Url=new("https://cdn.example/movie.mp4"),Kind=MediaKind.Progressive,Extension="mp4",HasAudio=true,HasVideo=true}],
            Subtitles=[new(){Url=new("https://cdn.example/en.vtt"),Language="en",Extension="vtt",Content="WEBVTT\n\n00:00:00.000 --> 00:00:01.000\nSaved cue.\n"}]});
    }
    internal static async Task<int> Worker(string directory,string phase)
    {
        PublicationFaults.Observer=async value=>{if(value!=phase)return;await File.WriteAllTextAsync(Path.Combine(directory,"worker-ready"),Environment.ProcessId.ToString());await Task.Delay(Timeout.Infinite);};
        await Engine().DownloadAsync(Request(directory,phase!="notified:0"),null,null,default,path=>File.AppendAllText(Path.Combine(directory,"callbacks.txt"),path+"\n"));
        return 1;
    }
    private static async Task Crash(string mode)
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var media=Path.Combine(directory,"movie.mp4");var sidecar=Path.Combine(directory,"movie.en.vtt");await File.WriteAllTextAsync(media,"old media");await File.WriteAllTextAsync(sidecar,"old captions");await File.WriteAllTextAsync(Path.Combine(directory,"archive.txt"),"prior record\n");
            var phase=mode is "edited" or "receipt" or "recipe"?"committed":mode is "backup" or "folder"?"installed:0":mode;
            var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
            foreach(var arg in new[]{typeof(PublicationRecoveryChecks).Assembly.Location,"--publication-worker",directory,phase})start.ArgumentList.Add(arg);
            using(var process=Process.Start(start)!)
            {
                var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();var ready=false;
                try
                {
                    for(var attempt=0;attempt<400&&!process.HasExited;attempt++){try{ready=File.Exists(Path.Combine(directory,"worker-ready"))&&int.Parse(await File.ReadAllTextAsync(Path.Combine(directory,"worker-ready")))==process.Id;}catch(IOException){}if(ready)break;await Task.Delay(50);}
                    AdaptiveRecoveryChecks.Assert(ready,"Publication worker did not reach "+phase+": "+(process.HasExited?await errors:"timeout"));process.Kill(entireProcessTree:true);await process.WaitForExitAsync();
                }
                finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}await output;await errors;}
            }
            if(mode=="processing")AdaptiveRecoveryChecks.Assert(await File.ReadAllTextAsync(media)=="old media"&&await File.ReadAllTextAsync(sidecar)=="old captions");
            var receipt=Directory.GetFiles(directory,"publication.json",SearchOption.AllDirectories).SingleOrDefault();
            if(mode=="edited")await File.WriteAllTextAsync(media,"user edit");
            if(mode=="receipt"){var node=System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(receipt!))!;node["Recipe"]=new string('A',64);await File.WriteAllTextAsync(receipt!,node.ToJsonString());}
            if(mode=="backup")await File.WriteAllTextAsync(Directory.GetFiles(directory,".native-backup-*").First(),"user backup edit");
            if(mode=="folder"){File.Delete(sidecar);Directory.CreateDirectory(sidecar);}
            var calls=0;var notified=0;var request=Request(directory,mode!="notified:0") with {WriteDescription=mode=="recipe"};var failure=mode is "edited" or "receipt" or "backup" or "folder";
            var failed=false;
            try
            {
                var result=await Engine(()=>calls++).DownloadAsync(request,null,null,default,_=>notified++);failed=!result.Succeeded;
                if(!failure)
                {
                    AdaptiveRecoveryChecks.Assert(result.Succeeded&&await File.ReadAllTextAsync(media)=="complete new media"&&(await File.ReadAllTextAsync(sidecar)).Contains("Saved cue."));
                    if(mode is "committed" or "archived" or "notified:0" or "recipe")AdaptiveRecoveryChecks.Assert(calls==0,"Committed outputs were downloaded again.");
                    if(mode=="notified:0")AdaptiveRecoveryChecks.Assert(notified==0&&File.ReadAllLines(Path.Combine(directory,"callbacks.txt")).Length==1,"A claimed callback repeated.");
                    else if(mode is "archived" or "recipe")AdaptiveRecoveryChecks.Assert(notified==0);
                    else AdaptiveRecoveryChecks.Assert(notified==1);
                    if(mode!="notified:0")AdaptiveRecoveryChecks.Assert(File.ReadAllLines(Path.Combine(directory,"archive.txt")).Count(line=>line=="publication-fixture movie")==1);
                    AdaptiveRecoveryChecks.Assert(Directory.GetDirectories(directory,".native-download-*").Length==0&&Directory.GetFiles(directory,".native-backup-*").Length==0&&Directory.GetFiles(directory,".native-save-*").Length==0);
                    var before=calls;var count=notified;var repeat=await Engine(()=>calls++).DownloadAsync(request with {NoOverwrites=mode=="notified:0"},null,null,default,_=>notified++);
                    AdaptiveRecoveryChecks.Assert(repeat.Succeeded&&repeat.Skipped&&notified==count&&calls==before);
                }
            }
            catch(Exception error)when(failure&&error is IOException or InvalidDataException or System.Text.Json.JsonException){failed=true;}
            AdaptiveRecoveryChecks.Assert(failed==failure,"Publication crash result: "+mode);
            if(mode=="edited")AdaptiveRecoveryChecks.Assert(await File.ReadAllTextAsync(media)=="user edit"&&notified==0);
            if(mode=="backup")AdaptiveRecoveryChecks.Assert(Directory.GetFiles(directory,".native-backup-*").Any(path=>File.ReadAllText(path)=="user backup edit"));
            if(failure)AdaptiveRecoveryChecks.Assert(notified==0&&await File.ReadAllTextAsync(Path.Combine(directory,"archive.txt"))=="prior record\n");
        }
        finally{Directory.Delete(directory,true);}
    }
}
