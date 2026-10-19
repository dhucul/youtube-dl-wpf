using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Providers;

internal static class NoOverwriteBatchChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
        new[]{"processed","plain","missing","edited","template","post-original","post-plain","post-main","archive","collision","folder","cancel","pair","pair-missing","partial"}.Select(mode=>("No overwrite batch progressive "+mode,(Func<Task>)(()=>Progressive(mode))))
        .Concat(new[]{"ts","master","fmp4","paired","video","audio","missing","component","unknown","incomplete","mixed","nested","drm","caption","constraint","wrong-file"}.Select(mode=>("No overwrite batch HLS "+mode,(Func<Task>)(()=>Hls(mode)))));
    private sealed class Provider(PageMetadata page):IMediaProvider
    {
        public MediaProviderInfo Info=>new("no-overwrite-fixture","No overwrite",0);public bool CanHandle(Uri uri)=>true;
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request,YoutubeDl.Engine.Http.MediaHttp http,CancellationToken ct)=>Task.FromResult(page);
    }
    private static MediaCandidate Candidate(string id,string extension,bool video=true)=>new(){Id=id,Url=new("https://cdn.example/"+id+"."+extension),Extension=extension,Kind=MediaKind.Progressive,HasAudio=video?id=="main":true,HasVideo=video,Height=video?72:null,VideoCodec=video?"h264":null,AudioCodec=video?null:"aac"};
    private static NativeDownloadRequest Request(string directory)=>new(){Url=new("https://no-overwrite.example/movie"),OutputDirectory=directory,FileName="movie",NoOverwrites=true,KeepOriginal=true,AddMetadata=true,Retries=0};
    private static string DirectoryFor()=>Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));
    private static async Task Progressive(string mode)
    {
        var directory=DirectoryFor();Directory.CreateDirectory(directory);
        try
        {
            var pair=mode.StartsWith("pair",StringComparison.Ordinal);var page=new PageMetadata{Id="movie",Extractor="fixture",Title="Movie",Media=pair?[Candidate("video","mp4"),Candidate("audio","m4a",false)]:[Candidate("main","mp4")]};
            var request=Request(directory) with {AddMetadata=mode!="plain"&&mode!="post-plain"};
            if(mode=="template")request=request with {OutputTemplate="%(ext)s/%(title)s.%(ext)s"};
            if(mode.StartsWith("post-",StringComparison.Ordinal))request=request with {NoOverwrites=false,NoPostOverwrites=true,KeepOriginal=mode!="post-main"};
            var main=Path.Combine(directory,"movie.mp4");var original=Path.Combine(directory,"movie.original.mp4");
            if(mode=="template"){Directory.CreateDirectory(Path.Combine(directory,"mp4"));main=Path.Combine(directory,"mp4","Movie.mp4");original=Path.Combine(directory,"mp4","Movie.original.mp4");}
            await File.WriteAllTextAsync(main,mode=="edited"?"edited main":"existing main");
            if(mode!="missing")await File.WriteAllTextAsync(original,"existing original");
            if(pair){await File.WriteAllTextAsync(Path.Combine(directory,"movie.video.mp4"),"existing video");if(mode!="pair-missing")await File.WriteAllTextAsync(Path.Combine(directory,"movie.audio.m4a"),"existing audio");}
            if(mode=="archive")request=request with {DownloadArchiveFile=Path.Combine(directory,"archive.txt")};
            if(mode=="collision")request=request with {DownloadArchiveFile=original};
            if(mode=="folder"){File.Delete(original);Directory.CreateDirectory(original);}
            string? partial=null;if(mode=="partial"){var scope=YoutubeDl.Engine.Download.AdaptiveFragmentCheckpoint.Hash(Path.GetFullPath(main)+"\n"+request.Url.AbsoluteUri);var raw=Path.Combine(directory,".native-download-"+scope[..24],"raw");Directory.CreateDirectory(raw);partial=Path.Combine(raw,"source.mp4.part");await File.WriteAllTextAsync(partial,"retained partial");}
            using var stop=new CancellationTokenSource();if(mode=="cancel")stop.Cancel();
            var transfers=0;var callbacks=0;var engine=new NativeEngine([new Provider(page)],_=>AdaptiveAccountChecks.Http((_,_)=>{transfers++;throw new HttpRequestException("Transfer required.");}));
            var fallback=mode is "missing" or "post-original" or "post-plain" or "pair-missing";var failure=mode is "collision" or "folder" or "cancel";var failed=false;
            try
            {
                var result=await engine.DownloadAsync(request,null,null,stop.Token,_=>callbacks++);WriteCleanupChecks.Check(!fallback&&!failure&&result.Succeeded&&result.Skipped,"Existing output set was not skipped.");
                WriteCleanupChecks.Check(result.NewOutputPaths?.Count==0&&result.OutputPaths.Count==(mode is "plain" or "post-main"?1:pair?4:2),"Skipped output inventory differs.");
                if(mode=="archive")WriteCleanupChecks.Check(File.ReadAllLines(request.DownloadArchiveFile).Single()=="fixture movie","No-transfer success did not seed archive.");
            }
            catch(Exception e)when((fallback||failure)&&e is HttpRequestException or IOException or OperationCanceledException or InvalidOperationException){failed=true;}
            WriteCleanupChecks.Check(failed==(fallback||failure)&&callbacks==0&&(fallback?transfers<=1:transfers==0),"No-overwrite transferred a complete set or skipped a missing original.");
            WriteCleanupChecks.Check(await File.ReadAllTextAsync(main)==(mode=="edited"?"edited main":"existing main"),"Existing main changed.");
            if(partial is not null)WriteCleanupChecks.Check(await File.ReadAllTextAsync(partial)=="retained partial","No-overwrite removed a pre-existing partial.");
        }
        finally{Directory.Delete(directory,true);}
    }
    private static async Task Hls(string mode)
    {
        var directory=DirectoryFor();Directory.CreateDirectory(directory);
        try
        {
            var paired=mode is "paired" or "component" or "unknown" or "video" or "audio";var master=paired||mode is "master" or "nested" or "caption";
            var fragmented=mode=="fmp4";var mainExt=paired&&mode!="video"&&mode!="audio"?"mp4":fragmented?"mp4":mode=="audio"?"mp3":"ts";
            var page=new PageMetadata{Id="movie",Title="Movie",Extractor="fixture",Media=[new(){Id="hls",Url=new("https://cdn.example/master.m3u8"),Kind=MediaKind.Hls,Extension="m3u8",HasVideo=true,HasAudio=true,Height=mode=="constraint"?null:72,VideoCodec="h264"}]};
            var request=Request(directory) with {VideoOnly=mode=="video",AudioOnly=mode=="audio",AudioFormat=mode=="audio"?"mp3":"best",MaximumHeight=mode=="constraint"?72:null,EmbedSubs=mode=="caption",
                MergeOutputFormat=mode=="unknown"?"webm/mkv":""};
            var main=Path.Combine(directory,"movie."+mainExt);var original=Path.Combine(directory,"movie.original."+mainExt);
            await File.WriteAllTextAsync(main,"existing main");if(mode!="missing")await File.WriteAllTextAsync(original,"existing original");
            if(mode=="audio"){File.Delete(original);await File.WriteAllTextAsync(Path.Combine(directory,"movie.ts"),"existing raw audio");}
            if(paired&&mode is not ("video" or "audio")){await File.WriteAllTextAsync(Path.Combine(directory,"movie.video.ts"),"existing video");if(mode!="component")await File.WriteAllTextAsync(Path.Combine(directory,"movie.audio.ts"),"existing audio");}
            if(mode=="wrong-file"){File.Delete(main);File.Delete(original);await File.WriteAllTextAsync(Path.Combine(directory,"movie.mp4"),"unrelated container");}
            var manifests=0;var bodies=0;var callbacks=0;
            var playlist="#EXTM3U\n#EXT-X-TARGETDURATION:1\n"+(fragmented?"#EXT-X-MAP:URI=\"init.mp4\"\n":"")+"#EXTINF:1,\nsegment.ts\n"+(mode=="mixed"?"#EXT-X-MAP:URI=\"later.mp4\"\n#EXTINF:1,\nlater.m4s\n":"")+(mode=="incomplete"?"":"#EXT-X-ENDLIST\n");
            var masterText="#EXTM3U\n"+(paired?"#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"a\",NAME=\"Audio\",DEFAULT=YES,URI=\"audio.m3u8\"\n":"")+(mode=="caption"?"#EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID=\"s\",NAME=\"English\",LANGUAGE=\"en\",URI=\"captions.m3u8\"\n":"")+
                "#EXT-X-STREAM-INF:BANDWIDTH=100000,RESOLUTION=128x72,CODECS=\"avc1.4d401e,mp4a.40.2\""+(paired?",AUDIO=\"a\"":"")+(mode=="caption"?",SUBTITLES=\"s\"":"")+"\nvideo.m3u8\n";
            if(mode=="drm")playlist=playlist.Replace("#EXTINF:1,","#EXT-X-KEY:METHOD=SAMPLE-AES,URI=\"key\"\n#EXTINF:1,");
            var engine=new NativeEngine([new Provider(page)],_=>AdaptiveAccountChecks.Http((message,_)=>{
                if(message.RequestUri!.AbsolutePath.EndsWith(".m3u8")){manifests++;return Task.FromResult(AdaptiveAccountChecks.Text(master&&message.RequestUri.AbsolutePath=="/master.m3u8"||mode=="nested"?masterText:playlist));}
                bodies++;throw new HttpRequestException("Media body requested.");
            }));
            var success=mode is "ts" or "master" or "fmp4" or "paired" or "video" or "audio";var failed=false;
            try{var result=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);WriteCleanupChecks.Check(success&&result.Succeeded&&result.Skipped&&result.NewOutputPaths?.Count==0,"HLS existing output was not resolved.");}
            catch(Exception e)when(!success&&e is HttpRequestException or InvalidOperationException or NotSupportedException or InvalidDataException){failed=true;}
            WriteCleanupChecks.Check(failed!=success&&bodies==0&&callbacks==0,"HLS preflight transferred media or reported completion.");WriteCleanupChecks.Check(manifests<7,"HLS preflight exceeded its bounded reads.");
            if(mode!="wrong-file")WriteCleanupChecks.Check(await File.ReadAllTextAsync(main)=="existing main","HLS preflight changed existing media.");
        }
        finally{Directory.Delete(directory,true);}
    }
}

