using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Omny;

public sealed partial class OmnyProvider
{
    private static async Task<PageMetadata> CollectionAsync(OmnyAddress address,NativeDownloadRequest request,MediaHttp http,CancellationToken ct)
    {
        JsonDocument? playlistDoc=null;JsonDocument? programDoc=null;
        try
        {
            string alias,org,program;JsonElement playlist=default;
            if(address.Ids)
            {
                playlistDoc=await JsonAsync(new(Api+"orgs/"+address.Organization+"/playlists/"+address.Playlist),http,ct).ConfigureAwait(false);playlist=playlistDoc.RootElement;
                if(Id(playlist)!=address.Playlist||Id(playlist,"OrganizationId")!=address.Organization)throw new InvalidDataException("Omny returned another requested playlist ID.");
                alias=Text(playlist,"ProgramSlug")??throw new InvalidDataException("Omny omitted a playlist's program slug.");org=Id(playlist,"OrganizationId");program=Id(playlist,"ProgramId");
                programDoc=await JsonAsync(new(Api+"programs/"+Uri.EscapeDataString(alias)),http,ct).ConfigureAwait(false);ValidateProgram(programDoc.RootElement,alias,org,program);
            }
            else
            {
                alias=address.Program!;programDoc=await JsonAsync(new(Api+"programs/"+alias),http,ct).ConfigureAwait(false);ValidateProgram(programDoc.RootElement,alias);
                org=Id(programDoc.RootElement,"OrganizationId");program=Id(programDoc.RootElement);
                if(address.Playlist is {} slug){playlistDoc=await JsonAsync(new(Api+"programs/"+alias+"/playlists/"+slug),http,ct).ConfigureAwait(false);playlist=playlistDoc.RootElement;}
            }
            var isPlaylist=playlistDoc is not null;string? playlistId=null;int? declared=null;
            if(isPlaylist){ValidatePlaylist(playlist,alias,address.Ids?null:address.Playlist,org,program);playlistId=Id(playlist);declared=Integer(playlist,"NumberOfClips")??throw new InvalidDataException("Omny omitted its public playlist count.");}
            if(declared>request.MaximumPlaylistEntries)throw new NotSupportedException("The complete Omny playlist exceeds the configured inventory limit.");
            var entries=new List<NativePlaylistEntry>();var seen=new HashSet<string>(StringComparer.Ordinal);var cursors=new HashSet<string>(StringComparer.Ordinal);string? cursor=null;string? lastId=null;string? firstFingerprint=null;
            for(var number=0;;number++)
            {
                ct.ThrowIfCancellationRequested();if(number>=200)throw new NotSupportedException("The complete Omny inventory exceeds its page bound.");
                var endpoint=Inventory(org,program,playlistId,cursor,lastId);
                using var response=await JsonAsync(endpoint,http,ct).ConfigureAwait(false);var root=response.RootElement;var clips=Clips(root);
                if(number==0)firstFingerprint=Fingerprint(clips);
                bool more;
                if(isPlaylist)
                {
                    more=Flag(root,"NextClipsAvailable");var previous=Flag(root,"PreviousClipsAvailable");
                    if(number==0&&previous||number>0&&!previous)throw new InvalidDataException("Omny returned conflicting playlist navigation state.");
                }
                else
                {
                    var total=Integer(root,"TotalCount")??throw new InvalidDataException("Omny omitted the public program count.");
                    if(declared is not null&&declared!=total)throw new InvalidDataException("Omny changed the public program count during pagination.");declared=total;
                    if(total>request.MaximumPlaylistEntries)throw new NotSupportedException("The complete Omny program exceeds the configured inventory limit.");
                    if(!root.TryGetProperty("Cursor",out var next))throw new InvalidDataException("Omny omitted its terminal cursor state.");
                    cursor=next.ValueKind==JsonValueKind.Null?null:next.ValueKind==JsonValueKind.String?next.GetString():throw new InvalidDataException("Omny returned an invalid program continuation.");
                    more=!string.IsNullOrEmpty(cursor);if(more&&(cursor!.Length>4096||cursor.Any(char.IsControl)||!cursors.Add(cursor)))throw new InvalidDataException("Omny repeated or invalidated its program continuation.");
                }
                if(clips.Length>100||clips.Length==0&&more)throw new InvalidDataException("Omny returned an oversized or empty continuing page.");
                foreach(var clip in clips)
                {
                    var id=Id(clip);if(Id(clip,"OrganizationId")!=org||(!isPlaylist&&Id(clip,"ProgramId")!=program)||isPlaylist&&!PlaylistIds(clip).Contains(playlistId!,StringComparer.Ordinal))
                        throw new InvalidDataException("Omny returned a clip outside the requested program or playlist.");
                    if(!seen.Add(id))throw new InvalidDataException("Omny repeated a collection clip.");
                    if(entries.Count>=request.MaximumPlaylistEntries||declared is not null&&entries.Count>=declared)throw new InvalidDataException("Omny exceeded its declared complete inventory.");
                    var available=Text(clip,"PublishState")=="Published"&&Text(clip,"State")=="Ready"&&Text(clip,"Visibility") is "Public" or "Unlisted";
                    if(!isPlaylist&&Text(clip,"Visibility")!="Public")throw new InvalidDataException("Omny included an unlisted clip in the public program inventory.");
                    entries.Add(new(entries.Count+1,id,Text(clip,"Title")??id,available?OmnyAddress.ClipId(org,id):null,Number(clip,"DurationSeconds"),Image(Text(clip,"ImageUrl")),available,ExpectedArchiveIdentity:new("omnyfm",id)));
                    lastId=id;
                }
                if(!more){if(entries.Count!=declared)throw new InvalidDataException("Omny ended before the complete declared inventory.");break;}
                if(entries.Count>=declared)throw new InvalidDataException("Omny advertised continuation after its complete count.");
            }
            using var first=await JsonAsync(Inventory(org,program,playlistId,null,null),http,ct).ConfigureAwait(false);
            if(Fingerprint(Clips(first.RootElement))!=firstFingerprint)throw new InvalidDataException("Omny changed its first inventory page during inspection.");
            var metadata=isPlaylist?playlist:programDoc.RootElement;
            using var owner=await JsonAsync(isPlaylist?new Uri(Api+"orgs/"+org+"/playlists/"+playlistId):new Uri(Api+"programs/"+alias),http,ct).ConfigureAwait(false);
            if(isPlaylist){ValidatePlaylist(owner.RootElement,alias,null,org,program);if(Id(owner.RootElement)!=playlistId||Integer(owner.RootElement,"NumberOfClips")!=declared)throw new InvalidDataException("Omny changed its playlist membership count.");}
            else ValidateProgram(owner.RootElement,alias,org,program);
            if(Text(owner.RootElement,"ModifiedAtUtc")!=Text(metadata,"ModifiedAtUtc"))throw new InvalidDataException("Omny changed collection metadata during inspection.");
            return new(){Id=isPlaylist?playlistId:program,PlaylistId=isPlaylist?playlistId:program,IsPlaylist=true,Extractor=isPlaylist?"omnyfm:playlist":"omnyfm:show",
                Title=Text(metadata,isPlaylist?"Title":"Name")??(isPlaylist?playlistId:program)!,Description=Text(metadata,"Description"),Uploader=Text(programDoc.RootElement,"Author")??Text(programDoc.RootElement,"Name"),
                ThumbnailUrl=Image(Text(metadata,"ArtworkUrl")),Entries=entries.AsReadOnly(),AnonymousPlayback=true,SourcePageUrl=new("https://omny.fm/shows/"+alias+(isPlaylist?"/playlists/"+Text(metadata,"Slug"):""))};
        }
        finally{playlistDoc?.Dispose();programDoc?.Dispose();}
    }
    private static Uri Inventory(string org,string program,string? playlist,string? cursor,string? last)=>new(Api+"orgs/"+org+(playlist is null?"/programs/"+program+"/clips?pageSize=100"+(cursor is null?"":"&cursor="+Uri.EscapeDataString(cursor)):
        "/playlists/"+playlist+"/clips/v2?pageSize=100"+(last is null?"":"&clipId="+last+"&direction=AfterExclusive")));
    private static JsonElement[] Clips(JsonElement root)=>root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("Clips",out var clips)&&clips.ValueKind==JsonValueKind.Array?clips.EnumerateArray().ToArray():throw new InvalidDataException("Omny omitted a public clip page.");
    private static string Fingerprint(IEnumerable<JsonElement> clips)=>JsonSerializer.Serialize(clips.Select(clip=>new{Id=Id(clip),Program=Id(clip,"ProgramId"),Org=Id(clip,"OrganizationId"),State=Text(clip,"PublishState"),Visibility=Text(clip,"Visibility"),Modified=Text(clip,"ModifiedAtUtc")}));
}
