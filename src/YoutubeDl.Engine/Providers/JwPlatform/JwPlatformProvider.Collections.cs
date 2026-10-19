using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Authentication;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;
namespace YoutubeDl.Engine.Providers.JwPlatform;
public sealed partial class JwPlatformProvider
{
    private static async Task<PageMetadata> ReadPlaylistAsync(MediaHttp http,NativeDownloadRequest request,JwAddress address,NativeHostingCredentials? credentials,CancellationToken ct)
    {
        var query=HostingCollectionPaging.Query(address.Endpoint);
        if(HostingCollectionPaging.Number(query,"page_offset",1)!=1)throw new NotSupportedException("A complete JW inventory starts at its first page; use native item selection.");
        var entries=new List<NativePlaylistEntry>();var ids=new HashSet<string>(StringComparer.Ordinal);var next=address.Endpoint;PageMetadata? first=null;long bytes=0;int? lastOffset=null,total=null;
        for(var page=0;page<256;page++)
        {
            ct.ThrowIfCancellationRequested();
            if(page>0&&credentials is {SigningKey.Length:0})throw new NotSupportedException("Signed JW pagination requires a publisher signing credential for each page.");
            var url=HostingDeliverySigning.Jw(next,credentials);var document=await http.GetDocumentAsync(url,8*1024*1024,ct,new Dictionary<string,string>{["Referer"]=string.IsNullOrWhiteSpace(request.Referer)?request.Url.AbsoluteUri:request.Referer}).ConfigureAwait(false);
            if(document.Url!=url)throw new InvalidDataException("JW changed its collection delivery resource.");
            if((bytes+=Encoding.UTF8.GetByteCount(document.Text))>32*1024*1024)throw new InvalidDataException("The complete JW inventory exceeds its byte limit.");
            using var json=JsonDocument.Parse(document.Text);var root=json.RootElement;var links=Object(root,"links");var continuation=Text(links,"next");
            if(HostingCollectionPaging.Count(root) is { } count){if(count<0||count>request.MaximumPlaylistEntries||total is { } oldCount&&count!=oldCount)throw new InvalidDataException("JW changed or exceeded its declared collection count.");total=count;}
            if(page>0||continuation is {Length:>0}){if(Text(root,"feedid")!=address.Id)throw new InvalidDataException("A JW page omitted the bound feed identity.");}
            if(Text(links,"last") is {Length:>0} last)
            {if(!Uri.TryCreate(address.Endpoint,last,out var lastUri))throw new InvalidDataException("JW returned an invalid final page.");var value=HostingCollectionPaging.Number(HostingCollectionPaging.Query(lastUri),"page_offset",-1);HostingCollectionPaging.Next(address.Endpoint,last,value,true);if(value<1||lastOffset is { } old&&old!=value)throw new InvalidDataException("JW changed its final page boundary.");lastOffset=value;}
            var slice=ParseDelivery(document.Text,address,request.MaximumPlaylistEntries,true);first??=slice;
            HostingCollectionPaging.Add(entries,ids,slice.Entries,request.MaximumPlaylistEntries);
            if(total is { } bound&&entries.Count>bound)throw new InvalidDataException("JW exceeded its declared collection count.");
            if(continuation is not {Length:>0})
            {if(lastOffset is { } final&&entries.Count<final||total is { } expected&&entries.Count!=expected)throw new InvalidDataException("JW ended before its complete declared inventory.");return first with {Entries=entries,RevalidateCollectionMembership=true};}
            next=HostingCollectionPaging.Next(address.Endpoint,continuation,entries.Count+1,true);
        }
        throw new InvalidDataException("JW collection paging exceeded its request limit.");
    }
}
