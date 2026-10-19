using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Providers.Brightcove;
using YoutubeDl.Engine.Providers.JwPlatform;
using YoutubeDl.Engine.Providers.Kaltura;
using YoutubeDl.Engine.Providers.Wistia;

namespace YoutubeDl.Engine.Authentication;

public enum NativeHostingProvider { Unspecified,Wistia,JwPlatform,Brightcove,Kaltura }
/// <summary>Explicit publisher/media credential scope, independent of the embedding page origin.</summary>
public sealed record NativeHostingCredentials
{
    public NativeHostingProvider Provider { get; init; }
    public string AccountId { get; init; }="";
    public string MediaId { get; init; }="";
    [JsonIgnore] public string AccessToken { get; init; }="";
    [JsonIgnore] public string SigningKey { get; init; }="";
    public int TokenLifetimeSeconds { get; init; }=3600;
    internal void Validate()
    {
        if(Provider==NativeHostingProvider.Unspecified||!Enum.IsDefined(Provider))throw new ArgumentException("Select the hosting credential provider.");
        foreach(var value in new[]{AccountId,MediaId,AccessToken,SigningKey})if(value is null||value.Length>16384||value.Any(char.IsControl))throw new ArgumentException("Hosting credential fields are invalid.");
        if(AccessToken.Any(char.IsWhiteSpace)||AccessToken.Length>0&&SigningKey.Length>0||TokenLifetimeSeconds is <60 or >10800)throw new ArgumentException("Choose one valid hosting credential and a bounded token lifetime.");
        if(AccessToken.Length==0&&SigningKey.Length==0)throw new ArgumentException("Provide the scoped hosting token or signing secret.");
        if(SigningKey.Length>0&&Provider!=NativeHostingProvider.JwPlatform)throw new NotSupportedException("Native publisher signing currently supports JW delivery only.");
        var valid=Provider switch
        {
            NativeHostingProvider.Wistia=>Match(MediaId,@"[a-z0-9]{10}")&&AccountId.Length==0,
            NativeHostingProvider.JwPlatform=>Match(MediaId,@"[A-Za-z0-9]{8}")&&AccountId.Length==0,
            NativeHostingProvider.Brightcove=>Match(AccountId,@"[0-9]{1,20}")&&Match(MediaId,@"(?:[0-9]{1,20}|ref:[A-Za-z0-9_. -]{1,200})"),
            NativeHostingProvider.Kaltura=>int.TryParse(AccountId,out var partner)&&partner>0&&Match(AccountId,@"[0-9]{1,10}")&&KalturaAddress.IsEntryId(MediaId),
            _=>false
        };
        if(!valid)throw new ArgumentException("Hosting credentials require an exact provider, account and media identity.");
    }
    internal bool Matches(Uri url)
    {
        if(url.Scheme!="https"||!url.IsDefaultPort||url.UserInfo.Length>0)return false;
        try
        {
            return Provider switch
            {
                NativeHostingProvider.Wistia=>new WistiaProvider().CanHandle(url)&&WistiaProvider.ParseIdentifier(url)==MediaId,
                NativeHostingProvider.JwPlatform=>new JwPlatformProvider().CanHandle(url)&&JwPlatformProvider.ParseAddress(url,SigningKey.Length>0).Id==MediaId,
                NativeHostingProvider.Brightcove=>BrightcoveAddress.IsHost(url)&&BrightcoveAddress.Parse(url) is { } b&&b.Account==AccountId&&b.MediaId==MediaId,
                NativeHostingProvider.Kaltura=>KalturaAddress.IsPlayer(url)&&KalturaAddress.Parse(url) is { } k&&k.PartnerId.ToString(System.Globalization.CultureInfo.InvariantCulture)==AccountId&&k.EntryId==MediaId,
                _=>false
            };
        }
        catch(Exception e)when(e is ArgumentException or NotSupportedException){return false;}
    }
    internal static bool IsHostingAddress(Uri url)=>new WistiaProvider().CanHandle(url)||new JwPlatformProvider().CanHandle(url)||BrightcoveAddress.IsHost(url)||KalturaAddress.IsPlayer(url);
    private static bool Match(string value,string pattern)=>Regex.IsMatch(value,"\\A(?:"+pattern+")\\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    public override string ToString()=>"Native hosting credentials [redacted]";
}
