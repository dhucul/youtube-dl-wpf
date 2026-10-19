using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Authentication;
using YoutubeDl.Engine.Discovery;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.YouTube;

internal static class YouTubeSearchApi
{
    internal static async Task<IReadOnlyList<NativePlaylistEntry>> ReadAsync(YouTubeSearchAddress address,NativeDownloadRequest request,NativeOAuthSession session,CancellationToken ct)
    {
        if(address.Parameters is not null)throw new NotSupportedException("Opaque Web search filters cannot be applied to the authorized YouTube Data API search.");var entries=new List<NativePlaylistEntry>();var ids=new HashSet<string>(StringComparer.Ordinal);var tokens=new HashSet<string>(StringComparer.Ordinal);string? token=null;
        do
        {
            ct.ThrowIfCancellationRequested();var url=new Uri("https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&maxResults=50&q="+Uri.EscapeDataString(address.Query)+(token is null?"":"&pageToken="+Uri.EscapeDataString(token)));var document=await session.GetAsync(url,ct).ConfigureAwait(false);using var data=JsonDocument.Parse(document.Text);if(!data.RootElement.TryGetProperty("items",out var rows)||rows.ValueKind!=JsonValueKind.Array||data.RootElement.TryGetProperty("error",out _))throw new InvalidDataException("The authorized search returned no explicit video inventory.");var added=0;
            foreach(var row in rows.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();var identity=Object(row,"id");var id=Text(identity,"videoId");if(Text(identity,"kind")!="youtube#video"||id is null||!Regex.IsMatch(id,@"\A[A-Za-z0-9_-]{11}\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))throw new InvalidDataException("The authorized video search returned a different resource type or invalid identity.");if(!ids.Add(id))continue;if(entries.Count>=request.MaximumPlaylistEntries)throw new InvalidDataException("Search exceeded the configured item limit; it was not truncated.");var snippet=Object(row,"snippet");entries.Add(new(entries.Count+1,id,Text(snippet,"title")??id,new("https://www.youtube.com/watch?v="+id),null,Text(Object(Object(snippet,"thumbnails"),"high"),"url"),true,ExpectedArchiveIdentity:new("youtube",id),CollectionMemberId:id));added++;
            }
            token=Text(data.RootElement,"nextPageToken");if(token is not null&&(token.Length is 0 or >2048||added==0||entries.Count>=request.MaximumPlaylistEntries||!tokens.Add(token)||tokens.Count>1000))throw new InvalidDataException("Authorized search continuation repeated, exceeded its bound or made no progress.");
        }while(token is not null);
        return entries;
    }
}
