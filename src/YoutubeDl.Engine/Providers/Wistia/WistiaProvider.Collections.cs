using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;
namespace YoutubeDl.Engine.Providers.Wistia;
public sealed partial class WistiaProvider
{
    internal static bool IsChannel(Uri url)=>IsHost(url)&&Regex.IsMatch(url.AbsolutePath,@"\A/(?:embed/channel|channel|channels)/[a-z0-9]{10}(?:\.json)?/?\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private static async Task<PageMetadata> ReadChannelAsync(NativeDownloadRequest request,MediaHttp http,CancellationToken ct)
    {
        var id=request.Url.AbsolutePath.TrimEnd('/').Split('/')[^1].Replace(".json","",StringComparison.Ordinal);var query=HostingCollectionPaging.Query(request.Url);
        if(request.Authentication.IsConfigured)throw new NotSupportedException("Exact Wistia media credentials do not grant a channel inventory.");
        if(query.Keys.Any(k=>k is not ("mode" or "width" or "height" or "responsive" or "maxwidth" or "maxheight")))throw new NotSupportedException("This Wistia channel navigation/access parameter is unsupported.");
        if(query.TryGetValue("mode",out var mode)&&mode is not ("playlist" or "inline" or "popover"))throw new NotSupportedException("This Wistia channel mode is unsupported.");
        var url=new Uri("https://fast.wistia.com/embed/channel/"+id+".json");var document=await http.GetDocumentAsync(url,8*1024*1024,ct).ConfigureAwait(false);
        if(document.Url!=url)throw new InvalidDataException("Wistia changed its channel inventory resource.");return ParseChannel(document.Text,id,request.MaximumPlaylistEntries);
    }
    internal static PageMetadata ParseChannel(string json,string expected,int maximum)
    {
        if(maximum is <1 or >10000||Encoding.UTF8.GetByteCount(json)>8*1024*1024)throw new InvalidDataException("The Wistia channel exceeds its limits.");
        using var document=JsonDocument.Parse(json);var root=document.RootElement;var series=Array(root,"series");
        if(Text(root,"hashedId")!=expected||Text(root,"source")!="channel"||True(root,"hasMoreSections")||Integer(root,"seriesCount")!=series.Length)throw new InvalidDataException("Wistia omitted a complete matching channel inventory.");
        var entries=new List<NativePlaylistEntry>();var episodeIds=new HashSet<string>(StringComparer.Ordinal);var sections=new HashSet<string>(StringComparer.Ordinal);
        foreach(var group in series)
        {
        if(!group.TryGetProperty("sections",out var groups)||groups.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Wistia omitted an explicit channel section inventory.");
        foreach(var section in groups.EnumerateArray())
        {
            var sectionId=Long(section,"numericId")?.ToString(System.Globalization.CultureInfo.InvariantCulture);var episodes=Array(section,"episodes");
            if(sectionId is null||!sections.Add(sectionId)||Integer(section,"episodeCount")!=episodes.Length||True(section,"hasMoreEpisodes"))throw new InvalidDataException("Wistia returned an incomplete or repeated channel section.");
            foreach(var episode in episodes)
            {
                var id=Text(episode,"episodeMediaHashedId");var member=Text(episode,"channelEpisodeId")??Text(episode,"id");if(id is null||!Regex.IsMatch(id,@"\A[a-z0-9]{10}\z")||Text(episode,"hashedId")!=id||member is null||member.Length is <1 or >200||member.Any(char.IsControl)||!episodeIds.Add(member))throw new InvalidDataException("Wistia channel member/media identities are invalid or repeated.");
                if(Text(episode,"type") is not ("Video" or "Audio")||entries.Count>=maximum)throw new InvalidDataException("The complete channel exceeds its supported entry contract.");
                if(!episode.TryGetProperty("requiresRegistration",out var registration)||registration.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new InvalidDataException("Wistia omitted the episode access state.");
                var available=!registration.GetBoolean();var title=Text(episode,"episodeTitle")??Text(episode,"name")??id;
                entries.Add(new(entries.Count+1,id,title,new("https://fast.wistia.com/embed/iframe/"+id),Number(episode,"durationInSeconds"),Text(episode,"thumbnailUrl"),available,ExpectedArchiveIdentity:new("wistia",id),CollectionMemberId:"wistia:episode:"+member));
            }
        }
        }
        return new(){Id=expected,PlaylistId=expected,Extractor="wistia:channel",IsPlaylist=true,Title=Text(Object(root,"embedOptions"),"title")??expected,Entries=entries,RevalidateCollectionMembership=true};
    }
}
