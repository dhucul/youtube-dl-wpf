using System.Globalization;
using System.Text.Json;
namespace YoutubeDl.Engine.Providers;
internal static class HostingCollectionPaging
{
    internal static int? Count(JsonElement root)=>root.TryGetProperty("count",out _)?ProviderJson.Integer(root,"count")??throw new InvalidDataException("The declared collection count is invalid."):null;
    internal static Dictionary<string,string> Query(Uri url)
    {
        if(url.Query.Length>16384)throw new InvalidDataException("The collection query exceeds its limit.");
        var values=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var part in url.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries))
        {var pair=part.Split('=',2);if(!values.TryAdd(Uri.UnescapeDataString(pair[0]),Uri.UnescapeDataString(pair.Length==2?pair[1]:"")))throw new InvalidDataException("Collection parameters are duplicated.");}
        return values;
    }
    internal static int Number(Dictionary<string,string> values,string key,int fallback)
        =>values.TryGetValue(key,out var value)?int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var result)?result:throw new InvalidDataException("A collection position is invalid."):fallback;
    internal static Uri Next(Uri root,string raw,int offset,bool jw)
    {
        if(!Uri.TryCreate(root,raw,out var next)||next.Scheme!="https"||next.UserInfo.Length>0||!next.IsDefaultPort||next.Host!=root.Host||next.AbsolutePath!=root.AbsolutePath||next.Fragment.Length>0)throw new InvalidDataException("A collection continuation changed its issuer or resource.");
        var original=Query(root);var values=Query(next);var key=jw?"page_offset":"offset";var limit=jw?"page_limit":"limit";
        if(Number(values,key,-1)!=offset||Number(values,limit,100) is <1 or >1000)throw new InvalidDataException("A collection continuation skipped source positions.");
        foreach(var pair in original)if(pair.Key!=key&&pair.Key is not ("token" or "exp" or "sig")&&(!values.TryGetValue(pair.Key,out var value)||value!=pair.Value))throw new InvalidDataException("Collection filters changed during paging.");
        foreach(var pair in values)if(!original.ContainsKey(pair.Key)&&pair.Key!=key&&pair.Key!=limit&&pair.Key is not ("format" or "internal" or "token"))throw new InvalidDataException("The continuation added another collection filter.");
        if(values.TryGetValue("format",out var format)&&format!="json"||values.TryGetValue("internal",out var internalValue)&&internalValue!="false")throw new InvalidDataException("The continuation changed the public collection representation.");
        return next;
    }
    internal static void Add(List<NativePlaylistEntry> entries,HashSet<string> ids,IReadOnlyList<NativePlaylistEntry> page,int maximum)
    {foreach(var entry in page){if(entry.Id is null)throw new InvalidDataException("A collection member omitted its media identity.");if(entries.Count>=maximum)throw new InvalidDataException("The complete collection exceeds its configured entry limit.");entries.Add(entry with {Position=entries.Count+1});}}
}
