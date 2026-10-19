using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;
namespace YoutubeDl.Engine.Providers.Wistia;
public sealed partial class WistiaProvider
{
    internal static bool IsLegacyPlaylist(Uri url)=>IsHost(url)&&Regex.IsMatch(url.AbsolutePath,@"\A/embed/playlists/[a-z0-9]{10}(?:\.json|\.jsonp)?/?\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private static async Task<PageMetadata> ReadLegacyPlaylistAsync(NativeDownloadRequest request,MediaHttp http,CancellationToken ct)
    {
        if(request.Authentication.IsConfigured)throw new NotSupportedException("Exact Wistia media credentials do not grant a legacy playlist.");
        var id=request.Url.AbsolutePath.TrimEnd('/').Split('/')[^1].Split('.')[0];var query=HostingCollectionPaging.Query(request.Url);
        if(query.Keys.Any(k=>k is not ("theme" or "width" or "height" or "version")))throw new NotSupportedException("This legacy Wistia navigation/access parameter is unsupported.");
        var url=new Uri("https://fast.wistia.com/embed/playlists/"+id+".json");var response=await http.GetDocumentAsync(url,8*1024*1024,ct).ConfigureAwait(false);if(response.Url!=url)throw new InvalidDataException("Wistia changed its legacy playlist resource.");
        return ParseLegacyPlaylist(response.Text,id,request.MaximumPlaylistEntries) with {SourcePageUrl=request.Url};
    }
    internal static PageMetadata ParseLegacyPlaylist(string json,string expected,int maximum=1000)
    {
        if(maximum is <1 or >10000||Encoding.UTF8.GetByteCount(json)>8*1024*1024)throw new InvalidDataException("The legacy playlist exceeds its limit.");
        using var document=JsonDocument.Parse(json);var root=document.RootElement;if(root.ValueKind!=JsonValueKind.Array||root.GetArrayLength()>1000)throw new InvalidDataException("Wistia returned an invalid legacy section inventory.");
        var entries=new List<NativePlaylistEntry>();var sections=new HashSet<long>();var members=new HashSet<long>();
        foreach(var section in root.EnumerateArray())
        {
            var sectionId=Long(section,"id");if(sectionId is null||!sections.Add(sectionId.Value)||True(section,"hasMore")||!section.TryGetProperty("medias",out var items)||items.ValueKind!=JsonValueKind.Array||HostingCollectionPaging.Count(section) is { } count&&count!=items.GetArrayLength())throw new InvalidDataException("Wistia returned an incomplete or repeated legacy section.");
            foreach(var item in items.EnumerateArray())
            {
                var member=Long(item,"id");if(member is null||!members.Add(member.Value)||entries.Count>=maximum)throw new InvalidDataException("Legacy member identities are repeated or exceed the limit.");
                var config=Object(item,"embed_config");var media=Object(config,"media");var id=Text(media,"hashedId");var title=Text(item,"name")??"Unavailable Wistia media";
                if(id is null){entries.Add(new(entries.Count+1,"member-"+member,title,null,null,null,false,CollectionMemberId:"wistia:member:"+member));continue;}
                if(!Regex.IsMatch(id,@"\A[a-z0-9]{10}\z")||Long(media,"mediaId")!=member||Text(item,"type") is not ("Video" or "Audio"))throw new InvalidDataException("Wistia changed its declared member/media identity.");
                var available=!True(media,"protected")&&!True(item,"isPrivate")&&!True(item,"requiresRegistration")&&!True(Object(Object(Object(config,"options"),"plugin"),"passwordProtectedVideo"),"on");
                entries.Add(new(entries.Count+1,id,title,new("https://fast.wistia.com/embed/iframe/"+id),Number(media,"duration"),Text(item,"thumbnail"),available,ExpectedArchiveIdentity:new("wistia",id),CollectionMemberId:"wistia:member:"+member));
            }
        }
        return new(){Id=expected,PlaylistId=expected,Extractor="wistia:playlist",Title=root.GetArrayLength()==1?Text(root[0],"name")??expected:expected,IsPlaylist=true,Entries=entries,RevalidateCollectionMembership=true};
    }
}
