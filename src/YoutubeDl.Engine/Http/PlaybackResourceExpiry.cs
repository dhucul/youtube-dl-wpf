using System.Globalization;
namespace YoutubeDl.Engine.Http;
/// <summary>Reads publisher expiry hints; the issuer/CDN still validates every signature.</summary>
internal static class PlaybackResourceExpiry
{
    internal static DateTimeOffset? Read(Uri url)
    {
        DateTimeOffset? expiry=null;var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var part in url.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries))
        {
            var pair=part.Split('=',2);var name=Uri.UnescapeDataString(pair[0]);if(pair.Length!=2||name is not ("expire" or "expires" or "__hdnea__" or "hdnea"))continue;
            if(!seen.Add(name))throw new InvalidDataException("Playback expiry fields are ambiguous.");var value=Uri.UnescapeDataString(pair[1]);
            if(name is "__hdnea__" or "hdnea"){var fields=value.Split('~').Select(f=>f.Split('=',2)).Where(f=>f.Length==2&&f[0]=="exp").ToArray();if(fields.Length!=1)throw new InvalidDataException("The publisher expiry is missing or ambiguous.");value=fields[0][1];}
            if(!long.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var seconds))throw new InvalidDataException("Playback expiry is invalid.");
            DateTimeOffset current;try{current=DateTimeOffset.FromUnixTimeSeconds(seconds);}catch(ArgumentOutOfRangeException){throw new InvalidDataException("Playback expiry is outside its supported range.");}expiry=expiry is null||current<expiry?current:expiry;
        }
        return expiry;
    }
    internal static void RequireFresh(Uri url){if(Read(url) is { } expiry&&expiry<=DateTimeOffset.UtcNow.AddSeconds(5))throw new ExpiredPlaybackResourcesException();}
}
internal sealed class ExpiredPlaybackResourcesException:IOException{internal ExpiredPlaybackResourcesException():base("The publisher returned expired playback resource links."){} }
