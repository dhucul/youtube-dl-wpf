using System.Buffers.Binary;
using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Protocols;
using YoutubeDl.Engine.Protocols.Sabr;

internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> AdaptiveNoOverwriteMediaChecks(string ffmpeg)=>
        new[]{"aac","copy","unknown","mkv-aac","webm-opus","mp3","flac","pcm","template","missing","bad-original","post-main"}
            .Select(mode=>("Adaptive no overwrite media audio "+mode,(Func<Task>)(()=>AdaptiveExistingAudio(ffmpeg,mode))))
        .Concat(new[]{"mkv","webm","template","missing","bad-component","post-main","post-original"}
            .Select(mode=>("Adaptive no overwrite media merge "+mode,(Func<Task>)(()=>AdaptiveExistingMerge(ffmpeg,mode)))))
        .Concat(new[]{"dash-pair","dash-audio","dash-video","dash-template","dash-segments","dash-missing","dash-no-original","sabr-pair","sabr-audio","sabr-video","sabr-missing","sabr-no-original","sabr-unknown"}
            .Select(mode=>("Adaptive no overwrite media "+mode,(Func<Task>)(()=>AdaptiveExistingTransport(ffmpeg,mode)))));

    private static async Task AdaptiveExistingAudio(string ffmpeg,string mode)
    {
        using var files=new Files();
        var raw=mode switch {"mkv-aac"=>"mkv","webm-opus"=>"webm","mp3"=>"mp3","flac"=>"flac","pcm"=>"wav",_=>"mp4"};
        var encoder=mode switch {"webm-opus"=>"libopus","mp3"=>"libmp3lame","flac"=>"flac","pcm"=>"pcm_s24le",_=>"aac"};
        var codec=mode is "unknown" or "bad-original"?"unknown":mode switch {"webm-opus"=>"opus","mp3"=>"mp3","flac"=>"flac","pcm"=>"pcm_s24le",_=>"aac"};
        var source=Path.Combine(files.Directory,"source."+raw);
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","sine=frequency=550:sample_rate=48000:duration=0.6","-c:a",encoder,source]);
        var bytes=await File.ReadAllBytesAsync(source);var media=0;
        var page=new PageMetadata{Id="audio",Title="Audio fixture",Media=[new(){Id="a",Url=new("https://cdn.example/source."+raw),Kind=MediaKind.Progressive,Extension=raw,HasAudio=true,HasVideo=false,AudioCodec=codec}]};
        NativeEngine Engine()=>new([new AdaptiveNoOverwriteChecks.Provider(page)],_=>AdaptiveAccountChecks.Http((_,_)=>{Interlocked.Increment(ref media);return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});}));
        var request=AdaptiveNoOverwriteChecks.Request(Path.Combine(files.Directory,"out")) with {NoOverwrites=false,AudioOnly=true,AudioFormat=mode=="copy"?"copy":"best",FfmpegPath=ffmpeg,OutputTemplate=mode=="template"?"%(ext)s/%(format_id)s.%(ext)s":""};
        var first=await Engine().DownloadAsync(request,null,null,default);WriteCleanupChecks.Check(first.Succeeded&&first.OutputPaths.Count==2,"Automatic audio did not retain its source.");
        var expected=mode switch {"mkv-aac" or "webm-opus"=>"mka","mp3"=>"mp3","flac"=>"flac","pcm"=>"wav",_=>"m4a"};
        WriteCleanupChecks.Check(Path.GetExtension(first.OutputPath)=="."+expected,"Unexpected automatic output type.");
        var original=first.OutputPaths.Single(path=>path!=first.OutputPath);
        if(mode=="missing")File.Delete(original);
        if(mode=="bad-original")await File.WriteAllTextAsync(original,"invalid retained media");
        var hashes=first.OutputPaths.Where(File.Exists).ToDictionary(path=>path,path=>System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        media=0;var callbacks=new List<string>();
        var result=await Engine().DownloadAsync(request with {NoOverwrites=mode!="post-main",NoPostOverwrites=mode=="post-main",KeepOriginal=mode!="post-main"},null,null,default,callbacks.Add);
        WriteCleanupChecks.Check(result.Succeeded,"Automatic audio repeat failed.");
        if(mode=="missing")WriteCleanupChecks.Check(media>0&&callbacks.SequenceEqual([original]),"Missing audio source was not downloaded.");
        else if(mode=="bad-original")WriteCleanupChecks.Check(media>0&&callbacks.Count==0,"Invalid local evidence caused a false skip.");
        else WriteCleanupChecks.Check(result.Skipped&&media==0&&callbacks.Count==0&&result.NewOutputPaths?.Count==0,"Automatic audio repeat transferred media.");
        await AdaptiveExistingVerify(ffmpeg,first,hashes,mode=="bad-original"?[original]:[]);
        if(mode!="bad-original")
        {
            var input=await AdaptivePacketHashes(ffmpeg,source,"a:0");var output=await AdaptivePacketHashes(ffmpeg,first.OutputPath!,"a:0");
            WriteCleanupChecks.Check(input.SequenceEqual(output),"Automatic extraction changed coded audio packets.");
        }
    }

    private static async Task AdaptiveExistingMerge(string ffmpeg,string mode)
    {
        using var files=new Files();var webm=mode=="webm";var video=Path.Combine(files.Directory,"video."+(webm?"webm":"mp4"));var audio=Path.Combine(files.Directory,"audio."+(webm?"webm":"m4a"));
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=12:duration=0.6","-an","-c:v",webm?"libvpx-vp9":"libx264",video]);
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","sine=frequency=600:sample_rate=48000:duration=0.6","-c:a",webm?"libopus":"aac",audio]);
        var assets=new Dictionary<string,byte[]>{["video"]=await File.ReadAllBytesAsync(video),["audio"]=await File.ReadAllBytesAsync(audio)};
        var page=new PageMetadata{Id="merge",Title="Merge fixture",Media=[MergeFormatChecks.Candidate("video",webm?"webm":"mp4",codec:"unknown"),MergeFormatChecks.Candidate("audio",webm?"webm":"m4a",true,codec:"unknown")]};
        var media=0;NativeEngine Engine()=>new([new AdaptiveNoOverwriteChecks.Provider(page)],_=>AdaptiveAccountChecks.Http((message,_)=>{Interlocked.Increment(ref media);return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(assets[message.RequestUri!.AbsolutePath.Trim('/')])});}));
        var request=MergeFormatChecks.Request with {OutputDirectory=Path.Combine(files.Directory,"out"),FileName="result",KeepOriginal=true,FfmpegPath=ffmpeg,MergeOutputFormat="webm/mkv",OutputTemplate=mode=="template"?"%(format_id)s/%(ext)s/%(title)s.%(ext)s":""};
        var first=await Engine().DownloadAsync(request,null,null,default);WriteCleanupChecks.Check(first.Succeeded&&first.OutputPaths.Count==3&&Path.GetExtension(first.OutputPath)==(webm?".webm":".mkv"),"Unknown merge did not choose the actual format.");
        var component=first.OutputPaths.Single(path=>path.Contains(".audio."));
        if(mode=="missing")File.Delete(component);if(mode=="bad-component")await File.WriteAllTextAsync(component,"invalid retained component");
        var hashes=first.OutputPaths.Where(File.Exists).ToDictionary(path=>path,path=>System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        var trap=Path.ChangeExtension(first.OutputPath!,webm?"mkv":"webm");await File.WriteAllTextAsync(trap,"unrelated output");
        media=0;var callbacks=new List<string>();var result=await Engine().DownloadAsync(request with {NoOverwrites=mode is not ("post-main" or "post-original"),NoPostOverwrites=mode is "post-main" or "post-original",KeepOriginal=mode!="post-main"},null,null,default,callbacks.Add);
        WriteCleanupChecks.Check(result.Succeeded&&await File.ReadAllTextAsync(trap)=="unrelated output","Unknown merge damaged an unrelated output.");
        if(mode=="missing")WriteCleanupChecks.Check(media==2&&callbacks.SequenceEqual([component]),"Missing merge input was not filled.");
        else if(mode=="bad-component")WriteCleanupChecks.Check(media==2&&callbacks.Count==0,"Invalid component caused a false skip.");
        else if(mode=="post-original")WriteCleanupChecks.Check(media==2&&callbacks.Count==2,"Post-only overwrite policy dropped fresh components.");
        else WriteCleanupChecks.Check(result.Skipped&&media==0&&callbacks.Count==0&&result.NewOutputPaths?.Count==0,"Unknown merge transferred media again.");
        await AdaptiveExistingVerify(ffmpeg,first,hashes,mode=="bad-component"?[component]:[]);
        WriteCleanupChecks.Check((await AdaptivePacketHashes(ffmpeg,video,"v:0")).AsEnumerable().SequenceEqual(await AdaptivePacketHashes(ffmpeg,first.OutputPath!,"v:0"))&&
            (await AdaptivePacketHashes(ffmpeg,audio,"a:0")).AsEnumerable().SequenceEqual(await AdaptivePacketHashes(ffmpeg,first.OutputPath!,"a:0")),"Unknown merge changed encoded packets.");
    }

    private static async Task AdaptiveExistingTransport(string ffmpeg,string mode)
    {
        using var files=new Files();var sabr=mode.StartsWith("sabr",StringComparison.Ordinal);var audioOnly=mode.EndsWith("audio",StringComparison.Ordinal);var videoOnly=mode.EndsWith("video",StringComparison.Ordinal);
        var video=Path.Combine(files.Directory,"video.mp4");var audio=Path.Combine(files.Directory,"audio.m4a");
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=12:duration=0.6","-an","-c:v","libx264","-movflags","frag_keyframe+empty_moov+default_base_moof",video]);
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","sine=frequency=650:sample_rate=48000:duration=0.6","-c:a","aac","-movflags","frag_keyframe+empty_moov+default_base_moof",audio]);
        var videoBytes=await File.ReadAllBytesAsync(video);var audioBytes=await File.ReadAllBytesAsync(audio);
        var assets=new Dictionary<string,byte[]>{["video.mp4"]=videoBytes,["audio.m4a"]=audioBytes};
        var manifest=AdaptiveNoOverwriteChecks.Manifest();
        if(mode=="dash-segments")
        {
            foreach(var (name,bytes) in new[]{("video",videoBytes),("audio",audioBytes)}){var boundary=AdaptiveFragmentBoundary(bytes);assets[name+"-init.mp4"]=bytes[..boundary];assets[name+"-1.m4s"]=bytes[boundary..];}
            manifest=manifest.Replace("<BaseURL>video.mp4</BaseURL>","<SegmentTemplate initialization=\"video-init.mp4\" media=\"video-$Number$.m4s\" duration=\"1\"/>")
                .Replace("<BaseURL>audio.m4a</BaseURL>","<SegmentTemplate initialization=\"audio-init.mp4\" media=\"audio-$Number$.m4s\" duration=\"1\"/>");
        }
        var page=sabr?AdaptiveNoOverwriteChecks.SabrPage(mode=="sabr-unknown"?"unknown":"pair"):new PageMetadata{Id="fixture",Title="Fixture",Media=[new(){Id="dash",Url=new("https://cdn.example/manifest.mpd"),Kind=MediaKind.Dash,Extension="mpd",HasVideo=true,HasAudio=true}]};
        var media=0;var manifests=0;
        NativeEngine Engine()=>new([new AdaptiveNoOverwriteChecks.Provider(page)],_=>AdaptiveAccountChecks.Http(async (message,_)=>{
            if(!sabr&&message.RequestUri!.AbsolutePath.EndsWith(".mpd",StringComparison.Ordinal)){Interlocked.Increment(ref manifests);return AdaptiveAccountChecks.Text(manifest);}
            Interlocked.Increment(ref media);
            if(!sabr)return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(assets[Path.GetFileName(message.RequestUri!.AbsolutePath)])};
            var fields=ProtobufReader.Read(await message.Content!.ReadAsByteArrayAsync());var selected=fields.Where(field=>field.Number is 16 or 17).Select(field=>ProtobufReader.Read(field.Bytes).Single(part=>part.Number==1).Integer).ToArray();
            using var response=new MemoryStream();SabrFixture.Status(response,2);byte id=0;
            foreach(var (format,bytes) in new[]{(SabrFixture.Video,videoBytes),(SabrFixture.Audio,audioBytes)}.Where(pair=>selected.Contains(pair.Item1.Itag)))
            {var boundary=AdaptiveFragmentBoundary(bytes);SabrFixture.Metadata(response,format,1,600);SabrFixture.Segment(response,format,++id,0,bytes[..boundary]);SabrFixture.Segment(response,format,++id,1,bytes[boundary..],new(0,600,1000));}
            return SabrFixture.Response(response.ToArray());
        }));
        var request=AdaptiveNoOverwriteChecks.Request(Path.Combine(files.Directory,"out")) with {NoOverwrites=false,FfmpegPath=ffmpeg,AudioOnly=audioOnly,AudioFormat="best",VideoOnly=videoOnly,
            KeepOriginal=!mode.EndsWith("no-original",StringComparison.Ordinal),OutputTemplate=mode=="dash-template"?"%(format_id)s/%(ext)s/%(title)s.%(ext)s":"",MergeOutputFormat=mode=="sabr-unknown"?"webm/mkv":""};
        var first=await Engine().DownloadAsync(request,null,null,default);WriteCleanupChecks.Check(first.Succeeded&&media>0,"Initial adaptive media was not transferred.");
        var missing=first.OutputPaths.Last();if(mode.EndsWith("missing",StringComparison.Ordinal))File.Delete(missing);
        var hashes=first.OutputPaths.Where(File.Exists).ToDictionary(path=>path,path=>System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        media=0;manifests=0;var callbacks=new List<string>();var result=await Engine().DownloadAsync(request with {NoOverwrites=true},null,null,default,callbacks.Add);
        if(mode.EndsWith("missing",StringComparison.Ordinal))WriteCleanupChecks.Check(result.Succeeded&&media>0&&callbacks.SequenceEqual([missing]),"Incomplete adaptive set was falsely skipped.");
        else WriteCleanupChecks.Check(result.Succeeded&&result.Skipped&&media==0&&callbacks.Count==0&&result.NewOutputPaths?.Count==0&&(sabr?manifests==0:manifests<=1),"Adaptive repeat fetched segments, initialization, indexes or SABR media.");
        await AdaptiveExistingVerify(ffmpeg,first,hashes,[]);
        if(!audioOnly)WriteCleanupChecks.Check((await AdaptivePacketHashes(ffmpeg,video,"v:0")).AsEnumerable().SequenceEqual(await AdaptivePacketHashes(ffmpeg,first.OutputPath!,"v:0")),"Adaptive output changed video packets.");
        if(!videoOnly)WriteCleanupChecks.Check((await AdaptivePacketHashes(ffmpeg,audio,"a:0")).AsEnumerable().SequenceEqual(await AdaptivePacketHashes(ffmpeg,first.OutputPath!,"a:0")),"Adaptive output changed audio packets.");
    }
    private static int AdaptiveFragmentBoundary(byte[] bytes)
    {
        var boundary=0;while(boundary+8<=bytes.Length&&!bytes.AsSpan(boundary+4,4).SequenceEqual("moof"u8))boundary+=checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(boundary,4)));
        if(boundary==0||boundary>=bytes.Length)throw new InvalidOperationException("Missing generated fragmented media.");return boundary;
    }
    private static async Task AdaptiveExistingVerify(string ffmpeg,NativeDownloadResult first,Dictionary<string,byte[]> hashes,IReadOnlyList<string> invalid)
    {
        foreach(var (path,hash) in hashes)WriteCleanupChecks.Check(hash.AsEnumerable().SequenceEqual(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path))),"An existing output changed bytes.");
        foreach(var path in first.OutputPaths.Except(invalid))await Run(ffmpeg,["-v","error","-xerror","-i",path,"-map","0:v?","-map","0:a?","-f","null","-"],true);
    }
    private static async Task<string[]> AdaptivePacketHashes(string ffmpeg,string path,string role)
    {
        using var document=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-select_streams",role,"-show_packets","-show_data_hash","sha256","-show_entries","packet=data_hash","-of","json",path]));
        return document.RootElement.GetProperty("packets").EnumerateArray().Select(packet=>packet.GetProperty("data_hash").GetString()!).ToArray();
    }
}
