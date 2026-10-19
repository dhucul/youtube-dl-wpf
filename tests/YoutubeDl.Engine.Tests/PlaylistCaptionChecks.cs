using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Authentication;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;
using YoutubeDl.Engine.Providers.Nowness;
using YoutubeDl.Engine.Providers.Wistia;
internal static class PlaylistCaptionChecks
{
    internal const string NUrl="https://www.nowness.com/playlist/123/Fixture-list",WUrl="https://fast.wistia.com/embed/playlists/legacy1234",MediaId="media00002";
    internal const string Vtt="WEBVTT\n\n00:00:00.000 --> 00:00:01.200\nFirst fixture cue.\n\n00:00:01.500 --> 00:00:02.900\nSecond fixture cue.\n",Srt="1\n00:00:00,000 --> 00:00:01,200\nFirst fixture cue.\n\n2\n00:00:01,500 --> 00:00:02,900\nSecond fixture cue.\n";
    public static IEnumerable<(string Name,Func<Task> Test)> All=>
    [
        ("Playlist captions Nowness numeric routes retain optional mixed-case slugs",Routes),
        ("Playlist captions Nowness Vimeo identities never become Brightcove requests",()=>Hosting("vimeo","148162161","player.vimeo.com")),
        ("Playlist captions Nowness YouTube identities retain their declared provider",()=>Hosting("youtube","_jW-7ikH-cU","www.youtube.com")),
        ("Playlist captions Nowness missing provider names cannot guess numeric hosting",()=>Hosting("","148162161","")),
        ("Playlist captions Nowness private lists stop before member requests",()=>Nowness("private")),
        ("Playlist captions Nowness changed numeric identities stop before members",()=>Nowness("identity")),
        ("Playlist captions Nowness foreign slugs fail before members",()=>Nowness("slug")),
        ("Playlist captions Nowness missing public state cannot grant inventory",()=>Nowness("privacy")),
        ("Playlist captions Nowness complete empty public lists are preserved",()=>Nowness("empty")),
        ("Playlist captions Nowness configured item limits reject whole lists",()=>Nowness("limit")),
        ("Playlist captions Nowness paging reconstructs declared membership order",()=>Nowness("valid")),
        ("Playlist captions Nowness changed totals fail without a partial list",()=>Nowness("total")),
        ("Playlist captions Nowness missing page members fail",()=>Nowness("missing")),
        ("Playlist captions Nowness duplicate positions fail",()=>Nowness("position")),
        ("Playlist captions Nowness duplicate post identities fail",()=>Nowness("duplicate")),
        ("Playlist captions Nowness hosted media grants cannot grant a list",()=>Nowness("credentials")),
        ("Playlist captions Nowness cancellation cannot return a partial inventory",()=>Nowness("cancel")),
        ("Playlist captions legacy Wistia preserves complete metadata without stale playback",()=>Legacy("valid")),
        ("Playlist captions legacy missing configs retain unavailable positions",()=>Legacy("missing")),
        ("Playlist captions legacy protected members retain unavailable positions",()=>Legacy("protected")),
        ("Playlist captions legacy member config identities cannot change",()=>Legacy("identity")),
        ("Playlist captions legacy repeated sections fail",()=>Legacy("section")),
        ("Playlist captions legacy repeated members fail",()=>Legacy("duplicate")),
        ("Playlist captions legacy whole inventory limits refuse truncation",()=>Legacy("limit")),
        ("Playlist captions legacy unsupported member kinds fail",()=>Legacy("type")),
        ("Playlist captions legacy complete empty inventories are explicit",()=>Legacy("empty")),
        ("Playlist captions declared legacy embeds cannot become a single video",Embeds),
        ("Playlist captions computed legacy widget arguments cannot execute",Computed),
        ("Playlist captions current channel playlist tags delegate channel mode",ChannelTag),
        ("Playlist captions Wistia formats bind ISO and declared BCP47 languages",()=>Captions("valid")),
        ("Playlist captions transcript prose cannot become timed caption content",()=>Captions("text")),
        ("Playlist captions repeated caption languages fail",()=>Captions("duplicate")),
        ("Playlist captions caption language paths cannot escape the resource",()=>Captions("language")),
        ("Playlist captions conflicting caption aliases fail",()=>Captions("alias")),
        ("Playlist captions declared automatic tracks honor automatic-only selection",()=>Captions("auto")),
        ("Playlist captions caption API links cannot change media identity",()=>Captions("scope")),
        ("Playlist captions signed caption identities survive link generations",()=>Captions("identity")),
        ("Playlist captions protected public responses never expose assets",()=>Captions("protected")),
        ("Playlist captions API bearer stays on exact media and selected caption",()=>Api("valid")),
        ("Playlist captions unrequested API captions do not transfer",()=>Api("unrequested")),
        ("Playlist captions caption API denials do not fall back to guest",()=>Api("denied")),
        ("Playlist captions caption API HTML cannot become timed captions",()=>Api("html")),
        ("Playlist captions caption API redirects cannot change scoped resources",()=>Api("redirect")),
        ("Playlist captions caption language inventories are bounded",()=>Captions("limit"))
    ];
    internal static void Assert(bool value){if(!value)throw new InvalidOperationException("Playlist/caption expectation failed.");}
    internal static string Detail(int count=9)=>JsonSerializer.Serialize(new{id=123,type="memberPlaylist",isPrivate=false,videosCount=count,translations=new[]{new{language="en-us",slug="Fixture-list",title="Public fixture list"}}});
    internal static string Posts(int page,int total=9)
    {
        var start=(page-1)*8;var items=Enumerable.Range(start,Math.Min(8,Math.Max(0,total-start))).Select(index=>{var node=JsonNode.Parse(HostingCollectionChecks.Post("fixture-story-"+index,(67890+index).ToString(),index+1))!;node["index"]=total-1-index;return node;}).ToArray();
        return JsonSerializer.Serialize(new{items,meta=new{page,limit=8,totalItems=total,totalPages=total==0?0:(total+7)/8}});
    }
    internal static string LegacyJson()=>JsonSerializer.Serialize(new[]{new{id=1,name="Legacy fixture",medias=new[]{new{id=1,name="First",type="Video",embed_config=new{options=new{},media=new{mediaId=1,hashedId="media00001",duration=3,protected_=false,assets=Array.Empty<object>()}}},new{id=2,name="Second",type="Video",embed_config=new{options=new{},media=new{mediaId=2,hashedId=MediaId,duration=3,protected_=false,assets=Array.Empty<object>()}}}}}});
    internal static string Media(string url="https://cdn.example/video.mp4",string? captionUrl=null,bool api=false)=>api?JsonSerializer.Serialize(new{hashed_id=MediaId,name="Media fixture",type="Video",status="ready",duration=3,assets=new[]{new{type="mp4",url,container="mp4",height=72,width=128,fileSize=100}},captions=new[]{new{language="eng",url=captionUrl??"https://api.wistia.com/modern/medias/"+MediaId+"/captions/eng.vtt"}}}):JsonSerializer.Serialize(new{media=new{hashedId=MediaId,name="Media fixture",mediaType="Video",duration=3,assets=new[]{new{type="mp4",url,container="mp4",status=2,height=72,width=128,size=100}},captions=new[]{new{language="eng",text="Untimed transcript fixture",url=captionUrl}},availableTranscripts=new[]{new{wistiaLanguageCode="eng",bcp47LanguageTag="en",hasCaptions=true}}}});
    private static Task Routes(){Assert(NownessProvider.Address(new(NUrl)).Playlist=="123"&&NownessProvider.Address(new("https://www.nowness.com/playlist/123")).Slug=="");SabrFixture.Throws<NotSupportedException>(()=>NownessProvider.Address(new(NUrl+"?previewToken=private")));return Task.CompletedTask;}
    private static Task Hosting(string provider,string id,string host){var node=JsonNode.Parse(HostingCollectionChecks.Post())!;node["media"]![0]!["source"]=provider;node["media"]![0]!["content"]=id;try{var page=NownessProvider.Parse(node.ToJsonString(),new(HostingCollectionChecks.Story));Assert(host.Length>0&&page.EmbeddedPages.Single().Host==host&&page.DelegatedArchiveIdentity?.Id==id);}catch(InvalidDataException)when(host.Length==0){}return Task.CompletedTask;}
    private static async Task Nowness(string mode)
    {
        var calls=0;var memberCalls=0;using var stop=new CancellationTokenSource();using var http=AdaptiveAccountChecks.Http((message,_)=>{
            calls++;Assert(message.Headers.Authorization is null&&message.Headers.GetValues("X-Nowness-Language").Single()=="en-us");var url=message.RequestUri!;
            if(url.AbsolutePath.Contains("/playlist/")){var node=JsonNode.Parse(Detail(mode=="empty"?0:9))!;if(mode=="private")node["isPrivate"]=true;if(mode=="privacy")node.AsObject().Remove("isPrivate");if(mode=="identity")node["id"]=456;if(mode=="slug")node["translations"]![0]!["slug"]="other";return Task.FromResult(AdaptiveAccountChecks.Text(node.ToJsonString()));}
            memberCalls++;Assert(url.Query.Contains("PlaylistId=123")&&!url.Query.Contains("playlistId="));var page=url.Query.Contains("page=2")?2:1;var data=JsonNode.Parse(Posts(page,mode=="empty"?0:9))!;
            if(mode=="total"&&page==2)data["meta"]!["totalItems"]=10;if(mode=="missing"&&page==2)data["items"]!.AsArray().Clear();if(mode=="position"&&page==2)data["items"]![0]!["index"]=8;if(mode=="duplicate"&&page==2)data["items"]![0]!["id"]=1;if(mode=="cancel")stop.Cancel();return Task.FromResult(AdaptiveAccountChecks.Text(data.ToJsonString()));});
        var request=new NativeDownloadRequest{Url=new(NUrl),OutputDirectory=".",MaximumPlaylistEntries=mode=="limit"?8:100,Authentication=mode=="credentials"?new(){Hosting=SharedHostingChecks.Credential("brightcove")}:new()};
        try{var page=await new NownessProvider().ExtractAsync(request,http,stop.Token);Assert(mode is "valid" or "empty"&&page.Entries.Count==(mode=="empty"?0:9)&&(mode=="empty"||page.Entries[0].Id=="67898"&&page.Entries[^1].Id=="67890")&&page.Entries.All(e=>e.ResolvedMedia is null));}
        catch(Exception e)when(mode is not ("valid" or "empty")&&e is InvalidDataException or NotSupportedException or OperationCanceledException){if(mode is "private" or "identity" or "slug" or "privacy" or "limit" or "credentials")Assert(memberCalls==0);}
    }
    private static Task Legacy(string mode)
    {
        var node=JsonNode.Parse(LegacyJson())!;if(mode=="empty")node=new JsonArray();if(mode=="missing")node[0]!["medias"]![1]!.AsObject().Remove("embed_config");if(mode=="protected")node[0]!["medias"]![1]!["embed_config"]!["media"]!["protected"]=true;if(mode=="identity")node[0]!["medias"]![1]!["embed_config"]!["media"]!["mediaId"]=44;if(mode=="section")node.AsArray().Add(node[0]!.DeepClone());if(mode=="duplicate")node[0]!["medias"]![1]!["id"]=1;if(mode=="type")node[0]!["medias"]![1]!["type"]="Image";
        try{var page=WistiaProvider.ParseLegacyPlaylist(node.ToJsonString(),"legacy1234",mode=="limit"?1:10);Assert(mode is "valid" or "missing" or "protected" or "empty"&&page.Entries.Count==(mode=="empty"?0:2)&&page.Entries.All(e=>e.ResolvedMedia is null)&&(mode is not ("missing" or "protected")||!page.Entries[1].Available));}catch(InvalidDataException)when(mode is "identity" or "section" or "duplicate" or "limit" or "type"){}
        return Task.CompletedTask;
    }
    private static Task Embeds(){var page=new Uri("https://article.example/");Assert(EmbeddedMediaReader.Read("<div class='wistia_playlist wistia_async_legacy1234'></div><script>Wistia.playlist('legacy1234',{});</script>",page).Single().AbsolutePath=="/embed/playlists/legacy1234");return Task.CompletedTask;}
    private static Task Computed(){Assert(EmbeddedMediaReader.Read("<script>Wistia.playlist(resolve(),{});</script>",new("https://article.example/")).Count==0);return Task.CompletedTask;}
    private static Task ChannelTag(){Assert(EmbeddedMediaReader.Read("<wistia-playlist channel-id='channel123'></wistia-playlist>",new("https://article.example/")).Single().AbsolutePath=="/embed/channel/channel123");return Task.CompletedTask;}
    private static Task Captions(string mode)
    {
        var node=JsonNode.Parse(Media(captionUrl:mode=="identity"?"https://cdn.example/captions.vtt?expire=9999999999":null))!;var root=node["media"]!;
        if(mode=="duplicate")root["captions"]!.AsArray().Add(root["captions"]![0]!.DeepClone());if(mode=="language")root["captions"]![0]!["language"]="../escape";if(mode=="alias"){var second=root["captions"]![0]!.DeepClone();second["language"]="en";second["bcp47LanguageTag"]="en";root["captions"]!.AsArray().Add(second);}if(mode=="auto")root["captions"]![0]!["automatic"]=true;if(mode=="scope")root["captions"]![0]!["url"]="https://api.wistia.com/modern/medias/other00001/captions/eng.vtt";if(mode=="protected")root["protected"]=true;
        if(mode=="limit")root["captions"]=new JsonArray(Enumerable.Range(0,129).Select(i=>(JsonNode)new JsonObject{["language"]="lang"+i}).ToArray());
        try{var page=WistiaProvider.ParseMedia(node.ToJsonString(),MediaId);Assert(mode is "valid" or "text" or "auto" or "identity"&&page.Subtitles.All(t=>t.Language=="en"&&t.Content is null&&t.ResourceIdentity is not null));if(mode=="valid")Assert(page.Subtitles.Count==2&&page.Subtitles.Any(t=>t.Extension=="srt")&&page.Subtitles.Any(t=>t.Extension=="vtt"));if(mode=="auto")Assert(SubtitleSelection.Select(page.Subtitles,new(){Url=new("https://fast.wistia.com/embed/iframe/"+MediaId),OutputDirectory=".",WriteAutoSubs=true}).Count==1);if(mode=="identity"){var later=WistiaProvider.ParseMedia(node.ToJsonString().Replace("9999999999","9999999998"),MediaId);Assert(page.Subtitles[0].ResourceIdentity==later.Subtitles[0].ResourceIdentity);}}catch(Exception e)when(mode is "duplicate" or "language" or "alias" or "scope" or "protected" or "limit"&&e is InvalidDataException or NotSupportedException){}
        return Task.CompletedTask;
    }
    private static async Task Api(string mode)
    {
        var captionCalls=0;using var http=AdaptiveAccountChecks.Http((message,_)=>{
            var uri=message.RequestUri!;Assert(uri.Host=="api.wistia.com"&&message.Headers.Authorization?.Parameter=="publisher-secret");
            if(uri.AbsolutePath.EndsWith(".json"))return Task.FromResult(AdaptiveAccountChecks.Text(Media(api:true)));
            captionCalls++;var response=AdaptiveAccountChecks.Text(mode=="html"?"<html>not captions</html>":Vtt,mode=="denied"?HttpStatusCode.Forbidden:HttpStatusCode.OK);
            if(mode=="redirect")response.RequestMessage=new(HttpMethod.Get,"https://foreign.example/caption.vtt");return Task.FromResult(response);});
        var request=new NativeDownloadRequest{Url=new("https://fast.wistia.com/embed/iframe/"+MediaId),OutputDirectory=".",WriteSubs=mode!="unrequested",Authentication=new(){Hosting=new(){Provider=NativeHostingProvider.Wistia,MediaId=MediaId,AccessToken="publisher-secret"}}};
        try{var page=await new WistiaProvider().ExtractAsync(request,http,default);Assert(mode is "valid" or "unrequested"&&captionCalls==(mode=="unrequested"?0:1)&&page.Subtitles.Single().Content==(mode=="unrequested"?null:Vtt));}
        catch(Exception e)when(mode is "denied" or "html" or "redirect"&&e is HttpRequestException or InvalidDataException){Assert(captionCalls==1);}
    }
}
