using System.Net;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Authentication;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

internal static class AdaptiveAccountChecks
{
    public static IEnumerable<(string Name,Func<Task> Test)> All=>
    [
        ("Adaptive account HLS segment renewal preserves canonical identity",()=>Graph("hls","segment")),
        ("Adaptive account HLS key renewal stays in the declared encryption graph",()=>Graph("hls","key")),
        ("Adaptive account HLS initialization and range facts survive renewal",()=>Graph("hls","init")),
        ("Adaptive account HLS relative signed prefixes resolve against the effective manifest",()=>Graph("hls","prefix")),
        ("Adaptive account master playlists renew selected video audio and captions together",()=>Graph("master","concurrent")),
        ("Adaptive account HLS changed timing fails before new media",()=>Graph("hls","changed")),
        ("Adaptive account missing static resources cannot become partial success",()=>Graph("hls","missing")),
        ("Adaptive account live windows renew present segments and refuse retired ones",()=>Graph("live","window")),
        ("Adaptive account DASH template resource renewal retains representation identity",()=>Graph("dash","segment")),
        ("Adaptive account live DASH period graphs retain declared timing identities",()=>Graph("periods","segment")),
        ("Adaptive account DASH initialization renewal retains byte range identity",()=>Graph("dash","init")),
        ("Adaptive account DASH captions share the declared signed graph",()=>Graph("dash","caption")),
        ("Adaptive account indexed DASH file renewal preserves index and init facts",()=>Graph("indexed","segment")),
        ("Adaptive account DASH representation changes stop renewal",()=>Graph("dash","changed")),
        ("Adaptive account encrypted DASH cannot register a renewable clear graph",()=>Graph("dash","drm")),
        ("Adaptive account repeated fragment rejection stops after one replay",()=>Graph("hls","denied")),
        ("Adaptive account cancellation stops graph issuer refresh",()=>Graph("hls","cancel")),
        ("Adaptive account foreign resources cannot acquire graph leases",()=>Graph("hls","foreign")),
        ("Adaptive account Google external consent verifies PKCE and account identity",()=>Consent(NativeOAuthProvider.YouTube,"valid")),
        ("Adaptive account Vimeo external consent scopes its confidential grant",()=>Consent(NativeOAuthProvider.Vimeo,"valid")),
        ("Adaptive account OAuth callback state mismatch sends no grant",()=>Consent(NativeOAuthProvider.YouTube,"state")),
        ("Adaptive account OAuth foreign callbacks send no grant",()=>Consent(NativeOAuthProvider.Vimeo,"foreign")),
        ("Adaptive account denied consent never exchanges a code",()=>Consent(NativeOAuthProvider.YouTube,"denied")),
        ("Adaptive account duplicate callbacks fail before grant",()=>Consent(NativeOAuthProvider.YouTube,"duplicate")),
        ("Adaptive account issuer redirects never receive OAuth secrets",()=>Consent(NativeOAuthProvider.Vimeo,"redirect")),
        ("Adaptive account insufficient OAuth scopes refuse the session",()=>Consent(NativeOAuthProvider.YouTube,"scope")),
        ("Adaptive account Google refresh is single flight and issuer bound",()=>OAuthRefresh("concurrent")),
        ("Adaptive account Google refresh refuses switched account identity",()=>OAuthRefresh("switched")),
        ("Adaptive account Google API rejection refreshes once",()=>OAuthRefresh("rejected")),
        ("Adaptive account Vimeo refresh grants remain explicitly unsupported",VimeoRefresh),
        ("Adaptive account OAuth credentials redact and preserve analysis arity",Options),
        ("Adaptive account authorized YouTube API inventories preserve complete order",()=>Inventory(false)),
        ("Adaptive account authorized YouTube API membership changes fail",()=>Inventory(true))
        ,("Adaptive account Google loopback consent completes without exposing the callback",()=>Browser("google"))
        ,("Adaptive account Vimeo loopback consent uses its registered callback",()=>Browser("vimeo"))
        ,("Adaptive account unrelated loopback callbacks cannot consume consent",()=>Browser("unrelated"))
        ,("Adaptive account browser cancellation closes the loopback listener",()=>Browser("cancel"))
        ,("Adaptive account native engine integrates explicit browser sign in",()=>Browser("engine"))
        ,("Adaptive account Vimeo text tracks preserve same-language API identities",()=>TextTracks("valid"))
        ,("Adaptive account Vimeo text-track continuation cannot export bearer tokens",()=>TextTracks("foreign"))
        ,("Adaptive account Vimeo changed text-track totals fail before caption media",()=>TextTracks("total"))
        ,("Adaptive account Vimeo foreign text-track membership fails",()=>TextTracks("identity"))
    ];
    internal static void Assert(bool value){if(!value)throw new InvalidOperationException("Adaptive account expectation failed.");}
    internal static MediaHttp Http(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action,CookieContainer? cookies=null)=>new(new Handler(action),new(){AllowAutoRedirect=false,CookieContainer=cookies});
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message,CancellationToken ct)=>action(message,ct);}
    internal static HttpResponseMessage Text(string text,HttpStatusCode status=HttpStatusCode.OK)=>new(status){Content=new StringContent(text)};
    internal static PageMetadata Page(string kind,int generation)=>new(){Id="123",Media=[new(){Id="root",Url=new("https://cdn.example/g"+generation+(kind is "dash" or "indexed" or "periods"?"/root.mpd":"/root.m3u8")),Kind=kind is "dash" or "indexed" or "periods"?MediaKind.Dash:MediaKind.Hls,Extension=kind is "dash" or "indexed" or "periods"?"mpd":"m3u8",HasVideo=true,HasAudio=true}]};
    internal static string Hls(int generation,bool complete=true,int first=1,bool changed=false,bool missing=false)=>
        "#EXTM3U\n#EXT-X-TARGETDURATION:1\n#EXT-X-MEDIA-SEQUENCE:"+first+"\n#EXT-X-KEY:METHOD=AES-128,URI=\"key.bin\",IV=0x00000000000000000000000000000001\n#EXT-X-MAP:URI=\"init.mp4\",BYTERANGE=\"16@0\"\n"+
        string.Join("",Enumerable.Range(first,missing?1:2).Select(i=>"#EXTINF:"+(changed?2:1)+",\n#EXT-X-BYTERANGE:16@"+(16*i)+"\ns"+i+".mp4\n"))+(complete?"#EXT-X-ENDLIST\n":"");
    internal static string Dash(int generation,bool changed=false,bool indexed=false)=>"<MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\" mediaPresentationDuration=\"PT2S\"><Period id=\"p\"><AdaptationSet mimeType=\"video/mp4\"><Representation id=\"v\" height=\""+(changed?144:72)+"\" bandwidth=\"100000\" codecs=\"avc1.64001e\">"+
        (indexed?"<BaseURL>file.mp4</BaseURL><SegmentBase indexRange=\"44-100\"><Initialization range=\"0-43\"/></SegmentBase>":"<SegmentTemplate timescale=\"1\" duration=\"1\" initialization=\"init.mp4\" media=\"s$Number$.m4s\" startNumber=\"1\"/>")+"</Representation></AdaptationSet>"+
        (indexed?"":"<AdaptationSet mimeType=\"application/mp4\" lang=\"en\"><Representation id=\"c\" codecs=\"wvtt\"><SegmentTemplate timescale=\"1\" duration=\"1\" initialization=\"caption-init.mp4\" media=\"c$Number$.m4s\" startNumber=\"1\"/></Representation></AdaptationSet>")+"</Period></MPD>";
    private static async Task Graph(string kind,string mode)
    {
        var generation=0;var renewals=0;var newMedia=0;var requests=0;
        string Manifest(Uri url)
        {
            var g=url.AbsolutePath.Contains("/g1/")?1:0;
            if(kind=="master"&&url.AbsolutePath.EndsWith("root.m3u8"))return "#EXTM3U\n#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"a\",NAME=\"Audio\",LANGUAGE=\"en\",DEFAULT=YES,URI=\"audio.m3u8\"\n#EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID=\"c\",NAME=\"Caption\",LANGUAGE=\"en\",URI=\"caption.m3u8\"\n#EXT-X-STREAM-INF:BANDWIDTH=100000,RESOLUTION=128x72,CODECS=\"avc1.64001e,mp4a.40.2\",AUDIO=\"a\",SUBTITLES=\"c\"\nvideo.m3u8\n";
            if(kind is "dash" or "indexed")return Dash(g,mode=="changed"&&g==1,kind=="indexed");
            if(kind=="periods")
            {
                var doc=System.Xml.Linq.XDocument.Parse(Dash(g));var root=doc.Root!;root.SetAttributeValue("type","dynamic");root.SetAttributeValue("availabilityStartTime","2026-01-01T00:00:00Z");root.SetAttributeValue("publishTime","2026-01-01T00:00:05Z");var first=root.Elements().Single();first.SetAttributeValue("duration","PT2S");var second=new System.Xml.Linq.XElement(first);second.SetAttributeValue("id","p2");second.SetAttributeValue("start","PT2S");second.AddFirst(new System.Xml.Linq.XElement(root.Name.Namespace+"BaseURL","p2/"));root.Add(second);root.SetAttributeValue("mediaPresentationDuration","PT4S");return doc.ToString();
            }
            return Hls(g,kind!="live",kind=="live"&&g==1?2:1,mode=="changed"&&g==1,mode=="missing"&&g==1);
        }
        using var owner=Http((message,_)=>{requests++;Assert(!message.Headers.Contains("Authorization"));var url=message.RequestUri!;
            if(url.AbsolutePath.EndsWith("m3u8")||url.AbsolutePath.EndsWith("mpd"))return Task.FromResult(Text(mode=="drm"?Dash(0).Replace("<Period","<ContentProtection/><Period"):Manifest(url)));
            if(url.AbsolutePath.Contains("/g1/"))newMedia++;return Task.FromResult(Text("complete",mode=="denied"||url.AbsolutePath.Contains("/g0/")?HttpStatusCode.Forbidden:HttpStatusCode.OK));});
        using var lease=new RenewableMediaAuthorization(Page(kind,0),_=>{renewals++;generation=1;return Task.FromResult(Page(kind,generation));},readManifest:(url,ct)=>owner.GetDocumentAsync(url,8*1024*1024,ct));using var media=owner.WithAuthorization(lease);
        var urls=new List<Uri>();
        try
        {
            if(kind is "dash" or "indexed" or "periods")
            {
                var parsed=await new DashDownloader(media).ReadAsync(Page(kind,0).Media[0].Url,default);
                urls.Add(mode=="init"?parsed.Representations[0].Initialization!:mode=="caption"?parsed.CaptionTracks[0].Representation.Segments[0].Url:kind=="indexed"?parsed.Representations[0].Url:kind=="periods"?parsed.Periods[1].Representations[0].Segments[0].Url:parsed.Representations[0].Segments[0].Url);
            }
            else
            {
                var reader=new HlsDownloader(media);var parsed=await reader.ReadAsync(Page(kind,0).Media[0].Url,default);
                if(kind=="master")foreach(var url in new[]{parsed.Variants[0].Url,parsed.AudioRenditions[0].Url!,parsed.SubtitleRenditions[0].Url!}){var child=await reader.ReadAsync(url,default);urls.Add(child.MediaSegments[0].Url);}
                else urls.Add(mode=="key"?parsed.MediaSegments[0].Encryption!.KeyUrl:mode=="init"?parsed.MediaSegments[0].Initialization!.Url:mode=="window"?parsed.MediaSegments[1].Url:mode=="missing"?parsed.MediaSegments[1].Url:parsed.MediaSegments[0].Url);
            }
            if(mode=="foreign"){await lease.AuthorizeAsync(new("https://foreign.example/segment.ts"),default);throw new InvalidOperationException();}
            using var stop=new CancellationTokenSource();if(mode=="cancel")stop.Cancel();
            await Task.WhenAll(urls.Select(async url=>{var bytes=await media.GetBytesAsync(url,1024,stop.Token);Assert(Encoding.UTF8.GetString(bytes)=="complete");}));
            Assert(renewals==1&&newMedia==urls.Count&&urls.All(u=>lease.Canonicalize(u)==u));
            if(mode=="window")SabrFixture.Throws<InvalidDataException>(()=>lease.AuthorizeAsync(new("https://cdn.example/g0/s1.mp4"),default).GetAwaiter().GetResult());
            if(mode=="prefix"){var fresh=await new HlsDownloader(media).ReadAsync(Page(kind,0).Media[0].Url,default);Assert(fresh.SourceUrl==Page(kind,0).Media[0].Url&&fresh.MediaSegments[0].Url==urls[0]);}
        }
        catch(InvalidDataException)when(mode is "changed" or "missing" or "foreign"){Assert(newMedia==0&&renewals==(mode=="foreign"?0:1));}
        catch(NotSupportedException)when(mode=="drm"){Assert(renewals==0&&newMedia==0);}
        catch(HttpRequestException)when(mode=="denied"){Assert(renewals==1&&newMedia==1);}
        catch(OperationCanceledException)when(mode=="cancel"){Assert(renewals==0&&newMedia==0);}
    }
    internal static string Token(NativeOAuthProvider provider,string token="token-new",bool scope=true)=>JsonSerializer.Serialize(new{access_token=token,token_type="bearer",expires_in=3600,refresh_token=provider==NativeOAuthProvider.YouTube?"refresh-fixture":"",scope=scope?NativeOAuthSession.RequiredScopes(provider):"public"});
    private static async Task Consent(NativeOAuthProvider provider,string mode)
    {
        var grants=0;var identity=0;using var flow=new NativeOAuthLogin(provider,"client-fixture",new("http://127.0.0.1:32145/callback"),provider==NativeOAuthProvider.Vimeo?"client-secret":"");
        var query=NativeOAuthLogin.Parse(flow.AuthorizationUrl.Query);Assert(query["response_type"]=="code"&&flow.AuthorizationUrl.Scheme=="https");
        if(provider==NativeOAuthProvider.YouTube)Assert(query["code_challenge_method"]=="S256"&&query["code_challenge"].Length==43);
        using var http=Http(async(message,ct)=>{Assert(message.RequestUri!.Scheme=="https");if(message.Method==HttpMethod.Post){grants++;Assert(message.RequestUri==NativeOAuthSession.TokenEndpoint(provider));var form=NativeOAuthLogin.Parse(await message.Content!.ReadAsStringAsync(ct));Assert(form["code"]=="code-fixture"&&form["grant_type"]=="authorization_code");if(provider==NativeOAuthProvider.Vimeo)Assert(message.Headers.Authorization?.Scheme=="Basic");else Assert(form["code_verifier"].Length==43&&!message.Headers.Contains("Authorization"));var response=Text(Token(provider,scope:mode!="scope"));if(mode=="redirect"){response.StatusCode=HttpStatusCode.Redirect;response.Headers.Location=new("https://foreign.example/steal");}return response;}identity++;Assert(message.Headers.Authorization?.Parameter=="token-new");return Text(provider==NativeOAuthProvider.YouTube?"{\"sub\":\"account-42\"}":"{\"uri\":\"/users/42\"}");});
        var callback=new Uri((mode=="foreign"?"https://foreign.example/callback":"http://127.0.0.1:32145/callback")+"?state="+(mode=="state"?"wrong":query["state"])+(mode=="denied"?"&error=access_denied":"&code=code-fixture")+(mode=="duplicate"?"&code=again":""));
        try{var credentials=await flow.CompleteAsync(callback,http,default);Assert(mode=="valid"&&grants==1&&identity==1&&credentials.AccountId is not null&&!JsonSerializer.Serialize(credentials).Contains("token-new"));SabrFixture.Throws<InvalidOperationException>(()=>flow.CompleteAsync(callback,http,default).GetAwaiter().GetResult());}
        catch(Exception e)when(mode!="valid"&&e is InvalidDataException or NotSupportedException or HttpRequestException){Assert(grants==(mode is "redirect" or "scope"?1:0));}
    }
    private static async Task OAuthRefresh(string mode)
    {
        var grants=0;var identities=0;var gets=0;var credentials=new NativeOAuthCredentials{Provider=NativeOAuthProvider.YouTube,ClientId="client-fixture",AccessToken="token-old",RefreshToken="refresh-fixture",Scopes=NativeOAuthSession.RequiredScopes(NativeOAuthProvider.YouTube),Expires=mode=="concurrent"?DateTimeOffset.UtcNow.AddSeconds(-1):DateTimeOffset.UtcNow.AddHours(1)};
        using var http=Http((message,_)=>{if(message.Method==HttpMethod.Post){grants++;Assert(message.RequestUri==NativeOAuthSession.TokenEndpoint(credentials.Provider));return Task.FromResult(Text(Token(credentials.Provider)));}if(message.RequestUri!.Host=="openidconnect.googleapis.com"){identities++;return Task.FromResult(Text("{\"sub\":\""+(mode=="switched"&&identities>1?"other-account":"account-42")+"\"}"));}gets++;return Task.FromResult(Text("{}",mode!="concurrent"&&gets==1?HttpStatusCode.Unauthorized:HttpStatusCode.OK));});using var session=new NativeOAuthSession(http,credentials);
        try{if(mode=="concurrent")await Task.WhenAll(Enumerable.Range(0,8).Select(_=>session.EnsureAsync(default)));else await session.GetAsync(new("https://www.googleapis.com/youtube/v3/playlists?part=id&id=fixture"),default);Assert(mode!="switched"&&grants==1&&session.Credentials.AccountId=="account-42");}
        catch(InvalidDataException)when(mode=="switched"){Assert(grants==1&&gets==1);}
    }
    private static Task VimeoRefresh(){SabrFixture.Throws<NotSupportedException>(()=>NativeOAuthSession.Validate(new(){Provider=NativeOAuthProvider.Vimeo,ClientId="client",RefreshToken="refresh"}));return Task.CompletedTask;}
    private static Task Options(){var request=NativeExtraArguments.Apply(new(){Url=new("https://www.youtube.com/playlist?list=PLfixture"),OutputDirectory="."},NativeAnalysisArguments.Select(["--oauth-client-id","client-fixture","--oauth-access-token=token-old","--oauth-refresh-token","refresh-fixture","--oauth-scopes","openid https://www.googleapis.com/auth/youtube.readonly"]));Assert(request.Authentication.OAuth?.RefreshToken=="refresh-fixture"&&!JsonSerializer.Serialize(request).Contains("token-old"));SabrFixture.Throws<NotSupportedException>(()=>new NativeEngine().ValidateAuthenticationOptions(request with {Url=new("https://vimeo.com/123")}));var login=NativeExtraArguments.Apply(new(){Url=request.Url,OutputDirectory="."},NativeAnalysisArguments.Select(["--oauth-login","--oauth-client-id=client-fixture"]));Assert(login.Authentication.OAuthLogin&&login.Authentication.OAuth?.ClientId=="client-fixture");new NativeEngine().ValidateAuthenticationOptions(login);return Task.CompletedTask;}
    private static async Task Inventory(bool wrong)
    {
        var credentials=new NativeOAuthCredentials{Provider=NativeOAuthProvider.YouTube,AccessToken="token-fixture"};using var http=Http((message,_)=>Task.FromResult(Text(message.RequestUri!.Host=="openidconnect.googleapis.com"?"{\"sub\":\"account-42\"}":message.RequestUri.AbsolutePath.EndsWith("playlists")?"{\"items\":[{\"id\":\"PLfixture\",\"snippet\":{\"title\":\"Private fixture\"},\"contentDetails\":{\"itemCount\":1}}]}":"{\"items\":[{\"snippet\":{\"playlistId\":\""+(wrong?"PLother":"PLfixture")+"\",\"position\":0,\"title\":\"fixture\"},\"contentDetails\":{\"videoId\":\"abcdefghijk\"}}]}")));
        try{var page=await new YoutubeDl.Engine.Providers.YouTube.YouTubeProvider().ExtractAsync(new(){Url=new("https://www.youtube.com/playlist?list=PLfixture"),OutputDirectory=".",Authentication=new(){OAuth=credentials}},http,default);Assert(!wrong&&page.Entries.Count==1&&page.Entries[0].Position==1&&page.Entries[0].Id=="abcdefghijk");}catch(InvalidDataException)when(wrong){}
    }
    private static async Task Browser(string mode)
    {
        var provider=mode is "vimeo" or "engine"?NativeOAuthProvider.Vimeo:NativeOAuthProvider.YouTube;Uri? callback=null;var grants=0;
        var port=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);port.Start();var number=((IPEndPoint)port.LocalEndpoint).Port;port.Stop();
        var registration=new NativeOAuthCredentials{Provider=provider,ClientId="client-fixture",ClientSecret=provider==NativeOAuthProvider.Vimeo?"secret-fixture":"",RedirectUri=provider==NativeOAuthProvider.Vimeo?new("http://127.0.0.1:"+number+"/callback"):null};
        using var stop=new CancellationTokenSource();
        async Task Open(Uri url,CancellationToken ct)
        {
            var query=NativeOAuthLogin.Parse(url.Query);callback=new(query["redirect_uri"]);if(mode=="cancel"){stop.Cancel();return;}
            using var browser=new HttpClient();if(mode=="unrelated"){using var bad=await browser.GetAsync(callback+"?state=unrelated&code=wrong",ct);Assert(bad.StatusCode==HttpStatusCode.BadRequest);}
            using var response=await browser.GetAsync(callback+"?state="+query["state"]+"&code=code-fixture",ct);var body=await response.Content.ReadAsStringAsync(ct);Assert(response.IsSuccessStatusCode&&!body.Contains(query["state"])&&!body.Contains("code-fixture"));
        }
        HttpResponseMessage Response(HttpRequestMessage message)
        {
            if(message.Method==HttpMethod.Post){grants++;return Text(Token(provider));}
            if(message.RequestUri!.AbsolutePath=="/me")return Text("{\"uri\":\"/users/42\"}");
            if(message.RequestUri.Host=="openidconnect.googleapis.com")return Text("{\"sub\":\"account-42\"}");
            return Text(AccountRenewalChecks.ApiJson());
        }
        try
        {
            if(mode=="engine"){var engine=new NativeEngine(httpFactory:_=>Http((m,_)=>Task.FromResult(Response(m))),accountBrowserOpen:Open);var info=await engine.InspectAsync(new(){Url=new("https://vimeo.com/123"),OutputDirectory=".",Authentication=new(){OAuth=registration,OAuthLogin=true}},stop.Token);Assert(info.Id=="123"&&grants==1);}
            else{using var http=Http((m,_)=>Task.FromResult(Response(m)));var result=await NativeOAuthBrowser.SignInAsync(registration,http,stop.Token,Open);Assert(mode!="cancel"&&result.AccountId is not null&&grants==1);}
        }
        catch(OperationCanceledException)when(mode=="cancel"){Assert(grants==0);}
        Assert(callback is not null);var closed=new System.Net.Sockets.TcpListener(IPAddress.Loopback,callback!.Port);closed.Start();closed.Stop();
    }
    private static async Task TextTracks(string mode)
    {
        var pages=0;using var http=Http((message,_)=>
        {
            Assert(message.RequestUri!.Host=="api.vimeo.com"&&message.Headers.Authorization?.Parameter=="token-fixture");
            if(!message.RequestUri.AbsolutePath.EndsWith("/texttracks"))return Task.FromResult(Text(AccountRenewalChecks.ApiJson()));pages++;
            var id=mode=="identity"?"124":"123";var next=pages==1?(mode=="foreign"?"https://foreign.example/steal":"/videos/123/texttracks?page=2"):null;
            return Task.FromResult(Text(JsonSerializer.Serialize(new{total=mode=="total"&&pages==2?3:2,data=new[]{new{uri="/videos/"+id+"/texttracks/"+pages,active=true,language="en",link="https://caption.example/"+pages+".vtt"}},paging=new{next}})));
        });
        try{var page=await new YoutubeDl.Engine.Providers.Vimeo.VimeoProvider().ExtractAsync(new(){Url=new("https://vimeo.com/123"),OutputDirectory=".",WriteSubs=true,Authentication=new(){AccessToken="token-fixture"}},http,default);Assert(mode=="valid"&&page.Subtitles.Count==2&&page.PlaybackAuthorization?.AppliesTo(page.Subtitles[0].Url)==true&&page.PlaybackAuthorization.AppliesTo(page.Subtitles[1].Url));}
        catch(InvalidDataException)when(mode!="valid"){Assert(pages==(mode=="total"?2:1));}
    }
}
