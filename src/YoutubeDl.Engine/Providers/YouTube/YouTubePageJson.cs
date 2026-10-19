using System.Text.Json;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.YouTube;

/// <summary>Reads JSON literals embedded in a page without executing page JavaScript.</summary>
public static class YouTubePageJson
{
    public static string ReadPlayerResponse(string html) => ReadAssignedObject(html, "ytInitialPlayerResponse", required: true)!;
    public static string ReadInitialData(string html) => ReadAssignedObject(html, "ytInitialData", required: true)!;
    public static string? TryReadInitialData(string html) => ReadAssignedObject(html, "ytInitialData", required: false);

    private static string? ReadAssignedObject(string html, string name, bool required)
    {
        var escaped = Regex.Escape(name);
        var assignment = Regex.Match(html,
            @"(?:\b" + escaped + @"\b|window\s*\[\s*[""']" + escaped + @"[""']\s*\])\s*=\s*(?<object>\{)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!assignment.Success)
        {
            if (!required) return null;
            throw new InvalidDataException($"The YouTube page did not include {name}. It may require consent, authentication, or a newer player integration.");
        }
        var start = assignment.Groups["object"].Index;
        return ReadObject(html,start);
    }
    internal static string ReadObject(string html,int start)
    {
        var quoted = false;
        var escapedCharacter = false;
        var depth = 0;
        for (var index = start; index < html.Length; index++)
        {
            if (index - start > 2 * 1024 * 1024) throw new InvalidDataException("The YouTube response exceeds the extraction limit.");
            var character = html[index];
            if (quoted)
            {
                if (escapedCharacter) escapedCharacter = false;
                else if (character == '\\') escapedCharacter = true;
                else if (character == '"') quoted = false;
                continue;
            }
            if (character == '"') quoted = true;
            else if (character == '{') depth++;
            else if (character == '}' && --depth == 0) return html[start..(index + 1)];
        }
        throw new InvalidDataException("The YouTube page contained an incomplete JSON response.");
    }
    internal static JsonElement ReadConfiguration(string html)
    {
        var fields=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        var count=0;
        foreach(Match match in Regex.Matches(html,@"\bytcfg\.set\s*\(\s*(?<object>\{)",RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1)))
        {
            if(++count>64||fields.Count>2048)throw new InvalidDataException("The YouTube configuration exceeds its limit.");
            var source=ReadObject(html,match.Groups["object"].Index);
            try{using var document=JsonDocument.Parse(source);foreach(var property in document.RootElement.EnumerateObject())fields[property.Name]=property.Value.Clone();}
            catch(JsonException){/* Page bootstrap expressions are not JSON; never evaluate them. */}
        }
        return JsonSerializer.SerializeToElement(fields);
    }

    public static Uri? ReadPlayerScriptUrl(string html)
    {
        var match = Regex.Match(html, @"""(?:jsUrl|PLAYER_JS_URL)""\s*:\s*(?<value>""(?:\\.|[^""\\])*"")",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success) return null;
        var value = JsonSerializer.Deserialize<string>(match.Groups["value"].Value);
        if (!Uri.TryCreate(new Uri("https://www.youtube.com/"), value, out var url) ||
            !IsPlayerScriptUrl(url))
            return null;
        return url;
    }

    internal static bool IsPlayerScriptUrl(Uri url) => url.IsAbsoluteUri && YouTubeAddress.IsYouTubeHost(url) && url.Scheme == "https" &&
        url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 &&
        url.AbsolutePath.StartsWith("/s/player/", StringComparison.Ordinal) && url.AbsolutePath.EndsWith(".js", StringComparison.Ordinal);
}
