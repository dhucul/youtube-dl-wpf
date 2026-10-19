namespace YoutubeDl.Engine.Providers.Clyp;

internal sealed record ClypAddress(string Id,bool Widget=false,string? Format=null,bool DownloadGateway=false)
{
    internal static bool IsHost(string host)=>host=="clyp.it"||host.EndsWith(".clyp.it",StringComparison.OrdinalIgnoreCase);
    internal static bool Matches(Uri url){try{Parse(url);return true;}catch(ArgumentException){return false;}}
    internal static ClypAddress Parse(Uri url)
    {
        if(!url.IsAbsoluteUri||url.Scheme is not("http" or "https")||!url.IsDefaultPort||url.UserInfo.Length!=0||url.Fragment.Length!=0||url.Host is not("clyp.it" or "www.clyp.it" or "api.clyp.it" or "a.clyp.it"))
            throw new ArgumentException("Use a public Clyp recording, widget or supplied audio address.");
        var parts=url.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
        var widget=parts.Length==2&&parts[1]=="widget";string? format=null;var id=parts[0];
        if(id.EndsWith(".mp3",StringComparison.Ordinal)||id.EndsWith(".ogg",StringComparison.Ordinal)){format=id[^3..];id=id[..^4];}
        if(parts.Length is not(1 or 2)||parts.Length==2&&!widget||widget&&format is not null||!IdValid(id)||url.Host is "api.clyp.it" or "a.clyp.it"&&widget||url.Host=="a.clyp.it"&&format is null)
            throw new ArgumentException("This Clyp recording or collection route needs additional native support.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in url.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries))
        {
            var pair=item.Split('=',2);var key=Uri.UnescapeDataString(pair[0]);var value=pair.Length==2?Uri.UnescapeDataString(pair[1]):"";
            if(!seen.Add(key)||!(key.StartsWith("utm_",StringComparison.Ordinal)||widget&&key=="autoplay"&&value is "true" or "false" or "0" or "1"))
                throw new ArgumentException("This Clyp private access or player selector needs additional native support.");
        }
        return new(id,widget,format,format is not null&&url.Host!="a.clyp.it");
    }
    internal static bool IdValid(string id)=>id.Length==8&&id.All(character=>char.IsAsciiDigit(character)||character is >= 'a' and <= 'z');
    internal Uri Web=>new("https://clyp.it/"+Id);
    internal Uri Player=>new("https://clyp.it/"+Id+"/widget");
    internal Uri Api=>new("https://api.clyp.it/"+Id);
}
