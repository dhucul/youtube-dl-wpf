using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YoutubeDl.Engine.Authentication;

internal static class HostingDeliverySigning
{
    internal static Uri Jw(Uri endpoint,NativeHostingCredentials? credentials,long? timestamp=null)
    {
        if(credentials is null)return endpoint;
        if(endpoint.Scheme!="https"||!endpoint.IsDefaultPort||endpoint.Host is not ("cdn.jwplayer.com" or "content.jwplatform.com" or "cdn.jwplatform.com")||endpoint.UserInfo.Length>0)throw new InvalidDataException("JW signing requires the selected secure delivery origin.");
        var values=new Dictionary<string,object>(StringComparer.Ordinal);
        foreach(var part in endpoint.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries))
        {
            var fields=part.Split('=',2);var name=Uri.UnescapeDataString(fields[0]);if(name is "token" or "exp" or "sig")continue;
            if(name is "resource"||!values.TryAdd(name,Uri.UnescapeDataString(fields.Length==2?fields[1]:"")))throw new InvalidDataException("JW delivery parameters are ambiguous.");
        }
        string token;
        if(credentials.SigningKey.Length>0)
        {
            var now=timestamp??DateTimeOffset.UtcNow.ToUnixTimeSeconds();values["resource"]=endpoint.AbsolutePath;values["exp"]=checked(((now+credentials.TokenLifetimeSeconds+299)/300)*300);
            var header=Encode(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));var payload=Encode(JsonSerializer.SerializeToUtf8Bytes(values));var text=header+"."+payload;var key=Encoding.UTF8.GetBytes(credentials.SigningKey);
            try{token=text+"."+Encode(HMACSHA256.HashData(key,Encoding.ASCII.GetBytes(text)));}finally{CryptographicOperations.ZeroMemory(key);}
        }
        else token=credentials.AccessToken;
        return new UriBuilder(endpoint){Query="token="+Uri.EscapeDataString(token)}.Uri;
    }
    private static string Encode(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
}
