using System.Text;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;
namespace YoutubeDl.Engine.Providers.Brightcove;
public sealed partial class BrightcoveProvider
{
    private static async Task<PageMetadata> ReadPlaylistAsync(MediaHttp http,NativeDownloadRequest request,BrightcoveAddress address,IReadOnlyDictionary<string,string> headers,CancellationToken ct)
    {
        var entries=new List<NativePlaylistEntry>();var ids=new HashSet<string>(StringComparer.Ordinal);PageMetadata? first=null;int? total=null;long bytes=0;
        for(var page=0;page<256;page++)
        {
            ct.ThrowIfCancellationRequested();var offset=entries.Count;var url=new UriBuilder(address.Playback){Query="limit=100&offset="+offset}.Uri;
            var document=await http.GetDocumentAsync(url,8*1024*1024,ct,headers).ConfigureAwait(false);if(document.Url!=url)throw new InvalidDataException("Brightcove changed its playlist API resource.");
            if((bytes+=Encoding.UTF8.GetByteCount(document.Text))>32*1024*1024)throw new InvalidDataException("The complete Brightcove inventory exceeds its byte limit.");
            using var json=Parse(document.Text);var root=json.RootElement;
            if(!root.TryGetProperty("videos",out var videos)||videos.ValueKind!=System.Text.Json.JsonValueKind.Array)throw new InvalidDataException("Brightcove omitted its explicit video inventory.");
            if(HostingCollectionPaging.Count(root) is { } count){if(count<0||count>request.MaximumPlaylistEntries||total is { } old&&old!=count)throw new InvalidDataException("Brightcove changed or exceeded its collection count.");total=count;}
            var slice=ParsePlayback(document.Text,address,request.MaximumPlaylistEntries,true);first??=slice;
            HostingCollectionPaging.Add(entries,ids,slice.Entries,request.MaximumPlaylistEntries);
            if(total is { } bound&&entries.Count>bound)throw new InvalidDataException("Brightcove exceeded its declared collection count.");
            var continuation=Text(root,"next_link");if(continuation is {Length:>0})HostingCollectionPaging.Next(url,continuation,entries.Count,false);
            var complete=total is { } expected?entries.Count==expected:slice.Entries.Count<100&&continuation is not {Length:>0};
            if(complete){if(continuation is {Length:>0})throw new InvalidDataException("Brightcove advertised more than its declared count.");return first with {Entries=entries,RevalidateCollectionMembership=true};}
            if(slice.Entries.Count==0)throw new InvalidDataException("Brightcove ended before its complete inventory.");
        }
        throw new InvalidDataException("Brightcove collection paging exceeded its request limit.");
    }
}
