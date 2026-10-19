using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Clyp;

internal static partial class FfmpegChecks
{
    private static async Task ClypMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var mp3=Path.Combine(files.Directory,"source.mp3");var ogg=Path.Combine(files.Directory,"source.ogg");var duration=mode=="short"?"1.6":"2";
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","sine=frequency=660:sample_rate=44100:duration="+duration,"-c:a","libmp3lame","-b:a","128k",mp3]);
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","sine=frequency=660:sample_rate=44100:duration=2","-c:a","libvorbis",ogg]);
        var sources=new Dictionary<string,byte[]>{["mp3"]=await File.ReadAllBytesAsync(mp3),["ogg"]=await File.ReadAllBytesAsync(ogg)};
        var calls=new List<(HttpMethod,Uri)>();var media=0;var callbacks=0;using var stop=new CancellationTokenSource();
        var engine=new NativeEngine([new ClypProvider()],_=>ClypChecks.Http(calls,sources,mutate:(kind,node)=>{if(kind=="api"&&mode=="mp3")node["Status"]="DownloadDisabled";},body:request=>
        {media++;if(mode=="cancel")stop.Cancel();return new(HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(sources[request.RequestUri!.AbsolutePath[^3..]])};}));
        var request=ClypChecks.Request(files.Directory)with{FfmpegPath=ffmpeg,KeepOriginal=true,AddMetadata=true,AudioOnly=mode=="convert",AudioFormat="m4a",DownloadArchiveFile=Path.Combine(files.Directory,"archive.txt"),FormatId=mode=="ogg"?"ogg":null};
        if(mode=="widget")request=request with{Url=new("https://clyp.it/"+ClypChecks.Id+"/widget?autoplay=false")};
        if(mode=="direct")request=request with{Url=new("https://a.clyp.it/"+ClypChecks.Id+".ogg"),FormatId=null};
        if(mode=="download")request=request with{Url=new("https://api.clyp.it/"+ClypChecks.Id+".mp3")};
        if(mode is "short" or "cancel")
        {
            var prior=Path.Combine(files.Directory,ClypChecks.Id+".mp3");await File.WriteAllBytesAsync(prior,sources["mp3"]);await File.WriteAllTextAsync(request.DownloadArchiveFile,"other preserved\n");var rejected=false;
            try{await engine.DownloadAsync(request,null,null,stop.Token,_=>callbacks++);}
            catch(InvalidDataException ex){rejected=true;if(mode=="short"&&!ex.Message.Contains("shorter"))throw new InvalidOperationException("Short source did not exercise the provider's minimum duration.");}
            catch(OperationCanceledException)when(mode=="cancel"){rejected=true;}
            if(!rejected||!(await File.ReadAllBytesAsync(prior)).SequenceEqual(sources["mp3"])||await File.ReadAllTextAsync(request.DownloadArchiveFile)!="other preserved\n"||callbacks!=0)throw new InvalidOperationException("Incomplete/cancelled Clyp changed prior media/archive/callbacks.");return;
        }
        var result=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);
        if(!result.Succeeded||result.OutputPaths.Count!=2||callbacks!=2||media!=1)throw new InvalidOperationException("Clyp full audio/original publication changed.");
        foreach(var path in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-err_detect","explode","-i",path,"-f","null","-"],true);
        var extension=mode is "ogg" or "direct"?"ogg":"mp3";var original=result.OutputPaths.Single(path=>File.ReadAllBytes(path).SequenceEqual(sources[extension]));var processed=result.OutputPaths.Single(path=>path!=original);
        if(mode!="convert"&&await AudioHash(ffmpeg,processed)!=await AudioHash(ffmpeg,extension=="ogg"?ogg:mp3))throw new InvalidOperationException("Clyp metadata processing changed decoded audio.");
        var requestsBefore=calls.Count;var repeat=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);
        if(!repeat.Skipped||calls.Count!=requestsBefore||callbacks!=2||File.ReadAllText(request.DownloadArchiveFile).Trim()!="clyp "+ClypChecks.Id)throw new InvalidOperationException("Clyp archive repeated metadata/media or completion.");
    }
}
