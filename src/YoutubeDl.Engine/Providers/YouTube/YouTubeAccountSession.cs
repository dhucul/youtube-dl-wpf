using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Providers.YouTube;

/// <summary>Owns one cookie-selected account context; identity digests are confined to YouTube API requests.</summary>
internal sealed class YouTubeAccountSession:IDisposable
{
    private readonly MediaHttp _owner;private readonly MediaHttp _http;private readonly int? _requestedIndex;
    private string? _binding;private JsonElement _config;private Uri? _anchor;
    internal YouTubeAccountSession(MediaHttp owner,int? requestedIndex){_owner=owner;_requestedIndex=requestedIndex;_http=owner.CreateDiscoverySession(requestedIndex is not null||Cookie(default) is not null);}
    internal Task<MediaDocument> ReadPageAsync(Uri url,CancellationToken ct)=>_http.GetDocumentAsync(url,8*1024*1024,ct);
    internal bool Authenticated { get; private set; }
    internal string WebVersion=>Text(_config,"INNERTUBE_CLIENT_VERSION")??"";
    internal void Observe(string html,Uri anchor,CancellationToken ct)
    {
        if(anchor.Scheme!="https"||anchor.Host!="www.youtube.com"||!anchor.IsDefaultPort||anchor.UserInfo.Length>0)throw new InvalidDataException("An account context requires the selected secure YouTube origin.");
        var config=YouTubePageJson.ReadConfiguration(html);var cookie=Cookie(ct);var logged=config.TryGetProperty("LOGGED_IN",out var value)&&value.ValueKind==JsonValueKind.True;
        if(_binding is not null&&(cookie is null||!logged))throw new NotSupportedException("The selected YouTube account session expired or signed out.");
        if(cookie is not null||_requestedIndex is not null)
        {
            if(cookie is null||!logged)throw new NotSupportedException("The selected YouTube account requires fresh signed-in cookies.");
            var index=Index(config);if(_requestedIndex is { } requested&&index!=requested)throw new InvalidDataException("YouTube selected another account index.");
            var dataSync=Text(config,"DATASYNC_ID");var userSession=Text(config,"USER_SESSION_ID");
            if(string.IsNullOrWhiteSpace(dataSync)&&string.IsNullOrWhiteSpace(userSession))throw new NotSupportedException("The selected YouTube account did not expose a stable identity marker.");
            var binding=JsonSerializer.Serialize(new[]{index.ToString(CultureInfo.InvariantCulture),Text(config,"DELEGATED_SESSION_ID"),dataSync,userSession});
            if(_binding is not null&&_binding!=binding)throw new InvalidDataException("The YouTube account or brand identity changed during this job.");
            _binding=binding;Authenticated=true;
        }
        _anchor=anchor;_config=config;
    }
    internal static Uri WithoutSelection(Uri url)=>new UriBuilder(url){Query=string.Join('&',url.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Where(part=>Uri.UnescapeDataString(part.Split('=')[0])!="authuser"))}.Uri;
    internal Uri Select(Uri url)
    {
        if(_requestedIndex is not { } index)return url;
        var selected=WithoutSelection(url);return new UriBuilder(selected){Query=selected.Query.TrimStart('?')+(selected.Query.Length==0?"":"&")+"authuser="+index.ToString(CultureInfo.InvariantCulture)}.Uri;
    }
    internal IReadOnlyDictionary<string,string> Headers(string clientName,string version,CancellationToken ct,long? timestamp=null)
    {
        var headers=new Dictionary<string,string>{["X-YouTube-Client-Name"]=clientName,["X-YouTube-Client-Version"]=version};
        if(!Authenticated)return headers;
        var cookie=Cookie(ct)??throw new NotSupportedException("The YouTube account cookies expired.");var time=timestamp??DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var bytes=Encoding.UTF8.GetBytes(time.ToString(CultureInfo.InvariantCulture)+" "+cookie+" https://www.youtube.com");string hash;
        try{hash=Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();}finally{CryptographicOperations.ZeroMemory(bytes);}
        headers["Authorization"]="SAPISIDHASH "+time.ToString(CultureInfo.InvariantCulture)+"_"+hash;headers["X-Goog-AuthUser"]=Index(_config).ToString(CultureInfo.InvariantCulture);headers["Origin"]="https://www.youtube.com";headers["X-Origin"]="https://www.youtube.com";
        if(Text(_config,"DELEGATED_SESSION_ID") is {Length:>0} delegated)headers["X-Goog-PageId"]=delegated;
        if(Text(_config,"VISITOR_DATA") is {Length:>0} visitor)headers["X-Goog-Visitor-Id"]=visitor;
        return headers;
    }
    internal async Task<string> PostAsync(string method,object body,string clientName,string version,long maximum,CancellationToken ct)
    {
        if(method is not ("player" or "browse" or "search"))throw new ArgumentException("Unsupported account API method.");
        var root=JsonSerializer.SerializeToNode(body)!.AsObject();if(Authenticated&&Text(_config,"DELEGATED_SESSION_ID") is {Length:>0} delegated)
            root["context"]!.AsObject()["user"]=new JsonObject{["onBehalfOfUser"]=delegated};
        for(var attempt=0;;attempt++)
        {
            try{return await _http.PostJsonAsync(new("https://www.youtube.com/youtubei/v1/"+method+"?prettyPrint=false"),root,maximum,Headers(clientName,version,ct),ct).ConfigureAwait(false);}
            catch(HttpRequestException e)when(Authenticated&&attempt==0&&e.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {var document=await _http.GetDocumentAsync(_anchor!,8*1024*1024,ct).ConfigureAwait(false);if(document.Url!=_anchor)throw new InvalidDataException("The account refresh redirected outside its selected page.");Observe(document.Text,_anchor!,ct);}
        }
    }
    internal async Task<YoutubeDl.Engine.Discovery.PageMetadata> PlayerAsync(string id,CancellationToken ct)
    {
        if(WebVersion.Length is 0 or >128)throw new NotSupportedException("The authenticated page did not provide a supported Web client context.");
        var json=await PostAsync("player",new{videoId=id,context=new{client=new{clientName="WEB",clientVersion=WebVersion,hl="en"}}},"1",WebVersion,2*1024*1024,ct).ConfigureAwait(false);
        return YouTubePlayerParser.ParseResponse(json,id);
    }
    private string? Cookie(CancellationToken ct)
    {
        var jar=_owner.YouTubeCookies(ct).ToContainer(ct);var cookies=jar.GetCookies(new Uri("https://www.youtube.com/youtubei/v1/player")).Cast<Cookie>().Where(c=>c.Secure).ToArray();
        foreach(var name in new[]{"SAPISID","__Secure-3PAPISID"})
        {var values=cookies.Where(c=>c.Name==name).Select(c=>c.Value).Distinct(StringComparer.Ordinal).ToArray();if(values.Length>1)throw new InvalidDataException("Ambiguous YouTube account cookie scope.");if(values.Length==1&&values[0].Length>0)return values[0];}return null;
    }
    private static string? Text(JsonElement root,string name)
    {if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty(name,out var value))return null;var text=value.ValueKind==JsonValueKind.String?value.GetString():value.ValueKind==JsonValueKind.Number?value.GetRawText():null;if(text is {Length:>2048}||text?.Any(char.IsControl)==true)throw new InvalidDataException("The account context field is invalid.");return text;}
    private static int Index(JsonElement root)=>int.TryParse(Text(root,"SESSION_INDEX")??"0",NumberStyles.None,CultureInfo.InvariantCulture,out var value)&&value is >=0 and <=9?value:throw new InvalidDataException("The selected account index is invalid.");
    public override string ToString()=>"YouTube account session [redacted]";
    public void Dispose()=>_http.Dispose();
}
