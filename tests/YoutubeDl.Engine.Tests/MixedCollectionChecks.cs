using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Providers.Vimeo;
using YoutubeDl.Engine.Providers.Nowness;
using YoutubeDl.Engine.Providers.Wistia;
internal static class MixedCollectionChecks
{
    internal const string Clip="11111111-2222-3333-4444-555555555555";
    internal static readonly Uri ManifestUrl=new("https://vod.example.vimeocdn.com/"+Clip+"/v2/playlist/av/primary/playlist.json");
    internal static string Config(string url="https://cdn.example/movie.mp4",bool dash=false)=>JsonSerializer.Serialize(new{view=1,video=new{id=12345,title="Literal player",duration=3},request=new{files=dash?(object)new{dash=new{default_cdn="cdn",cdns=new{cdn=new{url}}}}:new{progressive=new[]{new{quality="360p",url,mime="video/mp4",height=72,width=128}}}}});
    internal static string JsonManifest(byte[]? videoInit=null,byte[]? audioInit=null,int generation=0)=>JsonSerializer.Serialize(new{clip_id=Clip,base_url="../../../remux/avf/",video=new[]{Track("video",videoInit,generation)},audio=new[]{Track("audio",audioInit,generation)}});
    private static object Track(string role,byte[]? init,int generation)=>new{id=role+"-track",base_url=role+"/",mime_type=role+"/mp4",codecs=role=="video"?"avc1.42001e":"mp4a.40.2",bitrate=100000,duration=3,framerate=12,height=72,init_segment=Convert.ToBase64String(init??new byte[]{0,0,0,8,102,116,121,112,0,0,0,8,109,111,111,118}),init_segment_url="",segments=new[]{new{start=0,end=3,url="segment.m4s?generation="+generation,size=100}}};
    public static IEnumerable<(string Name,Func<Task> Test)> All=>
        new[]{"literal","direct","notfound","wrong","duplicate","computed","multiple","denied","redirect","unavailable","password","api","cancel"}.Select(mode=>("Mixed collections Vimeo player "+mode,(Func<Task>)(()=>Player(mode))))
        .Concat(new[]{"valid","foreign","clip","duplicate","gap","truncated","escaped","protected","init","tracks","live"}.Select(mode=>("Mixed collections JSON DASH "+mode,(Func<Task>)(()=>Manifest(mode)))))
        .Concat(new[]{"repeat","same-video","unknown","missing","conflict","preview"}.Select(mode=>("Mixed collections Nowness membership "+mode,(Func<Task>)(()=>Nowness(mode)))))
        .Concat(new[]{"repeat-media","repeat-episode","missing-episode","private"}.Select(mode=>("Mixed collections Wistia membership "+mode,(Func<Task>)(()=>Wistia(mode)))));
    private static async Task Player(string mode)
    {
        var calls=new List<Uri>();using var stop=new CancellationTokenSource();using var http=AdaptiveAccountChecks.Http((request,ct)=>{
            var uri=request.RequestUri!;calls.Add(uri);PlaylistCaptionChecks.Assert(request.Headers.Authorization is null||mode=="api");
            if(mode=="api"){PlaylistCaptionChecks.Assert(uri.Host=="api.vimeo.com");return Task.FromResult(AdaptiveAccountChecks.Text("denied",HttpStatusCode.Forbidden));}
            PlaylistCaptionChecks.Assert(request.Headers.Referrer?.AbsoluteUri=="https://publisher.example/story");
            if(mode=="cancel")stop.Cancel();
            if(uri.AbsolutePath.EndsWith("/config")&&mode!="direct")return Task.FromResult(AdaptiveAccountChecks.Text("denied",mode=="notfound"?HttpStatusCode.NotFound:HttpStatusCode.Forbidden));
            var config=Config();if(mode=="wrong")config=config.Replace("12345","54321");if(mode=="duplicate")config=config.Replace("\"view\":1","\"view\":1,\"view\":1");if(mode=="unavailable")config=config.Replace("\"view\":1","\"view\":2");if(mode=="password")config=config.Replace("\"view\":1","\"view\":4");
            var html="<script>window.playerConfig = "+config+";</script>";if(mode=="computed")html="<script>window.playerConfig = load();</script>";if(mode=="multiple")html+=html;
            var response=AdaptiveAccountChecks.Text(mode=="direct"?config:html,mode=="denied"?HttpStatusCode.Forbidden:HttpStatusCode.OK);if(mode=="redirect")response.RequestMessage=new(HttpMethod.Get,"https://player.vimeo.com/video/99999");return Task.FromResult(response);
        });
        try
        {
            using var session=new VimeoSession(http,new(){Url=new("https://vimeo.com/12345"),OutputDirectory=".",Referer="https://publisher.example/story",Authentication=mode=="api"?new(){AccessToken="test-grant"}:new()},VimeoAddress.Parse(new("https://vimeo.com/12345")));
            var page=await session.ReadAsync(stop.Token);PlaylistCaptionChecks.Assert(mode is "literal" or "direct" or "notfound"&&page.Id=="12345"&&page.Media.Count==1&&calls.Count==(mode=="direct"?1:2));
        }
        catch(Exception e)when(mode is not ("literal" or "direct" or "notfound")&&e is InvalidDataException or NotSupportedException or HttpRequestException or JsonException or OperationCanceledException){PlaylistCaptionChecks.Assert(calls.Count<=2);}
    }
    private static Task Manifest(string mode)
    {
        var json=JsonNode.Parse(JsonManifest())!;var url=ManifestUrl;
        if(mode=="foreign")url=new("https://foreign.example/"+Clip+"/playlist.json");if(mode=="clip")json["clip_id"]="aaaaaaaa-2222-3333-4444-555555555555";
        if(mode=="gap")json["video"]![0]!["segments"]![0]!["start"]=1;if(mode=="truncated")json["video"]![0]!["segments"]![0]!["end"]=2;
        if(mode=="escaped")json["video"]![0]!["segments"]![0]!["url"]="https://foreign.example/segment";if(mode=="protected")json["drm"]=new JsonObject();
        if(mode=="init")json["video"]![0]!["init_segment"]=Convert.ToBase64String(new byte[16]);if(mode=="tracks")json.AsObject().Remove("audio");
        var text=json.ToJsonString();if(mode=="duplicate")text=text.Replace("\"base_url\":","\"clip_id\":\""+Clip+"\",\"base_url\":");
        try{var result=DashManifestParser.Parse(text,url,mode=="live");PlaylistCaptionChecks.Assert(mode=="valid"&&result.Representations.Count==2&&result.Representations.All(t=>t.InlineInitialization?.Length==16&&t.Segments.Count==1&&t.Segments[0].DurationSeconds==3));}
        catch(Exception e)when(mode!="valid"&&e is InvalidDataException or NotSupportedException or JsonException){}
        return Task.CompletedTask;
    }
    private static Task Nowness(string mode)
    {
        var node=JsonNode.Parse(HostingCollectionChecks.SeriesJson())!;var posts=node["posts"]!.AsArray();
        if(mode is "repeat" or "conflict")posts[1]=posts[0]!.DeepClone();if(mode=="conflict")posts[1]!["media"]![0]!["content"]="67891";
        if(mode=="same-video")posts[1]!["media"]![0]!["content"]="67890";if(mode=="unknown")posts[1]!["media"]![0]!["source"]="other-host";
        if(mode=="missing")posts[1]!["media"]![0]!.AsObject().Remove("source");if(mode=="preview")posts[1]!["isPublished"]=false;
        try{var page=NownessProvider.Parse(node.ToJsonString(),new(HostingCollectionChecks.Series));PlaylistCaptionChecks.Assert(mode is not ("missing" or "conflict")&&page.Entries.Count==2&&page.RevalidateCollectionMembership&&page.Entries.All(e=>e.CollectionMemberId is not null));if(mode=="repeat")PlaylistCaptionChecks.Assert(page.Entries[0].CollectionMemberId==page.Entries[1].CollectionMemberId);if(mode is "preview" or "unknown")PlaylistCaptionChecks.Assert(!page.Entries[1].Available);}
        catch(InvalidDataException)when(mode is "missing" or "conflict"){}
        return Task.CompletedTask;
    }
    private static Task Wistia(string mode)
    {
        var node=JsonNode.Parse(HostingCollectionChecks.WistiaJson(mode=="private"))!;var list=node["series"]![0]!["sections"]![0]!["episodes"]!;
        if(mode=="repeat-media"){list[1]!["hashedId"]="media00001";list[1]!["episodeMediaHashedId"]="media00001";}
        if(mode=="repeat-episode")list[1]!["channelEpisodeId"]="episode001";if(mode=="missing-episode")list[1]!.AsObject().Remove("channelEpisodeId");
        try{var page=WistiaProvider.ParseChannel(node.ToJsonString(),"channel123",10);PlaylistCaptionChecks.Assert(mode is "repeat-media" or "private"&&page.RevalidateCollectionMembership&&page.Entries.Count==2&&page.Entries[0].CollectionMemberId!=page.Entries[1].CollectionMemberId);if(mode=="private")PlaylistCaptionChecks.Assert(!page.Entries[1].Available);}
        catch(InvalidDataException)when(mode is "repeat-episode" or "missing-episode"){}
        return Task.CompletedTask;
    }
}
