using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.YouTube;

internal sealed record YouTubeSearchAddress(string Query,string? Parameters)
{
    internal Uri Url=>new("https://www.youtube.com/results?search_query="+Uri.EscapeDataString(Query)+(Parameters is null?"":"&sp="+Uri.EscapeDataString(Parameters)));
    internal static bool TryParse(Uri url,out YouTubeSearchAddress? address)
    {
        address=null;if(!YouTubeAddress.IsYouTubeHost(url)||url.Host is not ("youtube.com" or "www.youtube.com" or "m.youtube.com")||url.AbsolutePath.TrimEnd('/')!="/results")return false;
        if(url.Fragment.Length>0)throw new ArgumentException("A search address cannot contain a fragment.");var fields=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var field in url.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries)){var parts=field.Split('=',2);var key=Uri.UnescapeDataString(parts[0].Replace('+',' '));var value=Uri.UnescapeDataString((parts.Length==2?parts[1]:"").Replace('+',' '));if(key is not ("search_query" or "sp")||!fields.TryAdd(key,value))throw new ArgumentException("Unsupported or repeated YouTube search parameter.");}
        if(!fields.TryGetValue("search_query",out var query)||string.IsNullOrWhiteSpace(query)||query.Length>2048||query.Any(char.IsControl))throw new ArgumentException("Provide a bounded nonempty YouTube search query.");var parameters=fields.GetValueOrDefault("sp");if(parameters is not null&&(parameters.Length is 0 or >2048||!Regex.IsMatch(parameters,@"\A[A-Za-z0-9_+/=-]+\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))))throw new ArgumentException("Invalid YouTube search filter.");address=new(query,parameters);return true;
    }
}
internal static class YouTubeSearchParser
{
    internal static YouTubeChannelBatch Page(string json,int maximum,CancellationToken ct)
    {
        using var document=JsonDocument.Parse(json);var primary=Object(Object(Object(document.RootElement,"contents"),"twoColumnSearchResultsRenderer"),"primaryContents");var list=Object(primary,"sectionListRenderer");if(list.ValueKind==JsonValueKind.Undefined||!list.TryGetProperty("contents",out var items)||items.ValueKind!=JsonValueKind.Array)throw new NotSupportedException("YouTube did not expose a supported primary search inventory.");return Items(items.EnumerateArray().ToArray(),Text(list,"targetId")??"search-feed",maximum,ct);
    }
    internal static YouTubeChannelBatch Continuation(string json,string target,int maximum,CancellationToken ct)
    {
        using var document=JsonDocument.Parse(json);var matches=new List<JsonElement>();foreach(var name in new[]{"onResponseReceivedCommands","onResponseReceivedActions","onResponseReceivedEndpoints"})foreach(var action in Array(document.RootElement,name)){var append=Object(action,"appendContinuationItemsAction");if(Text(append,"targetId")==target)matches.Add(append);}if(matches.Count!=1||!matches[0].TryGetProperty("continuationItems",out var items)||items.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Search continuation did not target its primary feed exactly once.");return Items(items.EnumerateArray().ToArray(),target,maximum,ct);
    }
    private static YouTubeChannelBatch Items(IReadOnlyList<JsonElement> sections,string target,int maximum,CancellationToken ct)
    {
        if(sections.Count>10000)throw new InvalidDataException("Search section inventory is excessive.");var items=new List<JsonElement>();foreach(var section in sections){ct.ThrowIfCancellationRequested();var wrapper=Object(section,"itemSectionRenderer");if(wrapper.ValueKind!=JsonValueKind.Undefined){if(!wrapper.TryGetProperty("contents",out var contents)||contents.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Search section has no explicit inventory.");foreach(var item in contents.EnumerateArray())Add(item);}else Add(section);}return YouTubeChannelParser.ParseItems(items,target,maximum,ct);
        void Add(JsonElement item)
        {
            ct.ThrowIfCancellationRequested();var content=Object(Object(item,"richItemRenderer"),"content");if(content.ValueKind==JsonValueKind.Undefined)content=item;
            if(Object(content,"videoRenderer").ValueKind!=JsonValueKind.Undefined||Object(content,"continuationItemRenderer").ValueKind!=JsonValueKind.Undefined){items.Add(content);return;}
            var modern=Object(content,"lockupViewModel");if(modern.ValueKind!=JsonValueKind.Undefined){if(Text(modern,"contentType")=="LOCKUP_CONTENT_TYPE_VIDEO")items.Add(content);else if(Text(modern,"contentType") is not ("LOCKUP_CONTENT_TYPE_CHANNEL" or "LOCKUP_CONTENT_TYPE_PLAYLIST"))throw new NotSupportedException("Unsupported primary search lockup type.");return;}
            if(Object(content,"messageRenderer").ValueKind!=JsonValueKind.Undefined)throw new NotSupportedException("YouTube returned a search availability message.");
            if(new[]{"channelRenderer","playlistRenderer","radioRenderer","shelfRenderer","reelShelfRenderer","horizontalCardListRenderer","adSlotRenderer","searchPyvRenderer","promotedSparklesWebRenderer","searchHeaderRenderer","didYouMeanRenderer","showingResultsForRenderer","backgroundPromoRenderer"}.Any(name=>Object(content,name).ValueKind!=JsonValueKind.Undefined))return;
            throw new NotSupportedException("Unsupported primary search item; no partial inventory was returned.");
        }
    }
}
public sealed partial class YouTubeProvider
{
    private static async Task<PageMetadata> ExtractSearchAsync(YouTubeSearchAddress address,NativeDownloadRequest request,MediaHttp http,CancellationToken ct)
    {
        if(request.MaximumPlaylistEntries is <1 or >10000)throw new ArgumentOutOfRangeException(nameof(request.MaximumPlaylistEntries));if(request.Authentication.OAuth is { } credentials){var oauth=http.SessionState("oauth:youtube",()=>new YoutubeDl.Engine.Authentication.NativeOAuthSession(http,credentials));return SearchInventory(address,await YouTubeSearchApi.ReadAsync(address,request,oauth,ct).ConfigureAwait(false));}var account=http.SessionState("youtube:account",()=>new YouTubeAccountSession(http,request.Authentication.YouTubeAccountIndex));var url=account.Select(address.Url);var document=await account.ReadPageAsync(url,ct).ConfigureAwait(false);if(!YouTubeSearchAddress.TryParse(YouTubeAccountSession.WithoutSelection(document.Url),out var final)||final!=address)throw new InvalidDataException("YouTube redirected to a different search context.");account.Observe(document.Text,document.Url,ct);
        var version=account.WebVersion;if(version.Length==0)version=Regex.Match(document.Text,@"""INNERTUBE_CLIENT_VERSION""\s*:\s*""(?<v>[^""]+)""",RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1)).Groups["v"].Value;
        var batch=YouTubeSearchParser.Page(YouTubePageJson.ReadInitialData(document.Text),request.MaximumPlaylistEntries,ct);var entries=new List<NativePlaylistEntry>();var ids=new HashSet<string>(StringComparer.Ordinal);var tokens=new HashSet<string>(StringComparer.Ordinal);
        for(var page=0;;page++)
        {
            ct.ThrowIfCancellationRequested();var added=0;foreach(var entry in batch.Entries){if(entry.Id is { } id&&!ids.Add(id))continue;if(entries.Count>=request.MaximumPlaylistEntries)throw new InvalidDataException("Search exceeds the configured item limit; it was not truncated.");added++;entries.Add(entry with{Position=entries.Count+1,ExpectedArchiveIdentity=entry.Id is null?null:new("youtube",entry.Id),CollectionMemberId=entry.Id});}
            if(batch.Continuation is not { } token)break;if(added==0||entries.Count>=request.MaximumPlaylistEntries||!tokens.Add(token)||page>=1000)throw new InvalidDataException("Search pagination exceeded its bound, repeated or made no progress.");if(version.Length is 0 or >128)throw new InvalidDataException("Search pagination requires a declared Web client version.");var json=await account.PostAsync("search",new{context=new{client=new{clientName="WEB",clientVersion=version}},continuation=token},"1",version,8*1024*1024,ct).ConfigureAwait(false);batch=YouTubeSearchParser.Continuation(json,batch.Target!,request.MaximumPlaylistEntries,ct);
        }
        return SearchInventory(address,entries);
    }
    private static PageMetadata SearchInventory(YouTubeSearchAddress address,IReadOnlyList<NativePlaylistEntry> entries)
    {
        var identity="search:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address.Url.Query))).ToLowerInvariant();return new(){Extractor="youtube",Id=identity,PlaylistId=identity,Title="YouTube search: "+address.Query,IsPlaylist=true,Entries=entries,SourcePageUrl=address.Url,RevalidateCollectionMembership=true};
    }
}
