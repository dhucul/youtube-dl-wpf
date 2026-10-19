using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Authentication;
using YoutubeDl.Engine.Discovery;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.YouTube;

internal static class YouTubeOAuthInventory
{
    internal static async Task<PageMetadata> ReadAsync(string id,NativeDownloadRequest request,NativeOAuthSession session,CancellationToken ct)
    {
        if(request.MaximumPlaylistEntries is <1 or >10000)throw new ArgumentOutOfRangeException(nameof(request.MaximumPlaylistEntries));
        var document=await session.GetAsync(new("https://www.googleapis.com/youtube/v3/playlists?part=snippet,contentDetails&id="+Uri.EscapeDataString(id)),ct).ConfigureAwait(false);
        using var metadata=JsonDocument.Parse(document.Text);var playlists=Array(metadata.RootElement,"items");if(playlists.Length!=1||Text(playlists[0],"id")!=id)throw new InvalidDataException("The authorized API did not return the selected playlist.");
        var count=Long(Object(playlists[0],"contentDetails"),"itemCount");if(count is null||count>request.MaximumPlaylistEntries)throw new InvalidDataException("The account playlist has an invalid or excessive item count.");
        var title=Text(Object(playlists[0],"snippet"),"title")??id;var entries=new List<NativePlaylistEntry>();var tokens=new HashSet<string>(StringComparer.Ordinal);string? token=null;
        do
        {
            document=await session.GetAsync(new("https://www.googleapis.com/youtube/v3/playlistItems?part=snippet,contentDetails,status&maxResults=50&playlistId="+Uri.EscapeDataString(id)+(token is null?"":"&pageToken="+Uri.EscapeDataString(token))),ct).ConfigureAwait(false);
            using var data=JsonDocument.Parse(document.Text);var items=Array(data.RootElement,"items");if(items.Length==0&&Text(data.RootElement,"nextPageToken") is not null)throw new InvalidDataException("The account playlist continuation made no progress.");
            foreach(var item in items)
            {
                if(entries.Count>=request.MaximumPlaylistEntries||entries.Count>=count)throw new InvalidDataException("The account playlist exceeded its declared inventory.");var snippet=Object(item,"snippet");
                if(Text(snippet,"playlistId")!=id||Long(snippet,"position")!=entries.Count)throw new InvalidDataException("The account playlist changed membership or source order.");
                var video=Text(Object(item,"contentDetails"),"videoId")??Text(Object(snippet,"resourceId"),"videoId");if(video is not null&&!Regex.IsMatch(video,@"\A[A-Za-z0-9_-]{11}\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))throw new InvalidDataException("The account playlist returned an invalid video identifier.");
                entries.Add(new(entries.Count+1,video,Text(snippet,"title")??"Unavailable video",video is null?null:new("https://www.youtube.com/watch?v="+video),null,null,video is not null));
            }
            token=Text(data.RootElement,"nextPageToken");if(token is not null&&(token.Length is 0 or >2048||!tokens.Add(token)||tokens.Count>1000))throw new InvalidDataException("The account playlist repeated an invalid continuation.");
        }while(token is not null);
        if(entries.Count!=count)throw new InvalidDataException("The account playlist returned an incomplete inventory.");
        return new(){Id=id,PlaylistId=id,IsPlaylist=true,Extractor="youtube",Title=title,Entries=entries,SourcePageUrl=request.Url};
    }
}
