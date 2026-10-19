using System.Text.Json;
using System.Text.RegularExpressions;
namespace YoutubeDl.Engine.Discovery;
internal static class BoundedPageState
{
    internal static JsonDocument Read(string html,string name,string? scriptId=null,bool base64=false,bool windowObject=false)
    {
        if(html.Length>5*1024*1024)throw new InvalidDataException("The consumer page exceeds its limit.");var values=new List<string>();
        foreach(Match script in Regex.Matches(html,@"<script\b(?<attributes>[^>]{0,8192})>(?<body>[\s\S]*?)</script\s*>",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1)))
        {
            if(scriptId is not null&&!Regex.IsMatch(script.Groups["attributes"].Value,"\\bid\\s*=\\s*['\"]"+Regex.Escape(scriptId)+"['\"]",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))continue;
            var body=script.Groups["body"].Value;var pattern=base64?"\\A\\s*window\\."+Regex.Escape(name)+"\\s*=\\s*\"(?<value>[A-Za-z0-9+/=]{1,6000000})\"\\s*;?\\s*\\z":"\\A\\s*(?:const|let|var)\\s+"+Regex.Escape(name)+"\\s*=\\s*";
            if(windowObject)pattern="\\A\\s*window\\."+Regex.Escape(name)+"\\s*=\\s*";
            var match=Regex.Match(body,pattern,RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));if(!match.Success)continue;
            if(base64){try{values.Add(new System.Text.UTF8Encoding(false,true).GetString(Convert.FromBase64String(match.Groups["value"].Value)));}catch(Exception e)when(e is FormatException or System.Text.DecoderFallbackException){throw new InvalidDataException("The consumer state encoding is invalid.");}}
            else values.Add(Object(body,match.Length));
        }
        if(values.Count!=1||values[0].Length>4*1024*1024)throw new InvalidDataException("The page did not expose one bounded consumer state.");var document=JsonDocument.Parse(values[0],new(){MaxDepth=64});try{var fields=0;Validate(document.RootElement,ref fields);return document;}catch{document.Dispose();throw;}
    }
    private static string Object(string value,int start)
    {
        if(start>=value.Length||value[start]!='{')throw new InvalidDataException("The consumer state requires literal JSON.");var quoted=false;var escape=false;var depth=0;
        for(var i=start;i<value.Length;i++){var c=value[i];if(quoted){if(escape)escape=false;else if(c=='\\')escape=true;else if(c=='"')quoted=false;}else if(c=='"')quoted=true;else if(c=='{')depth++;else if(c=='}'&&--depth==0)return value[start..(i+1)];}throw new InvalidDataException("The consumer state is incomplete.");
    }
    private static void Validate(JsonElement value,ref int fields)
    {if(value.ValueKind==JsonValueKind.Object){var names=new HashSet<string>(StringComparer.Ordinal);foreach(var p in value.EnumerateObject()){if(++fields>100000||!names.Add(p.Name))throw new InvalidDataException("Consumer state fields are excessive or duplicated.");Validate(p.Value,ref fields);}}else if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())Validate(item,ref fields);}
}
