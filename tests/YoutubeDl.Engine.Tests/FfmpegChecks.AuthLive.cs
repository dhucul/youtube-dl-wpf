using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

internal static partial class FfmpegChecks
{
    private static async Task LiveBatchMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var source=Path.Combine(files.Directory,"source.m3u8");var fragmented=mode=="fmp4";var encrypted=mode=="encrypted";
        var args=new List<string>{"-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=12","-f","lavfi","-i","sine=frequency=590:sample_rate=48000","-t",mode=="tail"?"8":"6","-c:v","libx264","-preset","ultrafast","-g","24","-c:a","aac","-f","hls","-hls_time","2","-hls_list_size","0"};
        if(fragmented)args.AddRange(["-hls_segment_type","fmp4","-hls_fmp4_init_filename","init.mp4"]);
        args.AddRange(["-hls_segment_filename",Path.Combine(files.Directory,fragmented?"seg-%d.m4s":"seg-%d.ts"),source]);await Run(ffmpeg,args);
        var manifest=await File.ReadAllTextAsync(source);var parsed=HlsPlaylistParser.Parse(manifest,new("https://live.example/live.m3u8"));var original=parsed.MediaSegments.Take(mode=="tail"?4:3).ToArray();if(original.Length!=(mode=="tail"?4:3))throw new InvalidOperationException("Generated live fixture segment count changed.");
        var chunks=new Dictionary<string,byte[]>();foreach(var segment in original)chunks[Path.GetFileName(segment.Url.AbsolutePath)]=await File.ReadAllBytesAsync(Path.Combine(files.Directory,Path.GetFileName(segment.Url.AbsolutePath)));
        if(fragmented)chunks["init.mp4"]=await File.ReadAllBytesAsync(Path.Combine(files.Directory,"init.mp4"));var key=Enumerable.Range(0,16).Select(i=>(byte)i).ToArray();
        if(encrypted)foreach(var name in chunks.Keys.ToArray()){using var aes=Aes.Create();aes.Key=key;aes.IV=new byte[16];chunks[name]=aes.EncryptCbc(chunks[name],aes.IV,PaddingMode.PKCS7);}
        var polls=0;var segmentReads=new List<string>();var windows=mode=="tail"?new[]{Window(0,4,false),Window(1,3,true)}:mode=="bounded"?new[]{Window(0,2,false)}:mode=="gap"?new[]{Window(0,1,false),Window(2,1,true)}:mode=="changed"?new[]{Window(0,1,false),Window(0,2,true).Replace("seg-0", "changed-0")}:new[]{Window(0,1,false),Window(0,2,false),Window(1,2,true)};
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));var http=AuthenticationBatchChecks.Http((message,_)=>
        {
            var name=Path.GetFileName(message.RequestUri!.AbsolutePath);
            if(name=="live.m3u8")return Task.FromResult(AuthenticationBatchChecks.Json(windows[Math.Min(polls++,windows.Length-1)]));
            if(name=="key")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(key)});
            segmentReads.Add(name);return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(chunks[name])});
        });
        var request=new NativeDownloadRequest {Url=new("https://live.example/live.m3u8"),OutputDirectory=files.Directory,FfmpegPath=ffmpeg,LiveDurationSeconds=mode=="bounded"?3:null,LiveFromStart=mode=="from-start",ConcurrentFragments=3};
        var first=await new HlsDownloader(http).ReadAsync(request.Url,timeout.Token);var output=Path.Combine(files.Directory,fragmented?"recording.mp4":"recording.ts");await File.WriteAllTextAsync(output,"prior published output");
        try
        {
            var result=await new HlsLiveRecorder(http).DownloadAsync(first,output,request,null,new(0),timeout.Token);
            if(mode is "gap" or "changed")throw new InvalidOperationException("Incomplete live media published.");
            var expected=mode=="bounded"?2:3;if(result.Playlist.MediaSegments.Count!=expected||segmentReads.Count(name=>name.StartsWith("seg-"))!=expected)throw new InvalidOperationException("Live capture repeated or lost a segment.");
            await Run(ffmpeg,["-v","error","-xerror","-i",output,"-f","null","-"],true);
            var reference=Path.Combine(files.Directory,fragmented?"reference.mp4":"reference.ts");await using(var joined=File.Create(reference)){if(fragmented)await joined.WriteAsync(chunks["init.mp4"]);foreach(var segment in original.Skip(mode=="tail"?1:0).Take(expected)){var name=Path.GetFileName(segment.Url.AbsolutePath);var bytes=encrypted?await File.ReadAllBytesAsync(Path.Combine(files.Directory,name)):chunks[name];await joined.WriteAsync(bytes);}}
            if(await AudioHash(ffmpeg,output)!=await AudioHash(ffmpeg,reference)||await Pictures(output)!=await Pictures(reference))throw new InvalidOperationException("Live recording changed captured decoded samples or pictures.");
        }
        catch(InvalidDataException) when(mode is "gap" or "changed"){if(await File.ReadAllTextAsync(output)!="prior published output")throw new InvalidOperationException("Failed live capture replaced prior output.");}
        finally{http.Dispose();CryptographicOperations.ZeroMemory(key);if(Directory.EnumerateDirectories(files.Directory,".hls-live-*").Any())throw new InvalidOperationException("Live session resources were not cleaned.");}
        string Window(int start,int count,bool end)
        {
            var header="#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:"+start+"\n"+(mode=="from-start"?"#EXT-X-PLAYLIST-TYPE:EVENT\n":"");
            if(fragmented)header+="#EXT-X-MAP:URI=\"init.mp4\"\n";if(encrypted)header+="#EXT-X-KEY:METHOD=AES-128,URI=\"key\",IV=0x00000000000000000000000000000000\n";
            return header+string.Concat(original.Skip(start).Take(count).Select(segment=>"#EXTINF:"+segment.DurationSeconds!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\n"+Path.GetFileName(segment.Url.AbsolutePath)+"\n"))+(end?"#EXT-X-ENDLIST\n":"");
        }
        Task<string> Pictures(string path)=>Run(ffmpeg,["-v","error","-i",path,"-map","0:v:0","-fps_mode","passthrough","-c:v","rawvideo","-f","hash","-"]);
    }
    private static async Task LiveBatchFailure(string ffmpeg,bool stall)
    {
        using var files=new Files();var output=Path.Combine(files.Directory,"recording.ts");await File.WriteAllTextAsync(output,"prior");using var cts=new CancellationTokenSource(stall?TimeSpan.FromSeconds(5):TimeSpan.FromMilliseconds(150));
        var text="#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:0\n";using var http=AuthenticationBatchChecks.Http((_,_)=>Task.FromResult(AuthenticationBatchChecks.Json(text)));var playlist=HlsPlaylistParser.Parse(text,new("https://live.example/live.m3u8"));
        try{await new HlsLiveRecorder(http){StallTimeout=TimeSpan.FromMilliseconds(100)}.DownloadAsync(playlist,output,new(){Url=playlist.SourceUrl!,OutputDirectory=files.Directory,FfmpegPath=ffmpeg},null,new(0),cts.Token);throw new InvalidOperationException("A stalled/cancelled recording published.");}
        catch(TimeoutException) when(stall){}catch(OperationCanceledException) when(!stall){}
        if(await File.ReadAllTextAsync(output)!="prior"||Directory.EnumerateDirectories(files.Directory,".hls-live-*").Any())throw new InvalidOperationException("Cancelled or stalled recording changed output/resources.");
    }
    private static async Task AuthenticatedBatchMedia(string ffmpeg)
    {
        using var files=new Files();var source=Path.Combine(files.Directory,"source.mp4");await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=12","-f","lavfi","-i","sine=frequency=590:sample_rate=48000","-t","2","-c:v","libx264","-preset","ultrafast","-c:a","aac",source]);var bytes=await File.ReadAllBytesAsync(source);var mediaReads=0;var logins=0;
        var engine=new NativeEngine(httpFactory:_=>AuthenticationBatchChecks.Http((message,_)=>
        {
            var path=message.RequestUri!.AbsolutePath;
            if(path.EndsWith("/local"))return Task.FromResult(AuthenticationBatchChecks.Json("""{"client_id":"client","client_secret":"secret"}"""));
            if(path.EndsWith("/users/token")){logins++;return Task.FromResult(AuthenticationBatchChecks.Json(AuthenticationBatchChecks.Tokens("account-token")));}
            if(path.EndsWith("/token"))return Task.FromResult(AuthenticationBatchChecks.Json(JsonSerializer.Serialize(new{files=new{token="resource-token",expires=DateTimeOffset.UtcNow.AddMinutes(5)}})));
            if(path=="/static/video.mp4")
            {if(message.Headers.Authorization is not null||message.Headers.Contains("x-peertube-video-password")||message.RequestUri.Query!="?videoFileToken=resource-token")throw new InvalidOperationException("API credentials leaked into media or a scoped token was lost.");mediaReads++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});}
            return Task.FromResult(path.EndsWith(AuthenticationBatchChecks.Id)?AuthenticationBatchChecks.Json(AuthenticationBatchChecks.Video.Replace("\"id\":1},\"files\"","\"id\":3},\"files\"")):AuthenticationBatchChecks.Json("{}",HttpStatusCode.NotFound));
        }));
        var request=AuthenticationBatchChecks.Request() with{OutputDirectory=files.Directory,OutputTemplate="protected.%(ext)s",FfmpegPath=ffmpeg,AddMetadata=true,KeepOriginal=true,DownloadArchiveFile=Path.Combine(files.Directory,"archive.txt")};var callbacks=0;
        var result=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);if(!result.Succeeded||result.OutputPaths.Count!=2||callbacks!=2||mediaReads!=1||logins!=1)throw new InvalidOperationException("Protected native playback/publication changed.");foreach(var path in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",path,"-f","null","-"],true);
        if(!result.OutputPaths.Any(path=>File.ReadAllBytes(path).SequenceEqual(bytes)))throw new InvalidOperationException("Protected original bytes changed.");
        var repeat=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);if(!repeat.Skipped||callbacks!=2||mediaReads!=1)throw new InvalidOperationException("Protected archive repeat published media.");
    }
}
