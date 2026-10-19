using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Authentication;
internal static partial class FfmpegChecks
{
    private static async Task HostingCollectionMedia(string ffmpeg,string mode,bool inspected=false)
    {
        using var files=new Files();var source=Path.Combine(files.Directory,"source.mp4");
        await Run(ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc2=size=128x72:rate=12","-f","lavfi","-i","sine=frequency=570:sample_rate=48000","-t","3","-c:v","libx264","-preset","ultrafast","-c:a","aac",source]);var data=await File.ReadAllBytesAsync(source);
        var story=mode is "story" or "series" or "changed";var wistia=mode is "wistia" or "protected";var jw=mode is "jw" or "duplicate" or "jw-renew" or "foreign";var collection=mode is not ("story" or "changed");
        var issuers=0;var stories=0;var media=0;var callbacks=0;var mediaPaths=new List<string>();var duplicate=mode is "duplicate" or "jw-renew";
        var engine=new NativeEngine(httpFactory:_=>AdaptiveAccountChecks.Http(async(message,ct)=>{
            var uri=message.RequestUri!;if(uri.Host=="api.nowness.com")
            {HostingCollectionChecks.Assert(message.Headers.Authorization is null&&message.Headers.GetValues("X-Nowness-Language").Single()=="en-us");if(uri.AbsolutePath.Contains("/series/"))return AdaptiveAccountChecks.Text(HostingCollectionChecks.SeriesJson());stories++;return AdaptiveAccountChecks.Text(HostingCollectionChecks.Post(uri.AbsolutePath.Split('/')[^1],uri.AbsolutePath.EndsWith("second-story")?"67891":"67890"));}
            if(uri.Host=="fast.wistia.com")
            {
                if(uri.AbsolutePath.Contains("/channel/"))return AdaptiveAccountChecks.Text(HostingCollectionChecks.WistiaJson(mode=="protected"));
                HostingCollectionChecks.Assert(uri.AbsolutePath.Contains("media00002"));issuers++;return AdaptiveAccountChecks.Text("{\"media\":{\"hashedId\":\"media00002\",\"name\":\"Selected fixture\",\"duration\":3,\"assets\":[{\"type\":\"mp4\",\"container\":\"mp4\",\"status\":2,\"url\":\"https://cdn.example/selected.mp4\",\"size\":"+data.Length+",\"height\":72,\"width\":128}]}}");
            }
            if(uri.Host=="players.brightcove.net"){HostingCollectionChecks.Assert(message.Headers.Authorization is null);return AdaptiveAccountChecks.Text("{\"account_id\":\""+(story?"2385340575001":"12345")+"\",\"video_cloud\":{\"policy_key\":\"policy\"}}");}
            if(uri.Host=="cdn.jwplayer.com")
            {
                issuers++;var offset=uri.Query.Contains("page_offset=2")?2:1;var generation=(issuers-1)/2;
                var next=offset==1?(mode=="foreign"?"https://foreign.example/list":"https://cdn.jwplayer.com/v2/playlists/List1234?page_limit=1&page_offset=2"):null;
                var node=JsonNode.Parse(HostingCollectionChecks.JwPage(offset,duplicate&&offset==2,next))!;var item=node["playlist"]![0]!;item["duration"]=3;item["sources"]![0]!["file"]="https://cdn.example/g"+generation+"/"+(offset==1?"first":"second")+".mp4";item["sources"]![0]!["filesize"]=data.Length;return AdaptiveAccountChecks.Text(node.ToJsonString());
            }
            if(uri.Host=="edge.api.brightcove.com")
            {
                issuers++;if(mode=="bc-renew")HostingCollectionChecks.Assert(message.Headers.Authorization?.Parameter=="publisher-grant");else HostingCollectionChecks.Assert(message.Headers.Authorization is null);
                var offset=uri.Query.Contains("offset=0")?0:1;var generation=story?0:(issuers-1)/2;
                var raw=story?SharedHostingChecks.Brightcove("https://cdn.example/story.mp4").Replace("12345","2385340575001").Replace("67890",mode=="changed"?"67899":mode=="series"?"67891":"67890"):HostingCollectionChecks.BcPage(offset);
                var node=JsonNode.Parse(raw)!;var video=(story?node:node["videos"]![0])!;video["duration"]=3000;video["sources"]![0]!["src"]="https://cdn.example/g"+generation+"/"+(offset==0?"first":"second")+".mp4";video["sources"]![0]!["size"]=data.Length;return AdaptiveAccountChecks.Text(node.ToJsonString());
            }
            HostingCollectionChecks.Assert(uri.Host=="cdn.example"&&message.Headers.Authorization is null);media++;mediaPaths.Add(uri.AbsolutePath);
            if(mode is "jw-renew" or "bc-renew"&&uri.AbsolutePath.Contains(inspected?"/g1/":"/g0/"))return new HttpResponseMessage(HttpStatusCode.Forbidden){Content=new StringContent("expired")};
            await Task.Yield();return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(data)};
        }));
        var url=story?mode=="series"?HostingCollectionChecks.Series:HostingCollectionChecks.Story:wistia?HostingCollectionChecks.Channel:jw?"https://cdn.jwplayer.com/v2/playlists/List1234?page_limit=1":"https://players.brightcove.net/12345/default_default/index.html?playlistId=99999";
        var output=Path.Combine(files.Directory,collection?"002 - recording.mp4":"recording.mp4");var archive=Path.Combine(files.Directory,"archive.txt");await File.WriteAllTextAsync(output,"prior output");await File.WriteAllTextAsync(archive,"prior archive\n");
        var request=new NativeDownloadRequest{Url=new(url),OutputDirectory=files.Directory,OutputTemplate=collection?"%(playlist_index)03d - recording.%(ext)s":"recording.%(ext)s",FfmpegPath=ffmpeg,KeepOriginal=true,DownloadPlaylist=collection,PlaylistItems=collection?mode=="duplicate"?"1,2":"2":"",DownloadArchiveFile=archive,Retries=0,
            Authentication=mode=="bc-renew"?new(){Hosting=new(){Provider=NativeHostingProvider.Brightcove,AccountId="12345",MediaId="99999",AccessToken="publisher-grant"}}:new()};
        NativeDownloadResult result;
        if(inspected){var info=await engine.InspectAsync(request,default);HostingCollectionChecks.Assert(info.PlaylistSnapshot?.Count==2);request=request with {PlaylistSnapshot=info.PlaylistSnapshot};}
        try{result=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);}
        catch(Exception e)when(mode is "foreign" or "changed"&&e is InvalidDataException or NotSupportedException)
        {HostingCollectionChecks.Assert(callbacks==0&&await File.ReadAllTextAsync(output)=="prior output"&&await File.ReadAllTextAsync(archive)=="prior archive\n"&&media==0);return;}
        if(mode is "protected" or "foreign" or "changed")
        {
            HostingCollectionChecks.Assert(!result.Succeeded&&callbacks==0&&await File.ReadAllTextAsync(output)=="prior output"&&await File.ReadAllTextAsync(archive)=="prior archive\n"&&media==0);return;
        }
        HostingCollectionChecks.Assert(result.Succeeded&&callbacks==(result.NewOutputPaths??result.OutputPaths).Count&&mediaPaths.All(p=>!p.Contains("/first.mp4")||mode=="duplicate"));
        if(mode=="series")HostingCollectionChecks.Assert(stories==1);if(mode=="wistia")HostingCollectionChecks.Assert(issuers==1);
        if(mode is "jw-renew" or "bc-renew")HostingCollectionChecks.Assert(issuers==(inspected?6:4)&&media==2);
        var main=result.OutputPaths.Single(p=>!Path.GetFileName(p).Contains(".original.")&&Path.GetExtension(p)==".mp4");foreach(var path in result.OutputPaths)await Run(ffmpeg,["-v","error","-xerror","-i",path,"-f","null","-"],true);
        using var probe=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-count_frames","-show_entries","stream=codec_type,nb_read_frames","-of","json",main]));HostingCollectionChecks.Assert(probe.RootElement.GetProperty("streams").EnumerateArray().Single(s=>s.GetProperty("codec_type").GetString()=="video").GetProperty("nb_read_frames").GetString()=="36");
        async Task<string[]> Packets(string path,string role){using var doc=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe"),["-v","error","-select_streams",role=="video"?"v:0":"a:0","-show_packets","-show_data_hash","sha256","-show_entries","packet=data_hash","-of","json",path]));return doc.RootElement.GetProperty("packets").EnumerateArray().Select(p=>p.GetProperty("data_hash").GetString()!).ToArray();}
        foreach(var role in new[]{"video","audio"}){var before=await Packets(source,role);var after=await Packets(main,role);HostingCollectionChecks.Assert(before.SequenceEqual(after));}
        var previous=media;var completed=callbacks;var repeat=await engine.DownloadAsync(request,null,null,default,_=>callbacks++);HostingCollectionChecks.Assert(repeat.Succeeded&&repeat.Skipped&&media==previous&&callbacks==completed);
    }
}
