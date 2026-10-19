using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;
namespace YoutubeDl.Engine.Providers.Nowness;
/// <summary>Public English publisher collections and declared hosted primary videos.</summary>
public sealed partial class NownessProvider:IMediaProvider
{
    private const string Account="2385340575001",Player="69d86acb-f5b0-4ada-b67a-8a4a383e38ab";
    public MediaProviderInfo Info {get;}=new("nowness","Nowness public posts and series",160);
    public NativeAuthenticationCapabilities AuthenticationCapabilities=>NativeAuthenticationCapabilities.Hosting;
    public bool CanHandle(Uri url)=>url.Scheme=="https"&&url.IsDefaultPort&&url.UserInfo.Length==0&&url.Host is "www.nowness.com" or "nowness.com";
    internal static (string Kind,string Slug,string? Series,string? Playlist) Address(Uri url)
    {
        var playlist=Regex.Match(url.AbsolutePath,@"\A/playlist/(?<id>[1-9][0-9]{0,17})(?:/(?<slug>[^/]{1,200}))?/?\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        if(playlist.Success)
        {
            var slug=Uri.UnescapeDataString(playlist.Groups["slug"].Value);
            if(slug.Any(char.IsControl)||slug.Contains('/')||HostingCollectionPaging.Query(url).Keys.Any(k=>!k.StartsWith("utm_",StringComparison.Ordinal)&&k is not ("fbclid" or "gclid")))throw new NotSupportedException("Nowness private/preview/navigation parameters require another native contract.");
            return ("playlist",slug,null,playlist.Groups["id"].Value);
        }
        var match=Regex.Match(url.AbsolutePath,@"\A/(?<kind>story|series)/(?<slug>[a-z0-9][a-z0-9-]{0,199})(?:/(?<post>[a-z0-9][a-z0-9-]{0,199}))?/?\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        if(!match.Success||match.Groups["kind"].Value=="story"&&match.Groups["post"].Success)throw new NotSupportedException("This Nowness route requires another native contract.");
        if(HostingCollectionPaging.Query(url).Keys.Any(k=>!k.StartsWith("utm_",StringComparison.Ordinal)&&k is not ("fbclid" or "gclid")))throw new NotSupportedException("Nowness preview/account/navigation parameters require another native contract.");
        return match.Groups["post"].Success?("story",match.Groups["post"].Value,match.Groups["slug"].Value,null):(match.Groups["kind"].Value,match.Groups["slug"].Value,null,null);
    }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request,MediaHttp http,CancellationToken ct)
    {
        if(!CanHandle(request.Url))throw new ArgumentException("The Nowness publisher origin is unsupported.");var address=Address(request.Url);
        if(address.Kind=="playlist")return await ReadPlaylistAsync(request,http,ct).ConfigureAwait(false);
        if(address.Kind=="series"&&request.Authentication.IsConfigured)throw new NotSupportedException("Scoped hosted credentials apply to an exact Nowness story, not a public series inventory.");
        var endpoint=new Uri("https://api.nowness.com/api/"+(address.Kind=="story"?"post":"series")+"/getBySlug/"+address.Slug);
        var headers=new Dictionary<string,string>{["X-Nowness-Language"]="en-us",["Origin"]="https://www.nowness.com",["Referer"]=request.Url.AbsoluteUri};
        var response=await http.GetDocumentAsync(endpoint,8*1024*1024,ct,headers).ConfigureAwait(false);if(response.Url!=endpoint)throw new InvalidDataException("Nowness changed its publisher API resource.");
        return Parse(response.Text,request.Url,request.MaximumPlaylistEntries);
    }
    internal static PageMetadata Parse(string json,Uri url,int maximum=1000)
    {
        if(maximum is <1 or >10000||Encoding.UTF8.GetByteCount(json)>8*1024*1024)throw new InvalidDataException("The Nowness inventory exceeds its limit.");
        using var document=JsonDocument.Parse(json);var root=document.RootElement;var address=Address(url);var translation=Translation(root,address.Slug.Length==0?null:address.Slug,address.Kind=="playlist");
        if(address.Kind=="story")return Post(root,url,address.Slug);
        if(address.Kind=="playlist")ValidatePlaylist(root,address);
        var id=Long(root,"id")??throw new InvalidDataException("Nowness omitted its series identity.");
        if(!root.TryGetProperty("posts",out var items)||items.ValueKind!=JsonValueKind.Array||True(root,"hasMore")||Text(root,"next") is {Length:>0}||HostingCollectionPaging.Count(root) is { } count&&count!=items.GetArrayLength())throw new InvalidDataException("Nowness omitted a complete series inventory.");
        if(items.GetArrayLength()>maximum)throw new InvalidDataException("The complete Nowness series exceeds its configured limit.");
        var entries=new List<NativePlaylistEntry>();var ids=new Dictionary<long,string>();
        foreach(var item in items.EnumerateArray())
        {
            var postId=Long(item,"id")??throw new InvalidDataException("A Nowness member has no identity.");
            var t=Translation(item);var slug=Text(t,"slug")!;var story=new Uri("https://www.nowness.com/story/"+slug);var available=Public(item);var media=available?DeclaredVideo(item):null;
            available&=media is not null;
            var signature=slug+"|"+available+"|"+media?.Archive.Record;
            if(ids.TryGetValue(postId,out var old)&&old!=signature)throw new InvalidDataException("Repeated Nowness post identities disagree on playback or access.");ids[postId]=signature;
            entries.Add(new(entries.Count+1,media?.Id??"post-"+postId,Text(t,"title")??slug,story,Number(item,"contentDuration")/1000,Text(item,"thumbnailUrl"),available,ExpectedArchiveIdentity:media?.Archive,CollectionMemberId:"nowness:post:"+postId));
        }
        return new(){Id=id.ToString(),PlaylistId=id.ToString(),Extractor="nowness:"+address.Kind,Title=Text(translation,"title")??address.Slug,IsPlaylist=true,Entries=entries,SourcePageUrl=url,RevalidateCollectionMembership=true};
    }
    private static JsonElement Translation(JsonElement value,string? expected=null,bool memberSlug=false)
    {
        var found=Array(value,"translations").Where(t=>Text(t,"language")=="en-us").ToArray();if(found.Length!=1||Text(found[0],"slug") is not { } slug||(memberSlug?slug.Length is <1 or >200||slug.Any(char.IsControl)||slug.Contains('/'):!Regex.IsMatch(slug,@"\A[a-z0-9][a-z0-9-]{0,199}\z"))||expected is not null&&slug!=expected)throw new InvalidDataException("Nowness returned another or ambiguous English identity.");return found[0];
    }
    private static bool Public(JsonElement item)=>True(item,"isPublished")&&Text(item,"status")=="published"&&!True(item,"isComingSoon")&&!True(item,"isPage")&&Text(item,"type")=="video"&&Text(item,"iframeUrl") is not {Length:>0};
    private static (string Id,Uri Url,Download.DownloadArchiveIdentity Archive)? DeclaredVideo(JsonElement item)
    {
        var media=Array(item,"media").Where(m=>Text(m,"key")=="main"&&Text(m,"type")=="video").ToArray();
        if(media.Length!=1)throw new InvalidDataException("Nowness omitted one primary video.");var id=Text(media[0],"content")??Long(media[0],"content")?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if(id is null)throw new InvalidDataException("Nowness omitted its primary hosting identity.");
        var provider=Text(media[0],"source");if(provider is null||!Regex.IsMatch(provider,@"\A[a-z][a-z0-9_-]{0,31}\z"))throw new InvalidDataException("Nowness omitted a valid main hosting provider.");
        return provider switch
        {
            "brightcove" when Regex.IsMatch(id,@"\A[0-9]{1,20}\z")=>(id,new("https://players.brightcove.net/"+Account+"/"+Player+"_default/index.html?videoId="+id),new("brightcovenew",id)),
            "vimeo" when Regex.IsMatch(id,@"\A[0-9]{1,20}\z")=>(id,new("https://player.vimeo.com/video/"+id),new("vimeo",id)),
            "youtube" when Regex.IsMatch(id,@"\A[A-Za-z0-9_-]{11}\z")=>(id,new("https://www.youtube.com/watch?v="+id),new("youtube",id)),
            "brightcove" or "vimeo" or "youtube"=>throw new InvalidDataException("Nowness declared an invalid primary hosting identity."),
            _=>null
        };
    }
    private static PageMetadata Post(JsonElement item,Uri url,string slug)
    {
        var translation=Translation(item,slug);if(!Public(item))throw new NotSupportedException("This Nowness post does not declare public complete video.");
        if(Address(url).Series is { } parent&&!Array(item,"series").Any(s=>Array(s,"translations").Any(t=>Text(t,"language")=="en-us"&&Text(t,"slug")==parent)))throw new InvalidDataException("The Nowness post belongs to another series route.");
        var id=Long(item,"id")??throw new InvalidDataException("Nowness omitted the post identity.");var media=DeclaredVideo(item)??throw new NotSupportedException("This declared Nowness hosting platform requires another native workflow.");
        return new(){Id=id.ToString(),Extractor="nowness",Title=Text(translation,"title")??slug,SourcePageUrl=url,EmbeddedPages=[media.Url],DelegatedArchiveIdentity=media.Archive,MetadataFields=new Dictionary<string,object?>{["consumer_key"]="nowness",["consumer_page"]=url.AbsoluteUri,["consumer_post_id"]=id}};
    }
}
