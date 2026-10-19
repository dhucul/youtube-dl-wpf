using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Authentication;

public enum NativeOAuthProvider { YouTube,Vimeo }
public sealed record NativeOAuthCredentials
{
    public NativeOAuthProvider Provider { get; init; }
    [JsonIgnore] public string ClientId { get; init; }="";
    [JsonIgnore] public string ClientSecret { get; init; }="";
    [JsonIgnore] public string AccessToken { get; init; }="";
    [JsonIgnore] public string RefreshToken { get; init; }="";
    [JsonIgnore] public string? AccountId { get; init; }
    [JsonIgnore] public string Scopes { get; init; }="";
    public DateTimeOffset? Expires { get; init; }
    [JsonIgnore] public Uri? RedirectUri { get; init; }
    public override string ToString()=>"Native OAuth credentials [redacted]";
}

/// <summary>Explicit system-browser consent transaction. No embedded login or password collection.</summary>
public sealed class NativeOAuthLogin:IDisposable
{
    private readonly NativeOAuthProvider _provider;private readonly string _clientId,_clientSecret;private readonly Uri _redirect;
    private string? _state,_verifier;private readonly DateTimeOffset _expires=DateTimeOffset.UtcNow.AddMinutes(10);private int _used;
    public Uri AuthorizationUrl { get; }
    public NativeOAuthLogin(NativeOAuthProvider provider,string clientId,Uri redirect,string clientSecret="")
    {
        NativeOAuthSession.Field(clientId,true);NativeOAuthSession.Field(clientSecret,false);
        if(!Enum.IsDefined(provider)||clientId.Length==0||clientId.Contains(':')||provider==NativeOAuthProvider.Vimeo&&clientSecret.Length==0)throw new ArgumentException("Provide the registered OAuth client credentials.");
        if(redirect.UserInfo.Length>0||redirect.Query.Length>0||redirect.Fragment.Length>0||!(redirect.Scheme=="https"||redirect.Scheme=="http"&&redirect.Host is "127.0.0.1" or "[::1]"))throw new ArgumentException("OAuth requires an exact registered HTTPS or IP-loopback callback.");
        _provider=provider;_clientId=clientId;_clientSecret=clientSecret;_redirect=redirect;_state=Base64(RandomNumberGenerator.GetBytes(32));
        var values=new Dictionary<string,string>{["client_id"]=clientId,["redirect_uri"]=redirect.AbsoluteUri,["response_type"]="code",["state"]=_state,["scope"]=NativeOAuthSession.RequiredScopes(provider)};
        if(provider==NativeOAuthProvider.YouTube){_verifier=Base64(RandomNumberGenerator.GetBytes(32));values["code_challenge"]=Base64(SHA256.HashData(Encoding.ASCII.GetBytes(_verifier)));values["code_challenge_method"]="S256";values["access_type"]="offline";values["prompt"]="consent";}
        var endpoint=provider==NativeOAuthProvider.YouTube?"https://accounts.google.com/o/oauth2/v2/auth":"https://api.vimeo.com/oauth/authorize";
        AuthorizationUrl=new(endpoint+"?"+string.Join('&',values.Select(p=>Uri.EscapeDataString(p.Key)+"="+Uri.EscapeDataString(p.Value))));
    }
    public async Task<NativeOAuthCredentials> CompleteAsync(Uri callback,MediaHttp http,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();if(DateTimeOffset.UtcNow>=_expires||Volatile.Read(ref _used)!=0)throw new InvalidOperationException("This OAuth consent transaction expired or was already used.");
        if(callback.GetLeftPart(UriPartial.Path)!=_redirect.AbsoluteUri||callback.UserInfo.Length>0||callback.Fragment.Length>0)throw new InvalidDataException("OAuth returned to another callback.");
        var query=Parse(callback.Query);if(!query.TryGetValue("state",out var state)||_state is null||!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state),Encoding.UTF8.GetBytes(_state)))throw new InvalidDataException("OAuth returned a different consent state.");
        if(Interlocked.Exchange(ref _used,1)!=0)throw new InvalidOperationException("This OAuth consent was already consumed.");
        try
        {
            if(query.ContainsKey("error"))throw new NotSupportedException("The account did not approve OAuth access.");
            if(!query.TryGetValue("code",out var code)||code.Length==0)throw new InvalidDataException("OAuth did not return an authorization code.");
            using var controlled=http.CreateDiscoverySession(true);var form=new Dictionary<string,string>{["grant_type"]="authorization_code",["code"]=code,["redirect_uri"]=_redirect.AbsoluteUri};
            IReadOnlyDictionary<string,string>? headers=null;
            if(_provider==NativeOAuthProvider.YouTube){form["client_id"]=_clientId;if(_clientSecret.Length>0)form["client_secret"]=_clientSecret;form["code_verifier"]=_verifier!;}
            else headers=new Dictionary<string,string>{["Authorization"]="Basic "+Convert.ToBase64String(Encoding.UTF8.GetBytes(_clientId+":"+_clientSecret))};
            var result=await controlled.PostFormDocumentAsync(NativeOAuthSession.TokenEndpoint(_provider),form,1024*1024,ct,headers).ConfigureAwait(false);
            var credentials=NativeOAuthSession.ParseToken(result.Text,new(){Provider=_provider,ClientId=_clientId,ClientSecret=_clientSecret},false);
            using var session=new NativeOAuthSession(http,credentials);await session.EnsureAsync(ct).ConfigureAwait(false);return session.Credentials;
        }
        finally{_state=null;_verifier=null;}
    }
    internal static Dictionary<string,string> Parse(string query)
    {
        if(query.Length>32768)throw new InvalidDataException("OAuth callback exceeds its limit.");var values=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var part in query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries)){var pair=part.Split('=',2);var name=Uri.UnescapeDataString(pair[0].Replace('+',' '));var value=Uri.UnescapeDataString((pair.Length==2?pair[1]:"").Replace('+',' '));NativeOAuthSession.Field(value,false);if(!values.TryAdd(name,value))throw new InvalidDataException("OAuth callback contains duplicate fields.");}return values;
    }
    private static string Base64(byte[] value)=>Convert.ToBase64String(value).TrimEnd('=').Replace('+','-').Replace('/','_');
    public override string ToString()=>"Native OAuth consent transaction [redacted]";
    public void Dispose(){_state=null;_verifier=null;Interlocked.Exchange(ref _used,1);}
}
