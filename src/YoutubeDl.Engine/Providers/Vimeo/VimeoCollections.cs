using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Authentication;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Vimeo;

internal sealed record VimeoCollectionAddress(string Kind,string Key,string MetadataPath,string Connection,Uri Source)
{
    private static bool Match(string text,string pattern)=>Regex.IsMatch(text,pattern,RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    internal static bool IsApiResource(Uri url)=>url.Scheme=="https"&&url.Host=="api.vimeo.com"&&url.IsDefaultPort&&url.UserInfo.Length==0&&url.Fragment.Length==0&&Match(url.AbsolutePath,@"\A/(?:me(?:/(?:albums/[0-9]{1,15}(?:/videos)?|videos|likes|watchlater))?|users/[A-Za-z0-9_-]{1,64}(?:/(?:albums/[0-9]{1,15}(?:/videos)?|videos|likes|watchlater))?|(?:channels|groups)/[A-Za-z0-9_-]{1,64}(?:/videos)?)\z");
    internal static bool TryParse(Uri url,out VimeoCollectionAddress? address)
    {
        address=null;if(!VimeoAddress.IsVimeoHost(url)||url.Host is not ("vimeo.com" or "www.vimeo.com" or "api.vimeo.com"))return false;var path=url.AbsolutePath.TrimEnd('/');var parts=path.Split('/',StringSplitOptions.RemoveEmptyEntries);string kind,key,metadata,connection="videos";
        if(url.Host=="api.vimeo.com")
        {
            if(parts.Length==4&&parts[0]=="users"&&parts[2]=="albums"&&Match(parts[1],@"\A[0-9]{1,15}\z")&&Match(parts[3],@"\A[0-9]{1,15}\z")){kind="album";key=parts[3].TrimStart('0');metadata="/users/"+parts[1].TrimStart('0')+"/albums/"+key;}
            else return false;
        }
        else if(parts.Length==2&&parts[0] is "album" or "showcase"&&Match(parts[1],@"\A[0-9]{1,15}\z")){kind="album";key=parts[1].TrimStart('0');metadata="/me/albums/"+key;}
        else if(parts.Length is 2 or 3&&parts[0] is "channels" or "groups"&&(parts.Length==2||parts[2]=="videos")&&Match(parts[1],@"\A[A-Za-z0-9_-]{1,64}\z")){kind=parts[0]=="channels"?"channel":"group";key=parts[1];metadata="/"+parts[0]+"/"+key;}
        else if(parts.Length==1&&parts[0]=="watchlater"){kind="watchlater";key="me";metadata="/me";connection="watchlater";}
        else if(parts.Length is 1 or 2&&Match(parts[0],@"\A[A-Za-z][A-Za-z0-9_-]{0,63}\z")&&(parts.Length==1||parts[1] is "videos" or "likes" or "watchlater")&&!new[]{"ondemand","event","events","categories","channels","groups","album","showcase","manage","log_in","join","search","upload","settings","help","features"}.Contains(parts[0],StringComparer.OrdinalIgnoreCase))
        {connection=parts.Length==2?parts[1]:"videos";kind=connection=="videos"?"user":connection;key=parts[0].StartsWith("user",StringComparison.Ordinal)&&Match(parts[0][4..],@"\A[0-9]{1,15}\z")?parts[0][4..].TrimStart('0'):parts[0];metadata=key=="me"?"/me":"/users/"+key;}
        else return false;
        if(key.Length==0||url.Query.Length>0||url.Fragment.Length>0)throw new ArgumentException("Use a canonical Vimeo collection without paging or access-query parameters.");address=new(kind,key,metadata,connection,new UriBuilder(url){Scheme="https",Port=-1,Path=path}.Uri);return true;
    }
    internal string Bind(JsonElement metadata)
    {
        var uri=Text(metadata,"uri")??throw new InvalidDataException("Vimeo collection metadata has no identity.");string pattern=Kind switch{"album"=>@"\A/users/[0-9]{1,15}/albums/"+Regex.Escape(Key)+@"\z","channel"=>@"\A/channels/[0-9]{1,15}\z","group"=>@"\A/groups/[0-9]{1,15}\z",_=>@"\A/users/[0-9]{1,15}\z"};if(!Match(uri,pattern))throw new InvalidDataException("The API returned a different collection kind or identity.");
        if(Kind=="album"){if(MetadataPath.StartsWith("/users/",StringComparison.Ordinal)&&uri!=MetadataPath)throw new InvalidDataException("The showcase owner changed.");}
        else if(Match(Key,@"\A[0-9]+\z")&&uri!=MetadataPath)throw new InvalidDataException("Vimeo changed its numeric collection identity.");
        else if(MetadataPath!="/me"&&uri!=MetadataPath)
        {
            var link=Text(metadata,"link");if(!Uri.TryCreate(link,UriKind.Absolute,out var target)||!TryParse(target,out var alias)||alias!.Kind!=(Kind is "likes" or "watchlater"?"user":Kind)||alias.Key!=Key)throw new InvalidDataException("The Vimeo collection alias did not bind to its returned identity.");
        }
        return uri;
    }
}
public sealed partial class VimeoProvider
{
    private static async Task<PageMetadata> ReadCollectionAsync(VimeoCollectionAddress address,NativeDownloadRequest request,MediaHttp owner,CancellationToken ct)
    {
        if(request.MaximumPlaylistEntries is <1 or >10000)throw new ArgumentOutOfRangeException(nameof(request.MaximumPlaylistEntries));if(request.Authentication.OAuth is null&&request.Authentication.AccessToken.Length==0)throw new NotSupportedException("Vimeo collection inventories require a supplied API token or Vimeo OAuth session. Web showcases use the authorized account's showcase route; owner-qualified API showcase URLs are also supported.");
        using var controlled=owner.CreateDiscoverySession(true);var oauth=request.Authentication.OAuth is { } credentials?owner.SessionState("oauth:vimeo",()=>new NativeOAuthSession(owner,credentials)):null;
        async Task<MediaDocument> Read(string path){var url=new Uri("https://api.vimeo.com"+path);if(!VimeoCollectionAddress.IsApiResource(url))throw new InvalidDataException("The selected collection API route is unsupported.");var doc=oauth is not null?await oauth.GetAsync(url,ct).ConfigureAwait(false):await controlled.GetDocumentAsync(url,8*1024*1024,ct,new Dictionary<string,string>{["Authorization"]="Bearer "+request.Authentication.AccessToken,["Accept"]="application/vnd.vimeo.*+json;version=3.4"}).ConfigureAwait(false);ct.ThrowIfCancellationRequested();if(doc.Url!=url)throw new InvalidDataException("Vimeo redirected its selected collection API resource.");return doc;}
        MediaDocument? accountDocument=null;string? accountId=null;if(address.MetadataPath=="/me"||address.MetadataPath.StartsWith("/me/",StringComparison.Ordinal)){if(oauth is not null){await oauth.EnsureAsync(ct).ConfigureAwait(false);accountId=oauth.Credentials.AccountId;}else{accountDocument=await Read("/me").ConfigureAwait(false);using var account=JsonDocument.Parse(accountDocument.Text);accountId=Text(account.RootElement,"uri");}if(accountId is null||!Regex.IsMatch(accountId,@"\A/users/[0-9]{1,15}\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))throw new InvalidDataException("Vimeo did not identify the selected collection account.");}
        var first=address.MetadataPath=="/me"&&accountDocument is not null?accountDocument:await Read(address.MetadataPath).ConfigureAwait(false);using var metadata=JsonDocument.Parse(first.Text);var root=metadata.RootElement;var identity=address.Bind(root);if(accountId is not null&&identity!=(address.Kind=="album"?accountId+"/albums/"+address.Key:accountId))throw new InvalidDataException("The self-scoped Vimeo collection changed its account owner.");var connection=Object(Object(Object(root,"metadata"),"connections"),address.Connection);var total=Long(connection,"total");if(total is null||total>request.MaximumPlaylistEntries)throw new InvalidDataException("The Vimeo collection has an invalid or excessive declared count.");var inventory=Text(connection,"uri");if(inventory!=identity+"/"+address.Connection)throw new InvalidDataException("The collection's video connection points to another resource.");var entries=new List<NativePlaylistEntry>();var ids=new HashSet<string>(StringComparer.Ordinal);
        for(var page=1;;page++)
        {
            ct.ThrowIfCancellationRequested();if(page>1000)throw new InvalidDataException("Vimeo collection pagination exceeded its bound.");var document=await Read(inventory+"?per_page=100&page="+page.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);using var data=JsonDocument.Parse(document.Text);var body=data.RootElement;if(Long(body,"total")!=total||Long(body,"page")!=page||Long(body,"per_page")!=100||!body.TryGetProperty("data",out var rows)||rows.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Vimeo collection paging/count context changed.");
            foreach(var row in rows.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();if(entries.Count>=total||entries.Count>=request.MaximumPlaylistEntries)throw new InvalidDataException("The Vimeo inventory exceeded its declared count.");var raw=Text(row,"uri");var match=Regex.Match(raw??"",@"\A/videos/(?<id>[0-9]{1,15})(?::(?<hash>[A-Za-z0-9]{1,64}))?\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));if(!match.Success||match.Groups["id"].Value.Trim('0').Length==0)throw new InvalidDataException("Vimeo collection returned an invalid video identity.");var id=match.Groups["id"].Value.TrimStart('0');if(!ids.Add(id))throw new InvalidDataException("The Vimeo collection repeated a video or changed source order.");var hash=match.Groups["hash"].Success?match.Groups["hash"].Value:null;
                if(Text(row,"link") is { } link){if(!Uri.TryCreate(link,UriKind.Absolute,out var url)||!VimeoAddress.IsVimeoHost(url))throw new InvalidDataException("Invalid Vimeo member link.");var selected=VimeoAddress.Parse(url);if(selected.VideoId!=id||hash is not null&&selected.UnlistedHash is not null&&hash!=selected.UnlistedHash)throw new InvalidDataException("The Vimeo member link changed its video identity.");hash??=selected.UnlistedHash;}
                var video=new Uri("https://vimeo.com/"+id+(hash is null?"":"/"+hash));var picture=Array(Object(row,"pictures"),"sizes").OrderByDescending(item=>Number(item,"width")??0).Select(item=>Text(item,"link")).FirstOrDefault(value=>Uri.TryCreate(value,UriKind.Absolute,out var u)&&u.Scheme=="https"&&u.UserInfo.Length==0);var available=Text(row,"status") is null or "available";entries.Add(new(entries.Count+1,id,Text(row,"name")??"Vimeo "+id,video,Number(row,"duration"),picture,available,ExpectedArchiveIdentity:new("vimeo",id),CollectionMemberId:id));
            }
            var paging=Object(body,"paging");if(!paging.TryGetProperty("next",out var next))throw new InvalidDataException("Vimeo omitted explicit pagination completion.");if(next.ValueKind==JsonValueKind.Null){if(entries.Count!=total)throw new InvalidDataException("Vimeo ended before its complete inventory.");break;}if(next.ValueKind!=JsonValueKind.String||rows.GetArrayLength()==0||entries.Count>=total)throw new InvalidDataException("Vimeo advertised an invalid or nonprogressing continuation.");ValidateNext(next.GetString()!,inventory,page+1);
        }
        return new(){Extractor="vimeo:"+address.Kind,Id=identity+":"+address.Connection,PlaylistId=identity+":"+address.Connection,IsPlaylist=true,Title=Text(root,"name")??"Vimeo "+address.Kind,Description=Text(root,"description"),Entries=entries,SourcePageUrl=address.Source,RevalidateCollectionMembership=true};
    }
    private static void ValidateNext(string next,string path,int page)
    {
        if(next.Length>4096||!Uri.TryCreate(new Uri("https://api.vimeo.com"),next,out var url)||!VimeoCollectionAddress.IsApiResource(url)||url.AbsolutePath!=path)throw new InvalidDataException("Vimeo continuation left the selected collection.");var fields=new Dictionary<string,string>(StringComparer.Ordinal);foreach(var part in url.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries)){var pair=part.Split('=',2);var key=Uri.UnescapeDataString(pair[0]);var value=Uri.UnescapeDataString(pair.Length==2?pair[1]:"");if(key is not ("page" or "per_page")||!fields.TryAdd(key,value))throw new InvalidDataException("Vimeo continuation changed its query context.");}if(fields.GetValueOrDefault("page")!=page.ToString(CultureInfo.InvariantCulture)||fields.GetValueOrDefault("per_page") is { } size&&size!="100")throw new InvalidDataException("Vimeo continuation repeated or changed page size.");
    }
}
