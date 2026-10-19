using System.Text.Json;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Kaltura;

internal sealed class KalturaAccountSession(NativeDownloadRequest request,KalturaAddress address)
{
    private string? _ks,_binding;private DateTimeOffset? _expiry;private int _logins;
    internal async Task<string> GetAsync(MediaHttp http,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(_ks is not null&&(_expiry is null||_expiry>DateTimeOffset.UtcNow.AddSeconds(30)))return _ks;
        string? candidate;DateTimeOffset? candidateExpiry=null;string? candidateBinding=_binding;
        var supplied=request.Authentication.Hosting?.AccessToken??address.Session;
        if(request.Authentication.Username.Length>0)
        {
            if(++_logins>3)throw new NotSupportedException("The Kaltura account session exceeded its login renewal limit.");
            var arguments=new Dictionary<string,object?>{["loginId"]=request.Authentication.Username,["password"]=request.Authentication.Password,["partnerId"]=address.PartnerId,["expiry"]=3600};
            if(_logins==1&&request.Authentication.TwoFactor.Length>0)arguments["otp"]=request.Authentication.TwoFactor;
            using var result=KalturaProvider.Read(await KalturaProvider.Call("user","loginByLoginId",arguments,http,ct).ConfigureAwait(false));
            if(result.RootElement.ValueKind!=JsonValueKind.String)throw new InvalidDataException("Kaltura did not return its account session.");candidate=result.RootElement.GetString();
        }
        else if(supplied is not null)
        {
            if(_ks is not null)throw new NotSupportedException("The supplied Kaltura session expired; provide a fresh authorized session.");candidate=supplied;
        }
        else
        {
            if(++_logins>3)throw new NotSupportedException("The Kaltura widget session exceeded its renewal limit.");
            using var result=KalturaProvider.Read(await KalturaProvider.Call("session","startWidgetSession",new(){["widgetId"]="_"+address.PartnerId,["expiry"]=3600},http,ct).ConfigureAwait(false));
            if(Integer(result.RootElement,"partnerId")!=address.PartnerId)throw new InvalidDataException("Kaltura changed the widget partner identity.");candidate=Text(result.RootElement,"ks");candidateExpiry=DateTimeOffset.UtcNow.AddSeconds(3500);
        }
        if(string.IsNullOrWhiteSpace(candidate)||candidate.Length>8192||candidate.Any(char.IsControl))throw new InvalidDataException("Kaltura returned an invalid session.");
        if(request.Authentication.Username.Length>0||request.Authentication.Hosting is not null)
        {
            using var result=KalturaProvider.Read(await KalturaProvider.Call("session","get",new(){["ks"]=candidate,["session"]=candidate},http,ct).ConfigureAwait(false));var root=result.RootElement;
            var user=Text(root,"userId");var expiry=Long(root,"expiry");if(Integer(root,"partnerId")!=address.PartnerId||user is null||request.Authentication.Username.Length>0&&string.IsNullOrWhiteSpace(user)||user.Length>256||user.Any(char.IsControl)||expiry is null)throw new InvalidDataException("Kaltura omitted the selected account/session identity.");
            var binding=JsonSerializer.Serialize(new object?[]{address.PartnerId,user,Integer(root,"type")});if(_binding is not null&&_binding!=binding)throw new InvalidDataException("Kaltura account renewal changed the authorized user.");candidateBinding=binding;
            try{candidateExpiry=DateTimeOffset.FromUnixTimeSeconds(expiry.Value);}catch(ArgumentOutOfRangeException){throw new InvalidDataException("Kaltura session expiry is invalid.");}
            if(candidateExpiry<=DateTimeOffset.UtcNow)throw new NotSupportedException("Kaltura returned an expired account session.");
        }
        ct.ThrowIfCancellationRequested();_ks=candidate;_expiry=candidateExpiry;_binding=candidateBinding;return candidate;
    }
    internal void Invalidate()=>_expiry=DateTimeOffset.MinValue;
    public override string ToString()=>"Kaltura account state [redacted]";
}
