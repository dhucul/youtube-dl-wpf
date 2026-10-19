using System.Text.Json.Serialization;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Authentication;

namespace YoutubeDl.Engine;

[Flags]
public enum NativeAuthenticationCapabilities { None=0, UserPassword=1, TwoFactor=2, VideoPassword=4, AccessToken=8, CookieAccount=16,OAuth=32,Hosting=64 }

/// <summary>Explicit provider credentials; never exported with request metadata or diagnostics.</summary>
public sealed record NativeAuthenticationOptions
{
    [JsonIgnore] public string Username { get; init; }="";
    [JsonIgnore] public string Password { get; init; }="";
    [JsonIgnore] public string TwoFactor { get; init; }="";
    [JsonIgnore] public string VideoPassword { get; init; }="";
    [JsonIgnore] public string AccessToken { get; init; }="";
    [JsonIgnore] public NativeOAuthCredentials? OAuth { get; init; }
    [JsonIgnore] public NativeHostingCredentials? Hosting { get; init; }
    public bool OAuthLogin { get; init; }
    public int? YouTubeAccountIndex { get; init; }
    public bool UseNetrc { get; init; }
    [JsonIgnore] public string NetrcLocation { get; init; }="";
    [JsonIgnore] internal Uri? Origin { get; init; }
    internal bool IsLocalConfigured=>Username.Length>0||Password.Length>0||TwoFactor.Length>0||VideoPassword.Length>0||AccessToken.Length>0||YouTubeAccountIndex is not null||UseNetrc||OAuth is not null||OAuthLogin;
    public bool IsConfigured=>IsLocalConfigured||Hosting is not null;
    public override string ToString()=>"Native authentication [redacted]";
}

internal static class NativeAuthentication
{
    internal static async Task<NativeDownloadRequest> PrepareAsync(NativeDownloadRequest request, IMediaProvider provider, CancellationToken ct)
    {
        NativeGeoVerification.ValidateProvider(request, provider);
        request=NativeGeoVerification.Prepare(request);
        var options=request.Authentication;
        Validate(request,provider.AuthenticationCapabilities);
        if(!options.IsConfigured)return request;
        if(options.Origin is null&&options.UseNetrc)
        {
            var credentials=await NativeNetrc.ReadAsync(options.NetrcLocation,request.Url.Host,options.Username,ct).ConfigureAwait(false);
            // Explicit fields take precedence; lookup is still constrained to this selected server.
            options=options with { Username=options.Username.Length>0?options.Username:credentials.Username,Password=options.Password.Length>0?options.Password:credentials.Password };
        }
        if((options.Username.Length==0)!=(options.Password.Length==0))throw new ArgumentException("Provide both the account name and password.");
        if(options.TwoFactor.Length>0&&options.Username.Length==0)throw new ArgumentException("A two-factor code requires account credentials.");
        return request with { Authentication=options with { Origin=options.Origin??(options.IsLocalConfigured?new Uri(request.Url.GetLeftPart(UriPartial.Authority)+"/"):null) } };
    }
    internal static void Validate(NativeDownloadRequest request, NativeAuthenticationCapabilities capabilities)
    {
        var options=request.Authentication;
        foreach(var value in new[]{options.Username,options.Password,options.TwoFactor,options.VideoPassword,options.AccessToken,options.NetrcLocation})
            if(value is null||value.Length>16384||value.Any(char.IsControl))throw new ArgumentException("Authentication fields contain invalid characters or exceed their limits.");
        if(!options.IsConfigured)return;
        if(options.Hosting is { } hosting)
        {
            hosting.Validate();if(options.IsLocalConfigured)throw new ArgumentException("Choose scoped hosting credentials or local account credentials for this job.");
            if(!capabilities.HasFlag(NativeAuthenticationCapabilities.Hosting)||NativeHostingCredentials.IsHostingAddress(request.Url)&&!hosting.Matches(request.Url))throw new NotSupportedException("Hosting credentials cannot follow a different provider, account or media identity.");
        }
        if(options.OAuth is { } oauth)
        {
            if(options.OAuthLogin)NativeOAuthSession.ValidateRegistration(oauth);else NativeOAuthSession.Validate(oauth);
            if(!capabilities.HasFlag(NativeAuthenticationCapabilities.OAuth)||oauth.Provider==NativeOAuthProvider.YouTube&&!Providers.YouTube.YouTubeAddress.IsYouTubeHost(request.Url)||oauth.Provider==NativeOAuthProvider.Vimeo&&!Providers.Vimeo.VimeoAddress.IsVimeoHost(request.Url))throw new NotSupportedException("OAuth credentials belong to another native provider.");
            if(options.AccessToken.Length>0)throw new ArgumentException("Choose the OAuth account or supplied Vimeo API token for this job.");
        }
        else if(options.OAuthLogin)throw new ArgumentException("Browser sign-in requires its registered OAuth client.");
        if(options.YouTubeAccountIndex is <0 or >9)throw new ArgumentException("Choose a YouTube account index from0 through9.");
        if(options.AccessToken.Length>0&&options.AccessToken.Any(char.IsWhiteSpace))throw new ArgumentException("An access token cannot contain whitespace.");
        if(request.Url.Scheme!="https"||request.Url.UserInfo.Length>0)throw new NotSupportedException("Native account credentials require a selected HTTPS server.");
        if(options.Origin is not null&&!SameOrigin(options.Origin,request.Url))throw new NotSupportedException("Account credentials cannot follow media discovery to another server.");
        if((options.Username.Length>0||options.Password.Length>0||options.UseNetrc)&&!capabilities.HasFlag(NativeAuthenticationCapabilities.UserPassword)||
            options.TwoFactor.Length>0&&!capabilities.HasFlag(NativeAuthenticationCapabilities.TwoFactor)||options.VideoPassword.Length>0&&!capabilities.HasFlag(NativeAuthenticationCapabilities.VideoPassword)||
            options.AccessToken.Length>0&&!capabilities.HasFlag(NativeAuthenticationCapabilities.AccessToken)||options.YouTubeAccountIndex is not null&&!capabilities.HasFlag(NativeAuthenticationCapabilities.CookieAccount))
            throw new NotSupportedException("The selected native provider does not support these authentication options.");
    }
    internal static bool SameOrigin(Uri first,Uri second)=>first.Scheme==second.Scheme&&first.Host==second.Host&&first.Port==second.Port&&second.UserInfo.Length==0;
}
