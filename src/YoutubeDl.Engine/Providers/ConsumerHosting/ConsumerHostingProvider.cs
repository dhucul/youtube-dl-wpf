using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;
namespace YoutubeDl.Engine.Providers.ConsumerHosting;
/// <summary>Article-bound publisher state delegates only primary video, excluding ads and related cards.</summary>
public sealed class ConsumerHostingProvider:IMediaProvider
{
    public MediaProviderInfo Info {get;}=new("consumer-hosting","Consumer articles with declared hosted video",150);
    public NativeAuthenticationCapabilities AuthenticationCapabilities=>NativeAuthenticationCapabilities.Hosting;
    public bool CanHandle(Uri url)=>url.Scheme is "http" or "https"&&url.UserInfo.Length==0&&url.IsDefaultPort&&url.Host is "www.businessinsider.com" or "businessinsider.com" or "www.aljazeera.com" or "aljazeera.com";
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request,MediaHttp http,CancellationToken ct)
    {
        if(!CanHandle(request.Url))throw new ArgumentException("This consumer host is unsupported.");var document=await http.GetDocumentAsync(request.Url,5*1024*1024,ct).ConfigureAwait(false);
        if(!CanHandle(document.Url)||Publisher(document.Url)!=Publisher(request.Url))throw new InvalidDataException("The consumer page redirected outside its publisher.");return Parse(document.Text,document.Url);
    }
    internal static PageMetadata Parse(string html,Uri page)=>page.Host.Contains("businessinsider",StringComparison.Ordinal)?BusinessInsider(html,page):AlJazeera(html,page);
    private static PageMetadata BusinessInsider(string html,Uri page)
    {
        using var document=BoundedPageState.Read(html,"fenrirClientData","fenrir-client-data");var root=document.RootElement;
        if(Text(root,"path")!=page.AbsolutePath.TrimEnd('/')||Text(root,"type")!="post"||Text(root,"id")!=page.AbsolutePath.Trim('/'))throw new InvalidDataException("Business Insider returned another article identity.");
        var meta=Object(root,"meta");if(True(meta,"hidden")||!True(meta,"hasHeroVideo")||!True(Object(meta,"paywall"),"isExempt"))throw new NotSupportedException("This article does not declare accessible public primary video.");
        var video=Object(Object(Object(root,"relationships"),"video"),"data");var id=Text(Object(Object(video,"meta"),"jwplayer"),"assetID");
        if(id is null||!Regex.IsMatch(id,@"\A[A-Za-z0-9]{8}\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))throw new InvalidDataException("The article omitted its primary JW identity.");
        return Delegate("BusinessInsider",page,Text(Object(video,"attributes"),"title")??PageMetadataReader.Read(html,page).Title,new("https://cdn.jwplayer.com/v2/media/"+id),new("jwplatform",id));
    }
    private static PageMetadata AlJazeera(string html,Uri page)
    {
        using var document=BoundedPageState.Read(html,"__APOLLO_STATE__",base64:true);var root=document.RootElement;var posts=root.EnumerateObject().Where(p=>p.Name.StartsWith("Post:",StringComparison.Ordinal)&&Text(p.Value,"link") is { } path&&MatchesArticle(page,path)).ToArray();
        if(posts.Length!=1)throw new InvalidDataException("Al Jazeera omitted one matching article identity.");var post=posts[0].Value;
        if(True(post,"isLive")||Text(post,"externalUrl") is {Length:>0})throw new NotSupportedException("This live or external article requires another native workflow.");
        var reference=Text(Object(post,"video"),"__ref");if(reference is null||!reference.StartsWith("Video:",StringComparison.Ordinal)||!root.TryGetProperty(reference,out var video))throw new InvalidDataException("The article's primary video reference is missing.");
        var id=Text(video,"id");var account=Text(video,"accountId");var player=Text(video,"playerId");
        if(id is null||account is null||player is null||reference!="Video:"+id||!Match(id,@"[0-9]{1,20}")||!Match(account,@"[0-9]{1,20}")||!Match(player,@"[A-Za-z0-9_-]{1,80}"))throw new InvalidDataException("The article's Brightcove identity is invalid.");
        return Delegate("AlJazeera",page,Text(post,"title")??id,new("https://players.brightcove.net/"+account+"/"+player+"_default/index.html?videoId="+id),new("brightcovenew",id));
    }
    private static string Publisher(Uri url)=>url.Host.StartsWith("www.",StringComparison.Ordinal)?url.Host[4..]:url.Host;
    private static bool MatchesArticle(Uri page,string path)=>Uri.TryCreate(page,path,out var link)&&link.Scheme==page.Scheme&&link.IsDefaultPort&&link.UserInfo.Length==0&&Publisher(link)==Publisher(page)&&link.AbsolutePath.TrimEnd('/')==page.AbsolutePath.TrimEnd('/');
    private static bool Match(string value,string pattern)=>Regex.IsMatch(value,"\\A(?:"+pattern+")\\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private static PageMetadata Delegate(string key,Uri page,string title,Uri player,Download.DownloadArchiveIdentity expected)=>new(){Id=page.AbsolutePath.Trim('/'),Extractor="consumer-hosting",Title=title,EmbeddedPages=[player],SourcePageUrl=page,DelegatedArchiveIdentity=expected,MetadataFields=new Dictionary<string,object?>{["consumer_key"]=key,["consumer_page"]=page.AbsoluteUri}};
}
