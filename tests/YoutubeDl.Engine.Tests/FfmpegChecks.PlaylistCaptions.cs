using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Authentication;
internal static partial class FfmpegChecks
{
    private static async Task PlaylistCaptionMedia(string ffmpeg,string mode)
    {
        using var files=new Files();var source=Path.Combine(files.Directory,"source.mp4");
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=12","-f","lavfi","-i","sine=frequency=650:sample_rate=48000","-t","3","-c:v","libx264","-preset","ultrafast","-c:a","aac",source]);var data=await File.ReadAllBytesAsync(source);
        var nowness=mode is "nowness" or "snapshot" or "private";var list=nowness||mode is "legacy" or "embed" or "protected" or "cancel";var captions=!list;var api=mode=="api";var renew=mode is "renew" or "revoked";
        var media=0;var captionRequests=0;var issuer=0;var stories=0;var callbacks=0;var firstVideo=0;var lastGeneration=-1;using var stop=new CancellationTokenSource();
        var engine=new NativeEngine(httpFactory:_=>AdaptiveAccountChecks.Http(async(message,ct)=>{
            var uri=message.RequestUri!;
            if(uri.Host=="article.example")return AdaptiveAccountChecks.Text("<script>Wistia.playlist('legacy1234',{});</script>");
            if(uri.Host=="api.nowness.com")
            {
                PlaylistCaptionChecks.Assert(message.Headers.Authorization is null);
                if(uri.AbsolutePath.Contains("/playlist/")){var detail=JsonNode.Parse(PlaylistCaptionChecks.Detail())!;if(mode=="private")detail["isPrivate"]=true;return AdaptiveAccountChecks.Text(detail.ToJsonString());}
                if(uri.AbsolutePath=="/api/post")return AdaptiveAccountChecks.Text(PlaylistCaptionChecks.Posts(uri.Query.Contains("page=2")?2:1));
                stories++;PlaylistCaptionChecks.Assert(uri.AbsolutePath.EndsWith("fixture-story-0"));return AdaptiveAccountChecks.Text(HostingCollectionChecks.Post("fixture-story-0"));
            }
            if(uri.Host=="players.brightcove.net")return AdaptiveAccountChecks.Text("{\"account_id\":\"2385340575001\",\"video_cloud\":{\"policy_key\":\"policy\"}}");
            if(uri.Host=="edge.api.brightcove.com"){issuer++;var video=JsonNode.Parse(SharedHostingChecks.Brightcove("https://cdn.example/movie.mp4").Replace("12345","2385340575001"))!;video["duration"]=3000;video["sources"]![0]!["size"]=data.Length;return AdaptiveAccountChecks.Text(video.ToJsonString());}
            if(uri.Host=="fast.wistia.com"&&uri.AbsolutePath.Contains("/playlists/"))
            {
                var legacy=JsonNode.Parse(PlaylistCaptionChecks.LegacyJson())!;if(mode=="protected")legacy[0]!["medias"]![1]!["embed_config"]!["media"]!["protected"]=true;return AdaptiveAccountChecks.Text(legacy.ToJsonString());
            }
            if(uri.Host is "fast.wistia.com" or "api.wistia.com"&&uri.AbsolutePath.Contains("/medias/")&&uri.AbsolutePath.EndsWith(".json"))
            {
                issuer++;if(api)PlaylistCaptionChecks.Assert(message.Headers.Authorization?.Parameter=="publisher-secret");else PlaylistCaptionChecks.Assert(message.Headers.Authorization is null);
                if(uri.AbsolutePath.Contains("media00001"))firstVideo++;
                PlaylistCaptionChecks.Assert(uri.AbsolutePath.Contains(PlaylistCaptionChecks.MediaId));
                var generation=issuer-1;var captionUrl=renew?"https://cdn.example/g"+generation+"/caption.vtt":null;var json=JsonNode.Parse(PlaylistCaptionChecks.Media("https://cdn.example/movie.mp4",captionUrl,api))!;
                var root=api?json:json["media"]!;root["duration"]=3;root["assets"]![0]![api?"fileSize":"size"]=data.Length;
                if(mode=="auto")root["captions"]![0]!["automatic"]=true;
                if(mode=="revoked"&&generation>0){root["captions"]=new JsonArray();root["availableTranscripts"]=new JsonArray();}
                return AdaptiveAccountChecks.Text(json.ToJsonString());
            }
            if(uri.AbsolutePath.Contains("/captions/")||uri.AbsolutePath.EndsWith("caption.vtt"))
            {
                captionRequests++;if(uri.Host=="api.wistia.com")PlaylistCaptionChecks.Assert(api&&message.Headers.Authorization?.Parameter=="publisher-secret");else PlaylistCaptionChecks.Assert(message.Headers.Authorization is null);
                if(renew&&uri.AbsolutePath.Contains("/g0/"))return AdaptiveAccountChecks.Text("expired",HttpStatusCode.Forbidden);
                lastGeneration=uri.AbsolutePath.Contains("/g1/")?1:0;return AdaptiveAccountChecks.Text(mode=="html"?"<html>wrong content</html>":uri.AbsolutePath.EndsWith(".srt")?PlaylistCaptionChecks.Srt:PlaylistCaptionChecks.Vtt);
            }
            PlaylistCaptionChecks.Assert(uri.Host=="cdn.example"&&message.Headers.Authorization is null);media++;await Task.Yield();return new(HttpStatusCode.OK){Content=new ByteArrayContent(data)};
        }));
        var directory=files.Directory;var output=Path.Combine(directory,list?nowness?"009 - recording.mp4":"002 - recording.mp4":"recording.mp4");var archive=Path.Combine(directory,"archive.txt");await File.WriteAllTextAsync(output,"prior output");await File.WriteAllTextAsync(archive,"prior archive\n");
        var url=nowness?PlaylistCaptionChecks.NUrl:mode=="embed"?"https://article.example/":list?PlaylistCaptionChecks.WUrl:"https://fast.wistia.com/embed/iframe/"+PlaylistCaptionChecks.MediaId;
        var request=new NativeDownloadRequest{Url=new(url),OutputDirectory=directory,OutputTemplate=list?"%(playlist_index)03d - recording.%(ext)s":"recording.%(ext)s",FfmpegPath=ffmpeg,KeepOriginal=true,AddMetadata=true,DownloadPlaylist=list,PlaylistItems=list?nowness?"9":"2":"",DownloadArchiveFile=archive,Retries=0,WriteSubs=captions&&mode!="auto",WriteAutoSubs=mode=="auto",EmbedSubs=captions&&mode!="caption-only",SkipDownload=mode=="caption-only",SubLanguages="en",SubFormat="vtt",ConvertSubs=mode=="convert"?"srt":"",
            Authentication=api?new(){Hosting=new(){Provider=NativeHostingProvider.Wistia,MediaId=PlaylistCaptionChecks.MediaId,AccessToken="publisher-secret"}}:new()};
        if(mode=="snapshot"){var inspected=await engine.InspectAsync(request,default);PlaylistCaptionChecks.Assert(inspected.PlaylistSnapshot?.Count==9);request=request with {PlaylistSnapshot=inspected.PlaylistSnapshot};}
        if(mode=="cancel")stop.Cancel();
        NativeDownloadResult result;
        try{result=await engine.DownloadAsync(request,null,null,stop.Token,_=>callbacks++);}
        catch(Exception e)when(mode is "private" or "revoked" or "html" or "cancel"&&e is InvalidDataException or NotSupportedException or OperationCanceledException)
        {PlaylistCaptionChecks.Assert(callbacks==0&&await File.ReadAllTextAsync(output)=="prior output"&&await File.ReadAllTextAsync(archive)=="prior archive\n");return;}
        if(mode=="protected"){PlaylistCaptionChecks.Assert(!result.Succeeded&&media==0&&callbacks==0&&await File.ReadAllTextAsync(output)=="prior output"&&await File.ReadAllTextAsync(archive)=="prior archive\n");return;}
        PlaylistCaptionChecks.Assert(result.Succeeded&&callbacks==(result.NewOutputPaths??result.OutputPaths).Count&&firstVideo==0);
        if(nowness)PlaylistCaptionChecks.Assert(stories==1);if(renew)PlaylistCaptionChecks.Assert(issuer==2&&captionRequests==2&&lastGeneration==1);
        if(captions)
        {
            var ext=mode=="convert"?"srt":"vtt";var path=Path.Combine(directory,"recording.en."+ext);PlaylistCaptionChecks.Assert(File.Exists(path));var text=await File.ReadAllTextAsync(path);PlaylistCaptionChecks.Assert(text.Contains("First fixture cue.")&&text.Contains("Second fixture cue."));
            if(mode!="caption-only"){var embedded=await Run(ffmpeg,["-v","error","-i",output,"-map","0:s:0","-f","webvtt","-"]);PlaylistCaptionChecks.Assert(embedded.Contains("First fixture cue.")&&embedded.Contains("Second fixture cue."));}
        }
        if(mode=="caption-only"){PlaylistCaptionChecks.Assert(media==0&&captionRequests==1&&await File.ReadAllTextAsync(output)=="prior output");return;}
        foreach(var path in result.OutputPaths.Where(p=>Path.GetExtension(p)==".mp4"))await Run(ffmpeg,["-v","error","-xerror","-i",path,"-map","0:v:0","-map","0:a:0","-f","null","-"],true);
        using var probe=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-count_frames","-show_entries","stream=codec_type,nb_read_frames","-of","json",output]));PlaylistCaptionChecks.Assert(probe.RootElement.GetProperty("streams").EnumerateArray().Single(s=>s.GetProperty("codec_type").GetString()=="video").GetProperty("nb_read_frames").GetString()=="36");
        async Task<string[]> Packets(string path,string role){using var doc=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-select_streams",role=="video"?"v:0":"a:0","-show_packets","-show_data_hash","sha256","-show_entries","packet=data_hash","-of","json",path]));return doc.RootElement.GetProperty("packets").EnumerateArray().Select(p=>p.GetProperty("data_hash").GetString()!).ToArray();}
        foreach(var role in new[]{"video","audio"}){var before=await Packets(source,role);var after=await Packets(output,role);PlaylistCaptionChecks.Assert(before.SequenceEqual(after));}
        var oldMedia=media;var oldCaptions=captionRequests;var oldCallbacks=callbacks;var repeat=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);PlaylistCaptionChecks.Assert(repeat.Succeeded&&repeat.Skipped&&oldMedia==media&&oldCaptions==captionRequests&&oldCallbacks==callbacks);
    }
}
