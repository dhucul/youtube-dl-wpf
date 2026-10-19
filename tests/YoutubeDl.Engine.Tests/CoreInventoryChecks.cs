using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Authentication;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.YouTube;
using YoutubeDl.Engine.Providers.Vimeo;

internal static class CoreInventoryChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>new[]{"query","plus","filter","duplicate","unknown","empty","fragment","music"}.Select(m=>("Core inventories search address "+m,(Func<Task>)(()=>SearchAddress(m))))
        .Concat(new[]{"album","api-album","channel","group","user","likes","watchlater","vanity","zero","query","video","foreign"}.Select(m=>("Core inventories Vimeo address "+m,(Func<Task>)(()=>VimeoAddressCase(m)))))
        .Concat(new[]{"page","modern","pagination","duplicate","empty","target","repeat","limit","version","unknown","redirect","cancel","oauth","oauth-filter","api-kind","api-repeat","oauth-refresh","cookie"}.Select(m=>("Core inventories YouTube "+m,(Func<Task>)(()=>Search(m)))))
        .Concat(new[]{"album","api-album","channel","group","user","likes","watchlater","vanity","empty","duplicate","total","short","identity","connection","foreign","query","repeated-page","unlisted","unavailable","credentials","oauth","cancel","owner","me-redirect","numeric-owner"}.Select(m=>("Core inventories Vimeo "+m,(Func<Task>)(()=>Vimeo(m)))));
    internal static void Check(bool value,string message="Core inventory contract differs."){if(!value)throw new InvalidOperationException(message);}
    private static Task SearchAddress(string mode)
    {
        var url=mode switch{"plus"=>"https://m.youtube.com/results?search_query=a%2Bb+c","filter"=>"https://www.youtube.com/results?search_query=bird&sp=EgIQAQ%3D%3D","duplicate"=>"https://youtube.com/results?search_query=x&search_query=y","unknown"=>"https://youtube.com/results?search_query=x&page=2","empty"=>"https://youtube.com/results?search_query=","fragment"=>"https://youtube.com/results?search_query=x#next","music"=>"https://music.youtube.com/results?search_query=x",_=>"https://youtube.com/results?search_query=caf%C3%A9"};var invalid=mode is "duplicate" or "unknown" or "empty" or "fragment";try{var supported=YouTubeSearchAddress.TryParse(new(url),out var address);Check(supported==(mode!="music"));if(supported){Check(!invalid);Check(address!.Query==(mode=="plus"?"a+b c":mode=="filter"?"bird":"café"));}}catch(ArgumentException)when(invalid){}return Task.CompletedTask;
    }
    internal static string Source(string mode)=>mode switch{"api-album"=>"https://api.vimeo.com/users/7/albums/42","channel"=>"https://vimeo.com/channels/native","group"=>"https://vimeo.com/groups/native/videos","user"=>"https://vimeo.com/user7/videos","likes"=>"https://vimeo.com/user7/likes","watchlater"=>"https://vimeo.com/watchlater","vanity"=>"https://vimeo.com/native/videos",_=>"https://vimeo.com/showcase/42"};
    private static Task VimeoAddressCase(string mode)
    {
        var url=mode switch{"zero"=>"https://vimeo.com/showcase/000","query"=>Source("album")+"?page=2","video"=>"https://vimeo.com/12345","foreign"=>"https://vimeo.com.foreign.example/channels/native",_=>Source(mode)};var invalid=mode is "zero" or "query";try{var supported=VimeoCollectionAddress.TryParse(new(url),out var address);Check(supported==(mode is not ("video" or "foreign")));if(supported)Check(!invalid&&address!.Connection==(mode is "likes" or "watchlater"?mode:"videos"));}catch(ArgumentException)when(invalid){}return Task.CompletedTask;
    }
    internal static readonly string[] Ids=["aaaaaaaaaaa","bbbbbbbbbbb","ccccccccccc"];
    internal static object Video(string id)=>new{videoRenderer=new{videoId=id,title=new{simpleText="Video "+id},lengthText=new{simpleText="1:02"},menu=new{videoRenderer=new{videoId="zzzzzzzzzzz"}}}};
    internal static string SearchHtml(object[] items,string? token=null,bool version=true)
    {
        var list=items.ToList();if(token is not null)list.Add(Next(token));var json=JsonSerializer.Serialize(new{contents=new{twoColumnSearchResultsRenderer=new{primaryContents=new{sectionListRenderer=new{targetId="search-feed",contents=new object[]{new{itemSectionRenderer=new{contents=list}}}}},secondaryContents=new{videoRenderer=new{videoId="zzzzzzzzzzz"}}}}});return "<script>ytInitialData="+json+";</script>"+(version?"<script>ytcfg.set({\"INNERTUBE_CLIENT_VERSION\":\"fixture.search\"});</script>":"");
    }
    internal static object Next(string token)=>new{continuationItemRenderer=new{continuationEndpoint=new{continuationCommand=new{token}}}};
    internal static string SearchNext(object[] items,string? token=null,string target="search-feed")
    {var list=items.ToList();if(token is not null)list.Add(Next(token));return JsonSerializer.Serialize(new{onResponseReceivedCommands=new[]{new{appendContinuationItemsAction=new{targetId=target,continuationItems=new object[]{new{itemSectionRenderer=new{contents=list}}}}}}});}
    private static async Task Search(string mode)
    {
        var calls=0;var refused=false;using var stop=new CancellationTokenSource();var oauth=mode is "oauth" or "oauth-filter" or "api-kind" or "api-repeat" or "oauth-refresh";var jar=new CookieContainer();if(mode=="cookie")jar.Add(new Cookie("SAPISID","fixture-cookie","/",".youtube.com"){Secure=true});var invalid=mode is "target" or "repeat" or "limit" or "version" or "unknown" or "redirect" or "cancel" or "oauth-filter" or "api-kind" or "api-repeat";
        using var http=AdaptiveAccountChecks.Http(async(message,ct)=>
        {
            calls++;if(mode=="cancel"){stop.Cancel();ct.ThrowIfCancellationRequested();}
            if(oauth)
            {
                if(message.RequestUri!.Host=="oauth2.googleapis.com"){Check(mode=="oauth-refresh"&&message.Headers.Authorization is null);return Json(JsonSerializer.Serialize(new{access_token="renewed-token",token_type="Bearer",expires_in=3600,scope=NativeOAuthSession.RequiredScopes(NativeOAuthProvider.YouTube)}));}Check(message.Headers.Authorization?.Parameter is "fixture-token" or "renewed-token");if(message.RequestUri!.Host=="openidconnect.googleapis.com")return Json("{\"sub\":\"account-fixture\"}");Check(message.RequestUri.Host=="www.googleapis.com"&&message.RequestUri.AbsolutePath=="/youtube/v3/search"&&message.RequestUri.Query.Contains("type=video"));if(mode=="oauth-refresh"&&!refused){refused=true;return new HttpResponseMessage(HttpStatusCode.Unauthorized);}var second=message.RequestUri.Query.Contains("pageToken=");return Json(JsonSerializer.Serialize(new{items=new[]{new{id=new{kind=mode=="api-kind"?"youtube#channel":"youtube#video",videoId=Ids[second?1:0]},snippet=new{title="API result"}}},nextPageToken=!second||mode=="api-repeat"?"next":null}));
            }
            if(mode=="cookie"&&message.Method==HttpMethod.Post){Check(message.Headers.Authorization?.Scheme=="SAPISIDHASH"&&message.Headers.GetValues("X-Goog-AuthUser").Single()=="1");}else Check(message.Headers.Authorization is null,"Anonymous search received credentials.");if(message.Method==HttpMethod.Post){using var body=JsonDocument.Parse(await message.Content!.ReadAsStringAsync(ct));Check(body.RootElement.GetProperty("continuation").GetString()=="next"&&body.RootElement.GetProperty("context").GetProperty("client").GetProperty("clientVersion").GetString()==(mode=="cookie"?"fixture.web":"fixture.search"));return Json(SearchNext([Video(Ids[1])],mode=="repeat"?"next":null,mode=="target"?"sidebar":"search-feed"));}
            object[] items=mode=="empty"?[]:mode=="unknown"?[new{unknownPrimaryRenderer=new{videoId=Ids[0]}}]:mode=="modern"?[new{lockupViewModel=new{contentId=Ids[0],contentType="LOCKUP_CONTENT_TYPE_VIDEO",metadata=new{lockupMetadataViewModel=new{title=new{content="Modern"}}}}}]:mode=="duplicate"?[Video(Ids[0]),Video(Ids[0])]:[new{channelRenderer=new{channelId="ignored"}},Video(Ids[0]),new{shelfRenderer=new{contents=new[]{Video(Ids[2])}}}];var response=Json(SearchHtml(items,mode is "pagination" or "target" or "repeat" or "limit" or "version" or "cookie"?"next":null,mode!="version"));if(mode=="cookie")response=Json(await response.Content.ReadAsStringAsync(ct)+AccountRenewalChecks.Config);if(mode=="redirect")response.RequestMessage=new(HttpMethod.Get,"https://www.youtube.com/results?search_query=other");return response;
        },jar);
        var request=new NativeDownloadRequest{Url=new("https://www.youtube.com/results?search_query=native"+(mode=="oauth-filter"?"&sp=EgIQAQ%3D%3D":"")),OutputDirectory=".",Retries=0,MaximumPlaylistEntries=mode=="limit"?1:10,Authentication=oauth?new(){OAuth=new(){Provider=NativeOAuthProvider.YouTube,AccessToken="fixture-token",ClientId=mode=="oauth-refresh"?"fixture-client":"",RefreshToken=mode=="oauth-refresh"?"fixture-refresh":""}}:mode=="cookie"?new(){YouTubeAccountIndex=1}:new()};
        try{var page=await new YouTubeProvider().ExtractAsync(request,http,stop.Token);Check(!invalid&&page.IsPlaylist&&page.RevalidateCollectionMembership&&page.Media.Count==0);Check(page.Entries.Count==(mode=="empty"?0:mode is "pagination" or "oauth" or "oauth-refresh" or "cookie"?2:1));Check(page.Entries.All(e=>e.Url!.Host=="www.youtube.com"&&e.Id!="zzzzzzzzzzz"&&e.ResolvedMedia is null));}catch(Exception e)when(invalid&&e is InvalidDataException or NotSupportedException or OperationCanceledException){if(mode=="oauth-filter")Check(calls==0);return;}Check(!invalid);
    }
    internal static string VimeoMetadata(string kind,int total=3,string? identity=null,string? connectionOverride=null)
    {
        var connection=kind is "likes" or "watchlater"?kind:"videos";var canonical=identity??(kind is "channel" or "group"?"/"+(kind=="channel"?"channels":"groups")+"/99":kind is "user" or "likes" or "watchlater" or "vanity"?"/users/7":"/users/7/albums/42");var link=kind is "channel" or "group"?"https://vimeo.com/"+(kind=="channel"?"channels":"groups")+"/native":kind is "user" or "likes" or "watchlater"?"https://vimeo.com/user7":kind=="vanity"?"https://vimeo.com/native":"https://vimeo.com/showcase/42";
        return JsonSerializer.Serialize(new{uri=canonical,name="Native collection",link,metadata=new{connections=new Dictionary<string,object>{{connection,new{uri=connectionOverride??canonical+"/"+connection,total}}}}});
    }
    internal static object VimeoRow(string id,string? status=null,bool unlisted=false)=>new{uri="/videos/"+id,name="Video "+id,duration=.6,status=status??"available",link="https://vimeo.com/"+id+(unlisted?"/privateHash":""),files=new[]{new{link="https://must-never-fetch.example/stale.mp4"}}};
    internal static string VimeoPage(string path,int page,object[] rows,int total=3,string? next=null)=>JsonSerializer.Serialize(new{total,page,per_page=100,data=rows,paging=new{next=next??(page==1&&total>rows.Length?path+"?per_page=100&page=2":null)}});
    private static async Task Vimeo(string mode)
    {
        var kind=mode=="numeric-owner"?"user":new[]{"album","api-album","channel","group","user","likes","watchlater","vanity"}.Contains(mode)?mode:"album";VimeoCollectionAddress.TryParse(new(Source(kind)),out var address);var canonical=kind is "channel" or "group"?"/"+(kind=="channel"?"channels":"groups")+"/99":kind is "user" or "likes" or "watchlater" or "vanity"?"/users/7":"/users/7/albums/42";var path=canonical+"/"+address!.Connection;var total=mode=="empty"?0:3;var calls=0;using var stop=new CancellationTokenSource();var invalid=mode is "duplicate" or "total" or "short" or "identity" or "connection" or "foreign" or "query" or "repeated-page" or "credentials" or "cancel" or "owner" or "me-redirect" or "numeric-owner";
        using var http=AdaptiveAccountChecks.Http((message,ct)=>
        {
            calls++;Check(message.RequestUri!.Host=="api.vimeo.com"&&message.Headers.Authorization?.Parameter=="fixture-token");if(mode=="cancel"){stop.Cancel();ct.ThrowIfCancellationRequested();}if(message.RequestUri.AbsolutePath=="/me"&&address.MetadataPath!="/me"){var account=Json("{\"uri\":\"/users/7\"}");if(mode=="me-redirect")account.RequestMessage=new(HttpMethod.Get,"https://api.vimeo.com/users/8");return Task.FromResult(account);}
            if(message.RequestUri.AbsolutePath==address.MetadataPath)return Task.FromResult(Json(VimeoMetadata(kind,total,mode=="identity"?"/users/7/albums/43":mode=="owner"?"/users/8/albums/42":mode=="numeric-owner"?"/users/8":null,mode=="connection"?"/users/8/albums/42/videos":null)));
            Check(message.RequestUri.AbsolutePath==path);var second=message.RequestUri.Query.Contains("page=2");var rows=mode=="empty"?Array.Empty<object>():second?new[]{VimeoRow(mode=="duplicate"?"101":"103")}:new[]{VimeoRow("101",mode=="unavailable"?"uploading":null,mode=="unlisted"),VimeoRow("102")};var next=mode=="foreign"?"https://foreign.example/leak?page=2":mode=="query"?path+"?page=2&token=leak":mode=="repeated-page"?path+"?page=1":null;var json=VimeoPage(path,second?2:1,rows,mode=="total"?4:total,next);if(mode=="short"){var node=System.Text.Json.Nodes.JsonNode.Parse(json)!;node["paging"]!["next"]=null;json=node.ToJsonString();}return Task.FromResult(Json(json));
        });var request=new NativeDownloadRequest{Url=new(Source(kind)),OutputDirectory=".",Retries=0,Authentication=mode=="credentials"?new():mode=="oauth"?new(){OAuth=new(){Provider=NativeOAuthProvider.Vimeo,AccessToken="fixture-token"}}:new(){AccessToken="fixture-token"}};
        try{var page=await new VimeoProvider().ExtractAsync(request,http,stop.Token);Check(!invalid&&page.IsPlaylist&&page.RevalidateCollectionMembership&&page.Media.Count==0&&page.Entries.Count==total);Check(page.Entries.All(e=>e.ResolvedMedia is null&&e.ExpectedArchiveIdentity?.Extractor=="vimeo"));if(mode=="unlisted")Check(page.Entries[0].Url!.AbsolutePath=="/101/privateHash");if(mode=="unavailable")Check(!page.Entries[0].Available);}
        catch(Exception e)when(invalid&&e is InvalidDataException or NotSupportedException or OperationCanceledException){if(mode=="credentials")Check(calls==0);return;}Check(!invalid);
    }
    internal static HttpResponseMessage Json(string value)=>new(HttpStatusCode.OK){Content=new StringContent(value)};
}
