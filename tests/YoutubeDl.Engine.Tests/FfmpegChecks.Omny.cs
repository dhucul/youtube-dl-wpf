using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Omny;

internal static partial class FfmpegChecks
{
    private static async Task OmnyMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var hls=mode.StartsWith("hls",StringComparison.Ordinal);var video=mode is "video" or "timed"||hls;var source=Path.Combine(files.Directory,"source."+(hls?"ts":video?"mp4":"mp3"));
        var args=new List<string>{"-nostdin","-v","error","-y"};if(video)args.AddRange(["-f","lavfi","-i","testsrc2=size=160x120:rate=10:duration=2"]);
        args.AddRange(["-f","lavfi","-i","sine=frequency=720:sample_rate=44100:duration="+(mode=="dynamic"?"3":"2")]);
        if(video)args.AddRange(["-c:v","libx264","-c:a","aac","-shortest"]);else args.AddRange(["-c:a","libmp3lame"]);if(hls)args.AddRange(["-f","mpegts"]);args.Add(source);await Run(ffmpeg,args);
        var bytes=await File.ReadAllBytesAsync(source);var selected=mode is "selected" or "snapshot";var id=selected?OmnyChecks.Other:OmnyChecks.Id;var clip=OmnyChecks.Clip(id,mode.Contains("incomplete",StringComparison.Ordinal)?20:2,mode=="dynamic",hls?"hls":video?"video":"audio");
        if(mode=="timed"){clip["HasPublishedTranscript"]=true;clip["Chapters"]=new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject{["Name"]="One",["Position"]="00:00:00"},new System.Text.Json.Nodes.JsonObject{["Name"]="Two",["Position"]="00:00:01"});}
        var calls=new List<Uri>();var completions=new List<string>();var engine=new NativeEngine([new OmnyProvider()],_=>OmnyChecks.Http(calls,clip:clip,media:r=>
        {
            if(hls&&r.RequestUri!.AbsoluteUri==OmnyChecks.Source(id,"video.m3u8"))return new(HttpStatusCode.OK){Content=new StringContent("#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:0\n#EXTINF:2,\nsegment.ts\n#EXT-X-ENDLIST\n")};
            var allowed=hls?r.RequestUri!.AbsolutePath.EndsWith("/segment.ts",StringComparison.Ordinal):r.RequestUri!.AbsoluteUri==OmnyChecks.Source(id,video?"video.mp4":"audio.mp3");
            if(!allowed||r.Headers.Contains("Authorization")||r.Headers.Contains("Cookie"))throw new InvalidOperationException("Omny media URI/credential scope changed.");
            return new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
        }));
        var archive=Path.Combine(files.Directory,"archive.txt");var request=OmnyChecks.Request(files.Directory,selected,mode=="snapshot")with{FfmpegPath=ffmpeg,KeepOriginal=true,AddMetadata=true,WriteInfoJson=true,DownloadArchiveFile=archive,PlaylistItems=selected?"2":"",AudioOnly=mode=="convert",AudioFormat="m4a",WriteSubs=mode=="timed",EmbedSubs=mode=="timed",EmbedChapters=mode=="timed",SubLanguages="en"};
        if(mode=="snapshot"){var inspected=await engine.InspectAsync(request,default);request=request with{PlaylistSnapshot=inspected.PlaylistSnapshot};}
        if(mode.Contains("incomplete",StringComparison.Ordinal))
        {
            var prior=Path.Combine(files.Directory,hls?"omny.ts":"omny.mp3");await File.WriteAllTextAsync(prior,"previous complete media");
            try{await engine.DownloadAsync(request,null,null,default,completions.Add);throw new InvalidOperationException("Short Omny source published.");}catch(InvalidDataException){}
            if(completions.Count!=0||File.Exists(archive)||await File.ReadAllTextAsync(prior)!="previous complete media")throw new InvalidOperationException("Omny refusal changed prior file/archive.");return;
        }
        var result=await engine.DownloadAsync(request,null,null,default,completions.Add);
        if(!result.Succeeded||result.OutputPaths.Count!=2||!result.OutputPaths.SequenceEqual(completions))throw new InvalidOperationException("Omny complete media/original publication failed: "+result.Error+string.Join(";",result.Failures.Select(f=>f.Message)));
        foreach(var output in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",output,"-map","0:a", "-f","null","-"],true);
        if(video)foreach(var output in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",output,"-map","0:v", "-f","null","-"],true);
        if(!result.OutputPaths.Any(output=>File.ReadAllBytes(output).SequenceEqual(bytes)))throw new InvalidOperationException("Omny original bytes changed.");
        if(mode!="convert")
        {
            var processed=result.OutputPath??result.OutputPaths.Single(path=>!Path.GetFileName(path).Contains(".original"));
            var before=await Run(ffmpeg,["-v","error","-xerror","-i",source,"-map","0:a","-c:a","pcm_s16le","-f","hash","-hash","sha256","-"],true);
            var after=await Run(ffmpeg,["-v","error","-xerror","-i",processed,"-map","0:a","-c:a","pcm_s16le","-f","hash","-hash","sha256","-"],true);if(before!=after)throw new InvalidOperationException("Omny processing changed unconverted decoded samples.");
        }
        using var info=JsonDocument.Parse(await File.ReadAllTextAsync(result.SidecarPaths.Single(path=>path.EndsWith(".info.json"))));
        if(mode=="timed")
        {
            var probe=Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe");using var metadata=JsonDocument.Parse(await Run(probe,["-v","error","-show_chapters","-show_streams","-of","json",result.OutputPath!]));
            if(metadata.RootElement.GetProperty("chapters").GetArrayLength()!=2||!metadata.RootElement.GetProperty("streams").EnumerateArray().Any(stream=>stream.GetProperty("codec_type").GetString()=="subtitle"))throw new InvalidOperationException("Omny chapters/captions did not embed.");
            var text=await Run(ffmpeg,["-v","error","-i",result.OutputPath!,"-map","0:s:0","-f","webvtt","-"],true);if(!text.Contains("First words")||!text.Contains("Last words"))throw new InvalidOperationException("Omny embedded caption text changed.");
        }
        if(info.RootElement.GetProperty("id").GetString()!=id||selected&&info.RootElement.GetProperty("playlist_index").GetInt32()!=2)throw new InvalidOperationException("Omny identity/source position changed.");
        if((await File.ReadAllTextAsync(archive)).Trim()!="omnyfm "+id)throw new InvalidOperationException("Omny archive identity changed.");calls.Clear();completions.Clear();
        var repeat=await engine.DownloadAsync(request,null,null,default,completions.Add);if(!repeat.Succeeded||completions.Count!=0||calls.Any(url=>url.Host=="traffic.omny.fm"||url.AbsolutePath.EndsWith("/"+id)))throw new InvalidOperationException("Archived Omny job repeated playback/transfer.");
    }
}
