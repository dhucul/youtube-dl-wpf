using System.Globalization;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Rtve;

internal sealed record RtveAddress(string Kind, string? Id, string? Slug)
{
    internal bool Collection => Kind == "program";
    internal Uri Canonical => Id is not null ? new("https://www.rtve.es/" + (Collection ? "api/programas/" + Id + ".json" : "a/" + Id + "/")) : new("https://www.rtve.es/play/audios/" + Slug + "/");
    internal static bool Host(Uri url) => url.IsAbsoluteUri && url.Host is "www.rtve.es" or "rtve.es" or "api.rtve.es" or "rneaudio.rtve.es";
    internal static RtveAddress Parse(Uri url)
    {
        if (!Host(url) || url.Scheme is not ("https" or "http") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0)
            throw new ArgumentException("Use a canonical RTVE audio or radio programme address.");
        var path = url.AbsolutePath;
        var match = Match(path, @"\A/(?:a|audio)/(?<id>[1-9][0-9]{0,11})/?\z");
        if (url.Host == "rneaudio.rtve.es")
        {
            if (match.Success) return new("audio", match.Groups["id"].Value, null);
            match = Match(path, @"\A/pr/(?<id>[1-9][0-9]{0,11})/?\z");
            if (match.Success) return new("program", match.Groups["id"].Value, null);
            throw new NotSupportedException("This RNE Audio share address is not supported yet.");
        }
        if (match.Success) return new("audio", match.Groups["id"].Value, null);
        match = Match(path, @"\A/api/(?<kind>audios|programas)/(?<id>[1-9][0-9]{0,11})(?:\.json)?/?\z");
        if (match.Success) return new(match.Groups["kind"].Value == "audios" ? "audio" : "program", match.Groups["id"].Value, null);
        match = Match(path, @"\A/(?:play|alacarta)/audios/(?<slug>[A-Za-z0-9_-]+)(?:/[A-Za-z0-9_-]+){0,3}/(?<id>[1-9][0-9]{0,11})/?\z");
        if (match.Success) return new("audio", match.Groups["id"].Value, match.Groups["slug"].Value);
        match = Match(path, @"\A/(?:play|alacarta)/audios/(?<slug>[A-Za-z0-9_-]+)/?\z");
        if (match.Success) return new("program", null, match.Groups["slug"].Value);
        throw new NotSupportedException("Native RTVE currently supports recorded public audio and radio programme inventories. Video, live and television routes remain pending.");
    }
    internal static string Numeric(string? value) => value is not null && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 && value.Length <= 12 ? value : throw new InvalidDataException("RTVE omitted a positive resource identity.");
    internal static Match Match(string value, string pattern) => Regex.Match(value, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
}
