using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;
namespace YoutubeDl.Engine.Providers.Nowness;
public sealed partial class NownessProvider
{
    private static void ValidatePlaylist(JsonElement root,(string Kind,string Slug,string? Series,string? Playlist) address)
    {
        if(Long(root,"id")?.ToString(System.Globalization.CultureInfo.InvariantCulture)!=address.Playlist||!root.TryGetProperty("isPrivate",out var flag)||flag.ValueKind!=JsonValueKind.False||Text(root,"type") is not ("memberPlaylist" or "playlist"))throw new NotSupportedException("Nowness did not declare a matching public playlist.");
        Translation(root,address.Slug.Length==0?null:address.Slug,true);
    }
    private static async Task<PageMetadata> ReadPlaylistAsync(NativeDownloadRequest request,MediaHttp http,CancellationToken ct)
    {
        if(request.Authentication.IsConfigured)throw new NotSupportedException("Exact hosted media credentials do not grant a Nowness playlist.");
        var address=Address(request.Url);var endpoint=new Uri("https://api.nowness.com/api/playlist/"+address.Playlist);var headers=new Dictionary<string,string>{["X-Nowness-Language"]="en-us",["Origin"]="https://www.nowness.com",["Referer"]=request.Url.AbsoluteUri};
        var detail=await http.GetDocumentAsync(endpoint,8*1024*1024,ct,headers).ConfigureAwait(false);if(detail.Url!=endpoint)throw new InvalidDataException("Nowness changed its playlist resource.");
        using var document=JsonDocument.Parse(detail.Text);ValidatePlaylist(document.RootElement,address);
        var total=Integer(document.RootElement,"videosCount")??throw new InvalidDataException("Nowness omitted its playlist count.");if(total>request.MaximumPlaylistEntries)throw new InvalidDataException("The complete Nowness playlist exceeds its configured limit.");
        var members=new SortedDictionary<int,JsonNode>();var posts=new HashSet<long>();long bytes=Encoding.UTF8.GetByteCount(detail.Text);const int limit=8;
        for(var page=1;page<=256;page++)
        {
            ct.ThrowIfCancellationRequested();var url=new Uri("https://api.nowness.com/api/post?PlaylistId="+address.Playlist+"&page="+page+"&limit="+limit+"&isPublished=true&includeImages=false");
            var response=await http.GetDocumentAsync(url,8*1024*1024,ct,headers).ConfigureAwait(false);if(response.Url!=url||(bytes+=Encoding.UTF8.GetByteCount(response.Text))>32*1024*1024)throw new InvalidDataException("Nowness changed or exceeded its bounded member resource.");
            using var data=JsonDocument.Parse(response.Text);var root=data.RootElement;var meta=Object(root,"meta");
            var pages=total==0?0:(total+limit-1)/limit;
            if(Integer(meta,"page")!=page||Integer(meta,"limit")!=limit||Integer(meta,"totalItems")!=total||Integer(meta,"totalPages") is not { } declaredPages||(total==0?declaredPages is not (0 or 1):declaredPages!=pages)||!root.TryGetProperty("items",out var items)||items.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Nowness returned an incomplete or changed playlist page.");
            if(items.GetArrayLength()!=Math.Min(limit,total-(page-1)*limit))throw new InvalidDataException("Nowness omitted declared playlist members.");
            foreach(var item in items.EnumerateArray())
            {
                var index=Integer(item,"index");var id=Long(item,"id");
                if(index is null||index<0||index>=total||id is null||!members.TryAdd(index.Value,JsonNode.Parse(item.GetRawText())!))throw new InvalidDataException("Nowness playlist positions or post identities are invalid.");
            }
            if(page>=pages)
            {
                if(members.Count!=total||!members.Keys.SequenceEqual(Enumerable.Range(0,total)))throw new InvalidDataException("Nowness omitted a complete source-position inventory.");
                var combined=JsonNode.Parse(detail.Text)!;combined["posts"]=new JsonArray(members.Values.ToArray());return Parse(combined.ToJsonString(),request.Url,request.MaximumPlaylistEntries);
            }
        }
        throw new InvalidDataException("Nowness playlist paging exceeded its request limit.");
    }
}
