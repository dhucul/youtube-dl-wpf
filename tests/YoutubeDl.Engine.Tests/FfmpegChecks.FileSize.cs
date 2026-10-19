using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static partial class FfmpegChecks
{
    private static async Task FileSizeMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var hls=mode.StartsWith("hls",StringComparison.Ordinal);var source=Path.Combine(files.Directory,hls?"source.ts":"source.wav");
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","sine=frequency=690:sample_rate=44100:duration=2","-c:a",hls?"aac":"pcm_s16le",source]);
        var bytes=await File.ReadAllBytesAsync(source);var url=new Uri("https://size-media.example/"+(hls?"stream.m3u8":"source.wav"));
        var page=new PageMetadata{Id="file-size",Title="Size",Media=[new(){Id="audio",Url=url,Kind=hls?MediaKind.Hls:MediaKind.Progressive,Extension=hls?"ts":"wav",HasAudio=true,HasVideo=false,AudioCodec=hls?"aac":"pcm_s16le"}]};
        var engine=new NativeEngine([new FileSizeProvider(page)],_=>new MediaHttp(new Handler(request=>request.RequestUri!.AbsolutePath.EndsWith(".m3u8")?
            new(HttpStatusCode.OK){Content=new StringContent("#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXTINF:2,\nsegment.ts\n#EXT-X-ENDLIST\n")}:
            new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)})));
        var archive=Path.Combine(files.Directory,"archive.txt");var prior=Path.Combine(files.Directory,hls?"size.ts":"size.mp3");
        var request=new NativeDownloadRequest{Url=url,OutputDirectory=files.Directory,OutputTemplate="size.%(ext)s",DownloadPlaylist=false,FfmpegPath=ffmpeg,DownloadArchiveFile=archive,
            AudioOnly=!hls,AudioFormat="mp3",AudioQuality="128K",KeepOriginal=true,AddMetadata=false,
            MinimumFileSize=mode=="hls-min"?(bytes.Length+1000).ToString():hls?"":"100K",MaximumFileSize=mode=="hls"?"1":""};
        var callbacks=0;
        if(hls)
        {
            await File.WriteAllTextAsync(prior,"previous complete source");
            try{await engine.DownloadAsync(request,null,null,default,_=>callbacks++);throw new InvalidOperationException("HLS size refusal was not enforced before publication.");}catch(NativeFileSizeException){}
            if(await File.ReadAllTextAsync(prior)!="previous complete source"||File.Exists(archive)||callbacks!=0)throw new InvalidOperationException("HLS size refusal changed previous outputs or archive.");
        }
        else
        {
            var result=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);if(!result.Succeeded||result.OutputPaths.Count!=2||callbacks!=2)throw new InvalidOperationException("Bounded conversion failed.");
            if(new FileInfo(result.OutputPath!).Length>=100*1024||!result.OutputPaths.Any(path=>File.ReadAllBytes(path).SequenceEqual(bytes)))throw new InvalidOperationException("File size was applied to converted output rather than original media.");
            foreach(var output in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",output,"-map","0:a","-f","null","-"],true);
        }
    }
    private sealed class FileSizeProvider(PageMetadata page):IMediaProvider
    {
        public MediaProviderInfo Info{get;}=new("size-media","Real file-size fixture",1000);public bool CanHandle(Uri url)=>true;
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request,MediaHttp http,CancellationToken ct)=>Task.FromResult(page);
    }
}
