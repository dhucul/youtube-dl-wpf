using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.ConsumerHosting;

internal static class ConsumerDashChecks
{
    internal const string BiUrl="https://www.businessinsider.com/fixture-video-2026-10",AjUrl="https://www.aljazeera.com/video/newsfeed/2026/10/11/fixture-video";
    public static IEnumerable<(string Name,Func<Task> Test)> All=>
    [
        ("Consumer DASH Business Insider resolves only its primary article video",()=>Parse("bi","valid")),
        ("Consumer DASH Al Jazeera resolves only its current post video",()=>Parse("aj","valid")),
        ("Consumer DASH foreign Business Insider article state fails",()=>Parse("bi","identity")),
        ("Consumer DASH foreign Al Jazeera post state fails",()=>Parse("aj","identity")),
        ("Consumer DASH foreign article origins cannot borrow matching paths",Origins),
        ("Consumer DASH publisher redirects cannot change consumer families",PublisherRedirect),
        ("Consumer DASH paid Business Insider pages cannot become public playback",()=>Parse("bi","paid")),
        ("Consumer DASH hidden articles cannot expose hosted video",()=>Parse("bi","hidden")),
        ("Consumer DASH missing primary hero video is an explicit refusal",()=>Parse("bi","missing")),
        ("Consumer DASH live Al Jazeera pages require their own workflow",()=>Parse("aj","live")),
        ("Consumer DASH broken primary video references fail",()=>Parse("aj","reference")),
        ("Consumer DASH duplicate matching article identities fail",()=>Parse("aj","duplicate")),
        ("Consumer DASH duplicate state fields fail without execution",()=>Parse("bi","duplicate")),
        ("Consumer DASH computed consumer state cannot execute",()=>Parse("bi","computed")),
        ("Consumer DASH bounded base64 state rejects malformed encoding",()=>Parse("aj","encoding")),
        ("Consumer DASH publisher host boundaries are exact",Hosts),
        ("Consumer DASH delegated issuer identity remains bound to the article",()=>Delegation(false)),
        ("Consumer DASH changed delegated identity fails before media",()=>Delegation(true)),
        ("Consumer DASH nested literal JSON braces remain data",Literal),
        ("Consumer DASH Akamai expiry hints retain their original signature",Expiry),
        ("Consumer DASH duplicate token expiry fields fail",BadExpiry),
        ("Consumer DASH expired segment graphs fail before resource requests",()=>Manifest("segment")),
        ("Consumer DASH expired initialization graphs fail before resource requests",()=>Manifest("init")),
        ("Consumer DASH malformed signatures remain issuer validated",()=>Manifest("future")),
        ("Consumer DASH failed initial graphs do not poison offered alternatives",Alternatives),
        ("Consumer DASH repeated expired graph renewal fails without cycling",Renewal)
    ];
    internal static void Assert(bool value){if(!value)throw new InvalidOperationException("Consumer/DASH expectation failed.");}
    internal static string Bi(string id="AbCd1234",bool paid=false,bool hidden=false)=>"<script id='fenrir-client-data'>const fenrirClientData="+JsonSerializer.Serialize(new{id="fixture-video-2026-10",path="/fixture-video-2026-10",type="post",meta=new{hasHeroVideo=true,hidden,paywall=new{isExempt=!paid}},relationships=new{video=new{data=new{attributes=new{title="Primary fixture"},meta=new{jwplayer=new{assetID=id}}}}},related=new{assetID="Other123"}})+";window.notExecuted();</script>";
    internal static string Aj(string id="67890",bool live=false)
    {
        var state=new JsonObject{["Post:1"]=new JsonObject{["link"]="/video/newsfeed/2026/10/11/fixture-video",["title"]="Primary fixture",["isLive"]=live,["video"]=new JsonObject{["__ref"]="Video:"+id}},["Video:"+id]=new JsonObject{["id"]=id,["accountId"]="12345",["playerId"]="default"},["Post:2"]=new JsonObject{["link"]="/video/related",["video"]=new JsonObject{["__ref"]="Video:999"}},["Video:999"]=new JsonObject{["id"]="999",["accountId"]="12345",["playerId"]="default"}};
        return Apollo(state);
    }
    private static string Apollo(JsonNode value)=>"<script>window.__APOLLO_STATE__=\""+Convert.ToBase64String(Encoding.UTF8.GetBytes(value.ToJsonString()))+"\";</script>";
    private static Task Parse(string site,string mode)
    {
        var html=site=="bi"?Bi(paid:mode=="paid",hidden:mode=="hidden"):Aj(live:mode=="live");
        if(mode=="identity")html=site=="bi"?html.Replace("fixture-video-2026-10","other-video-2026-10"):ChangeAj(html,n=>n["Post:1"]!["link"]="/video/other");
        if(mode=="origin")html=ChangeAj(html,n=>n["Post:1"]!["link"]="https://foreign.example"+new Uri(AjUrl).AbsolutePath);
        if(mode=="missing")html=html.Replace("\"hasHeroVideo\":true","\"hasHeroVideo\":false");
        if(mode=="reference")html=ChangeAj(html,n=>n.AsObject().Remove("Video:67890"));
        if(mode=="duplicate")html=site=="bi"?html.Replace("\"type\":\"post\"","\"type\":\"post\",\"type\":\"other\""):ChangeAj(html,n=>n["Post:3"]=n["Post:1"]!.DeepClone());
        if(mode=="computed")html=html.Replace("const fenrirClientData={","const fenrirClientData=execute({");if(mode=="encoding")html="<script>window.__APOLLO_STATE__=\"invalid==\";</script>";
        try{var page=ConsumerHostingProvider.Parse(html,new(site=="bi"?BiUrl:AjUrl));Assert(mode=="valid"&&page.EmbeddedPages.Count==1&&page.DelegatedArchiveIdentity is not null&&page.EmbeddedPages[0].AbsoluteUri.Contains(site=="bi"?"AbCd1234":"67890"));}
        catch(Exception e)when(mode!="valid"&&e is InvalidDataException or NotSupportedException or JsonException or ArgumentException){}
        return Task.CompletedTask;
    }
    private static string ChangeAj(string html,Action<JsonNode> edit){var encoded=html.Split('"')[1];var node=JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)))!;edit(node);return Apollo(node);}
    private static Task Hosts(){var provider=new ConsumerHostingProvider();Assert(provider.CanHandle(new(BiUrl))&&provider.CanHandle(new(AjUrl))&&!provider.CanHandle(new("https://www.businessinsider.com.foreign.example/article"))&&!provider.CanHandle(new("https://aljazeera.com:9443/article")));return Task.CompletedTask;}
    private static Task Origins(){foreach(var prefix in new[]{"https://foreign.example","https://www.www.aljazeera.com","https://www.aljazeera.com:9443","https://user@www.aljazeera.com","http://www.aljazeera.com"}){var html=ChangeAj(Aj(),n=>n["Post:1"]!["link"]=prefix+new Uri(AjUrl).AbsolutePath);try{ConsumerHostingProvider.Parse(html,new(AjUrl));throw new InvalidOperationException("Foreign article origin accepted.");}catch(InvalidDataException){}}return Task.CompletedTask;}
    private static async Task PublisherRedirect(){using var http=AdaptiveAccountChecks.Http((message,_)=>{var response=AdaptiveAccountChecks.Text(Aj());response.RequestMessage=new HttpRequestMessage(HttpMethod.Get,AjUrl);return Task.FromResult(response);});try{await new ConsumerHostingProvider().ExtractAsync(new(){Url=new(BiUrl),OutputDirectory="."},http,default);throw new InvalidOperationException("Publisher redirect accepted.");}catch(InvalidDataException){}}
    private static async Task Delegation(bool changed)
    {
        var media=0;var engine=new NativeEngine(httpFactory:_=>AdaptiveAccountChecks.Http((message,_)=>{if(message.RequestUri!.Host=="www.businessinsider.com")return Task.FromResult(AdaptiveAccountChecks.Text(Bi()));if(message.RequestUri.Host=="cdn.jwplayer.com"){Assert(message.Headers.Referrer?.AbsoluteUri==BiUrl);return Task.FromResult(AdaptiveAccountChecks.Text(SharedHostingChecks.Jw("https://cdn.example/clip.mp4",id:changed?"Other123":"AbCd1234")));}media++;throw new InvalidOperationException("Inspection attempted media.");}));
        try{var info=await engine.InspectAsync(new(){Url=new(BiUrl),OutputDirectory="."},default);Assert(!changed&&info.Id=="AbCd1234"&&media==0);}catch(InvalidDataException)when(changed){Assert(media==0);}
    }
    private static Task Literal(){var html=Bi().Replace("Primary fixture","Quoted } { fixture");Assert(ConsumerHostingProvider.Parse(html,new(BiUrl)).EmbeddedPages.Count==1);return Task.CompletedTask;}
    internal static string Token(bool expired)=>"__hdnea__="+Uri.EscapeDataString("st=1~exp="+(expired?"1":DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString())+"~acl=/media/*~hmac=publisher-signature");
    private static Task Expiry(){var url=new Uri("https://cdn.example/file.mp4?"+Token(true));Assert(PlaybackResourceExpiry.Read(url)==DateTimeOffset.FromUnixTimeSeconds(1)&&url.Query.Contains("publisher-signature"));return Task.CompletedTask;}
    private static Task BadExpiry(){SabrFixture.Throws<InvalidDataException>(()=>PlaybackResourceExpiry.Read(new("https://cdn.example/a?__hdnea__=exp%3D1~exp%3D2")));return Task.CompletedTask;}
    internal static string Mpd(string token="",string id="v")=>"<MPD xmlns='urn:mpeg:dash:schema:mpd:2011' mediaPresentationDuration='PT2S'><Period><AdaptationSet mimeType='video/mp4'><Representation id='"+id+"' height='72' bandwidth='1000' codecs='avc1.64001e'><SegmentTemplate timescale='1' duration='1' initialization='init.mp4"+(token.Length>0?"?"+token:"")+"' media='s$Number$.m4s"+(token.Length>0?"?"+token:"")+"'/></Representation></AdaptationSet></Period></MPD>";
    private static async Task Manifest(string mode)
    {
        var media=0;var xml=Mpd(Token(mode!="future"));if(mode=="segment")xml=xml.Replace("init.mp4?"+Token(true),"init.mp4");if(mode=="init")xml=xml.Replace("s$Number$.m4s?"+Token(true),"s$Number$.m4s");
        using var http=AdaptiveAccountChecks.Http((message,_)=>{if(!message.RequestUri!.AbsolutePath.EndsWith("mpd"))media++;return Task.FromResult(AdaptiveAccountChecks.Text(xml));});
        try{var manifest=await new DashDownloader(http).ReadAsync(new("https://cdn.example/manifest.mpd"),default);Assert(mode=="future"&&manifest.Representations[0].Segments[0].Url.Query.Contains("publisher-signature")&&media==0);}
        catch(ExpiredPlaybackResourcesException)when(mode!="future"){Assert(media==0);}
    }
    private static async Task Alternatives()
    {
        var roots=new PageMetadata{Id="1_fixture",Extractor="kaltura",Media=[new(){Id="dash-1",Url=new("https://cdn.example/old.mpd"),Kind=MediaKind.Dash},new(){Id="dash-2",Url=new("https://cdn.example/new.mpd"),Kind=MediaKind.Dash}]};
        using var owner=AdaptiveAccountChecks.Http((message,_)=>Task.FromResult(AdaptiveAccountChecks.Text(Mpd(message.RequestUri!.AbsolutePath=="/old.mpd"?Token(true):""))));using var lease=new RenewableMediaAuthorization(roots,_=>Task.FromResult(roots),readManifest:(url,ct)=>owner.GetDocumentAsync(url,1024*1024,ct));using var scoped=owner.WithAuthorization(lease);var reader=new DashDownloader(scoped);
        try{await reader.ReadAsync(roots.Media[0].Url,default);throw new InvalidOperationException("Expired manifest registered.");}catch(ExpiredPlaybackResourcesException){}
        var fresh=await reader.ReadAsync(roots.Media[1].Url,default);Assert(fresh.Representations.Count==1);var current=await lease.AuthorizeAsync(fresh.Representations[0].Segments[0].Url,default);Assert(current.Url.AbsolutePath=="/s1.m4s");
    }
    private static async Task Renewal()
    {
        var refresh=0;var initial=new PageMetadata{Id="fixture",Media=[new(){Id="dash",Url=new("https://cdn.example/root.mpd"),Kind=MediaKind.Dash}]};using var owner=AdaptiveAccountChecks.Http((message,_)=>Task.FromResult(AdaptiveAccountChecks.Text(Mpd())));
        using var lease=new RenewableMediaAuthorization(initial,_=>{refresh++;return Task.FromResult(initial);},readManifest:(url,ct)=>Task.FromResult(new MediaDocument(url,Mpd(Token(true)),"application/dash+xml")));using var scoped=owner.WithAuthorization(lease);var manifest=await new DashDownloader(scoped).ReadAsync(initial.Media[0].Url,default);
        try{await lease.RenewAsync(manifest.Representations[0].Segments[0].Url,1,default);throw new InvalidOperationException("Repeated stale resources were accepted.");}catch(ExpiredPlaybackResourcesException){Assert(refresh==1);}
    }
}
