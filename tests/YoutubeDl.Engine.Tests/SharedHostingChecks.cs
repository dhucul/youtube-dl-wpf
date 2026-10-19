using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Authentication;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.Brightcove;
using YoutubeDl.Engine.Providers.JwPlatform;
using YoutubeDl.Engine.Providers.Kaltura;
using YoutubeDl.Engine.Providers.Wistia;

internal static class SharedHostingChecks
{
    internal const string WistiaId="e4a27b971d",JwId="AbCd1234",BcId="67890",KId="1_fixture";
    public static IEnumerable<(string Name,Func<Task> Test)> All=>
    [
        ("Shared hosting typed credential scopes redact and preserve analysis arity",Options),
        ("Shared hosting scopes refuse different providers accounts and media",Scopes),
        ("Shared hosting credentials cannot blend with unscoped account fields",Blend),
        ("Shared hosting publisher signing uses exact JWT resource and HMAC",Signing),
        ("Shared hosting signed player aliases require the publisher signing capability",Player),
        ("Shared hosting Wistia API reads stay at the exact issuer",()=>Issuer("wistia")),
        ("Shared hosting JW token reads stay at the exact delivery resource",()=>Issuer("jwplatform")),
        ("Shared hosting Brightcove JWT stays inside the scoped playback API",()=>Issuer("brightcove")),
        ("Shared hosting Wistia foreign API identity fails",()=>Issuer("wistia","identity")),
        ("Shared hosting Brightcove wrong account fails before the token request",()=>Issuer("brightcove","account")),
        ("Shared hosting issuer redirects fail without forwarding tokens",()=>Issuer("wistia","redirect")),
        ("Shared hosting unready Wistia API media cannot be published",()=>Issuer("wistia","unready")),
        ("Shared hosting Wistia progressive links renew by stable format facts",()=>Renew("wistia")),
        ("Shared hosting JW progressive links renew by stable format facts",()=>Renew("jwplatform")),
        ("Shared hosting Brightcove progressive links renew by stable format facts",()=>Renew("brightcove")),
        ("Shared hosting reordered Wistia assets retain initial selector identities",()=>Renew("wistia","reorder")),
        ("Shared hosting changed encoded format facts refuse renewed media",()=>Renew("jwplatform","changed")),
        ("Shared hosting revoked media gets one bounded replay",()=>Renew("brightcove","denied")),
        ("Shared hosting canceled refresh sends no issuer or new media request",()=>Renew("wistia","cancel")),
        ("Shared hosting JW playlist snapshots carry selected-resource renewal",()=>Playlist("jwplatform")),
        ("Shared hosting Brightcove playlist snapshots carry selected-resource renewal",()=>Playlist("brightcove")),
        ("Shared hosting a disappeared selected playlist member fails",()=>Playlist("jwplatform",true)),
        ("Shared hosting generic delegation sends tokens only to the declared provider",()=>Delegate("valid")),
        ("Shared hosting unrelated embeds receive no declared provider credential",()=>Delegate("wrong")),
        ("Shared hosting ambiguous matching embeds fail before issuer requests",()=>Delegate("ambiguous")),
        ("Shared hosting redirected article referrers survive delegated renewal",()=>Delegate("redirect")),
        ("Shared hosting flat JW setup literals delegate without executing code",Literal),
        ("Shared hosting Kaltura password and OTP grants bind the partner account",()=>Kaltura("login")),
        ("Shared hosting Kaltura expired sessions relogin without reusing OTP",()=>Kaltura("expired")),
        ("Shared hosting Kaltura renewed user changes fail before playback",()=>Kaltura("switched")),
        ("Shared hosting supplied Kaltura sessions are scoped to API bodies",()=>Kaltura("supplied")),
        ("Shared hosting supplied expired sessions never fall back to anonymous login",()=>Kaltura("supplied-expired")),
        ("Shared hosting Kaltura partner changes fail before media",()=>Kaltura("partner")),
        ("Shared hosting Kaltura failed grants never use a guest session",()=>Kaltura("denied"))
        ,("Shared hosting observed Kaltura CDN redirects stay credential free and bounded",Redirect)
    ];
    internal static void Assert(bool condition){if(!condition)throw new InvalidOperationException("Shared hosting expectation failed.");}
    internal static Uri Url(string provider,bool list=false)=>new(provider switch{"wistia"=>"https://home.wistia.com/medias/"+WistiaId,"jwplatform"=>"https://cdn.jwplayer.com/v2/"+(list?"playlists/List1234":"media/"+JwId),"brightcove"=>"https://players.brightcove.net/12345/default_default/index.html?"+(list?"playlistId=99999":"videoId="+BcId),"kaltura"=>"https://cdnapisec.kaltura.com/p/309/embedPlaykitJs?entry_id="+KId,_=>throw new ArgumentException()});
    internal static NativeHostingCredentials Credential(string provider,bool list=false)=>new(){Provider=provider switch{"wistia"=>NativeHostingProvider.Wistia,"jwplatform"=>NativeHostingProvider.JwPlatform,"brightcove"=>NativeHostingProvider.Brightcove,"kaltura"=>NativeHostingProvider.Kaltura,_=>throw new ArgumentException()},MediaId=provider switch{"wistia"=>WistiaId,"jwplatform"=>list?"List1234":JwId,"brightcove"=>list?"99999":BcId,_=>KId},AccountId=provider is "brightcove"?"12345":provider is "kaltura"?"309":"",AccessToken="token-fixture"};
    internal static string Wistia(string url,int height=72,bool api=false,bool second=false,bool reverse=false)
    {
        var assets=new List<object>{new{type="hd_mp4_video",url,container="mp4",status=2,height,width=128,size=100,codec="h264",contentType="video/mp4"}};if(second)assets.Add(new{type="hd_mp4_video",url=url.Replace("one","other").Replace("two","other-two"),container="mp4",status=2,height=144,width=256,size=200,codec="h264",contentType="video/mp4"});if(reverse)assets.Reverse();
        return api?JsonSerializer.Serialize(new{hashed_id=WistiaId,status="ready",name="fixture",duration=1,type="Video",assets}):JsonSerializer.Serialize(new{media=new{hashedId=WistiaId,name="fixture",duration=1,mediaType="Video",assets}});
    }
    internal static string Jw(string url,int height=72,string id=JwId)=>JsonSerializer.Serialize(new{playlist=new[]{new{mediaid=id,title="fixture",duration=1,sources=new[]{new{file=url,type="video/mp4",height,width=128,filesize=100}},tracks=Array.Empty<object>()}}});
    internal static string Brightcove(string url,int height=72,string id=BcId)=>JsonSerializer.Serialize(new{account_id="12345",id,name="fixture",duration=1000,sources=new[]{new{src=url,type="video/mp4",height,width=128,size=100}},text_tracks=Array.Empty<object>()});
    private static NativeDownloadRequest Request(string provider,bool list=false)=>new(){Url=Url(provider,list),OutputDirectory=".",Authentication=new(){Hosting=Credential(provider,list)}};
    private static Task Options(){var request=NativeExtraArguments.Apply(new(){Url=new("https://article.example/"),OutputDirectory="."},NativeAnalysisArguments.Select(["--hosting-provider=brightcove","--hosting-account","12345","--hosting-media","67890","--hosting-access-token=token-fixture"]));Assert(request.Authentication.Hosting?.Provider==NativeHostingProvider.Brightcove&&!JsonSerializer.Serialize(request).Contains("token-fixture"));new NativeEngine().ValidateAuthenticationOptions(request);return Task.CompletedTask;}
    private static Task Scopes(){var engine=new NativeEngine();foreach(var provider in new[]{"wistia","jwplatform","brightcove","kaltura"}){engine.ValidateAuthenticationOptions(Request(provider));SabrFixture.Throws<NotSupportedException>(()=>engine.ValidateAuthenticationOptions(Request(provider) with {Url=new("https://www.youtube.com/watch?v=abcdefghijk")}));SabrFixture.Throws<NotSupportedException>(()=>engine.ValidateAuthenticationOptions(Request(provider) with {Authentication=new(){Hosting=Credential(provider) with {MediaId=provider=="kaltura"?"1_other":provider=="brightcove"?"1":provider=="wistia"?"abcdefghij":"Other123"}}}));}return Task.CompletedTask;}
    private static Task Blend(){SabrFixture.Throws<ArgumentException>(()=>new NativeEngine().ValidateAuthenticationOptions(Request("kaltura") with {Authentication=new(){Hosting=Credential("kaltura"),Username="user",Password="secret"}}));return Task.CompletedTask;}
    private static Task Signing(){var credential=Credential("jwplatform") with {AccessToken="",SigningKey="publisher-secret"};var url=HostingDeliverySigning.Jw(Url("jwplatform"),credential,1234);var token=Uri.UnescapeDataString(url.Query[7..]);var parts=token.Split('.');var expected=HMACSHA256.HashData(Encoding.UTF8.GetBytes("publisher-secret"),Encoding.ASCII.GetBytes(parts[0]+"."+parts[1]));Assert(Convert.ToBase64String(expected).TrimEnd('=').Replace('+','-').Replace('/','_')==parts[2]);using var payload=JsonDocument.Parse(Decode(parts[1]));Assert(payload.RootElement.GetProperty("resource").GetString()=="/v2/media/"+JwId&&payload.RootElement.GetProperty("exp").GetInt64()==5100);return Task.CompletedTask;}
    private static byte[] Decode(string value){value=value.Replace('-','+').Replace('_','/');return Convert.FromBase64String(value+new string('=',(4-value.Length%4)%4));}
    private static Task Player(){var url=new Uri("https://cdn.jwplayer.com/players/AbCd1234-Plyr5678.html?exp=1&sig=old");SabrFixture.Throws<NotSupportedException>(()=>JwPlatformProvider.ParseAddress(url));Assert(JwPlatformProvider.ParseAddress(url,true).Endpoint==Url("jwplatform"));return Task.CompletedTask;}
    private static async Task Issuer(string provider,string mode="valid")
    {
        var api=0;var foreign=0;using var http=AdaptiveAccountChecks.Http((message,_)=>{var url=message.RequestUri!;
            if(url.Host=="players.brightcove.net"){Assert(message.Headers.Authorization is null);return Task.FromResult(AdaptiveAccountChecks.Text("{\"account_id\":\""+(mode=="account"?"other":"12345")+"\",\"video_cloud\":{\"policy_key\":\"policy-fixture\"}}"));}
            if(url.Host=="foreign.example")foreign++;api++;
            if(provider=="jwplatform")Assert(url.Query=="?token=token-fixture"&&message.Headers.Authorization is null);else Assert(message.Headers.Authorization?.Parameter=="token-fixture");
            var json=provider=="wistia"?Wistia("https://cdn.example/one.mp4",api:true):provider=="jwplatform"?Jw("https://cdn.example/one.mp4"):Brightcove("https://cdn.example/one.mp4");
            if(mode=="identity")json=json.Replace(WistiaId,"other12345");if(mode=="unready")json=json.Replace("ready","processing");var response=AdaptiveAccountChecks.Text(json);if(mode=="redirect"){response.StatusCode=HttpStatusCode.Redirect;response.Headers.Location=new("https://foreign.example/steal");}return Task.FromResult(response);});
        try{var page=await Provider(provider).ExtractAsync(Request(provider),http,default);Assert(mode=="valid"&&page.Media.Count==1&&api==1&&page.PlaybackAuthorization is not null);}catch(Exception e)when(mode!="valid"&&e is InvalidDataException or NotSupportedException or HttpRequestException){Assert(foreign==0&&api==(mode=="account"?0:1));}
    }
    private static IMediaProvider Provider(string provider)=>provider switch{"wistia"=>new WistiaProvider(),"jwplatform"=>new JwPlatformProvider(),"brightcove"=>new BrightcoveProvider(),_=>new KalturaProvider()};
    private static async Task Renew(string provider,string mode="valid")
    {
        var issuers=0;var media=0;using var http=AdaptiveAccountChecks.Http((message,_)=>
        {
            if(message.RequestUri!.Host=="players.brightcove.net")return Task.FromResult(AdaptiveAccountChecks.Text("{\"account_id\":\"12345\",\"video_cloud\":{\"policy_key\":\"policy\"}}"));
            if(message.RequestUri.Host!="cdn.example"){issuers++;var url="https://cdn.example/"+(issuers==1?"one":"two")+".mp4";return Task.FromResult(AdaptiveAccountChecks.Text(provider=="wistia"?Wistia(url,api:true,second:mode=="reorder",reverse:issuers>1):provider=="jwplatform"?Jw(url,mode=="changed"&&issuers>1?144:72):Brightcove(url)));}
            media++;Assert(message.Headers.Authorization is null);return Task.FromResult(AdaptiveAccountChecks.Text("complete",mode=="denied"||message.RequestUri.AbsolutePath.Contains("one")?HttpStatusCode.Forbidden:HttpStatusCode.OK));
        });
        var page=await Provider(provider).ExtractAsync(Request(provider),http,default);using var scoped=http.WithAuthorization(page.PlaybackAuthorization!);using var stop=new CancellationTokenSource();if(mode=="cancel")stop.Cancel();
        try{var bytes=await scoped.GetBytesAsync(page.Media[0].Url,1024,stop.Token);Assert(Encoding.UTF8.GetString(bytes)=="complete"&&issuers==2&&media==2);if(mode=="reorder")Assert((await page.PlaybackAuthorization!.AuthorizeAsync(page.Media[0].Url,default)).Url.AbsolutePath=="/two.mp4");}
        catch(InvalidDataException)when(mode=="changed"){Assert(issuers==2&&media==1);}catch(HttpRequestException)when(mode=="denied"){Assert(issuers==2&&media==2);}catch(OperationCanceledException)when(mode=="cancel"){Assert(issuers==1&&media==0);}
    }
    private static async Task Playlist(string provider,bool missing=false)
    {
        var calls=0;using var http=AdaptiveAccountChecks.Http((message,_)=>
        {
            if(message.RequestUri!.Host=="players.brightcove.net")return Task.FromResult(AdaptiveAccountChecks.Text("{\"account_id\":\"12345\",\"video_cloud\":{\"policy_key\":\"policy\"}}"));
            if(message.RequestUri.Host=="cdn.example"){Assert(message.Headers.Authorization is null);return Task.FromResult(AdaptiveAccountChecks.Text("complete",message.RequestUri.AbsolutePath=="/one.mp4"?HttpStatusCode.Forbidden:HttpStatusCode.OK));}
            calls++;var json=provider=="jwplatform"?Jw("https://cdn.example/"+(calls==1?"one":"two")+".mp4",id:missing&&calls>1?"Other123":JwId):"{\"account_id\":\"12345\",\"id\":\"99999\",\"name\":\"list\",\"videos\":["+Brightcove("https://cdn.example/"+(calls==1?"one":"two")+".mp4")+"]}";return Task.FromResult(AdaptiveAccountChecks.Text(json));
        });var page=await Provider(provider).ExtractAsync(Request(provider,true),http,default);Assert(calls==1&&page.Entries[0].ExpectedArchiveIdentity is not null);var item=page.Entries[0].ResolvedMedia!;using var scoped=http.WithAuthorization(item.PlaybackAuthorization!);
        try{await scoped.GetBytesAsync(item.Media[0].Url,1024,default);Assert(!missing&&calls==2);}catch(InvalidDataException)when(missing){Assert(calls==2);}
    }
    private static async Task Delegate(string mode)
    {
        var api=0;var request=Request("wistia") with {Url=new("https://article.example/page")};var engine=new NativeEngine(httpFactory:_=>AdaptiveAccountChecks.Http((message,_)=>
        {
            if(message.RequestUri!.Host=="article.example"){Assert(message.Headers.Authorization is null);var src=mode=="wrong"?"https://fast.wistia.com/embed/iframe/other12345":Url("wistia").AbsoluteUri;var response=AdaptiveAccountChecks.Text("<iframe src='"+src+"'></iframe>"+(mode=="ambiguous"?"<iframe src='https://fast.wistia.com/embed/iframe/"+WistiaId+"'></iframe>":"")+"<video src='https://unrelated.example/trailer.mp4'></video>");if(mode=="redirect")response.RequestMessage=new(HttpMethod.Get,"https://final.example/article");return Task.FromResult(response);}
            api++;Assert(message.RequestUri.Host=="api.wistia.com"&&message.Headers.Authorization?.Parameter=="token-fixture");return Task.FromResult(AdaptiveAccountChecks.Text(Wistia("https://cdn.example/one.mp4",api:true)));
        }));try{var info=await engine.InspectAsync(request,default);Assert(mode is "valid" or "redirect"&&info.Id==WistiaId&&api==1);}catch(InvalidDataException)when(mode is "wrong" or "ambiguous"){Assert(api==0);}
    }
    private static Task Literal(){var html="<script>jwplayer('p').setup({playlist:'https://cdn.jwplayer.com/v2/media/AbCd1234?token=literal',width:640});</script>";var urls=EmbeddedMediaReader.Read(html,new("https://article.example/"));Assert(urls.Count==1&&urls[0].Query=="?token=literal");Assert(EmbeddedMediaReader.Read(html.Replace("'https://cdn.jwplayer.com/v2/media/AbCd1234?token=literal'","execute()"),new("https://article.example/")).Count==0);return Task.CompletedTask;}
    internal static string KMedia=>"{\"objectType\":\"KalturaMediaEntry\",\"id\":\"1_fixture\",\"partnerId\":309,\"mediaType\":1,\"name\":\"fixture\",\"duration\":1}";
    internal static string KContext=>"{\"objectType\":\"KalturaPlaybackContext\",\"actions\":[],\"sources\":[{\"format\":\"url\",\"flavorIds\":\"1_low\",\"url\":\"https://cdn.example/media.mp4\",\"drm\":[]}],\"flavorAssets\":[{\"objectType\":\"KalturaFlavorAsset\",\"id\":\"1_low\",\"entryId\":\"1_fixture\",\"partnerId\":309,\"status\":2,\"fileExt\":\"mp4\",\"height\":72,\"width\":128,\"videoCodecId\":\"avc1\"}],\"playbackCaptions\":[]}";
    private static async Task Kaltura(string mode)
    {
        var logins=0;var guests=0;var media=0;using var http=AdaptiveAccountChecks.Http(async(message,ct)=>
        {
            Assert(message.RequestUri!.Host=="www.kaltura.com"&&message.Method==HttpMethod.Post);using var doc=JsonDocument.Parse(await message.Content!.ReadAsStringAsync(ct));var root=doc.RootElement;var action=Path.GetFileName(message.RequestUri.AbsolutePath);
            if(action=="startWidgetSession"){guests++;return AdaptiveAccountChecks.Text("{}");}
            if(action=="loginByLoginId"){logins++;Assert(root.GetProperty("password").GetString()=="password-fixture"&&root.TryGetProperty("otp",out _)==(logins==1));return AdaptiveAccountChecks.Text(mode=="denied"?"{\"objectType\":\"KalturaAPIException\"}":"\"ks-"+logins+"\"");}
            if(action=="get"&&message.RequestUri.AbsolutePath.Contains("/session/"))return AdaptiveAccountChecks.Text(JsonSerializer.Serialize(new{partnerId=mode=="partner"?310:309,userId=mode=="switched"&&logins>1?"other-user":"user-fixture",type=0,expiry=DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}));
            if(action=="get"){media++;if(mode is "expired" or "switched" or "supplied-expired"&&media==1)return AdaptiveAccountChecks.Text("{\"objectType\":\"KalturaAPIException\",\"code\":\"INVALID_KS\"}");return AdaptiveAccountChecks.Text(KMedia);}
            if(action=="getPlaybackContext")return AdaptiveAccountChecks.Text(KContext);Assert(action=="getUrl");return AdaptiveAccountChecks.Text("\"https://cdn.example/media.mp4\"");
        });var request=new NativeDownloadRequest{Url=Url("kaltura"),OutputDirectory=".",Authentication=mode.StartsWith("supplied")?new(){Hosting=Credential("kaltura")}:new(){Username="user-fixture",Password="password-fixture",TwoFactor="123456"}};
        try{var page=await new KalturaProvider().ExtractAsync(request,http,default);Assert(mode is "login" or "expired" or "supplied"&&page.Media.Count==1&&guests==0&&logins==(mode=="supplied"?0:mode=="expired"?2:1));}
        catch(Exception e)when(mode is "switched" or "supplied-expired" or "partner" or "denied"&&e is InvalidDataException or NotSupportedException or InvalidOperationException){Assert(guests==0&&logins==(mode=="supplied-expired"?0:mode=="switched"?2:1));}
    }
    private static async Task Redirect()
    {
        foreach(var foreign in new[]{false,true})
        {
            var requests=0;using var owner=AdaptiveAccountChecks.Http((message,_)=>{requests++;Assert(message.Headers.Authorization is null);if(message.RequestUri!.Host=="cdnapisec.kaltura.com"){var reply=AdaptiveAccountChecks.Text("",HttpStatusCode.Redirect);reply.Headers.Location=new("https://"+(foreign?"foreign.akamaihd.net":"kalhlspd-a.akamaihd.net")+"/media.mp4");return Task.FromResult(reply);}return Task.FromResult(AdaptiveAccountChecks.Text("complete"));});
            var page=new PageMetadata{Id=KId,Extractor="kaltura",Media=[new(){Id="file",Url=new("https://cdnapisec.kaltura.com/media.mp4"),Kind=MediaKind.Progressive,Extension="mp4"}]};using var session=new ProviderPlaybackSession(owner,Request("kaltura"),"kaltura",(_,_)=>Task.FromResult(page),true);var bound=session.BindInitial(page);using var scoped=owner.WithAuthorization(bound.PlaybackAuthorization!);
            try{await scoped.GetBytesAsync(page.Media[0].Url,1024,default);Assert(!foreign&&requests==2);}catch(InvalidDataException)when(foreign){Assert(requests==1);}
        }
    }
}
