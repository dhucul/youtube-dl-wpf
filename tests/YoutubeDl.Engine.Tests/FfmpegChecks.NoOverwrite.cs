using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Providers;

internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> NoOverwriteMediaChecks(string ffmpeg)=>
        new[]{"progressive","hls-ts","hls-fmp4","hls-paired","hls-template","missing-original","missing-component","post-original","post-main","audio","archive","concurrent"}
        .Select(mode=>("No overwrite batch media "+mode,(Func<Task>)(()=>NoOverwriteMedia(ffmpeg,mode))));
    private sealed class NoOverwriteMediaProvider(PageMetadata page):IMediaProvider
    {public MediaProviderInfo Info=>new("no-overwrite-media","No overwrite",0);public bool CanHandle(Uri uri)=>true;public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request,YoutubeDl.Engine.Http.MediaHttp http,CancellationToken ct)=>Task.FromResult(page);}
    private static async Task NoOverwriteMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var source=Path.Combine(files.Directory,"fixture.mp4");
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=12","-f","lavfi","-i","sine=frequency=600:sample_rate=48000","-t","1","-c:v","libx264","-c:a","aac",source]);
        var assets=new Dictionary<string,byte[]>();var hls=mode.StartsWith("hls",StringComparison.Ordinal)||mode=="missing-component";
        if(hls)
        {
            var fragmented=mode=="hls-fmp4";var paired=mode is "hls-paired" or "missing-component";var playlist=Path.Combine(files.Directory,"video.m3u8");
            var args=new List<string>{"-v","error","-y","-i",source,"-c","copy"};
            if(paired)args.AddRange(["-map","0:v:0"]);args.AddRange(["-f","hls","-hls_time","0.5","-hls_list_size","0","-hls_segment_filename",Path.Combine(files.Directory,fragmented?"video%d.m4s":"video%d.ts")]);
            if(fragmented)args.AddRange(["-hls_segment_type","fmp4","-hls_fmp4_init_filename","init.mp4"]);args.Add(playlist);await Run(ffmpeg,args);
            if(paired)await Run(ffmpeg,["-v","error","-y","-i",source,"-map","0:a:0","-c","copy","-f","hls","-hls_time","0.5","-hls_list_size","0","-hls_segment_filename",Path.Combine(files.Directory,"audio%d.ts"),Path.Combine(files.Directory,"audio.m3u8")]);
            var master="#EXTM3U\n"+(paired?"#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"a\",NAME=\"Audio\",DEFAULT=YES,URI=\"audio.m3u8\"\n":"")+
                "#EXT-X-STREAM-INF:BANDWIDTH=100000,RESOLUTION=128x72,CODECS=\"avc1.4d401e,mp4a.40.2\""+(paired?",AUDIO=\"a\"":"")+"\nvideo.m3u8\n";
            assets["master.m3u8"]=System.Text.Encoding.UTF8.GetBytes(master);
            foreach(var path in Directory.GetFiles(files.Directory).Where(path=>Path.GetExtension(path) is ".m3u8" or ".ts" or ".m4s"||Path.GetFileName(path)=="init.mp4"))assets[Path.GetFileName(path)]=await File.ReadAllBytesAsync(path);
        }
        else assets["fixture.mp4"]=await File.ReadAllBytesAsync(source);
        var page=new PageMetadata{Id="fixture",Extractor="no-overwrite-media",Title="Fixture",Media=[new(){Id="main",Url=new("https://cdn.example/"+(hls?"master.m3u8":"fixture.mp4")),Extension=hls?"m3u8":"mp4",Kind=hls?MediaKind.Hls:MediaKind.Progressive,Height=72,Width=128,HasVideo=true,HasAudio=true,VideoCodec="h264",AudioCodec="aac"}]};
        var media=0;NativeEngine Engine()=>new([new NoOverwriteMediaProvider(page)],_=>AdaptiveAccountChecks.Http((message,_)=>{
            var name=Path.GetFileName(message.RequestUri!.AbsolutePath);if(!name.EndsWith(".m3u8"))Interlocked.Increment(ref media);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(assets[name])});
        }));
        var request=new NativeDownloadRequest{Url=new("https://no-overwrite-media.example/fixture"),OutputDirectory=Path.Combine(files.Directory,"out"),FileName="recording",KeepOriginal=true,AddMetadata=true,FfmpegPath=ffmpeg,Retries=0,
            AudioOnly=mode=="audio",AudioFormat=mode=="audio"?"mp3":"best",OutputTemplate=mode=="hls-template"?"%(ext)s/%(title)s.%(ext)s":""};
        var first=await Engine().DownloadAsync(request,null,null,default);AdaptiveRecoveryChecks.Assert(first.Succeeded&&first.OutputPaths.Count>1,"Initial complete fixture failed.");
        var main=first.OutputPath!;var original=first.OutputPaths.First(path=>path!=main&&!path.Contains(".video.")&&!path.Contains(".audio."));var hashes=first.OutputPaths.ToDictionary(path=>path,path=>System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        var missing=mode=="missing-component"?first.OutputPaths.Single(path=>path.Contains(".audio.")):original;
        if(mode is "missing-original" or "missing-component")File.Delete(missing);
        var callbacks=new List<string>();media=0;var repeat=request with {NoOverwrites=mode is not ("post-original" or "post-main"),NoPostOverwrites=mode is "post-original" or "post-main",KeepOriginal=mode!="post-main",
            DownloadArchiveFile=mode=="archive"?Path.Combine(files.Directory,"archive.txt"):""};
        if(mode=="concurrent")
        {
            var results=await Task.WhenAll(Enumerable.Range(0,6).Select(_=>Engine().DownloadAsync(repeat,null,null,default,_=>throw new InvalidOperationException("Existing file callback."))));
            AdaptiveRecoveryChecks.Assert(results.All(result=>result.Succeeded&&result.Skipped)&&media==0);
        }
        else
        {
            var result=await Engine().DownloadAsync(repeat,null,null,default,callbacks.Add);AdaptiveRecoveryChecks.Assert(result.Succeeded);
            if(mode is "missing-original" or "missing-component")AdaptiveRecoveryChecks.Assert(media>0&&callbacks.SequenceEqual([missing])&&result.NewOutputPaths?.SequenceEqual([missing])==true,"A missing retained file was not filled.");
            else if(mode=="post-original")AdaptiveRecoveryChecks.Assert(media>0&&callbacks.SequenceEqual([original]),"Post-only policy lost a requested fresh original.");
            else AdaptiveRecoveryChecks.Assert(result.Skipped&&media==0&&callbacks.Count==0&&result.NewOutputPaths?.Count==0,"Complete media set transferred again.");
        }
        foreach(var path in first.OutputPaths)
        {
            AdaptiveRecoveryChecks.Assert(hashes[path].AsEnumerable().SequenceEqual(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path))),"An existing output changed.");
            await Run(ffmpeg,["-v","error","-xerror","-i",path,"-map","0:v?","-map","0:a?","-f","null","-"],true);
        }
        if(mode=="archive")AdaptiveRecoveryChecks.Assert(File.ReadAllLines(repeat.DownloadArchiveFile).Single()=="no-overwrite-media fixture");
        AdaptiveRecoveryChecks.Assert(Directory.GetDirectories(request.OutputDirectory,".native-download-*",SearchOption.AllDirectories).Length==0,"Skipped job left recovery data.");
    }
}
