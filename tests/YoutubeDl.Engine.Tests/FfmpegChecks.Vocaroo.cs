using System.Net;
using System.Security.Cryptography;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Vocaroo;

internal static partial class FfmpegChecks
{
    private static async Task VocarooMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var source=Path.Combine(files.Directory,"source.mp3");
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","sine=frequency=610:sample_rate=44100:duration=2","-c:a","libmp3lame","-b:a","128k",source]);
        var bytes=await File.ReadAllBytesAsync(source);var delivered=bytes;
        if(mode=="changed")
        {
            var other=Path.Combine(files.Directory,"other.mp3");await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","sine=frequency=730:sample_rate=44100:duration=2","-c:a","libmp3lame","-b:a","128k",other]);
            delivered=await File.ReadAllBytesAsync(other);if(delivered.Length!=bytes.Length||delivered.SequenceEqual(bytes))throw new InvalidOperationException("Digest test did not provide a different complete same-size MP3.");
            await Run(ffmpeg,["-v","error","-xerror","-i",other,"-f","null","-"],true);
        }
        if(mode=="short")delivered=bytes[..^500];
        var id=mode=="legacy"?VocarooChecks.Legacy:VocarooChecks.Modern;var calls=new List<(HttpMethod,Uri)>();var media=0;var callbacks=0;using var stop=new CancellationTokenSource();
        var engine=new NativeEngine([new VocarooProvider()],_=>VocarooChecks.Http(calls,bytes,request=>
        {
            media++;if(mode=="cancel")stop.Cancel();return new(HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(delivered)};
        }));
        var request=VocarooChecks.Request(files.Directory,id)with{FfmpegPath=ffmpeg,KeepOriginal=true,AddMetadata=true,AudioOnly=mode=="convert",AudioFormat="m4a",DownloadArchiveFile=Path.Combine(files.Directory,"archive.txt")};
        if(mode=="embed")request=request with{Url=new("https://vocaroo.com/embed/"+id+"?autoplay=0")};
        if(mode=="direct")request=request with{Url=VocarooChecks.Media(id)};
        if(mode is "changed" or "short" or "cancel")
        {
            var prior=Path.Combine(files.Directory,id+".mp3");await File.WriteAllBytesAsync(prior,bytes);await File.WriteAllTextAsync(request.DownloadArchiveFile,"other preserved\n");var rejected=false;
            try{await engine.DownloadAsync(request,null,null,stop.Token,_=>callbacks++);}
            catch(InvalidDataException ex){rejected=true;if(mode=="changed"&&!ex.Message.Contains("digest"))throw new InvalidOperationException("Same-size replacement did not exercise the source digest.");}
            catch(OperationCanceledException)when(mode=="cancel"){rejected=true;}
            if(!rejected||!(await File.ReadAllBytesAsync(prior)).SequenceEqual(bytes)||await File.ReadAllTextAsync(request.DownloadArchiveFile)!="other preserved\n"||callbacks!=0)throw new InvalidOperationException("Failed Vocaroo changed prior media/archive/completions.");return;
        }
        var result=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);
        if(!result.Succeeded||result.OutputPaths.Count!=2||callbacks!=2||media!=1)throw new InvalidOperationException("Vocaroo complete audio/original publication changed.");
        foreach(var path in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-err_detect","explode","-i",path,"-f","null","-"],true);
        var original=result.OutputPaths.Single(path=>File.ReadAllBytes(path).SequenceEqual(bytes));var processed=result.OutputPaths.Single(path=>path!=original);
        if(mode!="convert"&&await AudioHash(ffmpeg,processed)!=await AudioHash(ffmpeg,source))throw new InvalidOperationException("Vocaroo metadata processing changed decoded samples.");
        var repeat=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);
        if(!repeat.Skipped||media!=1||callbacks!=2||File.ReadAllText(request.DownloadArchiveFile).Trim()!="vocaroo "+id)throw new InvalidOperationException("Vocaroo archive identity or repeat changed.");
    }
}
