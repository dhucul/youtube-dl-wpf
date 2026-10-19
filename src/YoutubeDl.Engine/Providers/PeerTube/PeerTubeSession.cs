using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Providers.PeerTube;

/// <summary>One selected instance's API session, owned and disposed by the job's HTTP session.</summary>
internal sealed class PeerTubeSession : IDisposable
{
    private readonly MediaHttp _http;
    private readonly Uri _origin;
    private NativeAuthenticationOptions _credentials;
    private readonly SessionLifetime _lifetime=new();
    private string _clientId="",_clientSecret="",_access="",_refresh="";
    private long _expires;
    internal PeerTubeSession(MediaHttp owner,Uri origin,NativeAuthenticationOptions credentials)
    { if(origin.Scheme!="https")throw new NotSupportedException("PeerTube account sessions require HTTPS.");_origin=origin;_credentials=credentials;_http=owner.CreateWithoutRedirects(); }
    public override string ToString()=>"PeerTube session [redacted]";
    internal async Task<string> ReadAsync(Uri endpoint,long limit,CancellationToken ct)
    {
        using var operation=_lifetime.Begin(ct);ct=operation.Token;
        await _lifetime.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ValidateEndpoint(endpoint);
            await EnsureTokenAsync(false,ct).ConfigureAwait(false);
            using var first=await ApiAsync(endpoint,HttpMethod.Get,ct).ConfigureAwait(false);
            if(first.StatusCode==HttpStatusCode.Unauthorized&&_access.Length>0)
            {
                await EnsureTokenAsync(true,ct).ConfigureAwait(false);
                using var second=await ApiAsync(endpoint,HttpMethod.Get,ct).ConfigureAwait(false);
                return await ReadResponseAsync(second,endpoint,limit,ct).ConfigureAwait(false);
            }
            return await ReadResponseAsync(first,endpoint,limit,ct).ConfigureAwait(false);
        }
        catch(Exception ex) when(ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {throw new InvalidDataException("PeerTube returned an invalid account response.");}
        finally { _lifetime.Gate.Release(); }
    }
    private readonly Dictionary<string,PeerTubePlaybackAuthorization> _playback=new(StringComparer.Ordinal);
    private readonly Dictionary<string,string> _playbackFacts=new(StringComparer.Ordinal);
    internal async Task<IPlaybackAuthorization> PlaybackAuthorizationAsync(PageMetadata page,CancellationToken ct)
    {
        if(!Guid.TryParseExact(page.Id,"D",out _))throw new InvalidDataException("The file token needs a verified video identity.");
        var scopes=new List<ScopedMediaQuery>();
        foreach(var media in page.Media)
        {
            if(!NativeAuthentication.SameOrigin(_origin,media.Url)||!media.Url.IsDefaultPort)throw new NotSupportedException("Private resources outside the selected origin require additional scope support.");
            var path=media.Url.AbsolutePath;
            if(media.Kind is MediaKind.Hls or MediaKind.Dash)
            {
                var directory=path[..(path.LastIndexOf('/')+1)];
                if(!directory.Split('/').Contains(page.Id,StringComparer.OrdinalIgnoreCase))throw new NotSupportedException("The private manifest directory is not bound to the verified video.");
                scopes.Add(new(new UriBuilder(media.Url){Path=directory,Query="",Fragment=""}.Uri,["videoFileToken=scope"]));
            }
            else scopes.Add(new(new UriBuilder(media.Url){Query="",Fragment=""}.Uri,["videoFileToken=scope"],exactResource:true));
        }
        if(scopes.Count is 0 or >16)throw new NotSupportedException("The private format inventory exceeds its token-scope limits.");
        PeerTubePlaybackAuthorization authorization;
        var facts=JsonSerializer.Serialize(page.Media.Select(media=>JsonSerializer.Serialize(new object?[]{media.Id,media.Kind,new UriBuilder(media.Url){Query="",Fragment=""}.Uri.AbsoluteUri,media.Extension,media.MimeType,media.Width,media.Height,media.Bandwidth,media.FramesPerSecond,media.FileSize,media.VideoCodec,media.AudioCodec,media.HasVideo,media.HasAudio,media.AudioLanguage,media.IsDefaultAudio})).Order(StringComparer.Ordinal).ToArray());
        authorization=_lifetime.Read(()=>{lock(_playback){if(!_playback.TryGetValue(page.Id!,out var found)){_playback.Add(page.Id!,found=new(this,page.Id!,scopes));_playbackFacts.Add(page.Id!,facts);}else if(_playbackFacts[page.Id!]!=facts)throw new InvalidDataException("The private video changed its verified formats or resource scopes.");return found;}});
        await authorization.AuthorizeAsync(page.Media[0].Url,ct).ConfigureAwait(false);
        return authorization;
    }
    internal async Task<(string Token,DateTimeOffset Expires)> RequestFileTokenAsync(string id,CancellationToken ct)
    {
        if(!Guid.TryParseExact(id,"D",out _))throw new InvalidDataException("The file token needs a verified video identity.");
        using var operation=_lifetime.Begin(ct);ct=operation.Token;await _lifetime.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureTokenAsync(false,ct).ConfigureAwait(false);
            var endpoint=new Uri(_origin,"api/v1/videos/"+id+"/token");
            using var response=await ApiAsync(endpoint,HttpMethod.Post,ct).ConfigureAwait(false);
            string json;
            if(response.StatusCode==HttpStatusCode.Unauthorized&&_access.Length>0)
            {await EnsureTokenAsync(true,ct).ConfigureAwait(false);using var renewed=await ApiAsync(endpoint,HttpMethod.Post,ct).ConfigureAwait(false);json=await ReadResponseAsync(renewed,endpoint,65536,ct).ConfigureAwait(false);}
            else json=await ReadResponseAsync(response,endpoint,65536,ct).ConfigureAwait(false);
            using var document=Parse(json);var files=document.RootElement.GetProperty("files");
            var token=Secret(files,"token");var expiration=files.GetProperty("expires").GetDateTimeOffset();
            if(expiration<=DateTimeOffset.UtcNow)throw new InvalidDataException("The private file token has expired.");
            ct.ThrowIfCancellationRequested();return(token,expiration);
        }
        catch(Exception ex) when(ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {throw new InvalidDataException("PeerTube returned an invalid file token descriptor.");}
        finally{_lifetime.Gate.Release();}
    }
    private async Task EnsureTokenAsync(bool rejected,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(_credentials.Username.Length==0)return;
        if(!rejected&&_access.Length>0&&Environment.TickCount64<_expires)return;
        if(_clientId.Length==0)
        {
            var endpoint=new Uri(_origin,"api/v1/oauth-clients/local");using var request=new HttpRequestMessage(HttpMethod.Get,endpoint);using var response=await _http.SendAsync(request,ct).ConfigureAwait(false);
            using var document=Parse(await ReadResponseAsync(response,endpoint,65536,ct).ConfigureAwait(false));
            var client=Secret(document.RootElement,"client_id");var secret=Secret(document.RootElement,"client_secret");_lifetime.Commit(ct,()=>{_clientId=client;_clientSecret=secret;});
        }
        var fields=new Dictionary<string,string>{{"client_id",_clientId},{"client_secret",_clientSecret},{"response_type","code"}};
        if(_access.Length>0)
        {if(_refresh.Length==0)throw new InvalidDataException("The PeerTube session cannot refresh its expired access token.");fields["grant_type"]="refresh_token";fields["refresh_token"]=_refresh;}
        else {fields["grant_type"]="password";fields["username"]=_credentials.Username;fields["password"]=_credentials.Password;}
        var tokenUrl=new Uri(_origin,"api/v1/users/token");using var message=new HttpRequestMessage(HttpMethod.Post,tokenUrl){Content=new FormUrlEncodedContent(fields)};
        if(_access.Length==0&&_credentials.TwoFactor.Length>0)message.Headers.Add("x-peertube-otp",_credentials.TwoFactor);
        using var tokenResponse=await _http.SendAsync(message,ct).ConfigureAwait(false);
        using var tokens=Parse(await ReadResponseAsync(tokenResponse,tokenUrl,65536,ct).ConfigureAwait(false));var root=tokens.RootElement;
        var access=Secret(root,"access_token");var refresh=root.TryGetProperty("refresh_token",out var refreshValue)?Secret(root,"refresh_token"):_refresh;
        if(!root.TryGetProperty("token_type",out var type)||!string.Equals(type.GetString(),"Bearer",StringComparison.OrdinalIgnoreCase)||!root.TryGetProperty("expires_in",out var life)||!life.TryGetInt64(out var seconds)||seconds is <=0 or >31536000)
            throw new InvalidDataException("PeerTube returned an invalid account token lifetime or type.");
        var expires=checked(Environment.TickCount64+Math.Max(1,seconds- Math.Min(30,seconds/10))*1000);
        _lifetime.Commit(ct,()=>{_access=access;_refresh=refresh;_expires=expires;});
    }
    private async Task<HttpResponseMessage> ApiAsync(Uri endpoint,HttpMethod method,CancellationToken ct)
    {
        ValidateEndpoint(endpoint);using var request=new HttpRequestMessage(method,endpoint);
        if(method==HttpMethod.Post)request.Content=new StringContent("{}",Encoding.UTF8,"application/json");
        if(_access.Length>0)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",_access);
        if(_credentials.VideoPassword.Length>0)request.Headers.Add("x-peertube-video-password",_credentials.VideoPassword);
        return await _http.SendAsync(request,ct).ConfigureAwait(false);
    }
    private void ValidateEndpoint(Uri endpoint)
    {
        if(!NativeAuthentication.SameOrigin(_origin,endpoint)||!endpoint.AbsolutePath.StartsWith("/api/v1/",StringComparison.Ordinal)||endpoint.UserInfo.Length>0)
            throw new InvalidDataException("The PeerTube account request left its selected API scope.");
    }
    private async Task<string> ReadResponseAsync(HttpResponseMessage response,Uri expected,long limit,CancellationToken ct)
    {
        if(response.RequestMessage?.RequestUri is { } final&&final!=expected)throw new InvalidDataException("The PeerTube account request changed endpoint.");
        if((int)response.StatusCode is >=300 and <400)throw new InvalidDataException("PeerTube account and password requests cannot follow redirects.");
        if(!response.IsSuccessStatusCode)throw new HttpRequestException("The selected PeerTube server refused the account or video request.",null,response.StatusCode);
        if(response.Content.Headers.ContentLength>limit)throw new InvalidDataException("The PeerTube account response exceeds its limit.");
        await using var input=await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);using var memory=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=await _http.ReadAsync(input,buffer,ct).ConfigureAwait(false))>0){if(memory.Length+count>limit)throw new InvalidDataException("The PeerTube account response exceeds its limit.");memory.Write(buffer,0,count);}
        if(response.Content.Headers.ContentLength is { } size&&memory.Length!=size)throw new InvalidDataException("The PeerTube account response is incomplete.");
        ct.ThrowIfCancellationRequested();return new UTF8Encoding(false,true).GetString(memory.ToArray());
    }
    private static JsonDocument Parse(string json)
    {
        try{return JsonDocument.Parse(json,new(){MaxDepth=32});}catch(JsonException){throw new InvalidDataException("PeerTube returned an invalid account response.");}
    }
    private static string Secret(JsonElement root,string name)
    {
        if(!root.TryGetProperty(name,out var element)||element.ValueKind!=JsonValueKind.String||element.GetString() is not {Length:>0 and <=16384} value||value.Any(ch=>char.IsControl(ch)||char.IsWhiteSpace(ch)))throw new InvalidDataException("PeerTube returned an invalid credential descriptor.");
        return value;
    }
    public void Dispose(){_lifetime.Dispose(()=>{lock(_playback){foreach(var authorization in _playback.Values)authorization.Dispose();_playback.Clear();_playbackFacts.Clear();}_access=_refresh=_clientId=_clientSecret="";_credentials=new();});_http.Dispose();}
}
