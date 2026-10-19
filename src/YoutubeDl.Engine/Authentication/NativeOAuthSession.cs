using System.Globalization;
using System.Net;
using System.Text.Json;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Authentication;

internal sealed class NativeOAuthSession:IDisposable
{
    private readonly MediaHttp _http;private readonly SessionLifetime _lifetime=new();private NativeOAuthCredentials _credentials;private bool _identified;private int _refreshes;private long _generation;
    internal NativeOAuthCredentials Credentials=>_lifetime.Read(()=>_credentials);
    internal NativeOAuthSession(MediaHttp owner,NativeOAuthCredentials credentials){Validate(credentials);_http=owner.CreateDiscoverySession(true);_credentials=credentials;}
    internal static string RequiredScopes(NativeOAuthProvider provider)=>provider==NativeOAuthProvider.YouTube?"openid https://www.googleapis.com/auth/youtube.readonly":"public private video_files";
    internal static Uri TokenEndpoint(NativeOAuthProvider provider)=>new(provider==NativeOAuthProvider.YouTube?"https://oauth2.googleapis.com/token":"https://api.vimeo.com/oauth/access_token");
    internal static void Field(string value,bool identifier){if(value is null||value.Length>16384||value.Any(char.IsControl)||identifier&&value.Any(char.IsWhiteSpace))throw new ArgumentException("OAuth credentials contain invalid fields.");}
    internal static void Validate(NativeOAuthCredentials value)
    {
        if(!Enum.IsDefined(value.Provider))throw new ArgumentException("The OAuth provider is invalid.");
        foreach(var field in new[]{value.ClientId,value.AccessToken,value.RefreshToken,value.AccountId??""})Field(field,true);Field(value.ClientSecret,false);Field(value.Scopes,false);
        if(value.AccessToken.Length==0&&value.RefreshToken.Length==0)throw new ArgumentException("Provide an OAuth access or refresh token.");
        if(value.RefreshToken.Length>0&&(value.Provider!=NativeOAuthProvider.YouTube||value.ClientId.Length==0))throw new NotSupportedException("OAuth refresh requires a registered Google client; Vimeo does not support refresh grants.");
    }
    internal static void ValidateRegistration(NativeOAuthCredentials value)
    {
        Field(value.ClientId,true);Field(value.ClientSecret,false);
        if(!Enum.IsDefined(value.Provider)||value.ClientId.Length==0||value.ClientId.Contains(':')||value.Provider==NativeOAuthProvider.Vimeo&&value.ClientSecret.Length==0)throw new ArgumentException("Provide the registered OAuth client credentials.");
        if(value.AccessToken.Length>0||value.RefreshToken.Length>0)throw new ArgumentException("Choose browser sign-in or existing account tokens for this job.");
        if(value.Provider==NativeOAuthProvider.Vimeo&&value.RedirectUri is null)throw new ArgumentException("Vimeo sign-in requires a registered callback.");
        if(value.RedirectUri is { } redirect&&(redirect.Scheme!="http"||redirect.Host is not ("127.0.0.1" or "[::1]")||redirect.IsDefaultPort||redirect.UserInfo.Length>0||redirect.Query.Length>0||redirect.Fragment.Length>0))throw new ArgumentException("Browser sign-in requires a registered IP-loopback callback with an explicit port.");
    }
    internal async Task EnsureAsync(CancellationToken ct)
    {
        using var operation=_lifetime.Begin(ct);ct=operation.Token;
        await _lifetime.Gate.WaitAsync(ct).ConfigureAwait(false);
        try{ct.ThrowIfCancellationRequested();var current=Credentials;if(current.AccessToken.Length==0||current.Expires is { } expiry&&expiry<=DateTimeOffset.UtcNow.AddSeconds(30))await RefreshAsync(ct).ConfigureAwait(false);if(!_identified)await IdentifyAsync(Credentials,ct).ConfigureAwait(false);}
        finally{_lifetime.Gate.Release();}
    }
    internal async Task<MediaDocument> GetAsync(Uri url,CancellationToken ct)
    {
        using var operation=_lifetime.Begin(ct);ct=operation.Token;var provider=Credentials.Provider;
        if(url.Scheme!="https"||!url.IsDefaultPort||url.UserInfo.Length>0||url.Fragment.Length>0||
            (provider==NativeOAuthProvider.Vimeo?url.Host!="api.vimeo.com"||!url.AbsolutePath.StartsWith("/videos/",StringComparison.Ordinal)&&!Providers.Vimeo.VimeoCollectionAddress.IsApiResource(url):url.Host!="www.googleapis.com"||url.AbsolutePath is not ("/youtube/v3/playlists" or "/youtube/v3/playlistItems" or "/youtube/v3/search")))throw new InvalidDataException("The OAuth resource is outside its declared issuer API.");
        await EnsureAsync(ct).ConfigureAwait(false);var rejected=_lifetime.Read(()=>(Token:_credentials.AccessToken,Generation:_generation));
        try{return await ReadAsync(url,rejected.Token,ct).ConfigureAwait(false);}
        catch(HttpRequestException e)when(e.StatusCode==HttpStatusCode.Unauthorized)
        {
            await _lifetime.Gate.WaitAsync(ct).ConfigureAwait(false);try{ct.ThrowIfCancellationRequested();if(_generation==rejected.Generation)await RefreshAsync(ct).ConfigureAwait(false);}finally{_lifetime.Gate.Release();}
            return await ReadAsync(url,Credentials.AccessToken,ct).ConfigureAwait(false);
        }
    }
    private async Task<MediaDocument> ReadAsync(Uri url,string token,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result=await _http.GetDocumentAsync(url,2*1024*1024,ct,new Dictionary<string,string>{["Authorization"]="Bearer "+token}).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();if(result.Url!=url)throw new InvalidDataException("The OAuth issuer redirected its selected resource.");return result;
    }
    private async Task IdentifyAsync(NativeOAuthCredentials candidate,CancellationToken ct)
    {
        var url=new Uri(candidate.Provider==NativeOAuthProvider.YouTube?"https://openidconnect.googleapis.com/v1/userinfo":"https://api.vimeo.com/me");
        var document=await ReadAsync(url,candidate.AccessToken,ct).ConfigureAwait(false);using var parsed=JsonDocument.Parse(document.Text);var root=parsed.RootElement;
        var field=candidate.Provider==NativeOAuthProvider.YouTube?"sub":"uri";var id=root.TryGetProperty(field,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
        if(string.IsNullOrWhiteSpace(id)||id.Length>256||id.Any(char.IsControl)||candidate.Provider==NativeOAuthProvider.Vimeo&&!System.Text.RegularExpressions.Regex.IsMatch(id,@"\A/users/[0-9]+\z",System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))throw new InvalidDataException("OAuth did not identify the authorized account.");
        _lifetime.Commit(ct,()=>{
            if(_credentials.AccountId is { } original&&original!=id||candidate.AccountId is { } expected&&expected!=id)throw new InvalidDataException("OAuth renewal changed the authorized account.");
            _credentials=candidate with {AccountId=id};_identified=true;_generation=checked(_generation+1);
        });
    }
    private async Task RefreshAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();var previous=Credentials;
        if(previous.Provider!=NativeOAuthProvider.YouTube||previous.RefreshToken.Length==0)throw new NotSupportedException("This account session needs fresh sign-in; no supported refresh grant is available.");
        if(++_refreshes>8)throw new NotSupportedException("The OAuth refresh limit was exceeded.");
        var form=new Dictionary<string,string>{["grant_type"]="refresh_token",["client_id"]=previous.ClientId,["refresh_token"]=previous.RefreshToken};if(previous.ClientSecret.Length>0)form["client_secret"]=previous.ClientSecret;
        var response=await _http.PostFormDocumentAsync(TokenEndpoint(previous.Provider),form,1024*1024,ct).ConfigureAwait(false);ct.ThrowIfCancellationRequested();
        var fresh=ParseToken(response.Text,previous,true);await IdentifyAsync(fresh,ct).ConfigureAwait(false);
    }
    internal static NativeOAuthCredentials ParseToken(string json,NativeOAuthCredentials previous,bool refresh)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        string? Text(string name)=>root.TryGetProperty(name,out var field)&&field.ValueKind==JsonValueKind.String?field.GetString():null;
        var token=Text("access_token");var type=Text("token_type");if(string.IsNullOrEmpty(token)||!string.Equals(type,"bearer",StringComparison.OrdinalIgnoreCase)||root.TryGetProperty("error",out _))throw new InvalidDataException("OAuth did not grant a valid bearer session.");Field(token,true);
        var scopes=Text("scope")??(refresh?previous.Scopes:"");var allowed=scopes.Split(' ',StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if(!RequiredScopes(previous.Provider).Split(' ').All(allowed.Contains))throw new NotSupportedException("OAuth did not grant the required read-only account/file scopes.");
        DateTimeOffset? expires=null;if(root.TryGetProperty("expires_in",out var ttl)){if(!ttl.TryGetInt64(out var seconds)||seconds is <1 or >31536000)throw new InvalidDataException("OAuth expiry is invalid.");expires=DateTimeOffset.UtcNow.AddSeconds(seconds);}
        if(previous.Provider==NativeOAuthProvider.YouTube&&expires is null)throw new InvalidDataException("Google OAuth omitted its session expiry.");
        var value=previous with {AccessToken=token,RefreshToken=Text("refresh_token")??previous.RefreshToken,Scopes=scopes,Expires=expires};Validate(value);return value;
    }
    public override string ToString()=>"Native OAuth account session [redacted]";
    public void Dispose(){_lifetime.Dispose(()=>{_credentials=new(){Provider=_credentials.Provider};_identified=false;_generation=0;});_http.Dispose();}
}
