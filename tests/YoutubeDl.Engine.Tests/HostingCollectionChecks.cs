using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Authentication;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Nowness;
using YoutubeDl.Engine.Providers.Wistia;
using YoutubeDl.Engine.Providers.JwPlatform;
using YoutubeDl.Engine.Providers.Brightcove;
internal static class HostingCollectionChecks
{
    internal const string Channel="https://fast.wistia.com/embed/channel/channel123?mode=playlist",Story="https://www.nowness.com/story/fixture-story",Series="https://www.nowness.com/series/fixture-series";
    public static IEnumerable<(string Name,Func<Task> Test)> All=>
    [
        ("Hosting collections Wistia ordered complete inventory omits playback leases",()=>Wistia("valid")),
        ("Hosting collections Wistia incomplete sections cannot return partial metadata",()=>Wistia("count")),
        ("Hosting collections Wistia additional sections fail explicitly",()=>Wistia("more")),
        ("Hosting collections Wistia repeated members fail",()=>Wistia("duplicate")),
        ("Hosting collections Wistia protected members retain unavailable positions",()=>Wistia("protected")),
        ("Hosting collections Wistia configured limits refuse full inventories",()=>Wistia("limit")),
        ("Hosting collections Wistia missing section arrays are not empty collections",()=>Wistia("sections")),
        ("Hosting collections Wistia missing access flags cannot grant public media",()=>Wistia("access")),
        ("Hosting collections Nowness primary English story delegates only main video",()=>Nowness("story")),
        ("Hosting collections Nowness series preserves hosted identity and source order",()=>Nowness("series")),
        ("Hosting collections Nowness foreign translated slugs fail",()=>Nowness("foreign")),
        ("Hosting collections Nowness unpublished previews cannot grant playback",()=>Nowness("preview")),
        ("Hosting collections Nowness repeated primary references fail",()=>Nowness("duplicate")),
        ("Hosting collections Nowness unsupported hosting does not guess identity",()=>Nowness("hosting")),
        ("Hosting collections Nowness complete series limits are enforced",()=>Nowness("limit")),
        ("Hosting collections Nowness unknown routes and publisher boundaries refuse",NownessHosts),
        ("Hosting collections Nowness selected hosted identity cannot switch",NownessDelegation),
        ("Hosting collections Nowness nested story routes verify series membership",NownessParent),
        ("Hosting collections Nowness preview parameters cannot be ignored",()=>Sync(()=>SabrFixture.Throws<NotSupportedException>(()=>NownessProvider.Address(new(Story+"?previewToken=private"))))),
        ("Hosting collections Wistia navigation parameters refuse before HTTP",()=>ChannelPreflight(false)),
        ("Hosting collections Wistia media credentials do not grant a channel",()=>ChannelPreflight(true)),
        ("Hosting collections JW pages retain all positions and materialized formats",()=>Paging("jw","valid")),
        ("Hosting collections JW foreign continuations receive no issuer credential",()=>Paging("jw","foreign")),
        ("Hosting collections JW repeated member positions retain independent leases",()=>Paging("jw","duplicate")),
        ("Hosting collections JW skipped offsets fail before another request",()=>Paging("jw","offset")),
        ("Hosting collections JW missing terminal pages fail without a partial list",()=>Paging("jw","truncated")),
        ("Hosting collections JW declared totals cannot end at a partial page",()=>Paging("jw","declared")),
        ("Hosting collections JW full inventory limits fail without truncation",()=>Paging("jw","limit")),
        ("Hosting collections JW page-specific publisher signing follows every page",()=>Paging("jw","signed")),
        ("Hosting collections JW opaque tokens cannot invent another page grant",()=>Paging("jw","opaque")),
        ("Hosting collections Brightcove count-bound paging preserves all positions",()=>Paging("bc","valid")),
        ("Hosting collections Brightcove changed collection counts fail",()=>Paging("bc","count")),
        ("Hosting collections Brightcove invalid totals cannot mean complete inventories",()=>Paging("bc","malformed")),
        ("Hosting collections Brightcove repeated member positions retain independent leases",()=>Paging("bc","duplicate")),
        ("Hosting collections Brightcove foreign next links fail before transfer",()=>Paging("bc","foreign")),
        ("Hosting collections Brightcove empty intermediate pages fail",()=>Paging("bc","truncated")),
        ("Hosting collections Brightcove complete count limits fail",()=>Paging("bc","limit")),
        ("Hosting collections paged failures and cancellation return no partial inventory",Cancellation),
        ("Hosting collections standalone parsers cannot label a slice complete",Slices)
    ];
    internal static void Assert(bool value){if(!value)throw new InvalidOperationException("Hosting collection expectation failed.");}
    internal static string WistiaJson(bool unavailable=false)=>JsonSerializer.Serialize(new{hashedId="channel123",source="channel",seriesCount=1,hasMoreSections=false,embedOptions=new{title="Channel fixture"},series=new[]{new{hashedId="series1234",sections=new[]{new{numericId=1,episodeCount=2,episodes=new[]{new{channelEpisodeId="episode001",episodeMediaHashedId="media00001",hashedId="media00001",type="Video",episodeTitle="First",durationInSeconds=3,requiresRegistration=false},new{channelEpisodeId="episode002",episodeMediaHashedId="media00002",hashedId="media00002",type="Video",episodeTitle="Second",durationInSeconds=3,requiresRegistration=unavailable}}}}}}});
    internal static string Post(string slug="fixture-story",string video="67890",int id=1)=>JsonSerializer.Serialize(new{id,type="video",status="published",isPublished=true,isComingSoon=false,contentDuration=3000,translations=new[]{new{language="en-us",slug,title="Story fixture"}},media=new[]{new{key="main",type="video",source="brightcove",content=video},new{key="related",type="video",source="brightcove",content="99999"}}});
    internal static string SeriesJson()=>JsonSerializer.Serialize(new{id=9,translations=new[]{new{language="en-us",slug="fixture-series",title="Series fixture"}},posts=new[]{JsonNode.Parse(Post()),JsonNode.Parse(Post("second-story","67891",2))}});
    private static Task Wistia(string mode)
    {
        var node=JsonNode.Parse(WistiaJson(mode=="protected"))!;if(mode=="count")node["series"]![0]!["sections"]![0]!["episodeCount"]=3;if(mode=="more")node["hasMoreSections"]=true;if(mode=="duplicate")node["series"]![0]!["sections"]![0]!["episodes"]![1]=node["series"]![0]!["sections"]![0]!["episodes"]![0]!.DeepClone();
        if(mode=="sections")node["series"]![0]!.AsObject().Remove("sections");if(mode=="access")node["series"]![0]!["sections"]![0]!["episodes"]![0]!.AsObject().Remove("requiresRegistration");
        try{var page=WistiaProvider.ParseChannel(node.ToJsonString(),"channel123",mode=="limit"?1:10);Assert(mode is "valid" or "protected"&&page.Entries.Count==2&&page.Entries.All(e=>e.ResolvedMedia is null&&e.ExpectedArchiveIdentity is not null)&&page.Entries[1].Available==(mode!="protected"));}catch(InvalidDataException)when(mode is "count" or "more" or "duplicate" or "limit" or "sections" or "access"){}
        return Task.CompletedTask;
    }
    private static Task Nowness(string mode)
    {
        var collection=mode is "series" or "limit";var node=JsonNode.Parse(collection?SeriesJson():Post())!;if(mode=="foreign")node["translations"]![0]!["slug"]="another-story";if(mode=="preview")node["isPublished"]=false;if(mode=="duplicate")node["media"]!.AsArray().Add(node["media"]![0]!.DeepClone());if(mode=="hosting")node["media"]![0]!["content"]="https://foreign.example/movie";
        try{var page=NownessProvider.Parse(node.ToJsonString(),new(collection?Series:Story),mode=="limit"?1:10);Assert(mode is "story" or "series"&&(collection?page.Entries.Count==2&&page.Entries.All(e=>e.ResolvedMedia is null&&e.ExpectedArchiveIdentity is not null):page.EmbeddedPages.Single().Host=="players.brightcove.net"&&page.DelegatedArchiveIdentity?.Id=="67890"));}catch(Exception e)when(mode is not ("story" or "series")&&e is InvalidDataException or NotSupportedException){}
        return Task.CompletedTask;
    }
    private static Task NownessHosts(){var provider=new NownessProvider();Assert(provider.CanHandle(new(Story))&&!provider.CanHandle(new("https://www.nowness.com.foreign.example/story/a")));SabrFixture.Throws<NotSupportedException>(()=>NownessProvider.Address(new("https://www.nowness.com/user/private-queue")));return Task.CompletedTask;}
    private static Task Sync(Action action){action();return Task.CompletedTask;}
    private static Task NownessParent(){var node=JsonNode.Parse(Post())!;node["series"]=new JsonArray(new JsonObject{["translations"]=new JsonArray(new JsonObject{["language"]="en-us",["slug"]="fixture-series"})});Assert(NownessProvider.Parse(node.ToJsonString(),new(Series+"/fixture-story")).EmbeddedPages.Count==1);SabrFixture.Throws<InvalidDataException>(()=>NownessProvider.Parse(node.ToJsonString(),new("https://www.nowness.com/series/foreign-series/fixture-story")));return Task.CompletedTask;}
    private static async Task ChannelPreflight(bool credentials){using var http=AdaptiveAccountChecks.Http((_,_)=>throw new InvalidOperationException("Preflight attempted HTTP."));try{await new WistiaProvider().ExtractAsync(new(){Url=new(Channel+(credentials?"":"&wmediaid=another001")),OutputDirectory=".",Authentication=credentials?new(){Hosting=SharedHostingChecks.Credential("wistia")}:new()},http,default);throw new InvalidOperationException("Channel access/navigation silently ignored.");}catch(NotSupportedException){}}
    private static async Task NownessDelegation()
    {
        var engine=new NativeEngine(httpFactory:_=>AdaptiveAccountChecks.Http((message,_)=>Task.FromResult(AdaptiveAccountChecks.Text(message.RequestUri!.Host=="api.nowness.com"?Post():message.RequestUri.Host=="players.brightcove.net"?"{\"account_id\":\"2385340575001\",\"video_cloud\":{\"policy_key\":\"policy\"}}":SharedHostingChecks.Brightcove("https://cdn.example/media.mp4").Replace("12345","2385340575001").Replace("67890","67891")))));
        try{await engine.InspectAsync(new(){Url=new(Story),OutputDirectory="."},default);throw new InvalidOperationException("Changed hosted identity accepted.");}catch(InvalidDataException){}
    }
    internal static string JwPage(int offset,bool duplicate=false,string? next=null)=>JsonSerializer.Serialize(new{feedid="List1234",title="JW collection fixture",links=new{next},playlist=new[]{JsonNode.Parse(SharedHostingChecks.Jw("https://cdn.example/"+offset+".mp4",id:duplicate?"Item0001":"Item000"+offset))!["playlist"]![0]}});
    internal static string BcPage(int offset,int count=2,bool duplicate=false,bool empty=false,string? next=null)=>JsonSerializer.Serialize(new{account_id="12345",id="99999",name="BC collection fixture",count,next_link=next,videos=empty?Array.Empty<JsonNode?>():new[]{JsonNode.Parse(SharedHostingChecks.Brightcove("https://cdn.example/"+offset+".mp4").Replace("67890",duplicate?"67890":(67890+offset).ToString()))}});
    private static async Task Paging(string provider,string mode)
    {
        var calls=0;var foreign=0;var root=provider=="jw"?"https://cdn.jwplayer.com/v2/playlists/List1234?page_limit=1":"https://players.brightcove.net/12345/default_default/index.html?playlistId=99999";
        using var http=AdaptiveAccountChecks.Http((message,_)=>{
            var uri=message.RequestUri!;if(uri.Host=="players.brightcove.net")return Task.FromResult(AdaptiveAccountChecks.Text("{\"account_id\":\"12345\",\"video_cloud\":{\"policy_key\":\"policy\"}}"));if(uri.Host is not ("cdn.jwplayer.com" or "edge.api.brightcove.com")){foreign++;throw new InvalidOperationException("Foreign issuer requested.");}
            calls++;var offset=provider=="jw"?calls:uri.Query.Contains("offset=0")?0:1;if(mode is "signed" or "opaque"){Assert(uri.Query.StartsWith("?token="));}
            var next=provider=="jw"&&offset==1?"https://cdn.jwplayer.com/v2/playlists/List1234?page_limit=1&page_offset=2":null;
            if(mode=="foreign"&&calls==1)next="https://foreign.example/playlists?offset=1";if(mode=="offset"&&calls==1)next=next!.Replace("offset=2","offset=9");
            string body=provider=="jw"?JwPage(offset,mode=="duplicate"&&calls>1,next):BcPage(offset,mode=="count"&&calls>1?3:2,mode=="duplicate"&&calls>1,mode=="truncated"&&calls>1,next);
            if(mode is "declared" or "malformed"){var declared=JsonNode.Parse(body)!;declared["count"]=mode=="declared"?JsonValue.Create(3):JsonValue.Create("unknown");body=declared.ToJsonString();}
            if(mode=="truncated"&&provider=="jw"&&calls==1){var node=JsonNode.Parse(body)!;node["links"]!["last"]="https://cdn.jwplayer.com/v2/playlists/List1234?page_limit=1&page_offset=3";body=node.ToJsonString();}return Task.FromResult(AdaptiveAccountChecks.Text(body));});
        var credentials=mode is "signed" or "opaque"?new NativeHostingCredentials{Provider=NativeHostingProvider.JwPlatform,MediaId="List1234",SigningKey=mode=="signed"?"publisher-fixture-secret":"",AccessToken=mode=="opaque"?"opaque-grant":""}:null;
        var request=new NativeDownloadRequest{Url=new(root),OutputDirectory=".",MaximumPlaylistEntries=mode=="limit"?1:10,Authentication=new(){Hosting=credentials}};
        try{var result=await (provider=="jw"?new JwPlatformProvider().ExtractAsync(request,http,default):new BrightcoveProvider().ExtractAsync(request,http,default));Assert(mode is "valid" or "signed" or "duplicate"&&result.Entries.Count==2&&result.Entries.Select(e=>e.Position).SequenceEqual(new[]{1,2})&&calls==2&&foreign==0&&result.Entries.All(e=>e.ResolvedMedia is not null));}
        catch(Exception e)when(mode is not ("valid" or "signed" or "duplicate")&&e is InvalidDataException or NotSupportedException){Assert(foreign==0);}
    }
    private static async Task Cancellation(){using var stop=new CancellationTokenSource();using var http=AdaptiveAccountChecks.Http((_,_)=>{stop.Cancel();return Task.FromResult(AdaptiveAccountChecks.Text(JwPage(1,next:"https://cdn.jwplayer.com/v2/playlists/List1234?page_limit=1&page_offset=2")));});try{await new JwPlatformProvider().ExtractAsync(new(){Url=new("https://cdn.jwplayer.com/v2/playlists/List1234?page_limit=1"),OutputDirectory="."},http,stop.Token);throw new InvalidOperationException("Cancelled list returned.");}catch(OperationCanceledException)when(stop.IsCancellationRequested){}}
    private static Task Slices(){SabrFixture.Throws<NotSupportedException>(()=>JwPlatformProvider.ParseDelivery(JwPage(1,next:"https://cdn.jwplayer.com/v2/playlists/List1234?page_offset=2"),new("List1234",true,new("https://cdn.jwplayer.com/v2/playlists/List1234"))));SabrFixture.Throws<NotSupportedException>(()=>BrightcoveProvider.ParsePlayback(BcPage(0),BrightcoveAddress.Parse(new("https://players.brightcove.net/12345/default_default/index.html?playlistId=99999"))));return Task.CompletedTask;}
}
